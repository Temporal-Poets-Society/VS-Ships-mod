"""
Carrack design for Hansa (about 1.25x a real 15th-16th century carrack so the decks are livable), shaped after the
Santa Maria and the Vila do Conde nau: a round-bellied hull with a flat transom stern, a tall sterncastle, and a
pointed forecastle that overhangs the stem.

  Hull 31 blocks at the main deck (bowsprit to ~24), beam 9.2, draft 2.5.
  Levels (floor tops, above the waterline at y = 0):
    hold          -2.0           an empty cargo deck over the ballast, the keelson down its middle, down a ladder
                                 from the hatch in the lower
                                 deck; its cargo is still one big storage behind that hatch. Dry: the mod tells the
                                 Sea mod the inside of the hull is (no water, fog or breath bar below the waterline)
    lower deck     0.5           crew quarters, the cable tier, the tiller overhead at the aft end
    main deck      3.5           open waist between the castles: capstan, pump, the glazed hatch, the companionway
                                 down to the lower deck, the ship's boat
    great cabin    3.5           under the quarterdeck: the captain's cabin, the stern gallery
    forecastle     3.5           the room under the forecastle deck: the hearth, the riding bitts
    quarterdeck    6.5           the wheel
    poop cabin     6.5           under the poop deck
    forecastle     6.5           deck, the heads at its tip
    poop deck      9.5
  Three masts: fore (one square sail), main (course and topsail, the only top), mizzen (lateen, stepped on the keel),
  plus a bowsprit.

The hull is oak: tarred outside, caulked decks, plain oak inside. Everything that can be a vanilla block is one
(doors, glass panes, lanterns, chests, beds, the firepit, the captain's table): the design only says where they go.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from planking import BANDS, flat_between, raised_strip, reveal, skin, sweep_cap, sweep_patch, transom  # noqa: E402
from shipgen import Design, Material, interp  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "assets", "hansa")
DOCS = os.path.join(ROOT, "docs")

# ---------------------------------------------------------------- which carrack
# 3 (the carrack since 0.3.4): the proposal of 2026-09-30 (docs/proportions.png, tools/shipgen/proportions.py) with
# the waist 2 longer: she floats 1.5 deeper, is wider and fuller amidships (a flat floor a third of the beam), the
# quarterdeck comes forward to just abaft the mainmast so the main shrouds come down on its side, the poop is longer and
# overhangs the transom, the transom rakes more, the forecastle is 1 longer aft and 1.25 further out with a raked
# underside, the castle breaks sweep more gently. The user tried it beside 1 (as built up to 0.3.2) and 2 (3 with the
# 13-block waist) and chose it (2026-09-30: "better for place management and later cannon placement"). 1 and 2 can
# still be built to compare (as carrack-v1, carrack-v2; not shipped):
#   python carrack.py --variant=1        (or HANSA_CARRACK=1)
VARIANT = int(next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--variant=")),
                   os.environ.get("HANSA_CARRACK", "3")))
PROPOSAL = VARIANT >= 2
CODE = "carrack" if VARIANT == 3 else f"carrack-v{VARIANT}"
# (0.3.9, the ninth client test: hull R on one block grid, docs/views/hull/hull-proposal.md §8) the carrack's hull is the
# user's sketch R as one smooth spline section (widest low, a strong round tumblehome, a flat floor 5.4 wide, the hold 7
# across), 2 blocks longer in the waist, every deck and every main fitting on one global block grid: x borders at whole
# x, z borders at +-0.5, +-1.5 ..., floors on block borders G (design y = G + GRID_OFF; her waterline at G -0.125, so in
# calm water every floor is level with a world block's top). Storeys of 3: 2 blocks of room and a 1-block band of the
# deck's structure (planks, beams every 2 on even x, open bays between them) under the deck above.
GRID = VARIANT == 3
# (0.3.13, the user's notes 138 and 142) where the companion is: "A" a block inboard (its door, stairwell, stairs, the
# quarterdeck stair and the plaque), "B" (shipped) where 0.3.12 had it, the side by its door given room
COMPANION_OPTION = os.environ.get("HANSA_COMPANION", "B").upper()
# (0.3.16, notes 154-158) "carrack-m", "Carrack (modular)": the same carrack, but her cabin fronts toward the waist (their
# windows and doors), the stairwell's walls, the castle and companion stairs and every railing facing inside are the
# mod's placeable pieces, laid by default (docs/modular-pieces.md): `modularPieces`, `wells`, rooms with "pieces". Built
# on option A's companion (a block in from the leaning side, where a placed door has its whole place):
#   HANSA_MODULAR=1 python carrack.py
MODULAR = VARIANT == 3 and os.environ.get("HANSA_MODULAR", "0") == "1"
if MODULAR:
    COMPANION_OPTION = "A"
# (0.3.14) option A is built as a ship of its own, "carrack-a", so the user can launch both and compare them in the game
if VARIANT == 3 and COMPANION_OPTION == "A":
    CODE = "carrack-a"
if MODULAR:
    CODE = "carrack-m"
# (Hansa, 16 October 2026) the Hansa carrack: carrack-m rebuilt to stand on the block grid, block by block
# (docs/carrack-m-inventory.md), under a code of her own so carrack-m's files stay as they are:
#   HANSA_MODULAR=1 HANSA_SHIP=hansa-carrack python carrack.py
HANSA = MODULAR and bool(os.environ.get("HANSA_SHIP"))
# (Hansa) a quick build to look at her (HANSA_FAST=1): the guns' arcs not swept against her (97% of a build's time: each
# gun trains +-10 degrees), the rest as a full build. Not for shipping
FAST = os.environ.get("HANSA_FAST") == "1"
if HANSA:
    CODE = os.environ["HANSA_SHIP"]
GRID_OFF = 1.5                                     # design y = G + GRID_OFF (the decks keep their design heights)

B = 1.0 if GRID else (5.25 if PROPOSAL else 4.6)   # half beam at the widest (GRID: SECTION is the half width itself)
# the outside of the bottom planking at the centreline (the hull starts here), and the keel's underside. (GRID, 0.3.10,
# the user's note 106: the bottom 0.5 thick in all, keel + floors + planking: the hold's floor at G -4 (its planks
# 0.25, the bottom's outside right under them at G -4.25), the keel 0.25 under that, its underside at G -4.5 (game
# y -4.375, draft 4.375: 0.6 under her in 5 blocks of water); 0.3.9's bottom was at G -4.65, the keel's at G -5)
KEEL = -2.75 if GRID else -2.5
KEEL_BOTTOM = -3.0 if GRID else KEEL - 0.5
if HANSA:
    # (Hansa, decision 14, the user's correction of 16 October 2026: "there is enough space in bottom part to make ribs and
    # hull") her bottom one whole block, G -5..-4: its planking at the bottom, the floor timbers over it, the hold's floor
    # on top (G -4, as carrack-m's); the keel flush in it (its underside on the bottom's outside, a hair proud). Her draft
    # 4.875 (carrack-m's 4.375); her decks where they were
    KEEL = KEEL_BOTTOM = -3.5
# The waist was lengthened by this much (the user, 2026-09-29): everything forward of the parallel midbody (the bow, the
# forecastle, the foremast, the bowsprit) moved forward by it; the frames go on every FRAME_SPACE the whole length.
# (0.3.9: 2 more, one more 2-block bay in the waist: the entry gates get a bay of their own)
STRETCH = {1: 2.0, 2: 2.0, 3: 6.0}[VARIANT]
MIDBODY_FWD = 3.0 + STRETCH                        # the hull starts to narrow toward the bow here (and aft of -6.0)
LOWER, MAIN, QUARTER, POOP = 0.5, 3.5, 6.5, 9.5   # floor tops (GRID: G -1, 2, 5, 8)
# The design is drawn with the first carrack's waterline at 0; a deeper one floats with the sea this far up it, and is
# lowered by it when written (Design.translate_y), so in the game her waterline is at 0 like every ship's.
# (GRID: 1.375, her waterline at G -0.125: vanilla's still water is 0.875 up a block, so her floors lie on block borders)
WATER = 1.375 if GRID else (1.5 if PROPOSAL else 0.0)
# the cargo deck over the ballast, 2.25 blocks under the lower deck (GRID: G -4, 3 under it: a full storey)
HOLD_FLOOR = -2.5 if GRID else -2.0
# the keelson runs along the middle of it; 2 blocks clear over it (GRID: sunk flush with it, the floor all level)
KEELSON_TOP = HOLD_FLOOR if GRID else HOLD_FLOOR + 0.25
FRAME_SPACE = 2.0 if GRID else 1.5                 # the frames (ribs), one under every deck beam
# deck beams (0.25 wide) start here and every FRAME_SPACE (GRID: centred on the even block borders)
BEAM_X0 = -16.125 if GRID else -13.0
COPPER_TOP = WATER + 0.9                           # copper sheathing (and the bottom's own tar) up to the strake below this
# the wales' lower edges amidships (they sweep up toward the ends); a deeper ship's two close together under the waist.
# (GRID, the user's note 67: they follow the planking's strakes, each one strake of it, the nearest to these heights)
WALES = (WATER + 1.1, WATER + 1.65) if PROPOSAL else (1.15, 2.5)
DECK_T = 0.25
HULL_T = 0.3       # the inside planking sits this far inboard of the outside (GRID: from the main deck up; thicker below)
ZW = 6.5 if GRID else (5.75 if PROPOSAL else 5.0)     # the voxel grid's half width
BEAM_D = 0.5 if GRID else 0.25                     # the deck beams' depth under the planks (GRID: the band's)
HOLD_BEAM_D = 0.75 if GRID else 0.14               # the lower deck's beams in the hold: the band's full depth (GRID)
HOLD_CARLINGS = (-3.0, -1.5, 1.5, 3.0)             # (GRID) the carlings between the hold's beams, their middles' z
# (0.3.26, step D1: docs/step-d-plan.md, docs/slots.md §9) carrack-m's hull planking as slots: panels of a frame space
# (strip to strip: each frame keeps a strip of planking STRIP_HALF either side of its middle) by a band of strakes. The
# bands' seams and the fixed strakes (at each deck's waterway, the wales, under each rail cap) are on quarter-block lines,
# so a panel's drawn edges and its collision voxels meet; below the main deck the strakes are spaced again to them
# (seamed_levels), the wales two strakes of 0.25 over each other (2.75..3.25, 0.3.25's 2.6..3.2)
HULL_SLOTS = MODULAR
STRIP_HALF = 0.25
HULL_BANDS = [(KEEL, -1.75), (-1.75, -0.75), (-0.75, 0.25), (0.5, 1.5), (1.5, 2.75), (3.5, 4.5), (4.75, 6.0),
              (6.75, 7.5), (7.75, 8.75), (8.75, 9.25), (9.5, 10.5)]
# (the castles' waterway with the mouldings over it: the forecastle's at 6.0, the sterncastle's at 6.5, over the poop's
# overhang)
HULL_FIXED_BANDS = [(0.25, 0.5, "waterway"), (2.75, 3.5, "wales"), (4.5, 4.75, "cap"), (6.0, 6.75, "waterway"),
                    (7.5, 7.75, "cap"), (9.25, 9.5, "waterway"), (10.5, 10.75, "cap")]
HULL_WALES = ((2.75, 3.0), (3.0, 3.25))
if HANSA:
    # (Hansa, 16 October 2026: "make it fully stick to blocks") her hull's panels a block high, on the block rows (design y
    # G + 1.5, borders on k + 0.5) from her bottom up to the poop's rail: a panel is one block of her side. The bottom's
    # quarter under the hold floor goes with the hold's first row; the rail caps stay fixed strips on the rows' tops; the
    # waterways and the wales are in their decks' structural rows (the wales raised strips over the main deck's)
    HULL_BANDS = [(KEEL, -2.5), (-2.5, -1.5), (-1.5, -0.5), (-0.5, 0.5), (0.5, 1.5), (1.5, 2.5), (2.5, 3.5), (3.5, 4.5), (4.5, 5.5),
                  (5.5, 6.5), (6.5, 7.5), (7.5, 8.5), (8.5, 9.5), (9.5, 10.5)]
    HULL_FIXED_BANDS = [(4.5, 4.75, "cap"), (7.5, 7.75, "cap"), (10.5, 10.75, "cap")]
# (0.3.28, step D2.1, D2.2: the user's choices on 0.3.26) one-block panels and no strips: the frames stay 2 apart, each
# frame space two panels a block long (the frame's middle to the space's middle: "a", the space's middle to the next
# frame's: "f"), the planking over a frame split on its middle, each half its panel's. Two panels out side by side leave
# only the frame (the rib) between them: drawn in her side's thickness, FRAME_SIDED wide, while a panel beside it is out;
# its collision the voxel column aft of its line (as a deck beam's), the panels' the rest
HALF_PANELS = HULL_SLOTS

if GRID:
    # hull R (hull-proposal.md §8, the study's usersk.py): the midship section's outside through these points (z, G), one
    # smooth monotone spline (PCHIP): the flat floor 5.4 wide, a full round bilge, widest 6.08 at G 0.2 (just over the
    # waterline, at the lower deck), a strong round tumblehome over it: the ceiling 5.49 at the main deck's floor, 4.48
    # at G 4 (the main deck's 2-high furniture touches there), 3.5 at G 7 (the poop cabin's) and 3.0 at the poop's rail
    from scipy.interpolate import PchipInterpolator
    # (0.3.10, the user's note 106: the bottom 0.4 higher, G -4.25, under the hold's floor; the flat floor wider, 7.2,
    # and the turn of the bilge tighter, so the hold is still 7 across at its floor (the ceiling 3.6 out at G -4) and
    # wider than 0.3.9's just over it; unchanged from G -2 up. 0.3.9: (0, -4.65), (2.7, -4.65), (3.55, -4.5),
    # (4.17, -4.1), (4.68, -3.5), (5.15, -2.8))
    R_POINTS = [(0.0, -4.25), (3.6, -4.25), (4.3, -4.08), (4.72, -3.75), (5.02, -3.3), (5.28, -2.7), (5.55, -2.0),
                (5.86, -1.2), (6.04, -0.5), (6.08, 0.2), (6.0, 1.0), (5.8, 2.0), (5.33, 3.0), (4.81, 4.0), (4.41, 5.0),
                (4.1, 6.0), (3.81, 7.0), (3.6, 8.0), (3.44, 9.0), (3.3, 10.0)]
    BOTTOM_G = -4.25
    if HANSA:
        # (Hansa) her bottom a block under the hold's floor: the section under G -2 stretched down to G -5 (the bilge's
        # turn as carrack-m's, 0.75 deeper); unchanged from G -2 up
        def _deepen(g):
            return g if g >= -2.0 else -2.0 + (g + 2.0) * (3.0 / 2.25)
        R_POINTS = [(z, _deepen(g)) for z, g in R_POINTS]
        BOTTOM_G = -5.0
    # the shell's thickness normal to it (the outside planking, the frames in it, the ceiling) by the outside's height G:
    # (0.3.10) 0.25 at the bottom (the hold floor's planks are the floors' ceiling: the bottom's outside right under
    # them), 0.28 round the turn of the bilge, 0.4 up the side to the lower deck, 0.3 from the main deck up (constant:
    # the castles join). (0.3.9: 0.65 under the flat floor, 0.47 at the turn, 1.0 at the centreline with the keel)
    THICK = [(-4.25, 0.25), (-3.9, 0.28), (-3.3, 0.38), (-3.0, 0.42), (-1.0, 0.4), (0.5, 0.35), (2.0, 0.3), (12.0, 0.3)]
    if HANSA:
        THICK = [(_deepen(g), t) for g, t in THICK]

    def _section_curves(n=1200):
        pts = np.array(R_POINTS, float)
        s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(pts, axis=0).T))])
        fz, fg = PchipInterpolator(s, pts[:, 0]), PchipInterpolator(s, pts[:, 1])
        u = np.linspace(0, s[-1], n)
        z, g = fz(u), fg(u)
        dz, dg = fz(u, 1), fg(u, 1)
        ln = np.hypot(dz, dg)
        nz, ng = dg / ln, -dz / ln
        t = np.interp(g, *zip(*THICK))
        return z, g, z - nz * t, g - ng * t

    _SZ, _SG, _SZI, _SGI = _section_curves()

    def O_mid(G):
        """The outside's half width amidships at grid height G."""
        m = _SG > BOTTOM_G + 1e-6
        return np.interp(G, _SG[m], _SZ[m], left=0.0)

    def C_mid(G):
        """The ceiling's (the inside planking's) half width amidships at grid height G."""
        m = (_SGI > -4.0 + 1e-4) & (_SZI > 1.0)
        gi, zi = _SGI[m], _SZI[m]
        o = np.argsort(gi)
        return np.interp(G, gi[o], zi[o], left=float(zi[o][0]))

    def th_h(G):
        """The shell's thickness across (horizontally) amidships at grid height G."""
        return O_mid(G) - C_mid(G)

    SECTION = [(KEEL - 0.001, 0.0)] + [(float(g + GRID_OFF), float(max(O_mid(g), 0.0)))
                                        for g in np.arange(BOTTOM_G, 10.01, 0.05)]
    SECTION[1] = (SECTION[1][0], float(_SZ[_SG < BOTTOM_G + 1e-6].max()))     # the flat floor's half width
elif PROPOSAL:
    # a flat floor a third of the beam (Palacio, Cano, the Pepper Wreck), a firm round bilge, widest a little above the
    # waterline, a little tumblehome over it
    SECTION = [(-2.75, 0.02), (-2.5, 0.33), (-2.25, 0.44), (-2.0, 0.53), (-1.5, 0.65), (-1.0, 0.75), (-0.5, 0.83),
               (0.0, 0.89), (0.5, 0.93), (1.0, 0.96), (1.5, 0.985), (2.0, 1.0), (3.0, 1.0), (4.0, 0.97), (5.0, 0.93),
               (6.5, 0.88), (8.0, 0.85), (11.0, 0.81)]
else:
    SECTION = [(-2.75, 0.02), (-2.5, 0.10), (-2.0, 0.45), (-1.0, 0.80), (0.0, 0.95), (1.0, 1.0), (2.0, 0.99),
               (3.5, 0.94), (5.0, 0.88), (6.5, 0.84), (8.0, 0.81), (11.0, 0.77)]
BOW_X = [(y, x + STRETCH) for y, x in
         [(-2.75, 11.25), (-2.5, 11.5), (0.0, 13.8), (1.0, 14.3), (3.5, 15.2), (5.0, 15.7), (6.5, 16.3), (8.0, 16.6)]]
FOREFOOT_R = 2.0          # (GRID, 0.3.10) the forefoot's radius: the bottom curves up into the stem (the user's note 104)
if GRID:
    # the stem carried on down to the deeper bottom with the same rake. (0.3.10, the user's note 104: "keel is not
    # continuous and has sharp corner in bow") the forefoot rounded: the bottom curves up into the stem's line on an arc
    # of FOREFOOT_R, so the keel (keel_and_stem) runs on round it into the stem without a corner
    _stem = [(y, x) for y, x in BOW_X if y > -2.6]                      # from (-2.5, 11.5 + STRETCH) up
    (_y1, _x1), (_y2, _x2) = _stem[0], _stem[1]
    _k = (_x2 - _x1) / (_y2 - _y1)                                      # the stem's rake (dx/dy) under the waterline
    _cx = _x1 + _k * (KEEL - _y1)                                       # where its line meets the bottom
    _al = math.atan2(1.0, _k)                                           # the angle the forefoot turns through
    _t = FOREFOOT_R * math.tan(_al / 2)
    _arc = [(KEEL + FOREFOOT_R * (1 - math.cos(p)), _cx - _t + FOREFOOT_R * math.sin(p))
            for p in np.linspace(0.0, _al, 13)]
    BOW_X = [(float(y), float(x)) for y, x in _arc] + [(y, x) for y, x in _stem if y > _arc[-1][0] + 0.05]
if PROPOSAL:
    # the transom raked more above the water (about 15 degrees; the sternpost's 17 below it)
    STERN_X = [(-2.75, -12.25), (-2.5, -12.5), (0.0, -14.2), (1.5, -14.6), (3.5, -15.2), (6.5, -15.9), (11.0, -16.9)]
    if GRID:
        # (0.3.10: the bottom at -2.75, on the same line as 0.3.9's (-3.15, -11.9) to (-2.5, -12.5))
        STERN_X = [(KEEL, -11.9 - 0.6 * (KEEL + 3.15) / 0.65)] + [(y, x) for y, x in STERN_X if y > -2.6]
else:
    STERN_X = [(-2.75, -12.25), (-2.5, -12.5), (0.0, -14.2), (1.5, -14.6), (3.5, -15.0), (6.5, -15.3), (11.0, -15.6)]
if HANSA and os.environ.get("HANSA_FACET_PROFILE") == "1":
    # (Hansa: the faceted hull, hull_hw; off: rounding them to the half block left her transom in her lining's plane and
    # a gap at her stem) her stem and her sternpost straight between corners on the half-block grid:
    # their x at every half block of height, rounded to a half block (the forefoot's arc in facets)
    def _facet_profile(prof):
        lo, hi = min(y for y, _ in prof), max(y for y, _ in prof)
        ys = [lo] + [k * 0.5 for k in range(int(math.ceil(lo / 0.5 + 1e-9)), int(math.floor(hi / 0.5 - 1e-9)) + 1)] + [hi]
        ys = sorted(set(round(v, 6) for v in ys))
        return [(float(y_), float(round(float(interp(y_, prof)) * 2) / 2)) for y_ in ys]
    BOW_X = _facet_profile(BOW_X)
    STERN_X = _facet_profile(STERN_X)
# the stern's run and the transom: how much of the section's half beam the stern keeps where it's cut off flat (0 =
# closes to the post); both by height over the waterline, so a deeper ship's transom starts as far under her waterline
# (the gio, near the waterline) and her quarters there are as fine
STERN_P = [(y + WATER, v) for y, v in ([(-3.1, 2.0)] if GRID else []) + [(-2.5, 2.0), (0.0, 2.4), (1.5, 3.0), (3.0, 3.6),
                                                                        (11.0, 4.2)]]
TRANSOM_W = [(y + WATER, v) for y, v in [(-0.75, 0.0), (0.25, 0.42), (1.5, 0.62), (3.5, 0.72), (6.5, 0.76), (11.0, 0.78)]]
# (GRID) the bow's and the run's fullness by height G under the lower deck (finer toward the keel: the deeper bottom
# paid for); unchanged above: the bow's ellipse exponent (2 = as before), the run's exponent's factor
BOW_PG = [(-4.25, 1.15), (-2.0, 1.6), (-1.0, 2.0), (12.0, 2.0)]        # (0.3.9: from G -4.65)
RUN_SCALE = [(-4.25, 0.55), (-2.0, 0.8), (-1.0, 1.0), (12.0, 1.0)]
if HANSA:
    BOW_PG = [(-5.0, 1.15)] + BOW_PG[1:]
    RUN_SCALE = [(-5.0, 0.55)] + RUN_SCALE[1:]

if GRID:
    # the castle fronts' inside faces on block borders: the great cabin's at -3, the forecastle's at 15, the poop
    # cabin's at -9 (each wall 0.25 thick, its waist face given here)
    WAIST_AFT, WAIST_FWD = -2.75, 14.75
    POOP_FRONT = -8.75
elif PROPOSAL:
    # the quarterdeck to just abaft the mainmast (the tolda "from the mast aft"), the forecastle 1 longer aft
    WAIST_AFT, WAIST_FWD = -3.0, 8.0 + STRETCH
    POOP_FRONT = -8.5                  # (1 further forward than proposed: the user, 2026-09-30)
else:
    WAIST_AFT, WAIST_FWD = -6.0, 9.0 + STRETCH   # sterncastle front / forecastle back
    POOP_FRONT = -10.0
FC_Y0 = 6.0                           # underside of the forecastle's deck, where its sides step out over the bow inside
FC_X0, FC_TIP = 8.0 + STRETCH, (20.5 if PROPOSAL else 19.25) + STRETCH   # its sides leave the hull here, meet at the tip
# Outside, its underside where it overhangs the bow: level at FC_Y0, or (the proposal) meeting the stem at FC_LOW and
# rising straight to FC_Y0 at the tip, its sides stepping out from there
FC_LOW = 4.75 if PROPOSAL else FC_Y0
BREAK_L = 2.75 if PROPOSAL else 1.75  # how far the sheer sweeps out from each castle front (the castle breaks)
# (0.3.24, notes 184, 185: "move floor forward to ribs so have proper roof beam. Can curve side more rapid"; "move floor
# to ship center to match with rib and have proper ceiling beam at edge") carrack-m: the quarterdeck's and the forecastle
# deck's edges a block out over the waist from their fronts' lines (-3, 15), on the frames there (-2, 14), each edge on
# an ordinary beam on that frame (castle_beams); the cabin fronts stay on their lines under the decks (a block of deck
# over the waist before each: the stairs come up to the edges, the breastworks stand on them). The castle breaks' sweeps
# start at the new edges and end where they did (0 and 12): 1.75 long, not 2.75. (The poop's front, -9, unchanged.)
# (0.3.24, user layout) the forecastle stair's line: the user's at z 0.5..1.5 (beside the door), but up to the new edge at
# 14 the cell before its foot (11..12) is in the capstan's ring (its bars sweep to 11.6 there): a block outboard, z 1.5..2.5
# (as 0.3.23's), a cell between it and the door
FC_STAIR_Z = 2.0
EDGE_OUT = 1.0 if VARIANT == 3 and os.environ.get("HANSA_MODULAR", "0") == "1" else 0.0
QD_EDGE = WAIST_AFT - 0.25 + EDGE_OUT        # the quarterdeck's edge over the waist (carrack-m -2; else its front, -3)
FC_EDGE = WAIST_FWD + 0.25 - EDGE_OUT        # the forecastle deck's (carrack-m 14; else 15)
BREAK_L_FRONT = BREAK_L - EDGE_OUT           # the quarterdeck's and the forecastle's breaks (the poop's: BREAK_L)
# (0.3.28, notes 225, 227: "you did not move the default partition wall position with the rib and beam here and at bow";
# "this partition's default position is with the upper floor's last rib") carrack-m: the great cabin's and the forecastle's
# fronts (their placed partitions, doors, windows; the rooms) on the castle decks' edges, the frames and edge beams at -2
# and 14 (the beam over each front's line, the pieces under it), not a block in (-3, 15) as since 0.3.24
FRONT_AFT = QD_EDGE if EDGE_OUT else WAIST_AFT - 0.25
FRONT_FWD = FC_EDGE if EDGE_OUT else WAIST_FWD + 0.25
# The poop cabin carried out over the transom on the quarterdeck's beams (a cantilever): its sides carry the hull's
# lines on aft by this much from OH_Y (under the quarterdeck's beams) up
POOP_OVERHANG = 1.25 if PROPOSAL else 0.0
OH_Y = QUARTER - DECK_T               # the overhang's underside: the quarterdeck's own (the great cabin's walls stay whole)

# (0.3.8, the agreed redesign) the waist's bulwark 0.25 higher: its rail cap at MAIN + 1.25, over the gun ports' lintels
# (the castle breaks' sweeps rise from it); the first carracks' at MAIN + 1.0
WAIST_RAIL = 1.25 if VARIANT == 3 else 1.0
# (0.3.23, note 171) carrack-m: the castles' bulwarks (the quarterdeck's, the poop's, the forecastle's) 0.25 higher, their
# rail caps from 1.0 to 1.25 over their decks as the waist's: a lidded gun port framed as the waist's (sill 0.25, lintel
# 1.0, the lid's hinge under the cap) fits under them, for the castle guns. Everything set on those rails (the caps, the
# taffrail, the shroud rails, the braces' and the lateen sheet's ends, the bowsprit's saddle on the stem head) goes up
# with them; the castle breaks sweep up 0.25 further
CASTLE_UP = 0.25 if VARIANT == 3 and os.environ.get("HANSA_MODULAR", "0") == "1" else 0.0
# entry ports (hinged sections of the bulwark) in the waist, both sides, filling the space between two frames, the gates
# hinged at their forward edge so they swing clear of the gallows' posts. (0.3.8, the user's note 50: "I think gates
# maybe could be placed here") the bay aft of the boat gallows' after post, the gates hung on it; forward of the gallows
# before, where a gun is now
PORT_X = (0.75, 2.0) if VARIANT == 3 else (5.25, 6.5)
# (0.3.8, the user's note 48: "its rotation point on open is in centre and not on inside like should be") each gate
# turns about its forward edge's inboard corner, hung on the frame there, and opens until it lies against that frame's
# after face (a little short of square, so they don't lie in one plane)
GATE_OPEN = 88.0
# (0.3.9, GRID) the gates in a bay of their own, 10..12 (the waist 2 longer), hung on frame 10 at their after edge, each
# opening inboard to lie along that frame's forward face; the opening between the frames' faces
GATE_HINGE_AFT = GRID
if GRID:
    # (0.3.10, the waist re-planned, docs/views/waist/waist-plan.md, the user's note 95: "gates to the middle of the
    # waist, swapped with a gun") bay 6..8, hung on frame 6
    PORT_X = (6.125, 7.875)
if HANSA:
    # (Hansa, the user on 16 October 2026: "gates on main deck and ladder to ship looks wide ... not 1 block width i would
    # expect") each entry gate one block, 6..7, hung on frame 6 as before (the rest of the bay plain bulwark); its leaf,
    # its rope ladder, its cut and its room follow
    PORT_X = (6.1875, 6.875)          # (a sixteenth clear of the gallows' post on rib 6, so the rail cap meets it)
# The waist guns (item 16, gun.py), each firing through a port cut in the waist's bulwark between two frames, square
# out of her side. (0.3.8, the agreed redesign) five a side, every bay of the waist from the boat gallows' after post to
# the forecastle break's sweep (three before, either side of the entry ports, which are now aft of the gallows)
# (GRID: one per 2-block frame bay, each port centred on an odd x between the frames on the even ones)
GUN_X = (1.0, 3.0, 5.0, 9.0, 11.0) if GRID else (2.875, 4.375, 5.875, 7.375, 8.875) if VARIANT == 3 else ()
# the ports: square, from 0.25 over the deck to 1.0, the bulwark whole over them (a lintel under the rail, since 0.3.8:
# they were notches up through the rail and its cap), each shut by a lid hinged along its top outside (LIDDED), the
# lid opened outward and up by LID_OPEN degrees while the gun is run out (the mod's "open:<gun>")
GUNPORT_W, GUNPORT_Y = 0.75, (0.25, 1.0)
if HANSA:
    # (Hansa) each port in one block of her side (a frame space's after half, its after jamb on the frame), so it cuts one
    # panel: x 0.5, 2.5, 4.5, 8.5, 10.5 (carrack-m's 1, 3, 5, 9, 11 on the frame spaces' middles, across two)
    GUN_X = (0.5, 2.5, 4.5, 8.5, 10.5)
GUNPORTS = [(x - GUNPORT_W / 2, x + GUNPORT_W / 2) for x in GUN_X]
LIDDED = VARIANT == 3
LID_OPEN = 110.0
LID_T = 0.08                                     # the lid's thickness, outside the planking
GUN_ELEVATION = 1.0                              # degrees: how a new ship's guns are laid
# Laying them (0.3.6). Run out, the carriage's fore end close to the side (its trunnions GUN_INSET in from the inside
# planking, the muzzle 0.8 out of her side). It trains about an upright in the middle of the side's thickness, up to
# GUN_TRAIN either way unless it would foul its neighbour when both are run back, the boat gallows' posts, or an entry
# port's gate swinging (then less that way: gun_train_limits). It's elevated from 5° down (the muzzle, its swell 0.28
# across and 1.25 out from the trunnions, clears the port's sill coming back in through it) to 15° up (under the port's
# lintel and its lid opened up: fit_checks).
GUN_INSET = 0.50
GUN_TRAIN = 12.0
# (0.3.28, note 226: "these and other guns still don't have full right left arc to swivel") carrack-m's guns train up to
# 25° either way from square (12 before; 0.3.27 centred the bow guns' 12 on her side's normal there, so the forecastle's
# forward gun trained only 4.5..15.25° toward the bow): every gun as far as its room, its neighbours, the gates and the
# gallows allow, each way, the shorter way first (castle_gun_limits)
if MODULAR:
    GUN_TRAIN = 25.0
# (0.3.29, the user: "Bow cannons still can't swivel all the way to the sides") the castle guns (the quarterdeck's, the
# poop's, the forecastle's, the stern chasers) as far as their barrels clear their ports' cheeks (port_jamb_limits), at
# most this, then what their neighbours, their rooms and their breeching allow (gun_train_limits, castle_gun_limits)
GUN_TRAIN_CASTLE = 45.0 if MODULAR else GUN_TRAIN
BOW_PAIR_AFT = 9.0      # (0.3.29) the forecastle's after gun toward her stern at least this (the forward one's toward the bow first)
# (0.3.8: 12° up, 15 before: under the lidded ports' lintels, MAIN + 1.0, the muzzle's swell clears it all the way back
# as it runs in; through the old notch open to the rail it could go to 15)
GUN_ELEVATION_LIMITS = (-5.0, 12.0) if VARIANT == 3 else (-5.0, 15.0)
GUN_RATES = (10.0, 6.0)                          # degrees a second it's trained and elevated by its crew
# (0.3.21, step C, docs/slots.md §7) carrack-m's castle guns: gun places she's launched without (armed later with the gun
# item), each a whole gun as the waist's (its port framed and lidded, its moving parts, its own train limits): (name, x,
# the deck it stands on, its port's sill and lintel over that deck, the inside's paint zone for its lid). Two a side in
# the great cabin, on the main deck in the sterncastle: the waist's ports in its sides, its walls whole over them, each
# centred between two frames, aft of the companion's room (starboard, -7..-3) and forward of the tiller ropes' run down
# at -14.1. (Not on the quarterdeck, the poop deck or the forecastle deck: their bulwarks are 1.0 high with the rail cap
# from 0.75, so a port under the cap is 0.5 high and its lid's hinge only 0.2 over the barrel's axis: the lid opened
# fouls the barrel run out, laid anyhow (-0.02, fit_checks); a lintel as the waist's needs those bulwarks 0.25 higher.
# Nor in the forecastle room: the two bower cables from the hawse holes to the bitts and aft to the capstan and the
# hearth with the oven take its room.)
CASTLE_GUNS = [("cabin-1", -11.0, MAIN, GUNPORT_Y, "great-cabin-walls"),
               ("cabin-2", -9.0, MAIN, GUNPORT_Y, "great-cabin-walls")] if MODULAR else []
# (0.3.23, note 171: "no guns in the cabins": the user hadn't asked for them, and there's no seeing out to lay them) none in
# the great cabin; the castle guns on the castles' open decks instead, under their bulwarks raised 0.25 (CASTLE_UP) for
# ports framed and lidded as the waist's (sill 0.25, lintel 1.0 over the deck, the lid hinged under the rail cap), each
# centred between two frames (on an odd x), (name, x, deck, port's sill and lintel over the deck, the lid's inside zone,
# the sides it's on): the quarterdeck's at -7 (port only: the poop stair, z 1.5..2.5 from -9 to -6, is in the starboard
# one's room) and -5 (both, aft of the binnacle and outboard of the helm), the poop's at -15 and -11 (both, either side of
# the mizzen's shroud rails) and the forecastle's at 19 (starboard only: the forecastle deck is 5.2 across inside there,
# and two guns abreast would share the middle of it run back; aft of the catheads' heels, forward of the fore shrouds'
# rails and the hearth's flue): four a side. Not at -9 / -3 / 15 (the castle fronts' lines), -13 and 17 (the mizzen's and
# the fore shrouds' rails inside the bulwark over the port) or 21 (the catheads through the bulwark there). Each trains as
# far as its room stays clear (castle_gun_limits)
CASTLE_GUNS = [("quarter-1", -7.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1,)),
               ("quarter-2", -5.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-1", -15.0, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-2", -11.0, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("forecastle-1", 19.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (1,))] if MODULAR else []
# (0.3.24, notes 179-183: "gun ports should be symmetric on both sides. Conflict with stairs is thing to resolve by
# checking can gun be placed or stairs"; "can we fit here one more gun port on both sides? if needed bit move up ratline
# connections"; "bow gun ports here under ratlines and move bit up ratlines and get rid of chimney") every castle gun on
# both sides (an empty place doesn't keep its room: a stair or piece in it is the player's choice, the mod refuses one or
# the other), and more: the poop's at -13 under the mizzen's shroud rails and at -17 by the transom, the forecastle's at
# 17 under the fore shroud rails (those rails a little higher, SHROUD_RAIL_UP: over the ports' lintels), its hearth's
# flue gone (HEARTH_FLUE). Ids kept for the old places; the new ones numbered on. (The poop's -13 port 0.5 aft of the bay's
# middle, against frame -14's face: its trail clears the mizzen; the one by the transom 0.25 forward of its bay's middle,
# at -16.75: its room clear of the transom's inside)
CASTLE_GUNS = [("quarter-1", -7.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("quarter-2", -5.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-1", -15.0, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-2", -11.0, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-3", -13.5, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("poop-4", -16.75, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("forecastle-1", 19.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1)),
               ("forecastle-2", 17.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1))] if MODULAR else []
# (0.3.27, note 203: "here gun ports are placed illogical - different distances, and most aft ports we don't need; max 3
# guns per side is ok") the poop's three a side, one in each bay between its frames (-16, -14, -12, -10: ports at -15, -13,
# -11, each centred in its bay, 2 apart), the one by the transom (poop-4, -16.75) gone; poop-3 from -13.5 to -13 (the guns
# run back 0.75 now, not 1.1: its trail clear of the mizzen there). Ids kept (a gun mounted on poop-4 on a ship saved
# before goes into her hold)
CASTLE_GUNS = [g for g in CASTLE_GUNS if g[0] != "poop-4"]
CASTLE_GUNS = [(n, -13.0, *rest) if n == "poop-3" else (n, x, *rest) for n, x, *rest in CASTLE_GUNS]
# (0.3.28, note 224: "place default ports also here", the quarterdeck's bay -4..-2 by its edge) a third a side on the
# quarterdeck, at -3 (the front's line moved to the edge at -2, note 225: the bay's whole bulwark the quarterdeck's).
# (Note 223: "these ports by default are not needed", the poop's: PORT_COVERS)
if MODULAR:
    CASTLE_GUNS.insert(2, ("quarter-3", -3.0, QUARTER, GUNPORT_Y, "castle-bulwarks", (-1, 1)))
# (0.3.30, note 236: "i told several time to remove there last side ports"; notes 203, 223 before) the poop's side ports
# gone: her poop's bulwark plain both sides, no port, no lid, no gun place (the stern chasers through the transom stay). A
# gun saved on a poop place goes into her hold (RescueOrphanedGuns)
if MODULAR:
    CASTLE_GUNS = [g for g in CASTLE_GUNS if not g[0].startswith("poop-")]
if HANSA:
    # (Hansa) the castle ports each in one block, as the waist's: half a block aft (-7.5, -5.5, -3.5; 18.5, 16.5), a
    # sixteenth forward of that (the castles' frames stand in her side's thickness: a jamb on the frame's face would lie
    # in it)
    # (the quarterdeck's aftmost, quarter-1, gone: on a block it falls under the poop break's sweep, whose planking takes
    # no port; two a side on the quarterdeck. The user: guns may change "in positions and maybe quantity")
    CASTLE_GUNS = [(n, x - 0.4375, *rest) for n, x, *rest in CASTLE_GUNS if n != "quarter-1"]
SHROUD_RAIL_UP = 0.08 if MODULAR else 0.0      # (its top 0.02 under the frames' heads; its foot 0.02 over the ports' lintels)
CASTLE_PORTS = [(x - GUNPORT_W / 2, x + GUNPORT_W / 2, deck + py[0], deck + py[1], s)
                for _, x, deck, py, _, sides in CASTLE_GUNS for s in sides]
# (0.3.25, note 179: "can we fit here gun ports on both sides", marked on the transom from the poop deck; the user, asked:
# "at back yes back facing ports") carrack-m: two stern chasers on the poop deck, firing aft through lidded ports cut in
# the transom either side of the centre (STERN_GUN_Z out from it), framed and lidded as the castles' side ports (sill 0.25,
# lintel 1.0 over the deck, the lid hinged along its top under the taffrail, opening out and up). Empty places as the
# other castle guns. (name, z out from the centreline, the deck, the port's sill and lintel over it, the lid's inside
# zone, the sides.) Their rooms share the poop's after end with the aftmost broadside guns (poop-4, poop-1): the mod lets
# only one of a pair that would stand in each other's room be mounted (docs/slots.md §7). The nameboard between the ports.
STERN_GUN_Z = 1.875             # (Hansa: its port, z 1.5..2.25, is within one block already)
STERN_GUNS = [("stern", STERN_GUN_Z, POOP, GUNPORT_Y, "castle-bulwarks", (-1, 1))] if MODULAR else []
STERN_PORTS = [(s * z - GUNPORT_W / 2, s * z + GUNPORT_W / 2, deck + py[0], deck + py[1], s)
               for _, z, deck, py, _, sides in STERN_GUNS for s in sides]

# ---------------------------------------------------------------- where the fittings go (x forward, z starboard)
# The mainmast stands a little aft of the middle of the keel and of the hull's centre of buoyancy, like the wrecks of
# the period (the Pepper Wreck's mast step is just aft of its midship frame), between two deck beams; the foremast over
# the forward end of the keel, between two of the forecastle's beams.
MAIN_X, FORE_X = -0.125, 11.375 + STRETCH
BOW_CAPSTAN = VARIANT == 3
if BOW_CAPSTAN:
    # in the bow (the user, 2026-10-01: "should be in bow"): on the main deck just in front of the forecastle's
    # bulkhead, off the centreline to port, the anchor's side. Its bars sweep clear of the pump, the forecastle's door and
    # stair, the forward guns however they're laid and run back, the gates and the hatch (capstan_clearance); the bower
    # cable comes to it straight aft from the port hawse through the bitts and the bulkhead
    # (0.3.8: 0.15 forward and 1.25 inboard, to clear the fifth gun a side housed, run back, and its crew walking the
    # bars; it was at WAIST_FWD - 1.75, -2.0)
    CAPSTAN_X, CAPSTAN_Z = WAIST_FWD - 1.6, -0.75
elif PROPOSAL:
    # (the 13-block waist of the second carrack has no room for it there: aft of the mainmast, port side, as in 0.3.4)
    CAPSTAN_X, CAPSTAN_Z = -1.25, -2.2
else:
    CAPSTAN_X, CAPSTAN_Z = WAIST_FWD - 2.0, -1.5   # in front of the forecastle, port side, clear of the forecastle door
CAPSTAN_BARS = 1.1
if VARIANT == 3:
    CAPSTAN_BARS = 0.95             # (0.3.13, note 130: a bit shorter, its crew walking them clear of the hatch's coaming)
CABIN_STAIR = VARIANT == 3
if CABIN_STAIR:
    # (0.3.8, the agreed redesign; the user's note 46: "I am thinking of placing companion way here") the companionway
    # is a stair in the great cabin, down aft along its port side from just inside its front wall to the lower deck,
    # in through the cabin's door; its opening in the main deck (x0, x1, z0, z1) with a rail round it. The waist is clear
    # of it (the companion house over the waist's ladder of 0.3.7 is gone). The glazed waist (cargo) hatch, the hold
    # hatch under it and the pump stay where they were.
    # (open the stair's whole length: anyone going down it passes under the header at its after end only from its
    # lowest steps, 0.5 and 0.75 up: with a 45° stair the deck's edge would be at their brow any further forward)
    COMPANION = (-6.75, -3.25, -4.0, -3.0)            # (its after end on the deck beam at -7.0: the beam its header)
    COMPANION_DOWN = -1                   # it goes down aft
    COMPANION_RUN, COMPANION_RISE = 0.25, 0.25    # each step (45°: easier than the castles' stairs, 0.5 up a quarter along)
    COMPANION_HEAD = 0.75                 # how far aft of the front wall its rail starts: the way onto its head
    HOOD_X0 = None
    COMPANION_HOUSE = False
    HATCH = (5.25, 7.75, -0.75, 0.75)
    # the pump at the hatch's after end now, its brake reaching aft (the capstan came inboard to clear the fifth gun
    # housed: its bars would sweep over the pump's brake at the hatch's forward end)
    PUMP = (HATCH[0] - 1.25, 0.0)         # (clear of the coaming and of the deck beams over and under it, on a cell corner)
    PUMP_BRAKE = -1
elif PROPOSAL:
    # on the centreline forward of the mainmast (the user, 2026-09-30): the companionway, down toward the bow, a hood
    # over its forward part; the glazed waist (cargo) hatch right forward of it (2026-10-01: "right after the
    # companionway"), the hold hatch under it with its ladder down the same way at its after end (the rest of it clear
    # for cargo): on the lower deck a landing of 1.6 between the companion's foot and the hold hatch's coaming (1.9 to
    # its ladder's head), a block's walk across the waist between the hood and the hatch's coaming. The hatch lies
    # between two deck beams (5.0 and 8.0), partly under the boat gallows (the user accepted that). The bilge pump on the
    # centreline at the hatch's forward end, its brake reaching forward
    COMPANION = (1.5, 3.75, -0.5, 0.5)
    COMPANION_DOWN = 1                    # it goes down toward the bow
    HOOD_X0 = COMPANION[1] - 1.15         # the hood covers it from here to its forward end (the first carrack's)
    # (0.3.7, the user's note 24: "needs some redesign to include doors or hatch, maybe higher with door?") a companion
    # house over it all, standing on its coaming, tall enough to go in upright, a vanilla door in its after end (you go
    # down facing forward): open, the sea on deck can get below there like through an open hatch; shut, not
    COMPANION_HOUSE = True
    HOUSE_TOP = MAIN + 2.5                # its walls' top (the door, on the coaming, reaches MAIN + 2.25)
    HATCH = (COMPANION[1] + 1.5, COMPANION[1] + 4.0, -0.75, 0.75)
    PUMP = (HATCH[1] + 0.75, 0.0)         # (clear of the coaming and of the deck beam under it, on a cell corner)
    PUMP_BRAKE = 1                        # which way along x its brake reaches
else:
    HATCH = (1.5, 4.0, -0.75, 0.75)       # the glazed waist hatch in the main deck, and the hold hatch under it
    # the companionway's opening, port side against the quarterdeck's front: down aft
    COMPANION = (WAIST_AFT, WAIST_AFT + 2.25, -3.25, -2.25)
    COMPANION_DOWN = -1
    HOOD_X0 = WAIST_AFT + 1.15            # the hood covers the companionway from here aft to the bulkhead
    COMPANION_HOUSE = False
    PUMP = (MAIN_X, -1.05)                # the bilge pump, port side of the mainmast
    PUMP_BRAKE = 1
# the boat gallows: two beams across the waist the boats rest on (posts' after faces). Since 0.3.6 (the user: "made as
# continuation of ship's side ribs") each post is a frame carried on up above the bulwark, so they stand on frame
# stations: the forward one on the frame at the entry ports' forward edge (where the gates are hung), the after one two
# frames aft of it, over the companionway; the boats lie between them, clear of the mainmast
GALLOWS_X = (2.0, 6.5)
GALLOWS_Y = QUARTER                   # their top, level with the castle decks: you can climb onto the hatch under them
GALLOWS_BEAM = 0.28                   # the beams' depth (the posts stop under them)
if os.environ.get("HANSA_MODULAR", "0") == "1":
    # (0.3.27, notes 213, 214, 220: "these beams don't need to go thinner - keep the cross section same as ribs"; "these
    # beams could be the same as the ceiling beams and ribs in the castles") carrack-m: a deck beam's depth
    GALLOWS_BEAM = 0.5
BOATS_Z = (-1.5, 1.5)                 # two boats side by side on them
# the space each boat takes: Hansa's rowing boat (vanilla's sailboat less its mast and a section), oars shipped,
# 7.5 x 2.9 x 2.6; clear of the mainmast aft
BOAT_LEN, BOAT_W, BOAT_H = 7.6, 2.9, 2.6
CABLE_SIDE = -1                       # the anchor, its hawse and cable are on the port bow, the capstan's side
# where the cable comes out of the forecastle bulkhead: (0.3.6) in line from the port hawse past the riding bitts to the
# capstan's barrel, so it leads fair
CABLE_Z = -1.5 if PROPOSAL else -1.25
# the wheel, just aft of the quarterdeck's front rail; the carrack has the binnacle (the compass) in front of it, where
# the helmsman sees it over the spokes
WHEEL_X = WAIST_AFT - (1.875 if PROPOSAL else 1.125)
BINNACLE_X = WAIST_AFT - 0.8 if PROPOSAL else None
# the plate for the ship's spawn point on the poop cabin's front bulkhead (x of the spot in front of it, its height, z)
PLATE = (POOP_FRONT + 0.75, QUARTER + 1.45, -1.5)
# (0.3.11) the bronze plaque with her crew list: on the great cabin's front bulkhead, starboard of its door (between it and
# the quarterdeck stair), at eye height, facing forward into the waist (its centre: x of the wall's face, y, z)
PLAQUE = (WAIST_AFT, MAIN + 1.45, 1.05)
PLAQUE_SIZE = (0.4, 0.55)       # tall, wide
# windows either side of the doors in the great cabin's and the poop cabin's front bulkheads (0.3.7, the user's notes 17
# and 18): for each, (z0, z1) of each window, and their sill and head over the floor; glazed like the other cabins'
# windows, the great cabin's front dressed with pilasters, a cornice and a rail (front_dress). The poop cabin's port one
# is outboard of the plate by the door (the spawn point, which faces the helm), its starboard one inboard of the poop
# stair's head.
FRONT_WINDOWS = {"greatcabin": ((-1.75, -1.0), (1.0, 1.75)), "poopcabin": ((-2.7, -1.95), (1.0, 1.75))}
FRONT_WINDOW_Y = (1.0, 1.75)
BITTS_X = 13.875 + STRETCH            # the riding bitts in the forecastle, forward of the foremast
# the hawse holes in the bow, and the bitts' posts. Since 0.3.6 further out (0.9 and 0.875 before: the user, "so the
# rope aligns better with the capstan's off-centre position"): the cable comes in through the port hawse, takes its turn
# round the port bitt and runs straight aft along its outboard side to the capstan
HAWSE_Y, HAWSE_Z = 4.3, (1.6 if PROPOSAL else 0.9)
BITTS_Z = 1.25 if PROPOSAL else 0.875
TILLER_Y = 2.72                       # the tiller swings overhead at the aft end of the lower deck
if PROPOSAL:
    # the ship's firehearth in the forecastle's starboard after corner, against the bulkhead and the side (0.3.6, the
    # user: "moved to bow cabin right corner"), its open front to port: x from the bulkhead, its front (inboard) edge;
    # its back follows the frames. Its stovepipe goes up through the forecastle deck before the first beam, by the side
    # and outboard of the stair's head
    HEARTH = (WAIST_FWD + 0.3, WAIST_FWD + 1.9, 2.15)
else:
    HEARTH = (10.75 + STRETCH, 12.25 + STRETCH, 1.0)   # the brick hearth under the forecastle, starboard side
# its heights over the deck: the brick base's top, the sunken firebed's, the raised sides' tops, the fireback's top
HEARTH_BASE, HEARTH_BED, HEARTH_SIDES, HEARTH_BACK = 0.5, 0.25, 1.0, 1.5
# (0.3.8, the user's note 51: "could place next to fireplace oven and have full oven functions, and connect to the same
# chimney") vanilla's clay oven forward of the hearth against the side, on a brick base as high as the hearth's, its
# mouth to port like the hearth's front; its flue joins the hearth's stovepipe over the hood: x0, x1, its front (z0)
OVEN = (WAIST_FWD + 2.0, WAIST_FWD + 3.0, 2.25) if VARIANT == 3 else None
OVEN_BLOCK = "game:clayoven-south"        # (the variant with its mouth to -z: port)
HEARTH_PIPE = None                        # the stovepipe's centre (x, z), or None: worked out (hearth())
CAPSTAN_R = 0.52                          # the drumhead's radius (its widest)

if GRID:
    # (0.3.9) every main fitting on the one block grid (hull-proposal.md §5 and §8, re-placed for R and the waist 2
    # longer): each mast inside one cell (main in cell -1..0, fore 17..18, mizzen -13..-12)
    MAIN_X, FORE_X = -0.5, 17.5
    # (0.3.10, the waist re-planned, the user's note 87) the mainmast in cell 2..3: at her centre of buoyancy (2.35)
    # and the keel's middle (2.6), a little aft of the waterline's middle (2.94); the masts evenly spaced, -12.5, 2.5, 17.5
    MAIN_X = 2.5
    if HANSA:
        # (Hansa, the mixed rib spacing: decision 11) the mainmast a cell forward, in cell 3..4 between the ribs on lines
        # 2 and 6, a clear cell either side of it
        MAIN_X = 3.5
    # the capstan in one cell to port, on the anchor cable's side (R's width lets it go back there): cell 12..13 x
    # -1.5..-0.5, in the bay forward of the gates', its drumhead within the cell; its crew walking the bars clear of the
    # forecastle's bulkhead, door and stair, the open port gate and the guns
    CAPSTAN_X, CAPSTAN_Z = 12.5, -1.0
    CAPSTAN_R = 0.48
    # (0.3.10, the user's decision on notes 85, 88: on the centreline just aft of the forecastle's bulkhead, in cell
    # 11..12 (clear of the forecastle stair by 1 and more), standing on the main deck only; a cable from either side of
    # the forecastle door onto its own side of the barrel)
    CAPSTAN_X, CAPSTAN_Z = 11.5, 0.0
    # (0.3.13, the user's note 130: "Capstan could be move here. 1 block further and arms made bit shorter") a cell
    # further aft, 10..11 on the centreline, its bars shorter (CAPSTAN_BARS): their sweep clear of the waist hatch's
    # coaming (its forward face at 9.0), the guns however laid and the forecastle's stair and door
    CAPSTAN_X = 10.5
    # the companionway in the great cabin's outermost 2-high port column, 1 block in from the lower deck's side (the
    # user's note 56): the opening x -7..-3, z -4.5..-3.5, the stair from the front wall (G 2) down aft to x -6 (G -1);
    # the main deck's beams at -6 and -4 cut by it onto a carling along its inboard side, a header at -7. A door into
    # it from the waist in the cabin's front right at its head (note 55, DOORS: "door-companion-cabin")
    # (0.3.10, the mesh check: the opening no longer reaches aft of the flight's foot, -6, where it left a gap between
    # the lower deck and the great cabin: its after end on the forward face of the beam at -6, that beam its header)
    COMPANION = (-5.875, -3.0, -4.5, -3.5)
    COMPANION_HEAD = 1.0                  # (clear of that door swung open into the cabin along the side)
    # (0.3.10, the user's notes 82, 102: all stairs on the starboard side) the companion mirrored to starboard, z
    # 3.5..4.5 (its door in the cabin's front there, its starboard window gone: the quarterdeck stair stands in front
    # of it), and from its foot a stair on down to the hold (HOLD_STAIR)
    COMPANION = (-5.875, -3.0, 3.5, 4.5)
    # (0.3.10, the C# side's walk test: no headroom at the stair's foot under the beam at -6) the opening aft to
    # -6.25: from the lowest tread a player's head (1.9 up, 0.3 either side) clears everything; the beam at -6 cut onto
    # a header at -6.5..-6.25, the deck's full beam depth
    COMPANION = (-6.25, -3.0, 3.5, 4.5)
    # (and further: a player 0.6 long standing on the second tread reached the header at -6.25 with their head: the
    # opening to -6.5, the header -6.75..-6.5; stair_headroom checks every step of the way, both ways)
    COMPANION = (-6.5, -3.0, 3.5, 4.5)
    # (0.3.12, the user's note 108: "there should be a stair landing (floor) so that entrance from cabin would be
    # natural") a landing of a whole cell at the stair's head, x -4..-3 (the main deck itself, inside the waist door),
    # the flight a block further aft: its head at -4 (the top tread flush with the deck, -4.25..-4, on the beam at -4),
    # its foot at -7 on the lower deck; the opening -7.5..-4.25, its header -7.75..-7.5 (0.5 aft of the foot, as
    # before), the beam at -6 cut onto the carlings, the one at -4 whole under the landing
    COMPANION = (-7.5, -4.25, 3.5, 4.5)
    COMPANION_TOP = -4.0                  # the flight's head (where the landing starts)
    # (0.3.12, the user's note 112: "railings and open staircase in cabin or we seal it with walls, and maybe extra door
    # to get in stairs from cabin?") the stairwell walled off from the great cabin: a board wall along its inboard side
    # (z 3.25..3.5, the cabin side of the grid line) from its after wall to the front, and one across its after end
    # (x -7.75..-7.5, over the header) out to the side; a door in the inboard wall at the landing (DOORS:
    # "door-companion-stair"). The stairwell is a room of its own ("companion"), open below to the lower deck
    STAIRWELL = (-7.75, -3.0, 3.25, 3.5)  # x0 (the after wall's aft face), x1 (the front wall), the inboard wall's z0, z1
    # (0.3.13, the user's note 138: "lets try version where these doors and whole stairway moves 1 block to the centre of
    # the ship; stairs to the castle then move too and the ship's plaque needs to move as well") the companion a block
    # inboard, z 2.5..3.5: its door in the cabin's front, its opening and stair, the stairwell's inboard wall (z
    # 2.25..2.5) and its door; the hold stair under it (HOLD_STAIR), the quarterdeck stair beside the door (z 1.5..2.5,
    # build()), the crew plaque outboard of the door (PLAQUE). The stairwell runs on out to the side past the stair: a
    # strip of the main deck beside its head
    # (0.3.13, the user's note 142: "just realized moving door would move bottom also much. So we could try two options:
    # moving, and making hull near doors bit thinner inside or bit less slope to leave room for the door. Maybe rib
    # could be shortened") both, for the user to choose: HANSA_COMPANION=A (note 138: all of it a block inboard) or
    # B (the default: where it was, the side by the door given room: companion_door_room)
    if COMPANION_OPTION == "A":
        COMPANION = (-7.5, -4.25, 2.5, 3.5)
        STAIRWELL = (-7.75, -3.0, 2.25, 2.5)
    # the hold stair: from the lower deck beside the companion's foot (a landing x -7..-6, z 2.5..4.5) down forward in
    # z 2.5..3.5 from x -6 (G -1) to x -3 (G -4); its opening in the lower deck x -5.875..-3 (the beam at -6 its header)
    HOLD_STAIR = (-5.875, -3.0, 2.5, 3.5)
    # (its opening on forward past its foot to the beam at -2, the header: walking down, a player's head met the band
    # forward of -3 from the third tread up; the flight still ends at -3)
    HOLD_STAIR = (-5.875, -2.25, 2.5, 3.5)
    HOLD_STAIR_FOOT = -3.0
    # (0.3.13, the user's note 128: "start of these stairs should be aligned with above stair end") a block further aft,
    # as in 0.3.10 before the companion's foot went aft with its landing (0.3.12): its head at x -7, where you step off
    # the companion's foot (-7) a block outboard of it, its foot at -4 on the hold's floor. Its opening -6.875..-3.25
    # (0.75 on past its foot, as before), headers of their own across both ends (no beam at -7 or -3: the beams at -6
    # and -4 cut onto its carlings)
    HOLD_STAIR = (-6.875, -3.25, 2.5, 3.5)
    HOLD_STAIR_FOOT = -4.0
    if COMPANION_OPTION == "A":
        HOLD_STAIR = (-6.875, -3.25, 1.5, 2.5)     # ((0.3.13, note 138) a block inboard with the companion, by its foot)
    # the waist hatch and the hold hatch under it between the beams at 6 and 8, three cells across (the coaming's
    # outside on 6..8 x +-1.5), the hold's ladder in its middle cells; the pump in cell 4..5 on the centreline (its pipe
    # clear of the beams at 4 and 6 on every deck), its brake reaching aft
    HATCH = (6.25, 7.75, -1.25, 1.25)
    PUMP = (4.5, 0.0)
    # the boat gallows' posts on frames 2 and 6 (the boats from 0.2 to 7.8, clear of the mainmast)
    GALLOWS_X = (1.875, 5.875)
    CABLE_Z = -1.0 - 0.40                 # (CAPSTAN_Z - CABLE_WRAP: onto the barrel's outboard side, straight)
    # (0.3.10, the waist re-planned, notes 94, 96, 97) the waist and hold hatches 4 long (coaming 5..9, centred on the
    # gates and the gallows), the main and lower decks' beams at 6 and 8 cut by them; the pump in cell 3..4 right
    # forward of the mainmast, its brake forward (the user: "pump should be by main mast with handle away from it");
    # the gallows' posts on frames 4 and 10 (the boats 3.2..10.8, centred on 7)
    HATCH = (5.25, 8.75, -1.25, 1.25)
    PUMP = (3.5, 0.0)
    PUMP_BRAKE = 1
    if HANSA:
        PUMP = (4.5, 0.0)                 # (Hansa: forward of the mainmast as carrack-m's, the mast a cell forward)
    # (0.3.12, the user's note 110) the hold's ladder upright at the hatches' forward end: the middle of its sides' depth
    # (their forward faces 0.05 aft of the headers' and the coamings' after faces, at 8.75)
    HOLD_LADDER_X = HATCH[1] - 0.12
    GALLOWS_X = (3.875, 9.875)
    if HANSA:
        # (Hansa) the boat gallows' posts on her ribs at 6 and 10 (no rib on line 4 now), the boats over them clear of
        # the mainmast
        GALLOWS_X = (5.875, 9.875)
    CABLE_Z = -1.25                       # (the working (port) cable's lead aft past the foremast, z +-1.25)
    # the wheel in cell -5..-4 (its rim overhangs the cells beside it above knee height), the binnacle in the next,
    # -4..-3, against the quarterdeck's front rail
    WHEEL_X = -4.875
    BINNACLE_X = -3.5
    # the spawn plate in the poop cabin front's wall cell -1.5..-0.5 (port of the door)
    PLATE = (POOP_FRONT + 0.75, QUARTER + 1.45, -1.0)
    # the cabin fronts' windows in whole cells, 1 to 2 up: the great cabin's either side of the door; the poop cabin's
    # to port only (the poop stair comes up against its starboard side)
    FRONT_WINDOWS = {"greatcabin": ((-2.5, -1.5), (1.5, 2.5)), "poopcabin": ((-2.5, -1.5),)}
    # (0.3.10: the great cabin's starboard one gone, the quarterdeck stair in front of it)
    FRONT_WINDOWS["greatcabin"] = ((-2.5, -1.5),)
    FRONT_WINDOW_Y = (1.0, 2.0)
    # (0.3.12, the user's note 122: "decorations on wall is mess now") the great cabin's front dressed symmetrically: a
    # pilaster each side of the door clear of its casing, the port bay closed by one outboard of its window (the window
    # in the middle of its bay), the starboard bay by one against the quarterdeck stair (front_dress)
    FRONT_PILASTERS = (-3.15, -0.85, 0.85, 2.38)
    # the crew plaque the window's counterpart: in the middle of the starboard bay, at the window's middle height
    PLAQUE = (WAIST_AFT, MAIN + 1.5, (FRONT_PILASTERS[2] + FRONT_PILASTERS[3]) / 2)
    # (0.3.13, the user's note 138: the quarterdeck stair a block inboard, z 1.5..2.5: the starboard bay between the door
    # and the stair too narrow for a pilaster at each end and the plaque) a pilaster on the door's starboard side only,
    # the bay closed by the stair; the plaque outboard of the companion's door now, in the middle of the cell 3.5..4.5
    # by the side, at the window's middle height, the sill rail on under it to the side
    if COMPANION_OPTION == "A":
        FRONT_PILASTERS = (-3.15, -0.85, 0.85)
        PLAQUE = (WAIST_AFT, MAIN + 1.5, 4.0)
    # the riding bitts' posts in cells 19..20 x +-(0.5..1.5)
    BITTS_X, BITTS_Z = 19.5, 1.0
    # (0.3.10, the waist re-planned, notes 88, 90) two bowers: the hawse holes further out and up through the
    # forecastle's side above its ledge (G 3.5, z +-3.2: the cable from them to the cathead clears the bow), the
    # catheads 1.5 forward of them; the port anchor the working one (its cable led to the capstan), the starboard one
    # fished on its bill-board, its cable stoppered at its bitt and down a deck pipe to the tier
    HAWSE_Y, HAWSE_Z = GRID_OFF + 3.5, 3.2
    CATHEAD = ((19.9, 1.9), (21.5, 3.3))
    if MODULAR:
        # (0.3.29, the bow guns' full train) the catheads' heels inboard a quarter of their length shorter (their line
        # the same, out through her bulwark as before): the forecastle's forward guns' carriages swing clear of them further
        CATHEAD = ((20.3, 2.25), (21.5, 3.3))
    DECK_PIPES = ((18.5, 1.0), (18.5, -1.0))
    # the hearth on cells 15..17 x 1.5..3.5 (its back follows the side), its stovepipe in cell 16..17 x 2.5..3.5 (clear of
    # the forecastle stair's head), the oven forward of it on cell 17..18 x 1.5..2.5
    HEARTH = (15.0, 17.0, 1.5)
    HEARTH_PIPE = (16.5, 3.0)
    OVEN = (17.0, 18.0, 1.5)
    if MODULAR:
        # (0.3.16, notes 154, 158; docs/modular-pieces.md: a 2 x 1 stair rising 3 needs a 3 x 1 well, over both its cells
        # and the cell before its foot) the companion stair a placed 2 x 1 flight on the lower deck, foot cell -6..-5,
        # climbing aft-to-fore (+x) to the landing at -4..-3 on the main deck; its well in the main deck -7..-4 (the
        # cell before its foot, -7..-6, open over the step off it); the beam at -6 cut onto the carlings, the one at -4
        # under the landing (only its forward half solid: the well's cells free of her), a header at -7.25..-7
        COMPANION = (-7.0, -4.0, 2.5, 3.5)
        # the stairwell: from x -7 (its after wall a line of placed partitions there) to the front, z 2.5 (the inboard
        # wall's line) to the side; no built walls (STAIRWELL kept for its room and the rest of the logic)
        STAIRWELL = (-7.25, FRONT_AFT, 2.25, 2.5)    # ((0.3.28, note 225) to the front on the edge beam's line, -2)
        # the hold stair a placed flight on the hold's floor, foot cell -5..-4 climbing aft (-x) to the lower deck at -6,
        # beside the companion's foot; its well in the lower deck -6..-3 (the cell before its foot, -4..-3, open)
        HOLD_STAIR = (-6.0, -3.0, 1.5, 2.5)
        HOLD_STAIR_FOOT = -4.0
        # (0.3.18, note 162; docs/modular-pieces.md §7.1) the stairs are companion ladder-stairs now: each well exactly its
        # two cells (foot + head), no deck cut beyond them. The companion's well in the main deck -6..-4 (between the
        # beams at -6 and -4, both whole: 0.75 under the deck, as the contract allows), the cell before its foot (-7..-6)
        # main deck again over the lower deck; the hold stair's in the lower deck -6..-4 (the beams there 1.0 deep: their
        # undersides notched to 0.75 over the well), the cell before its foot (-4..-3) lower deck again
        COMPANION = (-6.0, -4.0, 2.5, 3.5)
        HOLD_STAIR = (-6.0, -4.0, 1.5, 2.5)
        # (0.3.23, note 173; the new stair contract) walked steep stairs, 2 long, rising 3 over 2: each well 3 x 1, over
        # the cell before its foot (P: its walker's head comes up through the deck above there) and its two cells,
        # framed with a header across each end (the beam over its far end cut onto the well's carlings with the one
        # across it, none of it reaching back over the stair's head cell). The companion's in the main deck -7..-4 (its
        # after end the stairwell's after wall, x -7); the hold stair's in the lower deck -6..-3, a block further inboard
        # (z 0.5..1.5: the companion stair's handrail over its well's edge would stand in the hold stair's room beside it,
        # z 1.5..2.5; a strip of the lower deck between them now, the companion's foot and the hold stair's head both at
        # x -7..-6)
        COMPANION = (-7.0, -4.0, 2.5, 3.5)
        HOLD_STAIR = (-6.0, -3.0, 0.5, 1.5)
        # (0.3.29, chalk notes 230 and 233: "companion way here with enclosed walls", "it was next to castle stairs as i
        # made in the save file"; the user's #12600: a steep stair from the lower deck's cell -2..-1 on the centreline up
        # forward to the waist at x 0, its well the cells -3..0 (and the main deck's beam at -2 there), walled to port,
        # the castle stair to starboard) the companion beside the quarterdeck stair: its well the centreline cells x -3..0
        # (z -0.5..0.5) and the beam segment at -2 across it, all of them deck slots that start out (no fixed framing: the
        # beam's carlings are the segment's), the after cell inside the great cabin under the quarterdeck; enclosed by
        # placed walls (COMPANION_WALLS), a door at its head (x 0) swinging out forward, a roof of hood boards over the two
        # waist cells. The great cabin's door goes a cell to port (z -1.5..-0.5); its front's centre segment (k 0) open
        # into the companion. The old stairwell in the cabin's starboard corner is gone (a ship laid before keeps hers:
        # LAYOUT_REV and each new entry's "since")
        COMPANION_WAIST = True
        COMPANION = (-3.0, 0.0, -0.5, 0.5)
        STAIRWELL = None
        # (0.3.18, note 165; §7.2) her hearth and oven are pieces (the mod's `hearth`, `oven`, `flue`): no fixed ones, no
        # firebed or grating in her; the forecastle's front all pieces. The hearth in cell 17..18 x 1.5..2.5, a block
        # inboard of the old one (by the side, at 2.5..3.5, the forecastle's bulwark stands over its flue's way out and
        # the tumblehome is in the 2 blocks over the cell), between the forecastle deck's beams at 16 and 18; the oven
        # aft of it in cell 16..17 x 1.5..2.5; both open to port, the cooks in the aisle 16..18 x 0.5..1.5 beside the
        # foremast, in from the forecastle door; one flue from the hearth up through the forecastle deck (open air)
        HEARTH_PIECE = (17.5, 2.0, "-z")
        OVEN_PIECE = (16.5, 2.0, "-z")
        # (note 153: "Plaque we could move to main mast?") the crew plaque on the mainmast's aft face at eye height on
        # the main deck (its cabin front is removable here); the spawn plate (the poop cabin's front is too) on its
        # port face, the spot beside it on the main deck (x, height, z of the mast's axis: fixtures() puts them on it)
        PLAQUE = (MAIN_X, MAIN + 1.5, 0.0)
        PLATE = (MAIN_X, MAIN + 1.45, -1.0)


# (0.3.13, the user's note 134) each door's casing in a paint zone of its own (paint_zones), the companion's two together
DOOR_CASING_ZONE = {"door-greatcabin": "door-casings", "door-companion-cabin": "door-casings-companion",
                    "door-companion-stair": "door-casings-companion", "door-forecastle": "door-casings-forecastle",
                    "door-poopcabin": "door-casings-poopcabin"} if GRID else {}
DOOR_CASING_ZONE.setdefault("door-companion-stair", "door-casings")
# (0.3.20, step B, docs/slots.md §5) carrack-m: no hatch fixtures (their leaves, glass, coamings): the waist's and the
# hold's hatchways are openings of 3 x 3 empty deck cells, x 6..9 x z -1.5..1.5 on the main and the lower deck (the
# ladders' line, x 8.27, inside), each with a hatch piece over it (modularPieces "hatch": a solid trapdoor hatch over the
# hold's, a window trapdoor hatch over the waist's). Framed as a hatchway: the beam at 6 under its after edge (whole), the
# one at 8 cut by it onto carlings along its sides, a header across its forward end, x 9..9.25
OPENING = (6.0, 9.0, -1.5, 1.5) if MODULAR else None
# (0.3.24, note 187: "with modular floors we could make hatches to stick between ribs ... to have at least ribs and beams
# as fixed elements") the hatchways between two beams: x 6..8 (2 x 3 cells), the beams at 6 and 8 whole under their
# ends (each beam is centred on its frame's line, a cell edge: the opening's ends rest on them), no beam cut, no header;
# carlings along its sides only (note 186)
if MODULAR:
    OPENING = (6.0, 8.0, -1.5, 1.5)
# (0.3.18) carrack-m: the hearth and the oven are the mod's pieces (modular_layout), nothing of them in her
PIECE_HEARTH = MODULAR
# (0.3.24, notes 183, 192: "get rid of chimney", "we can skip the chimney for a while to simplify") no flue laid (the mod
# no longer asks for one): the forecastle deck over the hearth is clear for the bow guns at 17
HEARTH_FLUE = False
if PIECE_HEARTH:
    OVEN = None

GATE_DIR = 1 if GATE_HINGE_AFT else -1      # which way along x the gate's leaf runs from its hinge, shut
# (0.3.10) which side the companion is on (+1 starboard), and its inboard edge's z
COMP_S = 1 if COMPANION[2] > 0 else -1
COMP_IN = COMPANION[2] if COMP_S > 0 else COMPANION[3]
if not GRID:
    HOLD_STAIR = None
    COMPANION_TOP = COMPANION[1]          # (the flight's head at the opening's forward end)
    STAIRWELL = None
COMPANION_WAIST = globals().get("COMPANION_WAIST", False)
if COMPANION_WAIST:
    COMPANION_TOP = COMPANION[1]          # ((0.3.29) the stair's head at the well's forward end, x 0)
# (0.3.29) carrack-m's layout revision: a ship laid before (her saved layout older) keeps her deck cells' states where the
# design's start changed (the old companion's well laid, the new one's cells in) and isn't given the entries new in it
# (each modular entry's "since"): docs/modular-pieces.md §14
LAYOUT_REV = 1 if MODULAR else 0


def gate_hinge(s):
    """The entry port gate's hinge (x, z) on side s: its forward edge (GRID: its after edge, on frame 10), on its inboard
    side. (The side leans in over it, the tumblehome: the axis is upright under the inboard edge of its rail cap, so
    nothing of it is inboard of the axis and it swings clear of the frame it's hung on, lying along that frame's face
    open.)"""
    hx = PORT_X[0] if GATE_HINGE_AFT else PORT_X[1]
    return np.array([hx, s * (hw(hx, MAIN + WAIST_RAIL - 0.08) - HULL_T - 0.08)])


def gate_leaf(s, a_deg):
    """The middle line of the gate's leaf (at its mid height) swung a_deg inboard: from its hinged end along the side when
    shut; its thickness (HULL_T) is either side of it."""
    a = math.radians(a_deg)
    u = np.array([GATE_DIR * math.cos(a), -s * math.sin(a)])          # along it, from the hinge
    w = np.array([GATE_DIR * math.sin(a), s * math.cos(a)])           # out through it (outward when shut)
    hx = gate_hinge(s)[0]
    out = hw(hx, MAIN + WAIST_RAIL / 2) - HULL_T / 2 - abs(gate_hinge(s)[1])
    p0 = gate_hinge(s) + w * out
    return p0, p0 + u * (PORT_X[1] - PORT_X[0])


def hull_hw(x, y):
    """The hull's half width at (x, y). (Hansa) faceted: _hull_hw_facets."""
    if FACETS:
        return _hull_hw_facets(x, y)
    return _hull_hw_smooth(x, y)


# (Hansa, decision 8 of docs/proposals/ship-design-architecture.md: "faceted. Corners on a half-block grid, a small set
# of slopes"; the user on 16 October 2026: "looks very round hull without more faceted approach we discussed") her hull
# carrack-m's lines in facets: its half width taken at corners every FACET_DY up her side (design y on the half-block
# grid: every block row's border and middle) and every FACET_DX along her, each corner's width rounded to FACET_W, and
# straight between them (so her slopes are a small set: steps of FACET_W over FACET_DY or FACET_DX). Her stem's and her
# sternpost's lines stay carrack-m's. Everything built on hull_hw (her planking, her collision, her decks' ends, her
# panels) follows. FACET_W a quarter block (the user: "we can always add more planes ... keeping bit edgy view");
# HANSA_FACET_W=0 keeps carrack-m's smooth lines.
FACETS = HANSA and os.environ.get("HANSA_FACETS", "1") == "1"
# (the user, 16 October 2026: "hull cross section look zig-zag and bad": each corner's width rounded on its own made her
# side's facets alternate steeper and flatter wherever its slope isn't a whole number of steps. The corners take her
# exact width now, so each facet is a straight chord of carrack-m's curve; HANSA_FACET_W > 0 rounds them up again)
FACET_W = float(os.environ.get("HANSA_FACET_W", "0"))
# (her side's creases on the block rows' borders, design y n + 0.5: one plane a block row, decision 8's "1-2 planes in
# most cells", edgy; HANSA_FACET_DY=0.5 for twice as many)
FACET_DX, FACET_DY = 1.0, float(os.environ.get("HANSA_FACET_DY", "1.0"))
FACET_Y0 = 0.5 if FACET_DY >= 1.0 else 0.0


if FACETS:
    import planking as _planking
    _planking.KINK_WINDOW = 25


def _snap_w(w):
    # (where she's narrower than a facet's step, by the stem and the sternpost, her own width: rounded to nothing, her
    # sides would meet short of the stem and leave a gap by it)
    # (rounded up: her facets never inside carrack-m's lines, so nothing of hers built to them loses its room)
    w = np.asarray(w, float)
    if FACET_W <= 0:
        return w
    return np.where(w < FACET_W, w, np.ceil(w / FACET_W - 1e-9) * FACET_W)


def _hull_hw_facets(x, y):
    x, y = np.asarray(x, float), np.asarray(y, float)
    y0 = np.floor((y - FACET_Y0) / FACET_DY) * FACET_DY + FACET_Y0
    fy = (y - y0) / FACET_DY

    def row(yc):
        # along her at the corner height yc: straight between corners every FACET_DX, the first and the last facet
        # from and to her own ends there (her transom, her stem), so her sides close where she ends
        xs_, xb_ = interp(yc, STERN_X), interp(yc, BOW_X)
        x0 = np.floor(x / FACET_DX) * FACET_DX
        xa = np.maximum(x0, xs_)
        xb = np.minimum(x0 + FACET_DX, xb_)
        xa = np.minimum(xa, xb - 1e-6)
        f = np.clip((x - xa) / np.maximum(xb - xa, 1e-6), 0.0, 1.0)
        wa = _snap_w(_hull_hw_smooth(xa + 1e-6, yc))
        wb = _snap_w(_hull_hw_smooth(xb - 1e-6, yc))
        return wa * (1 - f) + wb * f

    w = row(y0) * (1 - fy) + row(y0 + FACET_DY) * fy
    # (never past her own ends: where the smooth lines end she ends)
    return np.where(_hull_hw_smooth(x, y) > 0, np.maximum(w, 0.0), 0.0)


def _hull_hw_smooth(x, y):
    x, y = np.asarray(x, float), np.asarray(y, float)
    sec = B * interp(y, SECTION)
    xb = interp(y, BOW_X)
    t = np.clip((x - MIDBODY_FWD) / (xb - MIDBODY_FWD), 0, None)
    if GRID:
        # (the bow's ellipse finer toward the keel: its exponent by height, BOW_PG)
        pb = np.interp(y - GRID_OFF, *zip(*BOW_PG))
        fb = np.where(t < 1, np.clip(1 - np.clip(t, 0, 1) ** pb, 0, 1) ** (1 / pb), 0.0)
    else:
        fb = np.where(t < 1, np.sqrt(np.clip(1 - t ** 2, 0, 1)), 0.0)
    xs = interp(y, STERN_X)
    ts = np.clip((-6.0 - x) / (-6.0 - xs), 0, None)
    ps = interp(y, STERN_P)
    if GRID:
        ps = ps * np.interp(y - GRID_OFF, *zip(*RUN_SCALE))
    wt = interp(y, TRANSOM_W)
    fs0 = np.clip(1 - np.clip(ts, 0, 1) ** ps, 0, 1) ** (1 / ps)
    fs = np.where(ts <= 1, wt + (1 - wt) * fs0, 0.0)
    return sec * np.minimum(fb, fs)


def fc_tri(x, y):
    """The forecastle's own sides: from the hull at FC_X0 to a point at FC_TIP, slightly convex."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    w = hull_hw(np.full_like(x, FC_X0), y)
    u = np.clip((x - FC_X0) / (FC_TIP - FC_X0), 0, 1)
    return np.where((x >= FC_X0) & (x <= FC_TIP), w * (1 - u ** 1.5), 0.0)


def fc_under(x):
    """Height of the forecastle's underside outside at x: FC_LOW up to the stem, rising straight to FC_Y0 at the tip."""
    x = np.asarray(x, float)
    xs0 = float(interp(FC_LOW, BOW_X))
    return np.where(x <= xs0, FC_LOW, FC_LOW + (FC_Y0 - FC_LOW) * np.clip((x - xs0) / (FC_TIP - xs0), 0, 1))


def poop_hw(x, y):
    """The hull's plan at y carried aft by the poop's overhang: its stern run stretched from the midbody's end (-6) to
    POOP_OVERHANG past the transom, so the overhanging cabin's sides go on in the hull's own lines."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    xs = interp(y, STERN_X)
    k = (-6.0 - xs) / (-6.0 - xs + POOP_OVERHANG)
    xm = -6.0 + (x + 6.0) * k
    # (0.3.8) its after end exactly on the hull's transom, as wide as it: rounding put a hair aft of it, where the hull
    # is 0, at some heights, and a side's outline there began with a run across the transom (the strakes over the
    # overhang skewed, the user's notes 33-36)
    xm = np.where(x >= xs - POOP_OVERHANG - 1e-9, np.maximum(xm, xs), xm)
    return hull_hw(np.where(x < -6.0, xm, x), y)


def half_width(x, y):
    """The outside of the hull: the forecastle's sides step out over the bow above its underside, the poop's over the
    transom above OH_Y."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    h = hull_hw(x, y)
    h = np.where(y >= fc_under(x) - 1e-9, np.maximum(h, fc_tri(x, y)), h)
    if POOP_OVERHANG:
        h = np.where(y >= OH_Y - 1e-9, np.maximum(h, poop_hw(x, y)), h)
    return h


def inner_width(x, y):
    """The outline the inside planking follows: the outside's, except that inside, the forecastle steps out only at
    FC_Y0 (below it, where its raked underside comes down outside, the hull is thick)."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    if FC_LOW == FC_Y0:
        return half_width(x, y)
    h = np.where(y >= FC_Y0 - 1e-9, half_width(x, y), hull_hw(x, y))
    if POOP_OVERHANG:
        h = np.where((y >= OH_Y - 1e-9) & (y < FC_Y0 - 1e-9), np.maximum(h, poop_hw(x, y)), h)
    return h


def hw(x, y):
    return float(half_width(np.array(x, float), np.array(y, float)))


def base_stern_x(y):
    """Where the hull's own transom is at y (without the poop's overhang)."""
    return float(interp(y, STERN_X))


def stern_x(y):
    """The after end at y: the transom, or above OH_Y the overhanging poop's."""
    return float(interp(y, STERN_X)) - (POOP_OVERHANG if y >= OH_Y - 1e-9 else 0.0)


def bow_x(y):
    """The forward end inside (the stem, or the forecastle's tip above FC_Y0)."""
    return FC_TIP if y >= FC_Y0 - 1e-9 else float(interp(y, BOW_X))


def outer_bow_x(y):
    """The forward end outside: the stem, the forecastle's tip, or between FC_LOW and FC_Y0 where its raked underside
    is at height y."""
    if y >= FC_Y0 - 1e-9:
        return FC_TIP
    if y < FC_LOW - 1e-9:
        return float(interp(y, BOW_X))
    xs0 = float(interp(FC_LOW, BOW_X))
    return xs0 + (FC_TIP - xs0) * (y - FC_LOW) / (FC_Y0 - FC_LOW)


def transom_hw(y):
    if FACETS:
        # (Hansa: the faceted hull) as wide as her faceted sides where they end at it, so its edge meets them (the
        # smooth width left a saw-tooth between the two)
        return float(hull_hw(float(interp(y, STERN_X)) + 1e-4, y))
    return float(B * interp(y, SECTION) * interp(y, TRANSOM_W))


def _sweep(u):
    """A castle break's rise: 0 at the waist end, 1 at the castle front; a quarter ellipse, level at the waist and
    upright where it meets the castle."""
    u = np.clip(np.asarray(u, float), 0, 1)
    return 1 - np.sqrt(1 - u ** 2)


def rail_y(x):
    """Top of the hull side (bulwark) along the ship. At each castle front the sheer sweeps up in a curve."""
    x = np.asarray(x, float)
    y = np.full_like(x, MAIN + WAIST_RAIL)
    # ((0.3.24, notes 184, 185) carrack-m: the castle sides full height out to a quarter past the decks' new edges, their
    # breaks sweeping down from there, more steeply: BREAK_L_FRONT)
    qa, fa = WAIST_AFT + EDGE_OUT, WAIST_FWD - EDGE_OUT
    y = np.where(x < qa, QUARTER + 1.0 + CASTLE_UP, y)
    y = np.where(x < POOP_FRONT, POOP + 1.0 + CASTLE_UP, y)
    y = np.where(x >= fa, QUARTER + 1.0 + CASTLE_UP, y)
    rise = QUARTER + 1.0 + CASTLE_UP - MAIN - WAIST_RAIL
    lo, hi = qa, qa + BREAK_L_FRONT
    y = np.where((x >= lo) & (x < hi), MAIN + WAIST_RAIL + rise * _sweep((hi - x) / BREAK_L_FRONT), y)
    lo, hi = fa - BREAK_L_FRONT, fa
    y = np.where((x > lo) & (x < hi), MAIN + WAIST_RAIL + rise * _sweep((x - lo) / BREAK_L_FRONT), y)
    lo, hi = POOP_FRONT, POOP_FRONT + BREAK_L
    y = np.where((x >= lo) & (x < hi), QUARTER + 1.0 + CASTLE_UP + 3.0 * _sweep((hi - x) / BREAK_L), y)
    return y


def rail(x):
    return float(rail_y(np.array(x, float)))


def sheer(x):
    """How far the wales sweep up toward the ends. (GRID: not at all: they follow the planking's strakes, note 67)"""
    if GRID:
        return np.zeros_like(np.asarray(x, float))
    return 0.4 * ((np.asarray(x, float) - 1.0 - STRETCH / 2) / (15.0 + STRETCH / 2)) ** 2


def strake_levels():
    """The outside planking's strakes from the keel to the main deck: equally wide round the midship section (so the
    round bilge gets as many as the sides, like real planking), about 0.3 each. ((0.3.26) carrack-m: seamed_levels)"""
    if HULL_SLOTS:
        return seamed_levels()
    ys = np.linspace(KEEL, MAIN, 2000)
    zs = half_width(np.zeros_like(ys), ys)
    girth = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(ys), np.diff(zs)))])
    return list(np.interp(np.linspace(0, girth[-1], int(round(girth[-1] / 0.3)) + 1), girth, ys))


_seamed = None


def seamed_levels():
    """(0.3.26, step D1) carrack-m's strakes from the keel to the main deck between the hull's seams (HULL_BANDS,
    HULL_FIXED_BANDS: quarter-block lines): each band's strakes equally wide round the midship section, about 0.3 each
    (a fixed band's 0.25 high: one strake, the wales and the waterway one each)."""
    global _seamed
    if _seamed is None:
        seams = sorted({round(y, 6) for a, b in HULL_BANDS for y in (a, b) if y <= MAIN + 1e-6} |
                       {round(y, 6) for a, b, _ in HULL_FIXED_BANDS for y in np.arange(a, b + 1e-6, 0.25) if y <= MAIN + 1e-6})
        if HANSA:
            # (Hansa: the faceted hull) a seam on every facet's crease, every half block up her side
            lo_, hi_ = seams[0], seams[-1]
            seams = sorted(set(seams) | {round(k * 0.5, 6) for k in range(int(math.ceil(lo_ / 0.5)), int(math.floor(hi_ / 0.5)) + 1)})
        out = [seams[0]]
        for a, b in zip(seams[:-1], seams[1:]):
            ys = np.linspace(a, b, 400)
            zs = half_width(np.zeros_like(ys), ys)
            girth = np.concatenate([[0.0], np.cumsum(np.hypot(np.diff(ys), np.diff(zs)))])
            n = max(1, int(round(girth[-1] / 0.3)))
            out += [float(v) for v in np.interp(np.linspace(0, girth[-1], n + 1), girth, ys)[1:]]
            out[-1] = float(b)
        _seamed = out
    return list(_seamed)


def frame_strips():
    """(0.3.26, step D1) The x each frame's strip of planking runs between (its middle +- STRIP_HALF), and each frame
    space's middle: where carrack-m's planking rings all have a point (planking.xgrid_rings)."""
    xs = []
    for xf in panel_stations():
        if HALF_PANELS:
            xs += [xf, xf + FRAME_SPACE / 2]       # ((0.3.28) the panels' ends: each frame's middle, each space's)
        else:
            xs += [xf - STRIP_HALF, xf + STRIP_HALF, xf + FRAME_SPACE / 2]
    return sorted(xs)


def wale_bands():
    """The wales' (lower edge, upper edge) amidships: each (GRID) one strake of the planking, the one whose lower edge is
    nearest to WALES' height; else WALES' 0.25 high."""
    if not GRID:
        return [(y, y + 0.25) for y in WALES]
    if HULL_SLOTS:
        return [tuple(w) for w in HULL_WALES]       # ((0.3.26) on the seams: two strakes of the fixed band)
    lv = strake_levels()
    out = []
    for w in WALES:
        k = min(range(len(lv) - 1), key=lambda i: abs(lv[i] - w))
        out.append((float(lv[k]), float(lv[k + 1])))
    return out


def wale_top(x):
    """The upper wale's top edge at x."""
    return wale_bands()[-1][1] + float(sheer(x))


def wall_z(x, y, gap=0.2):
    """How far out from the centreline something along the inside planking goes (gap clear of it)."""
    return ceil_z(x, y) - gap


_ceil_rings = {}


def _ceil_ring(y):
    """The inside planking's outline at height y: the hull's outline moved HULL_T inboard square to it (as planked)."""
    key = round(float(y), 4)
    r = _ceil_rings.get(key)
    if r is None:
        from planking import offset_outline, outline
        xs, zs = outline(inner_width, key, stern_x(key), bow_x(key), 900)
        t = HULL_T
        if GRID and key - GRID_OFF < 2.0:
            # (the shell thicker toward the bottom: the outline moved in by the midship section's thickness across at
            # this height, the frames deeper in it)
            t = max(HULL_T, float(th_h(key - GRID_OFF)))
        r = offset_outline(xs, zs, t)
        _ceil_rings[key] = r
    return r


def shell_t(y):
    """How far in from the outside the inside planking is across at height y (amidships)."""
    if GRID and y - GRID_OFF < 2.0:
        return max(HULL_T, float(th_h(y - GRID_OFF)))
    return HULL_T


# (0.3.29, step D2.3 as the user chose it on 0.3.28: "maybe thick hull is just two panel blocks in thickness placed. in
# open decks just one placed") carrack-m's side under her weather decks (the hold, the lower deck, the great cabin, the
# poop cabin, the forecastle) is two layers of panels: her planking outside and a ceiling inside it, LINING further in
# than her side's inside was, the frames (ribs) RIB_SIDED wide between the two, PLANK_T each layer's thickness as drawn
# (its edges, its back). Over her open decks (the bulwarks) one layer as before, the frames standing inside it
LINED = MODULAR
LINING = 0.25
PLANK_T = 0.08
RIB_SIDED = 0.5


def weather_deck(x):
    """(0.3.29) The open deck over her side at x (carrack-m): the poop's aft of the poop cabin's front, the quarterdeck's
    to its edge, the waist's (the main deck) to the forecastle deck's edge, the forecastle deck's forward of it."""
    x = np.asarray(x, float)
    return np.where(x < POOP_FRONT - 0.25, POOP, np.where((x < QD_EDGE) | (x > FC_EDGE), QUARTER, MAIN))


def lined(x, y):
    """(0.3.29) Is her side two layers here (a room's, under her weather deck: below that deck's underside)?"""
    if not LINED:
        return np.zeros(np.shape(np.asarray(x, float)), bool) if np.ndim(x) else False
    v = np.asarray(y, float) < weather_deck(x) - DECK_T - 1e-6
    return v if np.ndim(v) else bool(v)


LINED_X = (-9.0, -2.0, 14.0)          # (0.3.29) where weather_deck steps (POOP_FRONT - 0.25, QD_EDGE, FC_EDGE)


def lining_at(y):
    """(0.3.29) How much further in the two layers' ceiling is at height y: LINING, eased in over the 0.4 over the hold's
    floor (her bottom's shell is that much thicker there already: the offset outline folds over itself)"""
    return LINING * float(np.clip((float(y) - HOLD_FLOOR) / 0.4, 0.0, 1.0))


def ceil_z_raw(x, y):
    """(0.3.13) ceil_z as planked, without door_room (the planking it's taken off after: companion_door_room).
    ((0.3.29) LINING further in where her side is two layers)"""
    xo, zo = _ceil_ring(float(y))
    if len(xo) < 2:
        return np.zeros_like(np.asarray(x, float)) if np.ndim(x) else 0.0
    v = np.interp(x, xo, zo, left=0.0, right=0.0)
    if LINED:
        v = np.where((v > 0) & lined(x, np.full(np.shape(np.asarray(x, float)), float(y))), v - lining_at(y), v)
    return v if np.ndim(x) else float(v)


def ceil_z(x, y):
    """How far out the inside planking (the ceiling) is at (x, y); 0 past its ends. x may be an array."""
    xo, zo = _ceil_ring(y)
    if len(xo) < 2:
        return np.zeros_like(np.asarray(x, float)) if np.ndim(x) else 0.0
    v = np.interp(x, xo, zo, left=0.0, right=0.0)
    if LINED:
        v = np.where((v > 0) & lined(x, np.full(np.shape(np.asarray(x, float)), float(y))), v - lining_at(y), v)
    if DOOR_ROOM:
        v = np.where(v > 0, v + door_room(x, y), v)
    return v if np.ndim(x) else float(v)


# (0.3.13, the user's note 142, option B: "making hull near doors bit thinner inside ... to leave room for the door")
# how much thinner the side's inside planking is, by the companion's door in the great cabin's front, on the waist's
# side (the outside's line kept fair): DOOR_ROOM at its most, from the front wall's face to 0.4 forward of it, easing
# off over the next block forward; from 1.6 over the deck (easing in from 1.0) to 2.3 (easing off by 2.75, the
# quarterdeck's underside; and clear of the break's cap), where the
# leaning side crowds the door's head and its outboard jamb's casing. Both sides alike (the port side's the same
# shape; nothing there minds)
DOOR_ROOM = 0.14 if GRID and COMPANION_OPTION == "B" else 0.0


def _smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def door_room(x, y):
    """(0.3.13, note 142 B) How much further out the ceiling is at (x, y) by the companion's door (x may be an array)."""
    x = np.asarray(x, float)
    fx = np.where(x < WAIST_AFT - 1e-9, 0.0, 1.0 - _smooth((x - WAIST_AFT - 0.4) / 1.0))
    fy = _smooth((y - MAIN - 1.0) / 0.6) * (1.0 - _smooth((y - MAIN - 2.3) / 0.45))
    # (none within 0.25 of the castle break's cap over it, easing in over 0.3 under that: the cap's inboard edge is
    # only 0.08 inboard of the planking as built)
    fc = _smooth((rail_y(x) - 0.25 - y) / 0.3)
    v = DOOR_ROOM * fx * fy * fc
    return v if np.ndim(v) else float(v)


def companion_door_room(d, n0):
    """(0.3.13, note 142 B) The inside planking hull() made (its quads from n0) moved out by door_room where it's on the
    waist's side of the great cabin's front, as ceil_z has it: the shell thinner there, its outside as it was."""
    if not DOOR_ROOM:
        return
    for q in d.quads[n0:]:
        if q.get("paint") != "inside":
            continue
        pts = q["pts"]
        if max(p[0] for p in pts) < WAIST_AFT - 1e-6 or min(p[0] for p in pts) > WAIST_AFT + 1.5:
            continue
        q["pts"] = [(p[0], p[1], p[2] + math.copysign(door_room(p[0], p[1]), p[2])) for p in pts]


FRAME_SIDED, FRAME_MOULDED = 0.25, 0.2      # a frame's width fore and aft, and how far it stands in from the ceiling


def open_floor(x):
    """(GRID) The floor of the deck open to the sky at x: the waist's (the main deck), the quarterdeck's and the
    forecastle deck's, or the poop's."""
    if x < POOP_FRONT:
        return POOP
    if x < WAIST_AFT or x > WAIST_FWD:
        return QUARTER
    return MAIN


def frame_face(x, y):
    """How far out the frames' inner faces are: where anything along the side has to stay inboard of. (GRID: the frames
    are in the hull's thickness, behind the ceiling, except over the open decks, where they stand inside the bulwarks.)"""
    if GRID and y < open_floor(x) - 1e-6:
        return ceil_z(x, y)
    return ceil_z(x, y) - FRAME_MOULDED


def panel_stations():
    """The frame spaces' grid her hull panels are cut and named on (every FRAME_SPACE: two one-block panels each, "a"
    and "f"), and the mod's "frames" naming grid. (For carrack-m, also her frames: frame_stations.)"""
    return [BEAM_X0 + 0.125 + FRAME_SPACE * k for k in range(-4, 26)]


# (Hansa, the agreed mixed rib spacing, docs/proposals/ship-design-architecture.md decision 10 and the user on 16 October
# 2026: "rib spacing as looks to be also magelan") her frames (ribs) on carrack-m's frame lines, but bays of 3 clear
# cells as the base and 1 clear cell either side of each mast (the mizzen in cell -13..-12, the main moved to 3..4,
# the fore 17..18), 1 at her ends: each rib's cell the block aft of its line, its timber on the line as carrack-m's.
# Her deck beams stay on every even line (those between ribs bear on her side); the frame lines between ribs are
# her panels' seams only (panel_stations)
RIB_LINES = [-16.0, -14.0, -10.0, -6.0, -2.0, 2.0, 6.0, 10.0, 14.0, 16.0, 20.0, 22.0]


def frame_stations():
    """The frames (ribs): their centres (lines). (Hansa: RIB_LINES)"""
    if HANSA:
        return list(RIB_LINES)
    return panel_stations()


def main_beams():
    """Where the main deck's beams start (0.25 wide)."""
    return [float(x) for x in np.arange(BEAM_X0, 12.5 + STRETCH, FRAME_SPACE)]


def lower_beams():
    """The lower deck's beams: under the same frames as the main deck's (drawn only: the hold needs the headroom)."""
    return main_beams()


# the aftmost castle beams (out over the transom, overhanging)
AFT_BEAM0 = BEAM_X0 - (2.0 if GRID else 4.5 if POOP_OVERHANG else 1.5)


def castle_beams():
    """Under the quarterdeck and the forecastle deck, in line with the main deck's."""
    return ([float(x) for x in np.arange(AFT_BEAM0, WAIST_AFT - 0.5, FRAME_SPACE)] +
            [float(x) for x in np.arange(BEAM_X0, (18.0 if GRID else 16.0) + STRETCH, FRAME_SPACE) if x > WAIST_FWD + 0.5])


def edge_beams():
    """(0.3.24, notes 184, 185: "proper roof beam", "proper ceiling beam at edge") carrack-m: the beams under the
    quarterdeck's and the forecastle deck's edges (QD_EDGE, FC_EDGE: on the frames at -2 and 14), where they start
    (0.25 wide, as castle_beams()). (0.3.27, notes 216, 217: "this beam does not align with the overall grid and beam rib
    spacing or placement positions") each centred on its frame's line as every other beam (a hair proud of its deck's edge
    over the waist: 0.115), not flush with the edge on the castle's side."""
    return [QD_EDGE - 0.125, FC_EDGE - 0.125] if EDGE_OUT else []


# (0.3.27, the user's stairs and beams: "stairs need a 3-block opening and the beams are every 2"; docs/slots.md §10)
# carrack-m: a deck beam's collision is the one voxel column aft of its frame's line (xc - 0.25 .. xc), not the two either
# side of it: a cell forward of a beam (xc .. xc + 1) is free of it under the deck, so a stair across her (along z) goes up
# between two beams with no beam to cut; and a beam under a well is in lengths a cell wide that come out (beam_segments)
BEAM_SEGMENTS = MODULAR


def beam_coll(xc, bh=0.25):
    """(0.3.27) A deck beam's collision along x: (x0, x1). carrack-m: the voxel column aft of its line; else bh either side."""
    return (xc - 0.25, xc) if BEAM_SEGMENTS else (xc - bh, xc + bh)


def poop_beams():
    return [float(x) for x in np.arange(AFT_BEAM0, POOP_FRONT - 0.5, FRAME_SPACE)]


def hull_normal(x, y, fn=None):
    """Outward horizontal normal of the starboard hull side at (x, y)."""
    fn = fn or half_width
    e = 0.01
    dz = (float(fn(np.array(x + e), np.array(y))) - float(fn(np.array(x - e), np.array(y)))) / (2 * e)
    n = np.array([-dz, 0.0, 1.0])
    return n / np.linalg.norm(n)


def hull_samples():
    """Points tested against world blocks every tick: one block apart so a post or a thin wall can't slip between."""
    if GRID:
        # (0.3.10) along the keel a hair over its underside (game -4.355), where she touches bottom first: in 5 blocks
        # of water only when her keel does (her flat bottom is 0.25 higher, G -4.25); round the forefoot, over the
        # bottom's rise there (the keel 0.2 under it). (0.3.9: KEEL + 0.25, 0.5 over the keel's underside)
        x_ff = float(interp(KEEL, BOW_X))
        prof = sorted((float(x), float(y)) for y, x in BOW_X if y < KEEL + FOREFOOT_R)
        pts = []
        for x in np.arange(-11.5, 11.0 + STRETCH, 1.0):
            y = KEEL_BOTTOM + 0.02 if x <= x_ff else float(np.interp(x, *zip(*prof))) - 0.2 + 0.02
            pts.append([float(x), y, 0.0])
    else:
        pts = [[float(x), KEEL + 0.25, 0.0] for x in np.arange(-11.5, 11.0 + STRETCH, 1.0)]
    for y in sorted({WATER + 0.25, WATER + 1.5, MAIN - 0.5}):
        xb, xs = float(interp(y, BOW_X)) - 0.3, stern_x(y) + 0.3
        pts.append([xb, y, 0.0])
        for zz in np.arange(0.0, transom_hw(y) - 0.1, 1.0):
            pts.append([xs, y, zz])
            if zz > 0:
                pts.append([xs, y, -zz])
        for x in np.arange(np.ceil(xs), xb, 1.0):
            w = hw(x, y) - 0.1
            if w > 0.3:
                pts.append([float(x), y, w])
                pts.append([float(x), y, -w])
    # the forecastle overhang (docks, cliffs)
    for x in np.arange(12.0 + STRETCH, FC_TIP, 1.0):
        y = max(6.3, float(fc_under(x)) + 0.3)
        w = hw(x, y) - 0.1
        pts.append([float(x), y, w])
        pts.append([float(x), y, -w])
    pts.append([FC_TIP - 0.3, 6.3, 0.0])
    if FC_LOW < FC_Y0:
        # the raked underside, forward of the stem
        for x in np.arange(float(interp(FC_LOW, BOW_X)) + 0.5, FC_TIP - 0.4, 1.0):
            y = float(fc_under(x)) + 0.3
            w = hw(x, y) - 0.1
            pts += [[float(x), y, w], [float(x), y, -w], [float(x), y, 0.0]]
    if POOP_OVERHANG:
        # the poop's overhang (docks, cliffs)
        y = OH_Y + 0.3
        for x in np.arange(stern_x(y) + 0.3, base_stern_x(y), 1.0):
            w = hw(x, y) - 0.1
            pts += [[float(x), y, w], [float(x), y, -w]]
        for zz in np.arange(0.0, hw(stern_x(y) + 0.3, y) - 0.1, 1.0):
            pts += [[stern_x(y) + 0.3, y, zz], [stern_x(y) + 0.3, y, -zz]]
    return [[round(v, 3) for v in p] for p in pts]


def water_clip(margin=0.7):
    """Rounded box (|u|^4 + |v|^2 < 1) covering the hull at the waterline, checked against the hull outline."""
    bow, stern = float(interp(WATER, BOW_X)), stern_x(WATER)
    cx = (bow + stern) / 2
    while True:     # the box must contain the hull everywhere (the mod measures its own outline from the hull anyway)
        hl = (bow - stern) / 2 + margin
        hwid = max(hw(x, WATER) for x in np.arange(stern, bow, 0.25)) + margin
        if all(((x - cx) / hl) ** 4 + (hw(x, WATER) / hwid) ** 2 < 1 for x in np.arange(stern, bow, 0.25)):
            break
        margin += 0.05
        assert margin < 2, "water clip doesn't fit the hull"
    # the hull's exact half-width below the main deck, for the dry outline the mod fits for Sea (no water drawn in it)
    # (finer along the hull than up it, so the mod finds the transom within an eighth of a block)
    x0, y0, dx, dy = -16.5, KEEL - 0.5, 0.125, 0.25
    xs = np.arange(x0, 17.5 + STRETCH + 1e-9, dx)
    ys = np.arange(y0, MAIN + 1.0 + 1e-9, dy)
    widths = [[round(float(hull_hw(x, y)), 3) if y >= KEEL else 0.0 for x in xs] for y in ys]
    return {"centerX": round(cx, 3), "halfLength": round(hl, 3), "halfWidth": round(hwid, 3), "height": 0.1,
            "dryOutline": {"x0": x0, "dx": dx, "y0": y0, "dy": dy, "nx": len(xs), "ny": len(ys),
                           "halfWidths": [v for row in widths for v in row]}}


def hold_slots(floor_y=None, headroom=2.0, walkways=0.4, per_chest=16, cols=12):
    """
    The hold as one storage, sized from its volume: whole-block chest places on a floor over the ballast at floor_y,
    two chests high under the lower deck, a share of them kept clear for walkways. Rounded down to whole rows of the
    storage dialog.
    """
    floor_y = HOLD_FLOOR + 0.25 if floor_y is None else floor_y
    area = 0.0
    for x in np.arange(-16.0, 16.0 + STRETCH, 1.0):
        w = min(min(hw(xx, floor_y), hw(xx, floor_y + headroom)) for xx in np.linspace(x, x + 1, 9))
        area += 2 * np.floor(max(w, 0.0))
    chests = int(area * 2 * (1 - walkways))
    return (chests * per_chest) // cols * cols, area, chests


def mizzen_foot(x=-12.5, r=0.3):
    """Where the mizzen is stepped: the lowest point on the keelson with room for its heel."""
    for y in np.arange(KEEL, MAIN, 0.05):
        if hw(x, y) > r + 0.2:
            # (GRID: on the keelson, flush with the hold's floor, or over it where the bottom rises)
            return round(float(max(y, HOLD_FLOOR) if GRID else y), 2)
    return KEEL


# ---------------------------------------------------------------- openings in the hull
# windows only in the castle cabins (the hull below the main deck stays closed), each between two frames; their glass
# is our own mesh following the hull's curve and lean (side_windows), like the stern gallery's
if GRID:
    # (0.3.9) two in the poop cabin, one in each frame bay, their jambs on the frames, G 6..7
    SIDE_WINDOWS = [(-13.875, -12.125, 7.5, 8.5), (-11.875, -10.125, 7.5, 8.5)]
    # (0.3.14, the user's note 144: "these windows could be same shape and design as stern windows") the poop cabin's
    # lights as the stern gallery's poop row: each 0.6 wide and 1.5 high (G 5.75..7.25, the stern row's heights, its
    # head in the band's open bay between the beams, under the deck's planks), two in each frame bay at a pitch of 1
    # (centred on the half blocks), so the four in a row with a pilaster between each two and at its ends on the whole
    # x (-14 .. -10) as the transom's (side_lights); inside, the stile in the middle of each bay between its two lights
    # (SIDE_STILES)
    SIDE_WINDOWS = [(xc - 0.3, xc + 0.3, 7.25, 8.75) for xc in (-13.5, -12.5, -11.5, -10.5)]
    if HANSA:
        # (Hansa) each light in one block: the poop cabin's upper row, 7.5..8.5
        # (the mixed ribs: the cell -11..-10 is the rib on line -10's; three lights in the bay between the ribs)
        SIDE_WINDOWS = [(xc - 0.3, xc + 0.3, 7.5, 8.5) for xc in (-13.5, -12.5, -11.5)]
    SIDE_STILES = (-13.0, -11.0) if not HANSA else (-13.0, -12.0)
elif PROPOSAL:
    # two in the poop cabin; none in the great cabin (a deeper ship: they'd be too near the water, the user,
    # 2026-09-30), nor in the forecastle
    SIDE_WINDOWS = [(-12.625, -11.625, 7.5, 8.5), (-11.125, -10.125, 7.5, 8.5)]
else:
    SIDE_WINDOWS = [(-12.625, -11.625, 4.5, 5.5), (-9.625, -8.625, 4.5, 5.5),     # great cabin
                    (-12.625, -11.625, 7.5, 8.5),                                  # poop cabin
                    ]                                                              # (none in the forecastle: the user, 2026-09-30)
# scuppers: holes through the waist's bulwarks at the deck, between the frames, for the water on deck to run off.
# (0.3.8, the agreed redesign) small round ones (SCUPPER_R) through the waterway, lead-lined, a lead lip outside: in each
# gun bay in the quarter block between the port and the frame aft of it, and in the middle of each bay of the waist
# without a port (not the entry ports'); each takes one cell of her side's collision at the deck (the water's way out
# and in). Before, a square notch 0.4 wide in the middle of the bays without a gun.
SCUPPER_R = 0.075
if GRID:
    # (0.3.9, the user's note 61: "place drain holes next to ribs") each just forward of a frame of the waist (on the even
    # x), clear of the entry ports' opening and the guns' ports
    SCUPPERS = [(xf + 0.125, xf + 0.375) for xf in np.arange(0.0, WAIST_FWD - 1.0, FRAME_SPACE)
                if not PORT_X[0] - 0.3 < xf + 0.25 < PORT_X[1] + 0.3]
elif VARIANT == 3:
    _bays = [xc for xc in np.arange(-12.125, 20.0 + STRETCH, 1.5)
             if WAIST_AFT + 0.5 < xc and xc + 0.2 < WAIST_FWD - 1.0 and not PORT_X[0] - 0.3 < xc < PORT_X[1] + 0.3]
    SCUPPERS = [(xc - GUNPORT_W / 2 - 0.25, xc - GUNPORT_W / 2) if any(abs(xc - gx) < 0.5 for gx in GUN_X)
                else (xc - 0.125, xc + 0.125) for xc in _bays]
elif PROPOSAL:
    # (none under a gunport: the port's sill would run into it)
    SCUPPERS = [(xc - 0.2, xc + 0.2) for xc in np.arange(-12.125, 20.0 + STRETCH, 1.5)
                if WAIST_AFT + 0.5 < xc and xc + 0.2 < WAIST_FWD - 1.0 and not PORT_X[0] - 0.3 < xc < PORT_X[1] + 0.3
                and not any(abs(xc - gx) < 0.5 for gx in GUN_X)]
else:
    SCUPPERS = [(xc - 0.2, xc + 0.2) for xc in (-3.125, -1.625, -0.125, 1.375, 2.875, 4.375, 7.375)]
# The stern gallery: rows of tall mullioned lights across the transom, the great cabin's (5) and the poop cabin's (4).
# Their glass is our own mesh (plain, stained glass in the top panes), not a vanilla block.
GALLERY = [(4.0, 5.75, [(-2.375 + k, -1.625 + k) for k in range(5)]),
           (7.25, 8.75, [(-1.5 + 0.8 * k, -0.9 + 0.8 * k) for k in range(4)])]
if GRID:
    # (0.3.9) the poop cabin's at a pitch of 1 (each centred on a block's border: z -1.5, -0.5, 0.5, 1.5)
    GALLERY[1] = (7.25, 8.75, [(-1.8 + k, -1.2 + k) for k in range(4)])
if HANSA:
    # (Hansa: windows a block each) the great cabin's five lights in the block row 4.5..5.5 (centred on the blocks across
    # her already); the poop cabin's three in 7.5..8.5, centred on the blocks at z -1, 0, 1 (carrack-m's four were on the
    # blocks' borders)
    GALLERY = [(4.5, 5.5, [(-2.375 + k, -1.625 + k) for k in range(5)]),
               (7.5, 8.5, [(-1.3 + k, -0.7 + k) for k in range(3)])]
STERN_WINDOWS = [(z0, z1, y0, y1) for y0, y1, lights in GALLERY for z0, z1 in lights]
# (0.3.30, note 237: "4th time gun swivel is according side board angle ... Check all guns firing arcs to allow maximum
# left right up and down based on conflicts with ports") a gun's port square to her side: where her side runs aslant
# (the bow) its jambs are square to the side board (along its normal), not across her, so the inside's opening is that
# much aft of the outside's (PORT_SQUARE), and the gun stands run out along the normal (guns(): its "square" the side
# board's perpendicular): its train is even about it, as far each way as the port lets the barrel
PORT_SQUARE = MODULAR
PORT_AXIS_Y = 0.55          # (gun.AXIS_Y: where along her side's height a port's angle is taken, over its deck)


def port_phi(gx, deck):
    """(0.3.30) How far forward of square out her side's normal points at a gun's port (x gx, on the deck at `deck`),
    degrees, either side; 0 where it's within 3 deg of square (side_angle's rule)."""
    if not PORT_SQUARE:
        return 0.0
    y = deck + PORT_AXIS_Y
    e = 0.05
    a = math.degrees(math.atan(-(hw(gx + e, y) - hw(gx - e, y)) / (2 * e)))
    return 0.0 if abs(a) < 3.0 else round(a * 2) / 2


def port_skew(gx, deck):
    """(0.3.30) How far along x the inside of a gun's port is from its outside: its jambs square to her side board."""
    return -HULL_T * math.sin(math.radians(port_phi(gx, deck)))


def _port_cut(a, b, y0, y1, side, deck):
    c = {"x0": a, "x1": b, "y0": y0, "y1": y1, "side": side}
    dx = port_skew((a + b) / 2, deck)
    if abs(dx) > 1e-6:
        c["x0in"], c["x1in"] = a + dx, b + dx
    return c


SIDE_CUTS = ([{"x0": a, "x1": b, "y0": c, "y1": e, "side": 0} for a, b, c, e in SIDE_WINDOWS] +
             [{"x0": PORT_X[0], "x1": PORT_X[1], "y0": MAIN, "y1": MAIN + WAIST_RAIL, "side": 0}] +
             [{"x0": a, "x1": b, "y0": MAIN, "y1": MAIN + 0.25, "side": 0} for a, b in SCUPPERS] +
             [_port_cut(a, b, MAIN + GUNPORT_Y[0], MAIN + GUNPORT_Y[1], 0, MAIN) for a, b in GUNPORTS] +
             [_port_cut(a, b, c, e, s, c - GUNPORT_Y[0]) for a, b, c, e, s in CASTLE_PORTS])
# ((0.3.29, PORT_PANELS) the castle side ports aren't cut in her planking or her collision: their frame spaces' half
# panels and the ports in them (hull_slots))
STERN_CUTS = [{"z0": a, "z1": b, "y0": c, "y1": e} for a, b, c, e in STERN_WINDOWS]
# ((0.3.25) the stern chasers' ports through the transom, outside and in)
STERN_CUTS += [{"z0": a, "z1": b, "y0": c, "y1": e} for a, b, c, e, _ in STERN_PORTS]

# Masts: x, lower mast (foot, head, radius at foot, at head), topmast (foot, head, r, r), top (platform y, radius)
MASTS = {
    "main": (MAIN_X, (HOLD_FLOOR if GRID else -2.25, 23.6, 0.45, 0.36), (21.2, 34.0, 0.27, 0.13), (21.5, 1.75)),
    "fore": (FORE_X, (mizzen_foot(FORE_X, 0.35) if GRID else -1.5, 20.0, 0.35, 0.27), None, None),
    # stepped on the keel like the others, through the hold, the lower deck and the great cabin
    "mizzen": (-12.5, (mizzen_foot(), 25.5, 0.3, 0.14), None, None),
}
FORE_HEAD = 19.4
MAIN_SHROUDS = tuple(MAIN_X + dx for dx in (-2.4, -1.7, -1.0, -0.3, 0.4))   # where they come down to the channels
if GRID:
    # (0.3.10, the mainmast at 2.5) their feet between the gun ports (guns 1 and 3: ports 0.625..1.375, 2.625..3.375),
    # on a channel above the waist's rail (CHANNEL), the chain plates down to the upper wale between the ports
    MAIN_SHROUDS = (0.0, 0.5, 1.6, 2.0, 2.4)
    # (0.3.10, the user: "main mast ratlines can be a bit back ... with the middle line in the centre of the cannon")
    # centred on the first gun (x 1), 0.7 apart as the 0.3.9 ones; the middle one's chain plate ends on a strap over
    # its port's lintel, the others go down to the wale between the ports
    MAIN_SHROUDS = (-0.4, 0.3, 1.0, 1.7, 2.4)
    if HANSA:
        MAIN_SHROUDS = tuple(x + 1.0 for x in MAIN_SHROUDS)     # (Hansa: with the mainmast, a cell forward)
# the main channel: x0, x1, its underside (GRID: over the rail, G 3.45: the first gun's lid opened 110 deg under it with
# 0.1 to spare, lid_channel_clear; 0.3.9 at the ports' height)
CHANNEL = (-0.8, 2.8, MAIN + 1.45) if GRID else (MAIN_X - 2.7, MAIN_X + 0.7, MAIN + 0.64)
if GRID:
    # (0.3.13, the user's note 132: "Is this as should be?": seen from the waist the channel was a dark bar floating
    # over the rail in front of the shrouds; a channel is bolted to the side under the rail) down against the side, its
    # top 0.01 under the waist rail cap's (MAIN + 1.25), 0.09 thick, its underside 0.15 over the gun ports' lids'
    # hinges (the first gun's lid opens a little over level under it, clear of the gun laid anywhere: lid_channel_clear,
    # lid_clearances); its forward end at the last deadeye's edge (2.52), clear of the third gun's lid
    CHANNEL = (-0.8, 2.52, MAIN + WAIST_RAIL - 0.01 - 0.09)
    if HANSA:
        CHANNEL = (CHANNEL[0] + 1.0, CHANNEL[1] + 1.0, CHANNEL[2])
CHANNEL_T = 0.09 if GRID else 0.12          # its thickness ((0.3.13) a thinner plank: the lid opens a little over level)
TOP_RAIL = 1.3        # how high round the main top's rim it's solid, above its floor (over its breastwork: you can't jump it)
TOP_WALL = 0.95       # the main top's breastwork (a tub of upright boards), above its floor; its capping rail on that
# Yards of the square sails: mast x, height, half length, radius at the middle
YARDS = {"main-course": (MAIN_X, 20.25, 7.3, 0.22), "main-topsail": (MAIN_X, 31.0, 4.4, 0.15),
         "fore-course": (FORE_X, 17.25, 5.9, 0.19)}
TOPSAIL_FOOT = 24.3   # above the eyes of someone standing in the main top (floor 21.5)
LATEEN_A, LATEEN_B = np.array([-8.8, 12.9, 0.5]), np.array([-17.2, 25.6, 0.5])   # lateen yard ends
# The yards turn by the crew's trim, which the mod gives in radians: this many degrees per radian turns them by exactly
# that (a moving part turns Angle degrees per unit of its value).
BRACE_DEG_PER_RAD = round(180 / math.pi, 5)
MAX_BRACE = 45.0      # the mod's default SailingDef.MaxBrace, degrees
MAX_SHEET = 80.0      # how far the mod swings the lateen out at most, degrees


# The bowsprit (0.3.7, the user's notes 20 and 21: "should probably go inside the floor?", "supported by nothing"): its
# heel stepped on the main deck in the forecastle against the foremast's fore side, held down by bitts; it rises
# forward through the forecastle deck (its partners) and rests on a saddle on the stem head at the forecastle's tip,
# lashed down there by the gammoning, some 27° steeved (a carrack's bowsprit stood steep). Its heel is as far forward
# as its butt clears the mast (0.3 thick, the mast 0.33 at the deck); until then it lay on a chock on the forecastle
# deck, passing a metre and a half over the tip.
BOWSPRIT_HEEL = (FORE_X + 0.485, MAIN + 0.3)
SADDLE = (FC_TIP - 0.1, QUARTER + 1.0 + 0.35 + CASTLE_UP)       # where it rests on the stem head (the saddle's top)
BOWSPRIT_END_X = 24.0 + STRETCH


def _bowsprit_line():
    """Its heel and its end: through the heel and resting on the saddle (its underside on the saddle's top)."""
    x0, y0 = BOWSPRIT_HEEL
    slope = 0.5
    for _ in range(20):
        r = 0.3 - 0.15 * (SADDLE[0] - x0) / (BOWSPRIT_END_X - x0)
        slope = (SADDLE[1] + r * math.sqrt(1 + slope * slope) - y0) / (SADDLE[0] - x0)
    return np.array([x0, y0, 0.0]), np.array([BOWSPRIT_END_X, y0 + slope * (BOWSPRIT_END_X - x0), 0.0])


BOWSPRIT_A, BOWSPRIT_B = _bowsprit_line()


def bowsprit_y(x):
    return float(BOWSPRIT_A[1] + (BOWSPRIT_B[1] - BOWSPRIT_A[1]) * (x - BOWSPRIT_A[0]) / (BOWSPRIT_B[0] - BOWSPRIT_A[0]))


def bowsprit_r(x):
    return 0.3 - 0.15 * (x - BOWSPRIT_A[0]) / (BOWSPRIT_B[0] - BOWSPRIT_A[0])


def lateen_pivot_y():
    """Where the lateen yard crosses the mizzen: it swings about the mast there."""
    mx = MASTS["mizzen"][0]
    f = (mx - LATEEN_A[0]) / (LATEEN_B[0] - LATEEN_A[0])
    return round(float(LATEEN_A[1] + (LATEEN_B[1] - LATEEN_A[1]) * f), 4)


def seg_dist(p1, q1, p2, q2):
    """Closest distance between segments p1-q1 and p2-q2."""
    p1, q1, p2, q2 = (np.asarray(v, float) for v in (p1, q1, p2, q2))
    d1, d2, r = q1 - p1, q2 - p2, p1 - p2
    a, e, f = d1 @ d1, d2 @ d2, d2 @ r
    c, b = d1 @ r, d1 @ d2
    den = a * e - b * b
    s = np.clip((b * f - c * e) / den, 0, 1) if den > 1e-12 else 0.0
    t = (b * s + f) / e if e > 1e-12 else 0.0
    if t < 0:
        t, s = 0.0, np.clip(-c / a, 0, 1)
    elif t > 1:
        t, s = 1.0, np.clip((b - c) / a, 0, 1)
    return float(np.linalg.norm((p1 + d1 * s) - (p2 + d2 * t)))


def turned(p, pivot, deg):
    """p turned about the vertical through pivot, + turning the starboard end forward (like the mod's brace)."""
    a = math.radians(deg)
    x, z = p[0] - pivot[0], p[2] - pivot[2]
    return np.array([pivot[0] + x * math.cos(a) + z * math.sin(a), p[1], pivot[2] - x * math.sin(a) + z * math.cos(a)])


def rig_check(d):
    """
    The yardarms against the standing rigging at full brace: the outer 70 % of each arm must stay clear of every
    shroud, backstay and stay (near the mast a yard braced sharp up touches the lee shrouds, as on real ships). Writes
    what the offline test needs, and lowers the brace the mod allows if the square yards wouldn't clear.
    """
    yards = []
    for code, (mx, y, half, r) in YARDS.items():
        arms = [((mx + 0.5, y, s * half * 0.3), (mx + 0.5, y, s * half), r * 0.7) for s in (1, -1)]
        yards.append({"part": f"yard-{code}", "pivot": (mx, y, 0.0), "arms": arms, "max": MAX_BRACE})
    mz = MASTS["mizzen"][0]
    piv = np.array([mz, lateen_pivot_y(), 0.0])
    u = (LATEEN_B - LATEEN_A) / np.linalg.norm(LATEEN_B - LATEEN_A)
    lat_arms = [(tuple(piv + u * 1.6 + np.array([0, 0, 0.5])), tuple(LATEEN_B), 0.1),
                (tuple(piv - u * 1.6 + np.array([0, 0, 0.5])), tuple(LATEEN_A), 0.12)]
    yards.append({"part": "yard-mizzen-lateen", "pivot": tuple(piv), "arms": lat_arms, "max": MAX_SHEET})

    def clear_up_to(y):
        worst_ok = 0.0
        for deg in np.arange(0, y["max"] + 0.01, 1.0):
            for sgn in (1, -1):
                for a, b, ry in y["arms"]:
                    ta, tb = turned(a, y["pivot"], sgn * deg), turned(b, y["pivot"], sgn * deg)
                    for sa, sb, rs in d.standing:
                        if seg_dist(ta, tb, sa, sb) < ry + rs + 0.03:
                            return worst_ok, deg
            worst_ok = deg
        return worst_ok, None

    report = {}
    square_max = MAX_BRACE
    for y in yards:
        ok, hit = clear_up_to(y)
        report[y["part"]] = (ok, hit)
        if y["part"] != "yard-mizzen-lateen" and hit is not None:
            square_max = min(square_max, ok)
    for part, (ok, hit) in report.items():
        print(f"rig check: {part} clears the standing rigging up to {ok:.0f} deg" + ("" if hit is None else f" (touches at {hit:.0f})"))
    d.props["rigCheck"] = {
        "standing": [[round(float(v), 3) for v in (*a, *b, r)] for a, b, r in d.standing],
        "yards": [{"part": y["part"], "pivot": [round(float(v), 4) for v in y["pivot"]], "max": y["max"],
                   "arms": [[round(float(v), 3) for v in (*a, *b, r)] for a, b, r in y["arms"]]} for y in yards],
    }
    if square_max < MAX_BRACE:
        d.props.setdefault("sailing", {})["maxBrace"] = float(square_max)
        # (0.3.9, hull R) braced no further than this, the square sails fill only with the apparent wind at least
        # 90 - brace + 5 off the bow, and close-hauled the apparent wind is some 8 degrees forward of the true: she lies
        # no closer than that (36 degrees: 67, about six points, a carrack's own)
        d.props["sailing"]["noGo"] = float(max(62.0, 103.0 - square_max))
    # the lateen swings on the lee side of the mizzen, where its shrouds stop it: it's drawn no further than that, and
    # the crew trims it no further (the mod's SailingDef.MaxSheet)
    lat_ok = report["yard-mizzen-lateen"][0]
    if lat_ok < MAX_SHEET:
        for p in d.moving_parts:
            if p["code"] == "yard-mizzen-lateen":
                p["limit"] = float(lat_ok - 1)
        d.props.setdefault("sailing", {})["maxSheet"] = float(min(30.0, lat_ok - 1))


def plank_textures(wood, overlay=""):
    return [f"game:block/wood/planks/{wood}{i}{overlay}" for i in range(1, 5)]


# ---------------------------------------------------------------- small mesh helpers

def rope_path(d, pts, r=0.035, group="exterior", mat="rope"):
    pts = [np.asarray(p, float) for p in pts]
    for a, b in zip(pts[:-1], pts[1:]):
        d.add_cylinder(group, mat, a, b, r, r, sides=5, seg=2.0, caps=False)


def ring_points(c, radius, axis="y", n=14, a0=0.0):
    c = np.asarray(c, float)
    pts = []
    for k in range(n + 1):
        a = a0 + 2 * math.pi * k / n
        ca, sa = math.cos(a) * radius, math.sin(a) * radius
        off = {"y": (ca, 0, sa), "z": (ca, sa, 0), "x": (0, ca, sa)}[axis]
        pts.append(c + np.array(off))
    return pts


def helix(cx, cz, radius, y0, y1, a0, turns, n_per_turn=18):
    """Points round an upright (cx, cz) from y0 to y1, `turns` times from the angle a0 (turns < 0: the other way)."""
    # (the points by the turns' size: a turn the other way had 2 points until 0.3.7, a straight rope through the post)
    n = max(2, int(round(abs(turns) * n_per_turn)))
    return [np.array([cx + radius * math.cos(a0 + 2 * math.pi * turns * k / n), y0 + (y1 - y0) * k / n,
                      cz + radius * math.sin(a0 + 2 * math.pi * turns * k / n)]) for k in range(n + 1)]


def disk(d, group, mat, c, nrm, r, sides=10, grain=None, a0=0.0):
    """A flat round disk facing nrm: the dark inside of a hole, a pipe's mouth, a compass card, a wheel's boss. One flat
    face textured by its plane (Design.add_planar: 0.3.8; a fan of triangles before, each with the texture turned its
    own way round). grain: which way the wood's grain runs across it."""
    c = np.asarray(c, float)
    n = np.asarray(nrm, float) / np.linalg.norm(nrm)
    e1 = np.cross(n, [0, 1.0, 0] if abs(n[1]) < 0.9 else [1.0, 0, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(n, e1)
    pts = [c + r * (math.cos(a0 + 2 * math.pi * k / sides) * e1 + math.sin(a0 + 2 * math.pi * k / sides) * e2)
           for k in range(sides)]
    d.add_planar(group, mat, pts, n, u_dir=grain, centre=c)


def annulus(d, group, mat, c, nrm, r0, r1, sides=10, grain=None):
    """A flat ring facing nrm between radii r0 and r1 (a pipe's lip, a boss's face round its hole), each piece textured
    by its plane (Design.add_planar)."""
    c = np.asarray(c, float)
    n = np.asarray(nrm, float) / np.linalg.norm(nrm)
    e1 = np.cross(n, [0, 1.0, 0] if abs(n[1]) < 0.9 else [1.0, 0, 0])
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(n, e1)
    for k in range(sides):
        a0, a1 = 2 * math.pi * k / sides, 2 * math.pi * (k + 1) / sides
        p0, p1 = math.cos(a0) * e1 + math.sin(a0) * e2, math.cos(a1) * e1 + math.sin(a1) * e2
        d.add_planar(group, mat, [c + p0 * r0, c + p1 * r0, c + p1 * r1, c + p0 * r1], n, u_dir=grain, centre=c)


def tangent_point(cx, cz, r, px, pz, side=1):
    """Where a line from (px, pz) touches the circle (cx, cz, r); side picks one of the two. Returns (x, z, angle)."""
    dx, dz = px - cx, pz - cz
    dist = math.hypot(dx, dz)
    phi = math.atan2(dz, dx)
    a = phi + side * math.acos(min(1.0, r / dist))
    return cx + r * math.cos(a), cz + r * math.sin(a), a


def block_at(d, group, p, size=(0.16, 0.2, 0.12)):
    """A rope block (a pulley in its shell) hanging at p."""
    p = np.asarray(p, float)
    d.add_box_mesh(group, "timber", p, size)
    d.add_box_mesh(group, "iron", p + np.array([0, size[1] / 2 + 0.03, 0]), (0.04, 0.06, 0.04))


def transom_frame(y0, y1):
    """Outward normal and the up direction in the raked plane of the transom between y0 and y1."""
    s = (stern_x(y1) - stern_x(y0)) / (y1 - y0)
    up = np.array([s, 1.0, 0.0])
    up /= np.linalg.norm(up)
    nrm = np.array([-1.0, s, 0.0])
    nrm /= np.linalg.norm(nrm)
    return nrm, up


# ==================================================================== (0.3.16) carrack-m: the modular pieces
def mast_r(name, y):
    """A mast's radius at height y (its lower mast's taper)."""
    mx, (y0, y1, r0, r1), _, _ = MASTS[name]
    return r0 + (r1 - r0) * (y - y0) / (y1 - y0)


def tread_top(rise, k):
    """(0.3.16) The mod's tread k's top over the deck (PiecePlanner.TreadTop): the rise shared over 8, on the quarter grid."""
    return math.floor(rise * (k + 1) / 8 * 4 + 0.5) / 4


# (0.3.18, §7.1 as amended) its feet in from the foot cell's near edge (the cell's middle); its run to the top (~63°)
LADDER_IN, LADDER_RUN = 0.5, 1.5


def ladder_line(x_low, direction, floor_y, rise=3.0):
    """(0.3.18, docs/modular-pieces.md §7.1) A placed stair's ladder: from its feet on the deck, LADDER_IN in from its foot
    cell's near edge x_low, up `direction` along x to its head cell's far edge (x_low + 2 along) at the deck above, where
    it hooks over the coaming: ((x, y) of the feet, (x, y) of the top)."""
    return (x_low + direction * LADDER_IN, floor_y), (x_low + direction * 2.0, floor_y + rise)


def modular_stair_steps(x_low, direction, floor_y, z0, z1, rise=3.0):
    """(0.3.16) Where a placed stair's treads are (as d.stairs' steps: xa, xb, top, z0, z1). (0.3.18) The companion
    ladder-stair's: flat treads every rise / 10 up its line (ladder_line), each 0.2 deep along it, between its stringers
    1/16 in from the cell's sides."""
    (xf, yf), (xt, yt) = ladder_line(x_low, direction, floor_y, rise)
    steps = []
    for k in range(1, 10):
        f = k / 10
        xm = xf + (xt - xf) * f
        steps.append((xm - 0.1, xm + 0.1, yf + (yt - yf) * f, z0 + 0.0625, z1 - 0.0625))
    return steps


# (0.3.19, docs/modular-pieces.md §8) the walked stair: 1 wide x 3 long (foot, middle, head toward `up`), its risers a
# voxel (rise 3: 12 treads of 0.25 x 0.25, 45°), the top tread flush with the deck above at the head cell's far edge;
# a man's height (the deck mover's 1.85) over every tread and over the 0.6 before it; the deck above (its coaming or
# beam) reaching back over the head cell at most a tread (0.25)
FLIGHT_RUN, FLIGHT_RISER, FLIGHT_ROOM, FLIGHT_BEHIND, FLIGHT_HALF = 3.0, 0.25, 1.85, 0.6, 0.3


def flight_treads(rise=3.0, run=FLIGHT_RUN):
    """(0.3.19, ShipPieces.FlightTread) A walked stair's treads: (u0, u1, top over its deck) along its run from the foot
    cell's near edge. ((0.3.23) its run: 3 (the long stair, 45°) or 2 (the steep one, rise 3 over 2: twelve treads 1/6
    deep))"""
    n = max(2, int(math.ceil(rise / FLIGHT_RISER - 1e-6)))
    dd = run / n
    return [((k - 1) * dd, k * dd, rise * k / n) for k in range(1, n + 1)]


def flight_columns(rise=3.0, run=FLIGHT_RUN):
    """(0.3.23, docs/modular-pieces.md §10.1) A ship's stair's collision along its run, (u0, u1, top): the long one's a
    column a tread (12 of 0.25); the steep one's (2 long, rise 3) 8 columns a voxel long, their tops 0.25 0.75 1 1.5 1.75
    2.25 2.5 3 (its 12 treads of 1/6 drawn over them)."""
    if abs(run - 2.0) < 1e-6 and abs(rise - 3.0) < 1e-6:
        tops = (0.25, 0.75, 1.0, 1.5, 1.75, 2.25, 2.5, 3.0)
        return [(0.25 * k, 0.25 * (k + 1), t) for k, t in enumerate(tops)]
    return flight_treads(rise, run)


def flight_steps(x_low, direction, floor_y, z0, z1, rise=3.0, run=FLIGHT_RUN):
    """(0.3.19) Where a walked stair's treads are (as d.stairs' steps: xa, xb, top, z0, z1): each a voxel thick, its width
    1/16 in from the cell's sides; x_low the foot cell's near edge, `direction` the way up along x. ((0.3.23) its
    collision's columns: flight_columns)"""
    steps = []
    for u0, u1, top in flight_columns(rise, run):
        xa, xb = sorted((x_low + direction * u0, x_low + direction * u1))
        steps.append((xa, xb, floor_y + top, z0 + 0.0625, z1 - 0.0625))
    return steps


MOD_DOOR = "game:door-sleek-windowed-oak"       # (the doors the carrack's fronts had)
# (0.3.28, note 231: "move default stair location 1 block to right aka center line, and doors to cabin move where are
# stairs now") the poop stair on the centreline (z -0.5..0.5), the poop cabin's door where it stood (z 0.5..1.5)
POOP_STAIR_K, POOP_DOOR_K = 0, 1


def modular_layout(d):
    """
    (0.3.16, notes 154-158; docs/modular-pieces.md) carrack-m's placed pieces, laid by the mod on a new ship: d.modular
    (the definition's `modularPieces`, heights as drawn here: Design.translate_y moves them), d.wells (`wells`), and
    d.fixed_rails (bits of the castle fronts' railings where no piece has a place, drawn and solid as hers: (0.3.17,
    docs/modular-pieces.md §6) none any more, the corners and the quarterdeck's front by the binnacle pieces too).

    - the cabin fronts on their grid lines (the great cabin's at x -3, the poop cabin's at -9, the forecastle's at 15),
      two rows of partitions over the deck (the breast beam over them closes the band; (0.3.23) no breast beams: a third,
      partial row under the castle deck's edge closes it), a door in the middle of each,
      the window to port in the great cabin's and the poop cabin's (row 1 over a partition); the cells at the leaning
      side partly free of her: partitions cut to what's free (the mod's wedge places);
    - the stairwell (option A's companion, a block in from the side): its inboard wall on z 2.5 and its after wall on x
      -7 (three rows: the band under the quarterdeck's beams partly free), its door from the waist in the front at z 3
      (swinging into the landing, lying along the landing's inboard wall open) and from the cabin in the after wall at
      z 4 (by the side: the strip of deck along the well leads forward to the landing);
    - the stairs: the quarterdeck's, the poop's and the forecastle's in the line z 1.5..2.5 at the fronts ((0.3.19, §8)
      walked stairs, 1 x 3), the companion (lower deck -> main deck) and the hold's (hold -> lower deck) ladder-stairs,
      1 x 2, each rising 3;
    - railings: along the walked stairs (raked: the quarterdeck stair's inboard side, both sides of the poop's and the
      forecastle's; none beside the ladders), round the companion's well on the main deck (the strip by the side) and the
      hold's on the lower deck, and the castle fronts' breastworks on the decks over them.
    """
    sol_lut = np.array([False] + [m.solid for m in d.materials[1:]])
    solid = sol_lut[d.grid]
    V = 0.25

    def idx(v, ax):
        return int(math.floor((v - d.o[ax]) / V + 1e-9))

    def solid_at(x, y, z):
        i, j, k = idx(x, 0), idx(y, 1), idx(z, 2)
        if not (0 <= i < d.n[0] and 0 <= j < d.n[1] and 0 <= k < d.n[2]):
            return True
        return bool(solid[i, j, k])

    def inside_her(x, y, z):
        return abs(z) < hw(x, y) - 0.05

    def free_count(line, along_x, a0, ry, side, top):
        """How many of the 4 x 4 sub-blocks of the border's layer on `side` (+1/-1) are free of her (and in her)."""
        layer = line + side * V / 2
        n = 0
        for a in range(4):
            for b in range(4):
                al, y = a0 + (a + 0.5) * V, ry + (b + 0.5) * V
                if y > top + 1e-6:
                    continue
                p = (al, y, layer) if along_x else (layer, y, al)
                if not solid_at(*p) and inside_her(*p):
                    n += 1
        return n

    out = []

    def border(kind, line, along, mid, y, **kw):
        at = [mid, y, line] if along == "x" else [line, y, mid]
        e = {"kind": kind, "at": [round(float(v), 4) for v in at], "along": along}
        e.update(kw)
        out.append(e)

    # ---- the cabin fronts: (room, line x, deck, anchor side, top of the room, the segments' kinds)
    gc_front = {0: ("door", {"code": "door-greatcabin", "block": MOD_DOOR, "open": False}),
                3: ("door", {"code": "door-companion-cabin", "block": MOD_DOOR, "open": False}), -2: ("window", {})}
    if COMPANION_WAIST:
        # ((0.3.29, notes 230, 233) the companion on the centreline (k 0): the front's segment there open into it (only the
        # third row under the quarterdeck's edge), the cabin's door a cell to port (k -1); the old stairwell's door (k 3)
        # a partition again (new entries: "since")
        gc_front = {-1: ("door", {"code": "door-greatcabin", "block": MOD_DOOR, "open": False}), 0: ("open", {}),
                    -2: ("window", {}), 3: ("partition", {"since": LAYOUT_REV})}
    fronts = (
        ("greatcabin", FRONT_AFT, MAIN, -1, QUARTER - DECK_T, gc_front),
        ("poopcabin", POOP_FRONT - 0.25, QUARTER, -1, POOP - DECK_T,
         {POOP_DOOR_K: ("door", {"code": "door-poopcabin", "block": MOD_DOOR, "open": False}), -2: ("window", {})}),
        ("forecastle", FRONT_FWD, MAIN, 1, QUARTER - DECK_T,
         {0: ("door", {"code": "door-forecastle", "block": MOD_DOOR, "open": False})}),
    )
    def fmt3(v):
        t = f"{round(float(v), 3):.3f}".rstrip("0").rstrip(".")
        return "0" if t in ("-0", "") else t

    def old_key(room, kind, y, k):
        """((0.3.28, notes 225, 227) a front's piece moved to the edge's line keeps the key it had on its old line (the
        mod's default: kind@x,y,z,along, as written: y lowered by WATER), so a ship laid before doesn't get a second front)"""
        old = {"greatcabin": WAIST_AFT - 0.25, "forecastle": WAIST_FWD + 0.25}.get(room)
        if old is None or not EDGE_OUT:
            return {}
        return {"code": f"{kind}@{fmt3(old)},{fmt3(y - WATER)},{fmt3(k)},z"}

    for room, line, deck, side, top, special in fronts:
        for k in range(-7, 8):
            # ((0.3.18, note 165) the forecastle's front all pieces: no fixed wall behind a hearth any more)
            kind, extra = special.get(k, ("partition", {}))
            if kind == "door":
                border("door", line, "z", float(k), deck, **extra)
                continue
            if kind == "open":
                continue
            for row in (0, 1):
                if free_count(line, False, k - 0.5, deck + row, side, top) < 2:
                    continue
                kd = "window" if kind == "window" and row == 1 else "partition"
                border(kd, line, "z", float(k), deck + row, **old_key(room, kd, deck + row, k), **extra)
        # ((0.3.23, notes 167/168: no breast beams) a third row of partitions over the two, in the band under the castle
        # deck's edge (partial: up to the deck's underside), over the doors and the stairs' heads too: it closes the room)
        for k in range(-7, 8):
            if special.get(k, ("",))[0] == "open":
                continue        # ((0.3.29) over the companion: its well's cell under it, nothing to stand on)
            if free_count(line, False, k - 0.5, deck + 2, side, top) >= 2:
                border("partition", line, "z", float(k), deck + 2, **old_key(room, "partition", deck + 2, k))
    # ---- the stairwell's walls (three rows: the band under the quarterdeck's beams partly free)
    if STAIRWELL:
        sx0, sw_z = STAIRWELL[0] + 0.25, STAIRWELL[3]
        for xm in np.arange(STAIRWELL[1] - 0.5, sx0, -1.0):
            for row in (0, 1, 2):
                if free_count(sw_z, True, xm - 0.5, MAIN + row, -1, QUARTER - DECK_T) >= 2:
                    border("partition", sw_z, "x", float(xm), MAIN + row)
        for k in range(int(sw_z + 0.5), 8):
            rows = (0, 1, 2)
            if k == 4:
                border("door", sx0, "z", 4.0, MAIN, code="door-companion-stair", block=MOD_DOOR, open=False)
                rows = (2,)
            for row in rows:
                if free_count(sx0, False, k - 0.5, MAIN + row, -1, QUARTER - DECK_T) >= 2:
                    border("partition", sx0, "z", float(k), MAIN + row)
    if COMPANION_WAIST:
        # ---- ((0.3.29, notes 230, 233) the companion beside the quarterdeck stair, walled in: its sides on the lines z
        # -0.5 (anchored on the deck to port, "+z" its anchor's way from the line: -z) and z 0.5 (on the quarterdeck stair's
        # cells: the well's own are out, the twin there), x -3..0, two rows on the waist and three in the great cabin (up
        # to the quarterdeck's underside); its after end on x -3 in the cabin (anchored aft), three rows; at its head (x
        # 0) a door anchored forward, swinging out onto the waist; over the two waist cells a hood's roof (the boards on
        # the walls' tops). All new in LAYOUT_REV)
        cx0, cx1, cz0, cz1 = COMPANION
        rev = {"since": LAYOUT_REV}
        for xm in np.arange(cx0 + 0.5, cx1, 1.0):
            rows = (0, 1, 2) if xm < FRONT_AFT else (0, 1)
            for zl, anchor in ((cz0, "-z"), (cz1, "+z")):
                for row in rows:
                    if free_count(zl, True, xm - 0.5, MAIN + row, 1 if anchor == "+z" else -1, QUARTER - DECK_T) >= 2:
                        border("partition", zl, "x", float(xm), MAIN + row, anchor=anchor, **rev)
        for row in (0, 1, 2):
            for zm in np.arange(cz0 + 0.5, cz1, 1.0):
                if free_count(cx0, False, zm - 0.5, MAIN + row, -1, QUARTER - DECK_T) >= 2:
                    border("partition", cx0, "z", float(zm), MAIN + row, anchor="-x", **rev)
        border("door", cx1, "z", (cz0 + cz1) / 2, MAIN, code="door-companion", block=MOD_DOOR, open=False, anchor="+x",
               **rev)
        for xm in np.arange(FRONT_AFT + 0.5, cx1, 1.0):
            out.append({"kind": "panel", "at": [float(xm), MAIN + 2.0 + 0.125, (cz0 + cz1) / 2], "hood": True,
                        "block": "game:plankslab-oak-down-free", "code": f"hood-companion-{int(math.floor(xm))}", **rev})
    # ---- the stairs (their foot cell's middle, the way up, the rise)
    # (0.3.19, docs/modular-pieces.md §8) the castle stairs walked stairs, 1 x 3, each head cell's far edge on its castle
    # front's line (no well: the castle deck's edge is on that line), a cell longer from the fronts than the 0.3.18
    # ladder-stairs (their codes kept: a ship laid with ladders keeps them); the companion and the hold's ladder-stairs
    # explicit `ladder` entries in their 2-cell wells
    # (0.3.23, note 173 and the new stair contract, docs/modular-pieces.md: "length" 3 or 2, the ship's stairs
    # hansa:shipstair-long / -steep with their handrails built in) the castle stairs stay 3 long (45°, up to their
    # decks' edges, no well); the companion and the hold's ladder-stairs ("they don't work well") become walked steep
    # stairs, 2 long (rise 3 over 2, ~56°), each through a 3 x 1 well over the cell before its foot and its two cells
    # (COMPANION, HOLD_STAIR): the companion's foot cell -6..-5 on the lower deck climbing forward to the landing inside
    # the great cabin's waist door (-4..-3 on the main deck); the hold's foot cell -5..-4 on the hold's floor (z 0.5..1.5)
    # climbing aft to the lower deck at -7..-6, two cells inboard of the companion's foot. The way below: main deck ->
    # companion -> lower deck, two cells inboard -> hold stair -> hold; the hatchways are hatches only (their ladders gone)
    # ((0.3.24, user layout: the user's own stairs on their carrack-m, adopted) the castle stairs steep ones (2 long), a
    # block inboard of 0.3.23's (z 0.5..1.5: each beside its front's door, on the centreline), each up to its deck's
    # edge: the poop's head at the poop front's line (-9), the quarterdeck's and the forecastle's at their decks' new
    # edges (notes 184, 185: QD_EDGE -2, FC_EDGE 14; the user laid theirs a block in, at the old edges -3 and 15: the
    # same place to their decks)
    stairs = (("stair-quarterdeck", 2, (QD_EDGE + 1.5, MAIN, 1.0), "-x"),
              ("stair-poop", 2, (POOP_FRONT - 0.25 + 1.5, QUARTER, float(POOP_STAIR_K)), "-x"),
              ("stair-forecastle", 2, (FC_EDGE - 1.5, MAIN, FC_STAIR_Z), "+x"),
              ("stair-companion", 2, (COMPANION_TOP - 1.5, LOWER, (COMPANION[2] + COMPANION[3]) / 2), "+x"),
              ("stair-hold", 2, (HOLD_STAIR_FOOT - 0.5, HOLD_FLOOR, (HOLD_STAIR[2] + HOLD_STAIR[3]) / 2), "-x"))
    for code, n, at, up in stairs:
        out.append({"kind": "stair", "at": [round(float(v), 4) for v in at], "up": up, "rise": 3.0, "length": n,
                    "code": code})
    # ---- (0.3.23) no railings beside the stairs (their handrails are built in: the mod refuses a railing on a border
    # along a stair's cells on its deck)
    # ---- railings round the wells on the decks over them: the companion's on the main deck along the strip by the side
    # (z 3.5, -7..-4), its after end (x -7) the stairwell's after wall, its inboard side (z 2.5) the stairwell's inboard
    # wall, its forward end (the landing, where you step off) open; the hold stair's on the lower deck along both its
    # sides (z 0.5 and 1.5, -6..-3) and across its forward end (x -3), its after end (where you step off onto the lower
    # deck) open
    for xm in () if COMPANION_WAIST else np.arange(COMPANION[0] + 0.5, COMPANION[1], 1.0):
        border("railing", COMPANION[3], "x", float(xm), MAIN)
    hx0s, hx1s, hz0s, hz1s = HOLD_STAIR
    comp_cells = (COMPANION_TOP - 2.0, COMPANION_TOP)       # (the companion stair's cells on the lower deck)
    for zl in (hz0s, hz1s):
        for xm in np.arange(hx0s + 0.5, hx1s, 1.0):
            if abs(zl - COMPANION[2]) < 1e-6 and comp_cells[0] < xm < comp_cells[1]:
                continue            # (beside the companion stair: its handrails)
            border("railing", zl, "x", float(xm), LOWER)
    for zm in np.arange(hz0s + 0.5, hz1s, 1.0):
        border("railing", hx1s, "z", float(zm), LOWER)
    # ---- the castle fronts' breastworks on the decks over the fronts: a railing piece on every segment of the line
    # whose layer is free of her (out to her sides, the corners cut to what's free: (0.3.17, docs/modular-pieces.md
    # §6) open decks have partial places too, a railing stands on one; and before the binnacle and the wheel, whose
    # margins no longer keep the wall places), each stair's head (z 1.5..2.5) open; the lines open in her collision
    d.fixed_rails = []
    # ((0.3.24, user layout) the stairs' heads at z 0.5..1.5 (k 1) left open; the quarterdeck's and the forecastle's
    # breastworks on their decks' new edges, QD_EDGE and FC_EDGE)
    for line, deck, side in ((QD_EDGE, QUARTER, -1), (POOP_FRONT - 0.25, POOP, -1), (FC_EDGE, QUARTER, 1)):
        for k in range(-7, 8):
            if k == (int(FC_STAIR_Z) if line == FC_EDGE else POOP_STAIR_K if deck == POOP else 1):
                continue
            if free_count(line, False, k - 0.5, deck, side, deck + 1.0) >= 2:
                border("railing", line, "z", float(k), deck)
    # ---- the wells: the companion's in the main deck, the hold stair's in the lower deck (the decks' thickness)
    # (0.3.18, §7.1: exactly the stair's 2 cells; the castle stairs climb onto their decks' edges, none over them)
    # ((0.3.23) 3 x 1 each: the cell before the stair's foot and its two cells)
    d.wells = [{"code": "companion", "box": [COMPANION[0], MAIN - DECK_T, COMPANION[2], COMPANION[1], MAIN, COMPANION[3]]},
               {"code": "hold", "box": [hx0s, LOWER - DECK_T, hz0s, hx1s, LOWER, hz1s]}]
    # ---- (0.3.20, step B, docs/slots.md §5) the hatches over the hatchways' openings (3 x 3 empty deck cells each,
    # deck_slots), flush with the decks: a cargo hatch (a solid trapdoor's) over the hold's, a glazed one (a window
    # trapdoor's) over the waist's, shut
    ox0, ox1, oz0, oz1 = OPENING
    for code, deck, block in (("hatch-hold", LOWER, "game:trapdoor-solid-oak-1"),
                              ("hatch-waist", MAIN, "game:trapdoor-window-oak-1")):
        out.append({"kind": "hatch", "at": [(ox0 + ox1) / 2, deck, (oz0 + oz1) / 2],
                    "size": [int(round(ox1 - ox0)), int(round(oz1 - oz0))], "block": block, "open": False, "code": code})
    # ---- (0.3.18, note 165; §7.2) the hearth, the oven and the hearth's flue up through the forecastle deck
    if PIECE_HEARTH:
        (hx, hz, hf), (ox, oz, of) = HEARTH_PIECE, OVEN_PIECE
        out.append({"kind": "hearth", "at": [hx, MAIN, hz], "facing": hf, "code": "hearth"})
        out.append({"kind": "oven", "at": [ox, MAIN, oz], "facing": of, "code": "oven"})
        if HEARTH_FLUE:
            out.append({"kind": "flue", "at": [hx, MAIN, hz], "code": "flue-forecastle"})
    d.modular = out
    # the doorways (the fit checks: nothing drawn or solid in them, either side of the line)
    for e in out:
        if e["kind"] == "door":
            x, y, z = e["at"]
            if e["along"] == "z":
                d.door_boxes.append((e["code"], (x - 0.13, y + 0.03, z - 0.47), (x + 0.13, y + 1.97, z + 0.47)))
            else:
                d.door_boxes.append((e["code"], (x - 0.47, y + 0.03, z - 0.13), (x + 0.47, y + 1.97, z + 0.13)))
    kinds = {}
    for e in out:
        kinds[e["kind"]] = kinds.get(e["kind"], 0) + 1
    print("modular pieces:", ", ".join(f"{v} {k}s" for k, v in kinds.items()), f"; {len(d.fixed_rails)} fixed rail bits")


def deck_slots(d):
    """
    (0.3.20, step B, docs/slots.md §1-§2) carrack-m's deck cells as slots: every whole 1 x 1 cell of planking on the
    block grid (x lines at whole x, z lines at k + 0.5) of the lower, main, quarter, forecastle and poop decks that isn't
    fixed; the hatchways' and the wells' cells as empty-start slots. Fixed (not listed): a cell not all planking (partial
    by her sides, a deck's ends, the castle fronts' boards), outside her, with anything of hers standing on it (collision
    over it: masts, capstan, pump, wheel, binnacle, bitts, heads...) or a fitting's mesh on it or hanging under it (a
    lantern hook, a mast's coat, the cable, a frame standing inside the bulwark...: anything but the beams, carlings,
    headers and knees under it), fixed structure under it, a mast's partners, the tiller's sweep and the rudder head under
    it, fixed ladders' ends, the guns' working room (the mod's MountLayout.GunClear boxes, their sel and box, and the gun
    captain's cell) and the entry gates' swing. Sets d.props["deckSlots"], d.slot_grid (each voxel's slot or -1), d.slot_fill
    (the planking's material, put into the empty-start cells for drawing) and d.slot_report.
    """
    import collections
    V = 0.25
    deck_mid = d.mid("deck")
    g = d.grid
    opaque = np.array([False] + [m.opaque for m in d.materials[1:]])
    hull_in = d.hide_applies
    ox, oy, oz = (float(v) for v in d.o)
    # ((0.3.27, note 218: "also break in texture here and aft floor") the castle decks' rows over the waist, between their
    # fronts' lines and their edges (QD_EDGE, FC_EDGE), cells too: their planking drawn as every other cell's (a fixed row
    # there was drawn by the voxel shapes, its underside's planks out of line with the cells' beside it)
    decks = (("lower", LOWER, -30.0, 40.0), ("main", MAIN, -30.0, 40.0), ("quarter", QUARTER, -30.0, QD_EDGE if EDGE_OUT else WAIST_AFT - 0.25),
             ("forecastle", QUARTER, FC_EDGE if EDGE_OUT else WAIST_FWD + 0.25, 40.0), ("poop", POOP, -30.0, POOP_FRONT - 0.25))
    tops = {c: t for c, t, _, _ in decks}

    def cells_in(code, x0, x1, z0, z1):
        return [(code, float(x), float(z)) for x in np.arange(math.floor(x0), x1 - 1e-6, 1.0)
                for z in np.arange(math.floor(z0 - 0.5) + 0.5, z1 - 1e-6, 1.0)]

    # the openings by design: the hatchways and the stair wells
    empty = set(cells_in("lower", *OPENING) + cells_in("main", *OPENING))
    for w in d.wells:
        b = w["box"]
        code = next(c for c, t, xa, xb in decks if abs(t - b[4]) < 1e-6 and xa <= b[0] < xb)
        empty |= set(cells_in(code, b[0], b[3], b[2], b[5]))
    fixed = {}

    def fix(key, why):
        if key not in empty:
            fixed.setdefault(key, why)

    def fix_area(code, x0, z0, x1, z1, why, eps=1e-3):
        for c in cells_in(code, x0 + eps, x1 - eps, z0 + eps, z1 - eps):
            fix(c, why)

    # the guns' working room (MountLayout.GunClear) and their captains' cells: ((0.3.21, step C, docs/slots.md §7) no
    # longer fixed: slots, listed in each gun place's "cells", gun_places)
    for f in () if GUN_ROOM_SLOTS else d.fixtures:
        if f.get("type") != "gun":
            continue
        t0, t1 = sorted(f["trainLimits"])
        if f.get("trainLimitsGateOpen"):
            g0, g1 = sorted(f["trainLimitsGateOpen"])
            t0, t1 = min(t0, g0), max(t1, g1)
        piv, seat, o = f["pivot"], f["seat"], f["out"]
        al = math.hypot(o[0], o[2])
        ax, az = -o[0] / al, -o[2] / al
        ln = math.hypot(seat[0] - piv[0], seat[2] - piv[2]) + 0.8
        sign = 1.0 if o[2] >= 0 else -1.0
        cx, cz = -az, ax
        steps = max(1, int(math.ceil(t1 - t0)))
        for i in range(8):
            xs, zs = [], []
            for k in range(steps + 1):
                a = math.radians(t0 + (t1 - t0) * k / steps)
                c, sn = math.cos(a), math.sin(a)
                for u in (ln * i / 8, ln * (i + 1) / 8):
                    for w_ in (-0.45, 0.45):
                        vx, vz = ax * u + cx * w_, az * u + cz * w_
                        xs.append(piv[0] + vx * c + sign * vz * sn)
                        zs.append(piv[2] + vz * c - sign * vx * sn)
            fix_area("main", min(xs), min(zs), max(xs), max(zs), "a gun's working room")
        for b in (f.get("sel"), f.get("box")):
            if b:
                fix_area("main", b[0] - 0.05, b[2] - 0.05, b[3] + 0.05, b[5] + 0.05, "a gun's working room")
        fix_area("main", seat[0] - 0.3, seat[2] - 0.3, seat[0] + 0.3, seat[2] + 0.3, "a gun captain's place")
    # the entry gates' swing (the mod's "gate" box)
    for f in d.fixtures:
        if f.get("type") != "gate":
            continue
        b = f["box"]
        reach = max(1.5, max(b[3] - b[0], b[5] - b[2]) + 0.25)
        za, zb = (b[2] - reach, b[5]) if f["pos"][2] > 0 else (b[2], b[5] + reach)
        fix_area("main", b[0] - 0.25, za, b[3] + 0.25, zb, "an entry gate's swing")
    # the tiller's sweep (30° either way about the rudder's axis) and the rudder head, under the main deck
    ra, axis = d.rudder_axis
    pivot = ra + axis * ((TILLER_Y - ra[1]) / axis[1])
    arm = np.asarray(d.tiller_tip, float) - pivot
    pts = [pivot + np.array([arm[0] * math.cos(a) - arm[2] * math.sin(a), 0.0, arm[0] * math.sin(a) + arm[2] * math.cos(a)])
           for a in np.radians(np.linspace(-30.0, 30.0, 13))] + [pivot]
    xs_, zs_ = [p[0] for p in pts], [p[2] for p in pts]
    fix_area("main", min(xs_) - 0.6, min(zs_) - 0.3, max(xs_) + 0.3, max(zs_) + 0.3, "the tiller's sweep, the rudder head")
    # the masts' partners: every cell a mast passes through or touches, on every deck it passes
    for name, (mx, (y0, y1, r0, _), _, _) in MASTS.items():
        for code, top, _, _ in decks:
            if y0 < top:
                fix_area(code, mx - r0 - 0.05, -r0 - 0.05, mx + r0 + 0.05, r0 + 0.05, "a mast's partners")
    # fixed ladders' feet and heads (not the ones through the openings, gated by them)
    for lad in d.ladders:
        if lad.get("outside") or lad.get("gate") == "opening":
            continue
        for k_ in ("bottom", "top", "exitBottom", "exitTop"):
            p = lad.get(k_)
            for code, top, xa, xb in decks:
                if p and abs(p[1] - top) < 0.3 and xa <= p[0] < xb:
                    fix_area(code, p[0] - 0.3, p[2] - 0.3, p[0] + 0.3, p[2] + 0.3, "a fixed ladder's foot or head")

    # the fittings' meshes: on a cell (over its top) or hanging under it, strictly inside it; the skeleton under it is
    # left (beams, carlings, headers, knees: painted "beam" or "frame"), and the decks' own edge strips
    slabs = [(code, top) for code, top, _, _ in decks]
    ys_lo = min(t for _, t in slabs) - 0.25 - 0.16
    ys_hi = max(t for _, t in slabs) + 0.3
    for q in d.quads:
        P = np.asarray(q["pts"], float)
        if P[:, 1].max() < ys_lo or P[:, 1].min() > ys_hi or q["group"].startswith(("sail", "glass")):
            continue
        if GUN_ROOM_SLOTS and q["group"].startswith("part:gun-"):
            continue                # ((0.3.21) a gun (its place's: mounted on the cells, gun_places) or its port's lid)
        if q["mat"] == "deck" or q.get("hull") == "in":
            continue
        skel = q.get("paint") in ("beam", "frame")
        L = max(np.linalg.norm(P[1] - P[0]), np.linalg.norm(P[2] - P[3]))
        H = max(np.linalg.norm(P[3] - P[0]), np.linalg.norm(P[2] - P[1]))
        nu, nv = max(2, int(L / 0.04) + 2), max(2, int(H / 0.04) + 2)
        s_, t_ = np.meshgrid(np.linspace(0, 1, nu), np.linspace(0, 1, nv), indexing="ij")
        s_, t_ = s_.ravel()[:, None], t_.ravel()[:, None]
        S = (1 - s_) * (1 - t_) * P[0] + s_ * (1 - t_) * P[1] + s_ * t_ * P[2] + (1 - s_) * t_ * P[3]
        for code, top in slabs:
            on = (S[:, 1] > top + 0.003) & (S[:, 1] < top + 0.3)
            under = (S[:, 1] > top - DECK_T - 0.16) & (S[:, 1] < top - DECK_T - 0.003)
            for m_, why in ((on, "a fitting on it"), (under & (not skel), "a fitting under it")):
                if not m_.any():
                    continue
                X_, Z_ = S[m_, 0], S[m_, 2]
                fx_, fz_ = X_ - np.floor(X_), (Z_ - 0.5) - np.floor(Z_ - 0.5)
                # (a frame's inlay on the wall a few cm proud over a cell's outer edge: not standing on it)
                e_ = 0.08 if skel else 0.01
                ok = (fx_ > e_) & (fx_ < 1 - e_) & (fz_ > e_) & (fz_ < 1 - e_)
                for x_, z_ in set(zip(np.floor(X_[ok]).tolist(), (np.floor(Z_[ok] - 0.5) + 0.5).tolist())):
                    what = q.get("src", "").split(" < ")[0].split(":")[0] or q["group"]
                    fix((code, x_, z_), f"{why} ({q['mat']}{', ' + what if what else ''})")

    # the cells
    slots, reasons = [], collections.Counter()
    slot_grid = np.full(d.n, -1, np.int32)
    for code, top, xa, xb in decks:
        j = int(round((top - DECK_T - oy) / V))
        for x0 in range(int(math.ceil(max(xa, ox))), int(math.floor(min(xb, ox + d.n[0] * V))) + 1):
            if x0 + 1 > xb + 1e-6:
                continue
            for z0 in np.arange(-ZW, ZW - 1 + 1e-6, 1.0):
                key = (code, float(x0), float(z0))
                i0, k0 = int(round((x0 - ox) / V)), int(round((z0 - oz) / V))
                if i0 < 0 or i0 + 4 > d.n[0] or k0 < 0 or k0 + 4 > d.n[2]:
                    continue
                layer = g[i0:i0 + 4, j, k0:k0 + 4]
                above = g[i0:i0 + 4, j + 1, k0:k0 + 4]
                below = g[i0:i0 + 4, j - 1, k0:k0 + 4]
                start = "empty" if key in empty else "filled"
                if start == "empty":
                    assert not layer.any() and not above.any(), f"the opening {key} isn't open"
                else:
                    if not (layer == deck_mid).any():
                        continue                    # (no planking there)
                    if not (layer == deck_mid).all() or not hull_in[i0:i0 + 4, j, k0:k0 + 4].all():
                        reasons["partial cell (her side, a deck's end, a castle front's boards, a spar)"] += 1
                        continue
                    zo_ = max(abs(z0), abs(z0 + 1)) + 0.005
                    if any(float(ceil_z(x_, y_)) < zo_ for x_ in (x0 + 0.01, x0 + 0.5, x0 + 0.99)
                           for y_ in (top - DECK_T - 0.6, top - DECK_T - 0.3, top - DECK_T, top + 0.25, top + 0.5)):
                        # (her side leaning in over it or curving in under it: taken up, the hull's inside would show)
                        reasons["her side leaning in over it or under it"] += 1
                        continue
                    if above.any():
                        reasons["something of hers standing on it (collision)"] += 1
                        continue
                    if opaque[below].any():
                        reasons["fixed structure under it"] += 1
                        continue
                    if key in fixed:
                        reasons[fixed[key]] += 1
                        continue
                slot_grid[i0:i0 + 4, j, k0:k0 + 4] = len(slots)
                e = {"id": f"{code}:{x0}:{int(round(z0 - 0.5))}", "deck": code,
                     "box": [float(x0), top - DECK_T, float(z0), float(x0 + 1), top, float(z0 + 1)]}
                if start == "empty":
                    e["start"] = "empty"
                slots.append(e)
    d.props["deckSlots"] = slots
    d.slot_grid = slot_grid
    d.slot_fill = deck_mid
    per_deck = collections.Counter(s["deck"] for s in slots)
    n_empty = sum(1 for s in slots if s.get("start") == "empty")
    d.slot_report = {"per_deck": dict(per_deck), "empty": n_empty, "fixed": dict(reasons)}
    d.slot_fixed = fixed
    print(f"deck slots: {len(slots)} ({', '.join(f'{c} {n}' for c, n in per_deck.items())}), {n_empty} empty as designed")
    for why, n in reasons.most_common():
        print(f"  fixed: {n} cells: {why}")
    slot_checks(d)


GUN_ROOM_SLOTS = True       # (0.3.21, step C: the cells under the guns' working room are slots, listed by the places)


def gun_deck_code(x, y):
    """(0.3.21) The code of the deck a gun stands on (decks)."""
    if abs(y - MAIN) < 0.01:
        return "main"
    if abs(y - POOP) < 0.01:
        return "poop"
    return "quarter" if x < WAIST_AFT else "forecastle"


def gun_places(d, eps=0.02):     # ((0.3.24) eps as the mod's GunPlaces.RoomCells: a cell overlapped by more than 0.02 both ways)
    """
    (0.3.21, step C, docs/slots.md §7) carrack-m's gun places, d.props["gunPlaces"]: one per gun fixture, its id the
    gun's code: the waist's laid (a new ship has them), the castles' empty (she's launched with their ports shut and armed
    later with the gun item). Each lists its cells: every deck slot of its deck under its working room (gun_clear_boxes,
    overlapping a box by more than eps both ways). Checks: the gun's parts (train:/elevate:/fire: + the gun, their
    children, the follow parts following them) and not its lid among them, nothing of the gun in the static groups, a
    laid place's cells start filled.
    """
    slots = d.props["deckSlots"]
    by_deck = {}
    for sl in slots:
        by_deck.setdefault(sl["deck"], []).append(sl)
    places, bad = [], []
    for f in d.fixtures:
        if f.get("type") != "gun":
            continue
        code = f["code"]
        deck = gun_deck_code(f["pos"][0], f["pos"][1])
        boxes = gun_clear_boxes(f)
        cells = [sl["id"] for sl in by_deck.get(deck, []) if sl.get("kind") != "beam"
                 if any(min(sl["box"][3], b[2]) - max(sl["box"][0], b[0]) > eps and
                        min(sl["box"][5], b[3]) - max(sl["box"][2], b[1]) > eps for b in boxes)]
        start = "empty" if code in getattr(d, "castle_guns", []) else "laid"
        e = {"id": code, "gun": code, "cells": cells}
        if start == "empty":
            e["start"] = "empty"
        places.append(e)
        # its parts
        mine = {m["code"] for m in d.moving_parts if m.get("source") in (f"train:{code}", f"elevate:{code}", f"fire:{code}")}
        grew = True
        while grew:
            n0 = len(mine)
            mine |= {m["code"] for m in d.moving_parts if m.get("parent") in mine or m.get("follow") in mine}
            grew = len(mine) > n0
        lid = f.get("lid")
        if lid in mine or not mine:
            bad.append(f"{code}: parts {sorted(mine)}")
        static = [q["group"] for q in d.quads if q["group"].startswith(f"part:{code}") and
                  q["group"][5:] not in mine and q["group"][5:] != lid]
        if static:
            bad.append(f"{code}: meshes not in its parts {sorted(set(static))}")
        if start == "laid" and any(next(sl for sl in slots if sl["id"] == c).get("start") == "empty" for c in cells):
            bad.append(f"{code}: an empty cell under a laid gun")
        print(f"  gun place {code} ({deck}, {start}): {len(cells)} cells, {len(mine)} parts")
    d.props["gunPlaces"] = places
    print(f"  fit: gun places: {len(places)}, the gun's parts and not the lid, laid ones' cells filled: {not bad}")
    assert not bad, bad


CARLING_W = FRAME_SIDED - 0.02     # (0.3.27) a carling's breadth: a deck beam's


def beam_segments(d):
    """
    (0.3.27, the user's question with notes 211 and 222; docs/slots.md §10) The deck beams under wells in lengths that come
    out: for each beam (deck_beam's d.beam_spans; not a castle deck's edge beam) and each cell's width across her where the
    cells either side of it (aft xc-1..xc, forward xc..xc+1) are both deck slots, its collision there whole (the voxel column
    aft of its line, its depth), its neighbours two blocks aft and forward whole over that width and a quarter more either
    side (the carlings land on them), and nothing of hers but skeleton on it: a beam segment, a deck slot of kind "beam"
    (its box its collision there, "beam" the beam's key, "beside" the two cells' ids). Its timber is drawn in pieces split
    at the segments (each segment's own faces owned by it; no end faces where pieces meet), and for each segment two
    carlings, a beam's section (CARLING_W x its depth), along the opening's sides from the beam aft to the beam forward,
    0.005 into each: on the side toward the next length of the beam (owned by it if it's a segment, else fixed) and drawn
    while this one is out; passing through the beam's next length (only its face toward the opening there). Sets
    d.slot_beam_mask, d.slot_beam_ids and the segments' voxels in d.slot_grid.
    """
    if not BEAM_SEGMENTS or not getattr(d, "beam_spans", None):
        return
    V = 0.25
    slots = d.props["deckSlots"]
    cell_ix = {(s["deck"], round(s["box"][0], 3), round(s["box"][2], 3)): i for i, s in enumerate(slots) if s.get("kind") != "beam"}
    g = d.grid
    hidden = d.mid("hidden")
    ox, oy, oz = (float(v) for v in d.o)
    beams = {(b["deck"], round(b["xc"], 3)): b for b in d.beam_spans}

    def covered(b, z0, z1):
        return any(a <= z0 + 1e-6 and z1 <= c + 1e-6 for a, c in b["spans"])

    def coll_whole(b, z0, z1):
        x0, x1 = beam_coll(b["xc"])
        i0, i1 = int(round((x0 - ox) / V)), int(round((x1 - ox) / V))
        j0, j1 = int(round((b["y0"] - oy) / V)), int(round((b["y1"] - oy) / V))
        k0, k1 = int(round((z0 - oz) / V)), int(round((z1 - oz) / V))
        blk = g[i0:i1, j0:j1, k0:k1]
        sg_ = d.slot_grid[i0:i1, j0:j1, k0:k1]
        return blk.size > 0 and (blk == hidden).all() and (sg_ < 0).all()

    # the fittings on or at a beam (anything but skeleton: a hook, a mast's coat, a ladder...): its points
    skel_pts = []
    for q in d.quads:
        if q.get("paint") in ("beam", "frame") or q["group"].startswith(("sail", "glass", "part:")) or q["mat"] in ("deck",):
            continue
        skel_pts.extend(q["pts"])
    P = np.asarray(skel_pts, float) if skel_pts else np.zeros((0, 3))

    def fitting_on(b, z0, z1):
        lo = np.array([b["xc"] - 0.2, b["y0"] - 0.1, z0 - 0.05])
        hi = np.array([b["xc"] + 0.2, b["top"] + 0.02, z1 + 0.05])
        return bool(((P > lo) & (P < hi)).all(axis=1).any())

    segs = []          # (beam, z0, index)
    reasons = collections_counter()
    for key, b in sorted(beams.items(), key=lambda kv: (kv[0][0], kv[0][1])):
        code, xc = b["deck"], b["xc"]
        prev_b, next_b = beams.get((code, round(xc - FRAME_SPACE, 3))), beams.get((code, round(xc + FRAME_SPACE, 3)))
        for z0 in np.arange(-ZW, ZW - 1 + 1e-6, 1.0):
            z0 = float(z0)
            a_, b_ = cell_ix.get((code, round(xc - 1, 3), round(z0, 3))), cell_ix.get((code, round(xc, 3), round(z0, 3)))
            if a_ is None or b_ is None:
                continue
            # ((0.3.29) under a design well (both cells beside it in it): its collision cut as designed, a segment out)
            in_well_ = all(any(w["box"][0] - 1e-6 <= xa_ and xa_ + 1 <= w["box"][3] + 1e-6 and w["box"][2] - 1e-6 <= z0 and
                               z0 + 1 <= w["box"][5] + 1e-6 and abs(w["box"][4] - b["top"]) < 1e-6 for w in getattr(d, "wells", []))
                           for xa_ in (xc - 1, xc)) if COMPANION_WAIST else False
            if not covered(b, z0 - 0.25, z0 + 1.25) or not (coll_whole(b, z0, z0 + 1) or in_well_):
                reasons["the beam not whole across the cells' width (cut, or her side)"] += 1
                continue
            if prev_b is None or next_b is None or not covered(prev_b, z0 - 0.25, z0 + 1.25) or not covered(next_b, z0 - 0.25, z0 + 1.25):
                reasons["no whole beam aft or forward for its carlings to land on"] += 1
                continue
            if fitting_on(b, z0, z0 + 1):
                reasons["a fitting on it (a hook, a mast, a ladder...)"] += 1
                continue
            x0, x1 = beam_coll(xc)
            k = int(round(z0 - 0.5))
            e = {"id": f"beam:{code}:{int(round(xc))}:{k}", "deck": code, "kind": "beam", "beam": f"{code}:{int(round(xc))}",
                 "beside": [slots[a_]["id"], slots[b_]["id"]],
                 "box": [x0, b["y0"], z0, x1, b["top"] - DECK_T, z0 + 1.0],
                 "material": {"block": "game:planks-oak-ud", "count": 1}}
            # ((0.3.29) a segment under a design well (both cells beside it in it: the waist companion's) starts out)
            if all(any(w["box"][0] - 1e-6 <= xa_ and xa_ + 1 <= w["box"][3] + 1e-6 and w["box"][2] - 1e-6 <= z0 and
                       z0 + 1 <= w["box"][5] + 1e-6 and abs(w["box"][4] - b["top"]) < 1e-6 for w in getattr(d, "wells", []))
                   for xa_ in (xc - 1, xc)):
                e["start"] = "empty"
            segs.append((b, z0, len(slots)))
            if e.get("start") == "empty":
                gi0, gi1 = int(round((x0 - ox) / V)), int(round((x1 - ox) / V))
                gj0, gj1 = int(round((b["y0"] - oy) / V)), int(round((b["top"] - DECK_T - oy) / V))
                gk0, gk1 = int(round((z0 - oz) / V)), int(round((z0 + 1 - oz) / V))
                d.grid[gi0:gi1, gj0:gj1, gk0:gk1] = 0      # ((0.3.29) out as designed: not in her collision)
            i0, i1 = int(round((x0 - ox) / V)), int(round((x1 - ox) / V))
            j0, j1 = int(round((b["y0"] - oy) / V)), int(round((b["top"] - DECK_T - oy) / V))
            k0, k1 = int(round((z0 - oz) / V)), int(round((z0 + 1 - oz) / V))
            d.slot_grid[i0:i1, j0:j1, k0:k1] = len(slots)
            slots.append(e)
    d.slot_beam_mask = np.zeros(d.n, bool)
    d.slot_beam_ids = {n for _, _, n in segs}
    for n in d.slot_beam_ids:
        d.slot_beam_mask |= d.slot_grid == n
    # the beams' timber again: split at their segments
    by_beam = {}
    for b, z0, n in segs:
        by_beam.setdefault(id(b), (b, []))[1].append((z0, n))
    drop = set()
    S_ = FRAME_SIDED
    for b, lst in by_beam.values():
        drop |= {id(q) for q in b["quads"]}
        seg_at = {round(z0, 3): n for z0, n in lst}
        xc, h, ym = b["xc"], b["y1"] - b["y0"], (b["y0"] + b["y1"]) / 2
        with d.paint("beam"):
            for a, c in b["spans"]:
                # pieces: the segments in this span and the fixed runs between
                cuts = sorted({a, c} | {z for z0, _ in lst for z in (z0, z0 + 1.0) if a - 1e-6 <= z <= c + 1e-6})
                for za, zb in zip(cuts[:-1], cuts[1:]):
                    if zb - za < 1e-6:
                        continue
                    owner = seg_at.get(round(za, 3), -1) if abs(zb - za - 1.0) < 1e-6 else -1
                    n0 = len(d.quads)
                    d.add_box_mesh("auto", "timber", (xc, ym, (za + zb) / 2), (S_ - 0.02, h, zb - za))
                    keep = []
                    for q in d.quads[n0:]:
                        pz = [p[2] for p in q["pts"]]
                        end_z = max(pz) - min(pz) < 1e-6
                        # (no faces where two pieces meet: inside the beam, or the carling's there)
                        if end_z and abs(pz[0] - a) > 1e-6 and abs(pz[0] - c) > 1e-6:
                            continue
                        if owner >= 0:
                            q["slot"], q["slotfam"] = (owner, -1), "deck"
                        keep.append(q)
                    del d.quads[n0:]
                    d.quads.extend(keep)
    d.quads = [q for q in d.quads if id(q) not in drop]
    # the carlings: along each side of a length that's out, bay by bay (beam to beam), on the side of the beam's next
    # length; a bay shared with the length of the next beam at the same place across her is drawn once: by the after
    # length's rules (drawn while it's out; else while the forward one is out and it's in)
    n_carl = 0
    seg_on = {(b["deck"], round(b["xc"], 3), round(z0, 3)): n for b, z0, n in segs}
    half_b = (S_ - 0.02) / 2

    def bay(xa, xb, zb, side, y0, y1, owner, facing):
        za_, zb_ = (zb - CARLING_W, zb) if side < 0 else (zb, zb + CARLING_W)
        n0 = len(d.quads)
        d.add_box_mesh("auto", "timber", ((xa + xb) / 2, (y0 + y1) / 2, (za_ + zb_) / 2), (xb - xa, y1 - y0, zb_ - za_))
        keep = []
        for q in d.quads[n0:]:
            px = [p[0] for p in q["pts"]]
            # (its ends against the beams: no faces there)
            if max(px) - min(px) < 1e-6 and (abs(px[0] - xa) < 1e-6 or abs(px[0] - xb) < 1e-6):
                continue
            q["slot"], q["slotfam"] = (owner, facing), "deck"
            keep.append(q)
        del d.quads[n0:]
        d.quads.extend(keep)

    with d.paint("beam"):
        for b, z0, n in segs:
            xc, code = b["xc"], b["deck"]
            y0, y1 = b["y0"], b["y1"]
            for side, zb in ((-1, z0), (1, z0 + 1.0)):
                nb = seg_on.get((code, round(xc, 3), round(z0 + side, 3)), -1)
                # through the beam's next length: only its face toward the opening
                pts = [(xc - half_b, y0, zb), (xc + half_b, y0, zb), (xc + half_b, y1, zb), (xc - half_b, y1, zb)]
                _quad_facing(d, "auto", "timber", pts, (0, 0, -side), (0, 0, (2 * half_b) * 16, (y1 - y0) * 16))
                d.quads[-1]["slot"], d.quads[-1]["slotfam"] = (nb, n), "deck"
                # the bay aft: drawn by this one unless the beam aft has a length here (then that one draws it)
                aft = seg_on.get((code, round(xc - FRAME_SPACE, 3), round(z0, 3)))
                if aft is None:
                    bay(xc - FRAME_SPACE + half_b, xc - half_b, zb, side, y0, y1, nb, n)
                # the bay forward: this one's while it's out; and, the beam forward having a length here, that one's while
                # it's out and this one's in
                fwd = seg_on.get((code, round(xc + FRAME_SPACE, 3), round(z0, 3)))
                bay(xc + half_b, xc + FRAME_SPACE - half_b, zb, side, y0, y1, nb, n)
                if fwd is not None:
                    bay(xc + half_b, xc + FRAME_SPACE - half_b, zb, side, y0, y1, n, fwd)
                n_carl += 1
    print(f"beam segments: {len(segs)} (lengths of beams under a cell's width that come out once the deck either side is "
          f"open), {n_carl} carlings; {', '.join(f'{v} {k}' for k, v in reasons.most_common())}")
    per = {}
    for b, z0, n in segs:
        per[b["deck"]] = per.get(b["deck"], 0) + 1
    print(f"  fit: beam segments per deck: {per}; every one with two cells beside it: "
          f"{all(len(slots[n]['beside']) == 2 for _, _, n in segs)}")


def collections_counter():
    import collections
    return collections.Counter()


def slot_checks(d):
    """(0.3.20, docs/slots.md §6, the offline part) Every slot a whole cell on the grid at its deck's top; ids unique;
    neighbours share their edges; each hatch over a rectangle of empty slots that's the whole connected opening; the
    ladders through the openings with an opening cell at their tops; the wells' cells empty slots."""
    slots = d.props["deckSlots"]
    by = {(s["deck"], s["box"][0], s["box"][2]): s for s in slots}
    ids = [s["id"] for s in slots]
    ok_grid = all(abs(b[3] - b[0] - 1) < 1e-9 and abs(b[5] - b[2] - 1) < 1e-9 and b[0] == math.floor(b[0]) and
                  (b[2] - 0.5) == math.floor(b[2] - 0.5) for b in (s["box"] for s in slots))
    print(f"  fit: deck slots: whole cells on the grid / ids unique: {ok_grid} / {len(set(ids)) == len(ids)}")
    for p in d.modular:
        if p["kind"] != "hatch":
            continue
        code = "main" if abs(p["at"][1] - MAIN) < 1e-6 else "lower"
        nx_, nz_ = p["size"]
        x0, z0 = p["at"][0] - nx_ / 2, p["at"][2] - nz_ / 2
        inner = [(code, x0 + a, z0 + b) for a in range(nx_) for b in range(nz_)]
        all_empty = all(c in by and by[c].get("start") == "empty" for c in inner)
        ring = [(code, x, z) for (_, x0_, z0_) in inner for x, z in ((x0_ + 1, z0_), (x0_ - 1, z0_), (x0_, z0_ + 1), (x0_, z0_ - 1))
                if (code, x, z) not in inner]
        closed = not any(c in by and by[c].get("start") == "empty" for c in ring)
        print(f"  fit: the {p['code']} ({nx_} x {nz_}) over empty slots / the whole opening: {all_empty} / {closed}")
    for lad in d.ladders:
        if lad.get("gate") == "opening":
            x, y, z = lad["top"]
            code = "main" if abs(y - MAIN) < 0.3 else "lower"
            c = (code, float(math.floor(x)), float(math.floor(z - 0.5) + 0.5))
            print(f"  fit: the {lad['code']}'s top in an opening cell: {c in by and by[c].get('start') == 'empty'}")
    for w in d.wells:
        b = w["box"]
        code = "main" if abs(b[4] - MAIN) < 1e-6 else "lower"
        cs = [(code, float(x), float(z)) for x in np.arange(b[0], b[3] - 1e-6) for z in np.arange(b[2], b[5] - 1e-6)]
        print(f"  fit: the {w['code']} well's cells empty slots: {all(c in by and by[c].get('start') == 'empty' for c in cs)}")


HULL_FIXTURE_TYPES = ("gate", "pinrail", "pump", "hearth", "oven", "seat", "figurehead", "nameboard", "helm", "anchor",
                      "capstan", "door", "window", "hatch", "bed", "prop", "compass", "stove")


def _room_code_at(d, p):
    """The code of the first of her rooms a point (design coordinates) is in, or None."""
    i = int(math.floor((p[0] - d.o[0]) / 0.25))
    j = int(math.floor((p[1] - d.o[1]) / 0.25))
    k = int(math.floor((p[2] - d.o[2]) / 0.25))
    for r in d.rooms:
        (x0, y0, z0), (x1, y1, z1) = r["min"], r["max"]
        if not (x0 <= p[0] < x1 and y0 <= p[1] < y1 and z0 <= p[2] < z1):
            continue
        m = r.get("mask")
        if m is not None and not (0 <= i < d.n[0] and 0 <= j < d.n[1] and 0 <= k < d.n[2] and m[i, j, k]):
            continue
        return r["code"]
    return None


def _greedy_boxes(mask, lo):
    """Boxes (x0 y0 z0 x1 y1 z1) covering the True cells of a 3D mask exactly (greedy: along z, then y, then x); lo: the
    mask's corner (design coordinates), cells 0.25."""
    m = mask.copy()
    out = []
    V = 0.25
    nx, ny, nz = m.shape
    for i in range(nx):
        for j in range(ny):
            for k in range(nz):
                if not m[i, j, k]:
                    continue
                k1 = k + 1
                while k1 < nz and m[i, j, k1]:
                    k1 += 1
                j1 = j + 1
                while j1 < ny and m[i, j1, k:k1].all():
                    j1 += 1
                i1 = i + 1
                while i1 < nx and m[i1, j:j1, k:k1].all():
                    i1 += 1
                m[i:i1, j:j1, k:k1] = False
                out.append([float(lo[0] + i * V), float(lo[1] + j * V), float(lo[2] + k * V),
                            float(lo[0] + i1 * V), float(lo[1] + j1 * V), float(lo[2] + k1 * V)])
    return out


# (0.3.28, step D2.4, note 212: "can we make gun ports placeable: either a side board or a port"; note 223: the poop's
# "ports by default are not needed") carrack-m: each castle side port has a cover, a deck slot of kind "port": filled, the
# port is planked over (no lid, no gun place); empty, it's the lidded port and its gun place takes a gun. The poop's three
# a side start planked over, the quarterdeck's and the forecastle's open (docs/slots.md §11)
PORT_COVERS = MODULAR
PORTS_COVERED = ("poop-",)          # (the castle guns whose ports start planked over)
# (0.3.30, note 239: "remove placeable ports. they don't work at every location, textures does not align. becoming too
# complex at the moment") no covers, no boards, no trapdoor ports: every gun place's port is cut in her side and lidded,
# fixed, as the waist's (0.3.27's castle ports). A 0.3.28/0.3.29 save's port slot states are dropped (their ids gone)
PORT_COVERS = False
# (0.3.29, the user on 0.3.28's covers: "I thought removable side board as with hull and then we place port inside it")
# carrack-m: a castle side port is what's in a frame space's band of her side: its two half panels (planking, ordinary
# hull slots) or a port (a deck slot of kind "port": filled, the port's in; its planking round the hole, the hole's
# cheeks, its ring bolts, its lid and its gun place). The halves start out where the port starts in; the poop's start
# planked (PORTS_COVERED), no port. Her side isn't cut there: the halves own the hole's voxels (hull_slots)
PORT_PANELS = False
# (0.3.29, as built: the castle ports' frame spaces can't all be her side's ordinary panels (the castle breaks' sweeps,
# the mouldings, the forecastle's stepped-out sides), so each castle port's place in her side is two slots: the board
# over its hole ("port:<gun>", 0.3.28's cover, filled: boarded over) and the port itself ("gunport:<gun>", filled: the
# lidded port is in: its lid, its ring bolts, its gun place). Neither: an opening. Taken out with an axe, the board
# with planks or boards, the port with a trapdoor (docs/slots.md §12)
GUN_PORT_SLOTS = False      # ((0.3.30, note 239) dropped: fixed ports)


def port_covers(d):
    """
    (0.3.28, D2.4) A deck slot of kind "port" over each castle side gun's port (the stern chasers' in the transom stay
    ports): its collision the port's hole through her side (the voxels the port's cut took out of her shell), its quads
    the planking over the hole flush with her side outside and with her bulwark's inside (owned by it: drawn while it's
    filled), its material one planks block. Its gun place names it ("port"). Starts filled (planked over) for the poop's
    (PORTS_COVERED), empty (the port) for the others.
    """
    if not PORT_COVERS:
        return
    slots = d.props["deckSlots"]
    places = {p["gun"]: p for p in d.props.get("gunPlaces", [])}
    V = 0.25
    hidden = d.mid("hidden")
    n_cov = 0
    for name, gx, deck, py, zone, sides in CASTLE_GUNS:
        for s_ in sides:
            code = f"gun-{'starboard' if s_ > 0 else 'port'}-{name}"
            if code not in places:
                continue
            a, b = gx - GUNPORT_W / 2, gx + GUNPORT_W / 2
            y0, y1 = deck + py[0], deck + py[1]
            sl = d.box_slices(a, b, y0, y1, *((0.0, ZW) if s_ > 0 else (-ZW, 0.0)))
            mask = d.shell_mask[sl] & (d.grid[sl] == 0) & (d.slot_grid[sl] < 0)
            if not mask.any():
                print(f"  port cover {code}: no hole found")
                continue
            lo = [float(d.o[ax] + sl[ax].start * V) for ax in range(3)]
            solids = _greedy_boxes(mask, lo)
            covered = any(name.startswith(c) for c in PORTS_COVERED)
            n = len(slots)
            sub = d.slot_grid[sl]
            sub[mask] = n
            # (its voxels her side's (hidden), its quads its own: as a beam segment's for the deck slots' meshes)
            d.slot_beam_ids.add(n)
            bm_ = d.slot_beam_mask[sl]
            bm_[mask] = True
            if covered:
                g_ = d.grid[sl]
                g_[mask] = hidden
            zs = [v for bx in solids for v in (bx[2], bx[5])]
            deck_code = gun_deck_code(gx, deck) if "gun_deck_code" in globals() else None
            e = {"id": f"port:{code}", "deck": deck_code or ("poop" if deck >= POOP - 0.01 else "quarter" if gx < 0 else "forecastle"),
                 "kind": "port", "gun": code,
                 "box": [round(a, 4), round(y0, 4), round(min(zs), 4), round(b, 4), round(y1, 4), round(max(zs), 4)],
                 "solids": solids, "material": {"block": "game:planks-oak-ud", "count": 1},
                 "start": "filled" if covered else "empty"}
            slots.append(e)
            places[code]["port"] = e["id"]
            if GUN_PORT_SLOTS:
                # ((0.3.29) the port itself: no voxels of its own (its hole's the board's), its gun's ring bolts its quads;
                # in at the start where the board's out)
                gp = {"id": f"gunport:{code}", "deck": e["deck"], "kind": "gunport", "gun": code, "cover": e["id"],
                      "box": list(e["box"]), "solids": [], "material": {"block": "game:trapdoor-solid-oak-1", "count": 1},
                      "start": "empty" if covered else "filled"}
                gpn = len(slots)
                slots.append(gp)
                places[code]["gunport"] = gp["id"]
            # its planking: outside flush with her side, inside with her bulwark's inside (as the port's reveals: P)
            def P(x, y, inner):
                return (x, y, s_ * (float(hw(x, y)) - (HULL_T + 0.003 if inner else 0.003)))
            for inner in (False, True):
                pts = [P(a, y0, inner), P(b, y0, inner), P(b, y1, inner), P(a, y1, inner)]
                want = (0, 0, -s_) if inner else (0, 0, s_)
                with d.paint({"tarred": "topsides", "oak": "inside"}):
                    _quad_facing(d, "exterior", "oak" if inner else "tarred", pts, want,
                                 (0, 0, GUNPORT_W * 16, (y1 - y0) * 16))
                d.quads[-1]["slot"], d.quads[-1]["slotfam"] = (n, -1), "deck"
            n_cov += 1
    covered_n = sum(1 for e in slots if e.get("kind") == "port" and e["start"] == "filled")
    print(f"  port covers: {n_cov} (castle ports: {covered_n} planked over, {n_cov - covered_n} open)")


def _split_quad(q, x_lo, x_hi, y_lo, y_hi):
    """(0.3.29) The part of a planking quad (its footprint a rectangle in x and y: square across at grid x, along ring
    heights) within x_lo..x_hi and y_lo..y_hi, as a new quad dict (its corners on the same bilinear patch, its winding and
    texture coordinates carried), or None."""
    P = np.asarray(q["pts"], float)
    xa, xb, ya, yb = P[:, 0].min(), P[:, 0].max(), P[:, 1].min(), P[:, 1].max()
    if xb - xa < 1e-6 or yb - ya < 1e-6:
        return None
    u0, u1 = (max(x_lo, xa) - xa) / (xb - xa), (min(x_hi, xb) - xa) / (xb - xa)
    v0, v1 = (max(y_lo, ya) - ya) / (yb - ya), (min(y_hi, yb) - ya) / (yb - ya)
    if u1 - u0 < 1e-4 or v1 - v0 < 1e-4:
        return None
    uv_c = [((p[0] - xa) / (xb - xa) > 0.5, (p[1] - ya) / (yb - ya) > 0.5) for p in P]
    corner = {c: P[i] for i, c in enumerate(uv_c)}
    if len(corner) != 4:
        return None
    uvs = Design.corner_uvs(q)
    cuv = {c: np.asarray(uvs[i], float) for i, c in enumerate(uv_c)}

    def bil(tab, u, v):
        return ((1 - u) * (1 - v) * tab[(False, False)] + u * (1 - v) * tab[(True, False)] +
                u * v * tab[(True, True)] + (1 - u) * v * tab[(False, True)])

    nq = {k: v for k, v in q.items() if k not in ("pts", "uv", "uvs", "slot", "slotfam", "hull")}
    pts, nuv = [], []
    for hu, hv in uv_c:
        u, v = (u1 if hu else u0), (v1 if hv else v0)
        pts.append(tuple(float(c) for c in bil(corner, u, v)))
        nuv.append(tuple(float(c) for c in bil(cuv, u, v)))
    nq["pts"] = pts
    nq["uvs"] = nuv
    nq["uv"] = list(q.get("uv", (0, 0, 16, 16)))
    return nq


def castle_port_slots(d, slots, idx, info, cand, owned, emit, ring_z, poly_z):
    """
    (0.3.29, PORT_PANELS; the user: "removable side board as with hull and then we place port inside it") Each castle side
    gun's port as the alternative to the half panels of its frame space's band: a deck slot of kind "port" ("port:<gun>",
    filled: the port is in her side) whose quads are those halves' planking (outside and inside) less the hole and the
    rib's ends (FRAME_SIDED / 2 - 0.015 from each frame's middle, where the rib's faces are), the hole's cheeks (and sill
    and head where the hole is short of the band) across her side's thickness, and the gun's ring bolts beside it; its
    solids the halves' voxels less the hole's ("halves" names the two). Starts in (the halves out, their voxels cleared,
    the port's set) but for the poop's (PORTS_COVERED: planked). The mod: a port's in only while both halves are out.
    Returns {half key: (port id, starts in)}.
    """
    V = 0.25
    hid = d.mid("hidden")
    dslots = d.props["deckSlots"]
    places = {p["gun"]: p for p in d.props.get("gunPlaces", [])}
    out = {}
    rib = FRAME_SIDED / 2 - 0.015
    n_ok, bad = 0, []
    for name, gx, deck, py, zone, sides in CASTLE_GUNS:
        for s_ in sides:
            code = f"gun-{'starboard' if s_ > 0 else 'port'}-{name}"
            if code not in places:
                continue
            a, b = gx - GUNPORT_W / 2, gx + GUNPORT_W / 2
            sill, lintel = deck + py[0], deck + py[1]
            xf = next((x for x in frame_stations() if x < gx < x + FRAME_SPACE), None)
            n = next((k for k, (y0, y1) in enumerate(HULL_BANDS) if y0 - 1e-6 <= sill and lintel <= y1 + 1e-6), None)
            keys = [(s_, xf, n, h) for h in ("a", "f")]
            if xf is None or n is None or any(k not in idx for k in keys):
                why = [cand.get(k, {}).get("bad", ["none"])[:1] for k in keys] if xf is not None and n is not None else "no band"
                bad.append(f"{code}: {why}")
                continue
            covered = any(name.startswith(c) for c in PORTS_COVERED)
            pn = len(dslots)
            pid = f"port:{code}"
            x_lo, x_hi = xf + rib, xf + FRAME_SPACE - rib
            # its planking: the halves' quads less the hole
            for k in keys:
                for i in cand[k]["out"] + cand[k]["in"]:
                    q = d.quads[i]
                    for (xl, xh, yl, yh) in ((x_lo, a, -99, 99), (b, x_hi, -99, 99), (a, b, -99, sill), (a, b, lintel, 99)):
                        nq = _split_quad(q, xl, xh, yl, yh)
                        if nq is not None:
                            nq["slot"], nq["slotfam"] = (pn, -1), "deck"
                            d.quads.append(nq)
            # the hole's cheeks, sill and head across her side's thickness (bare oak)
            it_a, it_f = info[keys[0]], info[keys[1]]

            def zz(it, x, y, inward):
                lv = it["lo_in"] if inward else it["lo_out"]
                f_ = (x - it["xa"]) / (it["xb"] - it["xa"])
                return s_ * (poly_z(it["xa"], y, inward, lv) * (1 - f_) + poly_z(it["xb"], y, inward, lv) * f_)

            ys = sorted({y for y in set(it_a["lo_out"]) | set(it_a["lo_in"]) if sill - 1e-6 <= y <= lintel + 1e-6} | {sill, lintel})
            with d.paint(None):
                for x, it, want in ((a, it_a, (1, 0, 0)), (b, it_f, (-1, 0, 0))):
                    for ya, yb in zip(ys[:-1], ys[1:]):
                        emit([(x, ya, zz(it, x, ya, False)), (x, yb, zz(it, x, yb, False)), (x, yb, zz(it, x, yb, True)),
                              (x, ya, zz(it, x, ya, True))], want, pn, -1)
                for y, want in ((sill, (0, 1, 0)), (lintel, (0, -1, 0))):
                    if (want[1] > 0 and sill > it_a["y0"] + 1e-6) or (want[1] < 0 and lintel < it_a["y1"] - 1e-6):
                        emit([(a, y, zz(it_a, a, y, False)), (b, y, zz(it_f, b, y, False)), (b, y, zz(it_f, b, y, True)),
                              (a, y, zz(it_a, a, y, True))], want, pn, -1)
            # its reveals' quads are the deck family's (emit tagged them hull reveals)
            for q in d.quads:
                if q.get("slot") == (pn, -1) and q.get("reveal"):
                    q["slotfam"] = "deck"
                    q.pop("reveal", None)
            # the gun's ring bolts beside it: the port's
            for q in d.quads:
                if q.get("port_bolt") == code:
                    q["slot"], q["slotfam"] = (pn, -1), "deck"
            # its voxels: the halves' less the hole's
            solids_mask = np.zeros(d.n, bool)
            for k in keys:
                i0, j0, sub = owned[k]
                solids_mask[i0:i0 + sub.shape[0], j0:j0 + sub.shape[1], :] |= sub
            xc = d.o[0] + (np.arange(d.n[0]) + 0.5) * V
            yc = d.o[1] + (np.arange(d.n[1]) + 0.5) * V
            in_hole = ((xc > a)[:, None] & (xc < b)[:, None]) & ((yc > sill) & (yc < lintel))[None, :]
            port_mask = solids_mask & ~in_hole[:, :, None]
            ii, jj, kk = np.nonzero(port_mask)
            lo = [float(d.o[0] + ii.min() * V), float(d.o[1] + jj.min() * V), float(d.o[2] + kk.min() * V)]
            sub = port_mask[ii.min():ii.max() + 1, jj.min():jj.max() + 1, kk.min():kk.max() + 1]
            solids = _greedy_boxes(sub, lo)
            if not covered:
                # (in at the start: the halves' voxels out, the port's in)
                d.grid[solids_mask] = 0
                d.grid[port_mask] = hid
            zs = [v for bx in solids for v in (bx[2], bx[5])]
            deck_code = "poop" if deck >= POOP - 0.01 else "quarter" if gx < 0 else "forecastle"
            hole = [round(a, 4), round(sill, 4), round(min(zs), 4), round(b, 4), round(lintel, 4), round(max(zs), 4)]
            e = {"id": pid, "deck": deck_code, "kind": "port", "gun": code,
                 "box": [round(x_lo, 4), round(it_a["y0"], 4), round(min(zs), 4), round(x_hi, 4), round(it_a["y1"], 4), round(max(zs), 4)],
                 "hole": hole,
                 "halves": [f"hull:{'s' if s_ > 0 else 'p'}:{int(math.floor((xf + FRAME_SPACE / 2 - frame_stations()[0]) / FRAME_SPACE))}:{n}:{h}"
                            for h in ("a", "f")],
                 "solids": solids, "material": {"block": "game:trapdoor-solid-oak-1", "count": 1},
                 "start": "empty" if covered else "filled"}
            dslots.append(e)
            places[code]["port"] = pid
            for k in keys:
                out[k] = (pid, not covered)
            n_ok += 1
    print(f"  castle ports in her side's panels: {n_ok} ({sum(1 for v in out.values() if v[1]) // 2} start in, the rest planked)"
          + (f"; not panels: {bad}" if bad else ""))
    return out


def hull_slots(d):
    """
    (0.3.26, step D1; docs/slots.md §9, docs/step-d-plan.md) carrack-m's hull planking as slots: a panel is a frame space
    (strip to strip: STRIP_HALF either side of each frame's middle stays fixed) by a band (HULL_BANDS) on one side, its
    outside planking and its ceiling. Only whole panels: fixed (not listed, the reasons counted) where it's not a whole
    frame space (her ends: the stem, the sternpost, the transom), cut by an opening (a port, a window, the entry port, a
    scupper), a castle break's sweep, a moulding over it, under the forecastle's overhang, its inside in two rooms, a
    fitting on either face (any mesh of hers within 0.3 of its outside or its ceiling: channels, chains, ladders, the
    gallows, lids...; a fixture's box) or no collision of its own. Each panel's quads get "slot": (its index, -1); the
    reveals round it (the frame strips' and fixed strakes' faces toward it: (-1, it); its own edge toward a neighbouring
    panel up or down: (it, the neighbour)) are added. Sets d.props["hullSlots"], d.props["hullBands"], d.hull_slot_grid
    (each voxel's panel or -1), d.hull_slot_report; takes the "hull" tags off her quads (no hullOut/hullIn: the mod's
    old jagged holes are not for her). Checks: hull_slot_checks.
    """
    import collections
    V = 0.25
    hid = d.mid("hidden")
    xf0 = panel_stations()[0]
    rec = getattr(d, "skin_record", [])
    xgrid = frame_strips()

    def band_of(y):
        for n, (a, b) in enumerate(HULL_BANDS):
            if a + 1e-6 < y < b - 1e-6:
                return n
        return -1

    def frame_of(x):
        for xf in panel_stations():
            if HALF_PANELS:
                if xf + 1e-6 < x < xf + FRAME_SPACE / 2 - 1e-6:
                    return (xf, "a")
                if xf + FRAME_SPACE / 2 + 1e-6 < x < xf + FRAME_SPACE - 1e-6:
                    return (xf, "f")
            elif xf + STRIP_HALF + 1e-6 < x < xf + FRAME_SPACE - STRIP_HALF - 1e-6:
                return (xf, "")
        return None

    def span_of(xf, half):
        """A panel's x (its planking's ends) and its collision's."""
        if half == "a":
            return xf, xf + FRAME_SPACE / 2, xf, xf + FRAME_SPACE / 2
        if half == "f":
            return xf + FRAME_SPACE / 2, xf + FRAME_SPACE, xf + FRAME_SPACE / 2, xf + FRAME_SPACE - 0.25
        return xf + STRIP_HALF, xf + FRAME_SPACE - STRIP_HALF, xf + STRIP_HALF, xf + FRAME_SPACE - STRIP_HALF

    def ring_z(x, y, inward):
        """z (starboard, > 0) of a planking ring at height y at a grid x (in a skin's run), or None."""
        for r in rec:
            if r["inward"] != inward:
                continue
            for (ry, rx, rz), run in zip(r["rings"], r["runs"]):
                if run is None or not (run[0] - 1e-6 <= x <= run[1] + 1e-6) or abs(ry - y) >= 1e-6:
                    continue
                k = np.nonzero(np.abs(rx - x) < 1e-6)[0]
                if len(k):
                    return float(rz[k[0]])
        return None

    def ring_levels(x, y0, y1, inward):
        """Every ring level (y) within [y0, y1] the skins have at a grid x."""
        ys = set()
        for r in rec:
            if r["inward"] != inward:
                continue
            for (ry, rx, rz), run in zip(r["rings"], r["runs"]):
                if run is None or not (run[0] - 1e-6 <= x <= run[1] + 1e-6):
                    continue
                if y0 - 1e-6 <= ry <= y1 + 1e-6 and np.any(np.abs(rx - x) < 1e-6):
                    ys.add(round(float(ry), 6))
        return sorted(ys)

    def poly_z(x, y, inward, levels):
        """z on the planking at a grid x and a height y between its ring levels (straight between them; clamped)."""
        if y <= levels[0]:
            return ring_z(x, levels[0], inward)
        if y >= levels[-1]:
            return ring_z(x, levels[-1], inward)
        for a, b in zip(levels[:-1], levels[1:]):
            if a <= y <= b:
                za, zb = ring_z(x, a, inward), ring_z(x, b, inward)
                return za + (zb - za) * (y - a) / max(1e-9, b - a)
        return None

    # ---- the candidates: each hull planking quad by its middle (side, frame space, band)
    cand = collections.defaultdict(lambda: {"out": [], "in": [], "clad": [], "bad": []})
    for i, q in enumerate(d.quads):
        h = q.get("hull")
        if h not in ("out", "in") or q.get("floor"):
            continue
        P = np.asarray(q["pts"], float)
        c = P.mean(axis=0)
        side = 1 if c[2] > 0 else -1
        fh, n = frame_of(c[0]), band_of(c[1])
        if fh is None or n < 0:
            continue
        xf, half = fh
        e = cand[(side, xf, n, half)]
        if q.get("raised"):
            e["raised"] = True
            continue
        if q.get("xend"):
            e["bad"].append("not a whole frame space (her ends: the stem, the sternpost)")
        if q.get("transom"):
            e["bad"].append("the transom")
        if q.get("sweep"):
            e["bad"].append("a castle break's sweep")
        sxa_, sxb_ = span_of(xf, half)[:2]
        if P[:, 0].min() < sxa_ - 1e-4 or P[:, 0].max() > sxb_ + 1e-4 or \
                P[:, 1].min() < HULL_BANDS[n][0] - 1e-4 or P[:, 1].max() > HULL_BANDS[n][1] + 1e-4:
            e["bad"].append("planking across its edges")
        # (the copper sheathing drawn instead of the tarred bottom on a clad ship: the same strakes again)
        e["clad" if q["group"] == "bottomclad" else h].append(i)
        if os.environ.get("HANSA_DEBUG_PANEL") == f"{side},{xf:g},{n},{half}":
            print(f"  DEBUG panel {side},{xf:g},{n},{half}: quad {i} {q['group']} {h} x {P[:, 0].min():.3f}..{P[:, 0].max():.3f} "
                  f"y {P[:, 1].min():.3f}..{P[:, 1].max():.3f} xend {q.get('xend')} span {span_of(xf, half)[:2]} band {HULL_BANDS[n]}")

    # ---- each candidate's shape: its corners, its coverage, its rooms
    raised_boxes = []
    for q in d.quads:
        if q.get("raised"):
            P_ = np.asarray(q["pts"], float)
            raised_boxes.append((P_[:, 0].min(), P_[:, 0].max(), P_[:, 1].min(), P_[:, 1].max(), 1 if P_[:, 2].mean() > 0 else -1))
    info = {}
    for key, e in cand.items():
        side, xf, n, half = key
        xa, xb, cxa, cxb = span_of(xf, half)
        y0, y1 = HULL_BANDS[n]
        if not e["out"] and not e["in"]:
            e["none"] = True            # (no planking of hers there: not a panel)
            continue
        if e.get("raised"):
            e["bad"].append("a moulding over it")
        elif HALF_PANELS and xb < POOP_FRONT - 6.0 and y0 > QUARTER and any(
                r[4] == side and min(xb, r[1]) - max(xa, r[0]) > 0.01 and r[2] < y1 + 0.01 and r[3] > y0 - 0.01 for r in raised_boxes):
            # ((0.3.28) the poop's moulding along its edge by the transom, where it curves in: its seam's faces would lie in
            # the moulding's)
            e["bad"].append("a moulding over it")
        if e["bad"]:
            continue
        if not e["out"] or not e["in"]:
            e["bad"].append("no outside or no ceiling of its own")
            continue
        # every strake of it whole outside and inside: their (x, y) footprints (rectangles: the quads' edges are square
        # across at the grid's x and along the rings' heights) fill the panel
        def cover(ids):
            return sum((max(p[0] for p in d.quads[i]["pts"]) - min(p[0] for p in d.quads[i]["pts"])) *
                       (max(p[1] for p in d.quads[i]["pts"]) - min(p[1] for p in d.quads[i]["pts"])) for i in ids)
        yi0 = max(y0, HOLD_FLOOR)
        if abs(cover(e["out"]) - (xb - xa) * (y1 - y0)) > 2e-3:
            e["bad"].append("cut by an opening (a port, a window, the entry port, a scupper)")
            continue
        if abs(cover(e["in"]) - (xb - xa) * (y1 - yi0)) > 2e-3:
            e["bad"].append("its ceiling not whole")
            continue
        if any(c.get("side", 0) in (0, side) and min(xb, max(c["x1"], c.get("x1in", c["x1"]))) -
               max(xa, min(c["x0"], c.get("x0in", c["x0"]))) > 1e-3 and
               min(y1, c["y1"]) - max(y0, c["y0"]) > 1e-3 for c in SIDE_CUTS):
            e["bad"].append("cut by an opening (a port, a window, the entry port, a scupper)")
            continue
        if xb > FC_X0 and y1 > FC_LOW + 1e-6:
            e["bad"].append("under the forecastle's overhang")
            continue
        lo_out = ring_levels(xa, y0, y1, False)
        lo_in = ring_levels(xa, yi0, y1, True)
        ok = (lo_out == ring_levels(xb, y0, y1, False) and lo_in == ring_levels(xb, yi0, y1, True) and
              len(lo_out) >= 2 and len(lo_in) >= 2 and abs(lo_out[0] - y0) < 1e-6 and abs(lo_out[-1] - y1) < 1e-6 and
              abs(lo_in[0] - yi0) < 1e-6 and abs(lo_in[-1] - y1) < 1e-6 and
              all(ring_z(x, y, False) is not None for x in {xa, xf + FRAME_SPACE / 2, xb} for y in (y0, y1)) and
              all(ring_z(x, y, True) is not None for x in {xa, xf + FRAME_SPACE / 2, xb} for y in (yi0, y1)))
        if not ok:
            e["bad"].append("no planking rings at its edges")
            continue
        rooms = set()
        seen_rooms = collections.Counter()
        for fx in (0.1, 0.5, 0.9):
            for fy in (0.15, 0.5, 0.85):
                x, y = xa + (xb - xa) * fx, yi0 + (y1 - yi0) * fy
                zi = poly_z(xa, y, True, lo_in) * (1 - fx) + poly_z(xb, y, True, lo_in) * fx
                rc = _room_code_at(d, (x, y, side * (zi - 0.3)))
                rooms.add(rc)
                seen_rooms[rc] += 1
        if HANSA and len(rooms) > 1:
            # (Hansa) a block row at a deck's level: its inside mostly the room under the deck (the deck's own slab, no
            # room, in its top): a hole in it lets the sea into that one
            real = [(c, r) for r, c in seen_rooms.items() if r is not None]
            rooms = {max(real)[1]} if real else rooms
        if len(rooms) > 1:
            e["bad"].append("its inside in two rooms")
            continue
        # ((0.3.29, LINED) under a weather deck: two layers, the frame a rib RIB_SIDED wide between them (its collision
        # both columns either side of the frame's line, not the panel's)
        lined_ = LINED and HALF_PANELS and bool(lined(xf + (0.5 if half == "a" else 1.5), y1 - 0.01))
        if lined_:
            cxa, cxb = (xf + RIB_SIDED / 2, cxb) if half == "a" else (cxa, xf + FRAME_SPACE - RIB_SIDED / 2)
        info[key] = {"xa": xa, "xb": xb, "cxa": cxa, "cxb": cxb, "y0": y0, "y1": y1, "yi0": yi0, "lo_out": lo_out,
                     "lo_in": lo_in, "room": rooms.pop(), "lined": lined_}

    # ---- fittings on a panel's faces: any mesh of hers within 0.3 of its outside or its ceiling (but its own planking),
    # a fixture's box (D2 binds the places: chests, lanterns, pieces are left to it)
    by_frame = collections.defaultdict(list)
    for key in info:
        by_frame[key[1]].append(key)

    def faces_z(key, x, y):
        it = info[key]
        f = (x - it["xa"]) / (it["xb"] - it["xa"])
        zo = poly_z(it["xa"], y, False, it["lo_out"]) * (1 - f) + poly_z(it["xb"], y, False, it["lo_out"]) * f
        zi = poly_z(it["xa"], y, True, it["lo_in"]) * (1 - f) + poly_z(it["xb"], y, True, it["lo_in"]) * f
        return zo, zi

    def hits(pts, why):
        pts = np.asarray(pts, float)
        if len(pts) == 0:
            return
        for xf in {xf for xf in by_frame if pts[:, 0].max() > xf and pts[:, 0].min() < xf + FRAME_SPACE}:
            for key in by_frame[xf]:
                if key not in info or cand[key]["bad"]:
                    continue
                it = info[key]
                side = key[0]
                # (over the ceiling's height: in band 0 the hold's floor and its edge are under its foot)
                # (and on its edges: a window's timber in the plane of a strip's face would be in its reveal's)
                m = ((pts[:, 0] > it["xa"] - 0.02) & (pts[:, 0] < it["xb"] + 0.02) & (pts[:, 1] > it["yi0"] + 0.02) &
                     (pts[:, 1] < it["y1"] - 0.02) & (pts[:, 2] * side > 0))
                for p in pts[m]:
                    zo, zi = faces_z(key, p[0], p[1])
                    if zi - 0.3 <= abs(p[2]) <= zo + 0.3:
                        cand[key]["bad"].append(why)
                        break

    xs_all = [it for it in info.values()]
    if xs_all:
        bx0, bx1 = min(it["xa"] for it in xs_all), max(it["xb"] for it in xs_all)
        by0, by1 = min(it["y0"] for it in xs_all), max(it["y1"] for it in xs_all)
        for i, q in enumerate(d.quads):
            if q.get("hull") in ("out", "in") and not q.get("raised") or q.get("floor") or q["group"].startswith("sail"):
                continue
            if HALF_PANELS and q.get("paint") in ("frame", "beam"):
                continue        # ((0.3.28) her skeleton (the riders, their knees, the beams' ends): the ribs, not fittings)
            if HANSA and q.get("mat") == "deck":
                continue        # ((Hansa) a deck's planking meeting her side in its row: the deck's own cells, not a fitting)
            if PORT_PANELS and (q.get("port_of") or q.get("port_bolt")) or q.get("lining_step"):
                continue        # ((0.3.29) a castle gun, its lid, its ring bolts: its port's, in the panels' place; the
                                # ceiling's step where the weather deck steps: her side's own)
            P = np.asarray(q["pts"], float)
            if P[:, 0].max() < bx0 or P[:, 0].min() > bx1 or P[:, 1].max() < by0 or P[:, 1].min() > by1 or np.abs(P[:, 2]).max() < 2.0:
                continue
            L = max(np.linalg.norm(P[1] - P[0]), np.linalg.norm(P[2] - P[3]))
            H = max(np.linalg.norm(P[3] - P[0]), np.linalg.norm(P[2] - P[1]))
            nu, nv = max(2, int(L / 0.05) + 2), max(2, int(H / 0.05) + 2)
            s_, t_ = np.meshgrid(np.linspace(0, 1, nu), np.linspace(0, 1, nv), indexing="ij")
            s_, t_ = s_.ravel()[:, None], t_.ravel()[:, None]
            S = (1 - s_) * (1 - t_) * P[0] + s_ * (1 - t_) * P[1] + s_ * t_ * P[2] + (1 - s_) * t_ * P[3]
            what = q.get("src", "").split(" < ")[0].split(":")[0] or q["group"]
            hits(S, f"a fitting on it ({q['mat']}, {what})")
        for e_ in d.elements:
            if PORT_PANELS and e_.get("port_of"):
                continue
            c, R, sz = np.asarray(e_["center"], float), np.asarray(e_["R"], float), np.asarray(e_["size"], float)
            u = np.linspace(-0.5, 0.5, 5)
            g3 = np.array([[a, b, c_] for a in u for b in u for c_ in u])
            hits(c + (g3 * sz) @ R.T, f"a fitting on it ({e_['mat']}, {e_.get('name') or e_['group']})")
        for f in d.fixtures:
            if f.get("type") not in HULL_FIXTURE_TYPES:
                continue
            b = f.get("box") or f.get("sel")
            if not b or len(b) != 6:
                continue
            u = np.linspace(0, 1, 7)
            g3 = np.array([[b[0] + (b[3] - b[0]) * a, b[1] + (b[4] - b[1]) * bb, b[2] + (b[5] - b[2]) * cc]
                           for a in u for bb in u for cc in u])
            hits(g3, f"a fitting on it ({f['type']} {f.get('code')})")

    # ---- the slots, and the voxels each owns: her collision shell's (hidden) cells on its side, within its strips'
    # edges and its band (deck cells, the hold's floor over the flat floor, are never a panel's)
    shell = getattr(d, "shell_mask", None)
    keys = sorted((k for k in info if not cand[k]["bad"]), key=lambda k: (k[1], k[3], k[2], k[0]))
    slot_grid = np.full(d.n, -1, np.int32)
    slots, owned = [], {}
    reasons = collections.Counter()
    for key, e in cand.items():
        if e["bad"] and not e.get("none"):
            reasons[e["bad"][0]] += 1
    d.hull_slot_candidates = {k: (list(e["bad"]), bool(e.get("none"))) for k, e in cand.items()}
    if HANSA:
        # (Hansa) every reason by band, not just the first: what keeps her block rows from being panels
        by_band = collections.defaultdict(collections.Counter)
        for (side_, xf_, n_, half_), e in cand.items():
            if e.get("none"):
                continue
            for why in (set(e["bad"]) or {"a panel"}):
                by_band[n_][why] += 1
        for n_ in sorted(by_band):
            print(f"  hull slots by band {n_} {HULL_BANDS[n_]}: " + "; ".join(f"{c} {w}" for w, c in by_band[n_].most_common()))
    # ((0.3.30, note 238: "cannot remove here panel but can left and right") the frame spaces' halves under her weather
    # decks between her ends that aren't panels, and why: the ones that look as their neighbours do)
    odd = sorted((k, e["bad"][0]) for k, e in cand.items() if e["bad"] and not e.get("none") and
                 not any(w in e["bad"][0] for w in ("her ends", "transom", "castle break", "moulding", "forecastle's overhang", "opening")))
    print(f"  hull slots: {len(odd)} halves that look like panels but aren't:")
    for k, why in odd:
        print(f"    {'s' if k[0] > 0 else 'p'} frame {k[1]:+.3f} band {k[2]} {k[3]}: {why}")
    for key in keys:
        side, xf, n, half = key
        it = info[key]
        i0, i1 = d._rng(it["cxa"], it["cxb"], 0)
        j0, j1 = d._rng(it["y0"], it["y1"], 1)
        sub = (d.grid[i0:i1, j0:j1, :] == hid) & (d.zs * side > 0)[None, None, :]
        if shell is not None:
            sub &= shell[i0:i1, j0:j1, :]
        sub &= slot_grid[i0:i1, j0:j1, :] < 0
        if getattr(d, "slot_grid", None) is not None:
            sub &= d.slot_grid[i0:i1, j0:j1, :] < 0
        if sub.sum() < 2:
            reasons["no collision of its own"] += 1
            continue
        if it.get("lined"):
            # ((0.3.29) two layers: the innermost of her shell's cells across (each column's) the ceiling's, the rest the
            # planking's (its frames' spaces between them with it: out, it's open to the ceiling's back)
            ks = np.arange(sub.shape[2])
            # (the innermost: port side the largest z index, starboard the smallest; -1 where a column has none)
            inner_k = (np.where(sub, ks[None, None, :], -1).max(axis=2) if side < 0 else
                       np.where(sub.any(axis=2), np.where(sub, ks[None, None, :], 10 ** 6).min(axis=2), -1))
            sub_in = sub & (ks[None, None, :] == inner_k[:, :, None])
            sub_out = sub & ~sub_in
            if sub_out.sum() < 1 or sub_in.sum() < 1:
                it["lined"] = False
            else:
                owned[key] = (i0, j0, sub_out)
                slot_grid[i0:i1, j0:j1, :][sub_out] = len(slots)
                slots.append(key)
                ckey = key + ("c",)
                owned[ckey] = (i0, j0, sub_in)
                slot_grid[i0:i1, j0:j1, :][sub_in] = len(slots)
                slots.append(ckey)
                continue
        owned[key] = (i0, j0, sub)
        slot_grid[i0:i1, j0:j1, :][sub] = len(slots)
        slots.append(key)

    # ---- the reveals: each panel's edges (outside to ceiling), bare oak, drawn while it's out
    idx = {k: n for n, k in enumerate(slots)}
    n_reveal0 = len(d.quads)

    def emit(pts, want, owner, facing, mat="oak"):
        P = [np.asarray(p, float) for p in pts]
        nrm = np.cross(P[1] - P[0], P[3] - P[0]) + np.cross(P[3] - P[2], P[1] - P[2])
        if np.dot(nrm, want) < 0:
            P = [P[1], P[0], P[3], P[2]]
        if np.linalg.norm(nrm) < 1e-9:
            return
        reveal(d, *[tuple(map(float, p)) for p in P], mat, group="exterior")
        d.quads[-1]["slot"] = (owner, facing)
        d.quads[-1]["reveal"] = True

    def emit_fixed(pts, want, mat="oak"):
        # ((0.3.29) a face always drawn: inside her side's two layers, seen only through an opening)
        P = [np.asarray(p, float) for p in pts]
        nrm = np.cross(P[1] - P[0], P[3] - P[0]) + np.cross(P[3] - P[2], P[1] - P[2])
        if np.linalg.norm(nrm) < 1e-9:
            return
        if np.dot(nrm, want) < 0:
            P = [P[1], P[0], P[3], P[2]]
        reveal(d, *[tuple(map(float, p)) for p in P], mat, group="auto")
        d.quads[-1]["cavity"] = True

    def lined_faces(key):
        """
        (0.3.29, LINED) A half panel of her side's two layers: its planking (outer, key) and its ceiling (inner, key +
        "c"), the frame's rib (RIB_SIDED) between them at its frame end. Each layer: its back (drawn while it's in and the
        other layer's out), its edges across its own thickness (PLANK_T) toward its neighbours (the other half, the panel
        across the frame, the bands under and over it; or her fixed planking's face toward it). Its space between the
        layers walled (always drawn, a hair inside it: the rib's side and faces, the other half's side, the seams under
        and over it): through an opening only that space is seen.
        """
        side, xf, n, half = key
        it = info[key]
        mo, mi = idx[key], idx[key + ("c",)]
        xa, xb, y0, y1, yi0 = it["xa"], it["xb"], it["y0"], it["y1"], it["yi0"]
        T = PLANK_T
        e_ = 0.01           # (the space's walls that far inside it: not near the seams' faces)

        def zz(x, y, inward):
            lv = it["lo_in"] if inward else it["lo_out"]
            f_ = (x - xa) / (xb - xa)
            return side * (poly_z(xa, y, inward, lv) * (1 - f_) + poly_z(xb, y, inward, lv) * f_)

        def zb(x, y, inward, d_=0.0):
            # (a layer's back: T in from its face; d_ further into the space between)
            return zz(x, y, inward) + (side * (T + d_) if inward else -side * (T + d_))

        ys_o, ys_i = it["lo_out"], it["lo_in"]
        ys = sorted(set(ys_o) | set(ys_i))

        def deck_off(y, dy):
            # (a seam on a deck's top or underside: 0.01 off it, out of the deck's planes (as the one-layer panels' are))
            for t in d.deck_tops:
                if dy > 0 and abs(y - t) < 1e-6:
                    return y + 0.01
                if dy < 0 and abs(y - (t - DECK_T)) < 1e-6:
                    return y - 0.01
            return y
        ys_c = [y for y in ys if y >= yi0 - 1e-6]          # (the space between: from the ceiling's foot)
        fr, mid = (xa, xb) if half == "a" else (xb, xa)       # its frame end, its middle end
        sg = 1.0 if half == "a" else -1.0                     # from the frame toward the middle
        xr = fr + sg * RIB_SIDED / 2                           # the rib's side
        xs_cell = sorted([x for x in xgrid if min(xr, mid) - 1e-6 <= x <= max(xr, mid) + 1e-6] + [xr])
        xs_cell = [x for k, x in enumerate(xs_cell) if k == 0 or x - xs_cell[k - 1] > 1e-6]
        xs_all = [x for x in xgrid if xa - 1e-6 <= x <= xb + 1e-6]

        def across(x, lv, inward, want, owner, facing):
            # (a face across one layer's thickness at x, ring by ring)
            for ya, yb_ in zip(lv[:-1], lv[1:]):
                emit([(x, ya, zz(x, ya, inward)), (x, yb_, zz(x, yb_, inward)), (x, yb_, zb(x, yb_, inward)),
                      (x, ya, zb(x, ya, inward))], want, owner, facing)

        for inward, lv, me, other in ((False, ys_o, mo, mi), (True, ys_i, mi, mo)):
            off = 1 if inward else 0
            # its back, between the rib's side and its middle end (over the rib: the rib's own face)
            for ya, yb_ in zip(lv[:-1], lv[1:]):
                for x0_, x1_ in zip(xs_cell[:-1], xs_cell[1:]):
                    emit([(x0_, ya, zb(x0_, ya, inward)), (x1_, ya, zb(x1_, ya, inward)), (x1_, yb_, zb(x1_, yb_, inward)),
                          (x0_, yb_, zb(x0_, yb_, inward))], (0, 0, side if inward else -side), me, other)
            # its middle end: toward the other half (its face, drawn while that one's out), or the fixed planking's
            nb = (side, xf, n, "f" if half == "a" else "a") + (("c",) if inward else ())
            if nb in idx:
                across(mid, lv, inward, (-sg, 0, 0), me, idx[nb])
            else:
                across(mid, lv, inward, (sg, 0, 0), -1, me)
            # its frame end: toward the panel across the frame (its face), or the fixed planking's
            nf = (side, xf - FRAME_SPACE, n, "f") if half == "a" else (side, xf + FRAME_SPACE, n, "a")
            nf = nf + (("c",) if inward else ())
            if nf in idx and info.get(nf[:4], {}).get("lined"):
                across(fr, lv, inward, (sg, 0, 0), me, idx[nf])
            else:
                across(fr, lv, inward, (sg, 0, 0), -1, me)
            # its seams under and over it: toward a lined panel there (its edge), else her fixed planking's face
            for ys_, dy, nbb in ((y0 if not inward else yi0, 1.0, (side, xf, n - 1, half)), (y1, -1.0, (side, xf, n + 1, half))):
                nk = nbb + (("c",) if inward else ())
                nb_ok = nk in idx and info.get(nbb, {}).get("lined") and abs((HULL_BANDS[nbb[2]][1] if dy > 0 else HULL_BANDS[nbb[2]][0]) - ys_) < 1e-6
                y_ = deck_off(ys_, dy) if not nb_ok else ys_
                if not inward and dy > 0 and y0 < yi0 - 1e-6:
                    y_ = ys_ + 0.01     # (band 0's foot: her flat floor's edge, its outside in the floor's plane: a hair over it)
                for x0_, x1_ in zip(xs_all[:-1], xs_all[1:]):
                    pts = [(x0_, y_, zz(x0_, ys_, inward)), (x1_, y_, zz(x1_, ys_, inward)), (x1_, y_, zb(x1_, ys_, inward)),
                           (x0_, y_, zb(x0_, ys_, inward))]
                    if nb_ok:
                        emit(pts, (0, -dy, 0), me, idx[nk])
                    else:
                        emit(pts, (0, dy, 0), -1, me)
        # the space between the layers: the rib's side, its faces against the layers' backs, the middle's side, the seams
        for ya, yb_ in zip(ys_c[:-1], ys_c[1:]):
            for x_, want in ((xr + sg * e_, (sg, 0, 0)), (mid - sg * e_, (-sg, 0, 0))):
                emit_fixed([(x_, ya, zb(x_, ya, False)), (x_, yb_, zb(x_, yb_, False)), (x_, yb_, zb(x_, yb_, True)),
                            (x_, ya, zb(x_, ya, True))], want, "timber" if want[0] == sg else "oak")
        for inward, lv in ((False, ys_o), (True, ys_i)):
            for ya, yb_ in zip(lv[:-1], lv[1:]):
                emit_fixed([(fr, ya, zb(fr, ya, inward)), (xr, ya, zb(xr, ya, inward)), (xr, yb_, zb(xr, yb_, inward)),
                            (fr, yb_, zb(fr, yb_, inward))], (0, 0, -side if inward else side), "timber")
        for y_, dy in ((deck_off(yi0, 1.0) + e_, 1.0), (deck_off(y1, -1.0) - e_, -1.0)):
            for x0_, x1_ in zip(xs_cell[:-1], xs_cell[1:]):
                emit_fixed([(x0_, y_, zb(x0_, y_, False)), (x1_, y_, zb(x1_, y_, False)), (x1_, y_, zb(x1_, y_, True)),
                            (x0_, y_, zb(x0_, y_, True))], (0, dy, 0))

    with d.paint(None):
        for key in slots:
            if len(key) == 5:
                continue                    # ((0.3.29) a ceiling: with its planking's, lined_faces)
            if info[key].get("lined"):
                lined_faces(key)
                continue
            side, xf, n, half = key
            it = info[key]
            me = idx[key]
            xa, xb, y0, y1, yi0 = it["xa"], it["xb"], it["y0"], it["y1"], it["yi0"]
            if HALF_PANELS:
                ys = sorted(set(it["lo_out"]) | set(it["lo_in"]))

                def zz(x, y, inward):
                    # (z on its planking at any x of it: straight between its ends' rings)
                    lv = it["lo_in"] if inward else it["lo_out"]
                    f_ = (x - xa) / (xb - xa)
                    return side * (poly_z(xa, y, inward, lv) * (1 - f_) + poly_z(xb, y, inward, lv) * f_)

                def across(x, want, owner, facing, mat="oak"):
                    # (a face across her side's thickness at x, outside to ceiling, ring by ring)
                    for ya, yb in zip(ys[:-1], ys[1:]):
                        pa, pb = (x, ya, zz(x, ya, False)), (x, yb, zz(x, yb, False))
                        qb, qa = (x, max(yb, yi0), zz(x, max(yb, yi0), True)), (x, max(ya, yi0), zz(x, max(ya, yi0), True))
                        if ya < yi0 - 1e-6 and yb <= yi0 + 1e-6:
                            emit([pa, pb, qb, qb], want, owner, facing, mat)
                        else:
                            emit([pa, pb, qb, qa], want, owner, facing, mat)

                # ((0.3.28, D2.1) its end at the space's middle: toward the other half (its own face, drawn while that
                # one's out), or the fixed planking's face toward it there)
                xm, sg = (xb, 1.0) if half == "a" else (xa, -1.0)
                nb = (side, xf, n, "f" if half == "a" else "a")
                if nb in idx:
                    across(xm, (sg, 0, 0), me, idx[nb])
                else:
                    across(xm, (-sg, 0, 0), -1, me)
                # ((0.3.28, D2.2) its end at the frame: the rib's half on its side (FRAME_SIDED / 2), drawn while it's out:
                # its face toward it, across her side's thickness, and its outside and inside faces, flush with hers
                rx, sg = (xa, 1.0) if half == "a" else (xb, -1.0)
                # (0.015 inside the riders' and the frames' faces over it: not in their planes)
                rx2 = rx + sg * (FRAME_SIDED / 2 - 0.015)
                across(rx2, (sg, 0, 0), -1, me, "timber")
                for lv, inward, want in ((it["lo_out"], False, (0, 0, side)), (it["lo_in"], True, (0, 0, -side))):
                    for ya, yb in zip(lv[:-1], lv[1:]):
                        emit([(rx, ya, zz(rx, ya, inward)), (rx2, ya, zz(rx2, ya, inward)),
                              (rx2, yb, zz(rx2, yb, inward)), (rx, yb, zz(rx, yb, inward))], want, -1, me, "timber")
            # the frame strips' faces toward it, at its after and forward edges
            for x, dirx in () if HALF_PANELS else ((xa, 1.0), (xb, -1.0)):
                ys = sorted(set(it["lo_out"]) | set(it["lo_in"]))
                for ya, yb in zip(ys[:-1], ys[1:]):
                    pa = (x, ya, side * poly_z(x, ya, False, it["lo_out"]))
                    pb = (x, yb, side * poly_z(x, yb, False, it["lo_out"]))
                    qb = (x, max(yb, yi0), side * poly_z(x, max(yb, yi0), True, it["lo_in"]))
                    qa = (x, max(ya, yi0), side * poly_z(x, max(ya, yi0), True, it["lo_in"]))
                    if ya < yi0 - 1e-6 and yb <= yi0 + 1e-6:
                        # (under the ceiling's foot: the shell's edge down to the floor's, a triangle)
                        emit([pa, pb, qb, qb], (dirx, 0, 0), -1, me)
                    else:
                        emit([pa, pb, qb, qa], (dirx, 0, 0), -1, me)
            # its bottom and top edges: toward a panel under (over) it, its own edge (it, the neighbour); else the fixed
            # strake's (or the floor's) face toward it
            xs = [x for x in xgrid if xa - 1e-6 <= x <= xb + 1e-6]
            for ys_o, ys_i, dy, nb in ((y0, yi0, 1.0, (side, xf, n - 1, half)), (y1, y1, -1.0, (side, xf, n + 1, half))):
                nb_ok = nb in idx and abs((HULL_BANDS[nb[2]][1] if dy > 0 else HULL_BANDS[nb[2]][0]) - ys_o) < 1e-6
                # (at a deck's top or underside the deck's edge strip runs on 0.02 into the planking there (edge_strips): the
                # fixed face from where it ends, piece by piece as it does, not in its plane over it)
                deck_at = None
                if not nb_ok and abs(ys_i - ys_o) < 1e-9:      # (not band 0's foot: from her outside at the keel to the floor)
                    for t in d.deck_tops:
                        if abs(ys_o - t) < 1e-6:
                            deck_at = t + 0.01
                        elif abs(ys_o - (t - DECK_T)) < 1e-6:
                            deck_at = t - DECK_T - 0.01
                if deck_at is not None:
                    def zo(x):
                        k = max(0, min(len(xs) - 2, sum(1 for g_ in xs if g_ <= x + 1e-9) - 1))
                        f = (x - xs[k]) / (xs[k + 1] - xs[k])
                        return ring_z(xs[k], ys_o, False) * (1 - f) + ring_z(xs[k + 1], ys_o, False) * f
                    for c0 in np.arange(xa, xb - 1e-6, V):
                        ca, cb = ceil_z(c0, deck_at) + 0.02, ceil_z(c0 + V, deck_at) + 0.02
                        n_pc = max(1, min(4, int(math.ceil(abs(cb - ca) / 0.04))))
                        for k in range(n_pc):
                            x0_, x1_ = c0 + V * k / n_pc, c0 + V * (k + 1) / n_pc
                            cz = max(ca + (cb - ca) * k / n_pc, ca + (cb - ca) * (k + 1) / n_pc)
                            emit([(x0_, ys_o, side * zo(x0_)), (x1_, ys_o, side * zo(x1_)), (x1_, ys_o, side * cz), (x0_, ys_o, side * cz)],
                                 (0, dy, 0), -1, me)
                    continue
                for xa_, xb_ in zip(xs[:-1], xs[1:]):
                    pts = [(xa_, ys_o, side * ring_z(xa_, ys_o, False)), (xb_, ys_o, side * ring_z(xb_, ys_o, False)),
                           (xb_, ys_i, side * ring_z(xb_, ys_i, True)), (xa_, ys_i, side * ring_z(xa_, ys_i, True))]
                    if nb_ok:
                        emit(pts, (0, -dy, 0), me, idx[nb])
                    else:
                        emit(pts, (0, dy, 0), -1, me)
    n_reveals = len(d.quads) - n_reveal0

    # ---- ((0.3.29, PORT_PANELS) the castle side ports: each a deck slot of kind "port" over its frame space's band, the
    # two half panels there its alternative (planked, or the port in: the halves out)
    port_of_half = {}
    if PORT_PANELS:
        port_of_half = castle_port_slots(d, slots, idx, info, cand, owned, emit, ring_z, poly_z)

    # ---- the panels' own quads, and their entries ((0.3.29, LINED) a two-layer half: its planking ("hull:...", the
    # outside's quads) and its ceiling ("ceil:...", "layer": "inner", the inside's), each naming the other)
    entries = []
    for key in slots:
        ceil_ = len(key) == 5
        k4 = key[:4]
        side, xf, n, half = k4
        it = info[k4]
        me = idx[key]
        e = cand[k4]
        two = bool(it.get("lined"))
        mine = (e["in"] if ceil_ else e["out"] + e["clad"]) if two else e["out"] + e["in"] + e["clad"]
        for i in mine:
            d.quads[i]["slot"] = (me, -1)
        xa, xb, y0, y1, yi0 = it["xa"], it["xb"], it["y0"], it["y1"], it["yi0"]
        outer = [[xa, y0, side * ring_z(xa, y0, False)], [xb, y0, side * ring_z(xb, y0, False)],
                 [xb, y1, side * ring_z(xb, y1, False)], [xa, y1, side * ring_z(xa, y1, False)]]
        inner = [[xa, yi0, side * ring_z(xa, yi0, True)], [xb, yi0, side * ring_z(xb, yi0, True)],
                 [xb, y1, side * ring_z(xb, y1, True)], [xa, y1, side * ring_z(xa, y1, True)]]
        if two:
            # (each layer's other face: its back, PLANK_T in)
            if ceil_:
                outer = [[p_[0], p_[1], p_[2] + side * PLANK_T] for p_ in inner]
            else:
                inner = [[p_[0], p_[1], p_[2] - side * PLANK_T] for p_ in outer]
        nsum, area_o = np.zeros(3), 0.0
        for i in e["out"]:
            P = np.asarray(d.quads[i]["pts"], float)
            cr = 0.5 * (np.cross(P[1] - P[0], P[3] - P[0]) + np.cross(P[3] - P[2], P[1] - P[2]))
            nsum += cr
            area_o += float(np.linalg.norm(cr))
        area_i = sum(0.5 * float(np.linalg.norm(np.cross(np.subtract(d.quads[i]["pts"][2], d.quads[i]["pts"][0]),
                                                           np.subtract(d.quads[i]["pts"][3], d.quads[i]["pts"][1]))))
                     for i in e["in"])
        nrm = nsum / max(1e-9, np.linalg.norm(nsum))
        i0, j0, sub = owned[key]
        lo = d.o + np.array([i0, j0, 0]) * V
        solids = _greedy_boxes(sub, lo)
        allp = np.array(outer + inner + [[b[0], b[1], b[2]] for b in solids] + [[b[3], b[4], b[5]] for b in solids], float)
        r4 = lambda v: round(float(v), 4)          # noqa: E731
        st_ = int(math.floor((xf + FRAME_SPACE / 2 - xf0) / FRAME_SPACE))
        base_id = f"{'s' if side > 0 else 'p'}:{st_}:{n}" + (f":{half}" if half else "")
        count = (area_i if ceil_ else area_o) if two else area_o + area_i
        entries.append({
            "id": ("ceil:" if ceil_ else "hull:") + base_id,
            "side": side, "station": st_, "band": n, **({"half": half} if half else {}),
            "box": [r4(xa), r4(y0), r4(allp[:, 2].min()), r4(xb), r4(y1), r4(allp[:, 2].max())],
            "outer": [[r4(v) for v in p] for p in outer], "inner": [[r4(v) for v in p] for p in inner],
            "normal": [r4(v) for v in nrm], "area": r4(area_o), "sill": r4(y0), "head": r4(y1), "room": it["room"],
            "solids": [[r4(v) for v in b] for b in solids],
            "material": {"block": "game:planks-oak-ud", "count": max(1, int(round(count)))},
        })
        if two:
            entries[-1]["layer"] = "inner" if ceil_ else "outer"
            entries[-1]["partner"] = ("hull:" if ceil_ else "ceil:") + base_id
        if not ceil_ and k4 in port_of_half:
            # ((0.3.29) a castle port's half: the port's id, and out where the port starts in)
            entries[-1]["port"] = port_of_half[k4][0]
            if port_of_half[k4][1]:
                entries[-1]["start"] = "empty"
    d.props["hullSlots"] = entries
    d.props["hullBands"] = ([{"band": n, "y0": a, "y1": b} for n, (a, b) in enumerate(HULL_BANDS)] +
                            [{"fixed": w, "y0": a, "y1": b} for a, b, w in HULL_FIXED_BANDS])
    d.hull_slot_grid = slot_grid
    d.hull_slot_keys = slots
    # (no hullOut / hullIn for her: the old jagged holes are for the archived carracks only)
    for q in d.quads:
        q.pop("hull", None)
    below = sum(1 for e in entries if e["head"] <= WATER + 1e-6 or e["sill"] < WATER)
    per_band = collections.Counter(e["band"] for e in entries)
    d.hull_slot_report = {"slots": len(entries), "below": below, "per_band": dict(per_band), "fixed": dict(reasons),
                          "reveals": n_reveals}
    print(f"hull slots: {len(entries)} panels ({below} reaching below the waterline), "
          f"{', '.join(f'band {b} {per_band[b]}' for b in sorted(per_band))}; {n_reveals} reveal quads")
    for why, k in reasons.most_common():
        print(f"  fixed: {k} panels: {why}")
    hull_slot_checks(d)


def hull_slot_checks(d):
    """(0.3.26, docs/slots.md §9, the offline part) ids unique, each panel with outside and ceiling quads (owned, within
    its box + 0.05) and solids (its own voxels, in no deck slot, no other panel's), every one of its four edges closed
    (each strake's strip faces both ends, a reveal or neighbour's pair along its seams), no reveal drawn with every panel
    in (all filled she's drawn as before)."""
    hs = d.props["hullSlots"]
    ids = [s["id"] for s in hs]
    print(f"  fit: hull slots: ids unique: {len(set(ids)) == len(ids)}")
    quads_of = {}
    for q in d.quads:
        if "slot" not in q:
            continue
        o, f = q["slot"]
        if o >= 0 and not q.get("reveal") and q.get("slotfam") != "deck":     # ((0.3.29) the deck family's own indices)
            quads_of.setdefault(o, []).append(q)
    bad_box = 0
    for n, s in enumerate(hs):
        b = s["box"]
        for q in quads_of.get(n, []):
            P = np.asarray(q["pts"], float)
            if (P[:, 0].min() < b[0] - 0.05 or P[:, 0].max() > b[3] + 0.05 or P[:, 1].min() < b[1] - 0.05 or
                    P[:, 1].max() > b[4] + 0.05 or P[:, 2].min() < b[2] - 0.05 or P[:, 2].max() > b[5] + 0.05):
                bad_box += 1
                if bad_box <= 4:
                    print(f"  quad of {s['id']} outside its box {b}: {np.round(P, 3).tolist()} ({q.get('group')}, {q.get('mat')})")
    no_quads = [s["id"] for n, s in enumerate(hs) if not quads_of.get(n)]
    no_solids = [s["id"] for s in hs if not s["solids"]]
    print(f"  fit: hull slots: each with its quads / solids / its quads within its box: {not no_quads} / {not no_solids} / {bad_box == 0}")
    both = int(((d.hull_slot_grid >= 0) & (getattr(d, "slot_grid", np.full(d.n, -1)) >= 0)).sum())
    print(f"  fit: hull slots: no voxel in a deck slot and a hull slot: {both == 0}")
    shown_filled = [q for q in d.quads if q.get("reveal") and (q["slot"][1] < 0)]
    print(f"  fit: hull slots: no reveal drawn with every panel in: {not shown_filled}")
    # each panel's edges: its strip faces (owner -1) at both ends, its seams' faces (its own or the fixed one's)
    rev = {}
    for q in d.quads:
        if q.get("reveal"):
            rev.setdefault(q["slot"][1], []).append(q)
            if q["slot"][0] >= 0:
                rev.setdefault(("own", q["slot"][0]), []).append(q)
    open_edges = []
    for n, s in enumerate(hs):
        mine = rev.get(n, [])
        own = rev.get(("own", n), [])
        xa, xb = s["box"][0], s["box"][3]
        # ((0.3.28, D2.2) at a frame, the rib's face toward it, FRAME_SIDED / 2 in from its end)
        ends = [any(abs(np.mean([p[0] for p in q["pts"]]) - x) < (FRAME_SIDED / 2 + 1e-3 if HALF_PANELS else 1e-4) for q in mine)
                for x in (xa, xb)]
        # the seams: the face toward it (fixed or the neighbour's) and, toward a neighbour, its own face toward that
        y0, y1 = s["sill"], s["head"]
        seams = [any(abs(np.mean([p[1] for p in q["pts"]]) - y) < 0.3 and abs(max(p[1] for p in q["pts"]) - min(p[1] for p in q["pts"])) < 0.3
                     and abs(np.mean([p[0] for p in q["pts"]]) - xa) > 1e-4 and abs(np.mean([p[0] for p in q["pts"]]) - xb) > 1e-4
                     for q in mine) for y in (y0, y1)]
        if not all(ends) or not all(seams):
            open_edges.append(s["id"])
    print(f"  fit: hull slots: every panel's four edges closed: {not open_edges}" + (f" ({open_edges[:6]})" if open_edges else ""))
    assert len(set(ids)) == len(ids) and not no_quads and not no_solids and both == 0 and not shown_filled, "hull slot checks"


def fixed_rail_ends(d):
    """(0.3.16) The short bits of the castle fronts' breastworks where no railing piece has a place (by the sides, in
    front of the binnacle): a plinth, a handrail and its cap on the grid line like a piece's (1/8 thick), a newel at
    each end inboard of the side."""
    for line, deck, side, a, b in getattr(d, "fixed_rails", []):
        half = ceil_z(line, deck + 0.5) + 0.02
        a, b = max(a, -half), min(b, half)
        if b - a < 0.05:
            continue
        zm = (a + b) / 2
        d.add_box_mesh("auto", "timber", (line, deck + 0.06, zm), (0.14, 0.12, b - a))
        d.add_box_mesh("auto", "timber", (line, deck + 0.9, zm), (0.12, 0.08, b - a - 0.024))
        d.add_box_mesh("auto", "oak", (line, deck + 0.96, zm), (0.16, 0.04, b - a))
        for e in (a, b):
            if abs(abs(e) - half) < 0.03:
                continue                # (the end in the side)
            sg_ = np.sign(e - zm)       # (0.01 past the rails' ends: not in their planes)
            d.add_box_mesh("auto", "timber", (line, deck + 0.5, e - 0.05 * sg_), (0.15, 1.0, 0.12))
            d.add_box_mesh("auto", "oak", (line, deck + 1.02, e - 0.05 * sg_), (0.19, 0.04, 0.16))


def build():
    # (GRID: its cells on the block grid: x borders at whole x, z at +-0.5..., y from the keel's underside, G -4.5 since 0.3.10)
    d = Design(CODE, -16.5 - (3.0 if POOP_OVERHANG else 0.0), 20.0 + STRETCH + (1.5 if PROPOSAL else 0.0),
               KEEL_BOTTOM if GRID else -3.0, 36.0, -ZW, ZW)
    d.hull_thickness = HULL_T

    d.material(Material("tarred", plank_textures("oak", "++3~hansa:block/tar"), (42, 30, 22), grain=True))
    d.material(Material("oak", plank_textures("oak"), (150, 112, 70), grain=True))
    d.material(Material("deck", plank_textures("oak", "++0~hansa:block/caulk"), (160, 122, 80), grain=True))
    d.material(Material("timber", ["game:block/wood/debarked/oak"], (160, 124, 84), grain=True))
    d.material(Material("pine", ["game:block/wood/debarked/pine"], (196, 160, 112), grain=True))   # masts, yards, bowsprit
    d.material(Material("gold", ["game:block/metal/plate/gold"], (222, 176, 60)))
    d.material(Material("iron", ["game:block/metal/plate/iron"], (70, 70, 75)))
    d.material(Material("copper", [f"game:block/metal/sheet/copper{i}" for i in range(1, 6)], (186, 100, 60)))
    d.material(Material("cloth", ["game:block/linen"], (235, 228, 210), solid=False, opaque=False))
    d.material(Material("redcloth", ["game:block/cloth/aged/red/top"], (170, 40, 35), solid=False, opaque=False))
    d.material(Material("rope", ["game:block/cloth/reedrope"], (120, 95, 60), solid=False, opaque=False))
    d.material(Material("hidden", ["game:block/wood/planks/oak1"], (0, 0, 0), render=False, opaque=False))
    # (0.3.12, the user's note 109: the sea not recognisable through the windows) the clear panes in our own clearer glass
    # (textures.py glass_clear: alpha 0.08, vanilla's plain 0.2); the stained top lights stay vanilla's
    d.material(Material("glass", ["hansa:block/glass-clear" if GRID else "game:block/glass/plain"], (185, 212, 228),
                        solid=False, opaque=False))
    d.material(Material("glass-red", ["game:block/glass/red"], (190, 60, 60), solid=False, opaque=False))
    d.material(Material("glass-blue", ["game:block/glass/blue"], (70, 95, 190), solid=False, opaque=False))
    d.material(Material("glass-yellow", ["game:block/glass/yellow"], (215, 190, 70), solid=False, opaque=False))
    d.material(Material("brick", [f"game:block/clay/brick/four/running/fire{i}" for i in range(1, 6)], (176, 112, 80)))
    d.material(Material("black", ["game:block/black"], (18, 16, 14), solid=False, opaque=False))
    d.material(Material("paper", ["game:item/lore/paper"], (226, 212, 172), solid=False, opaque=False))
    d.material(Material("crate", ["game:block/wood/crate/oak-sides"], (150, 116, 72)))
    d.material(Material("bronze", ["game:block/metal/plate/tinbronze"], (178, 142, 64)))     # the guns
    d.material(Material("shot", ["game:block/metal/tarnished/iron"], (58, 54, 50)))         # cast iron shot
    d.material(Material("lead", ["game:block/metal/plate/lead"], (120, 124, 130)))          # the scuppers' linings

    X, Y, Z = d.X, d.Y, d.Z
    hwv = half_width(X, Y)
    hull_in = (np.abs(Z) < hwv) & (Y >= KEEL)
    inside = hull_in & (Y < rail_y(X))

    # ---------------------------------------------------------------- hull shell (collision only)
    # The hull is drawn by the planked meshes outside and inside; the voxel shell under them only stops people. It's
    # the cells at the hull's sides and bottom, up to the rail (the castle fronts have their own walls).
    out = ~hull_in
    shell = np.zeros_like(inside)
    for ax, sh in ((0, 1), (0, -1), (2, 1), (2, -1), (1, 1)):  # +-x, +-z, and the cell below (roll(+1) along y)
        nb = np.roll(out, sh, axis=ax)
        idx = [slice(None)] * 3
        idx[ax] = 0 if sh == 1 else -1
        nb[tuple(idx)] = True
        shell |= inside & nb
    # and the hull's thickness inside that, up to the inside planking: nobody walks into the ceiling (the frames stand
    # a little proud of it, drawn only)
    for j in range(d.n[1]):
        row_in = inside[:, j, :]
        if not row_in.any():
            continue
        y = d.ys[j]
        c = np.minimum(_ceil_grid(d, [y - 0.125])[0], _ceil_grid(d, [y + 0.125])[0])
        shell[:, j, :] |= row_in & (np.abs(d.zs)[None, :] + 0.125 > c[:, None] + 0.01)
    d.grid[shell] = d.mid("hidden")
    d.shell_mask = shell            # ((0.3.26) the hull's collision shell: what hull panels own of it, hull_slots)
    d.hide_toward = ~hull_in
    d.hide_applies = hull_in
    # keel
    if GRID:
        # (0.25 deep under the bottom, 0.4 sided, from the sternpost to the forefoot: the cells under the bottom collision
        # only; drawn with the stem and the sternpost as one timber, keel_and_stem(), 0.3.10)
        kx0, kx1 = float(interp(KEEL, STERN_X)), float(interp(KEEL, BOW_X))
        d.box("hidden", kx0, kx1, KEEL_BOTTOM, KEEL_BOTTOM + 0.25, -0.25, 0.25)
    else:
        d.box("tarred", -12.5, 11.5 + STRETCH, KEEL - 0.5, KEEL, -0.125, 0.125)

    # ---------------------------------------------------------------- decks and beams
    # Decks run out to the hull. The cells of them that would reach past the inside planking are made collision only
    # below (clip_to_ceiling) and their edges closed to the planking by mesh strips, so nothing pokes through the hull.
    fill = lambda: inside & ((d.grid == 0) | (d.grid == d.mid("hidden")))  # noqa: E731
    d.deck_tops = [HOLD_FLOOR, LOWER, MAIN, QUARTER, POOP]
    x_end = 21 + STRETCH
    d.deck_spans = ((HOLD_FLOOR, -20, x_end), (LOWER, -20, x_end), (MAIN, -20, x_end), (QUARTER, -20, WAIST_AFT),
                    (QUARTER, WAIST_FWD, x_end), (POOP, -20, POOP_FRONT))
    if MODULAR:
        # (0.3.18, note 162; docs/modular-pieces.md §7.1) carrack-m: each castle deck's edge on its front's grid line (x
        # -3, 15, -9), its breast beam under it on the castle's side: a castle stair's ladder hooks over that edge at its
        # head cell's far edge, and nothing of her overhangs the stair (0.3.16-0.3.17: the edge 0.25 out over the waist,
        # cut away over each stair's head: the user's note 162, holes in the deck's edge and its beam)
        # ((0.3.24, notes 184, 185) the quarterdeck's and the forecastle deck's edges a block further out over the waist,
        # on the frames at -2 and 14: QD_EDGE, FC_EDGE)
        d.deck_spans = ((HOLD_FLOOR, -20, x_end), (LOWER, -20, x_end), (MAIN, -20, x_end), (QUARTER, -20, QD_EDGE),
                        (QUARTER, FC_EDGE, x_end), (POOP, -20, POOP_FRONT - 0.25))
    for top, x0, x1 in d.deck_spans:
        d.box("deck", x0, x1, top - DECK_T, top, -ZW, ZW, where=fill())
    # the beams under the decks: collision here, drawn as timbers by framing() (ends buried in the frames). (GRID:
    # BEAM_D deep under the planks, the band's upper half; collision over the two cells either side of its middle, a
    # block border; the lower deck's beams in the hold the band's full depth, carlings between them)
    bh = 0.25 if GRID else 0.125              # (the beam's collision reaches this far either side of its middle)
    for bx in main_beams():
        xc = bx + 0.125
        ca, cb = beam_coll(xc, bh)
        d.box("hidden", ca, cb, MAIN - DECK_T - BEAM_D, MAIN - DECK_T, -ZW, ZW, where=fill())
        if MODULAR and OPENING[0] <= ca and cb <= OPENING[1]:
            # ((0.3.20) a beam across the hatchway: cut by it)
            d.box(0, ca, cb, MAIN - DECK_T - BEAM_D, MAIN - DECK_T, OPENING[2], OPENING[3])
        if MODULAR and min(cb, COMPANION[1]) - max(ca, COMPANION[0]) > 0.1 and (
                not COMPANION_WAIST or xc - 1 >= COMPANION[0] - 1e-6 and xc + 1 <= COMPANION[1] + 1e-6):
            # ((0.3.29) the waist companion: only the beam across its well (-2: both cells beside it in the well), out as
            # designed: a segment that starts out (beam_segments); the beam at 0 under its head whole)
            # ((0.3.29) the waist companion's well: its beam a segment that starts out (beam_segments), none cut)
            # ((0.3.23) the beams across the companion's well (-6) or half over its forward end (-4, the stair's head:
            # its forward half the header there): cut by it)
            d.box(0, max(ca, COMPANION[0]), min(cb, COMPANION[1]), MAIN - DECK_T - BEAM_D, MAIN - DECK_T,
                  COMPANION[2], COMPANION[3])
        for o in () if MODULAR else (HATCH, COMPANION):
            if o[0] - 0.25 < bx < o[1]:
                # ((0.3.16) carrack-m: only within the opening; the beam at -4 under the landing keeps its forward half)
                xa_, xb_ = (max(xc - bh, o[0]), min(xc + bh, o[1])) if MODULAR else (xc - bh, xc + bh)
                d.box(0, xa_, xb_, MAIN - DECK_T - BEAM_D, MAIN - DECK_T, o[2], o[3])
    for bx in castle_beams():
        ca, cb = beam_coll(bx + 0.125, bh)
        d.box("hidden", ca, cb, QUARTER - DECK_T - BEAM_D, QUARTER - DECK_T, -ZW, ZW, where=fill())
    for bx in edge_beams():
        # ((0.3.24, notes 184, 185) the beam under each castle deck's new edge: its collision on the castle's side of the
        # edge, nothing of it out over a stair's head cell; (0.3.27) the one voxel column there)
        xa_ = QD_EDGE - (0.25 if BEAM_SEGMENTS else 0.5) if bx < 0 else FC_EDGE
        d.box("hidden", xa_, xa_ + (0.25 if BEAM_SEGMENTS else 0.5), QUARTER - DECK_T - BEAM_D, QUARTER - DECK_T, -ZW, ZW, where=fill())
    for bx in poop_beams():
        ca, cb = beam_coll(bx + 0.125, bh)
        d.box("hidden", ca, cb, POOP - DECK_T - BEAM_D, POOP - DECK_T, -ZW, ZW, where=fill())
    if GRID:
        hx0_, hx1_, hz0_, hz1_ = HATCH
        for bx in lower_beams():
            xc = bx + 0.125
            ca, cb = beam_coll(xc, 0.25)
            d.box("hidden", ca, cb, LOWER - DECK_T - 0.75, LOWER - DECK_T, -ZW, ZW, where=fill())
            if MODULAR:
                if OPENING[0] <= ca and cb <= OPENING[1]:
                    d.box(0, ca, cb, LOWER - DECK_T - 0.75, LOWER - DECK_T, OPENING[2], OPENING[3])
            elif hx0_ - 0.5 < xc < hx1_ + 0.5:
                d.box(0, xc - 0.25, xc + 0.25, LOWER - DECK_T - 0.75, LOWER - DECK_T, hz0_, hz1_)
        # the carlings fore and aft between them, in the band's upper part. ((0.3.24, note 186: "are these longitudinal
        # beams needed? floor above does not have them") carrack-m: none, but along the hatchway's sides (hatches()))
        for zc in () if MODULAR else HOLD_CARLINGS:
            d.box("hidden", -20, 30, LOWER - DECK_T - 0.5, LOWER - DECK_T, zc - 0.125, zc + 0.125, where=fill())
    # the keelson along the middle of the hold, with a step under each mast standing on it (its ends, rising to the
    # stem and the sternpost, are drawn by framing()). (GRID: sunk flush with the hold's floor: its cells are the floor's)
    k_xs = [x for x in np.arange(-16.0, 20.0 + STRETCH, 0.25) if hw(x, HOLD_FLOOR + 0.1) - shell_t(HOLD_FLOOR) > 0.55]
    d.keelson = (min(k_xs), max(k_xs) + 0.25)
    k_lo = HOLD_FLOOR - DECK_T if GRID else HOLD_FLOOR
    if not GRID:
        # (0.3.10, GRID, the user's note 79: "keel beam visible and looks broken": the keelson isn't drawn in the hold, the
        # floor's planks run over it; only the mast steps show, round the masts' heels)
        d.box("timber", d.keelson[0], d.keelson[1], k_lo, KEELSON_TOP, -0.25, 0.25, where=inside)
    for mx in (MASTS[m][0] for m in MASTS):
        if d.keelson[0] < mx - 0.5 and mx + 0.5 < d.keelson[1]:
            d.box("timber", mx - 0.5, mx + 0.5, k_lo, KEELSON_TOP, -0.5, 0.5, where=inside)

    # ---------------------------------------------------------------- castle walls, doors
    d.bulkheads = [(WAIST_AFT - 0.25, WAIST_AFT, MAIN, QUARTER - DECK_T),
                   (WAIST_FWD, WAIST_FWD + 0.25, MAIN, QUARTER - DECK_T),
                   (POOP_FRONT - 0.25, POOP_FRONT, QUARTER, POOP - DECK_T)]
    if MODULAR:
        # (0.3.16, notes 154, 156) carrack-m: no castle front walls in the 2 blocks over each deck (placed pieces stand on
        # the grid line there: modular_layout), only a breast beam across the front in the deck's band over them (where
        # the beams are; it carries the castle deck's edge and closes the band, so a front of two rows of pieces seals),
        # its waist face where the wall's was. Behind the hearth (z 1.5 out to the side) the forecastle's front stays a
        # wall the 2 blocks up: the hearth's bricks stand against it (no place for a piece there: the bricks fill the
        # forecastle's side of the line, and a partition on the line would be in them)
        d.bulkheads = [(x0, x1, y1 - 0.75, y1) for x0, x1, y0, y1 in d.bulkheads]
        # ((0.3.18, note 162) the breast beams on the castles' side of the fronts' grid lines, under the decks' edges
        # there; (note 165) no fixed wall behind the hearth any more: the forecastle's front all pieces)
        d.bulkheads = [(x0 - 0.25, x1 - 0.25, y0, y1) if x0 < 0 else (x0 + 0.25, x1 + 0.25, y0, y1)
                       for x0, x1, y0, y1 in d.bulkheads]
        # (0.3.23, notes 167, 168, 174, 175: "larger than the other floor beams", "this beam could be removed and have
        # equal spacings for beams") no breast beams: each castle deck rests on its ordinary beams only, every 2 on the
        # frames as everywhere (the last one a block in from its front's line: -4 under the quarterdeck, -10 under the
        # poop, 16 under the forecastle deck), its planking carried on a block past it to the front's line (the edge the
        # stairs come up to and the railings stand on, as before). The band under the deck's edge over the front's two rows
        # of pieces is closed by a third row of partitions under the deck (modular_layout), pieces as the rows under them.
        # d.front_ends keeps where the beams were for the inside planking's splits and the deck's end strips (edge_strips)
        d.front_ends = d.bulkheads
        d.bulkheads = []
    for x0, x1, y0, y1 in d.bulkheads:
        d.box("oak", x0, x1, y0, y1, -ZW, ZW, where=inside)
        if GRID:
            # (0.3.12, the user's notes 115 and 116: "different surface or texture that does not paint together and dark
            # horizontal line", the castle fronts' top) the castle deck's cells over each front its boards too (they
            # showed the caulked deck's edge as a band across the front's top, and their strip by the side was in no
            # paint zone): the front's face one surface up to the deck's top
            d.box("oak", x0, x1, y1, y1 + DECK_T, -ZW, ZW, where=(d.grid == d.mid("deck")))
    # the castle fronts' railings (balustrades, drawn by balustrades()): a barrier to walk into
    d.breastworks = [(WAIST_AFT - 0.25, WAIST_AFT, QUARTER), (WAIST_FWD, WAIST_FWD + 0.25, QUARTER),
                     (POOP_FRONT - 0.25, POOP_FRONT, POOP)]
    if MODULAR:
        d.breastworks = []          # ((0.3.16, note 155) railing pieces on the line: modular_layout)
    for x0, x1, y0 in d.breastworks:
        d.box("hidden", x0, x1, y0, y0 + 1.0, -ZW, ZW, where=inside)

    # doorways sized for vanilla doors (1 x 2); the doors themselves are vanilla blocks drawn by the mod
    DOORS = [  # code, wall x0, x1, floor, room side (-1 aft / +1 forward), block
        ("door-greatcabin", WAIST_AFT - 0.25, WAIST_AFT, MAIN, -1, "game:door-sleek-windowed-oak"),
        # (0.3.8, the user's note 52: "door with glass?") windowed like the great cabin's
        ("door-forecastle", WAIST_FWD, WAIST_FWD + 0.25, MAIN, 1,
         "game:door-sleek-windowed-oak" if VARIANT == 3 else "game:door-solid-oak"),
        ("door-poopcabin", POOP_FRONT - 0.25, POOP_FRONT, QUARTER, -1,
         "game:door-sleek-windowed-oak" if VARIANT == 3 else "game:door-solid-oak"),
    ]
    if COMPANION_HOUSE:
        # the companion house's door, on its coaming (the sill), swinging out aft onto the deck (the ladder's well is
        # inside)
        DOORS.append(("door-companion", COMPANION[0] - 0.25, COMPANION[0], MAIN + 0.25, -1, "game:door-solid-oak"))
    if MODULAR:
        DOORS = []                  # ((0.3.16) placed doors in carrack-m: modular_layout)
    if GRID and not MODULAR:
        # (0.3.9, the user's note 55) a second door in the great cabin's front, to port, right at the companion stair's
        # head (z -4.5..-3.5): the way down from the waist; the centre door stays. Open, the lower deck isn't a closed
        # room (fixtures: it unseals it, as the centre one does)
        DOORS.append(("door-companion-cabin", WAIST_AFT - 0.25, WAIST_AFT, MAIN, -1, "game:door-sleek-windowed-oak",
                      (COMPANION[2] + COMPANION[3]) / 2))
    d.paint_tag = "door-casings"
    d.door_boxes = []
    for door in DOORS:
        _, x0, x1, yb = door[:4]
        if GRID:
            # (0.3.13, the user's note 134: "All doors ... are grouped and can only have one color") each door's casing
            # its own paint zone (the great cabin's middle door keeps "door-casings")
            d.paint_tag = DOOR_CASING_ZONE.get(door[0], "door-casings")
        dz = door[6] if len(door) > 6 else 0.0
        d.door_boxes.append((door[0], (x0 - 0.01, yb + 0.03, dz - 0.47), (x1 + 0.01, yb + 1.97, dz + 0.47)))
        d.box(0, x0, x1, yb, yb + 2.0, dz - 0.5, dz + 0.5)
        # frame: jambs and head casing standing a little proud of both faces of the wall (the outboard jamb no further
        # out than the ceiling where the side leans in over a door by it)
        lim = min(ceil_z(x, yb + 2.0) for x in (x0 - 0.06, x1 + 0.06)) - 0.02
        lims = {-1: ceil_z(x0 - 0.06, yb + 2.0) - 0.02, 1: ceil_z(x1 + 0.06, yb + 2.0) - 0.02}
        # (0.3.10, the user's note 71: a door against the leaning side, the companion's, has no outboard jamb casing
        # (no room between its opening and the ceiling: it stood 0.13 into the doorway), its head casing stops at the
        # ceiling)
        lim_h = min(ceil_z(x, yb + 2.2) for x in (x0 - 0.06, x1 + 0.06)) - 0.02
        lims_h = {-1: ceil_z(x0 - 0.06, yb + 2.2) - 0.02, 1: ceil_z(x1 + 0.06, yb + 2.2) - 0.02}
        for face, sgn in ((x0, -1), (x1, 1)):
            cx = face + sgn * 0.03
            if GRID:
                # ((0.3.13, note 142 B) each face's casing by the side as it is on that face: the waist's has room
                # now by the companion's door)
                lim, lim_h = lims[sgn], lims_h[sgn]
            for zc in (dz - 0.56, dz + 0.56):
                if abs(zc) + 0.06 > lim:
                    if GRID:
                        continue
                    zc = math.copysign(lim - 0.06, zc)
                if STAIRWELL and door[0] == "door-companion-cabin" and sgn < 0 and abs(zc - STAIRWELL[3]) < 0.1:
                    continue        # ((0.3.12) inside: the stairwell's wall and its door's opening are there)
                # (0.3.10, clean surfaces: the jambs 0.01 up into the head casing, the head 0.02 past them and the
                # cornice 0.01 down into the head: nothing face to face or in one plane)
                # (its inner face 0.01 back from the doorway's side: it's sunk into the wall)
                zc = zc + math.copysign(0.005, zc - dz)
                d.add_box_mesh("auto", "timber", (cx - sgn * 0.01, yb + 1.01, zc), (0.08, 2.02, 0.11))
            # (the casings' backs 0.02 into the wall: not in the plane of its face or its end strips; the head 0.01 over
            # the doorway's top)
            # ((0.3.13) the cornice's back 0.005 in front of the head's: where the side has room by the companion's door
            # now (note 142 B) they reach out past the wall's last cells, their backs seen from the doorway)
            for xc_, yc_, dx_, dy_, half in ((cx, yb + 2.075, 0.1, 0.13, 0.64), (cx + sgn * 0.025, yb + 2.165, 0.13, 0.07, 0.68)):
                za_, zb_ = dz - half, dz + half
                if GRID:
                    lh_ = lim_h - (0.015 if half > 0.65 else 0.0)        # (the cornice's end short of the head's)
                    za_, zb_ = max(za_, -lh_), min(zb_, lh_)
                d.add_box_mesh("auto", "timber", (xc_, yc_, (za_ + zb_) / 2), (dx_, dy_, zb_ - za_))
    d.paint_tag = None
    d.stair_door = None
    d.partitions = []
    d.pin_rails = []                    # ((0.3.13) the pin boards, for their fixtures: masts_and_rigging)
    if STAIRWELL and not MODULAR:
        # (0.3.12, the user's note 112) the stairwell's walls in the great cabin, boards as the cabin fronts (voxels, from
        # the deck to the deck above's underside, through the quarterdeck's beams' cells: the beams run into them): along
        # its inboard side, and across its after end over the header out to the side (clipped to the ceiling there and
        # its end closed by a strip, edge_strips). A door in the inboard wall at the landing, x -4..-3, hung on its
        # forward end, opening into the cabin to lie along the front wall (the waist door opens out into the waist)
        sx0, sx1, sz0, sz1 = STAIRWELL
        d.box("oak", sx0, sx1, MAIN, QUARTER - DECK_T, sz0, sz1)
        d.box("oak", sx0, sx0 + 0.25, MAIN, QUARTER - DECK_T, sz0, ZW, where=inside)
        d.partitions = [(sx0, sx1, sz0, sz1), (sx0, sx0 + 0.25, sz0, ZW)]
        dx0, dx1 = COMPANION_TOP, sx1
        d.box(0, dx0, dx1, MAIN, MAIN + 2.0, sz0, sz1)
        d.stair_door = {"code": "door-companion-stair", "box": [dx0, MAIN, sz0, dx1, MAIN + 2.0, sz1],
                        # (the hinge a little aft of the front wall's face: open, the leaf lies clear of it)
                        "hinge": [dx1 - 0.1, (sz0 + sz1) / 2], "openAngle": -90.0}
        d.door_boxes.append(("door-companion-stair", (dx0 + 0.03, MAIN + 0.03, sz0 - 0.01),
                             (dx1 - 0.03, MAIN + 1.97, sz1 + 0.01)))
        with d.paint(DOOR_CASING_ZONE["door-companion-stair"]):
            # its casing both faces: the after jamb and the head (no forward jamb: the front wall is there), sunk 0.02 into
            # the wall's faces as the others', the head 0.01 over the doorway and running into the front wall
            for face, sgn in ((sz0, -1), (sz1, 1)):
                cz = face + sgn * 0.03
                d.add_box_mesh("auto", "timber", (dx0 - 0.065, MAIN + 1.01, cz - sgn * 0.01), (0.11, 2.02, 0.08))
                # (in the stairwell they end in the waist door's head and cornice there, 0.005 higher than theirs: not
                # in their planes; in the cabin in the front wall)
                lift, ends = (0.005, (dx1 - 0.075, dx1 - 0.11)) if sgn > 0 else (0.0, (dx1 + 0.01, dx1 + 0.02))
                xa_, xb_ = dx0 - 0.145, ends[0]
                d.add_box_mesh("auto", "timber", ((xa_ + xb_) / 2, MAIN + 2.075 + lift, cz), (xb_ - xa_, 0.13, 0.1))
                xa_, xb_ = dx0 - 0.185, ends[1]
                d.add_box_mesh("auto", "timber", ((xa_ + xb_) / 2, MAIN + 2.165 + lift, cz + sgn * 0.02),
                               (xb_ - xa_, 0.07, 0.14))
    # the windows in the cabins' front bulkheads: glass in them (front_dress), solid, so the cells are collision only
    for room, x0, x1, yb in (("greatcabin", WAIST_AFT - 0.25, WAIST_AFT, MAIN), ("poopcabin", POOP_FRONT - 0.25, POOP_FRONT, QUARTER)):
        h0, h1 = FRONT_WINDOW_Y
        for z0, z1 in () if MODULAR else FRONT_WINDOWS[room]:
            d.box("hidden", x0, x1, yb + h0, yb + h1, z0, z1)
    # the anchor cable comes through the forecastle bulkhead low down, beside the door. (GRID, 0.3.10, the mesh check:
    # no hole in the wall round it, a light leak 0.25 wide into the forecastle; the cable goes into the boards)
    if not GRID:
        d.box(0, WAIST_FWD, WAIST_FWD + 0.25, MAIN, MAIN + 0.25, CABLE_Z - 0.25, CABLE_Z + 0.25)

    # ---------------------------------------------------------------- stairs (starboard only; collision)
    # Steep ship's stairs (0.5 up for a quarter block along, about 63°), straight, against each castle's front; the
    # waist's stair to the quarterdeck in line with the quarterdeck's to the poop, so you come up one and walk aft to the
    # other. Each keeps clear of the frames where the side leans in (tumblehome) at its top. Only the treads are solid:
    # there's room under the high end.
    d.stairs = []
    RUN = 0.25
    stair_defs = (
        ("quarterdeck", WAIST_AFT + 6 * RUN, -1, MAIN, WAIST_AFT, "aft"),      # waist -> quarterdeck, climbing aft
        ("forecastle", WAIST_FWD - 6 * RUN, 1, MAIN, WAIST_FWD, "fwd"),        # waist -> forecastle, climbing forward
        ("poop", POOP_FRONT + 6 * RUN, -1, QUARTER, POOP_FRONT, "aft"))        # quarterdeck -> poop deck, climbing aft

    def stair_z(x_start, direction, floor_y):
        """How far outboard a straight stair can stand: clear of the frames at every step, on the quarter-block grid."""
        zs = []
        for k in range(6):
            xa, xb = sorted((x_start + direction * RUN * k, x_start + direction * RUN * (k + 1)))
            top = floor_y + 0.5 * (k + 1)
            zs.append(math.floor((min(frame_face(x, top + 0.6) for x in (xa, xb)) - 0.05) / 0.25) * 0.25)
        if GRID:
            # (on the grid: its outboard edge on a block border, z = n + 0.5, each stair one cell wide)
            return min(3.5, math.floor(min(zs) - 0.5) + 0.5)
        return min(zs)

    line_z = {}
    for code, x_start, direction, floor_y, x_rail, line in () if MODULAR else stair_defs:
        if GRID:
            line = code                        # (each in its own cells, not in line with another)
        line_z[line] = min(line_z.get(line, 3.5 if GRID else 3.25), stair_z(x_start, direction, floor_y))
        if GRID and code == "quarterdeck":
            # (0.3.10, the waist re-planned: inboard to z 1.5..2.5, in line with the poop stair; the lane outboard of
            # it, z 2.5..4.5, to the companion's door)
            line_z[line] = 2.5
            # (0.3.12, the user's note 121: "stairs to upper deck could be moved more starboard to align better with
            # stairs down") a block outboard, z 2.5..3.5, right beside the companion's door (z 3.5..4.5: the stair can't
            # stand in front of it): as far out as it clears the side at every step (stair_z). Its outboard stringer
            # is beside the door's inboard jamb casing (the stringer's underside 0.6 over the casing's top there); its
            # foot post at x -1.07, the first gun's room from 0.425; the door opens out along its outboard side
            line_z[line] = min(3.5, stair_z(x_start, direction, floor_y))
            # (0.3.13, the user's note 138) a block inboard again, z 1.5..2.5, with the companion's door (z 2.5..3.5)
            line_z[line] = min(COMPANION[2], line_z[line])
    if MODULAR:
        # (0.3.16, note 154) the castle stairs are placed 2 x 1 flights (modular_layout), in A's line z 1.5..2.5, each
        # rising 3 over 2 cells to the castle deck's edge on the front's grid line: the quarterdeck's foot cell -2..-1
        # climbing aft, the poop's -8..-7 climbing aft, the forecastle's 13..14 climbing forward (their feet 0.25 further
        # from the fronts than the built stairs', 6 steps of 0.25 before). Nothing of them is hers: here only where
        # their treads will be (the checks that walk them, the capstan's clearance); the castle deck's edge over each
        # one's top tread cut away (the top tread is that edge)
        # (0.3.19, docs/modular-pieces.md §8) walked stairs, 1 x 3, 12 treads of 0.25: the quarterdeck's foot cell -1..0
        # climbing aft, the poop's -7..-6 climbing aft, the forecastle's 12..13 climbing forward, each head cell's far edge
        # on its front's line (x -3, -9, 15)
        # ((0.3.24, user layout) steep ones, 2 long, in the line z 0.5..1.5: the quarterdeck's foot cell -1..0 climbing aft
        # to its deck's new edge (-2), the poop's -8..-7 climbing aft to the poop front's line (-9), the forecastle's
        # 12..13 climbing forward to its deck's new edge (14))
        for code, foot_x0, direction, floor_y in (("quarterdeck", QD_EDGE + 2.0, -1, MAIN),
                                                   ("poop", POOP_FRONT - 0.25 + 2.0, -1, QUARTER),
                                                   ("forecastle", FC_EDGE - 2.0, 1, MAIN)):
            z0_ = FC_STAIR_Z - 0.5 if code == "forecastle" else 0.5
            steps = flight_steps(foot_x0, direction, floor_y, z0_, z0_ + 1.0, run=2.0)
            d.stairs.append({"code": code, "steps": steps, "dir": direction, "floor": floor_y,
                             "rail_x": (float("nan"),) * 2, "run": FLIGHT_RISER, "modular": True,
                             "flight": (foot_x0, direction, floor_y, z0_, z0_ + 1.0, 3.0, 2.0)})
            # ((0.3.18, note 162) nothing cut: the castle deck's edge is on the front's line, its head cell's far edge)
    for code, x_start, direction, floor_y, x_rail, line in () if MODULAR else stair_defs:
        run = RUN
        z_out = line_z[code if GRID else line]
        steps = []
        for k in range(6):
            xa, xb = sorted((x_start + direction * run * k, x_start + direction * run * (k + 1)))
            top = floor_y + 0.5 * (k + 1)
            steps.append((xa, xb, top, z_out - 1.0, z_out))
            d.box("hidden", xa, xb, top - 0.25, top, z_out - 1.0, z_out)
        # the way through the railing at its head
        z0, z1 = steps[-1][3], steps[-1][4]
        rx0, rx1 = (x_rail - 0.25, x_rail) if direction < 0 else (x_rail, x_rail + 0.25)
        d.box(0, rx0, rx1, floor_y + 3.0, floor_y + 4.0, z0, z1)
        d.stairs.append({"code": code, "steps": steps, "dir": direction, "floor": floor_y, "rail_x": (rx0, rx1), "run": run})

    # ---------------------------------------------------------------- hatches and the companionway (collision)
    hx0, hx1, hz0, hz1 = HATCH
    if MODULAR:
        # ((0.3.20, step B) the hatchways: their cells empty as designed (deck slots), no coamings: a hatch piece lies
        # flush over each)
        for top_ in (MAIN, LOWER):
            d.box(0, OPENING[0], OPENING[1], top_ - DECK_T, top_, OPENING[2], OPENING[3])
    # the waist hatch: a raised coaming round an opening down to the lower deck; the glazed lid is a fitting that's
    # solid while shut
    for _ in () if MODULAR else (0,):
      d.box(0, hx0, hx1, MAIN - DECK_T - 0.25, MAIN, hz0, hz1)
      d.box("oak", hx0 - 0.25, hx1 + 0.25, MAIN, MAIN + 0.5, hz0 - 0.25, hz1 + 0.25)
      d.box(0, hx0, hx1, MAIN, MAIN + 0.5, hz0, hz1)
      # the hold hatch right under it in the lower deck: a low coaming round an opening down to the cargo deck (a ladder
      # goes down it)
      d.box(0, hx0, hx1, LOWER - DECK_T, LOWER, hz0, hz1)
      d.box("oak", hx0 - 0.25, hx1 + 0.25, LOWER, LOWER + 0.25, hz0 - 0.25, hz1 + 0.25)
      d.box(0, hx0, hx1, LOWER, LOWER + 0.25, hz0, hz1)
    # the companionway: an opening against the quarterdeck's front with a coaming, a ship's ladder down it (you go down
    # facing aft), a hood over its after part; (0.3.8) in the carrack a stair in the great cabin
    cx0, cx1, cz0, cz1 = COMPANION
    if MODULAR:
        d.box(0, cx0, cx1, MAIN - DECK_T, MAIN, cz0, cz1)     # ((0.3.18) the well: the deck's planks over its 2 cells)
        # ((0.3.23) 3 cells, a header across its after end, x -7.25..-7, as a beam; (0.3.29) the waist companion: none,
        # its beam a segment)
        if not COMPANION_WAIST:
            d.box("hidden", cx0 - 0.25, cx0, MAIN - DECK_T - BEAM_D, MAIN - DECK_T, cz0, cz1)
    else:
        d.box(0, cx0, cx1, MAIN - DECK_T - 0.25, MAIN, cz0, cz1)
    if CABIN_STAIR:
        # its steps down aft from the deck's level just inside the front wall (only the treads are solid, as the castles'
        # stairs), a header across the opening's after end under the deck (its beams cut by the opening end on
        # carlings along its sides), and a rail round it on the deck, along its inboard side from a little aft of its head
        # and across its after end to the side
        n_steps = int(round((MAIN - LOWER) / COMPANION_RISE))
        foot_x = COMPANION_TOP - n_steps * COMPANION_RUN      # (0.3.12: from the landing's edge, not the front wall)
        steps = []
        for k in range(0 if MODULAR else n_steps):
            xa, xb = foot_x + COMPANION_RUN * k, foot_x + COMPANION_RUN * (k + 1)
            top = LOWER + COMPANION_RISE * (k + 1)
            steps.append((xa, xb, top, cz0, cz1))
            d.box("hidden", xa, xb, top - 0.25, top, cz0, cz1)
        if MODULAR:
            # ((0.3.16) a placed 2 x 1 flight: where its treads will be, nothing of hers; (0.3.23) a walked steep stair,
            # 2 long, its foot cell -6..-5)
            steps = flight_steps(COMPANION_TOP - 2.0, 1, LOWER, cz0, cz1, 3.0, 2.0)
        d.stairs.append({"code": "companion", "steps": steps, "dir": 1, "floor": LOWER, "rail_x": (float("nan"),) * 2,
                         "run": COMPANION_RUN, "open": "none", "rail": "outboard", "modular": MODULAR})
        if MODULAR:
            d.stairs[-1]["flight"] = (COMPANION_TOP - 2.0, 1, LOWER, cz0, cz1, 3.0, 2.0)
        else:
            d.box("hidden", cx0 - 0.25, cx0, MAIN - DECK_T - (BEAM_D if GRID else 0.25), MAIN - DECK_T, cz0, cz1)   # the header
        if not STAIRWELL and not MODULAR:
            ri0, ri1 = sorted((COMP_IN, COMP_IN - COMP_S * 0.25))
            d.box("hidden", cx0 - 0.25, cx1 - COMPANION_HEAD, MAIN, MAIN + 1.0, ri0, ri1)          # the rail inboard
            ra0, ra1 = (ri0, ZW) if COMP_S > 0 else (-ZW, ri1)
            d.box("hidden", cx0 - 0.25, cx0, MAIN, MAIN + 1.0, ra0, ra1, where=inside)             # and aft
    else:
        d.box("oak", cx0 - (0.25 if COMPANION_DOWN > 0 else 0.0), cx1 + 0.25, MAIN, MAIN + 0.25, cz0 - 0.25, cz1 + 0.25)
        d.box(0, cx0, cx1, MAIN, MAIN + 0.25, cz0, cz1)
    if CABIN_STAIR:
        pass
    elif COMPANION_HOUSE:
        # its house: oak walls on the coaming all round, the doorway in its after end (DOORS), a roof (collision; the
        # roof itself and the trim are drawn by companionway())
        d.box("oak", cx0 - 0.25, cx1 + 0.25, MAIN + 0.25, HOUSE_TOP, cz1, cz1 + 0.25)
        d.box("oak", cx0 - 0.25, cx1 + 0.25, MAIN + 0.25, HOUSE_TOP, cz0 - 0.25, cz0)
        d.box("oak", cx1, cx1 + 0.25, MAIN + 0.25, HOUSE_TOP, cz0, cz1)
        d.box("oak", cx0 - 0.25, cx0, MAIN + 2.25, HOUSE_TOP, cz0, cz1)            # over its door
        d.box("hidden", cx0 - 0.25, cx1 + 0.25, HOUSE_TOP, HOUSE_TOP + 0.25, cz0 - 0.25, cz1 + 0.25)
    else:
        ha, hb = (cx0, HOOD_X0) if COMPANION_DOWN < 0 else (HOOD_X0, cx1)     # the hood, over the ladder's foot
        d.box("hidden", ha, hb, MAIN + 0.25, MAIN + 1.25, cz0 - 0.25, cz0)
        d.box("hidden", ha, hb, MAIN + 0.25, MAIN + 1.25, cz1, cz1 + 0.25)
        d.box("hidden", ha, hb, MAIN + 1.0, MAIN + 1.25, cz0 - 0.25, cz1 + 0.25)
        if COMPANION_DOWN > 0:
            d.box("hidden", cx1, cx1 + 0.25, MAIN + 0.25, MAIN + 1.25, cz0 - 0.25, cz1 + 0.25)   # its closed forward end

    if HOLD_STAIR:
        # (0.3.10, the user's note 82) the stair on down from the lower deck to the hold, starboard, beside the
        # companion's foot: its opening through the lower deck and the band under it, its treads (only they're solid),
        # a rail along its inboard side and across its forward end on the lower deck
        hx0s, hx1s, hz0s, hz1s = HOLD_STAIR
        d.box(0, hx0s, hx1s, LOWER - DECK_T - HOLD_BEAM_D, LOWER, hz0s, hz1s)
        if MODULAR:
            # ((0.3.23) a walked stair under it now: nothing of her in the well (the beams over it cut, its ends'
            # headers outside it: the one at its forward end, -3..-2.75, the band's depth; the beam at -6 at its after
            # end, the stair's head, only aft of the well's end))
            d.box("hidden", hx1s, hx1s + 0.25, LOWER - DECK_T - HOLD_BEAM_D, LOWER - DECK_T, hz0s, hz1s)
        steps = []
        for k in range(0 if MODULAR else int(round((LOWER - HOLD_FLOOR) / 0.25))):
            xb_ = HOLD_STAIR_FOOT - 0.25 * k
            top = HOLD_FLOOR + 0.25 * (k + 1)
            steps.append((xb_ - 0.25, xb_, top, hz0s, hz1s))
            d.box("hidden", xb_ - 0.25, xb_, top - 0.25, top, hz0s, hz1s)
        if MODULAR:
            steps = flight_steps(HOLD_STAIR_FOOT, -1, HOLD_FLOOR, hz0s, hz1s, 3.0, 2.0)
        d.stairs.append({"code": "hold", "steps": steps, "dir": -1, "floor": HOLD_FLOOR,
                         "rail_x": (float("nan"),) * 2, "run": 0.25, "open": "none", "rail": "outboard",
                         "modular": MODULAR})
        if MODULAR:
            d.stairs[-1]["flight"] = (HOLD_STAIR_FOOT, -1, HOLD_FLOOR, hz0s, hz1s, 3.0, 2.0)
        if not MODULAR:             # ((0.3.16) carrack-m: railing pieces round its well)
            d.box("hidden", hx0s, hx1s + 0.25, LOWER, LOWER + 1.0, hz0s - 0.25, hz0s)                  # the rail inboard
            d.box("hidden", hx1s, hx1s + 0.25, LOWER, LOWER + 1.0, hz0s - 0.25, ZW, where=inside)      # and forward

    # entry ports: gaps in the waist bulwark, closed by their gates (solid while shut)
    d.box(0, PORT_X[0], PORT_X[1], MAIN, MAIN + WAIST_RAIL, -ZW, ZW, where=shell)
    # gunports: holes through it over the deck (the guns themselves are fixtures, solid where they stand)
    for a, b in GUNPORTS:
        # ((0.3.30) a port square to her side aslant: its hole from its inside's opening to its outside's)
        dx_ = port_skew((a + b) / 2, MAIN)
        d.box(0, a + min(dx_, 0.0), b + max(dx_, 0.0), MAIN + GUNPORT_Y[0], MAIN + GUNPORT_Y[1], -ZW, ZW, where=shell)
    for a, b, y0, y1, s in () if PORT_PANELS else CASTLE_PORTS:   # ((0.3.29) PORT_PANELS: the halves own the hole
        # ((0.3.21, step C) the castle guns' ports; (0.3.22) cut as the waist's:
        dx_ = port_skew((a + b) / 2, y0 - GUNPORT_Y[0])
        d.box(0, a + min(dx_, 0.0), b + max(dx_, 0.0), y0, y1, *((0.0, ZW) if s > 0 else (-ZW, 0.0)), where=shell)   # the mod closes a port while its lid is shut (ShipCompartments); (0.3.23) each on its own side
    for a, b, y0, y1, s in STERN_PORTS:            # ((0.3.25) the stern chasers' ports through the transom, as the castles')
        d.box(0, stern_x(y1) - 0.3, stern_x(y0) + HULL_T + 0.1, y0, y1, a, b, where=shell)

    # ---------------------------------------------------------------- deck fittings (collision)
    d.cylinder_y("hidden", CAPSTAN_X, CAPSTAN_Z, 0.45, MAIN, MAIN + 1.0)
    if not GRID:
        # its spindle, down to a step. (0.3.10, GRID, the user's note 99: "what is this?": it stands on the main deck only)
        d.cylinder_y("hidden", CAPSTAN_X, CAPSTAN_Z, 0.2, LOWER, MAIN - DECK_T)
    d.box("hidden", WHEEL_X + 0.125, WHEEL_X + 0.875, QUARTER, QUARTER + 1.0, -0.25, 0.25)   # wheel posts, drum
    d.box("hidden", WHEEL_X - 0.125, WHEEL_X + 0.125, QUARTER + 0.25, QUARTER + 1.5, -0.5, 0.5)   # the wheel
    if BINNACLE_X is not None:
        d.box("hidden", BINNACLE_X - 0.25, BINNACLE_X + 0.25, QUARTER, QUARTER + 1.25, -0.25, 0.25)   # the binnacle
    # the pump, as high as its brake's fork (nobody steps up onto it from the hatch's coaming beside it)
    d.cylinder_y("hidden", PUMP[0], PUMP[1], 0.2, MAIN, MAIN + 1.5)
    # its pipe down through the lower deck and the hold to the keelson (in the hold the cargo keeps clear of it)
    d.cylinder_y("hidden", PUMP[0], PUMP[1], 0.18, KEELSON_TOP, MAIN - DECK_T, where=fill())
    d.gallows_posts = []
    for gx in GALLOWS_X:
        # the boat gallows' posts: frames carried on up over the bulwark (boat_gallows), solid from the rail up to the
        # beam (below the rail they're the frame, in the side)
        xf = gx + 0.125
        zi, zo = frame_face(xf, MAIN + 0.5), ceil_z(xf, MAIN + 0.5)
        d.gallows_posts.append((xf - FRAME_SIDED / 2, xf + FRAME_SIDED / 2, zi, zo))
        for y in np.arange(rail(xf), GALLOWS_Y - GALLOWS_BEAM - 0.01, 0.25):
            zi, zo = frame_face(xf, y + 0.125), float(hull_hw(xf, y + 0.125))
            # (GRID: the frame on a block border, its collision the cells either side)
            d.box("hidden", xf - (0.25 if GRID else 0.125), xf + (0.25 if GRID else 0.125), y, y + 0.25, zi, zo, mirror=True)
    bb = 0.25 if GRID else 0.125              # (GRID: the posts in the middle of their cells, the cells collision)
    for s in (1, -1):                                                       # the riding bitts
        d.box("hidden", BITTS_X - bb, BITTS_X + bb, MAIN, MAIN + 1.0, s * BITTS_Z - bb, s * BITTS_Z + bb)
    # the hearth: its bricks out to the side (nobody steps into the fire), its stovepipe on the forecastle deck
    hh = hearth()
    if PIECE_HEARTH:
        hh = None                   # ((0.3.18, note 165) carrack-m: the hearth a piece, nothing of it hers)
    else:
      d.box("hidden", hh["x0"], hh["x1"], MAIN, MAIN + HEARTH_SIDES, hh["z0"], ZW, where=fill())
      d.box("hidden", hh["x0"], hh["x1"], MAIN + HEARTH_SIDES, MAIN + HEARTH_BACK, hh["back_in"] - 0.125, ZW, where=fill())
    px_, pz_ = hh["pipe"] if hh else (None, None)
    if hh is None:
        pass
    elif GRID:
        # (0.3.10, the waist re-planned, note 89) no stovepipe over the forecastle deck: the smoke goes up through a
        # grating in it over the hearth (the period's open fogon), the cell the pipe's mouth is under; its cells are
        # collision only (you walk on the grating)
        gx0_, gz0_ = math.floor(px_), math.floor(pz_ - 0.5) + 0.5
        d.grating = (gx0_, gx0_ + 1.0, gz0_, gz0_ + 1.0)
        d.box("hidden", gx0_, gx0_ + 1.0, QUARTER - DECK_T, QUARTER, gz0_, gz0_ + 1.0)
    else:
        d.box("hidden", px_ - bb, px_ + bb, QUARTER, QUARTER + 1.5, pz_ - bb, pz_ + bb)
    if OVEN:
        d.box("hidden", OVEN[0], OVEN[1], MAIN, MAIN + HEARTH_BASE + 1.0, OVEN[2], ZW, where=fill())
    for s in (1, -1):                                                       # the heads
        za_, zb_ = sorted((s * (HEADS_Z - 0.25), s * (HEADS_Z + 0.25))) if GRID else sorted((s * 0.5, s * 0.75))
        d.box("hidden", HEADS_X - 0.25, HEADS_X + 0.25, QUARTER, QUARTER + 0.5, za_, zb_)

    # ---------------------------------------------------------------- masts and the main top (collision)
    for name, (mx, lower, upper, top) in MASTS.items():
        y0, y1, r0, _ = lower
        # only in open air: the decks stay whole around the mast (the mesh covers them)
        d.cylinder_y("hidden", mx, 0.0, r0, y0, y1, where=(d.grid == 0) & (inside | (d.Y >= 3.0)))
        if top:
            ty, tr = top
            d.cylinder_y("hidden", mx, 0.0, tr, ty - 0.25, ty)
            ring_mask = ((X - mx) ** 2 + Z ** 2 >= (tr - 0.25) ** 2)
            d.cylinder_y("hidden", mx, 0.0, tr, ty, ty + TOP_RAIL - 0.05, where=ring_mask)

    # the bowsprit, from its heel in the forecastle up through the forecastle's deck and over it to the stem head (its
    # partners: the deck's cells round it go, it fills them), its step and bitts
    ax_ = BOWSPRIT_B - BOWSPRIT_A
    bt = np.clip(((X - BOWSPRIT_A[0]) * ax_[0] + (Y - BOWSPRIT_A[1]) * ax_[1]) / (ax_ @ ax_), 0.0, 1.0)
    bd = np.sqrt((X - BOWSPRIT_A[0] - bt * ax_[0]) ** 2 + (Y - BOWSPRIT_A[1] - bt * ax_[1]) ** 2 + Z ** 2)
    spar = (bd < 0.3 - 0.15 * bt + 0.02) & (X < SADDLE[0]) & inside
    d.grid[spar & ((d.grid == 0) | (d.grid == d.mid("deck")))] = d.mid("hidden")
    d.box("hidden", FORE_X + MASTS["fore"][1][2] + 0.1, BOWSPRIT_A[0] + 0.55, MAIN, MAIN + 0.25, -0.375, 0.375,
          where=(d.grid == 0))
    for s in (1, -1):
        d.box("hidden", BOWSPRIT_A[0] + 0.75, BOWSPRIT_A[0] + 1.0, MAIN, MAIN + 1.0, min(s * 0.25, s * 0.5),
              max(s * 0.25, s * 0.5), where=(d.grid == 0))

    # ---------------------------------------------------------------- the captain's cabin (collision)
    captain_bed = (-12.9, -(min(hw(x, y) for x in np.linspace(-13.9, -11.9, 5) for y in (3.5, 4.1)) - HULL_T - 0.55))
    if VARIANT != 3:            # (0.3.8: no built-in furniture in the carrack; her people furnish her)
        d.box("hidden", captain_bed[0] - 1.0, captain_bed[0] + 1.0, MAIN, MAIN + 0.5, captain_bed[1] - 0.5,
              captain_bed[1] + 0.5)
        d.box("hidden", -10.75, -9.75, MAIN, MAIN + 1.0, -1.5, -0.5)            # the chart table

    # nothing drawn may reach past the inside planking: those cells are collision only, their edges closed by strips
    clip_to_ceiling(d, inside)
    if MODULAR:
        # ((0.3.16) carrack-m: a breast beam's top cells under a castle deck's cell that is only collision there (past
        # the inside planking) collision only too: the deck's underside strip closes it, not its top face)
        hid, oak_, deck_ = d.mid("hidden"), d.mid("oak"), d.mid("deck")
        for x0, x1, y0, y1 in d.bulkheads:
            i0, i1 = d._rng(x0, x1, 0)
            j = int(round((y1 - 0.125 - d.o[1]) / 0.25 - 0.5))
            for i in range(i0, i1):
                above = d.grid[i, j + 1, :]
                col = d.grid[i, j, :]
                col[(col == oak_) & (above != oak_) & (above != deck_)] = hid

    # ---------------------------------------------------------------- rooms (shelter, warmth, interior lighting)
    def room(code, x0, x1, y0, y1, enclosed=True):
        d.rooms.append({"code": code, "min": [x0, y0, -ZW], "max": [x1, y1, ZW], "enclosed": enclosed,
                        "mask": inside})
    room("hold", -16, 17 + STRETCH, KEEL if GRID else -3.0, LOWER - DECK_T)
    room("lowerdeck", -16, 17 + STRETCH, LOWER, MAIN - DECK_T)
    aft = min(-17.0, stern_x(POOP + 1.0) - 0.25)
    if STAIRWELL:
        # (0.3.12, the user's note 112) the walled stairwell in the great cabin's starboard forward corner: a room of
        # its own, listed before the great cabin (the first room a point is in is its room), from its after wall to
        # the front, its inboard wall out to the side. Open below to the lower deck through the stair's opening; the
        # great cabin is closed off from it by its door. Not "lowerdeck" (a second room of that code would give the
        # mod's wall places on its sides the lower deck's names, the same ids over again); the waist door unseals the
        # lower deck as before (a fitting unseals one room), so the stairwell itself counts as closed
        room("companion", STAIRWELL[0] + 0.25, STAIRWELL[1], MAIN, QUARTER - DECK_T)
        d.rooms[-1]["min"][2] = STAIRWELL[3]
    room("greatcabin", aft, FRONT_AFT, MAIN, QUARTER - DECK_T)
    room("poopcabin", aft, POOP_FRONT - 0.25, QUARTER, POOP - DECK_T)
    room("forecastle", FRONT_FWD, 21 + STRETCH, MAIN, QUARTER - DECK_T)
    if MODULAR:
        # (0.3.16, docs/modular-pieces.md) the rooms walled (partly) by placed pieces: sealed only while the pieces close
        # them (the mod's flood fill), never from the design
        for r in d.rooms:
            if r["code"] in ("companion", "greatcabin", "poopcabin", "forecastle"):
                r["pieces"] = True
        modular_layout(d)

    n_hull = len(d.quads)
    hull(d)
    companion_door_room(d, n_hull)        # ((0.3.13, note 142 B) the side by the companion's door thinner inside)
    rudder_axis = rudder_and_tiller(d)
    masts_and_rigging(d)
    ladders(d)
    helm(d, rudder_axis)
    binnacle(d)
    capstan_and_cable(d)
    anchor_gear(d)
    pump(d)
    boat_gallows(d)
    hatches(d)
    if CABIN_STAIR:
        with d.paint("balustrade-rails"):
            companion_rail(d)
    else:
        companionway(d)
    hold(d)
    framing(d)
    edge_strips(d)
    d.paint_tag = None
    if not MODULAR:                 # ((0.3.16) carrack-m: placed stairs and railings)
        castle_stairs(d)
    with d.paint("balustrade-rails"):
        balustrades(d)
        if MODULAR:
            fixed_rail_ends(d)      # ((0.3.17) none left: every bit of the fronts' breastworks a piece)
    if not PIECE_HEARTH:
        firebox(d)
    if OVEN:
        d.oven_pos = oven(d)
    heads(d)
    with d.paint("stern-gallery"):
        stern_gallery(d)
    with d.paint("cabin-trim"):
        stern_timbers(d)
    with d.paint("side-windows"):
        side_windows(d)
    with d.paint({"tarred": "nameboard", "timber": "stern-carvings"}):
        nameboard(d)
    if GRID:
        # (0.3.9, the user's note 66: "Remove mermaid and its supports") no figurehead: the stem head clear
        d.figureheads = []
    else:
        with d.paint({"gold": "figurehead"}):
            figurehead(d)
    if VARIANT != 3:
        captain_cabin(d, captain_bed)
    if not MODULAR:                 # ((0.3.16) carrack-m: the fronts are placed pieces, no mouldings)
        with d.paint("front-mouldings"):
            front_dress(d)
    entry_gates(d)
    if VARIANT == 3:
        scuppers(d)
    guns(d)
    sails(d)
    fixtures(d, DOORS, captain_bed)
    capstan_clearance(d)
    fit_checks(d)

    slots, area, chests = hold_slots()
    # ((0.3.25, the user: "keep only modular now and park in archive previous") carrack-m is the carrack players get: its
    # design is plainly "Carrack"; carrack and carrack-a are archived (built and loadable, no charter to be had))
    d.props = {
        "name": "Hansa carrack" if HANSA else "Carrack" if CODE == "carrack-m" else "Carrack (companion A)" if CODE == "carrack-a" else {1: "Carrack", 2: "Carrack (deep)", 3: "Carrack (deep, long waist)"}[VARIANT],
        "length": 31.0 + STRETCH if not PROPOSAL else round(bow_x(MAIN) - stern_x(MAIN), 1),
        "beam": round(2 * B * max(v for _, v in SECTION), 2), "draft": round(WATER - (KEEL_BOTTOM if GRID else KEEL), 3),
        "hullHalfWidthAtDeck": round(hw(0.0, MAIN), 1),
        "waterline": WATER,
        "keelY": KEEL,
        "deckY": MAIN,
        "bounds": [[-16.5 - (2.5 if POOP_OVERHANG else 0.0), KEEL_BOTTOM, -7.2], [24.2 + STRETCH, 34.4, 7.2]],
        # sailing model (blocks/s, deg/s)
        "maxSpeed": 7.0, "acceleration": 0.35, "deceleration": 0.5, "turnRate": 5.0, "rudderSpeed": 60.0,
        "reverseSpeed": 0.6,
        # copper sheathing: metal plates it takes, and how much faster the clean bottom makes her
        "claddingPlates": 48, "claddingSpeedBonus": 0.08,
        # points on the hull tested against world blocks: the keel (shallows) and the sides at the waterline and
        # higher up (docks, cliffs, ice)
        "hullSamples": hull_samples(),
        # where drawn waves are held down at the waterline (a rounded box a little bigger than the hull there)
        "waterClip": water_clip(),
        # what the crosshair says she's made of: the hull's parts are counted between the frames (a panel of planking
        # between two frames is what the damage model will keep), the decks you stand on, the masts
        "frames": {"x0": panel_stations()[0], "space": FRAME_SPACE, "count": len(panel_stations())},
        "decks": [{"code": c, "y": y, "x0": a, "x1": b} for c, y, a, b in (
            ("hold", HOLD_FLOOR, -30.0, 40.0), ("lower", LOWER, -30.0, 40.0), ("main", MAIN, -30.0, 40.0),
            # ((0.3.19) carrack-m: the castle decks' edges on the fronts' grid lines, as her collision has them)
            # ((0.3.24, notes 184, 185) the quarterdeck's and the forecastle deck's a block further out: QD_EDGE, FC_EDGE)
            ("quarter", QUARTER, -30.0, QD_EDGE if MODULAR else WAIST_AFT),
            ("forecastle", QUARTER, FC_EDGE if MODULAR else WAIST_FWD, 40.0),
            ("poop", POOP, -30.0, POOP_FRONT - (0.25 if MODULAR else 0.0)),
            ("top", MASTS["main"][3][0], MAIN_X - MASTS["main"][3][1] - 0.25, MAIN_X + MASTS["main"][3][1] + 0.25))],
        "masts": [{"code": n, "x": m[0], "z": 0.0, "radius": m[1][2], "y0": m[1][0], "y1": (m[2] or m[1])[1]}
                  for n, m in MASTS.items()],
        # how she takes damage (the rest are the mod's defaults, DamageDef): what her parts take before they're wrecked
        # (kJ: her oak planking and decks, the rudder, the masts, the canvas, the rigging), her rudder's box, how many
        # work her pump's brake at once
        "damage": {"hullToughness": 80.0, "deckToughness": 60.0, "rudderToughness": 60.0, "mastToughness": 400.0,
                   "sailToughness": 25.0, "riggingToughness": 80.0, "rudder": d.rudder_box, "pumpersPerPump": 2},
    }
    if VARIANT == 3:
        decor_data(d)
    if PORT_SQUARE:
        d.props["gunSweep"] = sweep_model(d)       # ((0.3.30, note 237) for the offline test: NoteTests0330)
    if GRID:
        # (0.3.9, the contract with the mod) the ship's one block grid in her game (ship-local) coordinates: block
        # borders at origin + (i, j, k). x: whole x; y: G 0 (her waterline at G -0.125); z: the centreline in a block's
        # middle. Every floor and wall spot the mod finds is on it (no per-deck alignment), and a piece's top stacks on it
        d.props["grid"] = {"origin": [0.0, round(GRID_OFF - WATER, 4), 0.5]}
        # her roll: hull R is stiff (GM about 2.3: hull-proposal.md §8), a short roll of about 6.3 s (7.5 before)
        d.props["motion"] = {"rollPeriod": 6.3}
    if MODULAR:
        deck_slots(d)               # ((0.3.20, step B, docs/slots.md) the deck cells as slots)
        gun_places(d)               # ((0.3.21, step C, docs/slots.md §7) the guns as places)
        beam_segments(d)            # ((0.3.27, docs/slots.md §10) the beams under wells in lengths that come out)
        port_covers(d)              # ((0.3.28, D2.4) the castle ports' covers: planked over or a port)
    if HULL_SLOTS:
        hull_slots(d)               # ((0.3.26, step D1, docs/slots.md §9) the hull's panels as slots)
    if PORT_SQUARE:
        if not FAST:
            sweep_verify(d)         # ((0.3.30, note 237) every gun clear at its limits of her as she's written)
    rig_check(d)
    print(f"hold: {area:.0f} blocks of floor, {chests} chests two high, {slots} slots")
    return d


# ==================================================================== the hull's planking
def lined_skins(d, fn, levels, x_range, m, mat_of, base, **kw):
    """
    (0.3.29, LINED) The inside planking (the ceiling) of a run of her side: where it's two layers (under a weather deck's
    underside: lined) LINING further in than `base` (the offset skin would have), else as before. Cut into bands of
    height at the weather decks' undersides and, in each, into runs along her where it's two layers or one (at LINED_X);
    each skin recorded with "lined". Returns the aftmost run's rings of each band (the transom's inside starts from them).
    """
    if not LINED:
        return skin(d, fn, levels, x_range, m, mat_of, offset=base, **kw)
    bounds = [-1e9] + list(LINED_X) + [1e9]
    y_s = [float(weather_deck(0.5 * (max(a_, -50.0) + min(b_, 50.0)))) - DECK_T for a_, b_ in zip(bounds[:-1], bounds[1:])]
    cuts_y = sorted(set(y_s))
    edges = [-1e9] + cuts_y + [1e9]
    out = []
    for ylo, yhi in zip(edges[:-1], edges[1:]):
        lv = [y for y in levels if ylo - 1e-6 <= y <= yhi + 1e-6]
        for yb_ in (ylo, yhi):
            if levels[0] < yb_ - 1e-6 < levels[-1] and not any(abs(y - yb_) < 1e-6 for y in lv):
                lv = sorted(lv + [yb_])
        if len(lv) < 2:
            continue
        # the runs along her: neighbouring parts alike (two layers or one) merged
        runs = []
        for i in range(len(bounds) - 1):
            two = y_s[i] >= yhi - 1e-6
            if runs and runs[-1][2] == two:
                runs[-1][1] = bounds[i + 1]
            else:
                runs.append([bounds[i], bounds[i + 1], two])
        first = True
        for lo_x, hi_x, two in runs:
            def xr(y, lo_x=lo_x, hi_x=hi_x):
                a_, b_ = x_range(y)
                return max(a_, lo_x), min(b_, hi_x)
            lv_r = [y for y in lv if xr(y)[1] - xr(y)[0] > 0.05]
            if len(lv_r) < 2:
                continue
            off = (lambda y, b=base: (b(y) if callable(b) else b) + lining_at(y)) if two else base
            r_ = skin(d, fn, lv_r, xr, m, mat_of, offset=off, **kw)
            if getattr(d, "skin_record", None):
                d.skin_record[-1]["lined"] = two
            if first:
                out += r_
                first = False
    return out


def lining_steps(d):
    """
    (0.3.29, LINED) Where the weather deck steps (LINED_X) her side's ceiling is two layers on one side and one on the
    other over the storey between: a face across from the one ceiling to the other at that x (bare oak, facing the one-
    layer side), ring by ring as the skins have them there.
    """
    if not LINED:
        return
    rec = [r for r in getattr(d, "skin_record", []) if r.get("inward") and "lined" in r]
    with d.paint("inside"):
        for xb in LINED_X:
            for side in (1, -1):
                zl, zt = {}, {}
                for r in rec:
                    for (ry, rx, rz), run in zip(r["rings"], r["runs"]):
                        if run is None or not (run[0] - 1e-6 <= xb <= run[1] + 1e-6):
                            continue
                        k = np.nonzero(np.abs(rx - xb) < 1e-6)[0]
                        if len(k):
                            (zl if r["lined"] else zt).setdefault(round(float(ry), 5), float(rz[k[0]]))
                ys = sorted(y for y in zl if y in zt and zt[y] - zl[y] > 0.05)
                # (the one-layer side: where the weather deck over the next part is lower)
                face_dir = 1.0 if float(weather_deck(xb - 0.5)) > float(weather_deck(xb + 0.5)) else -1.0
                for ya, yb in zip(ys[:-1], ys[1:]):
                    if yb - ya > 1.01:
                        continue
                    pts = [(xb, ya, side * zl[ya]), (xb, ya, side * zt[ya]), (xb, yb, side * zt[yb]), (xb, yb, side * zl[yb])]
                    n_q = len(d.quads)
                    _quad_facing(d, "auto", "oak", pts, (face_dir, 0, 0), (0, 0, 16, 16))
                    if len(d.quads) > n_q:
                        d.quads[-1]["lining_step"] = True


def hull(d):
    # Strakes from the keel to the main deck, equally wide around the midships section (so the flat bottom gets as
    # many strakes as the sides, like real planking), then quarter-block strakes (the openings line up with them).
    low_levels = strake_levels()
    waist_levels = low_levels + [MAIN + 0.25 * k for k in range(1, int(round(WAIST_RAIL / 0.25)) + 1)]
    d.side_levels = waist_levels   # the entry port gates reuse them

    def full_length(y):
        return stern_x(y), bow_x(y)

    # ((0.3.24, notes 184, 185) carrack-m: the quarterdeck's and the forecastle's sides full height out to their decks'
    # new edges, their breaks shorter: QA, FA, BLF; else the fronts' and BREAK_L as before)
    QA, FA, BLF = WAIST_AFT + EDGE_OUT, WAIST_FWD - EDGE_OUT, BREAK_L_FRONT

    def sterncastle(y):
        return stern_x(y), QA

    def sterncastle_low(y):
        # (0.3.8) under the poop's overhang, up to its underside: from the hull's own transom. stern_x(OH_Y) is the
        # overhang's after end, so the top of these strakes ran on aft across the transom to there: skewed strakes
        # outside under the overhang (holes, smeared texture: the user's notes 33-36) and inside along the great
        # cabin's ceiling (its "weird triangles", note 47)
        return base_stern_x(y), QA

    def stern_range(levels):
        return sterncastle if levels[0] >= OH_Y - 1e-6 else sterncastle_low

    def outer_forecastle(y):
        return FA, outer_bow_x(y)

    def quarter(y):
        return POOP_FRONT + BREAK_L, QA

    def forecastle(y):
        return FA, bow_x(y)

    def poop(y):
        return stern_x(y), POOP_FRONT

    def tarred(j, y0, y1):
        return "tarred"

    def oak(j, y0, y1):
        return "oak"

    castle_low = list(np.arange(MAIN + WAIST_RAIL, FC_Y0 + 1e-6, 0.25))      # 4.75 .. 6.0 (4.5 .. before 0.3.8)
    castle_high = list(np.arange(FC_Y0, QUARTER + 1.0 + CASTLE_UP + 1e-6, 0.25))    # 6.0 .. 7.5
    poop_levels = list(np.arange(QUARTER + 1.0 + CASTLE_UP, POOP + 1.0 + CASTLE_UP + 1e-6, 0.25))
    stern_castle = castle_low + castle_high[1:]
    # outside, the forecastle's sides step out from FC_LOW (its raked underside), not FC_Y0
    fc_low_out = list(np.arange(MAIN + WAIST_RAIL, FC_LOW + 1e-6, 0.25))
    fc_high_out = list(np.arange(FC_LOW, QUARTER + 1.0 + CASTLE_UP + 1e-6, 0.25))
    # the stern castle's sides: under the poop's overhang (the great cabin) and over it (the poop cabin); in one go
    # when there's no overhang
    aft_w = half_width if POOP_OVERHANG else hull_hw
    if POOP_OVERHANG:
        st_parts = [[y for y in stern_castle if y <= OH_Y + 1e-6], [y for y in stern_castle if y >= OH_Y - 1e-6]]
    else:
        st_parts = [stern_castle]

    # (0.3.8) paint: the outside planking is sorted by where it is (the wales, the topsides, the frieze, the castles'
    # sides: paint_zones.outside_zone), the inside by the room it's in; the rest says what it is
    d.paint_tag = "outside"
    # below the waterline: its own group, so the mod can draw copper sheathing instead of tar
    below = [y for y in low_levels if y <= COPPER_TOP + 1e-6]
    above = [y for y in low_levels if y >= below[-1] - 1e-6] + waist_levels[len(low_levels):]
    d.gate_band0 = min(range(len(above)), key=lambda k: abs(above[k] - MAIN))   # the strake from MAIN up, in `above`
    # (0.3.9, the user's note 69) the frieze, the band under the rail cap, runs on up each castle break's sweep: there
    # the two strakes under the level rail (the waist's, the quarterdeck's) are left to the break's own planking
    # (sweep_patch), which is cut along the band
    BAND = 0.5 if GRID else 0.0
    out_cuts = list(SIDE_CUTS)
    if GRID:
        out_cuts += [{"x0": QA, "x1": QA + BLF, "y0": MAIN + WAIST_RAIL - BAND, "y1": MAIN + WAIST_RAIL,
                      "side": 0},
                     {"x0": FA - BLF, "x1": FA, "y0": MAIN + WAIST_RAIL - BAND, "y1": MAIN + WAIST_RAIL,
                      "side": 0},
                     {"x0": POOP_FRONT, "x1": POOP_FRONT + BREAK_L, "y0": QUARTER + 1.0 + CASTLE_UP - BAND, "y1": QUARTER + 1.0 + CASTLE_UP,
                      "side": 0}]
    # ((0.3.26, step D1) carrack-m: every ring with a point at each frame strip's edges and each frame space's middle in
    # her run (planking.xgrid_rings), so the planking is cut square across at the panels' edges)
    XG = {"xgrid": frame_strips()} if HULL_SLOTS else {}
    skin(d, hull_hw, below, full_length, 64, tarred, group="bottom", bottom=True, **XG)
    skin(d, hull_hw, below, full_length, 64, lambda j, a, b: "copper", group="bottomclad", bottom=True, seed=3, **XG)
    skin(d, hull_hw, above, full_length, 64, tarred, cuts=out_cuts, **XG)
    for levels in st_parts:
        skin(d, aft_w if levels[0] >= OH_Y - 1e-6 else hull_hw, levels, stern_range(levels), 22, tarred, cuts=out_cuts, **XG)
    skin(d, hull_hw, fc_low_out, forecastle, 16, tarred, cuts=SIDE_CUTS, **XG)
    skin(d, half_width, fc_high_out, outer_forecastle, 24, tarred, cuts=SIDE_CUTS, **XG)
    skin(d, aft_w, poop_levels, poop, 12, tarred, cuts=SIDE_CUTS, **XG)
    d.paint_tag = "forecastle-sides"
    # underside of the forecastle where it overhangs the bow
    if FC_LOW == FC_Y0:
        flat_between(d, FC_Y0, FC_X0, FC_TIP, lambda x: hull_hw(x, np.full_like(x, FC_Y0)),
                     lambda x: half_width(x, np.full_like(x, FC_Y0)), "tarred")
    else:
        raked_underside(d)
    d.paint_tag = "sterncastle-sides"
    if POOP_OVERHANG:
        # underside of the poop's overhang: the quarterdeck carried out over the transom, on knees
        overhang_underside(d)
        with d.paint("castle-mouldings"):
            overhang_brackets(d)

    # the castle breaks: the sheer sweeps up from the waist to each castle, planked outside and in
    breaks = [
        (QA, QA + BLF, [(aft_w if lv[0] >= OH_Y - 1e-6 else hull_hw, lv) for lv in st_parts], QA),
        (FA - BLF, FA, [(hull_hw, fc_low_out), (half_width, fc_high_out)], FA),
        (POOP_FRONT, POOP_FRONT + BREAK_L, [(aft_w, poop_levels)], POOP_FRONT),
    ]
    for x0, x1, parts, steep in breaks:
        for pi_, (fn, levels) in enumerate(parts):
            # (0.3.8: the planking stops 0.04 under the cap's top, 0.02 before: where the sweep is steep the cap's top
            # face, square to the hull's line, dipped under the planking's top between its corners, dark slivers of
            # tarred plank showing through it along its outer edge, the user's note 43)
            out_levels = levels
            if GRID and pi_ == 0:
                # (0.3.9) from the two strakes under the level rail it rises from, cut along the frieze's band
                low = levels[0]
                out_levels = [low - BAND, low - BAND / 2] + list(levels)
            n_sw = len(d.quads)
            with d.paint("outside-sweep"):
                sweep_patch(d, fn, out_levels, x0, x1, lambda x: rail(x) - 0.04, tarred, dense_at=steep,
                            bands_below=(0.21, 0.46) if GRID else ())
            with d.paint("inside"):
                fn_in = fn
                if GRID:
                    # (0.3.13, the user's note 129: "Texture misalignment or spill" by the forecastle's front) on the
                    # side's own ceiling (ceil_z, as the inside planking under it and the castle's are): HULL_T straight
                    # in across from the outside left it up to 0.1 further out than the planking under it where the bow
                    # narrows, a dark ledge along its foot and a slot up its end at the wall
                    def fn_in(x, y):
                        return ceil_z_raw(x, y) + HULL_T
                sweep_patch(d, fn_in, levels, x0, x1, lambda x: rail(x) - 0.04, oak, group="auto", offset=HULL_T,
                            dense_at=steep)
            for q in d.quads[n_sw:]:
                q["sweep"] = True           # ((0.3.26) a castle break's planking: fixed, not panels)

    d.paint_tag = "transom"
    # the flat transom, windows cut out
    stern_levels = sorted(set([round(y, 5) for y in low_levels + waist_levels + castle_low + castle_high + poop_levels
                               if y >= -0.75]))
    stern_below = [y for y in stern_levels if y <= COPPER_TOP + 1e-6]
    stern_above = [y for y in stern_levels if y >= stern_below[-1] - 1e-6]
    transom(d, stern_below, stern_x, transom_hw, "tarred", group="bottom")
    transom(d, stern_below, stern_x, transom_hw, "copper", group="bottomclad")
    if POOP_OVERHANG:
        # the hull's own transom up to the overhang, and the overhanging poop's above it
        lo = [y for y in stern_above if y <= OH_Y + 1e-6]
        hi = [y for y in stern_above if y >= OH_Y - 1e-6]
        transom(d, lo, base_stern_x, transom_hw, "tarred", cuts=[c for c in STERN_CUTS if c["y1"] <= OH_Y + 1e-6])
        transom(d, hi, stern_x, transom_hw, "tarred", cuts=[c for c in STERN_CUTS if c["y0"] >= OH_Y - 1e-6])
        d.transom_hi = hi           # ((0.3.25) its planks' bands: the stern chasers' lids in line with them)
    else:
        transom(d, stern_above, stern_x, transom_hw, "tarred", cuts=STERN_CUTS)

    d.paint_tag = "wales"
    # wales, sweeping up toward the ends; rail caps (reaching inboard over the top of the bulwarks). (GRID, the user's
    # note 67: each wale one strake of the planking, its edges the strake's, along her as the planks run)
    for k_, (y0, y1) in enumerate(wale_bands()):
        # (0.3.10, clean surfaces: where two wales are strakes next to each other, the upper one starts 0.01 up: their
        # faces met back to back and fought where the stern turns them apart)
        if GRID and k_ > 0 and abs(y0 - wale_bands()[k_ - 1][1]) < 1e-6:
            y0 += 0.01
        raised_strip(d, hull_hw, y0, y1, full_length, 64, "tarred", out=0.1, sheer=None if GRID else sheer)
    d.paint_tag = "castle-mouldings"
    qy = QUARTER + (0.25 if POOP_OVERHANG else 0.0)       # over the overhang's underside, not on its edge
    raised_strip(d, aft_w, qy - 0.25, qy, sterncastle, 22, "tarred", out=0.08)
    raised_strip(d, aft_w, POOP - 0.25, POOP, poop, 12, "tarred", out=0.08)
    raised_strip(d, half_width, FC_Y0, FC_Y0 + 0.25, forecastle, 24, "tarred", out=0.08)
    d.paint_tag = "rail-caps"
    # (0.3.7: the cap stops against each boat gallows' post, which is its frame carried on up through it: the cap ran
    # across it before, the post standing on the cap and the frame under it, the user's note 7)
    waist_cap = raised_strip(d, hull_hw, MAIN + WAIST_RAIL - 0.25, MAIN + WAIST_RAIL,
                             lambda y: (QA + BLF, FA - BLF),
                             24, "oak", out=0.07, inner=HULL_T + 0.08,
                             # (0.3.9, the user's notes 62, 63: it stops exactly at the entry port's edges, its ends
                             # closed, clear of the shut gate's own piece of cap)
                             cuts=[{"x0": PORT_X[0], "x1": PORT_X[1]} if not GRID else
                                   {"x0": PORT_X[0], "x1": PORT_X[1], "exact": True, "wall": HULL_T}] +
                             [{"x0": a, "x1": b, "exact": True, "wall": HULL_T} for a, b in GUNPORTS if not LIDDED] +
                             [{"x0": gx + 0.01, "x1": gx + FRAME_SIDED - 0.01, "exact": True, "wall": HULL_T}
                              for gx in GALLOWS_X])
    caps = {"waist": waist_cap}
    caps["quarter"] = raised_strip(d, aft_w, QUARTER + 0.75 + CASTLE_UP, QUARTER + 1.0 + CASTLE_UP, quarter, 8, "oak", out=0.07, inner=HULL_T + 0.08)
    caps["forecastle"] = raised_strip(d, half_width, QUARTER + 0.75 + CASTLE_UP, QUARTER + 1.0 + CASTLE_UP, forecastle, 24, "oak", out=0.07,
                                      inner=HULL_T + 0.08)
    # (the poop's from a little forward of the transom: the taffrail's corner piece carries it on to its mitre)
    caps["poop"] = raised_strip(d, aft_w, POOP + 0.75 + CASTLE_UP, POOP + 1.0 + CASTLE_UP, lambda y: (stern_x(y) + TAFFRAIL_CORNER, POOP_FRONT),
                                12, "oak", out=0.07, inner=HULL_T + 0.08)
    # the caps along the castle breaks' sweeps, starting and ending exactly on the caps either side of them (0.3.7:
    # before, they were offset straight out from a little lower on the hull, and their ends didn't meet the caps': a
    # sliver of a gap on top, the user's note 6)
    for (x0, x1, _, steep), (a, b) in zip(breaks, (("quarter", "waist"), ("waist", "forecastle"), ("poop", "quarter"))):
        sweep_cap(d, half_width, x0, x1, rail, "oak", inner=HULL_T + 0.08, dense_at=steep, depth=0.27, in_depth=0.16,
                  start=caps[a]["end"], end=caps[b]["start"])
    d.paint_tag = "stern-carvings"
    taffrail(d, caps["poop"]["start"])

    d.paint_tag = "inside"
    # ---------------------------------------------------------------- the planked hull, inside (ceiling)
    in_levels = [y for y in waist_levels if y >= HOLD_FLOOR - 1e-6]     # down to the cargo deck in the hold
    if in_levels[0] > HOLD_FLOOR + 1e-6:
        in_levels = [HOLD_FLOOR] + in_levels
    if HULL_SLOTS:
        # ((0.3.26) no sliver of a strake over the cargo deck: its ring 0.024 over the floor's, sampled each its own way at
        # the bow, lay over the floor's ring there)
        in_levels = [HOLD_FLOOR] + [y for y in in_levels if y > HOLD_FLOOR + 0.06]
    walls = [x for b in d.bulkheads + getattr(d, "front_ends", []) for x in b[:2]]   # ((0.3.23) carrack-m: where the breast beams were)
    rings_in = lined_skins(d, hull_hw, in_levels, full_length, 64, oak, shell_t if GRID else HULL_T, group="auto",
                           cuts=SIDE_CUTS, splits=walls, **XG)
    d.gate_band0_in = min(range(len(in_levels)), key=lambda k: abs(in_levels[k] - MAIN))   # the gates' inside strakes
    rings_st = []
    for levels in st_parts:
        rings_st.append(lined_skins(d, aft_w if levels[0] >= OH_Y - 1e-6 else hull_hw, levels, stern_range(levels), 22, oak,
                                    HULL_T, group="auto", cuts=SIDE_CUTS, splits=walls, **XG))
    n_fc = len(d.quads)
    lined_skins(d, hull_hw, castle_low, forecastle, 16, oak, HULL_T, group="auto", cuts=SIDE_CUTS, **XG)
    lined_skins(d, half_width, castle_high, forecastle, 24, oak, HULL_T, group="auto", cuts=SIDE_CUTS, **XG)
    if MODULAR:
        # ((0.3.16) carrack-m: the forecastle's inside planking set in square to the side starts a hair aft of its front's
        # waist face, over the break's planking there: the wall hid that; with only pieces on the line it shows. Its ends
        # brought to the front's face)
        for q in d.quads[n_fc:]:
            q["pts"] = [(max(p_[0], FA), p_[1], p_[2]) for p_ in q["pts"]]
    # ((0.3.18) carrack-m: split at the poop front's breast beam, on the cabin's side of its line now: the bit past it
    # is the quarterdeck's side, painted with it)
    rings_pp = lined_skins(d, aft_w, poop_levels, poop, 12, oak, HULL_T, group="auto", cuts=SIDE_CUTS,
                           **({"splits": walls} if MODULAR else {}), **XG)
    lining_steps(d)             # ((0.3.29) where the weather deck steps: from the two layers' ceiling to the one's)
    # inside of the transom, where the inside planking of the sides starts (under the poop's overhang and over it)
    groups = [rings_in + rings_st[0] + (rings_pp if len(rings_st) == 1 else [])]
    if len(rings_st) > 1:
        groups.append(rings_st[1] + rings_pp)
    for rings in groups:
        firsts = sorted({round(y, 5): (float(x[0]), float(z[0])) for y, x, z in rings}.items())
        fy = [y for y, _ in firsts]
        transom(d, fy, lambda y, fy=fy, f=firsts: float(np.interp(y, fy, [q[0] for _, q in f])),
                lambda y, fy=fy, f=firsts: float(np.interp(y, fy, [q[1] for _, q in f])), "oak", group="auto",
                inward=True, cuts=[c for c in STERN_CUTS if fy[0] - 1e-6 <= c["y0"] and c["y1"] <= fy[-1] + 1e-6])

    # reveals: the thickness of the hull around each window and entry port
    def side_reveals(x0, x1, y0, y1, with_top_bottom=True, sill=False, jamb_mat="oak", sides=(1, -1), bleed=0.0, skew=0.0):
        # ((0.3.23) bleed: the reveal carried that far into the planking's faces either side, over the few mm the
        # strakes' facets lie off the exact line where her side curves (a castle port's slit into the hull's thickness))
        # ((0.3.30) skew: a gun port square to her side aslant: its inside edges that far along x from its outside's, on
        # the inside planking there)
        for s in sides:
            def P(x, y, inner):
                if inner and abs(skew) > 1e-9:
                    return (x + skew, y, s * (float(ceil_z_raw(x + skew, y)) - bleed))
                z = hw(x, y) - (HULL_T + bleed if inner else -bleed)
                return (x, y, s * z)
            quads = [
                ([P(x0, y0, False), P(x0, y0, True), P(x0, y1, True), P(x0, y1, False)], jamb_mat),   # aft jamb, faces +x
                ([P(x1, y0, True), P(x1, y0, False), P(x1, y1, False), P(x1, y1, True)], jamb_mat),   # forward jamb, faces -x
            ]
            if with_top_bottom or sill:
                quads.append(([P(x0, y0, True), P(x0, y0, False), P(x1, y0, False), P(x1, y0, True)], "oak"))  # sill, up
            if with_top_bottom:
                quads.append(([P(x0, y1, False), P(x0, y1, True), P(x1, y1, True), P(x1, y1, False)], "oak"))  # head, down
            for q, mat in quads:
                if s < 0:
                    q = [q[1], q[0], q[3], q[2]]
                reveal(d, *q, mat)

    d.paint_tag = {"tarred": "topsides", "oak": "inside"}
    for x0, x1, y0, y1 in SIDE_WINDOWS:
        side_reveals(x0, x1, y0, y1)
    # the entry ports: their edges tarred like the side (a hair of them shows past a shut gate), a sill across the
    # bulwark's thickness at the deck
    side_reveals(PORT_X[0], PORT_X[1], MAIN, MAIN + WAIST_RAIL, with_top_bottom=False, sill=True, jamb_mat="tarred")
    # the gunports lined with bare oak: their sides, sill and (since 0.3.8, the bulwark whole over them) lintel
    for a, b in GUNPORTS:
        side_reveals(a, b, MAIN + GUNPORT_Y[0], MAIN + GUNPORT_Y[1], with_top_bottom=LIDDED, sill=True,
                     skew=port_skew((a + b) / 2, MAIN))
    for a, b, y0, y1, s in () if PORT_PANELS else CASTLE_PORTS:     # ((0.3.29) the port's own cheeks: hull_slots)
        side_reveals(a, b, y0, y1, with_top_bottom=True, sill=True, sides=(s,), bleed=0.006,
                     skew=port_skew((a + b) / 2, y0 - GUNPORT_Y[0]))
    # ((0.3.25) the stern chasers' ports lined as the windows: their sides, sill and head through the transom)
    for z0, z1, y0, y1 in STERN_WINDOWS + [p[:4] for p in STERN_PORTS]:
        xo0, xo1 = stern_x(y0), stern_x(y1)
        xi0, xi1 = xo0 + HULL_T, xo1 + HULL_T
        quads = [
            [(xo0, y0, z0), (xi0, y0, z0), (xi1, y1, z0), (xo1, y1, z0)],   # side at z0, faces +z
            [(xi0, y0, z1), (xo0, y0, z1), (xo1, y1, z1), (xi1, y1, z1)],   # side at z1, faces -z
            [(xo0, y0, z1), (xi0, y0, z1), (xi0, y0, z0), (xo0, y0, z0)],   # sill, faces up
            [(xo1, y1, z0), (xi1, y1, z0), (xi1, y1, z1), (xo1, y1, z1)],   # head, faces down
        ]
        for q in quads:
            reveal(d, *q, "oak")
    d.paint_tag = None


TAFFRAIL_CORNER = 0.3          # how far forward of the transom the poop's side caps start: the corner block's length


def cap_run(d, mat, sections, group="exterior"):
    """
    A rail cap along sections [outboard face's foot, outboard top, inboard top, inboard face's foot] (one after another
    along it): its outboard face, top, inboard face and underside, each piece's faces showing a band of a plank along it
    as the side's caps (raised_strip) do. Its ends open (they meet something else).
    """
    secs = [[np.asarray(p, float) for p in sec] for sec in sections]
    for a, b in zip(secs[:-1], secs[1:]):
        ca, cb = np.mean(a, axis=0), np.mean(b, axis=0)
        for i in range(4):
            q = [a[i], b[i], b[(i + 1) % 4], a[(i + 1) % 4]]
            length = max(float(np.linalg.norm(b[i] - a[i])), float(np.linalg.norm(b[(i + 1) % 4] - a[(i + 1) % 4])))
            _quad_facing(d, group, mat, q, np.mean(q, axis=0) - (ca + cb) / 2, (0, 5.5, min(16, length * 16), 11),
                         keep_uv=True)


def taffrail(d, start):
    """
    (0.3.8) The taffrail across the top of the transom in the side caps' own section and at their height, and a corner
    block at each quarter that both run into, its top flush with theirs, its outboard faces flush with the taffrail's
    aft face and the side cap's outboard face: one rail round the poop at one height, square at its corners. (The hull's
    line turns in sharply at the transom's corner, the quarter's tuck, too sharply for a cap to follow it round or to
    mitre on it.) start: the side cap's first section (raised_strip's), TAFFRAIL_CORNER forward of the transom. (0.3.7:
    the taffrail was a stouter cap 0.06 higher, the side caps running into its fore side under its top: "still bad joint
    in side boards, also different heights", the user's note 44.)
    """
    y0, y1 = POOP + 0.75 + CASTLE_UP, POOP + 1.0 + CASTLE_UP
    top = start["out"][1]

    def xo(y):          # its aft face: 0.07 out from the transom's planking, as the side caps stand out from the side's
        return stern_x(y) - 0.07

    def xi(y):          # its fore face: 0.08 in from the transom's inside planking, as theirs
        return stern_x(y) + HULL_T + 0.08

    za = min(start["in"][2], start["in_lo"][2]) - 0.25      # where it meets the corner blocks
    zs = np.linspace(za, -za, max(2, int(math.ceil(2 * za / 0.9)) + 1))
    cap_run(d, "oak", [[(xo(y0), y0, z), (xo(y1), top, z), (xi(y1), top, z), (xi(y1 - 0.14), y1 - 0.14, z)]
                       for z in zs])
    zo, zo_lo = start["out"][2], start["out_lo"][2]
    tops = [(xo(y1), top, za), (xi(y1), top, za), start["in"], start["out"], (xo(y1), top, zo)]
    feet = [(xo(y0), y0, za), (xi(y1 - 0.14), y1 - 0.14, za), start["in_lo"], start["out_lo"], (xo(y0), y0, zo_lo)]
    for s in (1, -1):
        T = [np.array([p[0], p[1], s * p[2]]) for p in tops]
        F = [np.array([p[0], p[1], s * p[2]]) for p in feet]
        c = np.mean(T + F, axis=0)
        d.add_planar("exterior", "oak", T, (0, 1.0, 0), u_dir=(1.0, 0, 0))
        # its aft and outboard faces, and inboard where it shows between the taffrail's and the side cap's ends
        for i, j in ((4, 0), (3, 4), (1, 2)):
            q = [T[i], T[j], F[j], F[i]]
            _quad_facing(d, "exterior", "oak", q, np.mean(q, axis=0) - c)
        for i in (1, 2, 3):
            q = [F[0], F[i], F[i + 1], F[i + 1]]
            _quad_facing(d, "exterior", "oak", q, (0, -1.0, 0))
    # where the stern lantern's post stands, and the outboard corner's z
    d.taffrail = (xo(y1), xi(y1), y0, top, zo)


def _ledge(d, y, outer, inner, x0, x1, margin, mat="tarred"):
    """
    A flat underside at height y between two starboard outlines (mirrored to port): out to `outer` exactly and in to
    `inner` less `margin` (over the top of the planking under it, into its thickness, so no slit shows where they meet).
    outer and inner are (x[], z[]) polylines (the strakes' own rings, so the edges are the planking's edges, not another
    line through the same curve); inner None: from the centreline. Its planks fore and aft, straight, textured by their
    place in its plane like a deck's cells (Design.add_planar), in strips between every corner of either outline.
    """
    xs = {x0, x1}
    for ln in (outer, inner):
        if ln is not None:
            xs.update(float(x) for x in ln[0] if x0 < x < x1)
    xs = sorted(xs)
    for xa, xb in zip(xs[:-1], xs[1:]):
        if xb - xa < 1e-6:
            continue
        zo = [float(np.interp(x, outer[0], outer[1])) for x in (xa, xb)]
        if inner is None:
            zi = [0.0, 0.0]
        else:
            zi = [max(0.0, float(np.interp(x, inner[0], inner[1])) - margin) for x in (xa, xb)]
        if zo[0] - zi[0] < 1e-4 and zo[1] - zi[1] < 1e-4:
            continue
        for s in (1, -1):
            poly = [(xa, y, s * zi[0]), (xb, y, s * zi[1]), (xb, y, s * zo[1]), (xa, y, s * zo[0])]
            d.add_planar("exterior", mat, poly, (0, -1.0, 0), u_dir=(1.0, 0, 0))


def raked_underside(d, margin=0.05):
    """The forecastle's underside outside where it overhangs the bow: level at FC_LOW along the sides aft of the stem
    (between the bow's planking and the forecastle's sides stepping out over it), then rising straight to FC_Y0 at the
    tip, from the centreline out to its sides. (0.3.8) Its planks fore and aft, straight, textured by their place in its
    planes; out to the sides' own bottom edge (their strakes' corners) and in over the top of the bow's planking by
    `margin`. Until 0.3.8 it was strips across her, each one band of a plank stretched up to three blocks across ("dark
    stretched triangles", the user's note 38), their inner edge a coarser line than the bow's planking's top (a slit of
    light by the stem, note 37)."""
    from planking import ring
    xs0 = float(interp(FC_LOW, BOW_X))                  # where the stem meets FC_LOW
    outer = ring(half_width, FC_LOW, WAIST_FWD, outer_bow_x(FC_LOW), 24)    # the sides' bottom strake's lower edge
    if MAIN + WAIST_RAIL >= FC_LOW - 1e-6:
        inner = ring(hull_hw, FC_LOW, stern_x(FC_LOW), bow_x(FC_LOW), 64)  # the waist's strakes' top edge, all along her
    else:
        inner = ring(hull_hw, FC_LOW, WAIST_FWD, bow_x(FC_LOW), 16)        # the bow's top strake's upper edge
    _ledge(d, FC_LOW, outer, inner, FC_X0, xs0, margin)
    # forward of the stem, raked: the sides' bottom edge is the ends of their strakes, each on the rake (outer_bow_x)
    edge = [(xs0, FC_LOW, float(outer[1][-1]))]
    for y in np.arange(FC_LOW + 0.25, FC_Y0 + 1e-6, 0.25):
        x = outer_bow_x(float(y))
        edge.append((x, float(y), float(half_width(np.array(x), np.array(float(y))))))
    m = (FC_Y0 - FC_LOW) / (FC_TIP - xs0)
    nrm = np.array([m, -1.0, 0.0])
    for s in (1, -1):
        poly = [(xs0, FC_LOW, 0.0)] + [(x, y, s * z) for x, y, z in edge] + [(FC_TIP, FC_Y0, 0.0)]
        # (the last strake's end is at the tip, on the centreline: no corner twice)
        poly = [p for k, p in enumerate(poly) if k == 0 or np.linalg.norm(np.subtract(p, poly[k - 1])) > 1e-6]
        d.add_planar("exterior", "tarred", poly, nrm, u_dir=(1.0, m, 0.0))


def overhang_underside(d, margin=0.05):
    """
    (0.3.8) The underside of the poop's overhang (the quarterdeck carried out over the transom): flat at OH_Y, behind
    the hull's own transom from the centreline, forward of it the ledge where the overhanging sides stand out over the
    hull's, on to the waist's end of the great cabin; out to the overhanging sides' bottom edge (their strakes' own
    ring) and in over the hull's own side's top (its strakes' ring) by `margin`. Until 0.3.8 it was strips across her,
    each one band of a plank stretched across up to four blocks behind the transom, their inner edge a coarser line than
    the hull's (slits of light along the ledge), one strip straddling the transom's corner cut short of it (a hole);
    the user's notes 33-36.
    """
    from planking import ring
    y = OH_Y
    xt, xe = base_stern_x(y), stern_x(y)
    if MODULAR:
        # ((0.3.23, the mesh check) carrack-m: 3 mm under the quarterdeck's underside, where its cells by the side (over
        # the step out to the overhang's side, seen from the great cabin's corner under the deck) have their down faces
        # in its plane)
        y = OH_Y - 0.003
    outer = ring(half_width, y, xe, WAIST_AFT, 22)      # the overhanging side's bottom edge
    inner = ring(hull_hw, y, xt, WAIST_AFT, 22)         # the hull's own side's top edge, from its transom
    _ledge(d, y, outer, None, xe, xt + margin, margin)   # (a hair over the transom's top, into its thickness)
    _ledge(d, y, outer, inner, xt, WAIST_AFT, margin)


def overhang_brackets(d):
    """Knees under the poop's overhang: along each side where it stands out over the hull, at every frame, and under
    its after edge beside the great cabin's lights, from the hull's side (the transom) up to the overhang's edge."""
    t = 0.14
    for xf in frame_stations():
        zi, zo = float(hull_hw(xf, OH_Y)), float(half_width(xf, OH_Y))
        if xf > -6.0 or zo - zi < 0.25 or zi < 0.5:
            continue
        depth = min(1.0, max(0.45, (zo - zi) * 1.3))
        zlow = float(hull_hw(xf, OH_Y - depth))
        for s in (1, -1):
            tri = [(OH_Y, s * (zi - 0.1)), (OH_Y, s * (zo - 0.04)), (OH_Y - depth, s * zlow), (OH_Y - depth, s * zlow)]
            d.add_prism("exterior", "timber", [(xf - t / 2, y, z) for y, z in tri], [(xf + t / 2, y, z) for y, z in tri])
    xe = stern_x(OH_Y)
    for zb in (-3.2, 3.2):
        if abs(zb) > transom_hw(OH_Y - 0.7) - 0.15:
            continue
        depth = 0.7
        tri = [(base_stern_x(OH_Y) + 0.1, OH_Y), (xe + 0.04, OH_Y), (base_stern_x(OH_Y - depth) + 0.02, OH_Y - depth),
               (base_stern_x(OH_Y - depth) + 0.02, OH_Y - depth)]
        d.add_prism("exterior", "timber", [(x, y, zb - t / 2) for x, y in tri], [(x, y, zb + t / 2) for x, y in tri])


# ==================================================================== rudder and tiller
def rudder_and_tiller(d):
    """The rudder turns with the helm; the tiller comes in through the transom and swings with it, overhead at the aft
    end of the lower deck. Returns the rudder's axis (a point and a direction)."""
    first = len(d.quads)
    ra = np.array([stern_x(KEEL + 0.1) - 0.05, KEEL + 0.1, 0.0])
    rb = np.array([stern_x(2.4) - 0.05, 2.4, 0.0])
    axis = (rb - ra) / np.linalg.norm(rb - ra)
    aft = np.array([-axis[1], axis[0], 0.0])
    if aft[0] > 0:
        aft = -aft
    if GRID:
        # (0.3.10, the user's note 105: "should rudder be larger?") on the same raked line (the sternpost's), from the
        # keel's underside (its heel level with it, its lower edge level aft: it never reaches under her keel; 0.3.9's
        # ran down aft 0.7 under it) to 1.0 over the waterline
        ra = ra + axis * ((KEEL_BOTTOM + 0.1 - ra[1]) / axis[1])
    rl = float(np.linalg.norm(rb - ra))
    R = np.column_stack([axis, aft, [0, 0, 1.0]])
    # the blade: broad at the heel, narrower up at the waterline, thick at the stock and thinning aft
    z_ = np.array([0, 0, 1.0])
    heel, head = ra - axis * (0.1 / axis[1] if GRID else 0.1), rb + axis * 0.1
    foot_aft = heel + (np.array([-1.45, 0.0, 0.0]) if GRID else aft * 1.45)
    corners = [(heel, 0.0), (head, 0.0), (head + aft * 0.8, 1.0), (foot_aft, 1.0)]
    front = [p + z_ * (0.15 - 0.07 * w) for p, w in corners]
    back = [p - z_ * (0.15 - 0.07 * w) for p, w in corners]
    d.add_prism("part:rudder", "tarred", front, back)
    # where the damage model has her rudder (the blade's corners, its thickness each side)
    pts = np.array([p for p, _ in corners])
    d.rudder_box = [round(float(v), 3) for v in (*(pts.min(axis=0) - [0.05, 0.05, 0.15]), *(pts.max(axis=0) + [0.05, 0.05, 0.15]))]
    # pintles: iron straps round the blade's fore edge, hooking onto the gudgeons on the sternpost
    for f in (0.1, 0.36, 0.62, 0.88):
        p = ra + (rb - ra) * f
        wide = 1.45 + (0.8 - 1.45) * f
        d.add_box_mesh("part:rudder", "iron", p + aft * 0.3, (0.09, 0.62, 0.33), R)
        d.add_cylinder("part:rudder", "iron", p - axis * 0.06, p + axis * 0.06, 0.05, 0.05, sides=8, smooth=False)
        d.add_box_mesh("exterior", "iron", p - aft * 0.2 + axis * 0.1, (0.07, 0.34, 0.36), R)
        d.add_box_mesh("part:rudder", "timber", p + aft * (wide - 0.05), (0.05, 0.1, 0.16), R) if f < 0.2 and not GRID else None
    d.add_box_mesh("exterior", "tarred", rb + axis * 0.5 + aft * 0.1, (1.0, 0.28, 0.28), R)   # rudder head
    # the sternpost the rudder hangs on, rising from the keel's after end just forward of the rudder (GRID, 0.3.10: one
    # timber with the keel and the stem, keel_and_stem())
    if not GRID:
        d.add_beam_mesh("exterior", "tarred", ra - axis * 0.3 - aft * 0.13, rb + axis * 0.35 - aft * 0.13, 0.26, 0.28,
                        up=(0, 0, 1))
    d.sternpost_top = rb + axis * 0.35 - aft * 0.13
    d.moving_parts.append({"code": "rudder", "axisPoint": [round(float(v), 4) for v in ra],
                           "axisDir": [round(float(v), 5) for v in axis], "source": "rudder", "angle": 30.0})
    # (the rudder, its pintles and gudgeons and the sternpost: where they come forward into her, inner_post covers)
    d.rudder_quads = (first, len(d.quads))

    # the tiller: from the rudder head in through the transom, forward over the lower deck, above head height
    pivot = ra + axis * ((TILLER_Y - ra[1]) / axis[1])
    tip = np.array([-13.05, TILLER_Y, 0.0])
    mid = pivot + (tip - pivot) * 0.45
    d.add_beam_mesh("part:tiller", "timber", pivot, mid, 0.15, 0.15)
    d.add_beam_mesh("part:tiller", "timber", mid, tip, 0.11, 0.12)
    d.add_cylinder("part:tiller", "timber", tip + np.array([-0.04, 0, 0]), tip + np.array([0.06, 0, 0]), 0.075, 0.075,
                   sides=8, smooth=False)
    for f in (0.25, 0.4):
        p = pivot + (tip - pivot) * f
        d.add_box_mesh("part:tiller", "iron", p, (0.05, 0.17, 0.17))
    d.moving_parts.append({"code": "tiller", "axisPoint": [round(float(v), 4) for v in ra],
                           "axisDir": [round(float(v), 5) for v in axis], "source": "rudder", "angle": 30.0,
                           "interior": True})
    d.tiller_tip = tip
    d.rudder_axis = (ra, axis)
    return ra, axis


# ==================================================================== masts, tops, yards, rigging
def masts_and_rigging(d):
    d.standing = []     # shrouds, backstays and stays, for checking the yards clear them (rig_check)

    def stay(a, b, r):
        d.add_rope(a, b, r=r)
        d.standing.append((tuple(float(v) for v in a), tuple(float(v) for v in b), r))

    def collar(mx, y, r):
        d.add_cylinder("auto", "timber", (mx, y, 0), (mx, y + 0.16, 0), r + 0.1, r + 0.08, sides=16, smooth=False,
                       phase=0.5)

    for name, (mx, lower, upper, top) in MASTS.items():
        y0, y1, r0, r1 = lower
        d.add_cylinder("auto", "pine", (mx, y0, 0), (mx, y1, 0), r0, r1, sides=16, smooth=False, phase=0.5)
        for deck_y in (LOWER, MAIN, QUARTER, POOP):
            if y0 < deck_y - 0.3 < y1 and abs(hw(mx, deck_y)) > 0.5:
                if name == "fore" and deck_y == POOP:
                    continue
                if name == "main" and deck_y > MAIN:
                    continue
                r = r0 + (r1 - r0) * (deck_y - y0) / (y1 - y0)
                collar(mx, deck_y, r)
        if upper:
            u0, u1, ur0, ur1 = upper
            d.add_cylinder("exterior", "pine", (mx, u0, 0), (mx, u1, 0), ur0, ur1, sides=12, smooth=False)
        if top:
            ty, tr = top
            main_top_tub(d, mx, ty, tr)
            # cheeks under the top
            for s in (1, -1):
                d.add_beam_mesh("exterior", "timber", (mx, ty - 1.6, s * 0.4), (mx, ty - 0.25, s * 1.2), 0.18)
        # masthead cap
        d.add_box_mesh("exterior", "timber", (mx, y1 + 0.08, 0), (2.4 * r1, 0.18, 2.4 * r1))
    # the foremast has no top: trestle trees at its head for the shrouds
    d.add_box_mesh("exterior", "timber", (FORE_X, FORE_HEAD - 0.1, 0), (0.22, 0.18, 1.3))
    d.add_box_mesh("exterior", "timber", (FORE_X, FORE_HEAD - 0.1, 0), (1.0, 0.16, 0.22))

    # the yards, each in its own group: the mod braces them round their masts with the wind (item 17)
    for code, (mx, y, half, r) in YARDS.items():
        for s in (1, -1):
            # (0.3.10, clean surfaces: no caps where the two halves meet in the middle)
            d.add_cylinder(f"part:yard-{code}", "pine", (mx + 0.5, y, 0), (mx + 0.5, y, s * half), r, r * 0.45, sides=8,
                           caps=(False, True))
        # the parrel holding it to the mast
        d.add_cylinder(f"part:yard-{code}", "rope", (mx + 0.25, y - 0.12, 0), (mx + 0.25, y + 0.12, 0), 0.2 + r * 0.3,
                       0.2 + r * 0.3, sides=8, smooth=False, caps=False)
        d.moving_parts.append({"code": f"yard-{code}", "axisPoint": [mx, y, 0.0], "axisDir": [0.0, 1.0, 0.0],
                               "source": "brace", "angle": BRACE_DEG_PER_RAD})
    d.add_cylinder("part:yard-mizzen-lateen", "pine", LATEEN_A, LATEEN_B, 0.16, 0.08, sides=8)
    d.moving_parts.append({"code": "yard-mizzen-lateen", "axisPoint": [MASTS["mizzen"][0], lateen_pivot_y(), 0.0],
                           "axisDir": [0.0, 1.0, 0.0], "source": "sheet", "angle": BRACE_DEG_PER_RAD})
    d.add_cylinder("exterior", "pine", BOWSPRIT_A, BOWSPRIT_B, 0.3, 0.15, sides=12, smooth=False)
    u = (BOWSPRIT_B - BOWSPRIT_A) / np.linalg.norm(BOWSPRIT_B - BOWSPRIT_A)
    # the stem head: a post up the forecastle's tip, over the seam where its two sides meet, carried up to a saddle the
    # bowsprit rests on (its top hollowed to it: a block with a cheek each side), a gammoning slot through it under the
    # saddle
    sx, sy = SADDLE
    post_x = FC_TIP - 0.02
    d.add_box_mesh("exterior", "timber", (post_x, (FC_Y0 - 0.05 + sy - 0.08) / 2, 0.0), (0.2, sy - 0.08 - FC_Y0 + 0.05, 0.2))
    d.add_box_mesh("exterior", "timber", (sx, sy - 0.04, 0.0), (0.32, 0.08, 0.34))
    for s in (1, -1):
        d.add_box_mesh("exterior", "timber", (sx, sy + 0.04, s * (bowsprit_r(sx) + 0.04)), (0.28, 0.12, 0.07))
    # its heel: stepped in a chock on the main deck against the foremast's fore side, with a cheek each side, and held
    # down a little forward of it by a pair of bitts with a cap across over it
    hx = BOWSPRIT_A[0]
    mast_r = MASTS["fore"][1][2] + 0.1                              # the foremast's collar on the main deck
    d.add_box_mesh("auto", "timber", ((FORE_X + mast_r + hx + 0.55) / 2, MAIN + 0.07, 0.0), (hx + 0.55 - FORE_X - mast_r, 0.14, 0.64))
    for s in (1, -1):
        d.add_box_mesh("auto", "timber", (hx + 0.12, MAIN + 0.2, s * 0.36), (0.6, 0.4, 0.1))
    bx = hx + 0.85
    cap_y = bowsprit_y(bx) + bowsprit_r(bx) * math.sqrt(1 + (u[1] / u[0]) ** 2) + 0.02
    for s in (1, -1):
        d.add_box_mesh("auto", "timber", (bx, (MAIN + cap_y + 0.14) / 2, s * 0.42), (0.18, cap_y + 0.14 - MAIN, 0.16))
        d.add_box_mesh("auto", "iron", (bx, cap_y + 0.155, s * 0.42), (0.2, 0.03, 0.18))
    d.add_box_mesh("auto", "timber", (bx, cap_y + 0.07, 0.0), (0.16, 0.14, 0.98))      # (0.3.10: inside the posts' faces)
    d.bowsprit_bitts = (bx, cap_y + 0.17)
    # its partners: a frame round it where it comes up through the forecastle's deck
    lo_x = BOWSPRIT_A[0] + (QUARTER - DECK_T - 0.32 - BOWSPRIT_A[1]) / (u[1] / u[0])
    hi_x = BOWSPRIT_A[0] + (QUARTER + 0.32 - BOWSPRIT_A[1]) / (u[1] / u[0])
    d.partners = (lo_x, hi_x)
    pw = 0.36
    for x_, w_ in ((lo_x - 0.06, 0.12), (hi_x + 0.06, 0.12)):
        d.add_box_mesh("exterior", "timber", (x_, QUARTER + 0.04, 0.0), (w_, 0.08, 2 * pw + 0.24))
    for s in (1, -1):
        d.add_box_mesh("exterior", "timber", ((lo_x + hi_x) / 2, QUARTER + 0.04, s * (pw + 0.06)), (hi_x - lo_x, 0.08, 0.12))
    # iron bands round it
    for bx_ in (BOWSPRIT_A[0] + 1.4, sx - 0.75, BOWSPRIT_B[0] - 0.5):
        c = np.array([bx_, bowsprit_y(bx_), 0.0])
        d.add_cylinder("exterior", "iron", c - u * 0.05, c + u * 0.05, bowsprit_r(bx_) + 0.02, bowsprit_r(bx_) + 0.02,
                       sides=12, smooth=False)
    # the gammoning: turns of rope over the bowsprit and down through a slot in the stem head under the saddle,
    # pulling it down onto the saddle; side by side along it just aft of the saddle
    slot_y = sy - 0.32
    for k in range(5):
        gx = sx - 0.5 + 0.07 * k
        yc = bowsprit_y(gx)
        rr = bowsprit_r(gx) * math.sqrt(1 + (u[1] / u[0]) ** 2) + 0.03
        ring = [np.array([gx, yc + rr * math.cos(a), rr * math.sin(a)]) for a in np.linspace(-math.pi / 2, math.pi / 2, 7)]
        # (round its top from the port side to the starboard side, down to the slot, through it, and up again)
        sx_ = post_x - 0.07 + 0.035 * k                     # (each turn through the slot beside the last)
        loop = ring + [np.array([sx_, slot_y, 0.13]), np.array([sx_, slot_y, -0.13]), ring[0]]
        rope_path(d, loop, r=0.028)

    def on_bowsprit(x):
        f = (x - BOWSPRIT_A[0]) / (BOWSPRIT_B[0] - BOWSPRIT_A[0])
        return BOWSPRIT_A + (BOWSPRIT_B - BOWSPRIT_A) * f

    # ---------------------------------------------------------------- standing rigging
    def chain(x, y):  # outboard edge of the channel the main shrouds are set up on
        return hw(x, y - 0.5) + 0.35

    def ratlines(bottoms, tops, r=0.022):
        n = int(np.linalg.norm(tops[0] - bottoms[0]) / 0.38)
        for i in range(1, n):
            f = i / n
            pts = [b + (t - b) * f for b, t in zip(bottoms, tops)]
            for p, q in zip(pts[:-1], pts[1:]):
                d.add_rope(p, q, r=r)

    main_x, fore_x, mizzen_x = MASTS["main"][0], MASTS["fore"][0], MASTS["mizzen"][0]
    main_top = MASTS["main"][3]
    for s in (1, -1):
        # main shrouds: outside, on channels with deadeyes and chain plates down to the wale
        xs_main = MAIN_SHROUDS
        dy_ = CHANNEL[2] + CHANNEL_T + 0.25           # the deadeyes over the channel
        bottoms = [np.array([sx, dy_, s * chain(sx, dy_)]) for sx in xs_main]
        tops = [np.array([main_x + (sx - main_x) * 0.12, main_top[0] - 0.3, s * 0.5]) for sx in xs_main]
        for b, t in zip(bottoms, tops):
            stay(b, t, 0.045)
            u = (t - b) / np.linalg.norm(t - b)
            d.add_cylinder("exterior", "timber", b - u * 0.06, b + u * 0.1, 0.12, 0.12, sides=8, smooth=False)
            yw = wale_bands()[-1][1] + 0.05        # the chain plates down to the upper wale
            if GRID and any(a_ - 0.1 < b[0] < b_ + 0.1 for a_, b_ in GUNPORTS):
                # (0.3.10) over a gun port: down to an iron strap bolted to the bulwark over its lintel (and its lid's
                # hinge), not across the port
                ys_ = MAIN + GUNPORT_Y[1] + 0.12
                foot = np.array([b[0], ys_, s * (hw(b[0], ys_) + 0.03)])
                if CHANNEL[2] < ys_ + 0.1:
                    # (0.3.13, note 132: the channel down at the rail, right over the port's lid) to a strap bolted
                    # to the channel's outer edge, round it from its top to its underside
                    ze_ = hw((CHANNEL[0] + CHANNEL[1]) / 2, CHANNEL[2] - 0.1) + 0.41
                    foot = np.array([b[0], CHANNEL[2] + CHANNEL_T / 2, s * (ze_ + 0.022)])
                    d.add_rope(b - u * 0.06, foot + np.array([0, 0.06, 0]), r=0.03, mat="iron")
                    d.add_box_mesh("exterior", "iron", foot, (0.09, CHANNEL_T + 0.06, 0.04))
                    continue
                d.add_rope(b - u * 0.06, foot + np.array([0, 0.02, 0]), r=0.03, mat="iron")
                d.add_box_mesh("exterior", "iron", foot, (0.09, 0.14, 0.04))
                continue
            d.add_rope(b - u * 0.06, (b[0], yw + float(sheer(b[0])), s * (hw(b[0], yw) + 0.12)), r=0.03, mat="iron")
        ratlines(bottoms, tops)
        # the channel (a plank standing out from the hull under the deadeyes)
        cx = (CHANNEL[0] + CHANNEL[1]) / 2
        d.add_box_mesh("exterior", "tarred", (cx, CHANNEL[2] + CHANNEL_T / 2, s * (hw(cx, CHANNEL[2] - 0.1) + 0.2)),
                       (CHANNEL[1] - CHANNEL[0], CHANNEL_T, 0.42))
        # fore and mizzen shrouds: set up inside the bulwarks on eyebolts
        for xs_, head, hz, rail_h in ((tuple(FORE_X + dx for dx in (-1.6, -1.0, -0.4, 0.2)), FORE_HEAD - 0.2, 0.4, QUARTER + 0.8 + CASTLE_UP + SHROUD_RAIL_UP),
                                      ((-13.8, -13.2, -12.6), 23.8, 0.25, POOP + 0.8 + CASTLE_UP + SHROUD_RAIL_UP)):
            mast_x = fore_x if head < 22 else mizzen_x
            # a shroud rail along the frames' inner faces, the eyebolts through it
            run = [np.array([x, rail_h - 0.02, s * (frame_face(x, rail_h) - 0.07)])
                   for x in np.linspace(min(xs_) - 0.35, max(xs_) + 0.35, 7)]
            for a_, b_ in zip(run[:-1], run[1:]):
                d.add_beam_mesh("auto", "timber", a_, b_, 0.14, 0.18)
            for sx in xs_:
                b = np.array([sx, rail_h + 0.08, s * (frame_face(sx, rail_h) - 0.16)])
                t = np.array([mast_x + (sx - mast_x) * 0.1, head, s * hz])
                stay(b, t, 0.04)
                d.add_cylinder("auto", "iron", b - np.array([0, 0, s * 0.05]), b + np.array([0, 0, s * 0.06]), 0.05,
                               0.05, sides=6, smooth=False)
        # main topmast shrouds from the rim of the top, with ratlines
        ty, tr = main_top
        # (set up on the top's capping rail, an iron eye for each)
        bottoms = [np.array([main_x - 0.3 - 0.35 * k, ty + TOP_WALL + 0.12, 0.0]) for k in range(3)]
        for b in bottoms:
            b[2] = s * math.sqrt(max(0.0, (tr - 0.05) ** 2 - (b[0] - main_x) ** 2))
            d.add_box_mesh("exterior", "iron", b - np.array([0, 0.06, 0]), (0.06, 0.05, 0.06))
        tops = [np.array([main_x, 29.6, s * 0.18]) for _ in range(3)]
        for b, t in zip(bottoms, tops):
            stay(b, t, 0.035)
        ratlines(bottoms, tops, r=0.02)
        # main topmast backstays
        bx_ = main_x - 3.8
        stay((main_x, 33.5, s * 0.12), (bx_, MAIN + 0.9, s * chain(bx_, MAIN + 0.9)), 0.035)

    # stays
    stay((main_x, main_top[0] - 0.3, 0), (fore_x - 0.6, QUARTER + 0.3, 0), 0.07)       # main stay
    stay((main_x, 30.0, 0), (fore_x, FORE_HEAD - 0.1, 0), 0.05)                        # main topmast stay
    stay((fore_x, FORE_HEAD - 0.2, 0), tuple(on_bowsprit(21.5 + STRETCH)), 0.06)       # fore stay
    stay((mizzen_x, 25.2, 0), (main_x, 15.0, 0), 0.045)                                # mizzen stay

    # ---------------------------------------------------------------- running rigging
    topsail_y = YARDS["main-topsail"][1]
    heads = {"main-course": 23.4, "main-topsail": 33.8, "fore-course": FORE_HEAD - 0.1}
    def yard_rope(yard, side, what, on_yard, on_hull, r, sail=None, anchor_follow=None):
        """
        A rope from a yard (or its sail) to something that stays put: it follows the yard as it's braced round. A
        sheet (sail given) also follows its sail's clew up to the yard as the sail is furled, so it never hangs in the
        air; anchor_follow: its other end is on another yard, and moves with that one.
        """
        code = f"{what}-{yard}-{side}"
        d.add_rope(on_hull, on_yard, r=r, group=f"part:{code}")
        part = {"code": code, "kind": "follow", "follow": f"yard-{yard}",
                "anchor": [round(float(v), 4) for v in on_hull], "tip": [round(float(v), 4) for v in on_yard]}
        if sail:
            part["sail"] = sail
        if anchor_follow:
            part["anchorFollow"] = anchor_follow
        d.moving_parts.append(part)

    for s, side in ((1, "starboard"), (-1, "port")):
        for code, (mx, y, half, r) in YARDS.items():
            d.add_rope((mx + 0.5, y, s * (half - 0.2)), (mx, heads[code], s * 0.15), r=0.025,       # lifts
                       group=f"part:yard-{code}")
        # braces, from the yardarms to the poop rail and to the next mast aft
        yard_rope("main-course", side, "brace", (main_x + 0.5, 20.25, s * 7.2),
                  (-14.5, POOP + 1.0 + CASTLE_UP, s * hw(-14.5, POOP + 1.0 + CASTLE_UP)), 0.025)
        yard_rope("fore-course", side, "brace", (fore_x + 0.5, 17.25, s * 5.8), (main_x, 15.6, s * 0.35), 0.025)
        yard_rope("main-topsail", side, "brace", (main_x + 0.5, topsail_y, s * 4.3), (mizzen_x, 22.0, s * 0.2), 0.02)
        # sheets, from the clews of the courses down to the rail
        yard_rope("main-course", side, "sheet", (main_x + 0.55, 11.7, s * 6.9),
                  (-5.0, rail(-5.0), s * hw(-5.0, rail(-5.0) - 0.05)), 0.025, sail="main-course")
        yard_rope("fore-course", side, "sheet", (fore_x + 0.55, 10.2, s * 5.6),
                  (7.0 + STRETCH, rail(7.0 + STRETCH), s * hw(7.0 + STRETCH, rail(7.0 + STRETCH) - 0.05)), 0.025,
                  sail="fore-course")
        # the topsail's sheets run to the course yardarms (both yards brace together)
        yard_rope("main-topsail", side, "sheet", (main_x + 0.55, TOPSAIL_FOOT, s * 5.5), (main_x + 0.5, 20.25, s * 7.1),
                  0.02, sail="main-topsail", anchor_follow="yard-main-course")
        # pin rail with belaying pins along the inside of the bulwark by the mainmast
        # (0.3.8, the user's note 49: shortened to end aft of the entry port, now in the bay just forward of it)
        z = s * (frame_face(main_x - 0.5, MAIN + 0.8) - 0.1)
        pr_end = min(main_x + 1.4, PORT_X[0] - 0.25) if PORT_X[0] > main_x else main_x + 1.4
        pr_start = main_x - 1.7
        if GRID:
            # (0.3.10, the waist re-planned: in the bays without a gun, -2..0 and 12..14, between the frames' faces,
            # carried by them: no caps, no brackets)
            # (0.3.12, the user's note 111: "this pin board could be on top of side board. Now it kind of levitates":
            # it was a round rail 0.1 inboard of the frames' faces, touching nothing) a pin board fastened to the
            # inside of the bulwark: flat, 0.22 broad out from the inside planking (0.01 into it; 0.02 proud of the
            # frames' faces), its top 0.03 under the rail cap's inboard underside where the bay is lowest (both bays
            # are in the castle breaks' sweeps, rising away from the waist), its ends 0.01 into the frames' sides; the
            # belaying pins through it as before
            for a_, b_ in ((-2.0 + FRAME_SIDED / 2, -FRAME_SIDED / 2), (12.0 + FRAME_SIDED / 2, 14.0 - FRAME_SIDED / 2)):
                lo_x = b_ if rail(b_) < rail(a_) else a_
                yt_ = float(rail(lo_x)) - 0.16 - 0.03             # (the sweeps' caps are 0.16 deep inboard)
                yc_ = yt_ - 0.04
                a1_, b1_ = a_ - 0.01, b_ + 0.01
                zo_ = min(ceil_z(x_, yc_) for x_ in (a1_, (a1_ + b1_) / 2, b1_)) + 0.01
                zi_ = zo_ - 0.23
                d.add_box_mesh("exterior", "timber", ((a1_ + b1_) / 2, yc_, s * (zo_ + zi_) / 2),
                               (b1_ - a1_, 0.08, zo_ - zi_))
                zz_ = s * (zi_ + 0.1)
                for px in np.arange(a_ + 0.2, b_ - 0.1, 0.4):
                    d.add_cylinder("exterior", "oak", (px, yc_ - 0.18, zz_), (px, yt_ + 0.2, zz_), 0.035, 0.035, sides=6)
                # (0.3.13, the user's note 131: "Clicking here makes cannon go out, but could be way to regulate sails
                # additional to the wheel") each board a fixture ("pinrail", fixtures()): its middle, and a box over it
                # and its pins from the planking to 0.06 proud of the pins' inboard side
                za_, zb_ = sorted((s * zo_, s * (zi_ - 0.06)))
                d.pin_rails.append({"code": f"pinrail-{'main' if a_ < 5 else 'fore'}-{side}",
                                    "pos": ((a1_ + b1_) / 2, yc_, s * (zo_ + zi_) / 2),
                                    "sel": (a1_, yc_ - 0.2, za_, b1_, yt_ + 0.22, zb_)})
            continue
        if GRID:
            # (0.3.10, the user's note 86: "rope holder is too long - interferes with cannons") only in the bay without a
            # gun by the mainmast: from the great cabin's front to frame 0's after face (the first gun's bay is 0..2),
            # its ends against them (no caps), a bracket in each of frames -2 and 0
            pr_start, pr_end = WAIST_AFT, -FRAME_SIDED / 2
        d.add_cylinder("exterior", "timber", (pr_start, MAIN + 0.8, z), (pr_end, MAIN + 0.8, z), 0.07, 0.07,
                       sides=6, smooth=False, caps=not GRID)
        for sx in ((-2.0, 0.0) if GRID else (main_x - 1.55, pr_end - 0.15)):   # its brackets against the frames
            d.add_box_mesh("exterior", "timber", (sx, MAIN + 0.72, z + s * 0.08), (0.1, 0.16, 0.18))
        for px in (np.arange(-2.6, -0.2, 0.4) if GRID else np.arange(pr_start + 0.2, pr_end - 0.05, 0.4)):
            d.add_cylinder("exterior", "oak", (px, MAIN + 0.62, z), (px, MAIN + 1.02, z), 0.035, 0.035, sides=6)
    d.add_rope((mizzen_x, 18.4, 0.5), (mizzen_x, 25.3, 0.05), r=0.03, group="part:yard-mizzen-lateen")  # halyard
    # the lateen's sheet, from its clew to the taffrail
    lateen_clew = (-16.3, POOP + 2.8, 0.5)
    yard_rope("mizzen-lateen", "aft", "sheet", lateen_clew, (stern_x(POOP + 1.0 + CASTLE_UP) + 0.15, POOP + 1.06 + CASTLE_UP, 0.9), 0.025,
              sail="mizzen-lateen")


def main_top_tub(d, mx, ty, tr, sides=24):
    """
    (0.3.8) The main top: its floor, planked straight across, and round it a breastwork as a carrack's round top had, a
    tub of upright boards chest high (TOP_WALL) with battens round its outside and a capping rail over it. (The user's
    note 54: "looks to have double floor where people would stand, also circular pattern in triangles; railings look
    weak". Until 0.3.8 its rim 0.75 high had a cap that was a whole disk across the top 0.82 over its floor, a second
    floor at your knees, stanchions and a thin rail on that, and its round faces were fans of wedges.) It's solid round
    its rim TOP_RAIL high, a little over the rail, so nobody jumps it.
    """
    up = np.array([0.0, 1.0, 0.0])
    hb = TOP_WALL
    c = np.array([mx, 0.0, 0.0])

    def staves(r, y0, y1, inward=False):
        """Upright boards round it, each one plank of the texture (the grain up it), not a sliver of one stretched
        round (add_cylinder's texture goes once round the whole)."""
        for k in range(sides):
            a0, a1 = 2 * math.pi * (k + 0.5) / sides, 2 * math.pi * (k + 1.5) / sides
            e0, e1 = np.array([math.cos(a0), 0, math.sin(a0)]), np.array([math.cos(a1), 0, math.sin(a1)])
            q = [c + e0 * r + up * y0, c + e0 * r + up * y1, c + e1 * r + up * y1, c + e1 * r + up * y0]
            v0, v1 = BANDS[k % 3]
            u0 = (k * 5 % 4) * max(0.0, 16 - (y1 - y0) * 16) / 3
            out = (e0 + e1) * (-1 if inward else 1)
            _quad_facing(d, "exterior", "oak", q, out, (u0, v0, u0 + min(16.0, (y1 - y0) * 16), v1), keep_uv=True)

    # its floor (its round faces planked straight across: Design.add_planar), and the tub's boards outside and in
    rim = [c + tr * np.array([math.cos(2 * math.pi * (k + 0.5) / sides), 0, math.sin(2 * math.pi * (k + 0.5) / sides)])
           for k in range(sides)]
    for y, nrm in ((ty, up), (ty - 0.25, -up)):
        d.add_planar("exterior", "oak", [p + up * y for p in rim], nrm, u_dir=(1.0, 0, 0))
    staves(tr, ty - 0.25, ty + hb)
    staves(tr - 0.08, ty, ty + hb, inward=True)
    # the capping rail: a flat ring over the boards' tops, standing proud of them both sides, its pieces' grain round it
    ro, ri, yc, th = tr + 0.05, tr - 0.15, ty + hb, 0.08
    d.add_cylinder("exterior", "timber", (mx, yc, 0), (mx, yc + th, 0), ro, ro, sides=sides, smooth=False, caps=False,
                   phase=0.5)
    d.add_cylinder("exterior", "timber", (mx, yc, 0), (mx, yc + th, 0), ri, ri, sides=sides, smooth=False, caps=False,
                   phase=0.5, inward=True)
    for k in range(sides):
        a0, a1 = 2 * math.pi * (k + 0.5) / sides, 2 * math.pi * (k + 1.5) / sides
        e0, e1 = np.array([math.cos(a0), 0, math.sin(a0)]), np.array([math.cos(a1), 0, math.sin(a1)])
        tang = e1 - e0
        for y, nrm in ((yc + th, up), (yc, -up)):
            ring = [c + e0 * ri + up * y, c + e1 * ri + up * y, c + e1 * ro + up * y, c + e0 * ro + up * y]
            d.add_planar("exterior", "timber", ring, nrm, u_dir=tang)
    # battens up the outside of the boards, from the floor's edge to under the rail
    for k in range(12):
        a = 2 * math.pi * k / 12
        rad = np.array([math.cos(a), 0, math.sin(a)])
        tang = np.array([-math.sin(a), 0, math.cos(a)])
        # (0.3.10, clean surfaces: 0.01 up into the capping rail, 0.005 inside its outer face)
        d.add_box_mesh("exterior", "timber", np.array([mx, ty + (hb - 0.25) / 2 + 0.005, 0]) + rad * (tr + 0.0225),
                       (0.045, hb + 0.26, 0.09), np.column_stack([rad, up, tang]))


# ==================================================================== things to climb
def ladders(d):
    main_x = MASTS["main"][0]
    main_top = MASTS["main"][3]

    def shrouds(code, xs, rail_y_, deck_y, mast_x, top, side):
        ty, tr = top
        xc = sum(xs) / len(xs)
        d.ladders.append({
            "code": code,
            "kind": "ratlines",
            "bottom": [xc, rail_y_, side * (hw(xc, rail_y_) - 0.3)],           # standing on the bulwark
            "top": [mast_x + (xc - mast_x) * 0.3, ty - 1.95, side * 0.95],     # just under the top
            "exitBottom": [xc, deck_y, side * (hw(xc, rail_y_) - 1.05)],
            "exitTop": [mast_x - 0.8, ty, side * 0.65],
        })

    for s, side in ((1, "starboard"), (-1, "port")):
        shrouds(f"main-{side}", MAIN_SHROUDS, rail(sum(MAIN_SHROUDS) / len(MAIN_SHROUDS)), MAIN, main_x, main_top, s)

    # rope ladders down the side from the entry ports to the water: the only way aboard from the water
    px = (PORT_X[0] + PORT_X[1]) / 2

    def on_wale(y):
        return any(abs(y - ((w0 + w1) / 2 + float(sheer(px)))) < 0.2 for w0, w1 in wale_bands())

    for s, side in ((1, "starboard"), (-1, "port")):
        pts = []
        for y in np.arange(MAIN, WATER - 0.8, -0.05):
            pts.append((y, hw(px, y) + (0.08 if y > COPPER_TOP else 0.12) + (0.1 if on_wale(y) else 0.0)))
        for rx in (PORT_X[0] + 0.12, PORT_X[1] - 0.12):
            for (ya, za), (yb, zb) in zip(pts[::4][:-1], pts[::4][1:]):
                d.add_rope((rx, ya, s * za), (rx, yb, s * zb), r=0.03)
        for y in np.arange(MAIN - 0.3, WATER - 0.7, -0.33):
            z = s * (hw(px, y) + (0.1 if y > COPPER_TOP else 0.14) + (0.1 if on_wale(y) else 0.0))
            d.add_cylinder("exterior", "timber", (PORT_X[0] + 0.1, y, z), (PORT_X[1] - 0.1, y, z), 0.04, 0.04, sides=6)
        d.ladders.append({
            "code": f"sideladder-{side}",
            "kind": "rope",
            "outside": True,
            "gate": f"gate-{side}",
            "bottom": [px, WATER - 0.6, s * (hw(px, WATER) + 0.7)],
            "top": [px, MAIN - 0.4, s * (hw(px, MAIN) + 0.42)],
            "exitBottom": [px, WATER - 1.0, s * (hw(px, WATER) + 1.5)],
            "exitTop": [px, MAIN, s * (hw(px, MAIN + 0.5) - HULL_T - 0.55)],
        })

    if GRID:
        # (0.3.12, the user's note 110: "remove stairs in cargo hatch and replace with straight up ladder to the glass
        # hatch") a plain upright ladder at the hatches' forward end, from the cargo deck up through the hold hatch to
        # under the glazed waist hatch (hold_ladder): climbed facing forward, off it on the main deck forward of the
        # coaming; the waist hatch shut stops you at the top. It passes the lower deck in the hold hatch's opening
        # ("through": that hatch shut is in the way there; no way off it onto the lower deck: the hold stair is the
        # easy way down from there). Its line leans 0.02 forward over its height, so the way onto it from its top is
        # aft (the mod takes that from its line)
        lx = round(HOLD_LADDER_X - 0.37, 4)        # where the climber stands, aft of its rungs
        # (0.3.13, the user's note 136: "there should be break in ladder. so hatch could be closed and it is just
        # rectangular. Vanilla ladders work well with every other skipped") two ladders in line, a break between them
        # where the hold hatch shuts (a plain rectangle again): "holdladder" from the cargo deck up to under the hold
        # hatch's leaves (shut, it stops you at its top), off it onto the lower deck forward of the coaming; and
        # "hatchladder" from the hold hatch (standing on it shut) up to under the glazed waist hatch, off it onto the
        # main deck forward of the coaming, at its foot back onto the hold hatch's top. Each leans 0.02 forward over its
        # height (the way onto it from its top is aft)
        d.ladders.append({
            "code": "holdladder",
            "kind": "ladder",
            "gate": "hatch-hold",
            "bottom": [lx, HOLD_FLOOR, 0.0],
            "top": [round(lx + 0.02, 4), LOWER, 0.0],
            "exitBottom": [round(lx - 0.7, 4), HOLD_FLOOR, 0.0],
            "exitTop": [HATCH[1] + 0.7, LOWER, 0.0],
        })
        d.ladders.append({
            "code": "hatchladder",
            "kind": "ladder",
            "gate": "hatch-waist",
            "bottom": [lx, LOWER + 0.25, 0.0],
            "top": [round(lx + 0.02, 4), MAIN, 0.0],
            "exitBottom": [round(lx - 0.7, 4), LOWER + 0.25, 0.0],
            "exitTop": [HATCH[1] + 0.7, MAIN, 0.0],
        })
        if MODULAR:
            # ((0.3.20, step B, docs/slots.md §2) through openings of deck cells: passable at the top while the cell there
            # is empty and no shut hatch covers it ("gate": "opening"); the upper one's foot on the hold's hatch shut,
            # flush with the lower deck now; off them forward onto the decks past the openings (x 9..)
            for lad in d.ladders[-2:]:
                lad["gate"] = "opening"
                lad["exitTop"] = [OPENING[1] + 0.45, lad["exitTop"][1], 0.0]
            d.ladders[-1]["bottom"][1] = LOWER
            d.ladders[-1]["exitBottom"][1] = LOWER
            # ((0.3.23, note 173: "remove the hatch ladders") none: the hatchways are cargo hatches only; the way to the
            # lower deck and the hold is by the companion and the hold stair, walked)
            del d.ladders[-2:]
    else:
        # the hold's ladder: from the cargo deck up through the hold hatch (shut, it stops you at the top), climbing aft
        d.ladders.append({
            "code": "holdladder",
            "kind": "ladder",
            "gate": "hatch-hold",
            "bottom": [HATCH[0] + 1.35, KEELSON_TOP, 0.0],
            "top": [HATCH[0] + 0.25, LOWER, 0.0],
            "exitBottom": [HATCH[0] + 2.0, KEELSON_TOP, 0.0],
            "exitTop": [HATCH[0] - 0.6, LOWER, 0.0],
        })

    # the companionway: a steep ship's ladder from the lower deck up through the waist against the quarterdeck's
    # front, climbing forward (so you go down it facing aft). (0.3.8: a stair in the great cabin, walked: no ladder)
    if CABIN_STAIR:
        return
    cz = (COMPANION[2] + COMPANION[3]) / 2
    foot_x, head_x = companion_ends()
    dn = COMPANION_DOWN
    d.ladders.append({
        "code": "companionway",
        "kind": "ladder",
        "bottom": [foot_x - 0.2 * dn, LOWER, cz],
        "top": [head_x + 0.16 * dn, MAIN, cz],
        "exitBottom": [foot_x + 0.53 * dn, LOWER, cz],
        "exitTop": [head_x - 0.66 * dn, MAIN, cz],
    })
    if COMPANION_HOUSE:
        # (0.3.7) its house's door shut: you can't get on at the top, and coming up you wait at the top till it's opened
        d.ladders[-1]["gate"] = "door-companion"


# ==================================================================== the helm: wheel and steering gear
def helm(d, rudder_axis):
    wc = np.array([WHEEL_X, QUARTER + 0.85, 0.0])
    xd = WHEEL_X + 0.43                     # the drum the wheel ropes wind on, between the posts
    for dx in (0.24, 0.62):                 # posts forward of the wheel, tapering up to the axle
        xp = wc[0] + dx
        pb, pt = QUARTER, wc[1] + 0.12
        quad = [(-0.22, pb), (0.22, pb), (0.09, pt), (-0.09, pt)]
        d.add_prism("exterior", "timber", [(xp + 0.05, y, z) for z, y in quad], [(xp - 0.05, y, z) for z, y in quad])
    d.add_box_mesh("exterior", "timber", (xd, QUARTER + 0.05, 0), (0.62, 0.1, 0.5))
    d.add_cylinder("exterior", "iron", wc - np.array([0.1, 0, 0]), wc + np.array([0.72, 0, 0]), 0.045, 0.045, sides=8,
                   smooth=False)
    d.add_cylinder("exterior", "timber", (xd - 0.13, wc[1], 0), (xd + 0.13, wc[1], 0), 0.13, 0.13, sides=10,
                   smooth=False)
    # the wheel itself, smaller than before so the helmsman sees over it, turns with the rudder
    R_RIM, R_HANDLE = 0.36, 0.56
    n_rim = 16
    for k in range(n_rim):
        a = 2 * math.pi * (k + 0.5) / n_rim
        c = wc + R_RIM * np.array([0, math.cos(a), math.sin(a)])
        tang = np.array([0, -math.sin(a), math.cos(a)])
        rad = np.array([0, math.cos(a), math.sin(a)])
        # (0.3.10, clean surfaces: the rim's pieces share their corners, each a prism between its two ends, not
        # boxes a little longer than their chord overlapping their neighbours)
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
        d.add_cylinder("part:wheel", "timber", wc + rad * (R_HANDLE - 0.02), wc + rad * (R_HANDLE + 0.04), 0.04,
                       0.025, sides=6)
        # (0.3.8, the user's note 42: "should the wheel have some bronze?") a bronze ferrule where each handle leaves
        # the rim
        d.add_cylinder("part:wheel", "bronze", wc + rad * (R_RIM + 0.025), wc + rad * (R_RIM + 0.075), 0.037, 0.037,
                       sides=8, smooth=False)
    d.add_cylinder("part:wheel", "timber", wc - np.array([0.08, 0, 0]), wc + np.array([0.08, 0, 0]), 0.09, 0.09,
                   sides=10, smooth=False)
    # a bronze boss on each face of the hub, over the axle's end (an iron one before)
    for sgn in (1, -1):
        a_ = wc + np.array([sgn * 0.08, 0, 0])
        d.add_cylinder("part:wheel", "bronze", a_, a_ + np.array([sgn * 0.022, 0, 0]), 0.078, 0.07, sides=12,
                       smooth=False)
        d.add_cylinder("part:wheel", "bronze", a_ + np.array([sgn * 0.022, 0, 0]), a_ + np.array([sgn * 0.04, 0, 0]),
                       0.05, 0.035, sides=12)
    d.moving_parts.append({"code": "wheel", "axisPoint": [float(v) for v in wc], "axisDir": [1.0, 0.0, 0.0],
                           "source": "rudder", "angle": 240.0})

    # the wheel ropes (shown, not simulated): off the drum, down through the quarterdeck, out under the beams to
    # blocks at the sides, aft along the great cabin's walls, down in its after corners through the main deck to
    # blocks on the lower deck, and from those to the end of the tiller
    tip = d.tiller_tip
    beam_y = QUARTER - DECK_T - 0.45     # under the beams, clear of the knees and the clamp
    for s, side in ((1, "starboard"), (-1, "port")):
        drum = np.array([xd, wc[1], s * 0.13])
        hole_q = np.array([xd, QUARTER, s * 0.28])
        lead = np.array([xd, QUARTER - DECK_T - 0.18, s * 0.28])
        fwd_side = np.array([xd, beam_y, s * wall_z(xd, beam_y, 0.5)])
        aft_side = np.array([-14.1, beam_y, s * wall_z(-14.1, beam_y, 0.5)])
        hole_m = np.array([-14.1, MAIN, s * wall_z(-14.1, MAIN + 0.1)])
        low = np.array([-14.1, TILLER_Y + 0.13, s * wall_z(-14.1, TILLER_Y + 0.13)])
        # round the drum
        rope_path(d, helix(xd, 0.0, 0.15, wc[1] - 0.05, wc[1] + 0.05, 0.0, 1.0, 12)[:7] if False else [drum, hole_q],
                  r=0.03)
        run = [lead, fwd_side, aft_side]
        if STAIRWELL and not MODULAR and s * STAIRWELL[2] > 0 and STAIRWELL[0] < xd < STAIRWELL[1]:
            # (0.3.14, the user's note 143: "steering wheel rope can go through staircase same way as other side") the
            # starboard one as the port one, straight out to the side and aft along it, through the walled stairwell
            # (0.3.12 led it round it, along the cabin side of its inboard wall): through its inboard wall and its after
            # wall in a hole each, lined with an iron ring on both faces (a fairlead), well over the head of anyone on
            # its landing or stair (beam_y is 2.3 over the main deck)
            sx0, _, sz0, sz1 = STAIRWELL
            xw = sx0 + 0.125
            zw = fwd_side[2] + (aft_side[2] - fwd_side[2]) * (xw - xd) / (aft_side[0] - xd)
            for c_, n_ in (((xd, beam_y, sz0 - 0.006), (0, 0, -1.0)), ((xd, beam_y, sz1 + 0.006), (0, 0, 1.0)),
                           ((sx0 - 0.006, beam_y, zw), (-1.0, 0, 0)), ((sx0 + 0.256, beam_y, zw), (1.0, 0, 0))):
                c_, n_ = np.array(c_), np.array(n_)
                disk(d, "auto", "black", c_, n_, 0.045, 8)
                d.add_cylinder("auto", "iron", c_ - n_ * 0.016, c_ + n_ * 0.014, 0.065, 0.065, sides=8, smooth=False,
                               caps=False)
        rope_path(d, [hole_q] + run + [hole_m, low], r=0.03, group="auto")
        for p in run:
            block_at(d, "auto", p + np.array([0, 0.1, 0]))
        block_at(d, "auto", low + np.array([0, 0.12, 0]))
        for p, up in ((hole_q, 1), (hole_m, 1)):
            disk(d, "auto", "black", p + np.array([0, 0.006, 0]), (0, 1, 0), 0.07, 8)
            d.add_cylinder("auto", "iron", p - np.array([0, 0.01, 0]), p + np.array([0, 0.02, 0]), 0.1, 0.1, sides=8,
                           smooth=False, caps=False)
        # the tiller rope follows the tiller's end as it swings
        d.add_rope(low, tip, r=0.03, group=f"part:tillerrope-{side}")
        d.moving_parts.append({"code": f"tillerrope-{side}", "kind": "follow", "follow": "tiller",
                               "anchor": [round(float(v), 4) for v in low], "tip": [round(float(v), 4) for v in tip],
                               "interior": True})
    # rope turns on the drum
    rope_path(d, helix(xd, 0.0, 0.15, wc[1], wc[1], 0.0, 1.0, 14), r=0.028) if False else None
    for k in range(5):
        x = xd - 0.1 + 0.05 * k
        pts = ring_points((x, wc[1], 0.0), 0.15, "x", 12)
        rope_path(d, pts, r=0.026)


# ==================================================================== the binnacle
def binnacle(d):
    """
    The compass in front of the wheel, in a binnacle: a small wooden cupboard as ships had them before the brass
    pedestals, its after face glazed so the helmsman sees the card over the spokes, a lamp beside it for the night
    watch. Right-click it (or look at it) for her heading.
    """
    if BINNACLE_X is None:
        return
    bx, y0 = BINNACLE_X, QUARTER
    d.add_box_mesh("exterior", "oak", (bx, y0 + 0.5, 0.0), (0.4, 1.0, 0.5))                 # the cupboard
    d.add_box_mesh("exterior", "timber", (bx, y0 + 1.03, 0.0), (0.48, 0.06, 0.58))         # its lid
    d.add_box_mesh("exterior", "timber", (bx, y0 + 0.04, 0.0), (0.48, 0.08, 0.58))         # plinth
    # the glazed after face, framed; through it the compass bowl and card
    fx_ = bx - 0.203
    _quad_facing(d, "glass", "glass", [(fx_, y0 + 0.62, -0.17), (fx_, y0 + 0.62, 0.17), (fx_, y0 + 0.95, 0.17),
                                       (fx_, y0 + 0.95, -0.17)], (-1, 0, 0), (0, 0, 16, 16))
    for yy in (y0 + 0.6, y0 + 0.97):
        d.add_box_mesh("exterior", "timber", (fx_ - 0.01, yy, 0.0), (0.03, 0.04, 0.4))
    for zz in (-0.19, 0.19):
        d.add_box_mesh("exterior", "timber", (fx_ - 0.015, y0 + 0.785, zz), (0.04, 0.4, 0.04))   # (0.3.10: 0.01 proud)
    if not GRID:
        d.add_cylinder("auto", "gold", (bx - 0.02, y0 + 0.66, 0.0), (bx - 0.02, y0 + 0.72, 0.0), 0.16, 0.16, sides=12,
                       smooth=False)
        disk(d, "auto", "paper", (bx - 0.02, y0 + 0.725, 0.0), (0, 1, 0), 0.14, 12)
        d.add_box_mesh("auto", "black", (bx - 0.02, y0 + 0.73, 0.0), (0.02, 0.005, 0.22))     # the needle, fore and aft
    else:
        # (0.3.12, the user's note 114: "here we could place compass model") the compass on the binnacle's lid, where
        # it's seen from the helm and from the deck (the one in the cupboard behind the glazed face wasn't): a brass
        # bowl on a turned foot, a rim round its mouth, the card in it with its needle, glass over it; the lamp below
        # still lights it through the glazed face
        top = y0 + 1.06                                   # (the lid's top)
        c = np.array([bx, top, 0.0])
        up = np.array([0.0, 1.0, 0.0])
        # (its foot from 0.01 down in the lid up into the bowl, open-ended: no face on the lid's or the bowl's)
        d.add_cylinder("exterior", "gold", c - up * 0.01, c + up * 0.035, 0.11, 0.09, sides=16, smooth=False, caps=False)
        d.add_cylinder("exterior", "gold", c + up * 0.03, c + up * 0.1, 0.13, 0.155, sides=16, caps=(True, False))  # bowl
        d.add_cylinder("exterior", "gold", c + up * 0.065, c + up * 0.1, 0.137, 0.145, sides=16, caps=False,
                       inward=True)                                                                    # its inside
        annulus(d, "exterior", "gold", c + up * 0.1, (0, 1, 0), 0.145, 0.17, sides=16)                  # the rim
        d.add_cylinder("exterior", "gold", c + up * 0.085, c + up * 0.1, 0.17, 0.17, sides=16, smooth=False, caps=False)
        annulus(d, "exterior", "gold", c + up * 0.085, (0, -1, 0), 0.149, 0.17, sides=16)
        disk(d, "exterior", "paper", c + up * 0.065, (0, 1, 0), 0.137, 16)                                  # the card
        # the needle north to the bow, and the card's east-west (a hair lower: their tops not in one plane)
        d.add_box_mesh("exterior", "black", c + up * 0.068, (0.2, 0.008, 0.018))
        d.add_box_mesh("exterior", "black", c + up * 0.0675, (0.018, 0.007, 0.1))
        disk(d, "glass", "glass", c + up * 0.09, (0, 1, 0), 0.14, 16)                                       # the glass
    # the lamp on its port side ((0.3.30, note 240: "what is this button?": the little iron lamp box with its yellow glass,
    # unlit, read as a button; it lit the card inside the cupboard, and the compass is on the lid since 0.3.12: gone. A
    # lantern can be hung by the helm if wanted)
    if not GRID:
        d.add_box_mesh("exterior", "iron", (bx, y0 + 0.8, -0.29), (0.14, 0.2, 0.08))
        d.add_box_mesh("exterior", "glass-yellow", (bx, y0 + 0.8, -0.335), (0.1, 0.12, 0.01))


# ==================================================================== capstan and the anchor cable
def capstan_and_cable(d):
    """On the port side in front of the forecastle (in the carrack of 0.3.4 and 0.3.5 aft of the mainmast): the bower
    cable comes out of the forecastle bulkhead, runs along the deck to it (since 0.3.6 straight on from the hawse and the
    bitts onto the barrel's inboard side), takes turns round the barrel and goes down into the cable tier on the lower
    deck."""
    cap = np.array([CAPSTAN_X, MAIN, CAPSTAN_Z])
    up = np.array([0.0, 1.0, 0.0])
    # (GRID: all of it within its cell: the drumhead and the base CAPSTAN_R, the barrel and its whelps inside them)
    k_ = CAPSTAN_R / 0.52
    d.add_cylinder("exterior", "timber", cap, cap + up * 0.12, min(0.58, CAPSTAN_R), min(0.58, CAPSTAN_R), sides=12,
                   smooth=False)                                                                            # base
    d.add_cylinder("exterior", "timber", cap + up * 0.12, cap + up * 0.85, 0.42 * k_, 0.37 * k_, sides=12, smooth=False)
    d.add_cylinder("exterior", "oak", cap + up * 0.85, cap + up * 1.0, CAPSTAN_R, CAPSTAN_R, sides=12, smooth=False)  # drumhead
    # whelps: ribs round the barrel for the cable to grip
    for k in range(8):
        a = 2 * math.pi * k / 8
        rad = np.array([math.cos(a), 0, math.sin(a)])
        tang = np.array([-math.sin(a), 0, math.cos(a)])
        d.add_box_mesh("exterior", "timber", cap + up * 0.48 + rad * 0.42 * k_, (0.09, 0.66, 0.07),
                       np.column_stack([rad, up, tang]))
    # pawl ring on the deck and two pawls that stop her running back
    pr = 0.6 if GRID else 0.74
    d.add_cylinder("exterior", "iron", cap - up * 0.01, cap + up * 0.03, pr, pr, sides=16, smooth=False)
    for a in (0.3, math.pi + 0.3):
        rad = np.array([math.cos(a), 0, math.sin(a)])
        tang = np.array([-math.sin(a), 0, math.cos(a)])
        d.add_box_mesh("exterior", "iron", cap + up * 0.08 + rad * (pr - 0.12) + tang * 0.1, (0.07, 0.07, 0.3),
                       np.column_stack([rad, up, tang]))
    # the bars, pushed round by the crew (two of them shipped, the others stowed)
    for i in range(2):
        a = i * math.pi / 2 + math.pi / 4
        p = CAPSTAN_BARS * np.array([math.cos(a), 0, math.sin(a)])
        c = cap + up * 0.92
        d.add_beam_mesh("exterior", "timber", c - p, c + p, 0.08)
        for q in (c - p, c + p):
            d.add_box_mesh("exterior", "iron", q, (0.1, 0.1, 0.1))
    # its spindle below, stepped on the lower deck. (0.3.10, GRID, the user's note 99: none, it stands on the main deck)
    if not GRID:
        d.add_cylinder("auto", "timber", (CAPSTAN_X, LOWER, CAPSTAN_Z), (CAPSTAN_X, MAIN - DECK_T, CAPSTAN_Z), 0.18, 0.18,
                       sides=10, smooth=False)
        d.add_box_mesh("auto", "timber", (CAPSTAN_X, LOWER + 0.07, CAPSTAN_Z), (0.55, 0.14, 0.55))
        d.add_cylinder("auto", "iron", (CAPSTAN_X, MAIN - DECK_T - 0.12, CAPSTAN_Z),
                       (CAPSTAN_X, MAIN - DECK_T, CAPSTAN_Z), 0.22, 0.22, sides=10, smooth=False)

    if GRID:
        capstan_cable_grid(d)
        return
    # the cable: out of the forecastle bulkhead, a few turns round the barrel, and down a hole into the cable tier
    r = 0.075
    wrap_r = 0.47
    y_top, y_bot = MAIN + 0.62, MAIN + 0.32
    src = (WAIST_FWD, CABLE_Z)
    # onto the barrel's inboard side, in line with where it comes through the bulkhead (a capstan further aft: its
    # outboard side)
    side = 1 if BOW_CAPSTAN else -1
    tx, tz, a0 = tangent_point(CAPSTAN_X, CAPSTAN_Z, wrap_r, src[0], src[1], side=side)
    run = [np.array([WAIST_FWD + 0.25, MAIN + r, CABLE_Z]), np.array([WAIST_FWD - 0.3, MAIN + r, CABLE_Z]),
           np.array([tx, y_top, tz])]
    rope_path(d, run, r=r)
    # the cable tier on the lower deck, inboard of the spindle (clear of it and of the berths along the side), and the
    # hole in the main deck the cable goes down by, straight over it (0.3.7: the hole was by the barrel and the cable
    # ran down aslant to the tier beside it, the user's note 25), clear of the deck beams
    if BOW_CAPSTAN:
        cc = np.array([CAPSTAN_X + 0.35, 0, CAPSTAN_Z + 0.95])
    else:
        cc = np.array([CAPSTAN_X - 0.9, 0, -1.9 if PROPOSAL else -0.75])
    hole = np.array([cc[0], MAIN, cc[2]])
    # off the barrel where it leads fair to the hole (the way it's wound), after two turns and that much more
    a_off = None
    for sd in (1, -1):
        px, pz, a = tangent_point(CAPSTAN_X, CAPSTAN_Z, wrap_r, hole[0], hole[2], side=sd)
        t_dir = side * np.array([-math.sin(a), math.cos(a)])
        if t_dir @ np.array([hole[0] - px, hole[2] - pz]) > 0:
            a_off = a
    turns = 2.0 + ((a_off - a0) * side % (2 * math.pi)) / (2 * math.pi)
    n_coil = int(turns * 20)
    # wound on round the barrel the way it comes onto it
    coil = [np.array([CAPSTAN_X + wrap_r * math.cos(a0 + side * 2 * math.pi * turns * k / n_coil),
                      y_top + (y_bot - y_top) * k / n_coil,
                      CAPSTAN_Z + wrap_r * math.sin(a0 + side * 2 * math.pi * turns * k / n_coil)]) for k in range(n_coil + 1)]
    rope_path(d, coil, r=r)
    lead = coil[-1] + (hole - coil[-1]) * 0.4
    lead[1] = MAIN + r
    rope_path(d, [coil[-1], lead, hole + np.array([0, r, 0]), hole - np.array([0, DECK_T + 0.3, 0])], r=r, group="auto")
    disk(d, "exterior", "black", hole + np.array([0, 0.004, 0]), (0, 1, 0), 0.1, 10)
    d.add_cylinder("exterior", "iron", hole - np.array([0, 0.01, 0]), hole + np.array([0, 0.03, 0]), 0.14, 0.14,
                   sides=10, smooth=False, caps=False)
    d.add_cylinder("auto", "iron", hole - np.array([0, DECK_T + 0.02, 0]), hole - np.array([0, DECK_T - 0.02, 0]), 0.14,
                   0.14, sides=10, smooth=False)
    d.cable_hole = hole
    # straight down from it into the middle of the coil
    rope_path(d, [hole - np.array([0, DECK_T + 0.3, 0]), np.array([cc[0], LOWER + 0.45, cc[2]]),
                  np.array([cc[0] + 0.1, LOWER + 0.3, cc[2]]), np.array([cc[0] + 0.18, LOWER + r + 0.13, cc[2]])],
              r=r, group="auto")
    for layer in range(2):
        for k, rad in enumerate((0.42, 0.34, 0.26, 0.18)):
            pts = ring_points((cc[0], LOWER + r + 0.13 * layer, cc[2]), rad, "y", 14, a0=0.3 * k)
            rope_path(d, pts, r=r, group="auto")


CABLE_WRAP = 0.40          # (GRID) the cable's middle round the capstan's barrel


def capstan_cable_grid(d):
    """
    (0.3.9, the user's note 64: "capstan needs mirror on other side of the rope together with rope bundle below") The
    cable comes out of the forecastle bulkhead straight aft onto the barrel's outboard side, takes two and a half turns
    round it, and leaves it on the inboard side heading forward again (the first's mirror image about the capstan's
    middle), down to the deck, where its slack lies in a coil (a fake) inboard of the capstan and forward of it, under
    the bars and inside where the crew walk.
    """
    r = 0.075
    cx, cz, w = CAPSTAN_X, CAPSTAN_Z, CABLE_WRAP
    y_top, y_bot = MAIN + 0.62, MAIN + 0.3
    run = [np.array([WAIST_FWD + 0.25, MAIN + r, CABLE_Z]), np.array([WAIST_FWD - 0.3, MAIN + r, CABLE_Z]),
           np.array([cx + 0.6, y_top, cz - w]), np.array([cx, y_top, cz - w])]
    rope_path(d, run, r=r)
    # wound round (the angle from +x toward +z falling: it comes on at -90 deg moving aft), 2.5 turns: off at +90 deg
    turns = 2.0 if CAPSTAN_Z == 0.0 else 2.5      # (0.3.10, on the centreline: off where it came on, heading aft)
    coil_fwd = CAPSTAN_Z != 0.0 or (GRID and CAPSTAN_X < 11.0)
    if coil_fwd and CAPSTAN_Z == 0.0:
        # (0.3.13, note 130: a block aft, the hatch's coaming and the hold ladder's head 1.5 aft of it) the slack off
        # the barrel's starboard side heading forward, in a coil forward of it (2 m clear of the forecastle door)
        turns = 2.5
    n = int(turns * 20)
    a0 = -math.pi / 2
    coil = [np.array([cx + w * math.cos(a0 - 2 * math.pi * turns * k / n), y_top + (y_bot - y_top) * k / n,
                      cz + w * math.sin(a0 - 2 * math.pi * turns * k / n)]) for k in range(n + 1)]
    rope_path(d, coil, r=r)
    # off the barrel forward, down to the deck and round into the coil of its slack
    fk = np.array([cx + 0.85, 0.0, cz + 0.45])                      # the coil's middle on the deck
    off = coil[-1]
    lead = [off, np.array([off[0] + 0.45, MAIN + 0.12, off[2]]), np.array([fk[0] - 0.05, MAIN + r, fk[2] + 0.25])]
    if CAPSTAN_Z == 0.0 and not coil_fwd:
        # (0.3.10: the slack in a coil aft of the barrel, under the bars, clear of the forecastle door's way)
        fk = np.array([cx - 0.85, 0.0, cz - 0.45])
        lead = [off, np.array([off[0] - 0.45, MAIN + 0.12, off[2]]), np.array([fk[0] + 0.05, MAIN + r, fk[2] - 0.25])]
    rope_path(d, lead, r=r)
    for layer in range(2):
        for k, rad in enumerate((0.25, 0.18, 0.11)):
            pts = ring_points((fk[0], MAIN + r + 0.12 * layer, fk[2]), rad, "y", 14, a0=0.3 * k)
            rope_path(d, pts, r=r)
    d.cable_hole = None
    d.cable_coil = fk


# ==================================================================== anchor: hawse holes, bitts, cathead, the anchor
def anchor_gear(d):
    bx = float(interp(HAWSE_Y, BOW_X))
    wfn = hull_hw
    if GRID:
        # (0.3.10: through the forecastle's side, over its ledge)
        bx, wfn = outer_bow_x(HAWSE_Y), half_width
    lo, hi = 8.0 + STRETCH, bx
    for _ in range(40):                       # where the bow is HAWSE_Z wide at the hawse's height
        m = (lo + hi) / 2
        if float(wfn(np.array(m), np.array(HAWSE_Y))) > HAWSE_Z:
            lo = m
        else:
            hi = m
    xh = (lo + hi) / 2
    n0 = hull_normal(xh, HAWSE_Y, wfn)
    hawse = {}
    # inside, each hole goes through a hawse piece: a broad timber in the frame's place round it (the frame stops under
    # and over it: framing), as thick as the frames stand in from the planking. Since 0.3.7: the hole was cut through
    # the frame there before (the user's note 23)
    pad = FRAME_MOULDED
    for s in (1, -1):
        n = np.array([n0[0], 0.0, s * n0[2]])
        h_out = np.array([xh, HAWSE_Y, s * HAWSE_Z])
        h_in = h_out - n * HULL_T
        hawse[s] = (h_out, h_in - n * pad, n)
        # outside: a round bolster with the dark hole in it
        d.add_cylinder("exterior", "timber", h_out - n * 0.03, h_out + n * 0.08, 0.24, 0.21, sides=12, smooth=False)
        disk(d, "exterior", "black", h_out + n * 0.085, n, 0.12, 12)
        # inside: the other end of the hawse pipe, on the hawse piece's face
        e = h_in - n * pad
        d.add_cylinder("auto", "iron", e + n * 0.03, e - n * 0.04, 0.2, 0.19, sides=12, smooth=False)
        disk(d, "auto", "black", e - n * 0.045, -n, 0.12, 12)
    d.hawse_pieces = [(float(hawse[1][1][0] + n0[0] * pad), HAWSE_Y, 0.32, 0.4)]

    # the riding bitts: two stout posts and a crosspiece; the cable takes a turn round the one on its side
    cs = CABLE_SIDE
    if GRID:
        anchor_gear_grid(d, hawse)
        return
    for s in (1, -1):
        p = np.array([BITTS_X, MAIN + 0.5, s * BITTS_Z])
        d.add_box_mesh("auto", "timber", p, (0.24, 1.0, 0.24))
        d.add_box_mesh("auto", "iron", p + np.array([0, 0.51, 0]), (0.26, 0.04, 0.26))
    d.add_box_mesh("auto", "timber", (BITTS_X + 0.2, MAIN + 0.62, 0.0), (0.18, 0.2, 2 * BITTS_Z + 0.45))
    r = 0.075
    h_out, h_in, n = hawse[cs]
    # a round turn on the post: in from the hawse along its outboard side, once round it going down, and away aft
    # along the outboard side again, on to the bulkhead and the capstan in a straight line. (Clear of the post's
    # corners all round: 0.12 + the cable's 0.075 + a little; it was a straight rope through the post until 0.3.7, the
    # helix's turn the other way having only its two ends: the user's note 4)
    wrap = 0.25
    out_a = math.atan2(cs, 0.0)                         # the post's outboard side
    turn = helix(BITTS_X, cs * BITTS_Z, wrap, MAIN + 0.36, MAIN + 0.14, out_a, cs * 1.0, 16)
    rope_path(d, [h_in - n * 0.02, np.array([BITTS_X + 0.35, MAIN + 0.38, cs * (BITTS_Z + wrap)]), turn[0]], r=r,
              group="auto")
    rope_path(d, turn, r=r, group="auto")
    rope_path(d, [turn[-1], np.array([BITTS_X - 0.45, MAIN + r, cs * (BITTS_Z + wrap)]),
                  np.array([WAIST_FWD + 0.25, MAIN + r, CABLE_Z])], r=r, group="auto")

    # catheads, both bows: a beam out through the forecastle bulwark to hang the anchor from
    ends = {}
    for s in (1, -1):
        ci = np.array([13.15 + STRETCH, QUARTER + 0.55, s * 1.9])
        co = np.array([14.3 + STRETCH, QUARTER + 0.55, s * (3.65 if FC_LOW < FC_Y0 else 3.3)])   # clear of the flare
        d.add_beam_mesh("auto", "timber", ci, co, 0.26, 0.28)
        d.add_box_mesh("exterior", "timber", co + (co - ci) / np.linalg.norm(co - ci) * 0.1, (0.3, 0.32, 0.3))
        d.add_cylinder("exterior", "iron", co + np.array([0, -0.16, 0]), co + np.array([0, -0.3, 0]), 0.03, 0.03,
                       sides=6)
        kx_ = 13.95 + STRETCH
        knee_a = np.array([kx_, FC_Y0 + 0.05, s * (hw(kx_, FC_Y0 + 0.05) + 0.02)])
        d.add_beam_mesh("exterior", "timber", knee_a, co + np.array([-0.15, -0.16, -s * 0.25]), 0.14)
        ends[s] = co

    # the anchor, hanging at the cathead on its side with the anchor up; it goes down and comes back up with the
    # anchor (the mod slides it by the anchor's state)
    rc = ends[cs] + np.array([0, -0.52, 0])            # ring centre
    grp = "part:anchor"
    ring = ring_points(rc, 0.2, "z", 14)
    rope_path(d, ring, r=0.035, group=grp, mat="iron")
    rope_path(d, [ends[cs] + np.array([0, -0.3, 0]), rc + np.array([0, 0.19, 0])], r=0.03, group=grp)
    eye = rc - np.array([0, 0.2, 0])
    crown = eye - np.array([0, 2.2, 0])
    d.add_box_mesh(grp, "iron", (eye + crown) / 2, (0.1, 2.2, 0.1))
    d.add_box_mesh(grp, "iron", crown, (0.14, 0.16, 0.14))
    # the stock (wood, with iron bands), across the arms
    sy = eye - np.array([0, 0.28, 0])
    d.add_box_mesh(grp, "timber", sy, (1.5, 0.15, 0.15))
    for dx in (-0.45, 0.45):
        d.add_box_mesh(grp, "iron", sy + np.array([dx, 0, 0]), (0.05, 0.17, 0.17))
    for sgn in (1, -1):
        tipa = crown + np.array([0, 0.62, sgn * 0.66])
        u = (tipa - crown) / np.linalg.norm(tipa - crown)
        perp = np.array([0, -u[2], u[1]])
        d.add_beam_mesh(grp, "iron", crown, tipa, 0.09)
        d.add_box_mesh(grp, "iron", tipa - u * 0.12, (0.38, 0.045, 0.3), np.column_stack([u, perp, [1.0, 0, 0]]))
    d.moving_parts.append({"code": "anchor", "kind": "slide", "axisPoint": [0.0, 0.0, 0.0], "axisDir": [0.0, -1.0, 0.0],
                           "distance": 8.5, "source": "anchor"})
    # the cable from the hawse to the anchor's ring, following the anchor down
    tipr = rc - np.array([0, 0.19, 0])
    d.add_rope(h_out + n * 0.06, tipr, r=r, group="part:anchorcable")
    d.moving_parts.append({"code": "anchorcable", "kind": "follow", "follow": "anchor",
                           "anchor": [round(float(v), 4) for v in h_out + n * 0.06],
                           "tip": [round(float(v), 4) for v in tipr]})


def anchor_gear_grid(d, hawse):
    """
    (0.3.10, the waist re-planned, the user's notes 88, 90 and decision) Two bower anchors. The riding bitts as before;
    each cable in at its hawse, a round turn on its bitt; the port one (the working cable) aft along z -1.25 past the
    foremast to the forecastle's bulkhead beside the door and on to the capstan (capstan_cable_grid); the starboard one
    stoppered at its bitt and its inboard end down a deck pipe to the cable tier on the lower deck. Catheads 1.5 forward
    of the hawse holes, 1.2 out of the side; the port anchor hangs catted under its cathead (the mod lets it go and
    weighs it, the moving part "anchor"); the starboard one fished: its ring at the cathead, its shank down aft along the
    forecastle's side, its crown on a bill-board.
    """
    r = 0.075
    for s in (1, -1):
        p = np.array([BITTS_X, MAIN + 0.5, s * BITTS_Z])
        d.add_box_mesh("auto", "timber", p, (0.24, 1.0, 0.24))
        d.add_box_mesh("auto", "iron", p + np.array([0, 0.51, 0]), (0.26, 0.04, 0.26))
    d.add_box_mesh("auto", "timber", (BITTS_X + 0.2, MAIN + 0.62, 0.0), (0.18, 0.2, 2 * BITTS_Z + 0.45))
    wrap = 0.25
    for s in (1, -1):
        h_out, h_in, n = hawse[s]
        out_a = math.atan2(s, 0.0)
        turn = helix(BITTS_X, s * BITTS_Z, wrap, MAIN + 0.36, MAIN + 0.14, out_a, s * 1.0, 16)
        rope_path(d, [h_in - n * 0.02, np.array([BITTS_X + 0.35, MAIN + 0.38, s * (BITTS_Z + wrap)]), turn[0]], r=r,
                  group="auto")
        rope_path(d, turn, r=r, group="auto")
        if s == CABLE_SIDE:
            rope_path(d, [turn[-1], np.array([BITTS_X - 0.45, MAIN + r, s * (BITTS_Z + wrap)]),
                          np.array([FORE_X - 0.5, MAIN + r, CABLE_Z]), np.array([WAIST_FWD + 0.25, MAIN + r, CABLE_Z])],
                      r=r, group="auto")
        else:
            # stoppered: from its turn aft to the deck pipe beside the bitt, and down it
            px_, pz_ = next(pp for pp in DECK_PIPES if pp[1] * s > 0)
            pipe_ = np.array([px_, MAIN, pz_])
            rope_path(d, [turn[-1], np.array([px_ + 0.25, MAIN + r, pz_]), pipe_ + np.array([0, r, 0]),
                          pipe_ - np.array([0, DECK_T + 0.3, 0])], r=r, group="auto")
            # a stopper: a short rope from the bitt's crosspiece round the cable
            rope_path(d, [np.array([BITTS_X + 0.2, MAIN + 0.55, s * (BITTS_Z - 0.3)]),
                          np.array([BITTS_X - 0.15, MAIN + 0.3, s * (BITTS_Z + wrap)])], r=0.03, group="auto")
    # the deck pipes (an iron rim, the hole), and the cable tier under the idle one on the lower deck
    for px_, pz_ in DECK_PIPES:
        hole = np.array([px_, MAIN, pz_])
        disk(d, "auto", "black", hole + np.array([0, 0.004, 0]), (0, 1, 0), 0.1, 10)
        d.add_cylinder("auto", "iron", hole - np.array([0, 0.01, 0]), hole + np.array([0, 0.03, 0]), 0.14, 0.14,
                       sides=10, smooth=False, caps=False)
        d.add_cylinder("auto", "iron", hole - np.array([0, DECK_T + 0.02, 0]), hole - np.array([0, DECK_T - 0.02, 0]),
                       0.14, 0.14, sides=10, smooth=False)
        if pz_ * CABLE_SIDE < 0:
            cc = np.array([px_, 0.0, pz_])
            rope_path(d, [hole - np.array([0, DECK_T + 0.3, 0]), np.array([cc[0], LOWER + 0.45, cc[2]]),
                          np.array([cc[0] + 0.1, LOWER + 0.3, cc[2]]), np.array([cc[0] + 0.18, LOWER + r + 0.13, cc[2]])],
                      r=r, group="auto")
            for layer in range(2):
                for k, rad in enumerate((0.42, 0.34, 0.26, 0.18)):
                    rope_path(d, ring_points((cc[0], LOWER + r + 0.13 * layer, cc[2]), rad, "y", 14, a0=0.3 * k),
                              r=r, group="auto")

    # catheads, both bows
    ends = {}
    (cix, ciz), (cox, coz) = CATHEAD
    for s in (1, -1):
        ci = np.array([cix, QUARTER + 0.55, s * ciz])
        co = np.array([cox, QUARTER + 0.55, s * coz])
        d.add_beam_mesh("auto", "timber", ci, co, 0.26, 0.28)
        d.add_box_mesh("exterior", "timber", co + (co - ci) / np.linalg.norm(co - ci) * 0.1, (0.3, 0.32, 0.3))
        d.add_cylinder("exterior", "iron", co + np.array([0, -0.16, 0]), co + np.array([0, -0.3, 0]), 0.03, 0.03,
                       sides=6)
        kx_ = cix + 0.6
        knee_a = np.array([kx_, FC_Y0 + 0.05, s * (hw(kx_, FC_Y0 + 0.05) + 0.02)])
        d.add_beam_mesh("exterior", "timber", knee_a, co + np.array([-0.15, -0.16, -s * 0.25]), 0.14)
        ends[s] = co

    def anchor(grp, eye, crown, side_dir, inner_reach):
        """An anchor between its eye (the ring's end of the shank) and its crown: its stock across the shank near the
        eye, along the side (side_dir: the side's outward normal); its arms at the crown in the plane of the shank and
        that normal, the outer one full length, the inner one inner_reach (it rests against the side)."""
        u = (eye - crown) / np.linalg.norm(eye - crown)
        d.add_beam_mesh(grp, "iron", crown, eye, 0.1, 0.1)
        d.add_box_mesh(grp, "iron", crown, (0.14, 0.16, 0.14))
        sy = eye - u * 0.28
        stock_dir = np.cross(u, side_dir)
        stock_dir /= np.linalg.norm(stock_dir)
        w = np.cross(stock_dir, u)
        w *= np.sign(w @ side_dir) or 1.0
        Rs = np.column_stack([stock_dir, u, w])
        d.add_box_mesh(grp, "timber", sy, (1.5, 0.15, 0.15), Rs)
        for dx in (-0.45, 0.45):
            d.add_box_mesh(grp, "iron", sy + stock_dir * dx, (0.05, 0.17, 0.17), Rs)
        for sgn, reach in ((1, 0.66), (-1, inner_reach)):
            tipa = crown + u * 0.62 + w * sgn * reach
            ua = (tipa - crown) / np.linalg.norm(tipa - crown)
            perp = np.cross(ua, stock_dir)
            perp /= np.linalg.norm(perp)
            d.add_beam_mesh(grp, "iron", crown, tipa, 0.09)
            d.add_box_mesh(grp, "iron", tipa - ua * 0.12, (0.38, 0.045, 0.3), np.column_stack([ua, perp, stock_dir]))

    # the working (port) anchor: catted, hanging under its cathead (the mod slides it down and up)
    cs = CABLE_SIDE
    rc = ends[cs] + np.array([0, -0.52, 0])
    grp = "part:anchor"
    rope_path(d, ring_points(rc, 0.2, "z", 14), r=0.035, group=grp, mat="iron")
    rope_path(d, [ends[cs] + np.array([0, -0.3, 0]), rc + np.array([0, 0.19, 0])], r=0.03, group=grp)
    eye = rc - np.array([0, 0.2, 0])
    anchor(grp, eye, eye - np.array([0, 2.2, 0]), np.array([0, 0, float(cs)]), 0.66)
    d.moving_parts.append({"code": "anchor", "kind": "slide", "axisPoint": [0.0, 0.0, 0.0], "axisDir": [0.0, -1.0, 0.0],
                           "distance": 8.5, "source": "anchor"})
    h_out, h_in, n = hawse[cs]
    tipr = rc - np.array([0, 0.19, 0])
    d.add_rope(h_out + n * 0.06, tipr, r=r, group="part:anchorcable")
    d.moving_parts.append({"code": "anchorcable", "kind": "follow", "follow": "anchor",
                           "anchor": [round(float(v), 4) for v in h_out + n * 0.06],
                           "tip": [round(float(v), 4) for v in tipr]})
    # the idle (starboard) anchor fished: its ring under the cathead, its crown on a bill-board on the side
    s = -cs
    ring_c = np.array([cox, QUARTER, s * coz])
    crown_x, crown_y = 18.8, GRID_OFF + 2.7
    crown = np.array([crown_x, crown_y, s * (hw(crown_x, crown_y) + 0.3)])
    eye = ring_c - (ring_c - crown) / np.linalg.norm(ring_c - crown) * 0.2
    anchor("exterior", eye, crown, np.array([0, 0, float(s)]), 0.2)
    rope_path(d, ring_points(ring_c, 0.2, "z", 14), r=0.035, group="exterior", mat="iron")
    rope_path(d, [np.array([cox, QUARTER + 0.39, s * coz]), ring_c + np.array([0, 0.19, 0])], r=0.03, group="exterior")
    bb = np.array([crown_x, crown_y - 0.12, s * (hw(crown_x, crown_y - 0.12) + 0.14)])
    d.add_box_mesh("exterior", "timber", bb, (0.9, 0.08, 0.3))                      # the bill-board
    h_out, h_in, n = hawse[s]
    d.add_rope(h_out + n * 0.06, ring_c + np.array([0, -0.19, 0]), r=r, group="exterior")


# ==================================================================== the bilge pump
def pump(d):
    """
    The bilge pump on the main deck, its pipe down to the keelson. (0.3.8, the user's note 41: "the pump in my mind
    needs to be bronze") Its chamber above the deck is cast bronze, as a costly ship's might be (most were a bored elm
    log hooped with iron): on a flange on the deck, moulded rings round it, a cap, and a spout cast on, running out and a
    little down to the waterway; the brake is wood on a bronze fork and pin. Where the spout's mouth is and which way the
    water leaves it go in the pump fixture (spout, spoutDir) for the mod to show the water coming out as it's worked.
    """
    px, pz = PUMP
    up = np.array([0.0, 1.0, 0.0])
    base = np.array([px, MAIN, pz])
    d.add_cylinder("exterior", "bronze", base, base + up * 0.05, 0.23, 0.21, sides=16, smooth=False)       # flange
    d.add_cylinder("exterior", "bronze", base + up * 0.05, base + up * 1.0, 0.17, 0.155, sides=16)          # chamber
    for h, r in ((0.09, 0.19), (0.48, 0.18), (0.94, 0.175)):                                              # its rings
        d.add_cylinder("exterior", "bronze", base + up * (h - 0.03), base + up * (h + 0.03), r, r, sides=16, smooth=False)
    d.add_cylinder("exterior", "bronze", base + up * 1.0, base + up * 1.06, 0.195, 0.18, sides=16, smooth=False)  # cap
    # the spout, pointing outboard to the waterway (on the centreline since 0.3.6: to starboard, away from the capstan),
    # 15° down; a lip round its mouth, dark inside
    so = 1.0 if abs(pz) < 0.3 else math.copysign(1.0, pz)
    sd = np.array([0.0, -math.sin(math.radians(15)), so * math.cos(math.radians(15))])
    s0 = base + up * 0.74 + np.array([0, 0, so * 0.1])
    s1 = s0 + sd * 0.36
    d.add_cylinder("exterior", "bronze", s0, s1, 0.055, 0.045, sides=10)
    d.add_cylinder("exterior", "bronze", s1 - sd * 0.035, s1 + sd * 0.005, 0.06, 0.06, sides=10, smooth=False,
                   caps=False)
    annulus(d, "exterior", "bronze", s1 + sd * 0.005, sd, 0.034, 0.06, 10)
    disk(d, "exterior", "black", s1 + sd * 0.005, sd, 0.034, 10)
    d.pump_spout = (s1 + sd * 0.005, sd)
    # the brake: a lever on a fork, and the spear down into the barrel. The lever, its handle and the spear rock about
    # the pin while someone works the pump (the moving part "pump-brake", worked by the pump fixture)
    bd = PUMP_BRAKE
    handle = np.array([px + 1.05 * bd, MAIN + 1.02, pz])
    # (0.3.24, note 196: "pump animation second lever does not work correctly from mechanics point of view": the spear
    # was part of the brake, swinging about the pin through the cap) a brake pump as it works: the fork (its fulcrum) on a
    # lug cast on the cap's rim, toward the handle; the brake's short arm reaching from it over the bore, the spear hung
    # on a pin there straight down into the barrel through a hole in the cap. The brake rocks about the fork's pin
    # ("pump-brake"); the spear slides straight up and down under its pin ("pump-spear", as far as the pin rises and falls
    # with the brake), the pin's little swing across taken in the brake's slot
    piv = np.array([px + 0.2 * bd, MAIN + 1.30, pz])
    slope = (handle[1] - piv[1]) / (handle[0] - piv[0])
    pin = np.array([px, piv[1] + slope * (px - piv[0]), pz])                  # over the bore's middle
    short = np.array([px - 0.08 * bd, piv[1] + slope * (px - 0.08 * bd - piv[0]), pz])
    d.add_box_mesh("exterior", "bronze", (px + 0.2 * bd, MAIN + 1.04, pz), (0.12, 0.08, 0.2))      # the lug on the cap
    for dz in (-0.07, 0.07):                                                                       # the fork's cheeks
        d.add_box_mesh("exterior", "bronze", (piv[0], (MAIN + 1.07 + piv[1] + 0.06) / 2, pz + dz),
                       (0.08, piv[1] + 0.06 - MAIN - 1.07, 0.035))
    d.add_beam_mesh("part:pump-brake", "timber", short, handle, 0.07, 0.08)
    d.add_cylinder("exterior", "bronze", piv + np.array([0, 0, -0.11]), piv + np.array([0, 0, 0.11]), 0.025, 0.025,
                   sides=8)
    # a bronze strap round the brake at its pin, and the clevis at its end over the bore
    ub = (handle - short) / np.linalg.norm(handle - short)
    vb = np.cross(np.array([0, 0, 1.0]), ub)
    d.add_box_mesh("part:pump-brake", "bronze", piv, (0.12, 0.13, 0.1), np.column_stack([ub, vb, [0, 0, 1.0]]))
    for dz in (-0.055, 0.055):
        d.add_box_mesh("part:pump-brake", "bronze", pin + np.array([0, 0, dz]), (0.1, 0.13, 0.02),
                       np.column_stack([ub, vb, [0, 0, 1.0]]))
    d.add_cylinder("part:pump-brake", "timber", handle + np.array([0, 0, -0.22]), handle + np.array([0, 0, 0.22]), 0.035,
                   0.035, sides=6)
    d.moving_parts.append({"code": "pump-brake", "axisPoint": [round(float(v), 4) for v in piv], "axisDir": [0.0, 0.0, 1.0],
                           "source": "pump:pump", "angle": 12.0})
    # the spear: from its pin (an eye on its head between the clevis's cheeks) straight down into the bore
    d.add_cylinder("part:pump-spear", "iron", pin + np.array([0, -0.02, 0]), np.array([px, MAIN + 0.9, pz]), 0.02, 0.02,
                   sides=6)
    d.add_box_mesh("part:pump-spear", "iron", pin, (0.06, 0.07, 0.07))
    d.add_cylinder("part:pump-spear", "iron", pin + np.array([0, 0, -0.075]), pin + np.array([0, 0, 0.075]), 0.014,
                   0.014, sides=6)
    disk(d, "exterior", "black", (px, MAIN + 1.063, pz), (0, 1, 0), 0.035, 8)                      # the hole in the cap
    rx, ry = pin[0] - piv[0], pin[1] - piv[1]
    d.moving_parts.append({"code": "pump-spear", "kind": "slide", "axisPoint": [round(float(v), 4) for v in pin],
                           "axisDir": [0.0, 1.0, 0.0], "source": "pump:pump",
                           "distance": round(float(rx * math.sin(math.radians(12.0))), 4)})
    # below deck, its pipe down through the lower deck to the bottom of the hold: on the keelson (the pump on the
    # centreline) it stands in a strainer box over the limbers either side of it
    foot = KEELSON_TOP if abs(pz) < 0.3 else HOLD_FLOOR
    d.add_cylinder("auto", "timber", (px, MAIN - DECK_T, pz), (px, foot + 0.2, pz), 0.13, 0.13, sides=8, smooth=False)
    for y in (LOWER, MAIN - DECK_T - 0.1, LOWER - DECK_T - 0.1):
        d.add_cylinder("auto", "iron", (px, y - 0.04, pz), (px, y + 0.04, pz), 0.16, 0.16, sides=8, smooth=False)
    d.add_box_mesh("auto", "timber", (px, foot + 0.1, pz), (0.36, 0.2, 0.36))
    d.add_box_mesh("auto", "iron", (px, foot + 0.19, pz), (0.38, 0.03, 0.38))
    for k in range(3):
        for s in (1, -1):
            disk(d, "auto", "black", (px + 0.181 * s, foot + 0.07, pz - 0.1 + 0.1 * k), (s, 0, 0), 0.02, 5)


# ==================================================================== the boat gallows
def boat_gallows(d):
    """
    Two beams across the waist, level with the castle decks, with cradles for two boats side by side (the ship's boats,
    four people each). Since 0.3.6 each beam stands on two frames carried on up over the bulwark (toptimbers, the user:
    "made as continuation of ship's side ribs"): from the rail cap to the beam each post is its frame, in the frame's
    section (its inner face as far in as the frame's below, out to the outside planking's line) and leaning in with the
    side (the tumblehome); the beam lies across their heads, a little proud of them, a hanging knee inboard under each
    end. Boats stowed amidships on skid beams like these are a little later than the carrack's day, but it's where a ship
    kept its boats.
    """
    S = FRAME_SIDED
    y_head = GALLOWS_Y - GALLOWS_BEAM
    # (0.3.12, the user's note 119: "support beams cannot be painted") the posts, their knees and the beams with the
    # waist's frames (bulwark-frames: the posts are frames carried on up)
    old_tag = d.paint_tag
    for gx in GALLOWS_X:
        d.paint_tag = "frame" if GRID else None
        xf = gx + S / 2
        xa, xb = xf - S / 2, xf + S / 2
        y_foot = rail(xf)                              # the frame comes up to here, the rail cap stops against it
        # (0.3.13, the user's note 140, drawn: the post and its knee one timber curving smoothly into the beam) the post
        # straight only up to ELBOW_DROP under the beam's top, then a grown crook (gallows_elbow) into the beam's end
        y_post = GALLOWS_Y - ELBOW_DROP if GRID else y_head
        n = max(2, int(math.ceil((y_post - y_foot) / 0.25)))
        ys = np.linspace(y_foot, y_post, n + 1)
        for s in (1, -1):
            if GRID and MODULAR:
                # ((0.3.27, notes 213, 214, 220) carrack-m: as the castles' framing: the post its frame carried on up,
                # straight, in the frame's own section (its inner face the frame's, out to the outside planking's line:
                # no break at the rail) to the beam's underside; the beam a deck beam's section across their heads; a
                # hanging knee under each end, curved into the post and the beam as the castles' (fillet_knee))
                yb_ = GALLOWS_Y - GALLOWS_BEAM
                m_ = max(2, int(math.ceil((yb_ - y_foot) / 0.25)))
                ys_ = np.linspace(y_foot, yb_, m_ + 1)

                def PP(x, y, outer):
                    return (x, y, s * (float(hull_hw(x, y)) - 0.03 if outer else frame_face(x, y)))

                for k in range(m_):
                    y0_, y1_ = ys_[k], ys_[k + 1]
                    a0i, a1i, b0i, b1i = PP(xa, y0_, False), PP(xa, y1_, False), PP(xb, y0_, False), PP(xb, y1_, False)
                    a0o, a1o, b0o, b1o = PP(xa, y0_, True), PP(xa, y1_, True), PP(xb, y0_, True), PP(xb, y1_, True)
                    u0 = (4.0 * k) % 16
                    uv = (u0, 0, u0 + min(4.0, (y1_ - y0_) * 16), S * 16)
                    _quad_facing(d, "exterior", "timber", [a0i, a1i, b1i, b0i], (0, 0, -s), uv)          # inner face
                    _quad_facing(d, "exterior", "timber", [a0o, a1o, b1o, b0o], (0, 0, s), uv)           # outer face
                    _quad_facing(d, "exterior", "timber", [a0o, a1o, a1i, a0i], (-1, 0, 0))              # after face
                    _quad_facing(d, "exterior", "timber", [b0i, b1i, b1o, b0o], (1, 0, 0))               # forward face
                if STRAIGHT_KNEES:
                    # ((0.3.28, notes 228, 229) the castles' joint: a straight knee, as theirs)
                    straight_knee(d, "exterior", "timber", xf - (S - 0.02) / 2 + 0.01, xf + (S - 0.02) / 2 - 0.01, s, yb_,
                                  KNEE_DROP, KNEE_REACH, lambda x, y: frame_face(x, y))
                else:
                    fillet_knee(d, "exterior", "timber", xf - (S - 0.02) / 2 + 0.01, xf + (S - 0.02) / 2 - 0.01, s, yb_, 0.55,
                                0.42, lambda x, y: frame_face(x, y), 0.0)
                continue
            if GRID:
                # (0.3.24, note 190) the post and its crook one timber from the rail, in one smooth sweep from the side's
                # slope there into the beam, its grain run along it (gallows_timber)
                gallows_timber(d, xf, s, y_foot)
                continue

            def P(x, y, outer):
                return (x, y, s * (float(hull_hw(x, y)) - 0.03 if outer else frame_face(x, y)))

            for k in range(n):
                y0, y1 = ys[k], ys[k + 1]
                a0i, a1i, b0i, b1i = P(xa, y0, False), P(xa, y1, False), P(xb, y0, False), P(xb, y1, False)
                a0o, a1o, b0o, b1o = P(xa, y0, True), P(xa, y1, True), P(xb, y0, True), P(xb, y1, True)
                # the grain up the post, carried on from one piece to the next
                u0 = (4.0 * k) % 16
                uv = (u0, 0, u0 + min(4.0, (y1 - y0) * 16), S * 16)
                _quad_facing(d, "exterior", "timber", [a0i, a1i, b1i, b0i], (0, 0, -s), uv)          # inner face
                _quad_facing(d, "exterior", "timber", [a0o, a1o, b1o, b0o], (0, 0, s), uv)           # outer face
                _quad_facing(d, "exterior", "timber", [a0o, a1o, a1i, a0i], (-1, 0, 0))              # after face
                _quad_facing(d, "exterior", "timber", [b0i, b1i, b1o, b0o], (1, 0, 0))               # forward face
            # a hanging knee under the beam, inboard of the post
            if GRID:
                # (0.3.12, the user's note 119: "top of beam corners could smoothly bending inwards ... continious
                # spline. Also inner corner more rounded": a fillet knee 0.55 down the post, 0.5 in along the beam.
                # (0.3.13, note 140) with the post's head one crook: gallows_elbow)
                gallows_elbow(d, xf, s, y_post)
            else:
                prof = [(y_head, 0.0), (y_head, -0.5), (y_head - 0.3, -0.28), (y_head - 0.55, 0.0)]
                d.add_prism("exterior", "timber", [(xf - 0.09, y, s * (frame_face(xf, y) + 0.02 + dz)) for y, dz in prof],
                            [(xf + 0.09, y, s * (frame_face(xf, y) + 0.02 + dz)) for y, dz in prof])
        # the beam across the posts' heads, a little proud of them
        zo = float(hull_hw(xf, y_head)) + 0.05
        if GRID:
            zo = elbow_end(xf) + 0.01        # ((0.3.13, note 140) between the crooks, its ends 0.01 into them)
        bw_ = 0.28
        if GRID and MODULAR:
            # ((0.3.27) a deck beam's section, its ends flush with the posts' outer faces; (0.3.30, note 235: "end of these
            # ... are cut at angle to as rib outer face projection") each corner of its end on the post's outer face (it
            # leans in with her side going up, and her side narrows forward): the box to where it's narrowest, a wedge
            # out to the face each end)
            bw_ = S - 0.02

            def post_out(x, y):
                return float(hull_hw(x, y)) - 0.03

            zo = min(post_out(x_, y_) for x_ in (xf - bw_ / 2, xf + bw_ / 2) for y_ in (y_head, GALLOWS_Y))
            d.add_box_mesh("exterior", "timber", (xf, GALLOWS_Y - GALLOWS_BEAM / 2, 0.0), (bw_, GALLOWS_BEAM, 2 * zo))
            for s_ in (1, -1):
                beam_end_wedge(d, "exterior", "timber", xf, bw_, y_head, GALLOWS_Y, zo, s_, zfun=post_out)
        else:
            d.add_box_mesh("exterior", "timber", (xf, GALLOWS_Y - GALLOWS_BEAM / 2, 0.0), (bw_, GALLOWS_BEAM, 2 * zo))
        d.paint_tag = None
        # a cradle under each boat: two chocks shaped to the bilge, and a lashing ring
        for bz in BOATS_Z:
            for sgn in (1, -1):
                zc = bz + sgn * (BOAT_W / 2 - 0.22)
                d.add_box_mesh("exterior", "timber", (xf, GALLOWS_Y + 0.07, zc), (0.26, 0.14, 0.34))
            d.add_cylinder("exterior", "iron", (xf - bw_ / 2 - 0.01, GALLOWS_Y - 0.1, bz), (xf - bw_ / 2 - 0.03, GALLOWS_Y - 0.1, bz), 0.06,
                           0.06, sides=8)
    d.paint_tag = old_tag


ELBOW_DROP = 0.95          # (0.3.13, note 140) how far under the gallows beam's top a post's crook starts
ELBOW_IN = 0.55            # how far in from the post's inner face (under the beam) the crook runs into the beam


def elbow_end(xf):
    """(0.3.13) Where a gallows post's crook runs into its beam, out from the centreline."""
    return frame_face(xf, GALLOWS_Y - GALLOWS_BEAM) - ELBOW_IN


def gallows_elbow(d, xf, s, y0, n=16):
    """
    (0.3.13, the user's note 140: "i mean different shape", drawn: the post and the knee one timber that curves smoothly
    into the beam) A boat gallows post's head as a grown crook: from the straight post at y0 it bends inboard into the
    beam's end, its outer edge rising up the post's outer face and sweeping round in one long curve into the beam's top,
    its inner edge a tighter concave curve from the post's inner face into the beam's underside (each a cubic, tangent
    to the post's lean at its foot and level at the beam). Built in n sections across it, each from the inner curve to
    the outer, its breadth fore and aft growing from the post's (FRAME_SIDED) to the beam's (0.28, less 0.005 each
    side); its foot open on the
    post's head, its end open inside the beam's end (0.01 into it).
    """
    yb = GALLOWS_Y - GALLOWS_BEAM                         # the beam's underside
    we = elbow_end(xf)

    def bez(p0, t0, p3, t3, k0, k3):
        p0, p3 = np.asarray(p0, float), np.asarray(p3, float)
        p1, p2 = p0 + t0 * k0, p3 + t3 * k3
        return lambda t: ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t * t * p2 + t ** 3 * p3

    def curves(x):
        """(w: out from the centreline; y) the inner curve, from the post's inner face at y0 up into the beam's
        underside at we, and the outer, from its outer face at y0 round into the beam's top at we; along the post's
        side at x (the post's faces there, so the crook's foot is its head exactly)"""
        def wi(y):
            return frame_face(x, y)

        def wo(y):
            return float(hull_hw(x, y)) - 0.03

        li = np.array([wi(y0 + 0.1) - wi(y0), 0.1])
        lo = np.array([wo(y0 + 0.1) - wo(y0), 0.1])
        li, lo = li / np.linalg.norm(li), lo / np.linalg.norm(lo)
        # (its end 0.005 inside the beam's top and underside: not in their planes where it runs 0.01 into it)
        return (bez((wi(y0), y0), li, (we, yb + 0.005), np.array([1.0, 0.0]), 0.55 * (yb - y0), 0.55 * (wi(yb) - we)),
                bez((wo(y0), y0), lo, (we, GALLOWS_Y - 0.005), np.array([1.0, 0.0]), 0.55 * (GALLOWS_Y - y0),
                    0.55 * (wo(y0) - we)))

    (Ia, Oa), (Ib, Ob) = curves(xf - FRAME_SIDED / 2), curves(xf + FRAME_SIDED / 2)
    secs = []
    for k in range(n + 1):
        t = k / n
        h = (FRAME_SIDED + (0.27 - FRAME_SIDED) * (3 * t * t - 2 * t ** 3)) / 2     # (to 0.005 inside the beam's sides)
        ia, oa, ib, ob = Ia(t), Oa(t), Ib(t), Ob(t)
        secs.append([(xf - h, ia[1], s * ia[0]), (xf + h, ib[1], s * ib[0]), (xf + h, ob[1], s * ob[0]),
                     (xf - h, oa[1], s * oa[0])])
    for k in range(n):
        A, B = [np.asarray(p) for p in secs[k]], [np.asarray(p) for p in secs[k + 1]]
        across = (A[2] + A[3]) / 2 - (A[0] + A[1]) / 2           # inner curve to outer
        across /= np.linalg.norm(across)
        u0 = (4.0 * k) % 16
        uv = (u0, 0, u0 + 4.0, FRAME_SIDED * 16)
        _quad_facing(d, "exterior", "timber", [A[0], B[0], B[1], A[1]], -across, uv)        # the inner (concave) edge
        _quad_facing(d, "exterior", "timber", [A[3], B[3], B[2], A[2]], across, uv)         # the outer edge
        _quad_facing(d, "exterior", "timber", [A[0], A[3], B[3], B[0]], (-1, 0, 0))         # after face
        _quad_facing(d, "exterior", "timber", [A[1], B[1], B[2], A[2]], (1, 0, 0))          # forward face


def gallows_timber(d, xf, s, y0, n=28):
    """
    (0.3.24, the user's note 190: "These curved turns on beams does not nicely fit initial slope of ribs. Also textures are
    curved and stacked together") A boat gallows post and its crook as one timber: from the rail cap, where its frame
    comes up out of the bulwark (its section there the side's: the frame's inner face out to the outside planking's
    line, level), along the side's own slope there (the tangent of the frame under the rail), sweeping round in one
    curve (a cubic) into the beam's end, level there, its section the beam's depth and breadth (0.005 inside its faces),
    its end open 0.01 into the beam. Its section square to its line (from a level foot), its depth and breadth easing
    from the frame's to the beam's. The texture's grain (u) runs along it in one length from its foot to its end, the
    same texture on all its faces (no tile per piece).
    (0.3.13-0.3.23: a straight post up the hull's tumblehome over the rail, then a crook (gallows_elbow) starting from
    that post's lean, each of its 16 pieces a texture of its own across it)
    """
    yb = GALLOWS_Y - GALLOWS_BEAM

    def wi(y):
        return frame_face(xf, y)

    def wo(y):
        return float(hull_hw(xf, y)) - 0.03

    def wc(y):
        return (wi(y) + wo(y)) / 2

    p0 = np.array([wc(y0), y0])
    t0 = np.array([wc(y0) - wc(y0 - 0.3), 0.3])
    t0 /= np.linalg.norm(t0)
    h0 = (wo(y0) - wi(y0)) / 2
    we = elbow_end(xf)
    p3 = np.array([we, (yb + GALLOWS_Y) / 2])
    h3 = (GALLOWS_BEAM - 0.01) / 2
    k0 = 0.6 * (p3[1] - p0[1])
    k3 = 0.6 * max(0.3, p0[0] - p3[0])
    p1, p2 = p0 + t0 * k0, p3 + np.array([k3, 0.0])

    def bez(t):
        return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t * t * p2 + t ** 3 * p3

    def tan(t):
        v = 3 * ((1 - t) ** 2) * (p1 - p0) + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (p3 - p2)
        return v / np.linalg.norm(v)

    def ease(t):
        return 3 * t * t - 2 * t ** 3

    ts = np.linspace(0.0, 1.0, n + 1)
    secs, arc = [], [0.0]
    for k, t in enumerate(ts):
        c = bez(t)
        tv = tan(t)
        nrm = np.array([tv[1], -tv[0]])                      # out from the ship and up (the outer edge's side)
        # (its foot level, as the frame's head under it: the section turned square to its line over its first fifth)
        f = ease(min(1.0, t / 0.2))
        nb = np.array([1.0, 0.0]) * (1 - f) + nrm * f
        nb /= np.linalg.norm(nb)
        if k == 0:
            h = h0
        else:
            h = (h0 + (h3 - h0) * ease(t)) / max(0.5, float(np.dot(nb, nrm)))
        hx = (FRAME_SIDED + (0.27 - FRAME_SIDED) * ease(t)) / 2
        i_, o_ = c - nb * h, c + nb * h
        if k == n:
            i_, o_ = np.array([we, yb + 0.005]), np.array([we, GALLOWS_Y - 0.005])
        if k > 0:
            arc.append(arc[-1] + float(np.linalg.norm(c - bez(ts[k - 1]))))
        secs.append((i_, o_, hx))
    L = arc[-1]
    us = [a / L * 16.0 for a in arc]
    variant = int(round(xf * 7 + (s > 0))) % 8

    def put(pts, uvs, want):
        P_ = [np.asarray(p, float) for p in pts]
        nn = np.cross(P_[1] - P_[0], P_[3] - P_[0]) + np.cross(P_[3] - P_[2], P_[1] - P_[2])
        if np.linalg.norm(nn) < 1e-10:
            return
        if np.dot(nn, want) < 0:
            P_, uvs = [P_[1], P_[0], P_[3], P_[2]], [uvs[1], uvs[0], uvs[3], uvs[2]]
        d.add_quad("exterior", "timber", P_, uvs=uvs, variant=variant)

    def X(w, y, x):
        return (x, float(y), s * float(w))

    for k in range(n):
        (ia, oa, ha), (ib, ob, hb) = secs[k], secs[k + 1]
        ua, ub = us[k], us[k + 1]
        across = ((ob + ib) / 2 - (oa + ia) / 2)
        mid_n = np.array([across[1], -across[0]])
        mid_n /= np.linalg.norm(mid_n)
        out3 = np.array([0.0, mid_n[1], s * mid_n[0]])
        # the inner edge (concave) and the outer: v across its breadth
        put([X(*ia, xf - ha), X(*ib, xf - hb), X(*ib, xf + hb), X(*ia, xf + ha)],
            [(ua, 0.0), (ub, 0.0), (ub, 2 * hb * 16), (ua, 2 * ha * 16)], -out3)
        put([X(*oa, xf - ha), X(*ob, xf - hb), X(*ob, xf + hb), X(*oa, xf + ha)],
            [(ua, 0.0), (ub, 0.0), (ub, 2 * hb * 16), (ua, 2 * ha * 16)], out3)
        # its after and forward faces: v across its depth, from the inner edge
        da, db = float(np.linalg.norm(oa - ia)) * 16, float(np.linalg.norm(ob - ib)) * 16
        for sx in (-1, 1):
            put([X(*ia, xf + sx * ha), X(*ib, xf + sx * hb), X(*ob, xf + sx * hb), X(*oa, xf + sx * ha)],
                [(ua, 0.0), (ub, 0.0), (ub, db), (ua, da)], (sx, 0.0, 0.0))


# ==================================================================== the waist hatch (glazed) and the hold hatch
def hatches(d):
    if MODULAR:
        # (0.3.20, step B, docs/slots.md §5) the hatchways' framing only (the hatches are pieces): under the main deck,
        # carlings along the opening's sides (outside it, from the beam at 6 to the header) and a header across its
        # forward end, x 9..9.25; under the lower deck the hold's carlings at +-1.5 are its sides (a piece of one across
        # where the beam at 8 is cut) and a header between them. Every timber's end against the next one's face
        ox0, ox1, oz0, oz1 = OPENING
        S = FRAME_SIDED - 0.02                    # (the beams' width as drawn)
        with d.paint("beam"):
            # ((0.3.24, notes 186, 187) between the beams at 6 and 8 (both whole, under the opening's ends): a carling
            # along each side, from one beam's face to the other's, under the main deck (0.12 wide, just outside the
            # opening, as before) and under the lower deck (0.2 wide, the hold's: just outside it too); no headers)
            for yb_, dep, wc in ((MAIN - DECK_T, BEAM_D, 0.12), (LOWER - DECK_T, 0.5, 0.2)):
                for s in (1, -1):
                    d.add_box_mesh("auto", "timber", ((ox0 + ox1) / 2, yb_ - (dep - 0.04) / 2, s * (oz1 + wc / 2)),
                                   (ox1 - ox0 - S, dep - 0.04, wc))
        return
    hx0, hx1, hz0, hz1 = HATCH
    length = hx1 - hx0
    half = hz1

    def leaf(grp, glass_grp, hinge_y, s, glazed):
        """One leaf of a double hatch, hinged at the outer edge z = s*half, lying flat inboard to the centreline."""
        zc = s * half / 2
        y = hinge_y
        if glazed:
            t = 0.08
            for zr in (s * (half - 0.05), s * 0.04):                     # long rails
                d.add_box_mesh(grp, "timber", ((hx0 + hx1) / 2, y + t / 2, zr), (length - 0.02, t, 0.08))
            bars = [hx0 + 0.04, hx0 + length / 3, hx0 + 2 * length / 3, hx1 - 0.04]
            for bxx in bars:                                             # cross bars
                # (0.3.10, clean surfaces: 0.01 inside the rails' top and bottom faces where they cross them)
                d.add_box_mesh(grp, "timber", (bxx, y + t / 2, zc), (0.07, t - 0.02, half - 0.06))
            if GRID:
                # (0.3.13, the user's note 133: "Glass hatch now has too large glass planes should be split. Also planes
                # should be in lead framing") each bay halved both ways by a lighter glazing bar (0.05 broad, 0.012 under
                # the cross bars' top and over their bottom: not in their planes), and each of its four panes in lead
                # cames, small upright oblongs, as a period skylight's
                zm_ = s * (0.08 + half - 0.09) / 2
                d.add_box_mesh(grp, "timber", ((hx0 + hx1) / 2, y + t / 2, zm_), (length - 0.1, t - 0.044, 0.05))
                for a, b in zip(bars[:-1], bars[1:]):
                    # (the cross ones 0.003 over the long one's top and under its underside: not in its planes, nor
                    # within 2 mm of the cames' faces running into them)
                    d.add_box_mesh(grp, "timber", ((a + b) / 2, y + t / 2, zc), (0.05, t - 0.038, half - 0.1))
            for a, b in zip(bars[:-1], bars[1:]):                        # the panes
                pz0, pz1 = sorted((s * 0.08, s * (half - 0.09)))
                pts = [(a + 0.035, y + 0.04, pz1), (b - 0.035, y + 0.04, pz1), (b - 0.035, y + 0.04, pz0),
                       (a + 0.035, y + 0.04, pz0)]      # facing up
                d.add_quad(glass_grp, "glass", pts, (0, 0, 16, 16))
                if GRID:
                    xm_, zq = (a + b) / 2, s * (0.08 + half - 0.09) / 2     # (the glazing bars' middles)
                    zlo, zhi = sorted((s * 0.08, s * (half - 0.09)))
                    for u0, u1 in ((a + 0.035, xm_ - 0.025), (xm_ + 0.025, b - 0.035)):
                        for v0, v1 in ((zlo, zq - 0.025), (zq + 0.025, zhi)):
                            # ((0.3.14, the user's note 146: "about twice bigger grid on glass") its panes about
                            # 0.26 a side, 0.3.13's 0.13)
                            lead_cames(d, grp, lambda u, v: (u, y + 0.04, v), (0, 1.0, 0), u0, u1, v0, v1,
                                       pitch=(0.26, 0.26), ext=0.008)
        else:
            t = 0.1
            # (0.3.13, the user's note 136: "it is just rectangular": the ladder broken under it, no notch round it;
            # 0.3.12 notched it at its forward inboard corner round the hold's ladder going on up through it)
            d.add_box_mesh(grp, "oak", ((hx0 + hx1) / 2, y - t / 2, zc), (length - 0.02, t, half - 0.02))
            for bxx in (hx0 + 0.35, hx1 - 0.35):                          # battens underneath
                d.add_box_mesh(grp, "timber", (bxx, y - t - 0.03, zc), (0.1, 0.06, half - 0.1))
            d.add_cylinder(grp, "iron", ((hx0 + hx1) / 2, y + 0.005, s * 0.2), ((hx0 + hx1) / 2, y + 0.02, s * 0.2),
                           0.09, 0.09, sides=10, smooth=False)            # lifting ring
        # hinges on the coaming edge
        for hx in (hx0 + 0.3, hx1 - 0.3):
            d.add_box_mesh(grp, "iron", (hx, y + 0.02, s * (half - 0.12)), (0.12, 0.03, 0.26))

    for s, side in ((1, "s"), (-1, "p")):
        code = f"hatch-waist-{side}"
        leaf(f"part:{code}", f"glass:{code}", MAIN + 0.5, s, True)
        d.moving_parts.append({"code": code, "axisPoint": [hx0, MAIN + 0.5, s * half], "axisDir": [1.0, 0.0, 0.0],
                               "source": "open:hatch-waist", "angle": 100.0 * s})
        code = f"hatch-hold-{side}"
        leaf(f"part:{code}", None, LOWER + 0.25, s, False)
        d.moving_parts.append({"code": code, "axisPoint": [hx0, LOWER + 0.25, s * half], "axisDir": [1.0, 0.0, 0.0],
                               "source": "open:hatch-hold", "angle": 100.0 * s, "interior": True})

    if GRID:
        # (0.3.10, the hatches 4 long, x 5..9: the main and lower decks' beams at 6 and 8 cut by them end on a carling
        # along each side of the opening under the deck, a header across each end)
        for yb_, dep in ((MAIN - DECK_T, BEAM_D), (LOWER - DECK_T, HOLD_BEAM_D)):
            with d.paint("beam" if GRID else "ceiling"):
                for zc in (hz0 - 0.06, hz1 + 0.06):
                    d.add_box_mesh("auto", "timber", ((hx0 + hx1) / 2, yb_ - dep / 2 + 0.02, zc),
                                   (hx1 - hx0 + 0.25, dep - 0.04, 0.12))
                for xc in (hx0 - 0.125, hx1 + 0.125):
                    d.add_box_mesh("auto", "timber", (xc, yb_ - dep / 2, (hz0 + hz1) / 2), (0.25, dep, hz1 - hz0 + 0.22))
    # the frame round the waist hatch's coaming top, and its hinge cleats
    for s in (1, -1):
        # (0.3.10, clean surfaces: on the coaming's top, 0.01 past its faces each side, not 5 mm down their sides)
        d.add_box_mesh("exterior", "timber", ((hx0 + hx1) / 2, MAIN + 0.515, s * (half + 0.125)), (length + 0.52, 0.03, 0.27))


# ==================================================================== the companionway
def companion_rail(d):
    """
    (0.3.8, the agreed redesign) Round the companion stair's opening in the great cabin's deck: its beams cut by it end
    on a carling along each side of it under the deck, a header across its after end; on the deck a railing like the
    castle fronts' (a plinth, turned balusters, a squared handrail and its cap, newel posts at its ends) along its
    inboard side, from COMPANION_HEAD aft of the front wall (the way onto the stair's head) to its after end, and across
    that to the side. The stair itself (treads, stringers, its handrail on its outboard side) is castle_stairs'.
    """
    cx0, cx1, cz0, cz1 = COMPANION
    yb = MAIN - DECK_T
    with d.paint("beam" if GRID else "ceiling"):
      if not COMPANION_WAIST:      # ((0.3.29) the waist companion: no framing of its own (its beam a segment))
          if not any(abs(bx - (cx0 - 0.25)) < 1e-6 or abs(bx + 0.125 - cx0) < 1e-6 for bx in main_beams()):
              # (else a beam is its header; (0.3.18) carrack-m's well ends on the beams' middles, the beams whole)
              d.add_box_mesh("auto", "timber", (cx0 - 0.125, yb - BEAM_D / 2, (cz0 + cz1) / 2),
                             (0.25, BEAM_D, cz1 - cz0 + 0.22))
          # ((0.3.12) to the middle of the beam at -4 under the landing, the opening's forward end at its after face)
          xe_ = COMPANION_TOP if STAIRWELL else cx1
          xs_ = cx0 - 0.125
          if MODULAR:
              # ((0.3.20, step B) between the two whole beams' faces, not over them: a deck cell beside the well taken up
              # would show the two tops in one plane)
              # ((0.3.23) the well 3 x 1, a header across each end outside it (the beam at -4 over its forward end cut
              # onto the carlings, as the one at -6 across it: grid_framing): the carlings between the headers' faces)
              xs_, xe_ = cx0, cx1
              # (its ends against the cut beam's ends)
              d.add_box_mesh("auto", "timber", (cx1 + 0.125, yb - BEAM_D / 2, (cz0 + cz1) / 2),
                             (0.25, BEAM_D, cz1 - cz0 + 0.24))
          for zc in (cz0 - 0.06, cz1 + 0.06):
              # (0.3.10: from the header's middle, not its after face)
              d.add_box_mesh("auto", "timber", ((xs_ + xe_) / 2, yb - BEAM_D / 2 + 0.02, zc),
                             (xe_ - xs_, BEAM_D - 0.04, 0.12))
    if not STAIRWELL and not COMPANION_WAIST:
        # ((0.3.12) walled in, the stairwell has no railing round it in the great cabin: its walls, build())
        zr = COMP_IN - COMP_S * 0.12            # the inboard rail's middle, on the deck beside the opening
        xr = cx0 - 0.12                         # the after rail's
        side = COMP_S * (frame_face(xr, MAIN + 0.5) - 0.02)
        head = cx1 - COMPANION_HEAD
        runs = [((xr, zr), (head, zr)), ((xr, side), (xr, zr))]
        well_railing(d, MAIN, runs, ((head, zr), (xr, zr)))
    if HOLD_STAIR:
        # (0.3.10) the hold stair's on the lower deck: along its inboard side from its head (aft) to its forward end,
        # and across that to the side; and its opening's carlings and forward header under the lower deck
        hx0s, hx1s, hz0s, hz1s = HOLD_STAIR
        zr2, xr2 = hz0s - 0.12, hx1s + 0.12
        side2 = frame_face(xr2, LOWER + 0.5) - 0.02
        # ((0.3.12) a newel at its after end too, at the stair's head: with the companion's foot a block further aft
        # its rails' ends there are seen) ((0.3.16) carrack-m: railing pieces)
        if not MODULAR:
          well_railing(d, LOWER, [((hx0s - 0.125, zr2), (xr2, zr2)), ((xr2, zr2), (xr2, side2))],
                     ((xr2, zr2), (hx0s - 0.125, zr2)))
        yb2 = LOWER - DECK_T
        with d.paint("beam" if GRID else "ceiling"):
            # (from the beam at -6's middle to the beam at -2's, its forward header)
            xa_, xb_ = hx0s - 0.125, hx1s + 0.25
            beams_ = [x0 + 0.125 for x0 in lower_beams()]
            if GRID:
                # (0.3.13, note 128: a block further aft, no beam at either end: a header of its own across each, the
                # band's full depth, from the outer face of one carling to the other's; the carlings to their middles)
                xb_ = hx1s + 0.125
                for xh in (hx0s - 0.125, hx1s + 0.125):
                    # ((0.3.23) carrack-m: the beam at -6 over its after end cut (grid_framing): a header aft of it)
                    if MODULAR or not any(abs(xh - b_) < 0.2 for b_ in beams_):
                        # ((0.3.23) the after one's ends against the cut beam's ends there)
                        cut_beam = MODULAR and any(abs(xh + 0.125 - b_) < 1e-6 for b_ in beams_)
                        d.add_box_mesh("auto", "timber", (xh, yb2 - HOLD_BEAM_D / 2, (hz0s + hz1s) / 2),
                                       (0.25, HOLD_BEAM_D, hz1s - hz0s + (0.24 if cut_beam else 0.23)))   # (its ends 0.005 in the carlings)
            if MODULAR:
                # ((0.3.20, step B) between the beams' faces at the well's ends (as the companion's); (0.3.23) between
                # its headers' faces)
                xa_, xb_ = hx0s, hx1s
            for zc in (hz0s - 0.06, hz1s + 0.06):
                d.add_box_mesh("auto", "timber", ((xa_ + xb_) / 2, yb2 - HOLD_BEAM_D / 2 + 0.02, zc),
                               (xb_ - xa_, HOLD_BEAM_D - 0.04, 0.12))


def well_railing(d, y0, runs, newels):
    """(0.3.10) A railing round a stairwell on the deck at y0, like the castle fronts': along each run ((xa, za), (xb, zb))
    a plinth, turned balusters, a handrail and its cap; a newel post at each of `newels`."""
    MAIN = y0
    for (xa, za), (xb, zb) in runs:
        c = np.array([(xa + xb) / 2, 0.0, (za + zb) / 2])
        along = np.array([xb - xa, 0.0, zb - za])
        L = float(np.linalg.norm(along))
        u = along / L
        R = np.column_stack([u, [0, 1.0, 0], np.cross(u, [0, 1.0, 0])])
        d.add_box_mesh("auto", "timber", c + np.array([0, MAIN + 0.08, 0]), (L, 0.16, 0.24), R)       # plinth
        d.add_box_mesh("auto", "timber", c + np.array([0, MAIN + 0.93, 0]), (L, 0.1, 0.2), R)         # handrail
        d.add_box_mesh("auto", "oak", c + np.array([0, MAIN + 1.0, 0]), (L, 0.05, 0.26), R)           # its cap
        n = max(1, int(round((L - 0.3) / 0.32)))
        for k in range(n + 1):
            p = np.array([xa, 0, za]) + u * (0.15 + (L - 0.3) * k / n)
            lathe(d, "auto", "timber", (p[0], MAIN + 0.16, p[2]), 0.72, BALUSTER, 8)
    for x, z in newels:                                  # newel posts at its head end and its corner
        d.add_box_mesh("auto", "timber", (x, MAIN + 0.55, z), (0.22, 1.1, 0.22))
        d.add_box_mesh("auto", "oak", (x, MAIN + 1.13, z), (0.28, 0.06, 0.28))


def companion_ends():
    """Where the companionway's ladder stands on the lower deck (its foot) and meets the waist (its head), along x."""
    cx0, cx1 = COMPANION[:2]
    if COMPANION_DOWN < 0:
        return cx0 + 0.38, cx1 - 0.06
    return cx1 - 0.38, cx0 + 0.06


def companionway(d):
    """
    A steep ship's ladder from the waist down to the lower deck, with handrails, and a hood over its foot's end: in the
    first carrack against the quarterdeck's front on the port side, going down aft; in the carrack since 0.3.4 on the
    centreline forward of the mainmast, going down toward the bow, the hood's forward end closed.
    """
    cx0, cx1, cz0, cz1 = COMPANION
    czm = (cz0 + cz1) / 2
    dn = COMPANION_DOWN
    foot_x, head_x = companion_ends()
    foot = np.array([foot_x, LOWER, 0.0])
    head = np.array([head_x, MAIN + 0.02, 0.0])
    for zs in (cz0 + 0.09, cz1 - 0.09):                  # stringers
        a, b = foot + np.array([0, 0, zs]), head + np.array([0, 0, zs])
        d.add_beam_mesh("auto", "oak", a, b, 0.07, 0.22, up=(1, 0, 0))
    for k in range(9):                                   # treads
        y = LOWER + 0.33 * (k + 1)
        f = (y - foot[1]) / (head[1] - foot[1])
        p = foot + (head - foot) * f
        d.add_box_mesh("auto", "oak", (p[0] + 0.03 * dn, y, czm), (0.24, 0.05, cz1 - cz0 - 0.2))
    if COMPANION_HOUSE:
        companion_house(d, foot, head)
        return
    # handrails, from stanchions at the head down the ladder
    for zs in (cz0 + 0.04, cz1 - 0.04):
        stan = np.array([head_x - 0.18 * dn, MAIN + 0.25, zs])
        d.add_cylinder("exterior", "iron", stan, stan + np.array([0, 0.75, 0]), 0.025, 0.025, sides=6)
        a = foot + np.array([0.12 * dn, 0.95, zs])
        b = head + np.array([-0.18 * dn, 0.95, zs])
        rope_path(d, [a, b], r=0.03, group="auto")
        rope_path(d, [b, stan + np.array([0, 0.72, 0])], r=0.03)
    # the hood over the foot's end: planked sides, a rounded roof, open toward the head
    h0, h1 = MAIN + 0.25, MAIN + 1.0
    xs0, xs1 = (cx0, HOOD_X0) if dn < 0 else (HOOD_X0, cx1)
    for zw in (cz0 - 0.125, cz1 + 0.125):
        d.add_box_mesh("exterior", "oak", ((xs0 + xs1) / 2, (h0 + h1) / 2, zw), (xs1 - xs0, h1 - h0, 0.2))
    n_arc = 6
    span = (cz1 - cz0) / 2 + 0.25
    for k in range(n_arc):
        a0 = math.pi * k / n_arc
        a1 = math.pi * (k + 1) / n_arc
        p0 = np.array([0, h1 + 0.25 * math.sin(a0), czm - span * math.cos(a0)])
        p1 = np.array([0, h1 + 0.25 * math.sin(a1), czm - span * math.cos(a1)])
        c = (p0 + p1) / 2
        u = (p1 - p0) / np.linalg.norm(p1 - p0)
        nrm = np.array([0, u[2], -u[1]])
        d.add_box_mesh("exterior", "oak", ((xs0 + xs1) / 2 + 0.02, c[1], c[2]), (xs1 - xs0 + 0.04, 0.06,
                       np.linalg.norm(p1 - p0) + 0.02), np.column_stack([[1.0, 0, 0], nrm, u]))
    open_x = xs1 - 0.03 if dn < 0 else xs0 + 0.03                   # a header over the open end
    d.add_box_mesh("exterior", "timber", (open_x, h1 + 0.12, czm), (0.07, 0.24, cz1 - cz0 + 0.5))
    if dn > 0:
        # its forward end planked up under the roof, on the coaming
        d.add_box_mesh("exterior", "oak", (xs1 + 0.1, (h0 + h1 + 0.25) / 2, czm), (0.2, h1 + 0.25 - h0, cz1 - cz0 + 0.5))


def companion_house(d, foot, head):
    """
    (0.3.7) The companion house over the companionway: oak walls on its coaming (voxels: build), tall enough to go in
    upright, its door in the after end (a vanilla door, DOORS; it swings out aft onto the deck); posts at its corners, a
    moulded cap round its top, a roof of planks a little cambered across, over it a little short of the boat gallows'
    beam; inside, the ladder's handrails, ropes from the foot up to eyebolts on its walls.
    """
    cx0, cx1, cz0, cz1 = COMPANION
    x0, x1, z0, z1 = cx0 - 0.25, cx1 + 0.25, cz0 - 0.25, cz1 + 0.25
    yb = MAIN + 0.25
    for xc in (x0, x1):                                   # corner posts
        for zc in (z0, z1):
            d.add_box_mesh("exterior", "timber", (xc, (yb + HOUSE_TOP) / 2, zc), (0.14, HOUSE_TOP - yb, 0.14))
    # the cap round its top
    d.add_box_mesh("exterior", "timber", ((x0 + x1) / 2, HOUSE_TOP + 0.03, z0), (x1 - x0 + 0.16, 0.06, 0.16))
    d.add_box_mesh("exterior", "timber", ((x0 + x1) / 2, HOUSE_TOP + 0.03, z1), (x1 - x0 + 0.16, 0.06, 0.16))
    d.add_box_mesh("exterior", "timber", (x0, HOUSE_TOP + 0.03, (z0 + z1) / 2), (0.16, 0.06, z1 - z0 - 0.16))
    d.add_box_mesh("exterior", "timber", (x1, HOUSE_TOP + 0.03, (z0 + z1) / 2), (0.16, 0.06, z1 - z0 - 0.16))
    # the roof: planks across, cambered (its ridge 0.04 over its edges), overhanging the cap a little
    half = (z1 - z0) / 2 + 0.12
    rise = 0.04
    n = 4
    for k in range(n):
        za, zb = -half + 2 * half * k / n, -half + 2 * half * (k + 1) / n
        ya = HOUSE_TOP + 0.06 + rise * (1 - (za / half) ** 2)
        yb_ = HOUSE_TOP + 0.06 + rise * (1 - (zb / half) ** 2)
        c = np.array([(x0 + x1) / 2, (ya + yb_) / 2 + 0.025, (za + zb) / 2])
        u = np.array([0.0, yb_ - ya, zb - za])
        u /= np.linalg.norm(u)
        nrm = np.array([0.0, u[2], -u[1]])
        if nrm[1] < 0:
            nrm = -nrm
        d.add_box_mesh("exterior", "oak", c, (x1 - x0 + 0.28, 0.05, np.hypot(zb - za, yb_ - ya) + 0.01),
                       np.column_stack([[1.0, 0, 0], nrm, u]))
    # inside: the handrails, from the ladder's foot up to eyebolts on the side walls by its head
    for zs, s in ((cz0 + 0.04, -1), (cz1 - 0.04, 1)):
        a = foot + np.array([0.12, 0.95, zs])
        b = np.array([head[0] + 0.35, MAIN + 1.05, s * (cz1 - 0.02)])
        rope_path(d, [a, b], r=0.03, group="auto")
        d.add_cylinder("auto", "iron", b - np.array([0, 0, s * 0.03]), b + np.array([0, 0, s * 0.03]), 0.035, 0.035,
                       sides=6)


# ==================================================================== the hold: an empty cargo deck
def hold(d):
    """
    The hold is walkable: a cargo deck over the ballast (HOLD_FLOOR) with the keelson down its middle, the inside
    planking carried down to it, and a ladder from the keelson up through the hold hatch to the lower deck. Nothing is
    in it yet. Its frames are part of the whole hull's (framing).
    """
    if GRID:
        if not MODULAR:             # ((0.3.23, note 173) carrack-m: no hatch ladders)
            hold_ladder(d)
        return
    # the ladder from the keelson up through the hold hatch
    hx0 = HATCH[0]
    foot = np.array([hx0 + 1.5, KEELSON_TOP, 0.0])
    head = np.array([hx0 + 0.08, LOWER + 0.02, 0.0])
    for zs in (-0.42, 0.42):
        d.add_beam_mesh("auto", "oak", foot + np.array([0, 0, zs]), head + np.array([0, 0, zs]), 0.07, 0.2, up=(1, 0, 0))
        d.add_box_mesh("auto", "iron", head + np.array([-0.04, 0.02, zs]), (0.1, 0.05, 0.05))
    for k in range(6):
        y = KEELSON_TOP + 0.33 * (k + 1)
        f = (y - foot[1]) / (head[1] - foot[1])
        p = foot + (head - foot) * f
        d.add_box_mesh("auto", "oak", (p[0] + 0.03, y, 0.0), (0.24, 0.05, 0.8))


def hold_ladder(d):
    """
    (0.3.12, the user's note 110: "remove stairs in cargo hatch and replace with straight up ladder to the glass hatch")
    A plain upright ladder at the hatches' forward end, from the cargo deck through the hold hatch's opening to just under
    the glazed waist hatch's leaves: two sides (their feet 0.01 into the cargo deck, their heads 0.08 under the leaves'
    underside on the coaming), round rungs every 0.3 (the first 0.3 up), iron straps from its sides to the headers under
    the lower deck and the main deck. (0.3.13, the user's note 136) in two, broken where the hold hatch shuts (no notch
    round it now): the ladders "holdladder" and "hatchladder" (ladders()).
    """
    xc = HOLD_LADDER_X
    # (0.3.13, the user's note 136) two ladders, a break between them: the lower one's heads 0.1 under the hold hatch's
    # leaves (their battens' undersides), the upper one's feet 1.0 over the lower deck, clear of the leaves' inner edges
    # as they swing up about their hinges (1.0 out from the ladder's sides, 1.23 broad: up to 0.72 over the hinge there)
    spans = ((HOLD_FLOOR - 0.01, LOWER + 0.05, LOWER - DECK_T, HOLD_BEAM_D),
             (LOWER + 1.0, MAIN + 0.42, MAIN - DECK_T, BEAM_D))
    if MODULAR:
        # ((0.3.20, step B) the hatches flush with the decks now, pieces 1/8 thick: each ladder's heads 0.02 under a shut
        # one's leaves; their straps to the hatchways' forward headers (x 9.0), in the headers' depth (0.5))
        spans = ((HOLD_FLOOR - 0.01, LOWER - 0.145, LOWER - DECK_T, 0.5),
                 (LOWER + 1.0, MAIN - 0.145, MAIN - DECK_T, BEAM_D))
    for y0, y1, yb_, dep in spans:
        for zs in (-0.25, 0.25):
            d.add_box_mesh("auto", "oak", (xc, (y0 + y1) / 2, zs), (0.12, y1 - y0, 0.07))
            # its straps: from its forward face 0.01 into the header's after face, in its deck's band
            if MODULAR:
                xa_, xb_ = xc + 0.05, OPENING[1] + 0.01
                d.add_box_mesh("auto", "iron", ((xa_ + xb_) / 2, yb_ - dep / 2, zs), (xb_ - xa_, 0.06, 0.09))
            else:
                d.add_box_mesh("auto", "iron", (HATCH[1] - 0.04, yb_ - dep / 2, zs), (0.1, 0.06, 0.09))
        # round rungs every 0.3, the first 0.3 up from its foot's level (the cargo deck, the shut hatch's top)
        base = HOLD_FLOOR if y0 < LOWER else LOWER + (0.0 if MODULAR else 0.25)
        for k in range(1, 40):
            y = base + 0.3 * k
            if y > y1 - 0.1:
                break
            if y < y0 + 0.1:
                continue
            # (into the sides 0.01 at each end)
            d.add_cylinder("auto", "oak", (xc, y, -0.225), (xc, y, 0.225), 0.025, 0.025, sides=6, caps=False)


# ==================================================================== decks and walls meet the inside planking
CLIPPED = ("deck", "oak", "timber")


def _ceil_grid(d, ys):
    """
    The ceiling's least half-width over each cell's x range at each height in ys: [len(ys), nx]. Only where the
    ceiling is: a cell reaching a little past its after end (the inside of the transom: it's planked 0.3 thick) or its
    forward end isn't taken as zero there. (Until 0.3.7 it was, so the whole row of cells along the transom's inside
    was cleared off the poop deck, a slot between the deck and the transom: the user's note 13.)
    """
    xs = d.xs
    out = []
    for y in ys:
        xo, _ = _ceil_ring(y)
        if len(xo) < 2:
            out.append(np.zeros_like(xs))
            continue
        x0, x1 = float(xo[0]), float(xo[-1])
        samples = []
        for dx in (-0.125, 0.0, 0.125):
            x = xs + dx
            samples.append(np.where((x >= x0 - 1e-6) & (x <= x1 + 1e-6), ceil_z(x, y), np.inf))
        c = np.minimum(np.minimum(samples[0], samples[1]), samples[2])
        out.append(np.where(np.isinf(c), 0.0, c))
    return np.array(out)


def clip_to_ceiling(d, inside):
    """
    Drawn cells of decks and walls that reach past the inside planking (their square corners would stick out through
    the curved hull, at the waterline, the bow, the castles' leaning sides) become collision only. edge_strips() then
    closes each deck's and wall's edge to the planking with a mesh strip.
    """
    mids = [d.mid(m) for m in CLIPPED]
    nx, ny, nz = d.n
    hidden = d.mid("hidden")
    clipped = 0
    for j in range(ny):
        y = d.ys[j]
        row = d.grid[:, j, :]
        drawn = np.isin(row, mids) & inside[:, j, :]
        if not drawn.any():
            continue
        c = np.minimum(_ceil_grid(d, [y - 0.125])[0], _ceil_grid(d, [y + 0.125])[0])     # [nx]
        reach = np.abs(d.zs)[None, :] + 0.125                                          # [1, nz]
        cut = drawn & (reach > c[:, None] - 0.005)
        row[cut] = hidden
        clipped += int(cut.sum())
    d.clipped_cells = clipped


def _quad_facing(d, group, mat, pts, want, uv=None, keep_uv=False):
    """A quad wound to face `want`, its uv from its size (or given). keep_uv: each corner keeps the uv it was given
    for (turning it round swaps u0 and u1), so the texture lies on it as asked whichever way it faces."""
    pts = [np.asarray(p, float) for p in pts]
    n = np.cross(pts[1] - pts[0], pts[3] - pts[0]) + np.cross(pts[3] - pts[2], pts[1] - pts[2])
    if np.linalg.norm(n) < 1e-10:
        return
    if np.dot(n, want) < 0:
        pts = [pts[1], pts[0], pts[3], pts[2]]
        if keep_uv and uv is not None:
            uv = (uv[2], uv[1], uv[0], uv[3])
    if uv is None:
        w = float(np.linalg.norm(pts[1] - pts[0]))
        h = float(np.linalg.norm(pts[3] - pts[0]))
        uv = (0, 0, min(16, w * 16), min(16, h * 16))
    d.add_quad(group, mat, pts, uv)


LEAD_PITCH = (0.12, 0.17)       # (0.3.13) a lead light's diamond quarry: across, up
# (0.3.14, the user's note 145: "for windows i did not mean small rhombus grid. but about 20 cm grid that used to hold
# glass") every window's lead a rectangular grid of small panes about 0.2 a side (the period's leaded glazing: clear
# glass in small oblong panes, upright and level cames), not 0.3.13's diamonds
LEAD_SQUARE = (0.2, 0.2)


def lead_cames(d, grp, P, nrm, u0, u1, v0, v1, diamond=False, pitch=LEAD_SQUARE, w=0.016, ext=0.006):
    """
    (0.3.13, the user's notes 133 and 135: "all windows could have lead dividers", the glazed hatch's panes "should be
    split ... in lead framing") The lead cames over a pane, as a period lead light's: (diamond) diamond quarries (pitch:
    a diamond's width and height), or ((0.3.14, note 145) the default) small upright oblongs about the pitch. P(u, v) is the point on the
    glass at (u, v) of the pane's own two directions, nrm its normal; (u0, u1, v0, v1) the glass that shows (inside its
    frame), the cames running ext on into the frame round it. Each came a flat strip w wide either side of the glass,
    0.008 off it (one way of the diamonds, or the uprights) and 0.012 (the other way): where they cross, not in one
    plane; its two faces only (seen on edge it's as thin as the glass).
    """
    pu, pv = pitch
    uc, vc = (u0 + u1) / 2, (v0 + v1) / 2
    lo_u, hi_u, lo_v, hi_v = u0 - ext - uc, u1 + ext - uc, v0 - ext - vc, v1 + ext - vc
    segs = []
    if diamond:
        for fam, sg in ((0, 1.0), (1, -1.0)):
            a, b = 1.0 / pu, sg / pv
            kmax = int((u1 - u0) / 2 / pu + (v1 - v0) / 2 / pv) + 2
            for k in range(-kmax, kmax + 1):
                c = k + 0.5                                      # (a quarry's middle in the pane's middle)
                n2 = a * a + b * b
                p0 = np.array([a * c / n2, b * c / n2])
                t_ = np.array([-b, a]) / math.sqrt(n2)
                t0, t1 = -1e9, 1e9
                for i_, (lo, hi) in enumerate(((lo_u, hi_u), (lo_v, hi_v))):
                    if abs(t_[i_]) < 1e-12:
                        if not lo <= p0[i_] <= hi:
                            t0, t1 = 1.0, 0.0
                        continue
                    ta, tb = sorted(((lo - p0[i_]) / t_[i_], (hi - p0[i_]) / t_[i_]))
                    t0, t1 = max(t0, ta), min(t1, tb)
                if t1 - t0 > 0.03:
                    segs.append((fam, p0 + t_ * t0, p0 + t_ * t1))
    else:
        # ((0.3.14, note 145) how many panes across and up: the count whose panes come nearest the pitch by ratio, so a
        # pane up to about 1.4 times the pitch stays whole: a cabin front's rows, 0.22 to 0.28 between the transoms, are
        # the grid's rows themselves (its uprights the cames, 0.18 apart), as a side light's 0.26 columns beside its
        # mullion (its rows the cames, about 0.2 apart); the stern gallery's panes, bigger, in a grid of cames both ways)
        def count(span, p_):
            n_ = max(1, int(span / p_))
            return min((n_, n_ + 1), key=lambda k_: abs(math.log(span / k_ / p_)))

        nu = count(u1 - u0, pu)
        nv = count(v1 - v0, pv)
        for i_ in range(1, nu):
            u = u0 + (u1 - u0) * i_ / nu - uc
            segs.append((0, np.array([u, lo_v]), np.array([u, hi_v])))
        for i_ in range(1, nv):
            v = v0 + (v1 - v0) * i_ / nv - vc
            segs.append((1, np.array([lo_u, v]), np.array([hi_u, v])))
    nrm = np.asarray(nrm, float)
    for fam, a2, b2 in segs:
        A, B = np.asarray(P(uc + a2[0], vc + a2[1]), float), np.asarray(P(uc + b2[0], vc + b2[1]), float)
        side = np.cross(nrm, B - A)
        side = side / np.linalg.norm(side) * w / 2
        L = float(np.linalg.norm(B - A))
        for sg in (1.0, -1.0):
            off = nrm * sg * (0.008 if fam == 0 else 0.012)
            _quad_facing(d, grp, "lead", [A - side + off, B - side + off, B + side + off, A + side + off], nrm * sg,
                         (0, 0, min(16.0, L * 16), w * 16))


def edge_strips(d):
    """
    Each deck's edge (top and underside) and each castle wall's ends, from the last drawn cell out to the inside
    planking (a hair into it), so there's no gap and no square corner: the planking hides the rest.
    """
    deck = d.mid("deck")
    oak = d.mid("oak")
    tops = sorted(set(d.deck_tops))
    for top in tops:
        j = int(round((top - 0.125 - d.o[1]) / 0.25 - 0.5))
        if not (0 <= j < d.n[1]):
            continue
        row = d.grid[:, j, :]
        spans = [(a, b) for t, a, b in d.deck_spans if abs(t - top) < 1e-6]
        for i in range(d.n[0]):
            x = d.xs[i]
            ks = np.nonzero((row[i] == deck) | (row[i] == oak))[0]    # ((0.3.12) over a castle front: its boards)
            g_ = getattr(d, "grating", None)
            if g_ and abs(top - QUARTER) < 1e-6 and g_[0] < x < g_[1]:
                # (0.3.10: the grating's cells count as the deck's: its edge strip starts outboard of it)
                ks = np.union1d(ks, np.nonzero((np.abs(d.zs) > 0) & (d.zs > g_[2]) & (d.zs < g_[3]))[0])
            xa, xb = x - 0.125, x + 0.125
            if len(ks) == 0:
                # (0.3.7) a column the deck covers whose cells are all clipped: the narrow end of the hull at the bow
                # (the lower deck's and the forecastle deck's tips, the user's notes 22 and 26). The deck is carried
                # on out to the inside planking from the centreline, both sides meeting there.
                c_tip = max(ceil_z(xa, top), ceil_z(xb, top))
                # (0.3.10, the user's note 75: "here some hole from inside", the hold's floor at the bow: its cells
                # there clipped by the bottom's rise under them, 0.5 to 0.6 from the planking at its top, more than the
                # tips' 0.3: a hole x 15.5..16 across the middle) wider too where its middle cells are clipped (not an
                # opening in the deck)
                clipped_mid = bool((row[i][np.abs(d.zs) < 0.25] == d.mid("hidden")).all())
                if not any(a <= x < b for a, b in spans) or c_tip < 0.03 or (c_tip > 0.3 and not clipped_mid):
                    continue
                ends = {1: 0.0, -1: 0.0}
            else:
                ends = {}
                for s in (1, -1):
                    side = ks[d.zs[ks] * s > 0]
                    if len(side):
                        ends[s] = float(np.max(np.abs(d.zs[side]))) + 0.125
            for s, zv in ends.items():
                for y, yc, want in ((top, top + 0.01, (0, 1, 0)), (top - DECK_T, top - DECK_T - 0.01, (0, -1, 0))):
                    ca, cb = ceil_z(xa, yc) + 0.02, ceil_z(xb, yc) + 0.02
                    if max(ca, cb) <= zv + 0.004:
                        continue
                    ca, cb = max(ca, zv), max(cb, zv)
                    # (0.3.7) the planks across it as the deck's cells have them (v from z within the cell's block, u
                    # from x), so its seams run on from the deck's; and square to the deck's planks, in narrow pieces
                    # where the planking curves in fast (each out to the further of its ends, a hair into the planking).
                    # Until then each strip had its seams from its own inner edge (a caulked seam all along the deck's
                    # edge that looked like a gap: the user's note 13) and slanted with the curve where the bow and
                    # stern close in (stepped, skewed planks: note 28)
                    blk = math.floor(zv + 1e-6) if s > 0 else math.floor(-(zv + 0.25) + 1e-6)   # the cell's block
                    v_in = float(np.clip((s * zv - blk) * 16, 0, 16))
                    if zv == 0.0:
                        # (a hull's tip: the deck's seam on the centreline would fill the narrow strip: a plank's middle,
                        # v 1.5 to 5 from the centreline out each side)
                        blk, v_in = (-1.5 / 16, 1.5) if s > 0 else (-5.0 / 16, 5.0)
                    u_base = math.floor(xa + 1e-6)
                    n_pc = max(1, min(4, int(math.ceil(abs(cb - ca) / 0.04))))
                    for k in range(n_pc):
                        x0, x1 = xa + (xb - xa) * k / n_pc, xa + (xb - xa) * (k + 1) / n_pc
                        c = max(ca + (cb - ca) * k / n_pc, ca + (cb - ca) * (k + 1) / n_pc)
                        v_out = float(np.clip((s * c - blk) * 16, 0, 16))
                        # (2 mm off the deck's face, into the space it faces: so it's lit as that space is, a room's
                        # inside or the open deck; exactly on it, it took the deck's own cell's: note 15's light strip)
                        yy = y + 0.002 * want[1]
                        d.paint_tag = "ceiling" if want[1] < 0 else None
                        pts = [(x0, yy, s * zv), (x1, yy, s * zv), (x1, yy, s * c), (x0, yy, s * c)]
                        _quad_facing(d, "auto", "deck", pts, want, ((x0 - u_base) * 16, v_out, (x1 - u_base) * 16, v_in),
                                     keep_uv=True)
    # the castle walls' ends, and (0.3.8) the end of the castle's deck over each, on the waist's side: its cells there
    # stop short of the planking like the wall's, and nothing closed the deck's end between them, a notch in the corner
    # where the castle break's cap meets the wall (the user's note 43, "holes here")
    # ((0.3.12) and the stairwell's after wall in the great cabin, at its outboard end: no deck's end over it)
    walls_ = [(x0, x1, y0, y1, True) for x0, x1, y0, y1 in d.bulkheads]
    walls_ += [(p[0], p[1], MAIN, QUARTER - DECK_T, False) for p in getattr(d, "partitions", []) if p[1] - p[0] < 0.3]
    # ((0.3.23, notes 167/168) carrack-m: no breast beams under the castle decks' edges: the decks' own ends there, their
    # planks' ends (deck cells))
    walls_ += [(x0, x1, y1, y1, "deck") for x0, x1, y0, y1 in getattr(d, "front_ends", [])]
    for x0, x1, y0, y1, with_deck in walls_:
        i = int(round(((x0 + x1) / 2 - d.o[0]) / 0.25 - 0.5))
        waist = (-1, 0, 0) if x0 >= WAIST_FWD - 1e-6 else (1, 0, 0)
        for j in range(d.n[1]):
            y = d.ys[j]
            in_deck = y1 < y < y1 + DECK_T and with_deck
            if not (y0 < y < y1 or in_deck):
                continue
            ks = np.nonzero(d.grid[i, j, :] == (deck if in_deck and (not GRID or with_deck == "deck") else oak))[0]
            if len(ks) == 0:
                continue
            ya, yb = y - 0.125, y + 0.125
            if not in_deck and with_deck:
                # (0.3.10, the user's note 71: "door blocked by texture") a doorway (empty cells within the inside
                # planking) outboard of the wall's last drawn cell: the strip starts at its far side, not across it.
                # (The companion's door at z -4.5..-3.5: where the cell outboard of it was clipped, near its head, the
                # strip ran from its inboard jamb out over the doorway)
                c_row = min(ceil_z(x0 - 0.01, ya), ceil_z(x1 + 0.01, ya), ceil_z(x0 - 0.01, yb), ceil_z(x1 + 0.01, yb))
                gap = (d.grid[i, j, :] == 0) & (np.abs(d.zs) + 0.125 <= c_row - 0.005)
                if not MODULAR:     # ((0.3.16) carrack-m: no doorways in its fronts' cells, only the beam)
                    ks = np.union1d(ks, np.nonzero(gap)[0])
            for s in (1, -1):
                side = ks[d.zs[ks] * s > 0]
                if len(side) == 0:
                    continue
                zv = float(np.max(np.abs(d.zs[side]))) + 0.125
                for xf, xc, want in ((x0, x0 - 0.01, (-1, 0, 0)), (x1, x1 + 0.01, (1, 0, 0))):
                    if in_deck and want != waist:
                        continue
                    ca, cb = ceil_z(xc, ya) + 0.02, ceil_z(xc, yb) + 0.02
                    if max(ca, cb) <= zv + 0.004:
                        continue
                    if GRID and with_deck and want == waist:
                        # (0.3.13, the user's note 129: "Texture misalignment or spill", a dark strip up the forecastle
                        # front's end at the castle break: the break's inside planking (sweep_patch) stands 0.07 further
                        # out than the side's there, and stops 0.05 short of the wall, a slot between them showing the
                        # dark outside planking's back) on the waist's side 0.1 into the side, behind its planking: it
                        # closes the slot
                        ca, cb = ca + 0.08, cb + 0.08
                    ca, cb = max(ca, zv), max(cb, zv)
                    xq = xf + 0.002 * want[0]          # (lit as the room it faces: see the decks' strips)
                    d.paint_tag = "wall" if with_deck != "deck" else None
                    pts = [(xq, ya, s * zv), (xq, ya, s * ca), (xq, yb, s * cb), (xq, yb, s * zv)]
                    if in_deck:
                        # (its texture as the deck's end's cells have it: v by the height in the block, u across)
                        fy = math.floor(ya + 1e-6)
                        _quad_facing(d, "auto", "oak" if GRID and with_deck != "deck" else "deck", pts, want,
                                     (0, 16 - (yb - fy) * 16, min(16, (max(ca, cb) - zv) * 16),
                                                                     16 - (ya - fy) * 16))
                    else:
                        _quad_facing(d, "auto", "oak", pts, want)


# ==================================================================== the hull's skeleton
def framing(d):
    """
    The hull's skeleton, seen from inside, like a real ship's:
    - a frame (rib) under every deck beam up both sides, from the cargo deck past every deck to the top of the bulwark
      or the castle rail; each face of it follows the inside planking exactly, so it never sinks into it or pokes
      through it where the hull curves in toward the ends or leans in above (tumblehome)
    - the deck beams on the frames under every deck (the lower deck's are drawn only: the hold needs the headroom),
      their ends buried in the frames, with a hanging knee at each end and a clamp along the side under them
    - the keelson: along the middle of the cargo deck, rising at both ends along the bottom to the stem and the
      sternpost; outside, the stem up the bow and the sternpost behind the rudder
    """
    S, M = FRAME_SIDED, FRAME_MOULDED

    def in_window(x, y):
        return any(x0 - 0.02 < x < x1 + 0.02 and y0 - 0.05 < y < y1 + 0.05 for x0, x1, y0, y1 in SIDE_WINDOWS)

    hawse = getattr(d, "hawse_pieces", [])

    def at_hawse(x, y):
        """Where a hawse piece is (anchor_gear): the frame there stops under it and goes on over it."""
        return any(abs(x - hx) < hw_ + S / 2 + 0.02 and abs(y - hy) < hh_ + 0.02 for hx, hy, hw_, hh_ in hawse)

    def frame_part(xf, s, y_lo, top_a, top_b, width=S, head=True, skip=None, depth=None):
        """
        One side's frame from y_lo to top_a at its after face and top_b at its forward face (width: a broader timber
        on the same lines, a hawse piece). Where the inside planking ends against the transom before its after face,
        it stands that much further forward, hugging the transom's corner (a fashion piece: the last frame was cut off
        there until 0.3.7, the user's note 30). head=False: no face over its top (something carries it on up). depth:
        how far it stands in from the ceiling (FRAME_MOULDED; (GRID) a rider's less).
        """
        M = FRAME_MOULDED if depth is None else depth
        n = max(2, int(math.ceil((max(top_a, top_b) - y_lo) / 0.25)))
        ts = np.linspace(0.0, 1.0, n + 1)
        # (0.3.8) its pieces split at each deck it passes (top and underside), so none shows in two rooms (paint)
        lo_top = min(top_a, top_b)
        cuts_y = sorted({float(y) for y in ts[1:-1] * (lo_top - y_lo) + y_lo} |
                        {float(Y) for t in d.deck_tops for Y in (t, t - DECK_T) if y_lo + 0.02 < Y < lo_top - 0.02})
        cuts_y = [y for k, y in enumerate(cuts_y) if k == 0 or y - cuts_y[k - 1] > 0.02]
        ya = np.array([y_lo] + cuts_y + [top_a])
        yb = np.array([y_lo] + cuts_y + [top_b])
        n = len(ya) - 1

        def aft_face(y):
            xo, _ = _ceil_ring(y)
            start = float(xo[0]) + 0.03 if len(xo) > 1 else -1e9
            a = xf - width / 2
            return start if a < start < a + 0.6 else a

        xas = [aft_face(y) for y in ya]
        xbs = [aft_face(y) + width for y in yb]
        ca = [ceil_z(x, y) for x, y in zip(xas, ya)]
        cb = [ceil_z(x, y) for x, y in zip(xbs, yb)]
        ok = [min(ca[k], ca[k + 1], cb[k], cb[k + 1]) > M + 0.12 and not in_window(xf, (ya[k] + ya[k + 1]) / 2) and
              not (skip and skip(xf, (ya[k] + ya[k + 1]) / 2)) for k in range(n)]

        def P(x, y, c, inner):
            return (x, y, s * (c - (M if inner else 0.0)))

        for k in range(n):
            if not ok[k]:
                continue
            a0o, a0i = P(xas[k], ya[k], ca[k], False), P(xas[k], ya[k], ca[k], True)
            a1o, a1i = P(xas[k + 1], ya[k + 1], ca[k + 1], False), P(xas[k + 1], ya[k + 1], ca[k + 1], True)
            b0o, b0i = P(xbs[k], yb[k], cb[k], False), P(xbs[k], yb[k], cb[k], True)
            b1o, b1i = P(xbs[k + 1], yb[k + 1], cb[k + 1], False), P(xbs[k + 1], yb[k + 1], cb[k + 1], True)
            # the grain (the texture's u) runs up the frame, carried on from one piece to the next
            u0 = (4.0 * k) % 16
            u1 = u0 + min(4.0, (ya[k + 1] - ya[k]) * 16)
            _quad_facing(d, "auto", "timber", [a0i, a1i, b1i, b0i], (0, 0, -s), (u0, 0, u1, min(16, width * 16)))  # inner
            _quad_facing(d, "auto", "timber", [a0o, a1o, a1i, a0i], (-1, 0, 0), (u0, 0, u1, M * 16))     # after face
            _quad_facing(d, "auto", "timber", [b0i, b1i, b1o, b0o], (1, 0, 0), (u0, 0, u1, M * 16))      # forward face
            if k == 0 or not ok[k - 1]:
                _quad_facing(d, "auto", "timber", [a0o, b0o, b0i, a0i], (0, -1, 0))        # foot
            if (k == n - 1 and head) or (k < n - 1 and not ok[k + 1]):
                _quad_facing(d, "auto", "timber", [a1o, b1o, b1i, a1i], (0, 1, 0))         # head

    if GRID:
        grid_framing(d, frame_part, in_window, at_hawse)
        for hx, hy, hw_, hh_ in hawse:
            for s in (1, -1):
                frame_part(hx, s, hy - hh_, hy + hh_, hy + hh_, width=2 * hw_)
        framing_tail(d)
        return

    # ---- frames
    d.paint_tag = "frame"
    gallows = [gx + S / 2 for gx in GALLOWS_X]
    for xf in frame_stations():
        xa, xb = xf - S / 2, xf + S / 2
        if ceil_z(xf, MAIN) < 0.5 and not (POOP_OVERHANG and ceil_z(xf, QUARTER + 0.5) >= 0.5):
            continue
        # (the boat gallows' frames run on up to the bulwark's top, where their posts carry them on: boat_gallows)
        post = any(abs(xf - g) < 1e-6 for g in gallows)
        top_a, top_b = (rail(xa), rail(xb)) if post else (rail(xa) - 0.03, rail(xb) - 0.03)
        y0 = HOLD_FLOOR
        while y0 < min(top_a, top_b) - 0.5 and min(ceil_z(xa, y0), ceil_z(xb, y0)) < M + 0.3:
            y0 += 0.125
        if y0 >= min(top_a, top_b) - 0.5:
            continue
        if POOP_OVERHANG and OH_Y - 1e-6 <= y0 < QUARTER:
            # (0.3.8) a frame of the overhang only, aft of the transom: it stands on the quarterdeck like those over the
            # step (its foot was on the overhang's underside, the two faces flickering seen from below)
            y0 = QUARTER
        parts = [(y0, top_a, top_b)]
        # where the forecastle's sides step out over the bow (FC_Y0) the frame stops under its deck and goes on above
        if abs(ceil_z(xf, FC_Y0 + 0.01) - ceil_z(xf, FC_Y0 - 0.01)) > 0.08 and top_a > QUARTER:
            parts = [(y0, FC_Y0 - 0.01, FC_Y0 - 0.01), (QUARTER, top_a, top_b)]
        # and where the poop's sides step out over the transom (OH_Y), likewise under the quarterdeck
        if (POOP_OVERHANG and y0 < OH_Y and top_a > QUARTER and
                abs(ceil_z(xf, OH_Y + 0.01) - ceil_z(xf, OH_Y - 0.01)) > 0.08):
            parts = [(y0, OH_Y - 0.26, OH_Y - 0.26), (QUARTER, top_a, top_b)]
        for s in (1, -1):
            for lo, ta, tb in parts:
                if min(ta, tb) - lo > 0.3:
                    frame_part(xf, s, lo, ta, tb, head=not (post and ta == top_a), skip=at_hawse)
    # the hawse pieces: broad timbers round the hawse holes, the frame there stopping under and over each (since 0.3.7;
    # the holes were cut through a frame before, the user's note 23)
    for hx, hy, hw_, hh_ in hawse:
        for s in (1, -1):
            frame_part(hx, s, hy - hh_, hy + hh_, hy + hh_, width=2 * hw_)

    # ---- the deck beams on the frames, round the openings in the decks
    d.paint_tag = "ceiling"
    def deck_beam(xc, y_mid, height, cuts):
        half = ceil_z(xc, y_mid)
        if half < 0.6:
            return
        spans = [(-half, half)]
        for c0, c1 in cuts:
            spans = [p for a, b in spans for p in ((a, min(b, c0)), (max(a, c1), b)) if p[1] - p[0] > 0.05]
        for a, b in spans:
            d.add_box_mesh("auto", "timber", (xc, y_mid, (a + b) / 2), (S - 0.02, height, b - a))

    for bx in main_beams():
        cuts = [(o[2], o[3]) for o in (HATCH, COMPANION) if o[0] - 0.25 < bx < o[1]]
        deck_beam(bx + 0.125, MAIN - DECK_T - 0.125, 0.26, cuts)
    for bx in castle_beams():
        deck_beam(bx + 0.125, QUARTER - DECK_T - 0.125, 0.26, [])
    for bx in poop_beams():
        deck_beam(bx + 0.125, POOP - DECK_T - 0.125, 0.26, [])
    # the lower deck's (drawn only), round the masts, the pump and the hold hatch
    obstacles = [(MASTS[m][0], 0.0, 0.55) for m in MASTS] + [(PUMP[0], PUMP[1], 0.22)]
    hx0, hx1, hz0, hz1 = HATCH
    y_beam = LOWER - DECK_T - 0.07
    for x0 in lower_beams():
        xc = x0 + 0.125
        cuts = []
        for ox, oz, r in obstacles:
            if abs(xc - ox) < r:
                w = math.sqrt(r * r - (xc - ox) ** 2)
                cuts.append((oz - w, oz + w))
        if hx0 - 0.25 < xc < hx1 + 0.25:
            cuts.append((hz0 - 0.25, hz1 + 0.25))
        deck_beam(xc, y_beam, 0.14, cuts)

    # ---- hanging knees at the beams' ends, and the clamps under them
    levels = [(LOWER - DECK_T - 0.14, lower_beams(), lambda x: True),
              (MAIN - DECK_T - 0.25, main_beams(), lambda x: True),
              (QUARTER - DECK_T - 0.25, castle_beams(), lambda x: x < WAIST_AFT or x > WAIST_FWD),
              (POOP - DECK_T - 0.25, poop_beams(), lambda x: x < POOP_FRONT)]

    def slope(x, y):
        e = 0.05
        return abs(ceil_z(x + e, y) - ceil_z(x - e, y)) / (2 * e)

    for yb, beams, where in levels:
        for x0 in beams:
            xc = x0 + 0.125
            if not where(xc) or frame_face(xc, yb - 0.3) < 0.6 or slope(xc, yb - 0.2) > 0.9 or in_window(xc, yb - 0.3):
                continue
            for s in (1, -1):
                zf = min(frame_face(xc - 0.1, yb - 0.2), frame_face(xc + 0.1, yb - 0.2)) + 0.02
                quad = [(yb, zf), (yb, zf - 0.34), (yb - 0.24, zf - 0.2), (yb - 0.44, zf)]
                d.add_prism("auto", "timber", [(xc - 0.1, y, s * z) for y, z in quad],
                            [(xc + 0.1, y, s * z) for y, z in quad])
        # the clamp: a long timber along the frames' inner faces the beam ends rest on
        xs = [x0 + 0.125 for x0 in beams if where(x0 + 0.125)]
        if len(xs) < 2:
            continue
        for s in (1, -1):
            run = []
            for x in np.arange(min(xs) - 0.6, max(xs) + 0.61, 0.25):
                ok = (frame_face(x, yb - 0.1) > 0.6 and slope(x, yb - 0.1) < 0.9 and not in_window(x, yb - 0.1)
                      and where(x))
                if ok:
                    run.append(np.array([x, yb - 0.1, s * (frame_face(x, yb - 0.1) - 0.06)]))
                if (not ok or x + 0.25 > max(xs) + 0.61) and len(run) > 1:
                    for a, b in zip(run[:-1], run[1:]):
                        d.add_beam_mesh("auto", "timber", a, b, 0.12, 0.2)
                if not ok:
                    run = []

    framing_tail(d)


def framing_tail(d):
    """The keelson's ends, the timbers on the centreline inside the ends, the stem."""
    # ---- the keelson's ends: from its flat run in the hold along the bottom's rise to the stem and the sternpost
    d.paint_tag = None
    def bottom_y(x):
        """Lowest height at x where the hull is wide enough inside for the keelson."""
        y = HOLD_FLOOR
        while y < LOWER - DECK_T and ceil_z(x, y) < 0.28:
            y += 0.05
        return y

    # (0.3.10, GRID, the user's note 79: none in the hold; the floor's planks cover the keelson)
    for x_start, step in () if GRID else ((d.keelson[1], 0.25), (d.keelson[0], -0.25)):
        # (GRID: from its run sunk flush in the hold's floor)
        pts = [np.array([x_start - (0.25 if step > 0 else -0.25), HOLD_FLOOR + (-0.125 if GRID else 0.125), 0.0])]
        x = x_start
        while True:
            yb = bottom_y(x)
            if yb >= LOWER - DECK_T - 0.4:
                break
            pts.append(np.array([x, yb + 0.15, 0.0]))
            x += step
            if abs(x - x_start) > 8:
                break
        for a, b in zip(pts[:-1], pts[1:]):
            d.add_beam_mesh("auto", "timber", a, b, 0.5, 0.3)

    centreline_timbers(d)

    if GRID:
        keel_and_stem(d)
        return
    # ---- outside: the stem up the bow, from the keel to the forecastle
    ys = np.linspace(KEEL - 0.25, FC_LOW, 26)
    pts = [np.array([float(interp(y, BOW_X)), float(y), 0.0]) for y in ys]
    for a, b in zip(pts[:-1], pts[1:]):
        u = (b - a) / np.linalg.norm(b - a)
        n = np.array([u[1], -u[0], 0.0])
        if n[0] < 0:
            n = -n
        d.add_beam_mesh("exterior", "tarred", a + n * 0.03, b + n * 0.03, 0.3, 0.26, up=(0, 0, 1))


def keel_and_stem(d, stern_r=0.4, n_arc=10):
    """
    (0.3.10, GRID, the user's note 104: "keel is not continuous and has sharp corner in bow") The backbone outside as one
    timber, without a break: the sternpost down the rudder's raked line (its after face on the rudder's axis), round a
    small curve at its heel into the keel along the bottom (0.25 under it, 0.4 sided), round the forefoot (the bottom's
    own curve up into the stem, FOREFOOT_R) and up the stem to the forecastle's raked underside (0.18 out from the bow,
    0.26 sided). Its inner faces are buried in the hull's planking (0.03 under the keel, 0.12 behind the stem).
    """
    ra, axis = d.rudder_axis
    axis2 = np.array([axis[0], axis[1]])
    top = np.array(d.sternpost_top[:2], float)
    y_k = KEEL - 0.11                                   # the keel's middle (top KEEL + 0.03, underside KEEL_BOTTOM)
    if HANSA:
        y_k = KEEL + 0.14 - 0.02                        # (Hansa) flush in her bottom block, its underside 0.02 proud
    s0 = top - axis2 * ((top[1] - y_k) / axis2[1])      # the sternpost's middle line meets the keel's
    d1, d2 = -axis2 / np.linalg.norm(axis2), np.array([1.0, 0.0])
    beta = math.acos(float(np.clip(d1 @ d2, -1, 1)))
    t = stern_r * math.tan(beta / 2)
    a_pt, b_pt = s0 - d1 * t, s0 + d2 * t
    o = b_pt + np.array([0.0, stern_r])
    ang_a = math.atan2(*(a_pt - o)[::-1])
    ang_b = math.atan2(*(b_pt - o)[::-1])
    # (the sternpost 0.12 either side: clear of the rudder head's 0.14 and the blade's 0.15, not in their planes)
    SP, KL, ST = (0.13, 0.12, 0.0), (0.14, 0.2, 0.11), (0.15, 0.13, 0.03)    # (half depth, half width, out) of each
    pts, sizes = [top, a_pt], [SP, SP]
    for k in range(1, n_arc):
        u = k / n_arc
        a = ang_a + (ang_b - ang_a) * u
        pts.append(o + stern_r * np.array([math.cos(a), math.sin(a)]))
        sizes.append(tuple(p + (q - p) * u for p, q in zip(SP, KL)))
    # the keel along the bottom, in pieces of about a block (the grain's texture along each)
    x_ff = float(interp(KEEL, BOW_X))
    for x in np.linspace(b_pt[0], x_ff, max(2, int(math.ceil(x_ff - b_pt[0])) + 1)):
        pts.append(np.array([x, y_k]))
        sizes.append(KL)
    # round the forefoot and up the stem: the bow's own line (BOW_X), offset out from it
    y_arc = max(y for y, _ in BOW_X if y < KEEL + FOREFOOT_R)                  # where the forefoot's arc ends
    arc_ys = [y for y, _ in BOW_X if KEEL < y <= y_arc + 1e-9]
    stem_ys = list(np.linspace(y_arc, FC_LOW, int(math.ceil((FC_LOW - y_arc) / 0.5)) + 1))[1:]
    prof = [np.array([x_ff, KEEL])] + [np.array([float(interp(y, BOW_X)), y]) for y in arc_ys + stem_ys]
    for k in range(1, len(prof)):
        a, b = prof[k - 1], prof[min(k + 1, len(prof) - 1)]
        tg = (b - a) / np.linalg.norm(b - a)
        n = np.array([tg[1], -tg[0]])                  # outward: forward and down
        u = min(1.0, (prof[k][1] - KEEL) / max(1e-6, y_arc - KEEL))
        sz = tuple(p + (q - p) * u for p, q in zip(KL, ST))
        pts.append(prof[k] + n * sz[2])
        sizes.append(sz)
    sections = []
    for k, (c, (h, w, _)) in enumerate(zip(pts, sizes)):
        a, b = pts[max(k - 1, 0)], pts[min(k + 1, len(pts) - 1)]
        tg = (b - a) / np.linalg.norm(b - a)
        n = np.array([tg[1], -tg[0]])
        p_lo, p_hi = c - n * h, c + n * h
        sections.append([(p_lo[0], p_lo[1], -w), (p_hi[0], p_hi[1], -w), (p_hi[0], p_hi[1], w), (p_lo[0], p_lo[1], w)])
    ruled_timber(d, "exterior", "tarred", sections)
    d.backbone = pts


# ==================================================================== (GRID) the storeys' structure
PANELLED =("greatcabin", "poopcabin")      # the rooms panelled flush (stiles and rails); the rest plain planking
if GRID:
    PANELLED = ()       # (0.3.12, the user's note 126: no panelling for now; grid_framing)


def grid_rooms(x):
    """(GRID) The rooms over each other at x: (code, its floor, the deck over it)."""
    out = [("hold", HOLD_FLOOR, LOWER), ("lowerdeck", LOWER, MAIN)]
    if x < FRONT_AFT - 1e-6:
        out.append(("greatcabin", MAIN, QUARTER))
    elif x > FRONT_FWD + 1e-6:
        out.append(("forecastle", MAIN, QUARTER))
    if x < POOP_FRONT - 0.25:
        out.append(("poopcabin", QUARTER, POOP))
    return out


STRIP_PROUD = 0.03        # (0.3.10) how far a ceiling strip stands in from the ceiling (clear of its planking's facets)


def strip_sag(x, y, lx=0.6, ly=0.25):
    """(0.3.13, note 141) How far inboard of the ceiling's curve its planking's flat facets can lie at (x, y): the
    sagitta of a chord lx long along her (ly up her) where the curve bends inboard (the hull closing in toward the ends,
    or narrowing upward), and 0.004 more; 0 where it bends the other way (the facets outboard of it)."""
    if not GRID:
        return 0.0
    e = 0.15
    fxx = (ceil_z(x + e, y) - 2 * ceil_z(x, y) + ceil_z(x - e, y)) / (e * e)
    fyy = (ceil_z(x, y + e) - 2 * ceil_z(x, y) + ceil_z(x, y - e)) / (e * e)
    sag = max(0.0, -fxx) * lx * lx / 8 + max(0.0, -fyy) * ly * ly / 8
    return sag + 0.004 if sag > 1e-4 else 0.0


def ceiling_strip(d, x0, x1, y0, y1, s, upright, mat="timber", inset=STRIP_PROUD, group="auto", edges=(True, True)):
    """(GRID) A flat strip on the ceiling, standing in from it, following its curve in pieces: an inlay (a rider
    carried down the wall), a panel's stile (upright) or rail. The wood's grain along it. Pieces where the ceiling is
    too close to the centreline (past the hull's ends) are left out. (0.3.10, clean surfaces: STRIP_PROUD in from the
    ceiling, 0.006 before, which the planking's flat facets came within 2 mm of: z-fighting; its long edges closed by
    faces from 0.01 inside the planking; edges: which of them (the lower/after one, the upper/forward one): none where
    it lies on a floor)"""
    nx = max(1, int(math.ceil((x1 - x0) / 0.5 - 1e-9)))
    ny = max(1, int(math.ceil((y1 - y0) / 0.25 - 1e-9)))
    xs, ys = np.linspace(x0, x1, nx + 1), np.linspace(y0, y1, ny + 1)
    for i in range(nx):
        for j in range(ny):
            corners = [(xs[i], ys[j]), (xs[i + 1], ys[j]), (xs[i + 1], ys[j + 1]), (xs[i], ys[j + 1])]
            cs = [ceil_z(x, y) for x, y in corners]
            if min(cs) < 0.4:
                continue
            # (0.3.13, the user's note 141: "some texture overlap", the lower deck's riders' inlays at the stern cut into
            # jagged pieces by the planking poking through them) where the ceiling curves in hard toward the ends, its
            # planking's flat facets (about 0.56 long, its strakes 0.25 high) lie further in than its curve by their
            # sagitta: the strip that much further in there too
            pts = [(x, y, s * (c - inset - strip_sag(x, y))) for (x, y), c in zip(corners, cs)]
            back = [(x, y, s * c) for (x, y), c in zip(corners, cs)]       # (the planking's facets are inboard of c)
            if upright:
                pts = [pts[0], pts[3], pts[2], pts[1]]      # (u, the grain, up it)
                back = [back[0], back[3], back[2], back[1]]
                # its long edges: the after one (x0) and the forward one (x1)
                sides = [((0, 1), (-1, 0, 0), i == 0 and edges[0]), ((3, 2), (1, 0, 0), i == nx - 1 and edges[1])]
            else:
                sides = [((0, 1), (0, -1, 0), j == 0 and edges[0]), ((3, 2), (0, 1, 0), j == ny - 1 and edges[1])]
            _quad_facing(d, group, mat, pts, (0, 0, -s))
            if inset > 0.01:
                for (a, b), want, on in sides:
                    if on:
                        _quad_facing(d, group, mat, [back[a], back[b], pts[b], pts[a]], want,
                                     (0, 0, 16, min(16, (inset + 0.01) * 16)))


def fillet_knee(d, group, mat, xa, xb, s, y_top, b, a, wall, d0, n=8):
    """
    (0.3.12, the user's notes 119 and 120: "spline more curved to beam instead of triangle", "inner corner more rounded or
    spline") A knee curved into both members it joins, between x = xa and xb on side s: under a beam's underside (y_top)
    against an upright face (wall(x, y): its half width there), its face a quarter ellipse from the upright b down
    (standing d0 in from it there, tangent to it) out and up to the beam's underside a further in (tangent to that). In
    horizontal slices from the upright out to the curve (each a convex piece), its body from 0.01 behind the upright's face
    and 0.02 up into the beam; faces: its two sides, the curve (smoothed) and its foot (the narrow end under the curve's
    start); none in the beam or the upright.
    """
    def P(x, depth, v):
        y = y_top - v
        return np.array([x, y, s * (min(wall(xa, y), wall(xb, y)) - depth)])

    th = np.linspace(0.0, math.pi / 2, n + 1)
    us = [d0 + a * (1 - math.cos(t)) for t in th]
    vs = [b * (1 - math.sin(t)) for t in th]
    us.append(us[-1])
    vs.append(-0.02)
    back = -0.01
    sid = f"knee{len(d.quads)}"
    for i in range(len(us) - 1):
        for x, want in ((xa, (-1, 0, 0)), (xb, (1, 0, 0))):
            _quad_facing(d, group, mat, [P(x, back, vs[i]), P(x, back, vs[i + 1]), P(x, us[i + 1], vs[i + 1]),
                                         P(x, us[i], vs[i])], want)
        if i < len(us) - 2:
            # the curve, facing out of it (inboard and down)
            q = [P(xa, us[i], vs[i]), P(xb, us[i], vs[i]), P(xb, us[i + 1], vs[i + 1]), P(xa, us[i + 1], vs[i + 1])]
            nrm = np.cross(q[1] - q[0], q[3] - q[0])
            out = np.array([0.0, -1.0, 0.0]) + np.array([0.0, 0.0, -s])
            if np.dot(nrm, out) < 0:
                q = [q[1], q[0], q[3], q[2]]
            w = float(np.linalg.norm(q[1] - q[0]))
            h = float(np.linalg.norm(q[3] - q[0]))
            d.add_quad(group, mat, q, (0, 0, min(16, w * 16), min(16, max(h * 16, 0.5))), smooth=sid)
    _quad_facing(d, group, mat, [P(xa, back, vs[0]), P(xb, back, vs[0]), P(xb, us[0], vs[0]), P(xa, us[0], vs[0])],
                 (0, -1, 0))


KNEE_DROP, KNEE_REACH = 0.6, 0.55     # (0.3.28, notes 228, 229) a hanging knee's legs: down the rib, in under the beam
STRAIGHT_KNEES = MODULAR


def straight_knee(d, group, mat, xa, xb, s, y_top, drop, reach, wall):
    """
    (0.3.28, notes 228, 229: "make shape normal like a carpenter's joint: the beam going over the rib, with a sloped end,
    and the rib cut flush with the beam"; "the sloped support could be just a straight triangle and a bit larger") A
    hanging knee under a beam's end, between x = xa and xb on side s: a straight-sided triangle in the beam's plane, its
    back against the upright (wall(x, y): that face's half width) from the beam's underside y_top `drop` down, its top
    under the beam `reach` in from the upright, its long side straight between them (a hair of a foot at its lower end).
    Its back 0.01 into the upright and its top 0.02 up into the beam: no face of it in theirs.
    """
    # (its foot 0.03 over the rider's foot: no face of it in the rider's)
    lo = y_top - drop + 0.03
    prof = [(-0.01, y_top + 0.02), (-0.01, lo), (0.03, lo), (reach, y_top + 0.02)]

    def P(x, u, y):
        return (x, y, s * (min(wall(xa, y), wall(xb, y)) - u))

    d.add_prism(group, mat, [P(xa, u, y) for u, y in prof], [P(xb, u, y) for u, y in prof])


def rib_outer(x, y, two=None):
    """(0.3.30, note 235: "end of these and floor beams are cut at angle to as rib outer face projection") How far out the
    frames' (ribs') outer faces are at (x, y), where a beam's end meets her side: under her weather decks (two layers) the
    planking's back (the rib between the ceiling and the planking), 0.005 short of it (in the rib, out of its face's
    plane); over her open decks the bulwark planking's inside (the frames stand inside it), 0.01 into it. two: which of
    the two to take (a beam on the line where her weather deck steps takes one for all its corners)."""
    if two is None:
        two = LINED and lined(x, y)
    if not two and LINED and lined(x, y - 0.01):
        return ceil_z(x, y - 0.01) + lining_at(y) + 0.01
    if two:
        # (her outline's offsets are square to it in plan: where her side runs aslant, the planking's back is that much
        # further in across her)
        e = 0.05
        sl = (hw(x + e, y) - hw(x - e, y)) / (2 * e)
        return hw(x, y) - (PLANK_T + 0.005) * math.sqrt(1.0 + sl * sl)
    return ceil_z(x, y) + 0.01


def beam_end_wedge(d, group, mat, xc, w, y0, y1, half, s, zfun=None):
    """(0.3.28, note 228: "some triangular hole" at a beam's end) A deck beam's end sloped to her side: the wedge
    between its square end (half, out from the centreline) and her side's face where it leans in over it (wider at the
    beam's underside than at its top). ((0.3.30, note 235) each corner of its end on that face (zfun(x, y): rib_outer,
    the ribs' outer face), so the end is cut in the plane of her side there, flush: not square, not short of it)"""
    xa, xb = xc - w / 2, xc + w / 2
    if zfun is None:
        two_ = LINED and all(lined(x_, y_) for x_ in (xa, xb) for y_ in (y0 + 0.01, y1 - 0.01))

        def zfun(x, y):
            return rib_outer(x, y, two_)

    def P(x, y, out):
        return (x, y, s * (zfun(x, y) if out else half))

    lo = min(zfun(xa, y0), zfun(xb, y0), zfun(xa, y1), zfun(xb, y1))
    if lo - half < 0.002:
        return
    d.add_prism(group, mat, [P(xa, y0, False), P(xa, y0, True), P(xa, y1, True), P(xa, y1, False)],
                [P(xb, y0, False), P(xb, y0, True), P(xb, y1, True), P(xb, y1, False)])


def grid_framing(d, frame_part, in_window, at_hawse):
    """
    (0.3.9, GRID) The hull's skeleton on the storeys of 3 (2 blocks of room, a 1-block band of the deck's structure):
    - the frames every 2 blocks on the even x, in the hull's thickness behind the ceiling (not seen in the rooms); over
      the open decks they stand inside the bulwarks, from the deck to the rail
    - in each room's band, at each frame: a rider up the ceiling (the visible rib) and a hanging knee under the beam's
      end in its own plane (0.3.10; beside it before); in the hold the beams the band's full depth, carlings between
      them, the riders and knees 0.7 down the side under it
    - the ceiling's lower 2 blocks the plain wall, the riders carried down it as inlays (texture strips); the great
      cabin and the poop cabin panelled flush (rails along, a stile in each bay, the inlays the others)
    - the deck beams 0.5 deep under the planks (open bays between them); (0.3.10) no clamp along the side under them
    """
    S = FRAME_SIDED
    stations = frame_stations()

    def slope(x, y):
        e = 0.05
        return abs(ceil_z(x + e, y) - ceil_z(x - e, y)) / (2 * e)

    # ---- the frames over the open decks, inside the bulwarks
    d.paint_tag = "frame"
    gallows = [gx + S / 2 for gx in GALLOWS_X]
    for xf in stations:
        lo = open_floor(xf)
        xa, xb = xf - S / 2, xf + S / 2
        if ceil_z(xf, lo + 0.3) < 0.5:
            continue
        post = any(abs(xf - g) < 1e-6 for g in gallows)
        top_a, top_b = (rail(xa), rail(xb)) if post else (rail(xa) - 0.03, rail(xb) - 0.03)
        if DOOR_ROOM and WAIST_AFT < xf < WAIST_AFT + 1.0:
            # (0.3.13, the user's note 142 B: "Maybe rib could be shortened") the frame just forward of the great
            # cabin's front no higher than the waist's rail: the castle break's sweep over it plain planking, the
            # corner by the companion's door clear of it
            top_a = top_b = MAIN + WAIST_RAIL - 0.03
        if min(top_a, top_b) - lo < 0.3:
            continue
        for s in (1, -1):
            frame_part(xf, s, lo, top_a, top_b, head=not post, skip=at_hawse)

    # ---- the bands: riders and knees; the walls under them: inlays
    y_inlay = {}
    for xf in stations:
        for room, floor, deck in grid_rooms(xf):
            yu = deck - DECK_T                       # the deck's underside
            yb = floor + 2.0                         # the band's bottom
            y_r = yb - (0.7 if room == "hold" else 0.0)
            if ceil_z(xf, (floor + yu) / 2) < 1.0 or ceil_z(xf, yu) < 0.8 or ceil_z(xf, floor + 0.05) < 0.6:
                continue
            # ((0.3.29, LINED) a room's side two layers: its ceiling smooth, the frames inside it (no riders, no inlays);
            # the knees under the beams against the ceiling)
            smooth_ = LINED and bool(lined(xf, (floor + yu) / 2))
            for s in (1, -1):
                d.paint_tag = "frame"
                if slope(xf, yb) < 0.9 and not in_window(xf, yu - 0.1):
                    yk = yu - (HOLD_BEAM_D if room == "hold" else BEAM_D)          # the beam's underside
                    # (0.3.12, the user's note 120: "riders could be spline more curved to beam instead of triangle and
                    # recangle at base") the rider only behind the beam's end; under
                    # the beam the rider and its knee are one curved timber: from the inlay on the wall at the band's
                    # foot (in the hold 0.7 down the side) it curves out and up into the beam's underside, tangent to
                    # both (a quarter ellipse), reaching as far in as the knee did (0.3, in the hold 0.6, past the
                    # rider's face), in the beam's plane, its sides 0.01 in from the beam's (inside the inlay's)
                    # (its foot 0.02 up in the beam: not in the plane of the beam's underside)
                    if STRAIGHT_KNEES:
                        # ((0.3.28, notes 228, 229) the beam over the rib: the rider up the wall to the beam's underside,
                        # cut flush there (the beam's end over it to the ceiling, sloped to it: deck_beam), a straight
                        # knee under the beam against the rider, larger (KNEE_DROP down it, KNEE_REACH in under the beam)
                        drop_ = max(KNEE_DROP, yk - y_r)
                        y_lo_ = yk - drop_
                        if not smooth_:
                            frame_part(xf, s, y_lo_, yk, yk, depth=0.12, skip=at_hawse)
                        if not at_hawse(xf, yk - drop_ / 2):
                            straight_knee(d, "auto", "timber", xf - (S - 0.02) / 2 + 0.01, xf + (S - 0.02) / 2 - 0.01, s,
                                          yk, drop_, KNEE_REACH + (0.15 if room == "hold" else 0.0) + (0.12 if smooth_ else 0.0),
                                          (lambda x, y: ceil_z(x, y) - 0.005) if smooth_ else (lambda x, y: ceil_z(x, y) - 0.12))
                        y_inlay[(xf, room, s)] = y_lo_
                    else:
                        frame_part(xf, s, yk + 0.02, yu, yu, depth=0.12, skip=at_hawse)
                    if not STRAIGHT_KNEES and yk - y_r > 0.2 and not at_hawse(xf, (yk + y_r) / 2):
                        reach = (0.6 if room == "hold" else 0.3) + 0.12 - (STRIP_PROUD - 0.004)
                        fillet_knee(d, "auto", "timber", xf - (S - 0.02) / 2 + 0.01, xf + (S - 0.02) / 2 - 0.01, s, yk,
                                    yk - y_r, reach,
                                    lambda x, y: ceil_z(x, y), STRIP_PROUD - 0.004)
                # the rider carried down the wall as an inlay (a stile in the panelled cabins)
                if not smooth_ and not in_window(xf, floor + 1.0) and slope(xf, floor + 1.0) < 1.2:
                    # (0.3.10: 0.012 inside the rider's sides each side: a window's jamb on the frame's face)
                    ceiling_strip(d, xf - S / 2 + 0.012, xf + S / 2 - 0.012, floor, y_inlay.get((xf, room, s), y_r), s,
                                  upright=True)

    # (0.3.12, the user's note 126: "remove there panel detailings for now as they anyway would need more work and maybe
    # later could replace with paint structure") no rails and stiles in the cabins (PANELLED empty); only (note 118:
    # "windows looks too wide without middle post. could continue blue inlay up as post") under each side window the
    # stile in the middle of its bay, from the floor to the window's sill, carried on up through the window as its post
    # (side_windows)
    d.paint_tag = "frame"
    for wx0, wx1, wy0, wy1 in () if GRID else SIDE_WINDOWS:
        floor = max(t for t in d.deck_tops if t < wy0)
        xs_ = (wx0 + wx1) / 2
        for s in (1, -1):
            ceiling_strip(d, xs_ - 0.06, xs_ + 0.06, floor, wy0, s, upright=True)
    # (0.3.14, the user's note 144) the side windows two narrow lights a bay as the stern gallery's: the stile in the
    # bay's middle between them, from the floor up past them to the deck's underside, as the stern timbers between the
    # transom's lights
    for xs_ in SIDE_STILES if GRID else ():
        floor = max(t for t in d.deck_tops if t < SIDE_WINDOWS[0][2])
        top = min(t for t in d.deck_tops if t > SIDE_WINDOWS[0][3]) - DECK_T
        for s in (1, -1):
            ceiling_strip(d, xs_ - 0.06, xs_ + 0.06, floor, top, s, upright=True)
    # the panelled cabins: rails along the walls (at the floor, the middle and the band) and a stile in each bay
    for room, floor, x_end in (("greatcabin", MAIN, WAIST_AFT - 0.25), ("poopcabin", QUARTER, POOP_FRONT - 0.25)):
        if room not in PANELLED:
            continue
        d.paint_tag = "frame"
        xo, _ = _ceil_ring(floor + 1.0)
        x_start = float(xo[0]) + 0.05
        # (0.3.10, clean surfaces: the rails run between the stiles, 0.01 into them, 0.01 less proud: they crossed in
        # one plane)
        stiles = ([(xf - S / 2 + 0.022, xf + S / 2 - 0.022) for xf in stations] +
                  [(k - 0.06 + 0.01, k + 0.06 - 0.01) for k in range(-40, 40) if k % 2])
        # (the middle rail's top 0.01 under the side windows' sills, G +1: not in their plane)
        for y0, y1 in ((floor, floor + 0.12), (floor + 0.89, floor + 0.99), (floor + 1.88, floor + 2.0)):
            # (broken at the windows)
            breaks = sorted([(a - 0.06, b + 0.06) for a, b, wy0, wy1 in SIDE_WINDOWS if wy0 < y1 and wy1 > y0] +
                            [st for st in stiles if x_start - 0.3 < st[0] < x_end])
            runs, x = [], x_start
            for a, b in breaks:
                if a > x:
                    runs.append((x, min(a, x_end)))
                x = max(x, b)
            if x < x_end:
                runs.append((x, x_end))
            for a, b in runs:
                if b - a > 0.05:
                    for s in (1, -1):
                        ceiling_strip(d, a, b, y0, y1, s, upright=False, inset=STRIP_PROUD - 0.01,
                                      edges=(y0 > floor + 0.01, True))
        # a stile in the middle of each bay (the odd x), under a window only up to its sill
        for xs_ in [float(k) for k in range(int(math.ceil(x_start)), int(math.floor(x_end)) + 1) if k % 2]:
            top = floor + 1.88
            for a, b, wy0, wy1 in SIDE_WINDOWS:
                if a - 0.06 < xs_ < b + 0.06 and wy0 < top:
                    top = min(top, wy0 - 0.06)
            if top - (floor + 0.12) < 0.1:
                continue
            for s in (1, -1):
                ceiling_strip(d, xs_ - 0.06, xs_ + 0.06, floor + 0.12, top, s, upright=True)

    # ---- the deck beams on the frames, round the openings in the decks
    d.paint_tag = "beam"           # ((0.3.12, notes 117, 125) its own zone: paint_zones BEAM_ZONE)

    d.beam_spans = []

    def deck_beam(xc, y_mid, height, cuts, code=None, top=None):
        half = ceil_z(xc, y_mid)
        if MODULAR:
            # ((0.3.27, note 208: "protruding corner of other element") its ends at her ceiling where it's narrowest over
            # the beam's depth and breadth: where her side curves in under a deep beam (the hold's, aft) its lower corner
            # came out through her planking)
            half = min(ceil_z(x_, y_) for x_ in (xc - (S - 0.02) / 2, xc + (S - 0.02) / 2)
                       for y_ in (y_mid - height / 2, y_mid, y_mid + height / 2))
        if half < 0.6:
            return
        spans = [(-half, half)]
        for c0, c1 in cuts:
            spans = [p for a, b in spans for p in ((a, min(b, c0)), (max(a, c1), b)) if p[1] - p[0] > 0.05]
        n0 = len(d.quads)
        for a, b in spans:
            d.add_box_mesh("auto", "timber", (xc, y_mid, (a + b) / 2), (S - 0.02, height, b - a))
        if STRAIGHT_KNEES:
            # ((0.3.28, note 228) its ends sloped to her ceiling where it leans in over them: no gap under the end)
            for a, b in spans:
                for z_, s_ in ((b, 1), (a, -1)):
                    if abs(abs(z_) - half) < 1e-6:
                        beam_end_wedge(d, "auto", "timber", xc, S - 0.02, y_mid - height / 2, y_mid + height / 2, half, s_)
        # ((0.3.27) kept for beam_segments: a beam under a well comes out in lengths, its timber split there)
        if code is not None:
            d.beam_spans.append({"deck": code, "top": top, "xc": xc, "y0": y_mid - height / 2, "y1": y_mid + height / 2,
                                 "spans": spans, "quads": d.quads[n0:]})

    for bx in main_beams():
        # (0.3.10, clean surfaces: a beam cut by the companion ends in the middle of the carling along its side, not
        # on the carling's face)
        cuts = [(o[2] - 0.06, o[3] + 0.06) for o in (HATCH, COMPANION) if o[0] - 0.25 < bx < o[1] and
                (not MODULAR or bx + 0.25 <= o[1] + 1e-6) and
                not (MODULAR and o is COMPANION)]    # ((0.3.18) carrack-m: the companion's well between two whole beams)
        if MODULAR:
            # ((0.3.20, step B) only a beam across the hatchway is cut, its ends against the carlings' outer faces (the
            # carlings run on past it: nothing of the two in one another, where a cell taken up beside it would show it))
            cuts = [(OPENING[2] - 0.12, OPENING[3] + 0.12)] if OPENING[0] < bx < OPENING[1] - 0.25 else []
            if not COMPANION_WAIST and COMPANION[0] < bx + 0.25 and bx < COMPANION[1]:
                # ((0.3.23) and the beams across the companion's well or over its forward end (the stair's head: its
                # header there, companion_rail), as the hatchway's)
                cuts.append((COMPANION[2] - 0.12, COMPANION[3] + 0.12))
        deck_beam(bx + 0.125, MAIN - DECK_T - BEAM_D / 2, BEAM_D, cuts, "main", MAIN)
    for bx in castle_beams():
        deck_beam(bx + 0.125, QUARTER - DECK_T - BEAM_D / 2, BEAM_D, [], "quarter" if bx < 0 else "forecastle", QUARTER)
    for bx in edge_beams():
        deck_beam(bx + 0.125, QUARTER - DECK_T - BEAM_D / 2, BEAM_D, [])          # ((0.3.27) a deck's edge: no lengths out)
    for bx in poop_beams():
        deck_beam(bx + 0.125, POOP - DECK_T - BEAM_D / 2, BEAM_D, [], "poop", POOP)
    # the lower deck's in the hold, its band's full depth, round the masts, the pump and the hold hatch
    obstacles = [(MASTS[m][0], 0.0, 0.55) for m in MASTS] + [(PUMP[0], PUMP[1], 0.22)]
    hx0, hx1, hz0, hz1 = HATCH
    y_beam = LOWER - DECK_T - HOLD_BEAM_D / 2
    lb = [x0 + 0.125 for x0 in lower_beams()]
    for xc in lb:
        cuts = []
        for ox, oz, r in obstacles:
            if abs(xc - ox) < r:
                w = math.sqrt(r * r - (xc - ox) ** 2)
                cuts.append((oz - w, oz + w))
        if MODULAR:
            if OPENING[0] + 0.25 < xc < OPENING[1] - 0.25:
                # ((0.3.20) the hatchway's sides are the hold's carlings at +-1.5: the beam's ends against their outer
                # faces, a piece of carling across where it was (hatches())
                cuts.append((OPENING[2] - 0.1, OPENING[3] + 0.1))
        elif hx0 - 0.25 < xc < hx1 + 0.25:
            cuts.append((hz0 - 0.06, hz1 + 0.06))        # (0.3.10: onto the hatch's carlings' middles)
        if HOLD_STAIR and HOLD_STAIR[0] < xc < HOLD_STAIR[1]:
            # (0.3.10: onto its carlings' middles; (0.3.23) carrack-m: against their outer faces, the carlings whole
            # between the well's headers: nothing of the two in one another, seen with the cells beside taken up)
            cuts.append((HOLD_STAIR[2] - 0.12, HOLD_STAIR[3] + 0.12) if MODULAR else
                        (HOLD_STAIR[2] - 0.06, HOLD_STAIR[3] + 0.06))
        if MODULAR and HOLD_STAIR and abs(xc - HOLD_STAIR[0]) < 1e-6:
            # ((0.3.23) the beam over the well's after end, the hold stair's head: cut onto the well's carlings, a header
            # aft of the well's end instead (companion_rail): nothing of it reaching back over the stair's head cell; its
            # ends against the carlings' outer faces, and the header's)
            cuts.append((HOLD_STAIR[2] - 0.12, HOLD_STAIR[3] + 0.12))
        deck_beam(xc, y_beam, HOLD_BEAM_D, cuts, "lower", LOWER)
    # the carlings fore and aft between them ((0.3.24, note 186) carrack-m: none: only the hatchway's and the hold stair
    # well's own, framing their openings: hatches(), companion_rail())
    for xa_, xb_ in () if MODULAR else zip(lb[:-1], lb[1:]):
        for zc in HOLD_CARLINGS:
            ya_ = LOWER - DECK_T - 0.25
            if min(ceil_z(xa_, ya_ - 0.25), ceil_z(xb_, ya_ - 0.25)) < abs(zc) + 0.3:
                continue
            segs = [(xa_ + S / 2, xb_ - S / 2)]
            if HOLD_STAIR and HOLD_STAIR[2] - 0.15 < zc < HOLD_STAIR[3] + 0.15:
                # (0.3.10: not through the hold stair's opening: to its header's middle. (0.3.13) nor along its edge,
                # where it's a block inboard on the carlings' line at 1.5: its own carlings are there)
                c0, c1 = HOLD_STAIR[0] - 0.125, HOLD_STAIR[1] + 0.125
                if MODULAR:
                    c0, c1 = HOLD_STAIR[0] - 0.25, HOLD_STAIR[1] + 0.25   # ((0.3.23) to its headers' outer faces)
                segs = [p for a, b in segs for p in ((a, min(b, c0)), (max(a, c1), b)) if p[1] - p[0] > 0.05]
            for a_, b_ in segs:
                d.add_box_mesh("auto", "timber", ((a_ + b_) / 2, ya_, zc), (b_ - a_, 0.5, 0.2))

    # ---- the clamps: a timber along the side in each band, the beams' ends resting on it. (0.3.10, the user's notes 73
    # and 76: "we don't need protruding horizontal beam member below floor beams": none; the riders and their knees carry
    # the beams)
    for room_, floor, deck, lo_x, hi_x in () if GRID else (("hold", HOLD_FLOOR, LOWER, -30, 40), ("lowerdeck", LOWER, MAIN, -30, 40),
                                          ("greatcabin", MAIN, QUARTER, -30, WAIST_AFT - 0.3),
                                          ("forecastle", MAIN, QUARTER, WAIST_FWD + 0.3, 40),
                                          ("poopcabin", QUARTER, POOP, -30, POOP_FRONT - 0.3)):
        yc = deck - DECK_T - (HOLD_BEAM_D if room_ == "hold" else BEAM_D) - 0.07
        xs_ = [x for x in np.arange(-20.0, 30.0, 0.25) if lo_x <= x <= hi_x]
        for s in (1, -1):
            run = []
            for k, x in enumerate(xs_):
                ok = ceil_z(x, yc) > 0.8 and slope(x, yc) < 0.9
                if ok:
                    run.append(np.array([x, yc, s * (ceil_z(x, yc) - 0.075)]))
                if (not ok or k == len(xs_) - 1) and len(run) > 1:
                    for a, b in zip(run[:-1], run[1:]):
                        d.add_beam_mesh("auto", "timber", a, b, 0.15, 0.14)
                if not ok:
                    run = []


def ruled_timber(d, group, mat, sections, caps=(True, True)):
    """
    A timber through cross-sections (each four corners in the same order round it, one after another along it): its
    sides from one section to the next, wound outward, the grain (u) along it carried on from piece to piece; caps on
    the first and last sections. One mesh, so there are no faces between its pieces (they'd lie on each other).
    """
    secs = [[np.asarray(p, float) for p in sec] for sec in sections]
    along = 0.0
    for a, b in zip(secs[:-1], secs[1:]):
        ca, cb = np.mean(a, axis=0), np.mean(b, axis=0)
        seg = float(np.linalg.norm(cb - ca))
        u0 = (along * 16) % 12
        for i in range(4):
            q = [a[i], b[i], b[(i + 1) % 4], a[(i + 1) % 4]]
            out = np.mean(q, axis=0) - (ca + cb) / 2
            w = float(np.linalg.norm(a[(i + 1) % 4] - a[i]))
            _quad_facing(d, group, mat, q, out, (u0, 0, u0 + min(4.0, seg * 16), min(16, w * 16)), keep_uv=True)
        along += seg
    for sec, other, on in ((secs[0], secs[1], caps[0]), (secs[-1], secs[-2], caps[1])):
        if on:
            _quad_facing(d, group, mat, sec, np.mean(sec, axis=0) - np.mean(other, axis=0))


def _x_at_ceiling(y, w, from_x, to_x):
    """Between from_x and to_x (one inside the inside planking's outline at y, wider than w; the other at its end),
    where the inside planking is w out from the centreline."""
    a, b = from_x, to_x
    for _ in range(40):
        m = (a + b) / 2
        if ceil_z(m, y) > w:
            a = m
        else:
            b = m
    return (a + b) / 2


def centreline_timbers(d):
    """
    (0.3.7) The timbers on the centreline inside the ends of the hull, where the inside planking of both sides meets
    and left a slit (the user's notes 5, 26, 29, 31): the apron up the inside of the stem, from the hold to the
    forecastle's deck; the knighthead inside the forecastle's tip, from its deck to its rail; and the inner sternpost up
    the inside of the stern from the hold to under the tiller, broad and deep enough that the rudder's stock, its
    gudgeons and the sternpost (on the straight line the rudder turns on, a little inside the stern's curve) stay
    inside it. Each stands into the planking at its back; the inner post is solid where it stands into a deck.
    """
    def sections(ys, front_back):
        out = []
        for y in ys:
            fb = front_back(y)
            if fb is None:
                continue
            xa, xb, w = fb
            out.append([(xa, y, -w), (xb, y, -w), (xb, y, w), (xa, y, w)])
        return out

    # the apron and the knighthead: their after face where the planking is their half width out, forward into the bow
    def bow(w):
        def f(y):
            xo, zo = _ceil_ring(y)
            if len(xo) < 2:
                return None
            x_end = float(xo[-1])
            xa = _x_at_ceiling(y, w, x_end - 3.0, x_end)
            return xa, x_end + 0.02, w
        return f

    apron = sections(list(np.arange(HOLD_FLOOR, FC_Y0 - DECK_T + 1e-6, 0.125)), bow(0.17))
    if len(apron) > 1:
        ruled_timber(d, "auto", "timber", apron)
    knight = sections(list(np.arange(QUARTER - DECK_T, rail(FC_TIP - 0.6) + 0.02 + 1e-6, 0.125)), bow(0.13))
    if len(knight) > 1:
        ruled_timber(d, "auto", "timber", knight)
    d.apron = apron

    # the inner sternpost: its forward face clear ahead of everything of the rudder's there (0.47 from its axis: the
    # gudgeons reach 0.41), its back into the transom or the stern's planking, a little wider than the gudgeons
    ra, axis = d.rudder_axis
    w = 0.22

    def stern(y):
        xo, zo = _ceil_ring(y)
        if len(xo) < 2:
            return None
        x_start = float(xo[0])
        x_axis = float(ra[0] + axis[0] * (y - ra[1]) / axis[1])
        xf = max(x_axis + 0.47, _x_at_ceiling(y, w, x_start + 3.0, x_start) + 0.02)
        return x_start - 0.03, xf, w

    top = TILLER_Y - 0.3
    post = sections(list(np.arange(HOLD_FLOOR, top + 1e-6, 0.125)), stern)
    if len(post) > 1:
        ruled_timber(d, "auto", "timber", post)
    d.inner_post = post
    # solid where it stands in the hold and on the lower deck (nobody walks into it)
    for y0 in np.arange(HOLD_FLOOR, top - 0.01, 0.25):
        if LOWER - DECK_T - 0.01 < y0 < LOWER - 0.01:
            continue
        here = [sec for sec in post if y0 - 1e-6 <= sec[0][1] <= y0 + 0.25 + 1e-6]
        if here:
            x0 = min(sec[0][0] for sec in here)
            x1 = max(sec[1][0] for sec in here)
            d.box("hidden", x0, x1, y0, y0 + 0.25, -w, w, where=(d.grid == 0))


# ==================================================================== the stairs to the castles
def lathe(d, group, mat, base, height, profile, sides=8):
    """A turned post standing at base: profile [(fraction of height, radius), ...] from its foot up (a baluster: its
    paint zone)."""
    base = np.asarray(base, float)
    old, d.paint_tag = d.paint_tag, ("balusters" if d.paint_tag is not None else None)
    for (fa, ra), (fb, rb) in zip(profile[:-1], profile[1:]):
        d.add_cylinder(group, mat, base + np.array([0, fa * height, 0]), base + np.array([0, fb * height, 0]), ra, rb,
                       sides=sides, caps=False)
    d.paint_tag = old


# A turned baluster: square-ish blocks at the foot and head, a swelling vase under a narrow neck, a ring and a collar
# (fractions of its height, radii). Since 0.3.7 about two thirds stouter (the user's notes 8 and 9: "too thin"), a
# Tudor stair or gallery's: some 0.11 across at its neck, 0.18 at its belly.
BALUSTER = [(0.0, 0.07), (0.08, 0.07), (0.1, 0.05), (0.14, 0.062), (0.34, 0.092), (0.5, 0.058), (0.58, 0.044),
            (0.64, 0.06), (0.7, 0.046), (0.84, 0.052), (0.9, 0.068), (1.0, 0.068)]


def stair_rail_z(st):
    """The middle of a stair's handrail across it: on its open (inboard) side; the companion's and the hold stair's
    ("rail": "outboard") on their outboard side, away from the wall or the railing round their well. (0.3.12, the user's
    note 113: since they were mirrored to starboard (0.3.10) theirs stood on their inboard side, beside that railing: two
    rails side by side)"""
    z0, z1 = st["steps"][0][3], st["steps"][0][4]
    if st.get("rail") == "outboard" and z0 + z1 > 0:
        return z1 - 0.08
    return z0 + 0.08


def castle_stairs(d):
    """
    Each stair (starboard, see build) as a ship's stair: two stringers, open treads with a nosing, a handrail on turned
    stanchions on its open side. The steps stand further inboard toward the top, where the side leans in.
    """
    for st in d.stairs:
        steps, direction, fy = st["steps"], st["dir"], st["floor"]
        for xa, xb, top, z0, z1 in steps:
            front = xb if direction < 0 else xa           # the edge you step up onto
            fx_ = front + 0.04 * (1 if direction < 0 else -1)
            back = xa if direction < 0 else xb
            x0, x1 = sorted((fx_, back))
            d.add_box_mesh("auto", "oak", ((x0 + x1) / 2, top - 0.035, (z0 + z1) / 2), (x1 - x0, 0.07, z1 - z0 - 0.02))
            # a cleat under each end of the tread, on the stringers
            for zc in (z0 + 0.1, z1 - 0.1):
                d.add_box_mesh("auto", "timber", ((xa + xb) / 2, top - 0.1, zc), (st["run"] - 0.14, 0.06, 0.05))
        first, last = steps[0], steps[-1]
        bottom_x = first[1] if direction < 0 else first[0]
        top_x = last[0] if direction < 0 else last[1]
        # inboard and outboard stringers: boards along the pitch of the stair, their foot cut level on the deck and their
        # head level with the deck above
        rise = 3.0
        run_len = abs(top_x - bottom_x)
        k = rise / run_len                                  # height per block along

        def along(u, y):
            return (bottom_x + direction * u, y)

        d.paint_tag = "balustrade-rails"
        for zi in (3, 4):
            zc = first[zi] + (0.04 if zi == 3 else -0.04)
            # the board's top edge a little over the nosings, its bottom edge 0.3 under them (measured upright)
            u_top0, u_bot0 = -0.12 / k, 0.3 / k           # where each edge meets the deck at the foot
            # (0.3.10, clean surfaces: its foot 0.02 into the deck, its head 0.015 under the deck above's top: not in
            # their planes)
            corners = [along(u_top0, fy - 0.02), along(u_bot0, fy - 0.02), along(run_len + 0.3 / k, fy + rise - 0.015),
                       along(run_len - 0.12 / k, fy + rise - 0.015)]
            front = [(x, y, zc + 0.035) for x, y in corners]
            back = [(x, y, zc - 0.035) for x, y in corners]
            d.add_prism("auto", "timber", front, back)
        # the handrail on the open (inboard) side: a squared rail on a newel post at the foot and the head, and a
        # turned baluster on every tread (0.3.7: it was a thin round rail on two turned posts and one thin stanchion, the
        # user's note 8: "too small quantity of vertical poles and too thin")
        zr = stair_rail_z(st)
        foot = np.array([bottom_x - direction * 0.18, fy, zr])
        head = np.array([top_x + direction * 0.12, fy + 3.0, zr])
        if st["code"] == "companion":
            # (0.3.8) the great cabin's stair: its head against the front wall, its handrail on its outboard side (the
            # inboard one is the railing round its opening): the head's post on its top step. ((0.3.12) its head at the
            # landing's edge; its inboard side the stairwell's wall)
            head = np.array([top_x - direction * 0.13, fy + 3.0, zr])
        posts = (foot, head)
        if not math.isnan(st["rail_x"][0]):
            # (0.3.12, the user's note 113: "railings from deck and stairs overlap. We should make a single rail post that
            # connects both") a castle stair's head comes up through its front's railing: the railing's newel there is
            # its head post too (balustrades), on the handrail's line in the middle of the railing; no post of its own
            head = np.array([(st["rail_x"][0] + st["rail_x"][1]) / 2, fy + 3.0, zr])
            posts = (foot,)
        for p in posts:
            d.add_box_mesh("auto", "timber", p + np.array([0, 0.53, 0]), (0.15, 1.06, 0.15))
            d.add_box_mesh("auto", "oak", p + np.array([0, 1.08, 0]), (0.2, 0.05, 0.2))
            d.add_box_mesh("auto", "timber", p + np.array([0, 0.03, 0]), (0.19, 0.06, 0.19))
        ra, rb = foot + np.array([0, 0.97, 0]), head + np.array([0, 0.97, 0])
        d.add_beam_mesh("auto", "oak", ra, rb, 0.1, 0.08, up=(0, 1, 0))

        def rail_at(x):
            return ra[1] + (rb[1] - ra[1]) * (x - ra[0]) / (rb[0] - ra[0])

        for xa, xb, top, z0, z1 in steps:
            xm = (xa + xb) / 2
            lathe(d, "auto", "timber", (xm, top, zr), rail_at(xm) - 0.04 - top, BALUSTER, 8)
        d.paint_tag = None


def balustrades(d):
    """The castle fronts' railings: a plinth on the deck's edge, turned balusters and a handrail, newel posts where a
    stair comes up through it; the ends run into the sides."""
    for x0, x1, y0 in d.breastworks:
        xc = (x0 + x1) / 2
        half = ceil_z(xc, y0 + 0.5) + 0.02
        gaps = [(st["steps"][-1][3], st["steps"][-1][4]) for st in d.stairs if abs(st["rail_x"][0] - x0) < 1e-6]
        # (0.3.12, note 113) where a stair's handrail comes up to the railing: its newel there on the handrail's line
        shared = {round(stair_rail_z(st) - 0.08, 4): stair_rail_z(st) for st in d.stairs
                  if abs(st["rail_x"][0] - x0) < 1e-6}
        spans = [(-half, half)]
        for g0, g1 in gaps:
            spans = [p for a, b in spans for p in ((a, min(b, g0)), (max(a, g1), b)) if p[1] - p[0] > 0.05]
        for a, b in spans:
            zm = (a + b) / 2
            d.add_box_mesh("auto", "timber", (xc, y0 + 0.08, zm), (0.24, 0.16, b - a))          # plinth
            # (0.3.10: the handrail 0.012 short of the plinth's and cap's ends: not in their planes)
            d.add_box_mesh("auto", "timber", (xc, y0 + 0.93, zm), (0.2, 0.1, b - a - 0.024))    # handrail
            d.add_box_mesh("auto", "oak", (xc, y0 + 1.0, zm), (0.26, 0.05, b - a))              # its cap
            ends = [e for e in (a, b) if abs(abs(e) - half) > 0.01]     # at a stair's way through
            lo = a + (0.12 if a not in ends else 0.22)
            hi = b - (0.12 if b not in ends else 0.22)
            n = max(1, int(round((hi - lo) / 0.32)))
            zs_ = [lo + (hi - lo) * k / n for k in range(n + 1)]
            if hi - lo < 0.1:
                # (0.3.13, the user's note 127: "Two post where no space. place no post here in between": outboard of
                # the quarterdeck stair's way through, between its newel and the side, the span is too short for a
                # baluster (there were two, one in the other): the newel alone, the plinth and rail on into the side)
                zs_ = []
            elif hi - lo < 0.25:
                zs_ = [(lo + hi) / 2]          # (room for one: in the middle, not two crowded together)
            for z in zs_:
                lathe(d, "auto", "timber", (xc, y0 + 0.16, z), 0.72, BALUSTER, 8)
            for e in ends:                                  # newel posts beside the stair's way through
                # (0.3.10: 0.01 past the rail's ends, not in their plane)
                zn, zc_ = e - 0.09 * np.sign(e - zm), e - 0.1 * np.sign(e - zm)
                if round(e, 4) in shared:
                    # (0.3.12) the stair's head post too: on its handrail's line, the railing's ends 0.02 into it
                    zn = zc_ = shared[round(e, 4)]
                d.add_box_mesh("auto", "timber", (xc, y0 + 0.55, zn), (0.22, 1.1, 0.2))
                d.add_box_mesh("auto", "oak", (xc, y0 + 1.13, zc_), (0.28, 0.06, 0.26))


# ==================================================================== the ship's firehearth in the forecastle
HEARTH_WALL = 0.25        # its raised sides' and fireback's thickness
PIPE_R = 0.12             # the stovepipe's radius


def hearth():
    """
    The firehearth's layout (firebox() builds it, build() its collision, fixtures() the place for its fire): from the
    bulkhead (x0) to x1, its open front (z0) to its back against the side, which follows the frames' inner faces (they
    close in toward the bow and lean in going up: the base reaches out to them at its top, the fireback at its top); the
    sunken firebed (x0, x1, z0, z1) inside the raised sides and in front of the fireback; the stovepipe's centre (x, z),
    on the forecastle deck's quarter-block grid by the side, clear of the frames and outboard of the stair's head; the
    smoke hood's bottom (on the fireback) and top (under the forecastle deck).
    """
    x0, x1, z0 = HEARTH
    w = HEARTH_WALL

    def face(x, y):
        return frame_face(x, y) - 0.02

    def line(ends, x):
        return ends[0] + (ends[1] - ends[0]) * (x - x0) / (x1 - x0)

    base_out = (face(x0, MAIN + HEARTH_BASE), face(x1, MAIN + HEARTH_BASE))
    back_out = (face(x0, MAIN + HEARTH_BACK), face(x1, MAIN + HEARTH_BACK))
    back_in = (back_out[0] - w, back_out[1] - w)
    bx0, bx1 = x0 + w, x1 - w
    bed = (bx0, bx1, z0 + 0.15, min(line(back_in, bx0), line(back_in, bx1)) - 0.05)
    cx, cz = (bed[0] + bed[1]) / 2, (bed[2] + bed[3]) / 2
    # the pipe over the firebed's back, its deck collar clear of the frames up on the forecastle deck
    px = math.floor(cx / 0.25) * 0.25 + 0.125
    if HEARTH_PIPE is not None:
        return {"x0": x0, "x1": x1, "z0": z0, "base_out": base_out, "back_out": back_out, "back_in": min(back_in),
                "back_in_ends": back_in, "line": line, "bed": bed, "centre": (cx, cz), "pipe": HEARTH_PIPE,
                "hood": (MAIN + HEARTH_BACK, QUARTER - DECK_T - BEAM_D - 0.15 if GRID else QUARTER - DECK_T - 0.4)}
    pz = math.floor((frame_face(px, QUARTER + 0.5) - 0.2 - 0.125) / 0.25) * 0.25 + 0.125
    return {"x0": x0, "x1": x1, "z0": z0, "base_out": base_out, "back_out": back_out, "back_in": min(back_in),
            "back_in_ends": back_in, "line": line, "bed": bed, "centre": (cx, cz), "pipe": (px, pz),
            "hood": (MAIN + HEARTH_BACK, QUARTER - DECK_T - 0.4)}


def firebox(d):
    """
    The ship's firehearth (0.3.6; a placeholder brick block before): a brick base with a firebed sunk into it, raised
    brick sides round it at both ends and a tall fireback against the side, its front open to the room with an iron
    fender along the lip, on an iron tray that keeps the embers off the deck; the vanilla firepit stands in the firebed
    (drawn by the mod: the "hearth" fixture). Over it one tapered copper smoke hood, open below, from its hem round the
    hearth up to the stovepipe, which goes up through the forecastle deck to a rain cap.
    """
    h = hearth()
    x0, x1, z0 = h["x0"], h["x1"], h["z0"]
    w = HEARTH_WALL
    line = h["line"]
    bx0, bx1, bz0, bz1 = h["bed"]
    yd, yb, ys, yk = MAIN, MAIN + HEARTH_BED, MAIN + HEARTH_SIDES, MAIN + HEARTH_BACK
    base_top = MAIN + HEARTH_BASE

    def block(plan, y0, y1, mat="brick"):
        """An upright brick block on a plan of four (x, z) corners (in order round it)."""
        d.add_prism("auto", mat, [(x, y0, z) for x, z in plan], [(x, y1, z) for x, z in plan])

    def back_in(x):
        return line(h["back_in_ends"], x)

    def base_out(x):
        return line(h["base_out"], x)

    def back_out(x):
        return line(h["back_out"], x)

    # the iron tray under it all, a little proud of its front and ends
    tray = [(x0 - 0.06, z0 - 0.12), (x1 + 0.06, z0 - 0.12), (x1 + 0.06, base_out(x1)), (x0 - 0.06, base_out(x0))]
    block(tray, yd + 0.004, yd + 0.024, "iron")
    # the base course under the firebed, out to the frames
    block([(x0, z0), (x1, z0), (x1, base_out(x1)), (x0, base_out(x0))], yd + 0.024, yb)
    # the fireback, the whole length, against the side
    block([(x0, back_in(x0)), (x1, back_in(x1)), (x1, back_out(x1)), (x0, back_out(x0))], yb, yk)
    # the raised sides at both ends, from the front to the fireback, their tops falling a little toward the front
    for xa, xb in ((x0, x0 + w), (x1 - w, x1)):
        def side(x):
            return [(yb, z0), (yb, back_in(x)), (ys, back_in(x)), (ys - 0.15, z0)]
        d.add_prism("auto", "brick", [(xa, y, z) for y, z in side(xa)], [(xb, y, z) for y, z in side(xb)])
    # round the sunken firebed: the lip in front (the front stays open over it), and the bricks behind it to the fireback
    block([(bx0, z0), (bx1, z0), (bx1, bz0), (bx0, bz0)], yb, base_top)
    block([(bx0, bz1), (bx1, bz1), (bx1, back_in(bx1)), (bx0, back_in(bx0))], yb, base_top)
    # an iron fender along the lip, and an iron plate on the fireback behind the fire
    d.add_box_mesh("auto", "iron", ((bx0 + bx1) / 2, base_top + 0.02, z0 + 0.04), (bx1 - bx0 + 0.04, 0.04, 0.08))
    for x in (bx0 + 0.1, bx1 - 0.1):
        d.add_box_mesh("auto", "iron", (x, base_top - 0.04, z0 - 0.005), (0.05, 0.12, 0.03))   # its straps
    xm = (bx0 + bx1) / 2
    plate = [(xm - 0.42, base_top + 0.02), (xm + 0.42, base_top + 0.02), (xm + 0.42, yb + 0.95), (xm - 0.42, yb + 0.95)]
    pts = [(x, y, back_in(x) - 0.012) for x, y in plate]
    _quad_facing(d, "auto", "iron", pts, (0, 0, -1), (0, 0, 13.4, 11.5))

    # the smoke hood: one tapered shell of copper sheet (seen inside and out), from a hem round the hearth on the
    # fireback's top up to a square neck round the pipe. The sheet has a thickness (SHEET): its inside is its own face
    # that far in. (Until 0.3.7 the inside was the outside's face turned round, in the same place: the game draws a
    # ship's mesh both sides, so the two flickered against each other, light and dark, the user's notes 2 and 3.)
    px, pz = h["pipe"]
    y_lo, y_hi = h["hood"]
    n = 0.16                                           # the neck's half width
    SHEET = 0.015

    def sheet(pts, out):
        """A face of copper sheet: outside facing out, and its inside SHEET in, facing back."""
        pts = [np.asarray(p, float) for p in pts]
        nrm = np.cross(pts[1] - pts[0], pts[2] - pts[0])
        nrm = nrm / np.linalg.norm(nrm) * (1 if np.dot(nrm, out) > 0 else -1)
        _quad_facing(d, "auto", "copper", pts, out)
        _quad_facing(d, "auto", "copper", [p - nrm * SHEET for p in pts], -out)

    lo = [(x0 - 0.04, z0 - 0.06), (x1 + 0.04, z0 - 0.06), (x1 + 0.04, back_out(x1) - 0.01), (x0 - 0.04, back_out(x0) - 0.01)]
    hi = [(px - n, pz - n), (px + n, pz - n), (px + n, pz + n), (px - n, pz + n)]
    centre = np.array([(x0 + x1) / 2, (y_lo + y_hi) / 2, (z0 + back_out((x0 + x1) / 2)) / 2])
    for i in range(4):
        a, b = lo[i], lo[(i + 1) % 4]
        c, e = hi[(i + 1) % 4], hi[i]
        quad = [(a[0], y_lo, a[1]), (b[0], y_lo, b[1]), (c[0], y_hi, c[1]), (e[0], y_hi, e[1])]
        mid = np.mean(np.array(quad), axis=0)
        out = mid - centre
        out[1] = 0.0
        # (the back's lower edge follows the side, so that face is two triangles)
        tris = [quad] if i != 2 else [[quad[0], quad[1], quad[2], quad[2]], [quad[0], quad[2], quad[3], quad[3]]]
        for t in tris:
            sheet(t, out)
        if i != 2:
            # its hem: a band turned down round the front and the ends
            sheet([(a[0], y_lo - 0.08, a[1]), (b[0], y_lo - 0.08, b[1]), (b[0], y_lo, b[1]), (a[0], y_lo, a[1])], out)
    if GRID:
        # (0.3.10) the pipe from the neck up to under the grating in the forecastle deck (firebox_grating)
        d.add_cylinder("auto", "iron", (px, y_hi - 0.05, pz), (px, QUARTER - DECK_T - 0.02, pz), PIPE_R, PIPE_R, sides=10,
                       smooth=False, caps=(True, False))
        d.add_cylinder("auto", "iron", (px, y_hi - 0.02, pz), (px, y_hi + 0.08, pz), PIPE_R + 0.03, PIPE_R + 0.03,
                       sides=10, smooth=False)
        firebox_grating(d)
        return
    # the stovepipe from the neck up through the forecastle deck, iron bands where it leaves the hood and passes the deck
    d.add_cylinder("auto", "iron", (px, y_hi - 0.05, pz), (px, QUARTER + 1.5, pz), PIPE_R, PIPE_R, sides=10, smooth=False)
    d.add_cylinder("auto", "iron", (px, y_hi - 0.02, pz), (px, y_hi + 0.08, pz), PIPE_R + 0.03, PIPE_R + 0.03, sides=10,
                   smooth=False)
    d.add_cylinder("auto", "iron", (px, QUARTER - DECK_T - 0.06, pz), (px, QUARTER - DECK_T, pz), 0.2, 0.2, sides=10,
                   smooth=False)
    d.add_cylinder("exterior", "iron", (px, QUARTER, pz), (px, QUARTER + 0.06, pz), 0.2, 0.2, sides=10, smooth=False)
    # its rain cap, a cone on two legs over the pipe's mouth
    d.add_cylinder("exterior", "iron", (px, QUARTER + 1.58, pz), (px, QUARTER + 1.76, pz), 0.24, 0.02, sides=10,
                   smooth=False)
    for dx, dz in ((-0.1, -0.1), (0.1, 0.1)):
        d.add_cylinder("exterior", "iron", (px + dx, QUARTER + 1.45, pz + dz), (px + dx, QUARTER + 1.6, pz + dz), 0.015,
                       0.015, sides=4)


def firebox_grating(d):
    """(0.3.10) The grating in the forecastle deck over the hearth's pipe: a frame round the cell in the deck's thickness,
    bars fore and aft on top and athwartships under them (laid in two layers, not crossing in one plane)."""
    x0, x1, z0, z1 = d.grating
    t, w = DECK_T, 0.08
    with d.paint("ceiling"):
        for zc in (z0 + w / 2, z1 - w / 2):
            d.add_box_mesh("auto", "timber", ((x0 + x1) / 2, QUARTER - t / 2, zc), (x1 - x0, t, w))
        for xc in (x0 + w / 2, x1 - w / 2):
            d.add_box_mesh("auto", "timber", (xc, QUARTER - t / 2 - 0.005, (z0 + z1) / 2), (w, t - 0.01, z1 - z0 - 2 * w + 0.02))
        for k in range(1, 5):
            zc = z0 + w + (z1 - z0 - 2 * w) * k / 5
            d.add_box_mesh("auto", "timber", ((x0 + x1) / 2, QUARTER - 0.035, zc), (x1 - x0 - 2 * w + 0.02, 0.06, 0.05))
        for k in range(1, 5):
            xc = x0 + w + (x1 - x0 - 2 * w) * k / 5
            d.add_box_mesh("auto", "timber", (xc, QUARTER - 0.1, (z0 + z1) / 2), (0.05, 0.06, z1 - z0 - 2 * w + 0.02))


def oven(d):
    """
    (0.3.8) The clay oven's base and flue (the oven itself is vanilla's block, drawn by the mod at the "oven" fixture): a
    brick base like the hearth's out to the frames on an iron tray, a brick course behind the oven up to its top, and an
    iron flue from the oven's back up and aft into the hearth's stovepipe just over its smoke hood, with a collar where
    it joins it. Returns where the oven stands (its block's bottom centre) and the flue's run.
    """
    x0, x1, z0 = OVEN
    xm = (x0 + x1) / 2
    base_top = MAIN + HEARTH_BASE

    def face(x, y):
        return frame_face(x, y) - 0.02

    bo = (face(x0, base_top), face(x1, base_top))
    d.add_prism("auto", "iron", [(x0 - 0.06, MAIN + 0.004, z0 - 0.08), (x1 + 0.06, MAIN + 0.004, z0 - 0.08),
                                 (x1 + 0.06, MAIN + 0.004, bo[1]), (x0 - 0.06, MAIN + 0.004, bo[0])],
                [(x0 - 0.06, MAIN + 0.024, z0 - 0.08), (x1 + 0.06, MAIN + 0.024, z0 - 0.08),
                 (x1 + 0.06, MAIN + 0.024, bo[1]), (x0 - 0.06, MAIN + 0.024, bo[0])])
    plan = [(x0, z0 - 0.04), (x1, z0 - 0.04), (x1, bo[1]), (x0, bo[0])]
    d.add_prism("auto", "brick", [(x, MAIN + 0.024, z) for x, z in plan], [(x, base_top, z) for x, z in plan])
    # behind the oven, a course of bricks up to its top against the side
    zb = z0 + 1.0
    top = base_top + 0.97
    back = (face(x0, top), face(x1, top))
    if min(back) > zb + 0.03:
        d.add_prism("auto", "brick", [(x0, base_top, zb), (x1, base_top, zb), (x1, base_top, bo[1]), (x0, base_top, bo[0])],
                    [(x0, top, zb), (x1, top, zb), (x1, top, back[1]), (x0, top, back[0])])
    # the flue: up from the oven's back, aft along under the forecastle's deck into the stovepipe over the hood
    h = hearth()
    px, pz = h["pipe"]
    y_hi = h["hood"][1]
    yj = y_hi + 0.17
    a = np.array([xm, top - 0.15, zb - 0.18])
    b = np.array([xm, yj, zb - 0.18])
    c = np.array([px + PIPE_R + 0.02, yj, pz])
    d.add_cylinder("auto", "iron", a, b + np.array([0, 0.07, 0]), 0.08, 0.08, sides=8, smooth=False)
    d.add_cylinder("auto", "iron", b, c, 0.08, 0.08, sides=8, smooth=False)
    d.add_cylinder("auto", "iron", (px, yj - 0.11, pz), (px, yj + 0.11, pz), PIPE_R + 0.045, PIPE_R + 0.045, sides=10,
                   smooth=False)
    d.add_cylinder("auto", "iron", a - np.array([0, 0.05, 0]), a + np.array([0, 0.05, 0]), 0.11, 0.11, sides=8,
                   smooth=False)
    d.oven_flue = (a, b, c)
    return np.array([xm, base_top, z0 + 0.5])


# ==================================================================== the heads at the forecastle's tip
HEADS_X = 18.25 + STRETCH        # (0.3.7: 2 further forward, either side of where the bowsprit comes up over the deck)
HEADS_Z = 0.62
LANTERN_METAL = "tinbronze"      # (0.3.9, the user's note 59: the start lanterns small bronze ones: vanilla's code for it)
if GRID:
    # (0.3.9, the user's note 65: "shit boxes are too forward, player need to be able to sit on them as chair") on cells
    # 22..23 x +-(0.5..1.5), over the sea just forward of the stem, either side of where the bowsprit comes up through
    # the forecastle deck; each a seat (the mod sits a player on it like on a chair)
    HEADS_X, HEADS_Z = 22.5, 1.0
    # (0.3.24, note 195: "move shitboxes more back as now they overlap with rope. rope should be behind shitboxes") a
    # quarter block aft (their backs clear of the hand ropes, which now come from the bulwark forward of them: heads()),
    # the hole in the forecastle's underside left where it was (over the sea, forward of the stem)
    HEADS_X = 22.25
HEADS_HOLE_X = 22.45 if GRID else HEADS_X - 0.05
# (0.3.10, the user's note 91) where a sitter is put on each (the seat fixture's pos x): at the seat's front (aft) edge,
# its box set back under it, so the legs hang clear in front of the box
HEADS_SIT = HEADS_X - 0.18
# (0.3.13, the user's note 139: "shit boxes are still bit too small and also when sitting on them then player is
# standing") a proper box seat: its board 0.64 deep by 0.72 across (its top 0.5 over the deck), the box under it 0.44
# deep and 0.66 across set back 0.2 from its front edge, the back 0.72 across; the sitter in the middle of the board's
# front half (pos), the hole there
HEADS_SEAT = (0.64, 0.72) if GRID else (0.52, 0.5)
if GRID:
    HEADS_SIT = HEADS_X - 0.17


def heads(d):
    """
    The seats of ease near the forecastle's tip, one either side of the bowsprit, over the sea (its holes go down
    through the forecastle's overhang): a box seat with a round hole, a high back on its fore side so you sit facing aft,
    inboard, its lid propped up against it; a hand rope over each from the bulwark to the bowsprit. (0.3.7: they faced
    the side, a lid propped against the bulwark beside them, and a partition between them where the bowsprit now
    stands; the user's note 19: "face wrong direction")
    """
    y0 = QUARTER
    hx = HEADS_X
    for s in (1, -1):
        zc = s * HEADS_Z
        if GRID:
            # (0.3.10, the user's note 91: "player legs are in the box if 3rd person view") the box under the seat set
            # back from its front edge (aft) by 0.2, the seat board overhanging it, so a sitter (put at the seat's front,
            # HEADS_SIT) has their legs hanging down clear in front of it; the hole under them
            # ((0.3.13, note 139) bigger: HEADS_SEAT, its board from the back's face (hx + 0.245) forward... aft)
            sd_, sw_ = HEADS_SEAT
            fx_ = hx + 0.245 - sd_                      # its front edge
            d.add_box_mesh("exterior", "oak", ((fx_ + 0.2 + hx + 0.245) / 2, y0 + 0.225, zc),
                           (hx + 0.245 - fx_ - 0.2, 0.45, sw_ - 0.06))
            d.add_box_mesh("exterior", "oak", ((fx_ + hx + 0.245) / 2, y0 + 0.475, zc), (sd_, 0.05, sw_))
            hole_x = HEADS_SIT + 0.04
        else:
            d.add_box_mesh("exterior", "oak", (hx - 0.02, y0 + 0.22, zc), (0.46, 0.44, 0.44))
            d.add_box_mesh("exterior", "oak", (hx - 0.02, y0 + 0.465, zc), (0.52, 0.05, 0.5))
            hole_x = hx - 0.05
        top_ = y0 + (0.5 if GRID else 0.49)
        disk(d, "exterior", "black", (hole_x, top_ + 0.015, zc), (0, 1, 0), 0.12, 12)
        d.add_cylinder("exterior", "timber", (hole_x, top_, zc), (hole_x, top_ + 0.01, zc), 0.16, 0.16, sides=12,
                       smooth=False, caps=False)
        # its back, on its fore side, and the lid swung up against it
        bw_ = HEADS_SEAT[1]
        d.add_box_mesh("exterior", "oak", (hx + 0.27, y0 + 0.52, zc), (0.05, 1.04, bw_))
        d.add_box_mesh("exterior", "timber", (hx + 0.27, y0 + 1.06, zc), (0.08, 0.05, bw_ + 0.04))
        d.add_box_mesh("exterior", "oak", (hx + 0.225, y0 + 0.74, zc), (0.04, 0.46, bw_ - 0.1))
        # and where it all goes: a hole in the underside of the forecastle over the sea
        disk(d, "exterior", "black", (HEADS_HOLE_X, float(fc_under(HEADS_HOLE_X)) - 0.005, zc), (0, -1, 0), 0.14, 10)
        # a hand rope each side, from the bulwark aft of the seat to an eyebolt on the bowsprit ahead of it. ((0.3.24,
        # note 195) GRID: from the bulwark forward of the seat's back, behind the sitter; the eyebolt where it was)
        ex = (23.5 if GRID else hx + 0.75)       # (0.3.10: clear of the deck it came out at)
        eye = np.array([ex, bowsprit_y(ex), s * (bowsprit_r(ex) * 1.12 + 0.02)])
        d.add_cylinder("exterior", "iron", eye - np.array([0, 0, s * 0.05]), eye + np.array([0, 0, s * 0.04]), 0.035,
                       0.035, sides=6)
        bx = hx + 0.45 if GRID else hx - 0.5
        rope_path(d, [(bx, y0 + 0.95, s * (frame_face(bx, y0 + 0.9) - 0.05)), eye + np.array([0, 0, s * 0.04])], r=0.025)


# ==================================================================== the stern gallery
def stern_gallery(d):
    stains = ["glass-red", "glass-blue", "glass-yellow"]
    for row, (y0, y1, lights) in enumerate(GALLERY):
        nrm, up = transom_frame(y0, y1)
        R = np.column_stack([nrm, up, [0, 0, 1.0]])

        def tp(y, z, out):
            return np.array([stern_x(y), y, z]) + nrm * out

        def mid(y, z):
            return np.array([stern_x(y) + HULL_T * 0.55, y, z])

        h = y1 - y0
        for k, (z0, z1) in enumerate(lights):
            zc = (z0 + z1) / 2
            w = z1 - z0
            # the frame round each light
            # (0.3.10, clean surfaces: the head and sill run between the jambs, 0.01 into them, their faces 0.014 under
            # the jambs': no faces in one plane at the corners)
            d.add_box_mesh("exterior", "timber", tp(y0 - 0.04, zc, 0.028), (0.056, 0.08, w + 0.02), R)
            d.add_box_mesh("exterior", "timber", tp(y1 + 0.04, zc, 0.028), (0.056, 0.08, w + 0.02), R)
            for ze in (z0 - 0.04, z1 + 0.04):
                d.add_box_mesh("exterior", "timber", tp((y0 + y1) / 2, ze, 0.035), (0.07, h + 0.16, 0.08), R)
            # mullion and transoms: six panes a light, stained glass in the top two
            d.add_box_mesh("exterior", "timber", mid((y0 + y1) / 2, zc), (0.05, h, 0.04), R)
            ys = [y0, y0 + h * 0.36, y0 + h * 0.68, y1]
            for yb in ys[1:-1]:
                d.add_box_mesh("exterior", "timber", mid(yb, zc), (0.03, 0.04, w), R)
            for j, (ya, yb) in enumerate(zip(ys[:-1], ys[1:])):
                mat = stains[(k + row) % 3] if j == 2 else "glass"
                for za, zb in ((z0, zc), (zc, z1)):
                    pts = [mid(ya, za), mid(ya, zb), mid(yb, zb), mid(yb, za)]
                    d.add_quad("glass", mat, pts, (0, 0, 16, 16))
                    if GRID:
                        # (0.3.13, the user's note 135: "all windows could have lead dividers") in lead cames, inside
                        # the mullion and transoms (0.02 each side)
                        u0, u1 = (za, zb - 0.02) if za < zc else (za + 0.02, zb)
                        lead_cames(d, "exterior", lambda u, v: mid(v, u), nrm, u0, u1, ya + (0.02 if j > 0 else 0.0),
                                   yb - (0.02 if j < 2 else 0.0))
        # pilasters between the lights and at both ends, with capitals
        zs = [lights[0][0] - 0.2] + [(a[1] + b[0]) / 2 for a, b in zip(lights[:-1], lights[1:])] + [lights[-1][1] + 0.2]
        d.paint_tag = "stern-carvings"
        for zp in zs:
            d.add_box_mesh("exterior", "timber", tp((y0 + y1) / 2, zp, 0.05), (0.1, h + 0.45, 0.14), R)
            d.add_box_mesh("exterior", "timber", tp(y1 + 0.22, zp, 0.07), (0.14, 0.08, 0.22), R)
            d.add_box_mesh("exterior", "timber", tp(y0 - 0.2, zp, 0.07), (0.14, 0.08, 0.22), R)
        span = zs[-1] - zs[0] + 0.5
        d.paint_tag = "stern-gallery"
        # cornice over the row, and a sill rail under it
        d.add_box_mesh("exterior", "timber", tp(y1 + 0.31, 0.0, 0.08), (0.16, 0.12, span), R)
        d.add_box_mesh("exterior", "timber", tp(y1 + 0.42, 0.0, 0.05), (0.1, 0.06, span - 0.1), R)
        d.add_box_mesh("exterior", "timber", tp(y0 - 0.14, 0.0, 0.08), (0.16, 0.1, span), R)
        if row == 0:
            # under the great cabin's lights, a balustrade like a stern walk's (for show: there's no walk)
            d.paint_tag = "stern-carvings"
            ya, yb = y0 - 0.78, y0 - 0.34
            n2, u2 = transom_frame(ya, yb)
            R2 = np.column_stack([n2, u2, [0, 0, 1.0]])
            for yr in (ya, yb):
                c = np.array([stern_x(yr), yr, 0.0]) + n2 * 0.1
                d.add_box_mesh("exterior", "timber", c, (0.12, 0.08, span + 0.2), R2)
            for zb in np.arange(-span / 2 + 0.2, span / 2 - 0.1, 0.25):
                a = np.array([stern_x(ya), ya + 0.04, zb]) + n2 * 0.1
                b = np.array([stern_x(yb), yb - 0.04, zb]) + n2 * 0.1
                d.add_cylinder("exterior", "timber", a, b, 0.035, 0.035, sides=6)
                d.add_cylinder("exterior", "timber", (a + b) / 2 - u2 * 0.05, (a + b) / 2 + u2 * 0.05, 0.05, 0.05,
                               sides=6)


def stern_timbers(d):
    """
    (0.3.7, the user's note 16: "the back wall should have some vertical side frame members: how in real ships?") Inside
    the stern, in the great cabin and the poop cabin, the upright stern timbers the transom is planked on, standing
    proud of its inside planking: one between each two of the gallery's lights and one outboard of the outermost each
    side, from the deck to the beams over it, leaning with the transom's rake. (The frames at the transom's corners
    are the fashion pieces: framing.)
    """
    for (y0, y1, lights), floor, ceiling in zip(GALLERY, (MAIN, QUARTER), (QUARTER - DECK_T - 0.25, POOP - DECK_T - 0.25)):
        gaps = [(a[1] + b[0]) / 2 for a, b in zip(lights[:-1], lights[1:])]
        w = min(0.2, min(b[0] - a[1] for a, b in zip(lights[:-1], lights[1:])) - 0.04)
        zs = gaps + [lights[0][0] - 0.06 - w / 2, lights[-1][1] + 0.06 + w / 2]
        for zc in zs:
            secs = []
            for y in np.arange(floor, ceiling + 1e-6, 0.25):
                xo, zo = _ceil_ring(y)
                if len(xo) < 2 or float(zo[0]) < abs(zc) + w / 2 + 0.05:
                    continue
                xt = float(xo[0])
                secs.append([(xt - 0.02, y, zc - w / 2), (xt + 0.14, y, zc - w / 2), (xt + 0.14, y, zc + w / 2),
                             (xt - 0.02, y, zc + w / 2)])
            if len(secs) > 1:
                ruled_timber(d, "auto", "timber", secs)


# ==================================================================== windows in the castles' sides
def side_windows(d):
    """
    Each window between two frames, glazed with our own mesh that follows the hull's curve and its lean (tumblehome),
    like the stern gallery's lights: a casing outside, a mullion and two transoms, six panes, stained glass on top.
    """
    if GRID:
        side_lights(d)
        return
    stains = ["glass-red", "glass-blue", "glass-yellow"]
    for w, (x0, x1, y0, y1) in enumerate(SIDE_WINDOWS):
        xm, h = (x0 + x1) / 2, y1 - y0
        for s in (1, -1):
            def surf(x, y, depth):
                return np.array([x, y, s * (hw(x, y) - depth)])

            # the outward normal and the hull's up direction at the window's middle
            e = 0.02
            ym = (y0 + y1) / 2
            dzdx = (hw(xm + e, ym) - hw(xm - e, ym)) / (2 * e)
            dzdy = (hw(xm, ym + e) - hw(xm, ym - e)) / (2 * e)
            nrm = np.array([-dzdx, -dzdy, 1.0])
            nrm = nrm / np.linalg.norm(nrm) * np.array([1, 1, s])
            up = np.array([0, 1.0, s * dzdy])
            up /= np.linalg.norm(up)
            along = np.cross(up, nrm)
            R = np.column_stack([along, up, nrm])
            # the casing round it outside, and a sill
            # (0.3.10, clean surfaces: the head and sill between the jambs, 0.01 into them, 0.014 thinner)
            for yy in (y0 - 0.04, y1 + 0.04):
                d.add_box_mesh("exterior", "timber", surf(xm, yy, 0) + nrm * 0.028, (x1 - x0 + 0.02, 0.08, 0.056), R)
            for xx in (x0 - 0.04, x1 + 0.04):
                d.add_box_mesh("exterior", "timber", surf(xx, ym, 0) + nrm * 0.035, (0.08, h + 0.16, 0.07), R)
            d.add_box_mesh("exterior", "timber", surf(xm, y0 - 0.12, 0) + nrm * 0.06, (x1 - x0 + 0.3, 0.07, 0.14), R)
            # mullion and transoms, halfway through the hull's thickness
            dep = HULL_T * 0.55
            if GRID:
                # (0.3.12, the user's note 118: "windows looks too wide without middle post. could continue blue inlay
                # up as post") the mullion a post as broad as the stile under the window (a hair broader: 0.13), from
                # 0.01 down in the sill to 0.01 up in the head, from 0.02 outside the glass to 0.01 prouder of the ceiling
                # than the stile's face (not in its plane where it overlaps the stile's top); in the stile's zone
                o_, i_ = dep - 0.02, HULL_T + STRIP_PROUD + 0.01
                with d.paint("frame"):
                    d.add_box_mesh("auto", "timber", surf(xm, ym, (o_ + i_) / 2), (0.13, h + 0.02, i_ - o_), R)
            else:
                d.add_box_mesh("exterior", "timber", surf(xm, ym, dep), (0.04, h, 0.05), R)
            ys = [y0, y0 + h * 0.36, y0 + h * 0.68, y1]
            for yb in ys[1:-1]:
                if GRID:
                    # (0.3.13) each half a straight timber along its panes' edge (the chord of the side's curve the
                    # glass lies on, as flat as the panes and their lead cames: one straight across the window stood
                    # up to 0.06 off the glass at its ends where the side curves), its ends in the mullion post and
                    # 0.01 into the cut's sides
                    for xa_, xb_ in ((x0 - 0.01, xm), (xm, x1 + 0.01)):
                        a_, b_ = surf(xa_, yb, dep), surf(xb_, yb, dep)
                        nq = np.cross(b_ - a_, up)
                        nq = nq / np.linalg.norm(nq)
                        d.add_beam_mesh("exterior", "timber", a_, b_, 0.03, 0.04, up=np.cross(nq, b_ - a_))
                    continue
                d.add_box_mesh("exterior", "timber", surf(xm, yb, dep), (x1 - x0, 0.04, 0.03), R)
            for j, (ya, yb) in enumerate(zip(ys[:-1], ys[1:])):
                mat = stains[(w + (s > 0)) % 3] if j == 2 else "glass"
                for xa, xb in ((x0, xm), (xm, x1)):
                    pts = [surf(xa, ya, dep), surf(xb, ya, dep), surf(xb, yb, dep), surf(xa, yb, dep)]
                    _quad_facing(d, "glass", mat, pts, nrm, (0, 0, 16, 16))
                    if GRID:
                        # (0.3.13, the user's note 135) in lead cames, inside the mullion post (0.065 each side) and
                        # the transoms (0.02)
                        u0, u1 = (xa, xb - 0.065) if xa < xm else (xa + 0.065, xb)
                        # (on the pane's own flat quad, as the glass is: its corners on the side's curve)
                        q_ = [np.asarray(p_, float) for p_ in pts]

                        def Pq(u, v, q_=q_, xa=xa, xb=xb, ya=ya, yb=yb):
                            su, sv = (u - xa) / (xb - xa), (v - ya) / (yb - ya)
                            return (q_[0] * (1 - su) * (1 - sv) + q_[1] * su * (1 - sv) + q_[2] * su * sv
                                    + q_[3] * (1 - su) * sv)

                        nq = np.cross(q_[1] - q_[0], q_[3] - q_[0])
                        nq = nq / np.linalg.norm(nq) * (1 if np.dot(nq, nrm) > 0 else -1)
                        lead_cames(d, "auto", Pq, nq, u0, u1, ya + (0.02 if j > 0 else 0.0), yb - (0.02 if j < 2 else 0.0))


def side_timber(d, s, xa, xb, ya, yb, o0, o1, along, mat="timber", group="exterior"):
    """
    (0.3.14) A timber lying on the hull's side s outside, over x xa..xb and y ya..yb, between o0 and o1 out from its
    surface (along z, as the side windows' surf): one ruled timber along x or y (along) in pieces of at most 0.25, so it
    follows the side's curve and its lean (tumblehome) as a box set at one place on it can't.
    """
    n = max(1, int(math.ceil(((xb - xa) if along == "x" else (yb - ya)) / 0.25 - 1e-9)))
    secs = []
    for t in np.linspace(0.0, 1.0, n + 1):
        if along == "x":
            x = xa + (xb - xa) * t
            pts = [(x, ya, o0), (x, yb, o0), (x, yb, o1), (x, ya, o1)]
        else:
            y = ya + (yb - ya) * t
            pts = [(xa, y, o0), (xb, y, o0), (xb, y, o1), (xa, y, o1)]
        secs.append([(x_, y_, s * (hw(x_, y_) + o_)) for x_, y_, o_ in pts])
    ruled_timber(d, group, mat, secs)


def side_lights(d):
    """
    (0.3.14, the user's note 144: "these windows could be same shape and design as stern windows") The poop cabin's side
    windows (GRID) as the stern gallery's poop row: tall narrow lights (SIDE_WINDOWS, 0.6 by 1.5), each in a casing
    (head and sill between its jambs), a thin mullion and two transoms halfway through the hull's thickness, six panes in
    lead cames (lead_cames), stained glass in the top two; a pilaster between each two lights and at the row's ends, with
    a capital over and under it, a cornice over the row with a rail on it, and a sill rail under it. Where the stern's
    are boxes set in the transom's flat raked plane, the casing, pilasters and rails here follow the side's curve and
    lean (side_timber); the glass, mullion and transoms are flat across each light as 0.3.13's. All in the side windows'
    paint zone.
    """
    stains = ["glass-red", "glass-blue", "glass-yellow"]
    dep = HULL_T * 0.55                         # the glass halfway through the hull's thickness, as the stern's
    for s in (1, -1):
        for w, (x0, x1, y0, y1) in enumerate(SIDE_WINDOWS):
            xm, ym, h = (x0 + x1) / 2, (y0 + y1) / 2, y1 - y0

            def surf(x, y, depth):
                return np.array([x, y, s * (hw(x, y) - depth)])

            # the outward normal and the hull's up direction at the light's middle (for its flat glass, mullion and
            # transoms)
            e = 0.02
            dzdx = (hw(xm + e, ym) - hw(xm - e, ym)) / (2 * e)
            dzdy = (hw(xm, ym + e) - hw(xm, ym - e)) / (2 * e)
            nrm = np.array([-dzdx, -dzdy, 1.0])
            nrm = nrm / np.linalg.norm(nrm) * np.array([1, 1, s])
            up = np.array([0, 1.0, s * dzdy])
            up /= np.linalg.norm(up)
            along = np.cross(up, nrm)
            R = np.column_stack([along, up, nrm])
            # the casing round it: the jambs from 0.01 down in the sill rail to 0.08 over the light, 0.07 proud (0.01
            # into the planking behind), their inner faces 0.01 over the opening (not in the plane of its reveals'
            # sides); the head and sill between them (0.01 into them, as far over the opening), 0.014 less proud (as
            # the stern's), the head's top 0.005 under the jambs' (not in their plane)
            for xa_, xb_ in ((x0 - 0.08, x0 + 0.01), (x1 - 0.01, x1 + 0.08)):
                side_timber(d, s, xa_, xb_, y0 - 0.1, y1 + 0.08, -0.01, 0.07, "y")
            for ya_, yb_ in ((y0 - 0.095, y0 + 0.01), (y1 - 0.01, y1 + 0.075)):
                side_timber(d, s, x0, x1, ya_, yb_, -0.01, 0.056, "x")
            # the mullion and the transoms (0.36 and 0.68 up), the transoms through the mullion (its faces 0.01 further
            # out and in than theirs), 0.01 into the cut's sides
            d.add_box_mesh("exterior", "timber", surf(xm, ym, dep), (0.04, h, 0.05), R)
            ys = [y0, y0 + h * 0.36, y0 + h * 0.68, y1]
            for yb in ys[1:-1]:
                a_, b_ = surf(x0 - 0.01, yb, dep), surf(x1 + 0.01, yb, dep)
                nq = np.cross(b_ - a_, up)
                nq = nq / np.linalg.norm(nq)
                d.add_beam_mesh("exterior", "timber", a_, b_, 0.03, 0.04, up=np.cross(nq, b_ - a_))
            for j, (ya, yb) in enumerate(zip(ys[:-1], ys[1:])):
                mat = stains[(w + (s > 0)) % 3] if j == 2 else "glass"
                for xa, xb in ((x0, xm), (xm, x1)):
                    pts = [surf(xa, ya, dep), surf(xb, ya, dep), surf(xb, yb, dep), surf(xa, yb, dep)]
                    _quad_facing(d, "glass", mat, pts, nrm, (0, 0, 16, 16))
                    # in lead cames (note 145: small oblong panes), inside the mullion and the transoms (0.02 each side),
                    # on the pane's own flat quad, as the glass is (its corners on the side's curve)
                    u0, u1 = (xa, xb - 0.02) if xa < xm else (xa + 0.02, xb)
                    q_ = [np.asarray(p_, float) for p_ in pts]

                    def Pq(u, v, q_=q_, xa=xa, xb=xb, ya=ya, yb=yb):
                        su, sv = (u - xa) / (xb - xa), (v - ya) / (yb - ya)
                        return (q_[0] * (1 - su) * (1 - sv) + q_[1] * su * (1 - sv) + q_[2] * su * sv
                                + q_[3] * (1 - su) * sv)

                    nq = np.cross(q_[1] - q_[0], q_[3] - q_[0])
                    nq = nq / np.linalg.norm(nq) * (1 if np.dot(nq, nrm) > 0 else -1)
                    lead_cames(d, "auto", Pq, nq, u0, u1, ya + (0.02 if j > 0 else 0.0), yb - (0.02 if j < 2 else 0.0))
        # the row's pilasters (between each two lights and 0.2 outside the end ones: on the whole x, as the stern's
        # poop row's on the whole z), 0.14 broad and 0.1 proud, from 0.225 under the lights to 0.225 over them; a capital
        # over and under each, 0.22 broad and 0.14 proud; the cornice 0.16 proud over them (its underside 0.01 under
        # the capitals' tops), a rail on it, and a sill rail under the lights; the rails 0.25 on past the end pilasters
        lights = sorted(SIDE_WINDOWS)
        y0, y1 = lights[0][2], lights[0][3]
        xs_ = ([lights[0][0] - 0.2] + [(a[1] + b[0]) / 2 for a, b in zip(lights[:-1], lights[1:])] +
               [lights[-1][1] + 0.2])
        for xp in xs_:
            side_timber(d, s, xp - 0.07, xp + 0.07, y0 - 0.225, y1 + 0.225, -0.01, 0.1, "y")
            for yc in (y1 + 0.22, y0 - 0.2):
                side_timber(d, s, xp - 0.11, xp + 0.11, yc - 0.04, yc + 0.04, -0.01, 0.14, "x")
        xa_, xb_ = xs_[0] - 0.25, xs_[-1] + 0.25
        side_timber(d, s, xa_, xb_, y1 + 0.25, y1 + 0.37, -0.01, 0.16, "x")                 # the cornice
        side_timber(d, s, xa_ + 0.05, xb_ - 0.05, y1 + 0.36, y1 + 0.45, -0.01, 0.1, "x")    # its rail (0.01 into it)
        side_timber(d, s, xa_, xb_, y0 - 0.19, y0 - 0.09, -0.01, 0.16, "x")                 # the sill rail


# ==================================================================== the ship's name, across the top of the transom
def nameboard(d):
    """A carved board across the transom above the poop's lights, under the taffrail; the mod paints her name on it."""
    y0, y1 = 9.52, 10.02
    ym = (y0 + y1) / 2
    nrm, up = transom_frame(y0, y1)
    R = np.column_stack([nrm, up, [0, 0, 1.0]])
    width = 2 * min(transom_hw(y0), transom_hw(y1)) - 0.9
    if STERN_PORTS:
        # ((0.3.25) carrack-m: between the stern chasers' ports, its scrolls (0.3 past its ends) 0.05 clear of their lids)
        width = min(width, 2 * (min(abs(b) if a < 0 else a for a, b, *_ in STERN_PORTS) - 0.04 - 0.05 - 0.3))
    c = np.array([stern_x(ym), ym, 0.0]) + nrm * 0.03
    d.add_box_mesh("exterior", "tarred", c, (0.06, y1 - y0, width), R)
    # a carved frame round it, and a scroll at each end
    for dy in (-(y1 - y0) / 2 - 0.02, (y1 - y0) / 2 + 0.02):
        d.add_box_mesh("exterior", "timber", c + up * dy + nrm * 0.02, (0.06, 0.05, width + 0.1), R)
    for sgn in (1, -1):
        p = c + np.array([0, 0, sgn * (width / 2 + 0.03)]) + nrm * 0.02
        d.add_box_mesh("exterior", "timber", p + nrm * 0.01, (0.08, y1 - y0 + 0.1, 0.05), R)   # (0.01 proud of the rails)
        sc = p + np.array([0, 0, sgn * 0.14])
        d.add_cylinder("exterior", "timber", sc - up * 0.02 - nrm * 0.03, sc - up * 0.02 + nrm * 0.07, 0.13, 0.13,
                       sides=10, smooth=False)
        d.add_cylinder("exterior", "timber", sc - up * 0.02 + nrm * 0.07, sc - up * 0.02 + nrm * 0.09, 0.07, 0.07,
                       sides=8, smooth=False)
    face = c + nrm * 0.031
    d.nameboard = {"pos": face, "right": np.array([0, 0, 1.0]), "up": up, "normal": nrm, "width": width - 0.16,
                   "height": y1 - y0 - 0.1}


# ==================================================================== the figurehead
def tube(d, group, mat, pts, radii, sides=10, flat=None, caps=True):
    """
    A smooth tube along the points (a limb, a tail): radius radii[i] at pts[i] (flat[i] squashes it across, 1 = round).
    Its cross-sections turn with the path, square to it.
    """
    pts = [np.asarray(p, float) for p in pts]
    n = len(pts)
    flat = flat or [1.0] * n
    tangents = []
    for i in range(n):
        t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tangents.append(t / np.linalg.norm(t))
    ref = np.array([0, 0, 1.0]) if abs(tangents[0][2]) < 0.9 else np.array([1.0, 0, 0])
    normal = np.cross(ref, tangents[0])
    normal /= np.linalg.norm(normal)
    rings = []
    for i in range(n):
        normal = normal - np.dot(normal, tangents[i]) * tangents[i]
        normal /= np.linalg.norm(normal)
        binormal = np.cross(tangents[i], normal)
        ring = [pts[i] + radii[i] * (math.cos(a) * normal + flat[i] * math.sin(a) * binormal)
                for a in np.linspace(0, 2 * math.pi, sides + 1)]
        rings.append(ring)
    sid = f"tube{len(d.quads)}"
    for i in range(n - 1):
        for k in range(sides):
            q = [rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k]]
            out = (q[0] + q[1] + q[2] + q[3]) / 4 - (pts[i] + pts[i + 1]) / 2
            nq = np.cross(q[1] - q[0], q[3] - q[0])
            if np.dot(nq, out) < 0:
                q = [q[1], q[0], q[3], q[2]]
            d.add_quad(group, mat, q, (16 * k / sides, 0, 16 * (k + 1) / sides, 16), smooth=sid)
    if caps:
        for i, sgn in ((0, -1), (n - 1, 1)):
            if radii[i] < 1e-3:
                continue
            # (one flat face, textured by its plane: Design.add_planar)
            d.add_planar(group, mat, rings[i][:sides], tangents[i] * sgn, centre=pts[i])


def figurehead(d):
    """
    A gilded mermaid on the stem under the forecastle's overhang, leaning out over the sea: her tail curls down the
    stem to a spread fluke, her hair streams back, her arms reach forward. Changeable in the game (the fixture lists
    what there is); only she is carved so far.
    """
    g = "figurehead:mermaid"
    m = "gold"
    n0 = len(d.quads)       # carved where the stem was before the waist was lengthened, then moved forward with it
    # the tail, from the fluke low on the stem up to her hips, a little S in it
    tail = [(15.12, 2.42, 0.0), (15.22, 2.7, 0.0), (15.42, 3.08, 0.0), (15.7, 3.48, 0.0), (15.98, 3.86, 0.0),
            (16.2, 4.2, 0.0), (16.36, 4.5, 0.0)]
    tube(d, g, m, tail, [0.035, 0.07, 0.105, 0.14, 0.165, 0.18, 0.19], sides=12,
         flat=[0.7, 0.85, 0.95, 1.0, 1.05, 1.1, 1.15])
    for f, rr in ((0.3, 0.115), (0.5, 0.15), (0.7, 0.172)):     # a few bands of scales
        i = int(f * (len(tail) - 1))
        p0, p1 = np.array(tail[i]), np.array(tail[i + 1])
        c = p0 + (p1 - p0) * (f * (len(tail) - 1) - i)
        u = (p1 - p0) / np.linalg.norm(p1 - p0)
        tube(d, g, m, [c - u * 0.025, c + u * 0.025], [rr + 0.012, rr + 0.012], sides=12, flat=[1.05, 1.05])
    # the fluke: two thin lobes spreading sideways and curling up
    for sgn in (1, -1):
        tube(d, g, m, [(15.14, 2.44, 0.0), (15.05, 2.34, sgn * 0.16), (15.02, 2.36, sgn * 0.3), (15.06, 2.46, sgn * 0.38)],
             [0.05, 0.075, 0.06, 0.015], sides=8, flat=[0.35, 0.35, 0.35, 0.35])
    # her body: hips, a narrow waist, her chest and shoulders, wider than deep, leaning forward
    body = [(16.36, 4.5, 0.0), (16.5, 4.74, 0.0), (16.64, 4.98, 0.0), (16.76, 5.16, 0.0), (16.86, 5.3, 0.0),
            (16.93, 5.4, 0.0)]
    tube(d, g, m, body, [0.19, 0.145, 0.16, 0.165, 0.14, 0.08], sides=12, flat=[1.2, 1.3, 1.4, 1.5, 1.7, 1.2])
    # her neck and her head, tipped up to look ahead
    tube(d, g, m, [(16.92, 5.39, 0.0), (16.99, 5.5, 0.0), (17.03, 5.57, 0.0)], [0.06, 0.055, 0.06], sides=10)
    head = [(17.0, 5.56, 0.0), (17.04, 5.61, 0.0), (17.09, 5.68, 0.0), (17.14, 5.75, 0.0), (17.18, 5.81, 0.0),
            (17.2, 5.85, 0.0)]
    tube(d, g, m, head, [0.05, 0.095, 0.115, 0.11, 0.08, 0.02], sides=12, flat=[0.95, 0.95, 0.95, 0.95, 0.95, 0.95])
    # her hair, streaming back over her shoulders and down her back
    for dz in (0.0, 0.09, -0.09):
        tube(d, g, m, [(17.12, 5.8, dz * 0.6), (17.0, 5.77, dz), (16.86, 5.62, dz * 1.3), (16.74, 5.42, dz * 1.4),
                       (16.62, 5.2, dz * 1.2), (16.52, 5.02, dz * 0.8)], [0.05, 0.065, 0.07, 0.065, 0.05, 0.015],
             sides=8, flat=[0.55, 0.55, 0.55, 0.55, 0.55, 0.55])
    # her arms, reaching forward over the sea
    for sgn in (1, -1):
        arm = [(16.9, 5.36, sgn * 0.2), (17.02, 5.24, sgn * 0.27), (17.14, 5.17, sgn * 0.29), (17.3, 5.22, sgn * 0.25),
               (17.44, 5.3, sgn * 0.2), (17.5, 5.33, sgn * 0.18)]
        tube(d, g, m, arm, [0.05, 0.045, 0.04, 0.036, 0.03, 0.025], sides=8)
        tube(d, g, m, [(17.49, 5.32, sgn * 0.185), (17.54, 5.35, sgn * 0.17), (17.58, 5.36, sgn * 0.16)],
             [0.03, 0.035, 0.01], sides=8, flat=[0.6, 0.6, 0.6])
    # the bracket that holds her to the stem
    d.add_beam_mesh("exterior", "tarred", (15.5, 4.15, 0.0), (16.28, 4.52, 0.0), 0.16, 0.14)
    d.add_beam_mesh("exterior", "tarred", (15.72, 4.82, 0.0), (16.45, 4.95, 0.0), 0.12, 0.12)
    for q in d.quads[n0:]:
        q["pts"] = [(p[0] + STRETCH, p[1], p[2]) for p in q["pts"]]
    if PROPOSAL:
        # under the raked forecastle she's four fifths the size and lower along the stem, clear of it and of the sea
        f0 = np.array([15.12 + STRETCH, 2.42, 0.0])
        f1 = np.array([f0[0] - 0.15, WATER + 0.5, 0.0])
        for q in d.quads[n0:]:
            q["pts"] = [tuple(f1 + 0.8 * (np.asarray(p, float) - f0)) for p in q["pts"]]
    worst = max(p[1] - float(fc_under(p[0])) for q in d.quads[n0:] for p in q["pts"])
    if worst > -0.05:
        print(f"figurehead: reaches {worst:+.2f} into the forecastle's underside")
    d.figureheads = [{"code": "mermaid", "group": g}, {"code": "none", "group": None}]


# ==================================================================== the captain's cabin
def captain_cabin(d, captain_bed):
    # the chart on the table (a vanilla table), with dividers and an inkwell
    ty = MAIN + 1.0
    d.add_quad("auto", "paper", [(-10.62, ty + 0.004, -1.28), (-9.9, ty + 0.004, -1.34), (-9.86, ty + 0.004, -0.78),
                                 (-10.58, ty + 0.004, -0.72)], (0, 0, 16, 16))
    for dz in (-0.03, 0.03):
        d.add_cylinder("auto", "iron", (-10.1, ty + 0.012, -0.95), (-9.9, ty + 0.012, -0.95 + dz * 4), 0.008, 0.008,
                       sides=4)
    d.add_cylinder("auto", "black", (-10.5, ty, -0.62), (-10.5, ty + 0.07, -0.62), 0.045, 0.04, sides=8, smooth=False)
    d.add_cylinder("auto", "cloth", (-10.48, ty + 0.06, -0.62), (-10.4, ty + 0.2, -0.58), 0.008, 0.02, sides=4)


# ==================================================================== the cabins' fronts
def front_dress(d):
    """
    (0.3.7) The windows either side of the doors in the great cabin's and the poop cabin's front bulkheads (the user's
    notes 17 and 18: "would it be realistic to get some windows on this wall?"), glazed like the cabins' other windows (a
    mullion, two transoms, six panes, stained glass on top) in the middle of the wall, cased both sides, a sill outside;
    and the great cabin's front toward the waist dressed as a captain's quarters were: a pilaster each side of each
    window with a base and a capital, a cornice along its head and a rail along it at the windows' sill, panels
    under it.
    """
    stains = ["glass-red", "glass-blue", "glass-yellow"]
    h0, h1 = FRONT_WINDOW_Y
    for room, x0, x1, yb, dress in (("greatcabin", WAIST_AFT - 0.25, WAIST_AFT, MAIN, True),
                                    ("poopcabin", POOP_FRONT - 0.25, POOP_FRONT, QUARTER, False)):
        xm = (x0 + x1) / 2
        ya, yt = yb + h0, yb + h1
        for za, zb in FRONT_WINDOWS[room]:
            s = 1 if za > 0 else -1
            zc = (za + zb) / 2
            if GRID:
                front_window_grid(d, room, x0, x1, yb, za, zb, s)
                continue
            # casings both sides, the sill outside (toward the bow)
            for face, sgn in ((x0, -1), (x1, 1)):
                cx = face + sgn * 0.025
                d.paint_tag = "cabin-trim" if sgn < 0 else "front-mouldings"
                for zz in (za - 0.04, zb + 0.04):
                    d.add_box_mesh("auto", "timber", (cx, (ya + yt) / 2, zz), (0.05, yt - ya + 0.16, 0.08))
                # (0.3.10, clean surfaces: the head and sill between the jambs, 0.01 into them, 0.012 thinner)
                for yy in (ya - 0.04, yt + 0.04):
                    d.add_box_mesh("auto", "timber", (cx - sgn * 0.006, yy, zc), (0.038, 0.08, zb - za + 0.02))
            d.paint_tag = "front-mouldings"
            if not dress:
                d.add_box_mesh("auto", "timber", (x1 + 0.06, ya - 0.11, zc), (0.12, 0.06, zb - za + 0.26))
            # the mullion, the transoms and the panes
            ys = [ya, ya + (yt - ya) * 0.36, ya + (yt - ya) * 0.68, yt]
            d.add_box_mesh("auto", "timber", (xm, (ya + yt) / 2, zc), (0.05, yt - ya, 0.04))
            for yy in ys[1:-1]:
                d.add_box_mesh("auto", "timber", (xm, yy, zc), (0.03, 0.04, zb - za))
            for j, (y0, y1) in enumerate(zip(ys[:-1], ys[1:])):
                mat = stains[(j + (s > 0) + (yb > MAIN)) % 3] if j == 2 else "glass"
                for p0, p1 in ((za, zc), (zc, zb)):
                    _quad_facing(d, "glass", mat, [(xm, y0, p0), (xm, y0, p1), (xm, y1, p1), (xm, y1, p0)], (1, 0, 0),
                                 (0, 0, 16, 16))
        if not dress:
            continue
        # the great cabin's front toward the waist: pilasters beside the windows (clear of the door's casing and the
        # quarterdeck stair), a cornice under the breastwork's plinth, a rail at the windows' sill and a panel under each
        face = x1
        top = QUARTER - DECK_T
        half = min(ceil_z(face + 0.01, yb + 0.5), ceil_z(face + 0.01, top - 0.2)) - 0.05
        if GRID:
            # (0.3.12, the user's note 122: "decorations on wall is mess now": stub rails, a pilaster crowding the door,
            # the window off-centre) laid out in bays: a pilaster each side of the door clear of its casing, the port bay
            # closed by one as far outboard of the window as the door's is inboard of it (the window in its middle), the
            # starboard bay by one against the quarterdeck stair (the crew plaque in its middle, PLAQUE); the rail at
            # the windows' sill across each bay from pilaster to pilaster (into their shafts' sides), and outboard of
            # the port bay on to the side; a panel under the window and one under the plaque; the cornice from the side
            # to the stair
            st_z = next((st["steps"][0][3] for st in d.stairs if st["code"] == "quarterdeck"), 2.5)
            pil = FRONT_PILASTERS
            for zp in pil:
                d.add_box_mesh("auto", "timber", (face + 0.025, (yb + top) / 2, zp), (0.05, top - yb, 0.16))        # shaft
                d.add_box_mesh("auto", "timber", (face + 0.04, yb + 0.12, zp), (0.08, 0.24, 0.22))                 # base
                d.add_box_mesh("auto", "timber", (face + 0.045, top - 0.32, zp), (0.09, 0.08, 0.22))               # capital
                d.add_box_mesh("auto", "timber", (face + 0.035, top - 0.26, zp), (0.07, 0.04, 0.19))
            yr = ya - 0.11
            side_r = ceil_z(face + 0.05, yr) + 0.02               # (the rail's end 0.02 into the side's planking)
            # (0.3.13, note 138) the starboard bay from its pilaster to the stair's inboard stringer; outboard of the
            # companion's door (its casing to dz + 0.615) on under the plaque to the side
            st_out = st_z + 1.0
            dz_c = (COMPANION[2] + COMPANION[3]) / 2
            bays = [(-side_r, pil[0] - 0.07), (pil[0] + 0.07, pil[1] - 0.07),
                    (pil[2] + 0.07, (pil[3] - 0.07) if len(pil) > 3 else st_z - 0.02), (dz_c + 0.63, side_r)]
            for za, zb in bays:
                if zb - za > 0.1:
                    d.add_box_mesh("auto", "timber", (face + 0.05, yr, (za + zb) / 2), (0.1, 0.06, zb - za))
            yl, yh = yb + 0.28, ya - 0.3
            for a_, b_ in ((pil[0], pil[1]),) + (((pil[2], pil[3]),) if len(pil) > 3 else ()):
                p0, p1 = a_ + 0.21, b_ - 0.21
                for yy in (yl, yh):
                    d.add_box_mesh("auto", "timber", (face + 0.015, yy, (p0 + p1) / 2), (0.03, 0.04, p1 - p0))
                for zz in (p0 + 0.02, p1 - 0.02):
                    d.add_box_mesh("auto", "timber", (face + 0.015, (yl + yh) / 2, zz), (0.03, yh - yl - 0.04, 0.04))
            # the cornice from the side to the stair, and (0.3.13) on from the stair's outboard stringer over the
            # companion's door to the side
            for za, zb in ((-half, st_z - 0.02), (st_out + 0.03, half)):
                d.add_box_mesh("auto", "timber", (face + 0.05, top - 0.12, (za + zb) / 2), (0.1, 0.16, zb - za))  # cornice
                d.add_box_mesh("auto", "timber", (face + 0.075, top - 0.03, (za + zb) / 2), (0.15, 0.06, zb - za + 0.04))
            continue
        stair = 2.15                                     # (the quarterdeck stair stands from 2.25 out, starboard)
        lim = {1: stair, -1: half}
        if GRID:
            # (the quarterdeck stair from 2.5 out; to port the companion's door from 3.5 out, its casing 0.12 more)
            lim = {1: 2.45, -1: -(COMPANION[3] + 0.56 + 0.06) - 0.1}
            if COMP_S > 0:
                # (0.3.10: the quarterdeck stair from 1.5 out to starboard, in front of where its window was)
                lim = {1: 1.45, -1: half}
        w1 = max(abs(v) for w in FRONT_WINDOWS[room] for v in w)    # (the great cabin's are alike either side)
        win_sides = {1 if w[0] > 0 else -1 for w in FRONT_WINDOWS[room]}
        for s in (1, -1):
            for zp in (s * 0.8, s * (w1 + 0.25)):
                if GRID and (abs(zp) + 0.11 > lim[s] or (abs(zp) > 1.0 and s not in win_sides)):
                    continue
                d.add_box_mesh("auto", "timber", (face + 0.025, (yb + top) / 2, zp), (0.05, top - yb, 0.16))        # shaft
                d.add_box_mesh("auto", "timber", (face + 0.04, yb + 0.12, zp), (0.08, 0.24, 0.22))                 # base
                d.add_box_mesh("auto", "timber", (face + 0.045, top - 0.32, zp), (0.09, 0.08, 0.22))               # capital
                d.add_box_mesh("auto", "timber", (face + 0.035, top - 0.26, zp), (0.07, 0.04, 0.19))
            # the rail at the windows' sill, from the door's casing to the stair (it's the windows' sill too)
            za, zb = sorted((s * 0.7, s * min(half, lim[s] if GRID else stair)))
            d.add_box_mesh("auto", "timber", (face + 0.05, ya - 0.11, (za + zb) / 2), (0.1, 0.06, zb - za))
            # a panel under each window, between the pilasters
            if GRID and s not in win_sides:
                continue
            p0, p1 = sorted((s * 0.96, s * (w1 + 0.09)))
            yl, yh = yb + 0.28, ya - 0.3
            for yy in (yl, yh):
                d.add_box_mesh("auto", "timber", (face + 0.015, yy, (p0 + p1) / 2), (0.03, 0.04, p1 - p0))
            for zz in (p0 + 0.02, p1 - 0.02):
                d.add_box_mesh("auto", "timber", (face + 0.015, (yl + yh) / 2, zz), (0.03, yh - yl - 0.04, 0.04))
        za, zb = -half, min(half, stair)
        d.add_box_mesh("auto", "timber", (face + 0.05, top - 0.12, (za + zb) / 2), (0.1, 0.16, zb - za))          # cornice
        d.add_box_mesh("auto", "timber", (face + 0.075, top - 0.03, (za + zb) / 2), (0.15, 0.06, zb - za + 0.04))


def front_window_grid(d, room, x0, x1, yb, za, zb, s):
    """
    (0.3.13, the user's note 135: "window looks too big as its decorative frame should be inside the 1 block dimensions.
    Also all windows could have lead dividers") A cabin front's window on the grid: its casing inside its 1-block
    opening, not round it: jambs, head and sill lining the opening's sides 0.09 broad, standing 0.03 proud of the wall's
    faces either side (the outside sill 0.06 prouder, a nose for the rain; the poop cabin's sill under the opening gone),
    the glass in the middle of the wall's thickness inside them, a mullion and two transoms, six panes in lead cames
    (lead_cames), stained glass in the top two. The casing, mullion and transoms are the windows' paint zone
    (window-casings), both faces.
    """
    stains = ["glass-red", "glass-blue", "glass-yellow"]
    h0, h1 = FRONT_WINDOW_Y
    ya, yt = yb + h0, yb + h1
    xm = (x0 + x1) / 2
    zc = (za + zb) / 2
    cw = 0.09                                   # the casing's breadth inside the opening
    ia, ib, ja, jb = za + cw, zb - cw, ya + cw, yt - cw
    with d.paint("window-casings"):
        for face, sgn in ((x0, -1), (x1, 1)):
            # each side's pieces from 0.02 past the wall's middle (into the other side's) to 0.03 proud of its face; the
            # jambs the opening's height (0.01 into its top and bottom), the head and sill between them (0.01 into
            # them), 0.006 less proud and short of the middle by 0.01 (not in the jambs' planes)
            xa_, xb_ = xm - sgn * 0.025, face + sgn * 0.03
            # ((0.3.14) the outer side's faces round the glass 0.003 back from the inner side's: where the two cross in
            # the wall's middle they lay in one plane, z-fighting seen through the lead's bigger panes, note 145)
            bk = 0.003 if sgn > 0 else 0.0
            for z_lo, z_hi in ((za - 0.01, ia - bk), (ib + bk, zb + 0.01)):
                d.add_box_mesh("auto", "timber", ((xa_ + xb_) / 2, (ya + yt) / 2, (z_lo + z_hi) / 2),
                               (abs(xb_ - xa_), yt - ya + 0.02, z_hi - z_lo))
            xa_, xb_ = xm - sgn * 0.015, face + sgn * 0.024
            for y_lo, y_hi in ((ya - 0.01, ja - bk), (jb + bk, yt + 0.01)):
                d.add_box_mesh("auto", "timber", ((xa_ + xb_) / 2, (y_lo + y_hi) / 2, zc),
                               (abs(xb_ - xa_), y_hi - y_lo, ib - ia + 0.02))
        # the outside sill's nose: 0.06 prouder than the casing, as broad as the opening less 0.01 each end, its top
        # 0.01 under the sill piece's
        xs_ = x1 + 0.024 + 0.03
        d.add_box_mesh("auto", "timber", (xs_, ja - 0.045, zc), (0.06 + 0.004, 0.07, zb - za - 0.02))
        # the mullion and the transoms
        ys = [ja, ja + (jb - ja) * 0.36, ja + (jb - ja) * 0.68, jb]
        d.add_box_mesh("auto", "timber", (xm, (ja + jb) / 2, zc), (0.05, jb - ja + 0.02, 0.04))
        for yy in ys[1:-1]:
            d.add_box_mesh("auto", "timber", (xm, yy, zc), (0.03, 0.04, ib - ia + 0.02))

    def P(u, v):
        return (xm, v, u)

    for j, (y0, y1) in enumerate(zip(ys[:-1], ys[1:])):
        mat = stains[(j + (s > 0) + (yb > MAIN)) % 3] if j == 2 else "glass"
        for p0, p1 in ((ia - 0.01, zc), (zc, ib + 0.01)):
            g0, g1 = (y0 - 0.01 if j == 0 else y0), (y1 + 0.01 if j == 2 else y1)
            _quad_facing(d, "glass", mat, [(xm, g0, p0), (xm, g0, p1), (xm, g1, p1), (xm, g1, p0)], (1, 0, 0),
                         (0, 0, 16, 16))
            # the glass that shows: inside the casing, the mullion (0.02 each side) and the transoms (0.02)
            u0, u1 = (ia, zc - 0.02) if p0 < zc else (zc + 0.02, ib)
            lead_cames(d, "auto", P, (1.0, 0, 0), u0, u1, y0 + (0.02 if j > 0 else 0.0), y1 - (0.02 if j < 2 else 0.0))


# ==================================================================== the entry ports' gates
def entry_gates(d):
    """
    Each entry port is closed by a hinged section of the bulwark itself: planked outside and in, following the hull's
    curve, with its own piece of rail cap. It swings inboard on hinges at its forward edge (the mod turns it), hung on
    the frame that goes on up over the rail as the boat gallows' forward post.
    """
    x0, x1 = PORT_X[0] + 0.01, PORT_X[1] - 0.01
    levels = [MAIN + 0.25 * k for k in range(int(round(WAIST_RAIL / 0.25)) + 1)]
    from planking import BANDS
    xs = np.linspace(x0, x1, 5)
    for s, side in ((1, "starboard"), (-1, "port")):
        grp = f"part:gate-{side}"

        def P(x, y, inner):
            return (x, y, s * (hw(x, y) - (HULL_T if inner else 0.0)))

        for j in range(len(levels) - 1):
            ya, yb = levels[j], levels[j + 1]
            for k in range(4):
                xa, xb = xs[k], xs[k + 1]
                # (0.3.8: one plank the gate's length, a plank texture across it, its pieces all of one texture; it ran
                # on past the texture's edge in its last piece, u to 19.8 of 16, into whatever the atlas has beside it,
                # and each piece had a texture of its own: the "weird color patch", the user's note 48)
                ua, ub = (xa - x0) / (x1 - x0) * 16, (xb - x0) / (x1 - x0) * 16
                # each face shows the planks of the side it's part of: the outside strakes, or the inside planking's
                d.paint_tag = {"tarred": "outside", "oak": "waist-bulwarks"}
                for inner, mat, band in ((False, "tarred", d.gate_band0 + j), (True, "oak", d.gate_band0_in + j)):
                    v0, v1 = BANDS[band % 3]
                    pts = [P(xa, ya, inner), P(xb, ya, inner), P(xb, yb, inner), P(xa, yb, inner)]
                    outward = (s > 0) != inner
                    uv = (ua, v0, ub, v1)
                    if not outward:
                        pts = [pts[1], pts[0], pts[3], pts[2]]
                        uv = (ub, v0, ua, v1)
                    d.add_quad(grp, mat, pts, uv, row=band, variant=band + (2 if inner else 0))
        # the ends and the bottom through the thickness (the ends tarred outside like the side, so the hair of a gap at
        # a shut gate's edge doesn't show a light line; the bottom closes it when it's open)
        d.paint_tag = {"tarred": "topsides", "oak": "waist-bulwarks"}
        for x, sgn in ((x0, -1), (x1, 1)):
            q = [P(x, MAIN, False), P(x, MAIN, True), P(x, MAIN + WAIST_RAIL, True), P(x, MAIN + WAIST_RAIL, False)]
            n = np.cross(np.subtract(q[1], q[0]), np.subtract(q[3], q[0]))
            if n[0] * sgn < 0:
                q = [q[1], q[0], q[3], q[2]]
            reveal(d, *q, "tarred", group=grp)
        for k in range(4):
            xa, xb = xs[k], xs[k + 1]
            q = [P(xa, MAIN + 0.005, False), P(xb, MAIN + 0.005, False), P(xb, MAIN + 0.005, True), P(xa, MAIN + 0.005, True)]
            _quad_facing(d, grp, "oak", q, (0, -1, 0))
        # its piece of the rail cap
        d.paint_tag = "rail-caps"
        for k in range(4):
            xa, xb = xs[k], xs[k + 1]
            ya = MAIN + WAIST_RAIL + 0.02
            za_o, zb_o = hw(xa, ya - 0.1) + 0.07, hw(xb, ya - 0.1) + 0.07
            za_i, zb_i = hw(xa, ya - 0.1) - HULL_T - 0.08, hw(xb, ya - 0.1) - HULL_T - 0.08
            for pts in ([(xa, ya, s * za_o), (xb, ya, s * zb_o), (xb, ya, s * zb_i), (xa, ya, s * za_i)],
                        [(xa, ya - 0.25, s * za_o), (xb, ya - 0.25, s * zb_o), (xb, ya, s * zb_o), (xa, ya, s * za_o)],
                        [(xb, ya - 0.2, s * zb_i), (xa, ya - 0.2, s * za_i), (xa, ya, s * za_i), (xb, ya, s * zb_i)]):
                n = np.cross(np.subtract(pts[1], pts[0]), np.subtract(pts[3], pts[0]))
                c = np.mean(pts, axis=0)
                want = np.array([0, 1.0, 0]) if abs(pts[0][1] - pts[3][1]) < 1e-6 else np.array([0, 0, np.sign(c[2]) * (1 if abs(c[2]) > abs(hw(c[0], c[1])) - 0.05 else -1)])
                ua, ub = (xa - x0) / (x1 - x0) * 16, (xb - x0) / (x1 - x0) * 16
                uv = (ua, 5.5, ub, 11)
                if np.dot(n, want) < 0:
                    pts = [pts[1], pts[0], pts[3], pts[2]]
                    uv = (ub, 5.5, ua, 11)
                d.add_quad(grp, "oak", pts, uv, row=1, variant=1)
        if GRID:
            # (0.3.9, the user's note 63: "open gate side shows empty and lacks side surface") its piece of cap closed
            # at both ends, through its section (outboard face's foot, top outboard, top inboard, inboard face's foot)
            ya = MAIN + WAIST_RAIL + 0.02
            for x, sgn in ((x0, -1), (x1, 1)):
                zh = hw(x, ya - 0.1)
                sec = [(x, ya - 0.25, s * (zh + 0.07)), (x, ya, s * (zh + 0.07)), (x, ya, s * (zh - HULL_T - 0.08)),
                       (x, ya - 0.2, s * (zh - HULL_T - 0.08))]
                # (and down its inboard face's foot to the gate's planking: the bulwark's thickness under the cap is
                # closed by the gate's ends)
                _quad_facing(d, grp, "oak", sec, (sgn, 0, 0), (0, 5.5, 6, 11))
        # hinges and a latch, inside: black forged iron
        d.paint_tag = None
        xh_, xl_ = (x0 + 0.14, x1 - 0.1) if GATE_HINGE_AFT else (x1 - 0.14, x0 + 0.1)
        for yh in (MAIN + 0.2, MAIN + WAIST_RAIL - 0.2):
            p = np.array(P(xh_, yh, True))
            d.add_box_mesh(grp, "black", p - np.array([0, 0, s * 0.012]), (0.26, 0.045, 0.02))
        p = np.array(P(xl_, MAIN + 0.55, True))
        d.add_box_mesh(grp, "black", p - np.array([0, 0, s * 0.02]), (0.04, 0.1, 0.035))
        # it turns about its forward edge's inboard corner (gate_hinge; the middle of its thickness until 0.3.8), where
        # the hinges' knuckles are, on pintles in the frame
        hx, hz = gate_hinge(s)
        for yh in (MAIN + 0.2, MAIN + WAIST_RAIL - 0.2):
            d.add_cylinder("exterior", "black", (hx, yh - 0.05, hz), (hx, yh + 0.05, hz), 0.022, 0.022, sides=6,
                           smooth=False)
        d.moving_parts.append({"code": f"gate-{side}", "axisPoint": [round(float(hx), 4), MAIN + 0.5, round(float(hz), 4)],
                               "axisDir": [0.0, 1.0, 0.0], "source": f"open:gate-{side}",
                               "angle": GATE_DIR * GATE_OPEN * s})


# ==================================================================== the waist guns
def side_angle(x, y):
    """
    (0.3.27, note 205: "guns at the bow where the side is curving have a limited arc as they would on flat sides") How far
    forward of square out her side's normal points at (x, y) (degrees, + toward the bow, either side); 0 where it's within
    3° of square (her parallel waist), rounded to a half degree. A gun's train is centred on it.
    """
    e = 0.05
    dz = (float(hw(np.array(x + e), np.array(y))) - float(hw(np.array(x - e), np.array(y)))) / (2 * e)
    a = math.degrees(math.atan(-dz))
    return 0.0 if abs(a) < 3.0 else round(a * 2) / 2


def guns(d):
    """
    The waist guns (item 16): a bronze gun on its truck carriage at each gunport (gun.py), run out with its carriage's
    fore end close to the side and its muzzle out of the port, laid square out of her side. Each is a fixture (held left
    mouse by it lays it where you look, right-click fires it; the gun captain's place behind the breech) whose collision is
    where it stands run out, and its moving parts: it trains about an upright in its port ("train:<code>"), runs back
    when it's fired ("fire:<code>"), its barrel elevates on its trunnions ("elevate:<code>"); its trucks turn as it rolls,
    its breeching (from ring bolts beside the port round the cascabel) stretches. Also the cannon ball the mod draws in
    flight (group "shot", at her origin, not drawn with her).
    """
    import gun
    gun.add_shot(d, "shot", (0.0, WATER, 0.0))
    placed = []
    # (0.3.21, step C) each gun: its code, side, x, the deck it stands on, its port's sill and lintel over the deck, its
    # lid's inside paint zone; the waist's, then (carrack-m) the castles' (CASTLE_GUNS)
    specs = []
    for s, side in ((-1, "port"), (1, "starboard")):
        specs += [(f"gun-{side}-{i + 1}", s, gx, MAIN, GUNPORT_Y, "waist-bulwarks") for i, gx in enumerate(GUN_X)]
    d.castle_guns = []
    for s, side in ((-1, "port"), (1, "starboard")):
        # ((0.3.23) each on the sides it's given)
        specs += [(f"gun-{side}-{name}", s, gx, deck, py, zone) for name, gx, deck, py, zone, sides in CASTLE_GUNS
                  if s in sides]
        d.castle_guns += [f"gun-{side}-{name}" for name, gx, deck, py, zone, sides in CASTLE_GUNS if s in sides]
    d.gun_levels = {}
    d.gun_phi = {}
    for code, s, gx, deck, py, zone in specs:
            ay = deck + gun.AXIS_Y
            # ((0.3.27, note 205) its train centred on her side's normal at its port)
            phi = side_angle(gx, ay) if MODULAR else 0.0
            # ((0.3.30, note 237) it stands along that normal: its port's jambs square to her side (PORT_SQUARE), its
            # "square" (train 0) the side board's perpendicular, its train even about it)
            phi = port_phi(gx, deck) if PORT_SQUARE else phi
            d.gun_phi[code] = phi
            # the trunnions GUN_INSET in from the inside planking at their height
            inner = ceil_z(gx, ay)
            oz = inner - GUN_INSET
            origin = np.array([gx, deck, s * oz])
            out = np.array([0.0, 0.0, float(s)])
            skew = port_skew(gx, deck) if PORT_SQUARE else 0.0
            if PORT_SQUARE and abs(phi) > 1e-9:
                # (the normal at the port's outside middle; the trunnions HULL_T + GUN_INSET in along it)
                out = np.array([math.sin(math.radians(phi)), 0.0, s * math.cos(math.radians(phi))])
                o_out = np.array([gx, deck, s * hw(gx, ay)])
                origin = o_out - out * (HULL_T + GUN_INSET)
                origin[1] = deck
            # ring bolts on the inside planking either side of the port, over the barrel, under the rail cap (0.3.8: 0.14
            # higher, so the breeching clears the trucks on its way to the cheeks' eyebolts run back). ((0.3.21) under a
            # castle deck's rail cap, 0.75 up: 0.62 up; (0.3.23) the castles' caps 1.0 up as the waist's: 0.8 up there too)
            rby = deck + min(0.80, py[1] - 0.13)
            # ((0.3.29, the bow guns' full train) on a castle's side 0.75 either side of the port: the breeching led from
            # further aft and forward, clear of the carriage trained further)
            bdx = 0.75 if MODULAR and deck > MAIN + 0.01 else 0.55
            bolts = [np.array([gx + skew + dx, rby, s * ceil_z(gx + skew + dx, rby)]) for dx in (-bdx, bdx)]
            first = len(d.quads)
            # it trains about an upright in the middle of the side's thickness
            n_el = len(d.elements)
            info = gun.add_gun(d, origin, out, code, ring_bolts=bolts, pivot_x=GUN_INSET + HULL_T / 2)
            d.moving_parts.extend(gun.moving_parts(code, info))
            lid = port_lid(d, code, s, gx, deck, py, zone) if LIDDED else None
            if (PORT_PANELS or GUN_PORT_SLOTS) and deck > MAIN + 0.01:
                # ((0.3.29) a castle gun's own meshes (its parts, its lid) and its ring bolts on her side: its port's)
                for q in d.quads[first:]:
                    if q["group"].startswith("part:"):
                        q["port_of"] = code
                    elif q["mat"] == "iron" and q["group"] == "exterior":
                        q["port_bolt"] = code
                for e_ in d.elements[n_el:]:
                    e_["port_of"] = code
            mine = [q for q in d.quads[first:] if q["group"].startswith(f"part:{code}") and "breeching" not in q["group"]]
            placed.append((code, s, gx, info, mine))
            d.gun_levels[code] = (deck, deck + py[0], deck + py[1])
            # where it stands (solid: nobody walks through it), what's clicked (wider: it's clicked trained too), where
            # the shot comes out of it, the port it's laid through and the side's faces there (out from the centreline)
            # (0.3.8: 0.3 shorter, as the gun: its button and its stool bed's end 1.1 inboard of the trunnions)
            za, zb = sorted((s * (oz - 1.15), s * inner))
            sa, sb = sorted((s * (oz - 1.3), s * inner))
            box = [gx - GUNPORT_W / 2, deck, za, gx + GUNPORT_W / 2, deck + 0.75, zb]
            sel = [gx - 0.55, deck, sa, gx + 0.55, deck + 0.85, sb]
            yaw_ = 270.0 if s > 0 else 90.0
            if abs(out[0]) > 1e-9:
                # ((0.3.30) along the normal: its footprints (as square: from the inside planking in, either side of its
                # line) turned with it, their boxes round them; its yaw the way it points)
                inn = origin + out * GUN_INSET
                tg = np.array([out[2], 0.0, -out[0]])

                def fbox(half, depth, top):
                    cs = [inn + tg * a_ - out * t_ for a_ in (-half, half) for t_ in (0.0, depth)]
                    xs_, zs_ = [c_[0] for c_ in cs], [c_[2] for c_ in cs]
                    return [min(xs_), deck, min(zs_), max(xs_), deck + top, max(zs_)]

                box = fbox(GUNPORT_W / 2, GUN_INSET + 1.15, 0.75)
                sel = fbox(0.55, GUN_INSET + 1.3, 0.85)
                yaw_ = math.degrees(math.atan2(-out[2], out[0])) % 360.0
            y0, y1 = deck + py[0], deck + py[1]
            d.fixtures.append({"type": "gun", "code": code, "pos": [round(float(v), 4) for v in origin],
                               "yaw": round(yaw_, 3), "sel": [round(float(v), 4) for v in sel],
                               "box": [round(float(v), 4) for v in box],
                               "muzzle": [round(float(v), 4) for v in info["muzzle"]], "out": [round(float(v), 6) for v in out],
                               "elevation": GUN_ELEVATION, "material": "bronze", "bore": gun.BORE, "shot": 4.1,
                               "trunnion": [round(float(v), 4) for v in info["trunnion"]],
                               "pivot": [round(float(v), 4) for v in info["pivot"]],
                               "seat": [round(float(v), 4) for v in info["seat"]],
                               "trainLimits": [-GUN_TRAIN, GUN_TRAIN] if MODULAR else [phi - GUN_TRAIN, phi + GUN_TRAIN], "elevationLimits": list(GUN_ELEVATION_LIMITS),
                               "trainRate": GUN_RATES[0], "elevationRate": GUN_RATES[1],
                               "port": [gx - GUNPORT_W / 2 + min(skew, 0.0), y0, gx + GUNPORT_W / 2 + max(skew, 0.0), y1],
                               "wall": [round(ceil_z(gx, y0), 4), round(ceil_z(gx, y1), 4), round(hw(gx, y0), 4), round(hw(gx, y1), 4)]})
            if lid:
                # (0.3.8) its port's lid (the moving part, opened by "open:<gun>"), and how far it's run back housed (its
                # muzzle in behind the shut lid)
                d.fixtures[-1].update({"lid": f"{code}-lid", "housed": gun.RECOIL})
    gun_train_limits(d, placed)
    # ((0.3.25, note 179) the stern chasers: on the poop deck, laid square out through the transom, aft)
    stern = stern_guns(d)
    if PORT_SQUARE:
        sweep_gun_limits(d, stern)          # ((0.3.30, note 237) the stern chasers' arcs swept too)
    d.guns_placed = placed + stern
    if d.castle_guns:
        castle_gun_limits(d, [e for e in placed if e[0] in d.castle_guns] + stern)
    if MODULAR:
        # ((0.3.28, note 226) the wider train: each way only as far as its breeching from her side clears the carriage
        # (fit_checks), the further way given way first)
        for e in placed + stern:
            f = next(x for x in d.fixtures if x.get("code") == e[0])
            lim = f["trainLimits"]
            before = list(lim)
            while lim[0] < -0.25 and breeching_out_clear(e, [lim[0], 0.0]) <= 0.005:
                lim[0] += 0.25
                sweep_set_why(d, e[0], 0, "its breeching")
            while lim[1] > 0.25 and breeching_out_clear(e, [0.0, lim[1]]) <= 0.005:
                lim[1] -= 0.25
                sweep_set_why(d, e[0], 1, "its breeching")
            if lim != before:
                print(f"  {e[0]}: trains {lim[0]:.2f}..{lim[1]:.2f}°, its breeching clear of its carriage")
            if f.get("trainLimitsGateOpen"):
                g = f["trainLimitsGateOpen"]
                g[0], g[1] = max(g[0], lim[0]), min(g[1], lim[1])
    if PORT_SQUARE:
        # ((0.3.30, note 237) what stops each gun each way, for the offline test: "hull" (her own surfaces: its port's
        # jambs, sill and lintel, her side, the frames...), "lid", a neighbour, its room, a gate, a stair, its breeching)
        print("  the guns' arcs (0.3.30, swept; - aft or to port of square, its side board's perpendicular):")
        for e in placed + stern:
            f = next(x for x in d.fixtures if x.get("code") == e[0])
            res = d.sweep.get(e[0])
            if not res:
                continue
            f["trainBound"] = list(res["why"])
            f["sweepElevations"] = list(res["elevs"])
            print(f"    {e[0]:28s} side {d.gun_phi.get(e[0], 0.0):5.1f}°  trains {f['trainLimits'][0]:7.2f} .. {f['trainLimits'][1]:6.2f}"
                  f"  ({res['why'][0]} / {res['why'][1]})  laid {f['elevationLimits'][0]:.2f} .. {f['elevationLimits'][1]:.2f}")


def stern_guns(d):
    """
    (0.3.25, note 179) carrack-m's stern chasers (STERN_GUNS): each a gun as the castles' (gun.py, its moving parts, a
    fixture of type gun), on the poop deck, pointing aft (out -x) through its port in the transom, its trunnions GUN_INSET
    forward of the transom's inside at their height, training about an upright in the middle of the transom's thickness;
    ring bolts on the transom's inside either side of the port; its port's lid on the transom (stern_port_lid). The fixture's
    "port" is (z0 y0 z1 y1) and its "wall" the transom's faces' x (inside at the sill and the head, outside at the sill
    and the head): the mod reads a gun pointing along x so (docs/slots.md §7). Empty places (castle_guns). Returns the
    placed entries (code, side, z, info, quads) as guns' are.
    """
    import gun
    placed = []
    d.stern_guns = []
    for s, side in ((-1, "port"), (1, "starboard")):
        for name, gz, deck, py, zone, sides in STERN_GUNS:
            if s not in sides:
                continue
            code = f"gun-{side}-{name}"
            d.castle_guns.append(code)
            d.stern_guns.append(code)
            zc = s * gz
            ay = deck + gun.AXIS_Y
            inner = stern_x(ay) + HULL_T                 # the transom's inside at the axis
            ox = inner + GUN_INSET
            origin = np.array([ox, deck, zc])
            out = np.array([-1.0, 0.0, 0.0])
            rby = deck + min(0.80, py[1] - 0.13)
            bolts = [np.array([stern_x(rby) + HULL_T, rby, zc + dz]) for dz in (-0.55, 0.55)]
            first = len(d.quads)
            info = gun.add_gun(d, origin, out, code, ring_bolts=bolts, pivot_x=GUN_INSET + HULL_T / 2)
            d.moving_parts.extend(gun.moving_parts(code, info))
            stern_port_lid(d, code, s, zc, deck, py, zone)
            mine = [q for q in d.quads[first:] if q["group"].startswith(f"part:{code}") and "breeching" not in q["group"]]
            placed.append((code, s, zc, info, mine))
            d.gun_levels[code] = (deck, deck + py[0], deck + py[1])
            box = [inner, deck, zc - GUNPORT_W / 2, ox + 1.15, deck + 0.75, zc + GUNPORT_W / 2]
            sel = [inner, deck, zc - 0.55, ox + 1.3, deck + 0.85, zc + 0.55]
            y0, y1 = deck + py[0], deck + py[1]
            d.fixtures.append({"type": "gun", "code": code, "pos": [round(float(v), 4) for v in origin], "yaw": 180.0,
                               "sel": [round(float(v), 4) for v in sel], "box": [round(float(v), 4) for v in box],
                               "muzzle": [round(float(v), 4) for v in info["muzzle"]], "out": [-1.0, 0.0, 0.0],
                               "elevation": GUN_ELEVATION, "material": "bronze", "bore": gun.BORE, "shot": 4.1,
                               "trunnion": [round(float(v), 4) for v in info["trunnion"]],
                               "pivot": [round(float(v), 4) for v in info["pivot"]],
                               "seat": [round(float(v), 4) for v in info["seat"]],
                               "trainLimits": [-GUN_TRAIN, GUN_TRAIN], "elevationLimits": list(GUN_ELEVATION_LIMITS),
                               "trainRate": GUN_RATES[0], "elevationRate": GUN_RATES[1],
                               "port": [zc - GUNPORT_W / 2, y0, zc + GUNPORT_W / 2, y1],
                               "wall": [round(stern_x(y0) + HULL_T, 4), round(stern_x(y1) + HULL_T, 4), round(stern_x(y0), 4),
                                        round(stern_x(y1), 4)],
                               "lid": f"{code}-lid", "housed": gun.RECOIL})
    return placed


def stern_port_lid(d, code, s, zc, deck=POOP, py=GUNPORT_Y, zone="castle-bulwarks"):
    """
    (0.3.25) A stern chaser's port lid: as port_lid's on her side, but on the transom: a panel of the transom's own planks
    (tarred outside, oak inside, each plank in line with the transom's strake behind it) a little bigger than the port,
    lying on the planking over it in its raked plane, hinged along its top outside under the taffrail, two iron straps
    down it from the hinge, a ring bolt near its foot. Its moving part "<gun>-lid" turns it out (aft) and up LID_OPEN
    degrees about its hinge (an axis across her, z) by the gun's port standing open ("open:<gun>").
    """
    grp = f"part:{code}-lid"
    z0, z1 = zc - GUNPORT_W / 2 - 0.04, zc + GUNPORT_W / 2 + 0.04
    y0, y1 = deck + py[0] - 0.04, deck + py[1]
    levels = [y0] + [deck + 0.25 * k for k in range(1, 8) if y0 < deck + 0.25 * k < y1] + [y1]
    zs = np.linspace(z0, z1, 5)
    hi = getattr(d, "transom_hi", None) or list(np.arange(OH_Y, POOP + 1.25 + 1e-6, 0.25))

    def P(z, y, out):
        return (stern_x(y) - (LID_T if out else 0.004), y, z)

    def band(y):
        """The transom's strake (its index in the transom's levels) at height y, and its bottom and top."""
        for j in range(len(hi) - 1):
            if hi[j] - 1e-6 <= y < hi[j + 1] - 1e-6:
                return j, hi[j], hi[j + 1]
        return len(hi) - 2, hi[-2], hi[-1]

    d.paint_tag = {"tarred": "transom", "oak": zone}
    for out, mat in ((True, "tarred"), (False, "oak")):
        for ya, yb in zip(levels[:-1], levels[1:]):
            j, ba, bb = band((ya + yb) / 2)
            v0, v1 = BANDS[j % 3]
            fa, fb = (ya - ba) / (bb - ba), (yb - ba) / (bb - ba)
            va, vb = v1 - (v1 - v0) * fa, v1 - (v1 - v0) * fb
            for za, zb in zip(zs[:-1], zs[1:]):
                ua, ub = (za - z0) / (z1 - z0) * 13.3, (zb - z0) / (z1 - z0) * 13.3
                pts = [np.array(P(za, ya, out)), np.array(P(zb, ya, out)), np.array(P(zb, yb, out)), np.array(P(za, yb, out))]
                uvs = [(ua, va), (ub, va), (ub, vb), (ua, vb)]
                want = np.array([-1.0 if out else 1.0, 0, 0])
                if np.dot(np.cross(pts[1] - pts[0], pts[3] - pts[0]), want) < 0:
                    pts, uvs = [pts[1], pts[0], pts[3], pts[2]], [uvs[1], uvs[0], uvs[3], uvs[2]]
                d.add_quad(grp, mat, pts, uvs=uvs)
    # its edges, through its thickness
    d.paint_tag = "transom"
    for (za, ya), (zb, yb), want in (((z0, y0), (z1, y0), (0, -1, 0)), ((z0, y1), (z1, y1), (0, 1, 0)),
                                     ((z0, y0), (z0, y1), (0, 0, -1)), ((z1, y0), (z1, y1), (0, 0, 1))):
        q = [P(za, ya, False), P(zb, yb, False), P(zb, yb, True), P(za, ya, True)]
        _quad_facing(d, grp, "tarred", q, want)
    # its hinges' straps down it from the hinge, their knuckles on the hinge's line; a ring bolt near its foot
    d.paint_tag = None
    hx, hy = stern_x(y1) - LID_T, y1
    for zh in (z0 + 0.17, z1 - 0.17):
        top, bot = np.array(P(zh, y1 - 0.02, True)), np.array(P(zh, y1 - 0.42, True))
        n_ = np.array([-0.008, 0, 0])
        d.add_beam_mesh(grp, "iron", top + n_, bot + n_, 0.05, 0.016, up=(1, 0, 0))
        d.add_cylinder(grp, "iron", (hx, hy, zh - 0.07), (hx, hy, zh + 0.07), 0.022, 0.022, sides=6, smooth=False)
    eye = np.array(P(zc, y0 + 0.12, True)) + np.array([-0.02, 0, 0])
    d.add_box_mesh(grp, "iron", eye, (0.02, 0.07, 0.07))
    rope_path(d, ring_points(eye + np.array([-0.02, -0.035, 0]), 0.035, "z", 8), r=0.009, group=grp, mat="iron")
    # (the transom rakes aft going up, its foot inboard: opened by the side lids' angle it would lie that much lower than
    # theirs, over the barrel laid up and trained; so the rake more)
    rake = math.degrees(math.atan2(stern_x(y0) - stern_x(y1), y1 - y0))
    d.moving_parts.append({"code": f"{code}-lid", "axisPoint": [round(float(hx), 4), round(float(hy), 4), round(float(zc), 4)],
                           "axisDir": [0.0, 0.0, 1.0], "source": f"open:{code}", "angle": round(-(LID_OPEN + rake), 3)})


def lid_bottom(deck, py, gx):
    """A port lid's lower edge: 0.04 under the port's sill; (0.3.23) on the quarterdeck's sides (carrack-m's castle guns
    there) just over the moulding along the deck's line outside (raised_strip at QUARTER..QUARTER + 0.25 over the poop's
    overhang), not on it."""
    y0 = deck + py[0] - 0.04
    if MODULAR and POOP_OVERHANG and abs(deck - QUARTER) < 1e-6 and gx < WAIST_AFT:
        y0 = max(y0, QUARTER + 0.25 + 0.003)
    return y0


def port_lid(d, code, s, gx, deck=MAIN, py=GUNPORT_Y, zone="waist-bulwarks"):
    """
    (0.3.8, the agreed redesign) A gun port's lid: a panel of the side's own planks (tarred outside, oak inside, each
    plank in line with the side's strake behind it) a little bigger than the port, lying on the planking over it,
    hinged along its top edge outside under the rail cap, two iron straps down it from the hinge and a ring bolt near
    its foot for its lanyard. Its moving part "<gun>-lid" turns it outward and up LID_OPEN degrees about its hinge, by
    the gun's port standing open ("open:<gun>"). Returns the hinge's axis point and the lid's box shut (x0, y0, x1, y1).
    """
    grp = f"part:{code}-lid"
    x0, x1 = gx - GUNPORT_W / 2 - 0.04, gx + GUNPORT_W / 2 + 0.04
    y0, y1 = lid_bottom(deck, py, gx), deck + py[1]
    levels = [y0] + [deck + 0.25 * k for k in range(1, 8) if y0 < deck + 0.25 * k < y1] + [y1]
    xs = np.linspace(x0, x1, 5)

    def P(x, y, out):
        return (x, y, s * (hw(x, y) + (LID_T if out else 0.004)))

    d.paint_tag = {"tarred": "outside", "oak": zone}
    for out, mat, band0 in ((True, "tarred", d.gate_band0), (False, "oak", d.gate_band0_in)):
        for ya, yb in zip(levels[:-1], levels[1:]):
            k = int(math.floor((ya - MAIN) / 0.25 + 1e-6))          # the side's strake it lies on
            v0, v1 = BANDS[(band0 + k) % 3]
            fa, fb = (ya - (MAIN + 0.25 * k)) / 0.25, (yb - (MAIN + 0.25 * k)) / 0.25
            va, vb = v1 - (v1 - v0) * fa, v1 - (v1 - v0) * fb        # (v0 at a strake's top edge)
            for xa, xb in zip(xs[:-1], xs[1:]):
                ua, ub = (xa - x0) / (x1 - x0) * 13.3, (xb - x0) / (x1 - x0) * 13.3
                pts = [np.array(P(xa, ya, out)), np.array(P(xb, ya, out)), np.array(P(xb, yb, out)), np.array(P(xa, yb, out))]
                uvs = [(ua, va), (ub, va), (ub, vb), (ua, vb)]
                want = np.array([0, 0, s * (1 if out else -1)])
                if np.dot(np.cross(pts[1] - pts[0], pts[3] - pts[0]), want) < 0:
                    pts, uvs = [pts[1], pts[0], pts[3], pts[2]], [uvs[1], uvs[0], uvs[3], uvs[2]]
                d.add_quad(grp, mat, pts, uvs=uvs, variant=band0 + k + (0 if out else 2))
    # its edges, through its thickness
    d.paint_tag = "topsides"
    for (xa, ya), (xb, yb), want in (((x0, y0), (x1, y0), (0, -1, 0)), ((x0, y1), (x1, y1), (0, 1, 0)),
                                     ((x0, y0), (x0, y1), (-1, 0, 0)), ((x1, y0), (x1, y1), (1, 0, 0))):
        q = [P(xa, ya, False), P(xb, yb, False), P(xb, yb, True), P(xa, ya, True)]
        _quad_facing(d, grp, "tarred", q, want)
    # its hinges' straps down it from the hinge, their knuckles on the hinge's line; a ring bolt near its foot
    d.paint_tag = None
    hy, hz = y1, s * (hw(gx, y1) + LID_T)
    # ((0.3.27, note 206: "gun port covers open at the wrong angle for the bow") the hinge along her side where it curves:
    # its line the side's tangent at the lid's top (along x where the side runs straight)
    e_ = 0.05
    slope = (float(hw(np.array(gx + e_), np.array(y1))) - float(hw(np.array(gx - e_), np.array(y1)))) / (2 * e_)
    tang = np.array([1.0, 0.0, s * slope])
    tang /= np.linalg.norm(tang)
    for xh in (x0 + 0.17, x1 - 0.17):
        top, bot = np.array(P(xh, y1 - 0.02, True)), np.array(P(xh, y1 - 0.42, True))
        n_ = np.array([0, 0, s * 0.008])
        d.add_beam_mesh(grp, "iron", top + n_, bot + n_, 0.05, 0.016, up=(0, 0, 1))
        kc = np.array([xh, hy, s * (hw(xh, y1) + LID_T)])
        d.add_cylinder(grp, "iron", tuple(kc - tang * 0.07), tuple(kc + tang * 0.07), 0.022, 0.022, sides=6, smooth=False)
    eye = np.array(P(gx, y0 + 0.12, True)) + np.array([0, 0, s * 0.02])
    d.add_box_mesh(grp, "iron", eye, (0.07, 0.07, 0.02))
    rope_path(d, ring_points(eye + np.array([0, -0.035, s * 0.02]), 0.035, "x", 8), r=0.009, group=grp, mat="iron")
    # (0.3.10) a lid under the main channel opens only as far as keeps it 0.05 under it (its foot stands out with the
    # side's flare, so at 110 deg it would reach 0.2 over the channel's underside)
    ang = LID_OPEN
    if GRID and x0 < CHANNEL[1] + 0.05 and x1 > CHANNEL[0] - 0.05:
        pts = np.array([p_ for q in d.quads if q["group"] == grp for p_ in q["pts"]], float)
        c = np.array([gx, hy, hz])
        while ang > 60:
            t = math.radians(-s * ang)
            yy = c[1] + np.cos(t) * (pts[:, 1] - c[1]) - np.sin(t) * (pts[:, 2] - c[2])
            if yy.max() <= CHANNEL[2] - 0.05:
                break
            ang -= 1.0
    d.moving_parts.append({"code": f"{code}-lid", "axisPoint": [round(float(gx), 4), round(float(hy), 4), round(float(hz), 4)],
                           "axisDir": [round(float(v), 5) for v in tang], "source": f"open:{code}", "angle": round(-s * ang, 3)})
    return (gx, hy, hz), (x0, y0, x1, y1)


def scuppers(d):
    """
    (0.3.8, the agreed redesign) The scuppers: a round hole (SCUPPER_R) through the waterway at the deck in each of the
    planking's square cuts (SCUPPERS), lined with lead through the side's thickness, a lead lip round it outside and a
    flange inside; the planking round the hole fills the cut (its plank's band, as the strake's).
    """
    r = SCUPPER_R
    yc = MAIN + 0.1                                  # its bottom 0.025 over the deck
    n = 16
    for a, b in SCUPPERS:
        xc = (a + b) / 2
        for s in (1, -1):
            for inner, mat, grp, band in ((False, "tarred", "exterior", d.gate_band0), (True, "oak", "auto", d.gate_band0_in)):
                v0, v1 = BANDS[band % 3]

                def P(x, y):
                    return np.array([x, y, s * (hw(x, y) - (HULL_T if inner else 0.0))])

                def uv(x, y):
                    return ((x - a) * 16 + 4.0, v1 - (v1 - v0) * (y - MAIN) / 0.25)

                def edge(t):
                    """Where the ray from the hole's centre at angle t leaves the square (a, b) x (MAIN, MAIN + 0.25)."""
                    dx, dy = math.cos(t), math.sin(t)
                    ks = []
                    if abs(dx) > 1e-9:
                        ks.append(((b if dx > 0 else a) - xc) / dx)
                    if abs(dy) > 1e-9:
                        ks.append(((MAIN + 0.25 if dy > 0 else MAIN) - yc) / dy)
                    k = min(kk for kk in ks if kk > 0)
                    return xc + dx * k, yc + dy * k

                corners = [(b, MAIN + 0.25), (a, MAIN + 0.25), (a, MAIN), (b, MAIN)]
                d.paint_tag = "inside" if inner else "outside"
                cang = [math.atan2(cy - yc, cx_ - xc) % (2 * math.pi) for cx_, cy in corners]
                want = np.array([0, 0, -s if inner else s])
                for k in range(n):
                    t0, t1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
                    c0 = (xc + r * math.cos(t0), yc + r * math.sin(t0))
                    c1 = (xc + r * math.cos(t1), yc + r * math.sin(t1))
                    e0, e1 = edge(t0), edge(t1)
                    polys = []
                    mid = [cn for cn, ca in zip(corners, cang) if t0 < ca < t1]
                    if mid:
                        polys = [[c0, c1, e1, mid[0]], [c0, mid[0], e0, e0]]
                    else:
                        polys = [[c0, c1, e1, e0]]
                    for poly in polys:
                        pts = [P(x, y) for x, y in poly]
                        uvs = [uv(x, y) for x, y in poly]
                        nrm = np.cross(pts[1] - pts[0], pts[2] - pts[0])
                        if np.linalg.norm(nrm) < 1e-12:
                            continue
                        if np.dot(nrm, want) < 0:
                            pts, uvs = [pts[1], pts[0], pts[3], pts[2]], [uvs[1], uvs[0], uvs[3], uvs[2]]
                        d.add_quad(grp, mat, pts, uvs=uvs, variant=band)
            # the lead lining through the side, a lip outside and a flange inside
            d.paint_tag = None
            po, pi = np.array([xc, yc, s * hw(xc, yc)]), np.array([xc, yc, s * (hw(xc, yc) - HULL_T)])
            d.add_cylinder("exterior", "lead", po + np.array([0, 0, s * 0.012]), pi - np.array([0, 0, s * 0.006]), r, r,
                           sides=12, caps=False, inward=True)
            nrm_o = hull_normal(xc, yc) * np.array([1, 1, s])
            annulus(d, "exterior", "lead", po + nrm_o * 0.012, nrm_o, r, r + 0.04, 12)
            d.add_cylinder("exterior", "lead", po - nrm_o * 0.002, po + nrm_o * 0.012, r + 0.04, r + 0.04, sides=12,
                           caps=False, smooth=False)
            annulus(d, "auto", "lead", pi - np.array([0, 0, s * 0.006]), (0, 0, -s), r, r + 0.03, 12)


def gun_top(entry, train_deg, back):
    """A placed gun's vertices from above (x, z), trained so (degrees, + its muzzle toward the bow) and run back `back`."""
    import gun
    code, s, gx, info, quads = entry
    f = info["frame"]
    p = np.concatenate([np.asarray(q["pts"], float) for q in quads]) - f.u * back
    piv = np.asarray(info["pivot"], float)
    phi = math.radians(train_deg) * gun.train_sign(f.u)       # about +y, by the right hand rule
    dx, dz = p[:, 0] - piv[0], p[:, 2] - piv[2]
    return piv[0] + dx * math.cos(phi) + dz * math.sin(phi), piv[2] - dx * math.sin(phi) + dz * math.cos(phi)


def capstan_clearance(d, margin=0.1):
    """
    The capstan's bars as the crew walk them round (their circle, the iron caps on their ends), and the crew walking
    them (a player's half width further out), against what's round the capstan: the pump and its brake, the waist
    hatch's coaming, the forecastle's door and stair, the entry ports' gates swinging open, and every gun from above,
    however it's laid within its limits and run out or back. Prints how close each comes; the bars sweeping into one
    stops the build.
    """
    import gun
    c = np.array([CAPSTAN_X, CAPSTAN_Z])
    bars = CAPSTAN_BARS + 0.07
    crew = 1.3 + 0.3                                   # where the offline test walks them, and half a player
    if VARIANT == 3:
        crew = 1.1 + 0.3                               # ((0.3.13, note 130) the shorter bars: walked at 1.1)

    def dist_box(x0, x1, z0, z1):
        dx = max(x0 - c[0], 0.0, c[0] - x1)
        dz = max(z0 - c[1], 0.0, c[1] - z1)
        return math.hypot(dx, dz)

    def dist_seg(a, b):
        a, b = np.asarray(a, float), np.asarray(b, float)
        t = np.clip(np.dot(c - a, b - a) / max(1e-9, np.dot(b - a, b - a)), 0, 1)
        return float(np.linalg.norm(a + (b - a) * t - c))

    near = {}
    px, pz = PUMP
    near["the pump"] = dist_box(px - 0.2, px + 0.2, pz - 0.2, pz + 0.2)
    # its brake rocked either way, the handle's crossbar on its end
    near["the pump's brake"] = min(dist_seg((px - 0.28 * PUMP_BRAKE, pz - 0.22), (px + 1.12 * PUMP_BRAKE, pz - 0.22)),
                                   dist_seg((px - 0.28 * PUMP_BRAKE, pz + 0.22), (px + 1.12 * PUMP_BRAKE, pz + 0.22)))
    hx0, hx1, hz0, hz1 = HATCH
    near["the waist hatch"] = dist_box(hx0 - 0.25, hx1 + 0.25, hz0 - 0.25, hz1 + 0.25)
    if MODULAR:
        near["the waist hatch"] = dist_box(*OPENING)       # ((0.3.20) flush, a piece: its opening)
    near["the forecastle door"] = dist_box(FRONT_FWD - 0.12 - (0.0 if MODULAR else 0.25), FRONT_FWD, -0.7, 0.7)
    for st in d.stairs:
        if st["floor"] != MAIN:
            continue
        xs = [v for step in st["steps"] for v in step[:2]]
        z0, z1 = st["steps"][0][3], st["steps"][0][4]
        near[f"the {st['code']} stair"] = dist_box(min(xs) - 0.3, max(xs), z0 - 0.1, z1)
    for s in (1, -1):
        leaves = [gate_leaf(s, a) for a in np.linspace(0.0, GATE_OPEN, 21)]
        near[f"the {'starboard' if s > 0 else 'port'} gate"] = min(dist_seg(p0, p1) for p0, p1 in leaves) - HULL_T / 2
    worst_gun = (9.0, "")
    for e in getattr(d, "guns_placed", []):
        f = next(x for x in d.fixtures if x.get("code") == e[0])
        for t in np.arange(f["trainLimits"][0], f["trainLimits"][1] + 1e-9, 1.0):
            for back in (0.0, gun.RECOIL / 2, gun.RECOIL):
                x, z = gun_top(e, float(t), back)
                dmin = float(np.min(np.hypot(x - c[0], z - c[1])))
                if dmin < worst_gun[0]:
                    worst_gun = (dmin, e[0])
    if worst_gun[1]:
        near[f"{worst_gun[1]} (laid anywhere, run back)"] = worst_gun[0]
    bad = []
    for what, dd in sorted(near.items(), key=lambda kv: kv[1]):
        print(f"  capstan: {what} {dd - bars:.2f} clear of the bars, {dd - crew:.2f} of the crew walking them")
        if dd < bars + margin:
            bad.append(what)
    assert not bad, f"the capstan's bars sweep into {', '.join(bad)}"


def fit_checks(d, margin=0.02):
    """
    (0.3.7, after the user's chalk notes) What the fixes for them rely on, checked on the built design: prints how close
    each comes, and stops the build if one fails.
    - the inner sternpost is ahead of every bit of the rudder, its pintles, gudgeons and the sternpost that comes forward
      into her (inside the inside planking), where they're within its width (note 29)
    - the bowsprit rests on the saddle on the stem head (note 21), and comes up through the forecastle's deck within its
      partners, clear of the heads either side (note 19)
    - the hawse holes go through their hawse pieces, clear of the frames (note 23)
    - the companion house's roof is under the boat gallows' beam (note 24)
    - the cable's hole in the main deck is clear of the deck beams, over the middle of the tier (note 25)
    """
    bad = []

    def say(what, value, ok):
        print(f"  fit: {what} {value}")
        if not ok:
            bad.append(what)

    # the rudder's parts inside her, against the inner post's front
    a, b = d.rudder_quads
    worst = 9.0
    secs = d.inner_post
    ys = np.array([sec[0][1] for sec in secs])
    fronts = np.array([sec[1][0] for sec in secs])
    w = secs[0][2][2]
    for q in d.quads[a:b]:
        pts = [np.asarray(p, float) for p in q["pts"]]
        for i in range(4):
            for t in np.linspace(0, 1, 6):
                p = pts[i] + (pts[(i + 1) % 4] - pts[i]) * t
                if abs(p[2]) > w or not ys[0] <= p[1] <= ys[-1]:
                    continue
                xo, _ = _ceil_ring(float(p[1]))
                if len(xo) < 2 or p[0] < float(xo[0]) - 0.01:
                    continue                                     # (aft of the inside planking: in the hull, or out of her)
                worst = min(worst, float(np.interp(p[1], ys, fronts)) - float(p[0]))
    say("the inner sternpost's front is ahead of the rudder's parts inside her by", f"{worst:.2f}", worst > margin)
    # the bowsprit on its saddle
    sx, sy = SADDLE
    u = BOWSPRIT_B - BOWSPRIT_A
    under = bowsprit_y(sx) - bowsprit_r(sx) * math.sqrt(1 + (u[1] / u[0]) ** 2)
    say("the bowsprit's underside over the stem head's saddle", f"{under - sy:+.3f}", abs(under - sy) < 0.01)
    lo_x, hi_x = d.partners
    say("the bowsprit through the forecastle deck between", f"{lo_x:.2f} and {hi_x:.2f}",
        lo_x > WAIST_FWD + 1.0 and (GRID or hi_x < HEADS_X - 0.3))
    r_h = bowsprit_r(HEADS_X) * math.sqrt(1 + (u[1] / u[0]) ** 2)
    say("the heads' seats clear of the bowsprit beside them by", f"{HEADS_Z - 0.25 - bowsprit_r(HEADS_X):.2f}",
        HEADS_Z - 0.25 - bowsprit_r(HEADS_X) > 0.08 or bowsprit_y(HEADS_X) - r_h > QUARTER + 0.6)
    # the hawse holes and the frames
    for hx, hy, hw_, hh_ in d.hawse_pieces:
        near = min(abs(hx - f) for f in frame_stations())
        say("the hawse hole's piece round it, the nearest frame's middle", f"{near:.2f} from its middle (the frame stops under and "
            f"over it)", hw_ > 0.24 and hh_ > 0.24)
    # the companion house under the gallows' beam
    if COMPANION_HOUSE:
        roof = HOUSE_TOP + 0.06 + 0.04 + 0.05
        say("the companion house's roof under the boat gallows' beam by", f"{GALLOWS_Y - GALLOWS_BEAM - roof:.2f}",
            GALLOWS_Y - GALLOWS_BEAM - roof > 0.03)
    # the cable's hole, clear of the main deck's beams
    hole = d.cable_hole
    if hole is not None:
        gap = min(min(abs(hole[0] - bx), abs(hole[0] - bx - 0.25)) for bx in main_beams())
        say("the cable's hole in the main deck clear of its beams by", f"{gap - 0.14:.2f}",
            gap > 0.14 and not any(bx < hole[0] < bx + 0.25 for bx in main_beams()))
    # (0.3.8) the shorter guns: the barrel clear of its carriage laid anywhere, the breeching clear of the barrel from
    # the cheeks' eyebolts round the cascabel (gun.check), and from the ring bolts on her side to the eyebolts clear of
    # the carriage's trucks, cheeks and axletrees, run out or run back, trained to its limits (the user's note 45)
    import gun
    own = gun.check(GUN_ELEVATION_LIMITS, verbose=False)
    say("the guns' barrels clear of their carriages and the breeching of the barrel, laid anywhere, by", f"{own:.3f}",
        own > 0.0)
    lims = {x["code"]: x["trainLimits"] for x in d.fixtures if x.get("type") == "gun"}
    out = min(breeching_out_clear(e, lims[e[0]]) for e in getattr(d, "guns_placed", []))
    say("the guns' breeching from her side to the cheeks clear of the carriage, run out or back, trained, by",
        f"{out:.3f}", out > 0.0)
    if LIDDED and getattr(d, "guns_placed", None):
        top, sill, lid, housed = lid_clearances(d, lims)
        say(f"the guns laid {GUN_ELEVATION_LIMITS[1]:.0f}° up under their ports' lintels, run out and running in, by",
            f"{top:.3f}", top > 0.0)
        say("the guns laid 5° down over their ports' sills, run out and running in, by", f"{sill:.3f}", sill > 0.0)
        # ((Hansa) a quick build's arcs and elevations aren't swept: only a full build's lids are checked)
        say("the guns run out, laid and trained anywhere, clear of their lids opened by", f"{lid:.3f}", lid > 0.0 or FAST)
        say("the guns housed (run back), their muzzles in behind the shut lids by", f"{housed:.3f}", housed > 0.1)
    # (0.3.10) the gun ports' lids opened (their moving parts' angle) clear of the main channel over them
    gap = lid_channel_clear(d)
    say("the gun ports' lids opened clear of the main channel by", f"{gap:.3f}", gap > 0.03)
    # (0.3.10, the C# side's walk tests) a player walking up and down the companion and the hold stair has headroom
    if MODULAR:
        piece_checks(d, say)        # ((0.3.18) the ladder-stairs, the hearth, the oven and the flue: §7)
    else:
      for code, room in stair_headroom(d).items():
        say(f"headroom walking the {code} stair (a player 0.6 x 1.85 x 0.6), at least", f"{room:.2f}", room >= 0.0)
    # (0.3.10, the user's note 71: "door blocked by texture") every doorway clear through its wall, 2 high and 1 wide:
    # no drawn face in it (sampled over each quad), no solid cell
    # ((0.3.12) the doorway's box given: the stairwell's door is in a wall along x)
    for code, lo, hi in getattr(d, "door_boxes", []):
        lo, hi = np.array(lo, float), np.array(hi, float)
        hits = 0
        for q in d.quads:
            p = np.asarray(q["pts"], float)
            if (p.max(axis=0) < lo).any() or (p.min(axis=0) > hi).any():
                continue
            for u in (0.1, 0.5, 0.9):
                for v in (0.1, 0.5, 0.9):
                    s_ = (1 - u) * (1 - v) * p[0] + u * (1 - v) * p[1] + u * v * p[2] + (1 - u) * v * p[3]
                    if (s_ > lo).all() and (s_ < hi).all():
                        hits += 1
                        if hits <= 4:
                            print(f"    in {code}'s doorway: {q['group']} {q['mat']} ({q.get('paint')}) "
                                  f"{np.round(p, 3).tolist()}")
                        break
                else:
                    continue
                break
        sel = ((d.X > lo[0] + 0.02) & (d.X < hi[0] - 0.02) & (d.Y > lo[1]) & (d.Y < hi[1]) & (d.Z > lo[2]) &
               (d.Z < hi[2]))
        solid = int((d.grid[sel] != 0).sum())
        say(f"the doorway of {code}: faces drawn in it / solid cells", f"{hits} / {solid}", hits == 0 and solid == 0)
    assert not bad, f"fit checks failed: {', '.join(bad)}"


def stair_headroom(d, w=0.3, h=1.85, step=0.55, codes=("companion", "hold")):
    """(0.3.10) For each of these stairs: a player (w either side, h tall) walked up it from 1 past its foot and down it
    from 1 past its head in 0.05 steps, standing on the highest solid under them they can step onto (its top at most
    `step` over where they stood): the least room between their head and the lowest solid over them (negative: their
    head in something), and whether they got to the other end."""
    out = {}
    solid = d.grid != 0
    if MODULAR:
        # ((0.3.16) carrack-m: its placed stairs' treads, as the mod lays them, solid for the walk; (0.3.19) the walked
        # stairs only: the ladders are climbed (piece_checks))
        codes = tuple(st["code"] for st in d.stairs if "flight" in st)
        solid = solid.copy()
        for st in d.stairs:
            if st.get("modular"):
                for xa, xb, top, z0, z1 in st["steps"]:
                    solid |= ((d.X > xa) & (d.X < xb) & (d.Y > top - 0.25) & (d.Y < top) & (d.Z > z0) & (d.Z < z1))
    for st in d.stairs:
        if st["code"] not in codes:
            continue
        z0, z1 = st["steps"][0][3], st["steps"][0][4]
        zc = (z0 + z1) / 2
        low = min(st["steps"], key=lambda t: t[2])
        high = max(st["steps"], key=lambda t: t[2])
        x_lo = (low[0] + low[1]) / 2
        x_hi = (high[0] + high[1]) / 2
        sgn = 1.0 if x_hi > x_lo else -1.0
        foot_x = min(t[0] for t in st["steps"]) if sgn > 0 else max(t[1] for t in st["steps"])
        head_x = max(t[1] for t in st["steps"]) if sgn > 0 else min(t[0] for t in st["steps"])
        worst = 9.0
        for xa, xb, f in ((foot_x - sgn * 1.0, head_x + sgn * 1.0, st["floor"]),
                          (head_x + sgn * 1.0, foot_x - sgn * 1.0, high[2])):
            for xc in np.arange(xa, xb, 0.05 * np.sign(xb - xa)):
                col = (d.X > xc - w) & (d.X < xc + w) & (d.Z > zc - w) & (d.Z < zc + w) & solid
                tops = d.Y[col] + 0.125
                can = tops[(tops <= f + step + 1e-6) & (tops > f - 3.5)]
                if len(can):
                    f = float(can.max())
                over = tops[(tops > f + 1e-6)] - 0.25
                if len(over):
                    worst = min(worst, float(over.min()) - (f + h))
        out[st["code"]] = worst
    return out


def piece_checks(d, say):
    """
    (0.3.18, docs/modular-pieces.md §7.1, §7.2, as amended) What the mod checks for carrack-m's laid stairs, hearth, oven and flue,
    on her collision:
    - each ladder-stair: the climber (0.4 wide, 1.85 over his feet) up its line free of her, standing on it or as far
      back from it as he can (reported: up to how far back he still clears); where he stands at its foot (half a block
      before its foot cell, on its deck) and where he steps off at its head (half a block past its head cell, on the
      deck above) floor with 1.85 over it; over its two cells nothing of her lower than 0.75 under the deck above, the
      deck's planks open (the well) where there's a deck over them; its head cell's floor there (a chest under it);
    - (0.3.19, §8) each walked stair: a man's height (1.85) over every tread and over the 0.6 before it, across his 0.6
      width; its treads (but the top one) free of her; floor with 1.85 over it half a block before its foot and half a
      block past its head (on the deck above); her over its head cell (the deck above, its coaming or beam) at most a
      tread (0.25) back from its far edge; walked up and down (stair_headroom);
    - the hearth: its cell a floor with 2 blocks clear; the oven: a block clear, within 1.5 of the hearth;
    - the flue: the 0.5 column over the hearth's middle free up to the deck above's underside, that deck at most 0.75
      thick there, a block free over it, and open air over that (nothing of her within 6 blocks).
    """
    sol_lut = np.array([False] + [m.solid for m in d.materials[1:]])
    solid = sol_lut[d.grid]

    def any_solid(x0, x1, y0, y1, z0, z1):
        i = (d.xs > x0 + 1e-6) & (d.xs < x1 - 1e-6)
        j = (d.ys > y0 + 1e-6) & (d.ys < y1 - 1e-6)
        k = (d.zs > z0 + 1e-6) & (d.zs < z1 - 1e-6)
        return bool(solid[np.ix_(i, j, k)].any())

    def stand(x, y, zc):
        """A floor under (x, y) (a solid voxel just under it) and 1.85 clear over it, 0.4 square."""
        return (any_solid(x - 0.2, x + 0.2, y - 0.25, y, zc - 0.2, zc + 0.2) and
                not any_solid(x - 0.2, x + 0.2, y, y + 1.85, zc - 0.2, zc + 0.2))

    for st in d.stairs:
        if "ladder" not in st:
            continue
        x_low, sg, fy, z0, z1 = st["ladder"]
        ya = fy + 3.0
        zc = (z0 + z1) / 2
        (xf, yf), (xt, yt) = ladder_line(x_low, sg, fy)
        clear = []
        for back in np.arange(0.0, 0.61, 0.05):
            if all(not any_solid(px - 0.2, px + 0.2, py + 0.05, py + 1.85, zc - 0.2, zc + 0.2)
                   for px, py in ((xf + (xt - xf) * t - sg * back, yf + (yt - yf) * t) for t in np.linspace(0, 1, 61))):
                clear.append(round(float(back), 2))
        # (§7.1 as amended: the climber's feet 0.2 behind the ladder's line, his body on its low side: he must clear
        # standing 0.2 back from it)
        say(f"the {st['code']} ladder-stair: the climber clears her all the way up standing this far back from its line",
            f"{clear[0]:.2f}..{clear[-1]:.2f}" if clear else "never", 0.2 in clear)
        cx0, cx1 = sorted((x_low, x_low + 2 * sg))
        foot_ok = stand(x_low - sg * 0.5, fy, zc)
        top_ok = stand(x_low + sg * 2.5, ya, zc)
        hang = any_solid(cx0, cx1, fy, ya - 0.75, z0, z1)
        planks = any_solid(cx0, cx1, ya - 0.25, ya, z0, z1)
        head_floor = any_solid(*sorted((x_low + sg * 1.0, x_low + sg * 2.0)), fy - 0.25, fy, z0, z1)
        say(f"the {st['code']} ladder-stair: floor before its foot / past its head, nothing lower than 0.75 under the "
            f"deck over its cells, the well open, its head cell's floor",
            f"{foot_ok} / {top_ok} / {not hang} / {not planks} / {head_floor}",
            foot_ok and top_ok and not hang and not planks and head_floor)
    # (0.3.19, docs/modular-pieces.md §8) the walked stairs
    walks = stair_headroom(d)
    for st in d.stairs:
        if "flight" not in st:
            continue
        x_low, sg, fy, z0, z1, rise, run = st["flight"]
        zc = (z0 + z1) / 2

        def xr(u0, u1):
            return tuple(sorted((x_low + sg * u0, x_low + sg * u1)))
        # a man's height over every tread and over the 0.6 before it (his 0.6 width across its middle): the least room
        # over that (his head's clearance; GridClash's boxes)
        least = 9.0
        for u0, u1, top in flight_treads(rise, run):
            xa, xb = xr(u0 - FLIGHT_BEHIND, u1)
            y = fy + top + 0.05
            while y < fy + top + 4.0 and not any_solid(xa, xb, y, y + 0.05, zc - FLIGHT_HALF, zc + FLIGHT_HALF):
                y += 0.05
            least = min(least, y - (fy + top + 0.05) - (FLIGHT_ROOM - 0.05))
        # the treads (but the top one, flush with the deck above, under its edge) free of her
        treads = sum(any_solid(*xr(u0 + 0.03, u1 - 0.03), fy + top - 0.22, fy + top - 0.03, z0 + 0.09, z1 - 0.09)
                     for u0, u1, top in flight_columns(rise, run)[:-1])
        foot_ok = stand(x_low - sg * 0.5, fy, zc)
        top_ok = stand(x_low + sg * (run + 0.5), fy + rise, zc)
        # how far her (the deck above, its coaming or beam) reaches back over the head cell from its far edge, over the
        # deck the stair stands on, within its width
        i = (d.xs > min(xr(0, run))) & (d.xs < max(xr(0, run)))
        j = (d.ys > fy) & (d.ys < fy + rise + FLIGHT_ROOM)
        k = (d.zs > z0 + 0.0625) & (d.zs < z1 - 0.0625)
        sub = solid[np.ix_(i, j, k)].any(axis=(1, 2))
        us = sg * (d.xs[i][sub] - x_low) - 0.125
        over = float(run - us.min()) if len(us) else 0.0
        # ((0.3.23, the steep stair, 2 long) only the deck's planking may reach back over its head cell (nothing under
        # it), and through a deck the deck above clear from its top down a quarter block before its foot cell's near
        # edge (the walker's head comes up through it there: a 3 x 1 well, or the deck cut back that far))
        under = any_solid(*xr(run - 1.0, run), fy + 0.05, fy + rise - DECK_T - 0.01, z0 + 0.0625, z1 - 0.0625)
        if COMPANION_WAIST and st["code"] == "companion":
            # ((0.3.29, the user's #12600) the beam at x 0 under its head's last quarter (its top tread on it): as the
            # user's own stair stood, walked; the walk below says)
            over, under = 0.0, False
        if under or over > 0.25 + 1e-6:
            xa_, xb_ = xr(run - 1.0, run)
            ii_ = (d.xs > xa_) & (d.xs < xb_); jj_ = (d.ys > fy + 0.05) & (d.ys < fy + rise - DECK_T - 0.01); kk_ = (d.zs > z0 + 0.0625) & (d.zs < z1 - 0.0625)
            hit_ = np.argwhere(solid[np.ix_(ii_, jj_, kk_)])
            if len(hit_):
                h0 = hit_[0]
                print(f"  DEBUG {st['code']}: her over its head cell at x {d.xs[ii_][h0[0]]:.3f} y {d.ys[jj_][h0[1]]:.3f} z {d.zs[kk_][h0[2]]:.3f} ({len(hit_)} voxels, material {d.materials[d.grid[np.ix_(ii_, jj_, kk_)][tuple(h0)]].code})")
        before = run < 3.0 - 1e-6 and any_solid(*xr(-0.25, 0.0), fy + 1.0, fy + rise, zc - FLIGHT_HALF, zc + FLIGHT_HALF)
        walk = walks.get(st["code"], -9.0)
        say(f"the {st['code']} walked stair ({run:.0f} long): the least room over a man on its treads (1.85 over each and "
            f"the 0.6 before it) / treads in her / floor before its foot, past its head / her over its head cell, under its "
            f"deck there / the deck above clear before its foot / walked up and down (a player 0.6 x 1.85), its least "
            f"headroom",
            f"{least:+.2f} / {treads} / {foot_ok}, {top_ok} / {over:.2f}, {under} / {not before} / {walk:+.2f}",
            least >= 0.0 and treads == 0 and foot_ok and top_ok and over <= 0.25 + 1e-6 and not under and not before
            and walk >= 0.0)
    if PIECE_HEARTH:
        (hx, hz, _), (ox, oz, _) = HEARTH_PIECE, OVEN_PIECE
        h_ok = (any_solid(hx - 0.5, hx + 0.5, MAIN - 0.25, MAIN, hz - 0.5, hz + 0.5) and
                not any_solid(hx - 0.5, hx + 0.5, MAIN, MAIN + 2.0, hz - 0.5, hz + 0.5))
        o_ok = (any_solid(ox - 0.5, ox + 0.5, MAIN - 0.25, MAIN, oz - 0.5, oz + 0.5) and
                not any_solid(ox - 0.5, ox + 0.5, MAIN, MAIN + 1.0, oz - 0.5, oz + 0.5))
        dist = math.hypot(ox - hx, oz - hz)
        say("the hearth's cell floor with 2 clear / the oven's with 1 clear, the oven from the hearth",
            f"{h_ok} / {o_ok}, {dist:.2f}", h_ok and o_ok and dist <= 1.5)
    if PIECE_HEARTH and HEARTH_FLUE:
        (hx, hz, _) = HEARTH_PIECE
        # the flue's column: up from the deck to the first of her over it (the deck above's underside)
        y = MAIN
        while y < MAIN + 6 and not any_solid(hx - 0.25, hx + 0.25, y, y + 0.25, hz - 0.25, hz + 0.25):
            y += 0.25
        under = y
        while any_solid(hx - 0.25, hx + 0.25, y, y + 0.25, hz - 0.25, hz + 0.25):
            y += 0.25
        top = y
        over = not any_solid(hx - 0.25, hx + 0.25, top, top + 1.0, hz - 0.25, hz + 0.25)
        air = not any_solid(hx - 0.25, hx + 0.25, top, top + 6.0, hz - 0.25, hz + 0.25)
        say("the flue: free from the hearth's deck up to the deck over it at / that deck's thickness there / a block "
            "free over it / open air over it", f"{under:.2f} / {top - under:.2f} / {over} / {air}",
            abs(top - QUARTER) < 1e-6 and top - under <= 0.75 + 1e-6 and over and air)


def lid_channel_clear(d):
    """(0.3.10) How far the gun ports' lids opened (every corner of their meshes turned by the part's angle about its
    hinge) stay under the main channel's underside where they're under it (its x range, out from the side to its
    edge); a large number when none is."""
    x0, x1, yc = CHANNEL
    best = 9.0
    for part in d.moving_parts:
        if not part["code"].endswith("-lid"):
            continue
        c = np.asarray(part["axisPoint"], float)
        t = math.radians(part["angle"])
        R = np.array([[1, 0, 0], [0, math.cos(t), -math.sin(t)], [0, math.sin(t), math.cos(t)]])
        for q in d.quads:
            if q["group"] != f"part:{part['code']}":
                continue
            for p in q["pts"]:
                w = c + R @ (np.asarray(p, float) - c)
                if x0 - 0.05 < w[0] < x1 + 0.05:
                    best = min(best, yc - w[1])
    return best


def lid_clearances(d, limits):
    """
    (0.3.8) The guns against their ports and lids: the barrel's surface (its profile's rings) laid from 5° down to 15° up,
    trained to either limit and square, run out: how far under the port's lintel (MAIN + 1.0) and over its sill where
    it's in the side's thickness, and clear of the lid opened (its slab turned back shut about the hinge); housed (run
    back the whole recoil), how far in from the shut lid's inside its muzzle is. The least of each, over every gun.
    """
    import gun
    pts = []
    for x in np.arange(gun.PROFILE[0][0], gun.MUZZLE_X + 1e-9, 0.02):
        r = gun.radius(x)
        for k in range(24):
            a = 2 * math.pi * k / 24
            pts.append((x, gun.AXIS_Y + r * math.cos(a), r * math.sin(a)))
    pts = np.array(pts)
    top = sill = lid = housed = 9.0
    fx_ = {x["code"]: x for x in d.fixtures if x.get("type") == "gun"}
    worst = {}
    for entry in d.guns_placed:
        t_, s_, l_, h_ = port_clearances(d, entry, limits[entry[0]], fx_[entry[0]]["elevationLimits"], pts)
        for k_, v_ in zip(("top", "sill", "lid", "housed"), (t_, s_, l_, h_)):
            if v_ < worst.get(k_, (9.0, ""))[0]:
                worst[k_] = (v_, entry[0])
        top, sill, lid, housed = min(top, t_), min(sill, s_), min(lid, l_), min(housed, h_)
    print("  lid clearances, the closest:", ", ".join(f"{k_} {v_[0]:.3f} ({v_[1]})" for k_, v_ in worst.items()))
    return top, sill, lid, housed


def port_clearances(d, entry, lim, elev_limits, pts=None):
    """(0.3.21) lid_clearances for one gun, laid within elev_limits and trained within lim: its port's lintel and sill
    where it stands (d.gun_levels: the castle guns' decks and ports)."""
    import gun
    if pts is None:
        pts = []
        for x in np.arange(gun.PROFILE[0][0], gun.MUZZLE_X + 1e-9, 0.02):
            r = gun.radius(x)
            for k in range(24):
                a = 2 * math.pi * k / 24
                pts.append((x, gun.AXIS_Y + r * math.cos(a), r * math.sin(a)))
        pts = np.array(pts)
    if entry[0] in getattr(d, "stern_guns", ()):
        return stern_port_clearances(d, entry, lim, elev_limits, pts)
    top = sill = lid = housed = 9.0
    for code, s, gx, info, quads in (entry,):
        limits = {code: lim}
        f = info["frame"]
        piv = np.asarray(info["pivot"], float)
        part = next(m for m in d.moving_parts if m["code"] == f"{code}-lid")
        hinge = np.asarray(part["axisPoint"], float)
        ang = math.radians(part["angle"])
        x0, x1 = gx - GUNPORT_W / 2 - 0.04, gx + GUNPORT_W / 2 + 0.04
        deck_, py0, py1 = getattr(d, "gun_levels", {}).get(code, (MAIN, MAIN + GUNPORT_Y[0], MAIN + GUNPORT_Y[1]))
        ly0, ly1 = lid_bottom(deck_, (py0 - deck_, py1 - deck_), gx), py1
        e_lo, e_hi = elev_limits
        for e in sorted({e_lo, 0.0, 5.0, 10.0, e_hi}):
            if not e_lo - 1e-6 <= e <= e_hi + 1e-6:
                continue
            loc = gun.elevated(pts, e)
            for t in (limits[code][0], 0.0, limits[code][1]):
                for back in (0.0, 0.2, 0.4, 0.6, 0.8, gun.RECOIL):
                    w = np.array([f(*p) for p in loc]) - f.u * back
                    phi = math.radians(t) * gun.train_sign(f.u)
                    dx, dz = w[:, 0] - piv[0], w[:, 2] - piv[2]
                    w = np.stack([piv[0] + dx * math.cos(phi) + dz * math.sin(phi), w[:, 1],
                                  piv[2] - dx * math.sin(phi) + dz * math.cos(phi)], axis=1)
                    hwv = np.asarray(half_width(w[:, 0], w[:, 1]), float)     # (vectorised: 0.3.9)
                    az = np.abs(w[:, 2])
                    if back >= gun.RECOIL - 1e-6:
                        housed = min(housed, float(np.min(hwv - az)))
                        continue
                    wallp = (az > hwv - HULL_T - 0.01) & (az < hwv + 0.01)
                    if wallp.any():
                        if e >= e_hi - 1e-6:
                            top = min(top, float(np.min(py1 - w[wallp, 1])))
                        if e <= e_lo + 1e-6:
                            sill = min(sill, float(np.min(w[wallp, 1] - py0)))
                    if back > 0:
                        continue
                    # the open lid: each point turned back about the hinge by the lid's angle, against the shut slab
                    q = w - hinge
                    c, sn = math.cos(-ang), math.sin(-ang)
                    qy, qz = q[:, 1] * c - q[:, 2] * sn, q[:, 1] * sn + q[:, 2] * c
                    pb = np.stack([q[:, 0] + hinge[0], qy + hinge[1], qz + hinge[2]], axis=1)
                    hwb = np.asarray(half_width(pb[:, 0], pb[:, 1]), float)
                    zz = s * pb[:, 2]
                    out_ = np.stack([x0 - pb[:, 0], pb[:, 0] - x1, ly0 - pb[:, 1], pb[:, 1] - ly1, hwb - zz,
                                     zz - (hwb + LID_T)], axis=1).max(axis=1)
                    lid = min(lid, float(out_.min()))
    return top, sill, lid, housed


def stern_xv(y):
    """stern_x for an array of heights."""
    y = np.asarray(y, float)
    return interp(y, STERN_X) - np.where(y >= OH_Y - 1e-9, POOP_OVERHANG, 0.0)


def stern_port_clearances(d, entry, lim, elev_limits, pts):
    """(0.3.25) port_clearances for a stern chaser (its port in the transom): the barrel's surface where it's in the
    transom's thickness under the port's lintel and over its sill, laid within elev_limits and trained within lim, run out
    and running in; clear of its lid opened (turned back shut about its hinge across her, against the shut slab); housed,
    its muzzle in from the transom's outside."""
    import gun
    top = sill = lid = housed = 9.0
    code, s, zc, info, quads = entry
    f = info["frame"]
    piv = np.asarray(info["pivot"], float)
    part = next(m for m in d.moving_parts if m["code"] == f"{code}-lid")
    hinge = np.asarray(part["axisPoint"], float)
    ang = math.radians(part["angle"])
    z0, z1 = zc - GUNPORT_W / 2 - 0.04, zc + GUNPORT_W / 2 + 0.04
    deck_, py0, py1 = d.gun_levels[code]
    ly0, ly1 = py0 - 0.04, py1
    e_lo, e_hi = elev_limits
    for e in sorted({e_lo, 0.0, 5.0, 10.0, e_hi}):
        if not e_lo - 1e-6 <= e <= e_hi + 1e-6:
            continue
        loc = gun.elevated(pts, e)
        base = np.array([f(*p) for p in loc])
        for t in (lim[0], 0.0, lim[1]):
            for back in (0.0, 0.2, 0.4, 0.6, 0.8, gun.RECOIL):
                w = base - f.u * back
                phi = math.radians(t) * gun.train_sign(f.u)
                dx, dz = w[:, 0] - piv[0], w[:, 2] - piv[2]
                w = np.stack([piv[0] + dx * math.cos(phi) + dz * math.sin(phi), w[:, 1],
                              piv[2] - dx * math.sin(phi) + dz * math.cos(phi)], axis=1)
                xo = stern_xv(w[:, 1])
                if back >= gun.RECOIL - 1e-6:
                    housed = min(housed, float(np.min(w[:, 0] - xo)))
                    continue
                wallp = (w[:, 0] > xo - 0.01) & (w[:, 0] < xo + HULL_T + 0.01)
                if wallp.any():
                    if e >= e_hi - 1e-6:
                        top = min(top, float(np.min(py1 - w[wallp, 1])))
                    if e <= e_lo + 1e-6:
                        sill = min(sill, float(np.min(w[wallp, 1] - py0)))
                if back > 0:
                    continue
                # the open lid: each point turned back about the hinge (across her) by the lid's angle, against the slab
                q = w - hinge
                c, sn = math.cos(-ang), math.sin(-ang)
                qx, qy = q[:, 0] * c - q[:, 1] * sn, q[:, 0] * sn + q[:, 1] * c
                pb = np.stack([qx + hinge[0], qy + hinge[1], q[:, 2] + hinge[2]], axis=1)
                xb = stern_xv(pb[:, 1])
                out_ = np.stack([z0 - pb[:, 2], pb[:, 2] - z1, ly0 - pb[:, 1], pb[:, 1] - ly1, (xb - LID_T) - pb[:, 0],
                                 pb[:, 0] - xb], axis=1).max(axis=1)
                lid = min(lid, float(out_.min()))
    return top, sill, lid, housed


def breeching_out_clear(entry, limits):
    """How far the breeching from the ring bolts on her side to the cheeks' eyebolts stays off the gun's carriage (its
    trucks, cheeks and axletrees; less the rope's radius), run out, half and all the way back, trained to either limit
    and square: in the carriage's own frame, where the ring bolt (on her side) moves."""
    import gun
    code, s, gx, info, quads = entry
    f = info["frame"]
    piv_x = float(np.dot(np.asarray(info["pivot"], float) - f.o, f.u))
    boxes = [((x0, bottom, z * gun.CHEEK_Z - gun.CHEEK_T / 2), (x1, top, z * gun.CHEEK_Z + gun.CHEEK_T / 2))
             for x0, x1, top, bottom in gun.CHEEKS for z in (1, -1)]
    boxes += [((ax - 0.065, gun.TRUCK_R - 0.055, -0.4), (ax + 0.065, 0.205, 0.4)) for ax in gun.AXLES.values()]
    worst = 9.0
    for b in info["breeching"]:
        if b.get("follow") != code:
            continue
        bolt = np.asarray(b["anchor"], float)
        eye = f.R.T @ (np.asarray(b["tip"], float) - f.o)
        for train in (limits[0], 0.0, limits[1]):
            phi = -math.radians(train) * gun.train_sign(f.u)
            for back in (0.0, gun.RECOIL / 2, gun.RECOIL):
                q = f.R.T @ (bolt - f.o)
                # the bolt in the carriage's frame: turned back about the pivot, slid out by how far she's run back
                dx, dz = q[0] - piv_x, q[2]
                q = np.array([piv_x + dx * math.cos(phi) + dz * math.sin(phi), q[1], -dx * math.sin(phi) + dz * math.cos(phi)])
                q[0] += back
                for t in np.linspace(0.0, 0.97, 60):
                    p = q + (eye - q) * t
                    for lo, hi in boxes:
                        lo, hi = np.asarray(lo), np.asarray(hi)
                        worst = min(worst, float(np.linalg.norm(np.maximum(np.maximum(lo - p, p - hi), 0.0))) - gun.ROPE_R)
                    for ax in gun.AXLES.values():
                        for zz in (1, -1):
                            # a truck: a disc about its axle, TRUCK_T thick
                            r_ = math.hypot(p[0] - ax, p[1] - gun.TRUCK_R)
                            dzz = abs(p[2] - zz * gun.TRUCK_Z) - gun.TRUCK_T / 2
                            dr = r_ - gun.TRUCK_R
                            dist = math.hypot(max(dr, 0.0), max(dzz, 0.0)) if (dr > 0 or dzz > 0) else max(dr, dzz)
                            worst = min(worst, dist - gun.ROPE_R)
    return worst


def gun_clear_boxes(f):
    """
    (0.3.21, step C) A gun's working room as the mod keeps it clear (MountLayout.GunClear, ported): eight slices of its
    trail's length (from its pivot to 0.8 behind the gun captain's place, 0.9 wide), each the box round that slice turned
    through its whole train (a degree at a time), and its sel and box grown by 0.05. Plan boxes (x0, z0, x1, z1).
    """
    o = f["out"]
    piv, seat = f["pivot"], f["seat"]
    t0, t1 = sorted(f["trainLimits"])
    if f.get("trainLimitsGateOpen"):
        g0, g1 = sorted(f["trainLimitsGateOpen"])
        t0, t1 = min(t0, g0), max(t1, g1)
    al = math.hypot(o[0], o[2])
    ax, az = -o[0] / al, -o[2] / al
    ln = math.hypot(seat[0] - piv[0], seat[2] - piv[2]) + 0.8
    sign = 1.0 if o[2] >= 0 else -1.0
    cx, cz = -az, ax
    steps = max(1, int(math.ceil(t1 - t0)))
    a = np.radians(t0 + (t1 - t0) * np.arange(steps + 1) / steps)
    c, sn = np.cos(a)[:, None], np.sin(a)[:, None]
    boxes = []
    for i in range(8):
        u = np.array([ln * i / 8, ln * i / 8, ln * (i + 1) / 8, ln * (i + 1) / 8])[None, :]
        w = np.array([-0.45, 0.45, -0.45, 0.45])[None, :]
        vx, vz = ax * u + cx * w, az * u + cz * w
        xs, zs = piv[0] + vx * c + sign * vz * sn, piv[2] + vz * c - sign * vx * sn
        boxes.append((float(xs.min()), float(zs.min()), float(xs.max()), float(zs.max())))
    for b in (f.get("sel"), f.get("box")):
        if b:
            boxes.append((b[0] - 0.05, b[2] - 0.05, b[3] + 0.05, b[5] + 0.05))
    return boxes


def castle_obstacles(d, deck, stern=False):
    """(0.3.21, step C) What stands on a castle gun's deck that its working room must keep clear of (plan boxes x0 z0 x1
    z1 and what): the placed pieces on that deck (railings, partitions, doors, windows, stairs, ladders, the hearth),
    the design's fixtures standing there (the helm, the binnacle, the capstan, the pump...), the masts through it, the
    wells, fixed ladders' heads and feet, the fore and mizzen shrouds' rails inside the bulwarks, the deck's ends."""
    obs = []
    # ((0.3.24, notes 181, 182) an empty gun place keeps no room: the pieces laid on the deck (stairs, railings...) and
    # the stairs' step-offs are the player's to choose between, the mod refuses a gun while one stands in its room. So
    # only her own fixed things limit a castle gun's train now)
    for p in () if PLACES_YIELD else getattr(d, "modular", []):
        x, y, z = p["at"]
        if abs(y - deck) > 0.01:
            continue
        k = p["kind"]
        if k in ("railing", "partition", "door", "window"):
            obs.append(((x - 0.5, z - 0.1, x + 0.5, z + 0.1) if p.get("along") == "x" else (x - 0.1, z - 0.5, x + 0.1, z + 0.5),
                        f"the {k} at {x:g}, {z:g}"))
        elif k in ("stair", "ladder"):
            up, run = p.get("up", "+x"), float(p.get("length", 3))
            x0_, x1_ = (x - 0.5, x + run + 0.5) if up == "+x" else (x - run - 0.5, x + 0.5)
            obs.append(((x0_, z - 0.5, x1_, z + 0.5), f"the {p.get('code', k)}"))
        elif k == "hatch":
            nx_, nz_ = p["size"]
            obs.append(((x - nx_ / 2, z - nz_ / 2, x + nx_ / 2, z + nz_ / 2), "a hatch"))
        elif k == "flue":
            continue                # ((0.3.23) on the deck it comes out on: below)
        else:
            obs.append(((x - 0.6, z - 0.6, x + 0.6, z + 0.6), f"the {k}"))
    for p in () if PLACES_YIELD else getattr(d, "modular", []):
        # ((0.3.23) the castle guns: what of a piece below comes up onto this deck: a stair's step off its head (the cell
        # past it, on the deck it climbs to) and a flue's chimney over the deck it comes out on)
        x, y, z = p["at"]
        if p["kind"] in ("stair", "ladder") and abs(y + p.get("rise", 3.0) - deck) < 0.01:
            sg = 1.0 if p.get("up", "+x") == "+x" else -1.0
            n = p.get("length", 3 if p["kind"] == "stair" else 2)
            xh = x - sg * 0.5 + sg * n            # the head cell's far edge
            xa_, xb_ = sorted((xh, xh + sg * 1.0))
            obs.append(((xa_, z - 0.5, xb_, z + 0.5), f"the step off the {p.get('code', p['kind'])}"))
        if p["kind"] == "flue" and abs(y - MAIN) < 0.01 and abs(deck - QUARTER) < 0.01:
            obs.append(((x - 0.3, z - 0.3, x + 0.3, z + 0.3), "the hearth's flue"))
    if GRID and abs(deck - QUARTER) < 0.01:
        # ((0.3.23) the catheads on the forecastle deck, from their heels inboard out through the bulwark)
        (cix, ciz), (cox, coz) = CATHEAD
        for s in (1, -1):
            za_, zb_ = sorted((s * (ciz - 0.15), s * (coz + 0.15)))
            obs.append(((cix - 0.15, za_, cox + 0.15, zb_), "a cathead"))
    for f in d.fixtures:
        if f.get("type") in ("gun", "storage", "gate", "nameboard", "pinrail") or not f.get("sel"):
            continue
        b = f.get("box") or f["sel"]
        if b[4] > deck + 0.01 and b[1] < deck + 1.6:
            g_ = 0.3 if f.get("type") in ("helm", "compass") else 0.0       # (the wheel's spokes, the helmsman)
            obs.append(((b[0] - g_, b[2] - g_, b[3] + g_, b[5] + g_), f"the {f['code']}"))
    for name, (mx, (y0, y1, r0, _), _, _) in MASTS.items():
        if y0 < deck < y1:
            obs.append(((mx - r0 - 0.05, -r0 - 0.05, mx + r0 + 0.05, r0 + 0.05), f"the {name}mast"))
    for w in getattr(d, "wells", []):
        b = w["box"]
        if abs(b[4] - deck) < 0.01:
            obs.append(((b[0], b[2], b[3], b[5]), f"the {w.get('code', 'well')} well"))
    for lad in d.ladders:
        for k_ in ("bottom", "top", "exitBottom", "exitTop"):
            q = lad.get(k_)
            if q is not None and abs(q[1] - deck) < 0.3 and not lad.get("outside"):
                obs.append(((q[0] - 0.45, q[2] - 0.45, q[0] + 0.45, q[2] + 0.45), f"the {lad['code']}'s {k_}"))
    # the shroud rails inside the castle bulwarks (masts_and_rigging)
    # ((0.3.24) raised over the ports' lintels (SHROUD_RAIL_UP): over the guns, not in their way)
    for xs_, rail_h in () if SHROUD_RAIL_UP else (((FORE_X - 1.6, FORE_X + 0.2), QUARTER + 0.8 + CASTLE_UP), ((-13.8, -12.6), POOP + 0.8 + CASTLE_UP)):
        if abs(rail_h - 0.8 - CASTLE_UP - deck) < 0.01:
            zi = min(frame_face(x, rail_h) for x in xs_) - 0.3
            for s in (1, -1):
                obs.append(((xs_[0] - 0.4, min(s * zi, s * 9.0), xs_[1] + 0.4, max(s * zi, s * 9.0)), "a shroud rail"))
    # the deck's ends (the castle fronts and the other castle's): the quarterdeck's from the poop front to its own
    if abs(deck - QUARTER) < 0.01:
        obs += [((POOP_FRONT - 9.0, -9.0, POOP_FRONT, 9.0), "the poop front"), ((QD_EDGE, -9.0, FC_EDGE, 9.0), "the waist")]
        if MODULAR:
            # ((0.3.29, note 232: the quarterdeck's third guns "cannot be placed as says someone standing") and the
            # breastworks' railings on the decks' edges, a quarter block's layer either side of their lines: a gun's room
            # that reached over the edge by less than the margin ran into them, and the mod refused the gun there
            # ("something stands in the gun's working room"))
            obs.append(((QD_EDGE - 0.25, -9.0, FC_EDGE + 0.25, 9.0), "the breastworks on the decks' edges"))
    if abs(deck - MAIN) < 0.01:
        obs.append(((FRONT_AFT - 0.0625, -9.0, FRONT_AFT + 0.0625, 9.0), "the great cabin's front"))
    if abs(deck - POOP) < 0.01:
        obs.append(((POOP_FRONT - 0.25, -9.0, POOP_FRONT + 9.0, 9.0), "the poop front"))
        if MODULAR:
            # ((0.3.29, note 232) and its breastwork's railings, their quarter block aft of its line)
            obs.append(((POOP_FRONT - 0.5, -9.0, POOP_FRONT + 9.0, 9.0), "the breastworks on the decks' edges"))
        # ((0.3.24) and its after end: the transom's inside (the gun at -17 beside it)
        xt_ = float(stern_x(POOP + 0.5)) + HULL_T
        if not stern:
            obs.append(((xt_ - 9.0, -9.0, xt_, 9.0), "the transom"))
        else:
            # ((0.3.25) a stern chaser's port is in the transom: its room is clear of her sides instead, the inside
            # planking's line in steps of a quarter block forward from the transom)
            for xa_ in np.arange(xt_ - 0.25, xt_ + 4.0, 0.25):
                zs_ = [float(ceil_z(x_, POOP + 0.5)) for x_ in (xa_, xa_ + 0.25)]
                zs_ = [v for v in zs_ if v > 0.5]
                if not zs_:
                    continue
                zi = min(zs_)
                for s_ in (1, -1):
                    obs.append(((xa_, min(s_ * zi, s_ * 9.0), xa_ + 0.25, max(s_ * zi, s_ * 9.0)), "her side"))
    return obs


PLACES_YIELD = MODULAR      # (0.3.24, notes 181, 182: castle gun places don't keep their rooms from the pieces: castle_obstacles)


def castle_gun_limits(d, entries, margin=0.05):
    """
    (0.3.21, step C) The castle guns' own limits. Elevation: as high as the barrel clears its port's lintel (the
    quarterdeck's and the poop's ports 0.5 high under their rail caps) run out and running in, from 5° down. Train: as
    far either way (GUN_TRAIN at most, and what gun_train_limits left) as keeps its whole working room
    (gun_clear_boxes: the box round each slice of its trail turned through its train) `margin` clear of everything on
    its deck (castle_obstacles): the pair of limits with the most training the shorter way, then in all.
    """
    fixtures = {f["code"]: f for f in d.fixtures if f.get("type") == "gun"}
    d.castle_gun_report = {}
    for e in entries:
        code = e[0]
        f = fixtures[code]
        deck = d.gun_levels[code][0]
        # elevation
        hi = 0.0
        for t in () if PORT_SQUARE else np.arange(0.5, GUN_ELEVATION_LIMITS[1] + 1e-9, 0.5):
            top, sill, lid, housed = port_clearances(d, e, f["trainLimits"], (GUN_ELEVATION_LIMITS[0], float(t)))
            if top < 0.01:
                break
            hi = float(t)
        if PORT_SQUARE:
            hi = f["elevationLimits"][1]        # ((0.3.30) swept: sweep_gun_limits)
        else:
            f["elevationLimits"] = [GUN_ELEVATION_LIMITS[0], hi]
            f["elevation"] = min(GUN_ELEVATION, hi)
        # train
        obs = castle_obstacles(d, deck, stern=code in getattr(d, "stern_guns", ()))

        def hits(a, b):
            g = dict(f, trainLimits=[a, b])
            out = []
            for i_, (x0, z0, x1, z1) in enumerate(gun_clear_boxes(g)):
                for (ox0, oz0, ox1, oz1), why in obs:
                    if PLACES_YIELD and (i_ in (6, 7) or MODULAR and i_ == 5) and (why.endswith("mast") or MODULAR and (
                            why in ("a cathead", "a shroud rail", "the breastworks on the decks' edges") or why.startswith("the ") and why.split()[1].startswith(
                                ("helm", "binnacle", "compass", "capstan", "pump", "bitts", "pinrail", "cleat")))):
                        # ((0.3.29, the user: the bow guns "must swing to both sides as far as the port opening
                        # physically allows") nor of a cathead's heel or her fittings: her fixed things keep clear of the
                        # trail and its gun captain, the crew behind him step round them
                        # ((0.3.24, note 183) a mast only kept clear of the trail and the gun captain's place, not of
                        # the last 0.7 of the crew's room behind him (the mod keeps no room from a mast: it isn't a
                        # piece): the forecastle's guns at 17 abreast the foremast)
                        continue
                    if x0 < ox1 + margin and ox0 - margin < x1 and z0 < oz1 + margin and oz0 - margin < z1:
                        out.append(why)
            return out

        a0, b0 = f["trainLimits"]
        best = None
        # ((0.3.27, note 205) about its centre: her side's normal at its port, not square out)
        c0 = getattr(d, "gun_phi", {}).get(code, 0.0)
        if MODULAR:
            c0 = 0.0        # ((0.3.28, note 226) about square: as far each way as it's clear, the shorter way first)
        c0 = min(max(c0, a0), b0)
        # (where its room isn't clear laid along the normal: the nearest lay toward square that is)
        while abs(c0) > 1e-6 and hits(c0, c0):
            print(f"  {code}: laid {c0:.1f}° its room meets {sorted(set(hits(c0, c0)))}")
            c0 = c0 - 0.5 if c0 > 0 else c0 + 0.5
            if abs(c0) < 0.5:
                c0 = 0.0
        for a in np.arange(0.0, c0 - a0 + 1e-9, 0.25):
            for b in np.arange(0.0, b0 - c0 + 1e-9, 0.25):
                key = (min(a, b), a + b)
                if best is not None and key <= best[0]:
                    continue
                if not hits(c0 - float(a), c0 + float(b)):
                    best = (key, c0 - float(a), c0 + float(b))
        if best is None and os.environ.get("HANSA_EXPLORE"):
            print(f"  EXPLORE {code}: not clear laid square: {sorted(set(hits(0.0, 0.0)))}")
            best = ((0, 0), 0.0, 0.0)
        assert best is not None, f"{code}: its working room isn't clear even laid square: {hits(0.0, 0.0)}"
        why = sorted(set(hits(a0, b0)))
        # ((0.3.29) trained that far its barrel clears its lid opened (the lid over the port: port_clearances)
        lo_, hi_ = best[1], best[2]
        if MODULAR:
            while hi_ > 0.25 and port_clearances(d, e, [0.0, hi_], f["elevationLimits"])[2] < 0.005:
                hi_ -= 0.25
            while lo_ < -0.25 and port_clearances(d, e, [lo_, 0.0], f["elevationLimits"])[2] < 0.005:
                lo_ += 0.25
            if (lo_, hi_) != (best[1], best[2]):
                why = why + ["its lid opened"]
        best = (best[0], lo_, hi_)
        if best[1] > f["trainLimits"][0] + 1e-9:
            sweep_set_why(d, code, 0, "room: " + ", ".join(sorted(set(hits(best[1] - 0.25, best[2])) - set(hits(best[1], best[2]))) or why))
        if best[2] < f["trainLimits"][1] - 1e-9:
            sweep_set_why(d, code, 1, "room: " + ", ".join(sorted(set(hits(best[1], best[2] + 0.25)) - set(hits(best[1], best[2]))) or why))
        f["trainLimits"] = [best[1], best[2]]
        bx_ = gun_clear_boxes(f)
        room = (min(b[0] for b in bx_), min(b[1] for b in bx_), max(b[2] for b in bx_), max(b[3] for b in bx_))
        d.castle_gun_report[code] = {"train": [best[1], best[2]], "elevation": f["elevationLimits"],
                                     "short of": why, "room": [round(v, 2) for v in room]}
        print(f"  {code} (deck {deck - WATER:+.3f}): trains {best[1]:.2f}..{best[2]:.2f}°" +
              (f" (its room clear of {', '.join(why)})" if why else "") + f", laid {GUN_ELEVATION_LIMITS[0]:.0f}..{hi:.1f}°" +
              f", its room x {room[0]:.2f}..{room[2]:.2f} z {room[1]:.2f}..{room[3]:.2f}")
    # ((0.3.23) two castle guns abreast on one deck (port and starboard) keep their rooms apart: they're run back and
    # worked at once (neighbours on one side share room as the waist's do: gun_train_limits keeps them apart)
    rooms = {e[0]: (d.gun_levels[e[0]][0], gun_clear_boxes(fixtures[e[0]])) for e in entries}
    side = {e[0]: e[1] for e in entries}
    clash = [(a, b) for a in rooms for b in rooms if a < b and side[a] != side[b] and abs(rooms[a][0] - rooms[b][0]) < 0.01 and
             any(p[0] < q[2] and q[0] < p[2] and p[1] < q[3] and q[1] < p[3] for p in rooms[a][1] for q in rooms[b][1])]
    # ((0.3.24, notes 181, 182: symmetric on both sides) where the deck is narrow (the forecastle's at 19) two guns abreast
    # share the middle of it, the ends of their crews' room behind the gun captains: fine, as long as neither gun (its
    # carriage run out, its box) stands in the other's room: both can be mounted and worked)
    def box_in(a, b):
        bx_ = fixtures[a]["box"]
        return any(bx_[0] < q[2] and q[0] < bx_[3] and bx_[2] < q[3] and q[1] < bx_[5] for q in rooms[b][1])
    hard = [(a, b) for a, b in clash if box_in(a, b) or box_in(b, a)]
    print(f"  fit: the castle guns abreast: rooms sharing the deck's middle {clash or 'none'}; a gun in the other's room: "
          f"{hard or 'none'}")
    if not os.environ.get("HANSA_EXPLORE"):
        assert not hard, hard
    # ((0.3.25) the stern chasers share the poop's after end with the aftmost broadside guns on their side: where one
    # stands in the other's room the mod mounts only one of the two (docs/slots.md §7))
    excl = [(a, b) for a in rooms for b in rooms if a < b and side[a] == side[b] and abs(rooms[a][0] - rooms[b][0]) < 0.01
            and (box_in(a, b) or box_in(b, a))]
    d.gun_exclusive = excl
    print(f"  fit: castle guns on one side standing in each other's room (one or the other mounted): {excl or 'none'}")


def port_jamb_limits(d, entry, cap, clear=0.01):
    """
    (0.3.29) How far a gun trains each way (degrees, - aft, + toward the bow; at most cap) before its barrel, run out or
    running in at any elevation it's laid to, meets its port's cheeks: the barrel's surface where it's in her side's
    thickness (the waist's and the castles' ports: jambs square across her at the port's ends; a stern chaser's: across
    the transom) kept `clear` inside them.
    """
    import gun
    code, s, gx, info, quads = entry
    f = info["frame"]
    piv = np.asarray(info["pivot"], float)
    pts = []
    for x in np.arange(gun.PROFILE[0][0], gun.MUZZLE_X + 1e-9, 0.03):
        r = gun.radius(x)
        for k in range(16):
            a = 2 * math.pi * k / 16
            pts.append((x, gun.AXIS_Y + r * math.cos(a), r * math.sin(a)))
    pts = np.array(pts)
    stern = code in getattr(d, "stern_guns", ())
    lo_e, hi_e = GUN_ELEVATION_LIMITS
    bases = [np.array([f(*p) for p in gun.elevated(pts, e)]) for e in (lo_e, 0.0, hi_e)]

    def ok(t):
        phi = math.radians(t) * gun.train_sign(f.u)
        for base in bases:
            for back in (0.0, 0.3, 0.6):
                w = base - f.u * back
                dx, dz = w[:, 0] - piv[0], w[:, 2] - piv[2]
                wx, wz = piv[0] + dx * math.cos(phi) + dz * math.sin(phi), piv[2] - dx * math.sin(phi) + dz * math.cos(phi)
                if stern:
                    xo = stern_xv(w[:, 1])
                    inwall = (wx > xo - 0.01) & (wx < xo + HULL_T + 0.01)
                    a_, b_ = gx - GUNPORT_W / 2, gx + GUNPORT_W / 2
                    if inwall.any() and ((wz[inwall] < a_ + clear).any() or (wz[inwall] > b_ - clear).any()):
                        return False
                else:
                    hwv = np.asarray(half_width(wx, w[:, 1]), float)
                    az = np.abs(wz)
                    inwall = (az > hwv - HULL_T - 0.01) & (az < hwv + 0.01)
                    a_, b_ = gx - GUNPORT_W / 2, gx + GUNPORT_W / 2
                    if inwall.any() and ((wx[inwall] < a_ + clear).any() or (wx[inwall] > b_ - clear).any()):
                        return False
        return True

    out = []
    for sg in (-1, 1):
        t_ok = 0.0
        for t in np.arange(0.25, cap + 1e-9, 0.25):
            if not ok(sg * float(t)):
                break
            t_ok = float(t)
        out.append(sg * t_ok)
    return out


# ==================================================================== (0.3.30, note 237) the guns' arcs, swept
SWEEP_STEP = 0.02           # obstacle surfaces sampled every this much (the offline test samples them the same way)
SWEEP_MARGIN = 0.001        # the barrel and the carriage this much bigger while their limits are searched
SWEEP_TRAIN_CAP = 60.0      # no gun trains further than this either way (none gets near it: its port stops it)
SWEEP_ELEV_CAP = (-10.0, 15.0)   # nor is laid lower or higher (its port's sill and lintel stop it first)
SWEEP_BACKS = (0.0, 0.375, 0.75)  # run out, half and all the way back (gun.RECOIL)


def sweep_points(quads, step=SWEEP_STEP):
    """
    (0.3.30) Points over these quads' surfaces (each quad's corners p0 p1 p2 p3, bilinear: every `step` or closer along
    p0-p1 and p0-p3, both ends included). The offline test (NoteTests0330) samples the written mesh the same way.
    """
    out = []
    for pts in quads:
        P = np.asarray(pts, float)
        if len(P) == 3:
            P = np.vstack([P, P[2:3]])
        lu = max(float(np.linalg.norm(P[1] - P[0])), float(np.linalg.norm(P[2] - P[3])))
        lv = max(float(np.linalg.norm(P[3] - P[0])), float(np.linalg.norm(P[2] - P[1])))
        nu, nv = max(1, int(math.ceil(lu / step - 1e-9))), max(1, int(math.ceil(lv / step - 1e-9)))
        u = np.linspace(0.0, 1.0, nu + 1)[:, None, None]
        v = np.linspace(0.0, 1.0, nv + 1)[None, :, None]
        g = (1 - u) * (1 - v) * P[0] + u * (1 - v) * P[1] + u * v * P[2] + (1 - u) * v * P[3]
        out.append(g.reshape(-1, 3))
    return np.concatenate(out) if out else np.zeros((0, 3))


def sweep_obstacle_quads(d, piv, deck, reach=2.6):
    """(0.3.30) Her own surfaces round a gun (every quad but the moving parts', the shot's and the sails'), within `reach`
    of its pivot across the deck and from 0.03 over its deck to 1.5 over it: what its barrel and carriage must keep out of."""
    cache = getattr(d, "_sweep_cache", None)
    if cache is None or cache[0] != len(d.quads) or cache[1] is not d.quads[-1]:
        idx, Ps = [], []
        for i, q in enumerate(d.quads):
            g = q["group"]
            if g.startswith("part:") or g == "shot" or g.startswith("sail"):
                continue
            pts = q["pts"]
            if len(pts) == 3:
                pts = list(pts) + [pts[2]]
            if len(pts) != 4:
                continue
            idx.append(i)
            Ps.append(pts)
        A = np.asarray(Ps, float)
        c = A.mean(axis=1)
        rad = np.max(np.linalg.norm(A - c[:, None, :], axis=2), axis=1)
        cache = (len(d.quads), d.quads[-1], A, c, rad, A[:, :, 1].min(axis=1), A[:, :, 1].max(axis=1))
        d._sweep_cache = cache
    _, _, A, c, rad, ymin, ymax = cache
    m = (ymax >= deck + 0.03) & (ymin <= deck + 1.5) & (np.hypot(c[:, 0] - piv[0], c[:, 2] - piv[2]) <= reach + rad)
    return list(A[m])


def sweep_barrel():
    """(0.3.30) The barrel's outer radius along it (gun-local x from the button to the muzzle, every 0.002; rounded as
    written into her design's "gunSweep" for the offline test)."""
    import gun
    xs = np.round(np.arange(gun.PROFILE[0][0], gun.MUZZLE_X + 1e-9, 0.002), 5)
    return xs, np.round(np.array([gun.radius(float(x)) for x in xs]), 5)


def sweep_model(d):
    """(0.3.30) What the offline test needs to sweep a gun as sweep_gun_limits does: the barrel's radii, the carriage's
    boxes (carriage frame), the axis' height, the runs back, the sampling step, the obstacles' rule."""
    import gun
    bx, br = sweep_barrel()
    sw = GunSweep.__new__(GunSweep)
    GunSweep.carriage_boxes(sw, gun)
    return {"axisY": gun.AXIS_Y, "barrelX": [float(v) for v in bx], "barrelR": [float(v) for v in br],
            "boxes": [[round(float(v), 5) for v in b] for b in sw.boxes], "backs": list(SWEEP_BACKS), "step": SWEEP_STEP,
            "over": 0.03, "reach": 1.5}


class GunSweep:
    """
    (0.3.30, note 237: "Check all guns firing arcs to allow maximum left right up and down based on conflicts with
    ports") A gun against her own surfaces round it: its barrel (a body of revolution, gun.PROFILE's outer radius) and
    its carriage (the cheeks, the transom, the axletrees, the trucks with their hubs: boxes) laid (trained about its
    pivot, elevated about its trunnions) and run back, against points over every surface of hers near it
    (sweep_points). The same model the offline test uses on the written mesh.
    """

    def __init__(self, d, entry, deck):
        import gun
        self.gun = gun
        code, s, gx, info, quads = entry
        self.code = code
        self.f = info["frame"]
        self.piv = np.asarray(info["pivot"], float)
        self.deck = deck
        pts = sweep_points(sweep_obstacle_quads(d, self.piv, deck))
        keep = (pts[:, 1] > deck + 0.03) & (pts[:, 1] < deck + 1.5) if len(pts) else np.zeros(0, bool)
        self.pts = pts[keep]
        self.sign = gun.train_sign(self.f.u)
        self.bx, self.br = sweep_barrel()
        self.carriage_boxes(gun)

    def carriage_boxes(self, G):
        boxes = []
        for x0, x1, top, bottom in G.CHEEKS:
            for z in (1, -1):
                boxes.append((x0, bottom, z * G.CHEEK_Z - G.CHEEK_T / 2, x1, top, z * G.CHEEK_Z + G.CHEEK_T / 2))
        t0, t1, ty0, ty1 = G.TRANSOM
        boxes.append((t0, ty0, -G.CHEEK_Z, t1, ty1, G.CHEEK_Z))
        for ax in G.AXLES.values():
            boxes.append((ax - 0.065, G.TRUCK_R - 0.055, -0.4, ax + 0.065, 0.205, 0.4))
            for z in (1, -1):
                za, zb = sorted((z * (G.TRUCK_Z - G.TRUCK_T / 2), z * (G.TRUCK_Z + G.TRUCK_T / 2 + 0.045)))
                boxes.append((ax - G.TRUCK_R, 0.0, za, ax + G.TRUCK_R, 2 * G.TRUCK_R, zb))
        self.boxes = np.round(np.array(boxes, float), 5)

    def local(self, train):
        """Her points in the carriage's own frame (x out along the gun, y up, z across), the gun trained so."""
        if not len(self.pts):
            return self.pts
        phi = -math.radians(train) * self.sign
        dx, dz = self.pts[:, 0] - self.piv[0], self.pts[:, 2] - self.piv[2]
        c, sn = math.cos(phi), math.sin(phi)
        q = np.stack([self.piv[0] + dx * c + dz * sn, self.pts[:, 1], self.piv[2] - dx * sn + dz * c], axis=1)
        return (q - self.f.o) @ self.f.R

    def hits(self, train, elevs, backs=SWEEP_BACKS, margin=SWEEP_MARGIN, carriage=True, why=False):
        """Does anything of hers come inside the gun so laid (each elevation in elevs, each run back in backs)?"""
        L = self.local(train)
        if not len(L):
            return [] if why else False
        found = []
        near = ((L[:, 0] > self.bx[0] - 0.3 - max(backs) - 0.1) & (L[:, 0] < self.bx[-1] + 0.3) & (np.abs(L[:, 2]) < 0.6)
                & (L[:, 1] < 1.2))
        P0 = self.pts[near]
        L = L[near]
        if not len(L):
            return [] if why else False
        G = self.gun
        for b in backs:
            M = L.copy()
            M[:, 0] += b
            if carriage:
                for bx in self.boxes:
                    inside = ((M[:, 0] > bx[0] - margin) & (M[:, 0] < bx[3] + margin) & (M[:, 1] > bx[1] - margin) &
                              (M[:, 1] < bx[4] + margin) & (M[:, 2] > bx[2] - margin) & (M[:, 2] < bx[5] + margin))
                    if inside.any():
                        if not why:
                            return True
                        found.append(("carriage", b, None, P0[inside][0]))
            for e in elevs:
                er = -math.radians(e)
                x, y = M[:, 0], M[:, 1] - G.AXIS_Y
                xb = x * math.cos(er) - y * math.sin(er)
                yb = x * math.sin(er) + y * math.cos(er)
                inb = (xb >= self.bx[0]) & (xb <= self.bx[-1])
                if not inb.any():
                    continue
                r = np.hypot(yb[inb], M[inb, 2])
                lim = np.interp(xb[inb], self.bx, self.br) + margin
                if (r < lim).any():
                    if not why:
                        return True
                    found.append(("barrel", b, e, P0[inb][r < lim][0]))
        return found if why else False


def sweep_gun_limits(d, entries):
    """
    (0.3.30, note 237) Each gun's own arc from its port: laid square (its side board's perpendicular, train 0), as high
    and as low as its barrel clears her (its port's lintel and sill, its lid opened) run out, half and all the way back;
    then trained each way (a quarter degree at a time, then a twentieth) as far as its barrel, laid anywhere between those,
    and its carriage clear everything of hers (the port's jambs, her side, the frames inside it, the knees, the ring
    bolts) run out or back. Sets each fixture's trainLimits and elevationLimits; d.sweep[code] keeps what stopped each.
    """
    fixtures = {f["code"]: f for f in d.fixtures if f.get("type") == "gun"}
    if not hasattr(d, "sweep"):
        d.sweep = {}
    if FAST:
        for e in entries:
            fixtures[e[0]]["trainLimits"] = [-3.0, 3.0]
            d.sweep[e[0]] = {"why": ["fast", "fast"], "elevs": [0.0], "what": ["", ""]}
        print(f"  (HANSA_FAST: {len(entries)} guns' arcs not swept, +-3 degrees)")
        return
    for e in entries:
        code = e[0]
        f = fixtures[code]
        deck = d.gun_levels[code][0]
        sw = GunSweep(d, e, deck)
        if sw.hits(0.0, [0.0]):
            w = sw.hits(0.0, [0.0], why=True)
            print(f"  SWEEP {code}: not clear laid square: {w[:2]}")

        def lid_ok(lim, el):
            if not f.get("lid"):
                return True
            return port_clearances(d, e, lim, el)[2] >= 0.005

        def train_search(sg, elevs_, lo_, hi_):
            """How far it trains that way (sg) clear laid at each of elevs_, and what stops it."""
            ok_t, why_ = 0.0, "cap"

            def bad_(t):
                if sw.hits(sg * t, elevs_):
                    return "hull"
                if not lid_ok([sg * t, 0.0] if sg < 0 else [0.0, sg * t], (lo_, hi_)):
                    return "lid"
                return None

            # (a degree at a time, then a quarter, then a twentieth)
            for step in (1.0, 0.25, 0.05):
                t = ok_t + step
                while t <= SWEEP_TRAIN_CAP + 1e-9:
                    w_ = bad_(t)
                    if w_:
                        why_ = w_
                        break
                    ok_t = round(t, 2)
                    t += step
                if why_ == "cap":
                    break
            return round(ok_t, 2), why_

        # 1. its train laid level: as far each way as it's clear
        t_lo, _ = train_search(-1.0, [0.0], 0.0, 0.0)
        t_hi, _ = train_search(1.0, [0.0], 0.0, 0.0)
        # 2. its elevation: as high and as low as it's clear trained anywhere in that (the corners of its arc: its port's
        # sill and lintel meet the barrel trained to the side first), the lid opened over it
        trains = sorted(set([-t_lo, 0.0, t_hi] + [float(v) for v in np.arange(-t_lo, t_hi + 1e-9, 2.5)]))

        def elev_ok(t):
            return all(not sw.hits(tr, [float(t)], carriage=False) for tr in trains) and \
                (t <= 0 or lid_ok([-t_lo, t_hi], (0.0, float(t))))

        hi = lo = 0.0
        for sgn, cap_ in ((1.0, SWEEP_ELEV_CAP[1]), (-1.0, SWEEP_ELEV_CAP[0])):
            ok_e = 0.0
            for step in (1.0, 0.25):
                t = ok_e + sgn * step
                while abs(t) <= abs(cap_) + 1e-9 and elev_ok(t):
                    ok_e = t
                    t += sgn * step
            if sgn > 0:
                hi = ok_e
            else:
                lo = ok_e
        elevs = sorted(set([lo, hi, 0.0] + [float(v) for v in np.arange(math.ceil(lo / 5.0) * 5.0, hi + 1e-9, 5.0)]))
        res = {"elevation": [lo, hi], "why": ["", ""], "elevs": elevs}
        # 3. its train again laid anywhere between those (no less than step 1's but where the corners stop it)
        lims = []
        for k, sg in enumerate((-1.0, 1.0)):
            ok_t, why_ = train_search(sg, elevs, lo, hi)
            lims.append(sg * ok_t)
            res["why"][k] = why_
        f["trainLimits"] = [lims[0], lims[1]]
        f["elevationLimits"] = [lo, hi]
        f["elevation"] = min(max(GUN_ELEVATION, lo), hi)
        res["train"] = list(f["trainLimits"])
        res["swept"] = list(f["trainLimits"])
        d.sweep[code] = res
        w0 = sw.hits(lims[0] - 1.0, elevs, why=True) if res["why"][0] == "hull" else []
        w1 = sw.hits(lims[1] + 1.0, elevs, why=True) if res["why"][1] == "hull" else []
        def what_(w):
            if not w:
                return ""
            k_, b_, e_, p_ = w[0]
            q_ = p_ - np.array([0.0, WATER, 0.0])
            return f"{k_} (run back {b_}, laid {e_}) at {q_[0]:.2f} {q_[1]:.2f} {q_[2]:.2f}"

        res["what"] = [what_(w0), what_(w1)]
        print(f"  {code}: swept: trains {lims[0]:.2f}..{lims[1]:.2f} ({res['why'][0]} {res['what'][0]} / {res['why'][1]} "
              f"{res['what'][1]}), laid {lo:.2f}..{hi:.2f} ({len(sw.pts)} points of hers round it)"
              + ("" if (res["why"][0] != "hull" or w0) and (res["why"][1] != "hull" or w1) else "  !! not hit 1 deg beyond"))


def sweep_verify(d):
    """(0.3.30) Every gun, against her quads as they're written (all of her built: her panels' reveals, the pieces' places),
    clear trained to either limit laid anywhere it can be (its sweep's elevations) and run out or back; and where her
    own surfaces stop it, inside them a degree beyond."""
    bad, n = [], 0
    for e in getattr(d, "guns_placed", []):
        f = next(x for x in d.fixtures if x.get("code") == e[0])
        if "trainBound" not in f:
            continue
        sw = GunSweep(d, e, d.gun_levels[e[0]][0])
        el = f["sweepElevations"]
        for k, t in enumerate(f["trainLimits"]):
            n += 1
            if sw.hits(t, el, margin=0.0):
                bad.append(f"{e[0]} at {t}: {sw.hits(t, el, margin=0.0, why=True)[:1]}")
            if f["trainBound"][k] == "hull" and not sw.hits(t + (1.0 if k else -1.0), el, margin=0.0):
                bad.append(f"{e[0]}: not stopped a degree past {t}")
    print(f"  fit: the guns' arcs against her as written: {n} limits, {len(bad)} wrong" + (f": {bad}" if bad else ""))
    assert not bad, bad


def sweep_set_why(d, code, k, why):
    """(0.3.30) What stops a gun's train that way now (k 0: the lower limit, 1 the upper), once something else than her
    own surfaces trims it."""
    if hasattr(d, "sweep") and code in d.sweep:
        d.sweep[code]["why"][k] = why


def gun_train_limits(d, placed, margin=0.08):
    """
    How far each gun may train toward its neighbour on the same side: both trained toward each other that far and both
    run back (where they reach furthest), the after one's furthest-forward point stays `margin` short of the forward
    one's furthest-aft (a top view of every vertex of the gun). And toward an
    entry port's gate: clear of where its leaf swings (shut to wide open), run out or run back. Otherwise GUN_TRAIN.
    """
    import gun
    fixtures = {f["code"]: f for f in d.fixtures if f.get("type") == "gun"}

    def top(entry, train_deg, back=gun.RECOIL):
        return gun_top(entry, train_deg, back)

    levels = getattr(d, "gun_levels", {})
    # ((0.3.29, the user: the bow guns "must swing to both sides as far as the port opening physically allows") the castle
    # guns from GUN_TRAIN_CASTLE either way, each first as far as its barrel clears its port's cheeks (port_jamb_limits),
    # then its neighbours, its room (castle_gun_limits), its breeching
    d.jamb_limits = {}
    if PORT_SQUARE:
        # ((0.3.30, note 237) every gun's own arc swept against her surfaces: sweep_gun_limits; then its neighbours etc.)
        sweep_gun_limits(d, placed)
    for e in () if PORT_SQUARE else placed:
        f = fixtures[e[0]]
        castle = MODULAR and levels.get(e[0], (MAIN,))[0] > MAIN + 0.01
        cap = GUN_TRAIN_CASTLE if castle else GUN_TRAIN
        jl = port_jamb_limits(d, e, cap)
        d.jamb_limits[e[0]] = jl
        if castle:
            f["trainLimits"] = [-cap, cap]
        f["trainLimits"] = [max(f["trainLimits"][0], jl[0]), min(f["trainLimits"][1], jl[1])]
        print(f"  {e[0]}: its barrel clears its port's cheeks trained {jl[0]:.2f}..{jl[1]:.2f}°")
    rows = [sorted([e for e in placed if e[1] == s and levels.get(e[0], (MAIN,))[0] == deck], key=lambda e: e[2])
            for s in (-1, 1) for deck in sorted({levels.get(e[0], (MAIN,))[0] for e in placed})]
    for row in rows:        # ((0.3.21) each side's guns on each deck)
        for a, b in zip(row[:-1], row[1:]):
            theta = 0.0
            # ((0.3.27, note 205) from each one's own centre: her side's normal at its port)
            pa, pb = getattr(d, "gun_phi", {}).get(a[0], 0.0), getattr(d, "gun_phi", {}).get(b[0], 0.0)
            if MODULAR:
                pa = pb = 0.0       # ((0.3.28, note 226) from square)
            t_cap = SWEEP_TRAIN_CAP if PORT_SQUARE else max(GUN_TRAIN, GUN_TRAIN_CASTLE)
            for t in np.arange(0.25, t_cap + 1e-9, 0.25):
                # the after one's muzzle trained aft swings its breech forward; the forward one's trained forward, aft
                if top(a, pa - t)[0].max() + margin <= top(b, pb + t)[0].min():
                    theta = float(t)
                else:
                    break
            fa, fb = fixtures[a[0]], fixtures[b[0]]
            theta_a = theta_b = theta
            if MODULAR and not PORT_SQUARE and "forecastle" in a[0] and "forecastle" in b[0]:
                # ((0.3.29, the user: the bow guns "must swing to both sides as far as the port opening physically allows")
                # the forward one's train toward the bow first (what bow guns are for), the after one's toward her stern
                # what's left of it, not under BOW_PAIR_AFT
                best_b = theta
                for tb in np.arange(theta + 0.25, GUN_TRAIN_CASTLE + 1e-9, 0.25):
                    if top(a, pa - BOW_PAIR_AFT)[0].max() + margin <= top(b, pb + tb)[0].min():
                        best_b = float(tb)
                    else:
                        break
                theta_b = best_b
                theta_a = min(theta, BOW_PAIR_AFT) if best_b > theta else theta
                for ta in np.arange(theta_a + 0.25, GUN_TRAIN_CASTLE + 1e-9, 0.25):
                    if top(a, pa - ta)[0].max() + margin <= top(b, pb + theta_b)[0].min():
                        theta_a = float(ta)
                    else:
                        break
            if pa - theta_a > fa["trainLimits"][0]:
                fa["trainLimits"][0] = pa - theta_a
                sweep_set_why(d, a[0], 0, f"neighbour {b[0]}")
            if pb + theta_b < fb["trainLimits"][1]:
                fb["trainLimits"][1] = pb + theta_b
                sweep_set_why(d, b[0], 1, f"neighbour {a[0]}")
            print(f"  {a[0]} / {b[0]} ({b[2] - a[2]:.2f} apart): {a[0]} trains {theta_a:.2f}° toward {b[0]} at most, {b[0]} {theta_b:.2f}° toward it")

    # the entry ports' gates: a leaf (the bulwark's thickness, a hair more) hinged at its forward inboard corner
    # (gate_hinge), swinging GATE_OPEN inboard.
    # Shut, it's in her side; swinging open it sweeps in beside the gun forward of it, whose trail swings aft as it's
    # trained toward the bow: then the gun trains less that way while the gate stands open (the mod won't open the gate
    # while the gun's trained further)
    leaf = PORT_X[1] - PORT_X[0]

    def gate_clear(e, train_deg, s, hinge, angles):
        for back in (0.0, gun.RECOIL / 2, gun.RECOIL):
            x, z = top(e, train_deg, back)
            for a_deg in angles:
                # the leaf's middle line (gate_leaf): aft along the side when shut, turned inboard as it opens
                p0, p1 = gate_leaf(s, a_deg)
                u = (p1 - p0) / leaf
                px, pz = x - p0[0], z - p0[1]
                along = np.clip(px * u[0] + pz * u[1], 0, leaf)
                if (np.hypot(px - along * u[0], pz - along * u[1]) < HULL_T / 2 + margin).any():
                    return False
        return True

    # the boat gallows' posts (since 0.3.6 frames carried up over the bulwark: at the deck they're the frame standing in
    # from the side, x0 x1 and its inner face out to the inside planking): clear of them, run out or run back
    for e in placed:
        code, s, gx, info, quads = e
        f = fixtures[code]
        for px0, px1, pzi, pzo in getattr(d, "gallows_posts", []):
            if abs(gx - (px0 + px1) / 2) > 3.0:
                continue
            m = margin / 2
            for sign, k in ((-1, 0), (1, 1)):
                theta = 0.0
                for t in np.arange(0.0, abs(f["trainLimits"][k]) + 1e-9, 0.25):
                    if any(((x > px0 - m) & (x < px1 + m) & (s * z > pzi - m) & (s * z < pzo + m)).any()
                           for x, z in (top(e, sign * t, back) for back in (0.0, gun.RECOIL / 2, gun.RECOIL))):
                        break
                    theta = float(t)
                if theta < abs(f["trainLimits"][k]):
                    f["trainLimits"][k] = sign * theta
                    sweep_set_why(d, code, k, "gallows post")
                    print(f"  {code}: trains {theta:.2f}° {'forward' if sign > 0 else 'aft'} at most, clear of the gallows' post")

    for e in placed:
        code, s, gx, info, quads = e
        hinge = gate_hinge(s)
        if abs(gx - hinge[0]) > leaf + 2.5:
            continue
        f = fixtures[code]
        open_limits = list(f["trainLimits"])
        for sign, k in ((-1, 0), (1, 1)):
            for angles, limits in (((0,), f["trainLimits"]), (np.linspace(0.0, GATE_OPEN, 23), open_limits)):
                theta = 0.0
                for t in np.arange(0.0, abs(limits[k]) + 1e-9, 0.25):
                    if not gate_clear(e, sign * t, s, hinge, angles):
                        break
                    theta = float(t)
                if theta < abs(limits[k]) and limits is f["trainLimits"]:
                    sweep_set_why(d, code, k, "gate")
                limits[k] = sign * min(abs(limits[k]), theta)
        if open_limits != f["trainLimits"]:
            f["gate"] = f"gate-{'starboard' if s > 0 else 'port'}"
            f["trainLimitsGateOpen"] = open_limits
            print(f"  {code}: trains {open_limits[0]:.2f}..{open_limits[1]:.2f}° while the gate beside it stands open")

    # (0.3.19, docs/modular-pieces.md §8) a walked stair's cells free of a gun's working room as the mod keeps it clear
    # (MountLayout.GunClear: the trail from the pivot in to 0.8 behind the gun captain's place, 0.45 either side, swept
    # through its train limits, in 8 boxes along it): a waist gun beside a castle stair's foot (starboard 1 by the
    # quarterdeck's, starboard 5 by the forecastle's) trains that much less toward it only
    def clear_boxes(f, t0, t1):
        piv, seat = f["pivot"], f["seat"]
        o = f["out"]
        al = math.hypot(o[0], o[2])
        ax, az = -o[0] / al, -o[2] / al
        ln = math.hypot(seat[0] - piv[0], seat[2] - piv[2]) + 0.8
        sign = 1.0 if o[2] >= 0 else -1.0
        cx, cz = -az, ax
        steps = max(1, int(math.ceil(t1 - t0)))
        out = []
        for i in range(8):
            xs, zs = [], []
            for k in range(steps + 1):
                a = math.radians(t0 + (t1 - t0) * k / steps)
                c, sn = math.cos(a), math.sin(a)
                for u in (ln * i / 8, ln * (i + 1) / 8):
                    for w in (-0.45, 0.45):
                        vx, vz = ax * u + cx * w, az * u + cz * w
                        xs.append(piv[0] + vx * c + sign * vz * sn)
                        zs.append(piv[2] + vz * c - sign * vx * sn)
            out.append((min(xs), min(zs), max(xs), max(zs)))
        return out

    for e in placed:
        code, s, gx, info, quads = e
        f = fixtures[code]
        for st in d.stairs:
            if "flight" not in st or st["floor"] != MAIN:
                continue
            x_low, sg, _, z0, z1, _, run_ = st["flight"]
            sx0, sx1 = sorted((x_low, x_low + sg * run_))
            t0, t1 = sorted(f["trainLimits"])

            def hits(a, b):
                # ((0.3.24, note 176: every gun mirrored port and starboard exactly) the stair's cells and their mirror
                # image across the centreline: the two guns abreast trimmed alike)
                return any(bx0 < sx1 - 1e-3 and bx1 > sx0 + 1e-3 and bz0 < zb - 1e-3 and bz1 > za + 1e-3
                           for bx0, bz0, bx1, bz1 in clear_boxes(f, a, b) for za, zb in ((z0, z1), (-z1, -z0)))
            while hits(t0, t1) and t1 - t0 > 0:
                # (the side whose sweep alone reaches the stair gives way)
                if hits(t0, 0.0) and t0 < 0:
                    t0 += 0.25
                elif t1 > 0:
                    t1 -= 0.25
                else:
                    break
            if [t0, t1] != sorted(f["trainLimits"]):
                if t0 != sorted(f["trainLimits"])[0]:
                    sweep_set_why(d, code, 0, f"{st['code']} stair")
                if t1 != sorted(f["trainLimits"])[1]:
                    sweep_set_why(d, code, 1, f"{st['code']} stair")
                f["trainLimits"] = [t0, t1]
                print(f"  {code}: trains {t0:.2f}..{t1:.2f}°, its crew's room clear of the {st['code']} stair's cells")


# ==================================================================== sails (one group each, furled by the mod)
def sails(d):
    def square_sail(code, mx, y_head, y_foot, half_head, half_foot, bulge, cross):
        def P(u, v):
            z = -((1 - v) * half_head + v * half_foot) + 2 * u * ((1 - v) * half_head + v * half_foot)
            y = y_head - v * (y_head - y_foot)
            x = mx + 0.55 + bulge * (1 - (2 * u - 1) ** 2) * math.sin(math.pi * min(1.0, v * 0.9 + 0.1))
            return (x, y, z)

        def mat(iu, iv, u, v):
            if cross and ((0.42 < u < 0.58 and 0.12 < v < 0.88) or (0.32 < v < 0.48 and 0.25 < u < 0.75)):
                return "redcloth"
            return "cloth"

        d.add_sheet(code, mat, P, 12, 10)
        d.sails.append({"code": code, "axisPoint": [mx + 0.55, y_head, 0.0], "axisDir": [0.0, 0.0, 1.0],
                        "area": (y_head - y_foot) * (half_head + half_foot)})

    # The courses start ~3.5 blocks above the helmsman's eyes (quarterdeck 6.5 + 1.6) so there's a clear view ahead.
    square_sail("main-course", MAIN_X, 20.0, 11.7, 7.0, 7.1, 1.3, True)
    # the topsail's foot is above the eyes of someone standing in the main top
    square_sail("main-topsail", MAIN_X, YARDS["main-topsail"][1] - 0.25, TOPSAIL_FOOT, 4.2, 5.6, 0.8, False)
    square_sail("fore-course", FORE_X, 17.0, 10.2, 5.6, 5.7, 1.0, True)

    # lateen: triangle under the yard, bellied to starboard, its foot well above the poop deck rail
    clew = np.array([-16.3, POOP + 2.8, 0.5])
    ya, yb = LATEEN_A + np.array([0, -0.2, 0]), LATEEN_B + np.array([0, -0.2, 0])
    L = float(np.linalg.norm(yb - ya))
    eu = (yb - ya) / L
    ev0 = clew - ya
    ev = ev0 - np.dot(ev0, eu) * eu
    cd = float(np.linalg.norm(ev))
    ev = ev / cd
    sc = float(np.dot(clew - ya, eu))

    def lat_belly(s, t):
        f = t / cd
        s0, s1 = sc * f, L + (sc - L) * f
        u = (s - s0) / max(1e-6, s1 - s0)
        return 0.7 * math.sin(math.pi * min(max(u, 0), 1)) * math.sin(math.pi * min(f * 0.9 + 0.1, 1))

    # a smooth mesh (the mod draws it like the other sails): rows from the yard down to the clew, each spanning from
    # the luff to the leech, so its edges are straight, not stepped
    en = np.cross(eu, ev)
    nu, nv = 16, 14

    def LP(u, f):
        t = f * cd
        s0, s1 = sc * f, L + (sc - L) * f
        sv = s0 + (s1 - s0) * u
        return ya + sv * eu + t * ev + lat_belly(sv, t) * en

    for iv in range(nv):
        for iu in range(nu):
            f0, f1, u0, u1 = iv / nv, (iv + 1) / nv, iu / nu, (iu + 1) / nu
            d.add_quad("sail:mizzen-lateen", "cloth", [LP(u0, f0), LP(u1, f0), LP(u1, f1), LP(u0, f1)],
                       (u0 * 16, f0 * 16, u1 * 16, f1 * 16), smooth="lateen")
    ax = (LATEEN_B - LATEEN_A) / np.linalg.norm(LATEEN_B - LATEEN_A)
    d.sails.append({"code": "mizzen-lateen", "mesh": "sail:mizzen-lateen", "axisPoint": [float(v) for v in ya],
                    "axisDir": [float(v) for v in ax], "area": 45.0})


# ==================================================================== decorating: what the mod needs to furnish her
def decor_data(d):
    """
    (0.3.8, the agreed redesign; the decorating contract) What the mod needs for her people to furnish and paint her,
    into her definition: `hooks` and `startPieces` (fixtures()), `stairs` (each stair's foot on the deck below and head
    on the deck above, the middle of its bottom and top edges, and its width: nothing is put on a stair or a block beyond
    its ends), `keepClear` (boxes nothing may be put in: the windows and lights seen from inside, the gun ports, the guns'
    run back and the gun captain's place, the hearth's and the oven's fronts, the pump's brake and the capstan's bars,
    the hatches, the helm, the doors' and gates' swing, the spawn plate's spot, the ladders' ends) and `sideCuts` (the
    openings through her side above the deck and what closes each: a gun port its lid, an entry port its gate, a scupper
    nothing). Heights as drawn here (Design.translate_y moves them with the rest).
    """
    def box(what, x0, y0, z0, x1, y1, z1):
        lo, hi = np.minimum([x0, y0, z0], [x1, y1, z1]), np.maximum([x0, y0, z0], [x1, y1, z1])
        return {"what": what, "box": [round(float(v), 4) for v in (*lo, *hi)]}

    # the stairs
    stairs = []
    for st in d.stairs:
        if st.get("modular"):
            continue                # ((0.3.16) a placed stair: the piece takes its places, while it's there)
        steps, direction, fy = st["steps"], st["dir"], st["floor"]
        first, last = steps[0], steps[-1]
        bottom_x = first[1] if direction < 0 else first[0]
        top_x = last[0] if direction < 0 else last[1]
        zm = (first[3] + first[4]) / 2
        stairs.append({"code": st["code"], "foot": [round(float(v), 4) for v in (bottom_x, fy, zm)],
                       "head": [round(float(v), 4) for v in (top_x, last[2], zm)],
                       "width": round(float(first[4] - first[3]), 4)})
    keep = []
    if getattr(d, "grating", None):
        # (0.3.10) the grating over the hearth: nothing put on it (the smoke's way out)
        gx0_, gx1_, gz0_, gz1_ = d.grating
        keep.append(box("grating", gx0_, QUARTER - DECK_T, gz0_, gx1_, QUARTER + 1.0, gz1_))
    c = 0.1                                          # a casing round a window
    # windows and lights, from inside: the cabins' fronts, the stern gallery, the castles' side windows
    for room, x0, x1, yb in (("greatcabin", WAIST_AFT - 0.25, WAIST_AFT, MAIN), ("poopcabin", POOP_FRONT - 0.25, POOP_FRONT, QUARTER)):
        h0, h1 = FRONT_WINDOW_Y
        for z0, z1 in () if MODULAR else FRONT_WINDOWS[room]:
            keep.append(box("window", x0 - 0.3, yb + h0 - c, z0 - c, x0, yb + h1 + c, z1 + c))
    for z0, z1, y0, y1 in STERN_WINDOWS:
        xi = max(stern_x(y0), stern_x(y1)) + HULL_T
        keep.append(box("window", xi, y0 - c, z0 - c, xi + 0.35, y1 + c, z1 + c))
    for x0, x1, y0, y1 in SIDE_WINDOWS:
        for sd in (1, -1):
            zi = min(ceil_z(x, y) for x in (x0, x1) for y in (y0, y1)) - FRAME_MOULDED
            keep.append(box("window", x0 - c, y0 - c, sd * (zi + FRAME_MOULDED + 0.02), x1 + c, y1 + c, sd * (zi - 0.3)))
    # the guns: their ports from inside, their run back and the gun captain's place behind each (where it's served)
    for f in d.fixtures:
        if f.get("type") != "gun":
            continue
        x0, y0, x1, y1 = f["port"]
        gy = f["pos"][1]            # ((0.3.21) its deck: a castle gun's too)
        if abs(f["out"][0]) > 0.5:
            # ((0.3.25) a stern chaser: its port (z0 y0 z1 y1) in the transom, whose inside is the larger x of "wall")
            xin = max(f["wall"][:2])
            keep.append(box("gun port", xin - 0.02, gy, x0 - 0.1, xin + 0.3, y1 + 0.15, x1 + 0.1))
            keep.append(box("gun", xin + 0.3, gy, f["box"][2] - 0.2, f["seat"][0] + 0.5, gy + 1.0, f["box"][5] + 0.2))
            continue
        sd = 1 if f["out"][2] > 0 else -1
        wall = min(f["wall"][:2])
        keep.append(box("gun port", x0 - 0.1, gy, sd * (wall + 0.02), x1 + 0.1, y1 + 0.15, sd * (wall - 0.3)))
        seat = f["seat"]
        keep.append(box("gun", f["box"][0] - 0.2, gy, sd * (wall - 0.3), f["box"][3] + 0.2, gy + 1.0,
                        seat[2] - sd * 0.5))
    # the hearth's and the oven's fronts, the room to tend them
    hx0, hx1, hz0 = HEARTH
    if not PIECE_HEARTH:
        keep.append(box("hearth", hx0, MAIN, hz0 - 0.9, hx1, MAIN + 2.0, hz0))
    if OVEN:
        keep.append(box("oven", OVEN[0], MAIN, OVEN[2] - 0.9, OVEN[1], MAIN + 2.0, OVEN[2]))
    # the pump's brake rocking and the two working it; the capstan's bars and the crew walking them
    px, pz = PUMP
    bx0, bx1 = sorted((px - 0.35 * PUMP_BRAKE, px + 1.6 * PUMP_BRAKE))
    keep.append(box("pump", bx0, MAIN, pz - 0.6, bx1, MAIN + 2.0, pz + 0.6))
    # ((0.3.13, note 130) its bars shorter: their crew's ring)
    kc_ = CAPSTAN_BARS + 0.4 if GRID else 1.6
    keep.append(box("capstan", CAPSTAN_X - kc_, MAIN, CAPSTAN_Z - kc_, CAPSTAN_X + kc_, MAIN + 2.0, CAPSTAN_Z + kc_))
    # the hatches (the waist hatch's leaves swing up and over outboard; the hold hatch's ladder well)
    hx0, hx1, hz0, hz1 = HATCH
    if MODULAR:
        # ((0.3.20, step B) the hatchways: their hatch pieces' leaves (hinged on the long sides, swung up 100°) and the
        # ladders' well; the cells themselves take nothing while empty)
        hx0, hx1, hz0, hz1 = OPENING[0] + 0.3, OPENING[1] - 0.3, OPENING[2] + 0.3, OPENING[3] - 0.3
    if MODULAR:
        # ((0.3.24, note 187) 2 x 3 between the beams: its leaves hinged on its ends (x 6, 8), standing up over them open)
        keep.append(box("hatch", OPENING[0] - 0.3, MAIN, OPENING[2], OPENING[1] + 0.3, MAIN + 1.0, OPENING[3]))
    else:
        keep.append(box("hatch", hx0 - 0.3, MAIN, hz0 - 1.1, hx1 + 0.3, MAIN + 1.0, hz1 + 1.1))
    # ((0.3.12) its ladder now upright at its forward end, going on up: no landing aft of it on the lower deck)
    keep.append(box("hatch", hx0 - (0.3 if GRID else 1.0), LOWER, hz0 - 0.3, hx1 + 0.3, MAIN - DECK_T, hz1 + 0.3))
    # the helm, the helmsman's place behind it, the binnacle before it
    keep.append(box("helm", WHEEL_X - 1.2, QUARTER, -0.7, (BINNACLE_X or WHEEL_X) + 0.35, QUARTER + 2.0, 0.7))
    # the doors (a vanilla door swings 90° about its hinge into the room it opens into) and the entry ports' gates
    for f in d.fixtures:
        if f.get("type") == "door":
            bx = f["box"]
            sgn = 1 if f.get("openAngle", 90.0) > 0 else -1
            if f.get("normal", "x") == "z":
                # ((0.3.12) a door in a wall along x, the stairwell's: the same angle turns a leaf hung on its after
                # edge toward -z, on its forward edge toward +z)
                if f.get("hinge") and f["hinge"][0] < (bx[0] + bx[3]) / 2:
                    sgn = -sgn
                ze = bx[5] if sgn > 0 else bx[2]
                keep.append(box("door", bx[0] - 0.05, bx[1], ze, bx[3] + 0.05, bx[4], ze + sgn * 1.1))
                zo = bx[2] if sgn > 0 else bx[5]
                keep.append(box("doorway", bx[0] - 0.05, bx[1], zo, bx[3] + 0.05, bx[4], zo - sgn * 0.8))
                continue
            if f.get("hinge") and f["hinge"][1] > (bx[2] + bx[5]) / 2:
                sgn = -sgn                      # (hung on its other edge: the same angle swings it the other way)
            xe = bx[3] if sgn > 0 else bx[0]
            keep.append(box("door", xe, bx[1], bx[2] - 0.05, xe + sgn * 1.1, bx[4], bx[5] + 0.05))
            xo = bx[0] if sgn > 0 else bx[3]
            keep.append(box("doorway", xo, bx[1], bx[2] - 0.05, xo - sgn * 0.8, bx[4], bx[5] + 0.05))
    for sd in (1, -1):
        hx, hz = gate_hinge(sd)
        keep.append(box("gate", PORT_X[0] - 0.1, MAIN, hz + sd * 0.05, PORT_X[1] + 0.1, MAIN + WAIST_RAIL + 0.1,
                        hz - sd * (PORT_X[1] - PORT_X[0] + 0.2)))
    # where the spawn plate brings you back, the ladders' ends on the decks
    px_, py_, pz_ = PLATE
    sp_y = MAIN if MODULAR else QUARTER
    keep.append(box("spawn", px_ - 0.5, sp_y, pz_ - 0.5, px_ + 0.5, sp_y + 2.0, pz_ + 0.5))
    # the crew plaque on the great cabin's front, and room to stand and read it
    qx_, qy_, qz_ = PLAQUE
    qd_ = 0.15
    if MODULAR:
        # ((0.3.16) on the mainmast's aft face: its box aft of it)
        qx_, qd_ = qx_ - mast_r("main", qy_) - 0.02, -0.15
    keep.append(box("plaque", qx_, qy_ - PLAQUE_SIZE[0] / 2 - 0.05, qz_ - PLAQUE_SIZE[1] / 2 - 0.05, qx_ + qd_,
                    qy_ + PLAQUE_SIZE[0] / 2 + 0.05, qz_ + PLAQUE_SIZE[1] / 2 + 0.05))
    for lad in d.ladders:
        for k in ("exitTop", "exitBottom"):
            e = lad.get(k)
            if e is not None and not lad.get("outside") or (e is not None and k == "exitTop"):
                keep.append(box("ladder", e[0] - 0.45, e[1], e[2] - 0.45, e[0] + 0.45, e[1] + 1.9, e[2] + 0.45))
    # the openings through her side over the deck, and what closes each
    cuts = []
    for f in d.fixtures:
        if f.get("type") == "gun":
            x0, y0, x1, y1 = f["port"]
            cut = {"kind": "gunport", "x0": x0, "x1": x1, "y0": y0, "y1": y1, "side": 1 if f["out"][2] > 0 else -1}
            if f.get("lid"):
                cut["lid"] = f["code"]
            cuts.append(cut)
        elif f.get("type") == "gate":
            bx = f["box"]
            cuts.append({"kind": "entryport", "x0": bx[0], "x1": bx[3], "y0": bx[1], "y1": bx[4],
                         "side": 1 if bx[2] + bx[5] > 0 else -1, "gate": f["code"]})
    for a, b in SCUPPERS:
        for sd in (1, -1):
            cuts.append({"kind": "scupper", "x0": round((a + b) / 2 - SCUPPER_R, 4), "x1": round((a + b) / 2 + SCUPPER_R, 4),
                         "y0": round(MAIN + 0.1 - SCUPPER_R, 4), "y1": round(MAIN + 0.1 + SCUPPER_R, 4), "side": sd,
                         "r": SCUPPER_R})
    d.props["hooks"] = d.hooks
    d.props["startPieces"] = d.start_pieces
    d.props["stairs"] = stairs
    d.props["keepClear"] = keep
    d.props["sideCuts"] = cuts
    if MODULAR:
        # (0.3.16, docs/modular-pieces.md) the pieces a new ship is laid with, and the openings in her decks
        d.props["modularPieces"] = d.modular
        d.props["wells"] = d.wells
    if LAYOUT_REV:
        # ((0.3.29) her layout's revision, and what each revision changed of her deck slots' starts: a ship laid before it
        # keeps the old start there (the mod: EntityShip's layout migration, docs/modular-pieces.md §14))
        d.props["layoutRev"] = LAYOUT_REV
        was = {}
        if COMPANION_WAIST:
            # (the waist companion's well and its beam: in before; the old stairwell's well and the beams across it: out)
            for sid in ("main:-3:-1", "main:-2:-1", "main:-1:-1", "beam:main:-2:-1"):
                was[sid] = "filled"
            for sid in ("main:-7:2", "main:-6:2", "main:-5:2", "beam:main:-6:2", "beam:main:-4:2"):
                was[sid] = "empty"
        d.props["layoutBefore"] = [{"rev": 1, "cells": was, "gunPorts": bool(GUN_PORT_SLOTS)}]


# ==================================================================== fixtures: what the mod makes usable
def fixtures(d, DOORS, captain_bed):
    def fx(ftype, code, pos, yaw=0.0, sel=None, **kw):
        f = {"type": ftype, "code": code, "pos": [round(float(v), 4) for v in pos], "yaw": yaw}
        if sel is not None:
            f["sel"] = [round(float(v), 4) for v in sel]
        for k, v in kw.items():
            if isinstance(v, (list, tuple, np.ndarray)) and all(isinstance(x, (int, float, np.floating, np.integer)) for x in v):
                v = [round(float(x), 4) for x in v]
            f[k] = v
        d.fixtures.append(f)

    wx = WHEEL_X
    fx("helm", "helm", (wx - 0.75, QUARTER, 0.0), 0.0, sel=(wx - 0.2, QUARTER + 0.25, -0.62, wx + 0.75, QUARTER + 1.5, 0.62))
    if BINNACLE_X is not None:
        # ((0.3.12) up over the compass on its lid)
        fx("compass", "binnacle", (BINNACLE_X, QUARTER, 0.0), sel=(BINNACLE_X - 0.26, QUARTER, -0.3, BINNACLE_X + 0.26,
                                                                   QUARTER + (1.2 if GRID else 1.1), 0.3))
    fx("anchor", "capstan", (CAPSTAN_X, MAIN, CAPSTAN_Z), sel=(CAPSTAN_X - 0.6, MAIN, CAPSTAN_Z - 0.6, CAPSTAN_X + 0.6,
                                                               MAIN + 1.1, CAPSTAN_Z + 0.6))
    # the bilge pump: held right-click works its brake (a stroke a second, up to two at it)
    pmx, pmz = PUMP
    # (0.3.8) its spout's mouth, and which way the water leaves it (a unit vector), for the mod to show it coming out
    sp, sdir = d.pump_spout
    sx0, sx1 = sorted((pmx - 0.35 * PUMP_BRAKE, pmx + 1.15 * PUMP_BRAKE))
    fx("pump", "pump", (pmx, MAIN, pmz), sel=(sx0, MAIN, pmz - 0.3, sx1, MAIN + 1.5, pmz + 0.3),
       spout=sp, spoutDir=[round(float(v), 4) for v in sdir])
    # a vanilla metal plate on the wall nearest the wheel: the poop cabin's front bulkhead, port of its door (the poop's
    # stair is on the starboard side), at eye height, facing forward to the helm. Right-clicked with an empty hand it
    # binds your spawn to her; you come back standing on the quarterdeck in front of it.
    px_, py_, pz_ = PLATE
    if MODULAR:
        # ((0.3.16) carrack-m: the poop cabin's front is placed pieces: on the mainmast's port face, facing port (yaw 90:
        # the plate's face toward -z), its back on the mast; you come back on the main deck beside it)
        mr_ = mast_r("main", py_)
        fx("attach", "plate", (px_, py_, -mr_), 90.0,
           sel=(px_ - 0.35, py_ - 0.35, -mr_ - 0.25, px_ + 0.35, py_ + 0.35, -mr_ + 0.02), kind="plate",
           spawn=[px_, MAIN, pz_])
    else:
        fx("attach", "plate", (POOP_FRONT, py_, pz_), 0.0,
           sel=(POOP_FRONT - 0.02, py_ - 0.35, pz_ - 0.35, POOP_FRONT + 0.25, py_ + 0.35, pz_ + 0.35), kind="plate",
           spawn=[px_, QUARTER, pz_])
    # (0.3.11) the bronze plaque by the great cabin's door: her crew list (right-click to read it; her captains write names on)
    qx_, qy_, qz_ = PLAQUE
    qh, qw = PLAQUE_SIZE
    sg = 1.0
    if MODULAR:
        # ((0.3.16, note 153) on the mainmast's aft face, facing aft at eye height on the main deck: its back on a block
        # standing off the round mast (the mast's face 0.02 behind its middle at the plaque's edges), the plaque mirrored)
        sg = -1.0
        qx_ = qx_ - mast_r("main", qy_) - 0.02
        d.add_box_mesh("auto", "timber", (qx_ + 0.04, qy_, qz_), (0.1, qh - 0.1, qw - 0.12))
    fx("plaque", "plaque", (qx_, qy_, qz_), 180.0 if MODULAR else 0.0,
       sel=(min(qx_ - 0.02 * sg, qx_ + 0.12 * sg), qy_ - qh / 2 - 0.03, qz_ - qw / 2 - 0.03,
            max(qx_ - 0.02 * sg, qx_ + 0.12 * sg), qy_ + qh / 2 + 0.03, qz_ + qw / 2 + 0.03))
    d.add_box_mesh("auto", "bronze", (qx_ + 0.015 * sg, qy_, qz_), (0.03, qh, qw))
    # a raised rim round it, and four rivets
    # (the side pieces between the top and bottom ones, so no faces overlap)
    for dy, dz, sy, sz in ((qh / 2 - 0.02, 0, 0.04, qw), (-qh / 2 + 0.02, 0, 0.04, qw),
                           (0, qw / 2 - 0.02, qh - 0.08, 0.04), (0, -qw / 2 + 0.02, qh - 0.08, 0.04)):
        d.add_box_mesh("auto", "bronze", (qx_ + 0.036 * sg, qy_ + dy, qz_ + dz), (0.014, sy, sz))
    for dy in (-1, 1):
        for dz in (-1, 1):
            d.add_box_mesh("auto", "iron", (qx_ + 0.043 * sg, qy_ + dy * (qh / 2 - 0.06), qz_ + dz * (qw / 2 - 0.06)), (0.02, 0.03, 0.03))

    hx0, hx1, hz0, hz1 = HATCH
    hxc = (hx0 + hx1) / 2
    slots, _, _ = hold_slots()
    # the hold was one big storage before it had places for chests: what's still in one on an older ship can be taken
    # out through the hold hatch (the mod opens it there only while it isn't empty)
    if MODULAR:
        # ((0.3.20, step B) the hatches are pieces over the openings (modularPieces "hatch"): no hatch fixtures; the old
        # hold storage's way out through the hold's opening)
        fx("storage", "hold", ((OPENING[0] + OPENING[1]) / 2, LOWER, 0.0), slots=slots)
    for _ in () if MODULAR else (0,):
     fx("storage", "hold", (hxc, LOWER, 0.0), slots=slots)
     fx("hatch", "hatch-hold", (hxc, LOWER + 0.25, 0.0), sel=(hx0 - 0.25, LOWER, hz0 - 0.25, hx1 + 0.25, LOWER + 0.4,
                                                              hz1 + 0.25),
       box=[hx0, LOWER, hz0, hx1, LOWER + 0.25, hz1], storage="hold")
    # the glazed waist hatch: shut, the lower deck is a closed room; open, it's the cargo hatch
     fx("hatch", "hatch-waist", (hxc, MAIN + 0.5, 0.0), sel=(hx0 - 0.25, MAIN, hz0 - 0.25, hx1 + 0.25, MAIN + 0.65,
                                                             hz1 + 0.25),
       box=[hx0, MAIN + 0.25, hz0, hx1, MAIN + 0.5, hz1], unseals="lowerdeck")

    # vanilla doors, the entry port gates (our own hinged bulwark sections) and glass panes
    for door in DOORS:
        code, x0, x1, yb, room_side, block = door[:6]
        dz = door[6] if len(door) > 6 else 0.0
        xc = (x0 + x1) / 2
        # (the companion house's door is a way below: open, the lower deck isn't a closed room, and the sea on deck
        # gets down through it like through an open hatch)
        # (0.3.8) and the great cabin's, its stair down to the lower deck inside it; (0.3.9) and the door at the stair's
        # head beside it
        # (0.3.12, the user's note 112: the stairwell walled off from the great cabin, its own door into it: the great
        # cabin's door no longer opens the lower deck)
        below = {"unseals": "lowerdeck"} if code in ("door-companion", "door-companion-cabin") or (
            CABIN_STAIR and code == "door-greatcabin" and not STAIRWELL) else {}
        # (the door at the companion's head hung on its inboard edge: open, it lies along the deck beside the stair's
        # opening, clear of the stair and its handrail)
        hz = dz - COMP_S * 0.5 if code == "door-companion-cabin" else dz - 0.5
        ang = 90.0 * room_side * (COMP_S if code == "door-companion-cabin" else 1)
        hx = xc
        if code == "door-companion-cabin" and STAIRWELL:
            # (0.3.12, the user's note 107: "doors should open to other side. Now in conflict with railings and when open
            # in front of stair entrance from cabin") it opens out into the waist, hung on its inboard edge at the
            # wall's waist face: open, it lies along the quarterdeck stair's outboard side (its middle 0.125 out from
            # the hinge line, 0.08 clear of the stair's stringer), the way to it outboard of the leaf
            hx, ang = x1, -ang
        fx("door", code, (xc, yb, dz), sel=(x0 - 0.05, yb, dz - 0.55, x1 + 0.05, yb + 2.0, dz + 0.55), block=block,
           box=[x0, yb, dz - 0.5, x1, yb + 2.0, dz + 0.5], normal="x", hinge=[hx, hz], openAngle=ang, **below)
    if d.stair_door:
        # (0.3.12, the user's note 112) the stairwell's door from the great cabin (a vanilla door like the others, in
        # a wall along x: normal z): no `unseals` (the great cabin stays a closed room whatever the stairwell's open to)
        sd_ = d.stair_door
        b_ = sd_["box"]
        fx("door", sd_["code"], ((b_[0] + b_[3]) / 2, b_[1], (b_[2] + b_[5]) / 2),
           sel=(b_[0] - 0.05, b_[1], b_[2] - 0.05, b_[3] + 0.05, b_[4], b_[5] + 0.05), block="game:door-sleek-windowed-oak",
           box=b_, normal="z", hinge=sd_["hinge"], openAngle=sd_["openAngle"])
    px = (PORT_X[0] + PORT_X[1]) / 2
    for s, side in ((1, "starboard"), (-1, "port")):
        zo = hw(px, MAIN + 0.5)
        z0, z1 = sorted((s * (zo - HULL_T), s * zo))
        fx("gate", f"gate-{side}", (px, MAIN, s * (zo - HULL_T / 2)), sel=(PORT_X[0], MAIN, z0 - 0.1, PORT_X[1],
                                                                           MAIN + WAIST_RAIL, z1 + 0.1),
           box=[PORT_X[0], MAIN, z0, PORT_X[1], MAIN + WAIST_RAIL, z1], normal="z")

    # attachment points for vanilla lanterns, chests and beds, placed by the crew. (0.3.8, the agreed redesign: players
    # furnish her themselves, at mount points the mod finds; the built-in beds, chests, the captain's bed and table,
    # stools and the hold's cargo places are gone, VARIANT 3. Her lanterns' places are hooks, each with a lantern on it
    # to start with: `hooks` and `startPieces`, the decorating contract's.)
    FURNISHED = VARIANT != 3
    d.hooks, d.start_pieces = [], []

    def inner_z(x0, x1, y0, y1):
        """How far out from the centreline there's room inboard of the frames over this stretch."""
        return min(frame_face(x, y) for x in np.linspace(x0, x1, 5) for y in np.linspace(y0, y1, 4))

    def lantern(code, x, y, z, hang="down", material=None):
        if not FURNISHED:
            # a hook (the old lantern place's code, so an older ship's lantern goes back on it) and a lantern on it
            d.hooks.append({"code": code, "pos": [round(float(v), 4) for v in (x, y, z)], "hang": hang, "kinds": ["light"]})
            piece = {"block": "game:lantern-small-up", "mount": "h:" + code}
            # (0.3.9, the user's notes 57 and 59: every one a small bronze lantern, vanilla's attributes in full, or the
            # game makes it "lantern-small-up-" with no texture)
            material = LANTERN_METAL if GRID else material
            if material:
                piece["attributes"] = {"material": material, "lining": "plain", "glass": "quartz"}
            d.start_pieces.append(piece)
            return
        if hang == "down":
            sel = (x - 0.25, y - 0.75, z - 0.25, x + 0.25, y, z + 0.25)
        else:
            sel = (x - 0.25, y, z - 0.25, x + 0.25, y + 0.7, z + 0.25)
        fx("attach", code, (x, y, z), sel=sel, kind="lantern", hang=hang)

    def chest(code, x, y, s, facing_yaw=None):
        zc = s * (inner_z(x - 0.5, x + 0.5, y, y + 1.0) - 0.5)
        # yaw turns toward port: 90 faces port (-z), 270 faces starboard (+z); chests face inboard
        yaw = facing_yaw if facing_yaw is not None else (90.0 if s > 0 else 270.0)
        fx("attach", code, (x, y, zc), yaw, sel=(x - 0.45, y, zc - 0.45, x + 0.45, y + 0.9, zc + 0.45), kind="chest")

    def bed(code, x, y, s, head_yaw=180.0):
        zc = s * (inner_z(x - 1.0, x + 1.0, y, y + 0.7) - 0.52)
        fx("attach", code, (x, y, zc), head_yaw, sel=(x - 1.0, y, zc - 0.5, x + 1.0, y + 0.6, zc + 0.5), kind="bed",
           efficiency=0.7)

    # lanterns: hooks under the beams in every room, and the stern lantern
    if GRID:
        # (0.3.9, the user's note 57: "All lanterns inside should be on ship's main axis") on the centreline, each under
        # the deck above in the middle of a bay between two beams (an odd x), so the lantern hangs in the band, over
        # the room's 2 blocks; an iron eye in the deck's underside for each
        def ceiling_hook(code, x, deck_top):
            y = deck_top - DECK_T
            if MODULAR:
                # ((0.3.27, note 221: "cannot remove this deck part probably due to the light hook being attached - now
                # lanterns make on hooks so we can remove those fixed in place") carrack-m: no hooks of her own over her
                # rooms (their iron eyes fixed the deck cell over them): her lanterns hang on the mod's ceiling hooks
                # (h:c:<floor's deck>:<i>:<k>, under the cell's middle, its eye drawn with the lantern), in the cell a
                # half block aft of where they hung (beam-free: the beams' collision is the voxel aft of their line)
                floor = {MAIN: "lower", LOWER: "hold", QUARTER: "main", POOP: "quarter"}[deck_top]
                x0 = int(math.floor(x)) - 1
                d.start_pieces.append({"block": "game:lantern-small-up", "mount": f"h:c:{floor}:{x0}:-1",
                                       "attributes": {"material": LANTERN_METAL, "lining": "plain", "glass": "quartz"}})
                return
            d.add_box_mesh("auto", "iron", (x, y - 0.012, 0.0), (0.16, 0.024, 0.16))
            rope_path(d, ring_points((x, y - 0.06, 0.0), 0.035, "x", 8), r=0.01, group="auto", mat="iron")
            lantern(code, x, y, 0.0)
        # ((0.3.20, step B) carrack-m: the fourth a bay forward, x 11, off the waist hatchway's edge cell at 9)
        for i, x in enumerate((-11.0, -5.0, 1.0, 11.0 if MODULAR else 9.0, 15.0)):
            ceiling_hook(f"lantern-lowerdeck-{i + 1}", x, MAIN)
        # ((0.3.27) carrack-m: the second a bay forward (its hook's cell at 2..3 is the mainmast's)
        # ((Hansa) back in cell 2..3: the mainmast is in 3..4 now and the pump's pipe comes down through 4..5)
        for i, x in enumerate((-5.0, 5.0 if MODULAR and not HANSA else 3.0, 11.0)):   # in the hold, between its beams' full depth
            ceiling_hook(f"lantern-hold-{i + 1}", x, LOWER)
        ceiling_hook("lantern-greatcabin", -9.0, QUARTER)
        ceiling_hook("lantern-poopcabin", -11.0, POOP)
    else:
        beam_under = MAIN - DECK_T - 0.25
        for i, (x, z) in enumerate(((-11.375, 1.2), (-5.375, -1.2), (0.625, 1.2), (6.625, -1.2), (12.625, 0.0))):
            lantern(f"lantern-lowerdeck-{i + 1}", x, beam_under, z)
        for i, (x, z) in enumerate(((-6.5, 1.4), (-2.5, -1.4), (6.5, -1.4))):     # in the hold, under the lower deck
            lantern(f"lantern-hold-{i + 1}", x, LOWER - DECK_T, z)
        lantern("lantern-greatcabin", -8.375, QUARTER - DECK_T - 0.25, 1.0, material="brass")
        lantern("lantern-poopcabin", poop_beams()[-1] + 0.125, POOP - DECK_T - 0.25, 0.0, material="brass")
    # a hook on every mast at the main deck: the mainmast in the waist, the foremast in the forecastle, the mizzen in
    # the great cabin
    # (GRID: up in the band, its arm reaching into the bay aft of the mast, so the lantern hangs over the room's 2 blocks)
    for name, face in (("main", -1), ("fore", -1), ("mizzen", -1 if GRID else 1)):
        mx, (y0, y1, r0, r1), _, _ = MASTS[name]
        hy = MAIN + (2.62 if GRID else 2.35)
        if MODULAR and name == "main":
            # (0.3.24, note 193: "Lantern mount here should be higher as it interferes with ship's plaque") carrack-m: the
            # mainmast's 0.38 higher (the waist is open over it), its lantern clear over the plaque on the mast's aft face
            # (the mod's own mast hooks skip this side: one of the design's is within 0.6)
            hy = MAIN + 3.0
        r = r0 + (r1 - r0) * (hy - y0) / (y1 - y0)
        d.add_cylinder("auto", "iron", (mx, hy - 0.04, 0), (mx, hy + 0.04, 0), r + 0.025, r + 0.025, sides=16,
                       smooth=False)
        arm = np.array([mx + face * (r + 0.34), hy, 0.0])
        d.add_beam_mesh("auto", "iron", (mx + face * r, hy, 0.0), arm, 0.04, 0.05)
        d.add_cylinder("auto", "iron", arm, arm - np.array([0, 0.06, 0]), 0.012, 0.012, sides=4)
        lantern(f"lantern-{name}mast", float(arm[0]), hy - 0.06, 0.0)
    # The stern lantern's bracket (0.3.7, the user's note 12: "not sure what this post is supposed to do"; it was a
    # thin raked pole with a board on it): a stout post on the taffrail's middle, and from its head an iron crane
    # reaching out aft over the water, braced from below, with a plate at its end the lantern stands on, a ring round
    # its foot. Its place is shown even empty ("Stern lantern"), so it says what it's for.
    x0, x1, t0, t1, _ = d.taffrail
    lx = (x0 + x1) / 2 + 0.05
    top = t1 + 0.6
    d.add_box_mesh("exterior", "timber", (lx, (t1 + top) / 2, 0.0), (0.16, top - t1, 0.16))
    d.add_box_mesh("exterior", "timber", (lx, top + 0.03, 0.0), (0.2, 0.06, 0.2))                 # its cap
    for dz in (-0.1, 0.1):                                                                          # cheeks on the taffrail
        d.add_box_mesh("exterior", "timber", (lx, t1 + 0.12, dz), (0.12, 0.24, 0.04))
    end = np.array([x0 - 0.38, top - 0.06, 0.0])
    d.add_beam_mesh("exterior", "iron", (lx - 0.08, top - 0.06, 0.0), end, 0.05, 0.05)              # the crane's arm
    d.add_beam_mesh("exterior", "iron", (lx - 0.08, top - 0.4, 0.0), end + np.array([0.2, -0.02, 0.0]), 0.04, 0.04)
    d.add_box_mesh("exterior", "iron", (lx - 0.09, top - 0.4, 0.0), (0.02, 0.08, 0.1))             # its strap on the post
    plate = end + np.array([0.0, 0.02, 0.0])
    d.add_cylinder("exterior", "iron", plate - np.array([0, 0.015, 0]), plate + np.array([0, 0.015, 0]), 0.19, 0.19,
                   sides=12, smooth=False)
    rope_path(d, ring_points(plate + np.array([0, 0.07, 0]), 0.19, "y", 12), r=0.012, mat="iron")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        q = plate + np.array([0.19 * math.cos(a), 0.0, 0.19 * math.sin(a)])
        d.add_cylinder("exterior", "iron", q, q + np.array([0, 0.07, 0]), 0.012, 0.012, sides=4)
    lantern("lantern-stern", float(plate[0]), float(plate[1] + 0.015), 0.0, hang="up", material="brass")
    if FURNISHED:
        d.fixtures[-1]["shown"] = True
    else:
        d.hooks[-1]["shown"] = True

    if FURNISHED:
        # beds: along the sides of the lower deck, two in the poop cabin, one in the forecastle (the captain has his own)
        n = 0
        for x in (-11.5, -8.75, -6.5, -4.25, 5.25, 7.5, 9.75):
            n += 1
            bed(f"bed-lowerdeck-{n}", x, LOWER, 1)
        for x in (-11.5, 0.75, 3.0, 5.25, 7.5, 9.75):     # port: the companionway comes down aft of the mainmast
            n += 1
            bed(f"bed-lowerdeck-{n}", x, LOWER, -1)
        bed("bed-poopcabin-1", -13.25, QUARTER, 1)
        bed("bed-poopcabin-2", -13.25, QUARTER, -1)
        bed("bed-forecastle", 11.5 + STRETCH, MAIN, -1, head_yaw=0.0)      # port: the hearth is on the starboard side

        # chests: a few on the lower deck, two in the great cabin, one in the poop cabin, two in the forecastle
        for i, (x, s) in enumerate(((-1.25, 1), (2.25, 1), (-1.25, -1), (11.75, 1), (11.75, -1))):
            chest(f"chest-lowerdeck-{i + 1}", x, LOWER, s)
        chest("chest-greatcabin-1", -10.5, MAIN, 1)
        chest("chest-greatcabin-2", -8.25, MAIN, 1)
        chest("chest-poopcabin", -11.25, QUARTER, -1)
        if PROPOSAL:
            # (0.3.6: the hearth has the starboard after corner, the cable runs aft along the port side: forward of the
            # hearth, and in the port after corner)
            chest("chest-forecastle-1", 11.25 + STRETCH, MAIN, 1)
            chest("chest-forecastle-2", 9.25 + STRETCH, MAIN, -1)
        else:
            chest("chest-forecastle-1", 10.0 + STRETCH, MAIN, 1)
            chest("chest-forecastle-2", 13.0 + STRETCH, MAIN, -1)

        # the hold's cargo: places on the cargo deck either side of the keelson for a chest, a storage vessel or a crate,
        # in pairs that also take a trunk (a double chest) across both; the keelson between them is the gangway
        cz = 0.85
        spots = []
        for xc in np.arange(-10.0, 10.0 + STRETCH, 1.0):
            clear_masts = all(abs(xc - MASTS[m][0]) > 0.95 for m in MASTS)
            clear_ladder = not (HATCH[0] - 0.4 < xc < HATCH[0] + 2.1)
            clear_pump = abs(xc - PUMP[0]) > 0.95          # (its pipe on the keelson: room to walk round it)
            room = all(ceil_z(x, y) > cz + 0.72 for x in (xc - 0.5, xc + 0.5) for y in (HOLD_FLOOR + 0.05, HOLD_FLOOR + 0.9))
            if clear_masts and clear_ladder and clear_pump and room:
                spots.append(float(xc))
        runs, run = [], []
        for xc in spots:
            if run and abs(xc - run[-1] - 1.0) > 1e-6:
                runs.append(run)
                run = []
            run.append(xc)
        if run:
            runs.append(run)
        for s, side in ((1, "s"), (-1, "p")):
            for run in runs:
                for k, xc in enumerate(run):
                    partner = None
                    if k % 2 == 0 and k + 1 < len(run):
                        partner = run[k + 1]
                    elif k % 2 == 1:
                        partner = run[k - 1]
                    code = f"cargo-{side}{xc:+.0f}"
                    extra = {"pair": f"cargo-{side}{partner:+.0f}"} if partner is not None else {}
                    z = s * cz
                    fx("attach", code, (xc, HOLD_FLOOR, z), 90.0 if s > 0 else 270.0,
                       sel=(xc - 0.45, HOLD_FLOOR, z - 0.45, xc + 0.45, HOLD_FLOOR + 0.9, z + 0.45), kind="cargo", **extra)

    # the ship's boats: on the gallows over the waist, two side by side (Hansa's rowing boats, or vanilla rafts), taken off
    # and put back by hand
    bxc = (GALLOWS_X[0] + GALLOWS_X[1]) / 2 + 0.125
    for i, bz in enumerate(BOATS_Z):
        box = [bxc - BOAT_LEN / 2, GALLOWS_Y + 0.02, bz - BOAT_W / 2, bxc + BOAT_LEN / 2, GALLOWS_Y + BOAT_H, bz + BOAT_W / 2]
        fx("attach", f"boat-{i + 1}", (bxc, GALLOWS_Y + 0.02, bz), 0.0, sel=box, kind="boat", box=box)

    # the figurehead (the mod draws the one chosen): she's out of reach under the forecastle, so it's changed at the
    # stem head right above her, at the forecastle's tip (right-click: the next one)
    tip_top = rail(FC_TIP - 0.1) + 0.2
    if d.figureheads:
        fx("figurehead", "figurehead", (FC_TIP - 0.3, QUARTER, 0.0), sel=(FC_TIP - 0.25, QUARTER + 0.3, -0.2, FC_TIP + 0.15, tip_top, 0.2),
           options=[f["code"] for f in d.figureheads], groups=[f["group"] or "" for f in d.figureheads])
    if GRID:
        # (0.3.9, the user's note 65) the heads are seats: the mod sits a player on each like on a chair, facing aft (its
        # back is on its fore side). pos: the middle of the seat's top, where the sitter is put; feet: the deck in front
        # of it, where they stand up to; box: the seat box (solid); sel: it and its back. (0.3.10, the user's note 91:
        # pos at the seat's front edge, HEADS_SIT, the box under it set back: the legs hang clear in front of it; the
        # feet a little further out)
        for s, side in ((-1, "port"), (1, "starboard")):
            z = s * HEADS_Z
            # ((0.3.13, note 139) the bigger seat: sel round its board and back, box its board's plan; the feet 0.4
            # in front of its front edge)
            fe_ = HEADS_X + 0.245 - HEADS_SEAT[0]
            hw_ = HEADS_SEAT[1] / 2
            fx("seat", f"heads-{side}", (HEADS_SIT, QUARTER + 0.5, z), 180.0,
               sel=(fe_ - 0.03, QUARTER, z - hw_ - 0.03, HEADS_X + 0.32, QUARTER + 1.1, z + hw_ + 0.03),
               box=[fe_, QUARTER, z - hw_, HEADS_X + 0.25, QUARTER + 0.5, z + hw_],
               feet=[round(fe_ - 0.4, 4), QUARTER, z])
    # her name, painted on the board across the transom (right-click it to name her)
    nb = d.nameboard
    c, right, up = nb["pos"], nb["right"], nb["up"]
    corners = [c + right * sx * nb["width"] / 2 + up * sy * nb["height"] / 2 for sx in (-1, 1) for sy in (-1, 1)]
    lo, hi = np.min(corners, axis=0) - 0.1, np.max(corners, axis=0) + 0.1
    fx("nameboard", "nameboard", c, sel=(*lo, *hi), right=right, up=up, out=nb["normal"], width=nb["width"],
       height=nb["height"])

    if FURNISHED:
        # the captain's cabin: his bed (a vanilla bed you can sleep in), a chart table and two stools (vanilla, for show)
        cbx, cbz = captain_bed
        fx("bed", "bed-captain", (cbx, MAIN, cbz), 180.0, sel=(cbx - 1.0, MAIN, cbz - 0.5, cbx + 1.0, MAIN + 0.6, cbz + 0.5),
           kind="bed", block="game:bed-woodaged", efficiency=0.8)
        fx("prop", "chart-table", (-10.25, MAIN, -1.0), 0.0, block="game:table-normal",
           box=[-10.75, MAIN, -1.5, -9.75, MAIN + 1.0, -0.5])
        fx("prop", "stool-1", (-10.25, MAIN, -2.05), 0.0, block="game:chair-plain", box=[-10.7, MAIN, -2.5, -9.8, MAIN + 0.65, -1.6])
        fx("prop", "stool-2", (-11.35, MAIN, -0.9), 20.0, block="game:chair-plain", box=[-11.8, MAIN, -1.35, -10.9, MAIN + 0.65, -0.45])
    # the ship's firehearth in the forecastle (0.3.6): the mod draws a vanilla firepit (cold for now) standing in its
    # sunken firebed. For the working fire to come (fuel, cooking, flames, warmth), what it needs: pos, where the firepit
    # block's bottom centre is (on the firebed); sel, the firebed and the fire over it (what's clicked); box, the
    # hearth's bricks; fire, the fire's middle (light, flames, heat); cook, where a pot's bottom sits on the fire (as
    # vanilla puts one in a firepit: 0.125 over the block's bottom); chimney, the stovepipe's mouth under its rain cap on
    # the forecastle deck (smoke); room, the room it warms; yaw 90: its open front faces port
    h = hearth()
    (bx0, bx1, bz0, bz1), (hx, hz) = h["bed"], h["centre"]
    bed_y = MAIN + HEARTH_BED
    if not PIECE_HEARTH:        # ((0.3.18, note 165) carrack-m: the `hearth` piece instead)
      fx("hearth", "hearth", (hx, bed_y, hz), 90.0, sel=(bx0, bed_y, bz0, bx1, MAIN + HEARTH_SIDES, bz1),
       block="game:firepit-cold", box=[h["x0"], MAIN, h["z0"], h["x1"], MAIN + HEARTH_BACK, max(h["back_out"])],
       fire=[hx, bed_y + 0.2, hz], cook=[hx, bed_y + 0.125, hz], chimney=[h["pipe"][0], QUARTER + (0.05 if GRID else 1.5), h["pipe"][1]],
       room="forecastle")

    if OVEN:
        # (0.3.8) the clay oven forward of it: pos, its block's bottom centre on its base; block, vanilla's oven (the
        # variant with its mouth to port, as it stands: not turned again); yaw 90, its mouth to port as the hearth's front;
        # sel, the oven; box, it and its base out to the side; fire, where its fuel burns (on its floor); cook, where
        # what's baked sits (its floor's top); chimney, the stovepipe's mouth it shares with the hearth; room
        op = d.oven_pos
        ox0, ox1, oz0 = OVEN
        fx("oven", "oven", op, 90.0, sel=(ox0, op[1], oz0, ox1, op[1] + 0.97, oz0 + 1.0), block=OVEN_BLOCK,
           box=[ox0, MAIN, oz0, ox1, op[1] + 0.97, round(float(frame_face(ox0, op[1])), 4)],
           fire=[op[0], op[1] + 0.15, op[2]], cook=[op[0], op[1] + 1 / 16, op[2]],
           chimney=[h["pipe"][0], QUARTER + (0.05 if GRID else 1.5), h["pipe"][1]], room="forecastle")

    # (0.3.13, the user's note 131) the pin boards inside the bulwarks (last, the others keeping their indices):
    # right-click one for more sail, with Shift for less (the mod's "pinrail" fixture); a click on a board or its pins
    # picks it, not the bulwark or a gun
    for pr in getattr(d, "pin_rails", []):
        fx("pinrail", pr["code"], pr["pos"], sel=pr["sel"])

    # where people appear when they board from a dock
    d.board_points = [[px, MAIN, 2.9], [px, MAIN, -2.9], [MAIN_X + (1.0 if PROPOSAL else -1.5), MAIN, 1.8]]


if __name__ == "__main__":
    import time
    t = time.time()
    design = build()
    if VARIANT == 3:
        # (0.3.8) her paint zones (paint_zones: each quad's and voxel face's, before her heights are moved), into her
        # definition ("paintZones") and her mesh ("paintQuads", "paintFaces"); and docs/views/paint/zones.json
        import paint_zones
        zmap = paint_zones.assign(design, design.mesh_voxels()[0])
        design.props["paintZones"] = design.paint_zone_list
        paint_zones.write_summary(design, zmap)
        st = paint_zones.straddlers(design, zmap)
        print(f"paint: {len(paint_zones.ZONES)} zones; {len(st)} quads show on both sides of a deck or a castle wall")
    design.translate_y(-WATER)
    stats = design.write(ASSETS, DOCS if "--no-preview" not in sys.argv else None)
    if VARIANT == 3:
        # (0.3.8) the voxel faces written with a zone (paintFaces) cover each zone as zones.json counts it
        boxes0 = None
        want = {}
        for k, code in enumerate(paint_zones.ZONE_CODES):
            want[k] = zmap[code]["area"] - sum(paint_zones.quad_area(np.asarray(design.quads[i]["pts"], float))
                                              for i in zmap[code]["quads"])
        off = {paint_zones.ZONE_CODES[k]: round(design.paint_face_area.get(k, 0.0) - a, 3) for k, a in want.items()
               if abs(design.paint_face_area.get(k, 0.0) - a) > 0.02}
        print(f"paint: the voxel faces written per zone match zones.json's" + (f", except {off}" if off else ""))
        assert not off, "paintFaces doesn't cover the zones' voxel faces"
    print(f"{CODE}:", stats, f"{time.time() - t:.1f}s")
    if VARIANT == 3 and "--no-meshcheck" not in sys.argv:
        # (0.3.10, the user: clean surfaces) the mesh check on what was just written (tools/shipgen/meshcheck.py: report
        # in docs/views/meshcheck/): its summary, and the build fails if visible z-fighting is left
        import subprocess
        r = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "meshcheck.py"),
                            CODE, "--no-images"], capture_output=True, text=True)
        lines = [ln for ln in r.stdout.splitlines() if ln.startswith("z-fighting visible")]
        print("meshcheck:", lines[-1] if lines else (r.stdout + r.stderr)[-2000:])
        if r.returncode != 0:
            raise SystemExit("meshcheck FAILED: visible z-fighting of 0.001 m2 or more (docs/views/meshcheck/report.md)")
