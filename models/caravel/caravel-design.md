# Caravel master model (proposal)

Status: **proposal**, 17 October 2026. Nothing here is agreed or wired into the game. Built in parallel with the
carrack's master model (`docs/master-model.md`) and follows its frame, units and vertical rules so both ships go
through the same slicer later. Reference look: the Mariners' Museum three-masted *caravela latina* model
(1991.0053.000001). The same hull takes a second rig, *caravela redonda* (square fore and main).

## Files

| File | What |
|---|---|
| `tools/caravel/caravel_lines.py` | Hull lines, levels and layout as data (Python + numpy). Writes `caravel-lines.json` and `views/lines.png`. |
| `tools/caravel/build_caravel.py` | AstoCAD build: lofts the lines, cuts sheer and transom, offsets the shell, adds decks, reference, both rigs, fittings and gun placeholders. Writes `caravel.FCStd`, `caravel-report.json`, `views/caravel-mesh.js`. |
| `models/caravel/caravel.html` | **Open this one.** The shared ship viewer (`tools/shipview/viewer.html`, the carrack uses it too) with the caravel's mesh inlined by `tools/shipview/bundle.py`, which the build runs: `#profile`, `#plan`, `#body`, `#bow`, `#stern`, `#deck`; `&rig=latina\|redonda`, `&hide=Body,...`, `&clip=<X mm>`. Needs cdnjs and jsdelivr for three.js. |
| `models/caravel/views/caravel-mesh.js` | The viewer data (`window.CARAVEL`). To rebundle without a build: `python tools/shipview/bundle.py models/caravel/views/caravel-mesh.js models/caravel/caravel.html`. |

```
python tools/caravel/caravel_lines.py
C:/AstoCAD/bin/AstoCADCmd.exe -c "exec(open(r'C:/VS mods/Hansa/tools/caravel/build_caravel.py').read())"
```

Build times (27 October 2026; see "Build speed"): on new lines 23 minutes, mostly three face merges of the lofted
solids; then about 2 minutes for the build and 2 for the export after a change to the rig or a fitting, every step
whose inputs didn't change read back from `%TEMP%/caravel-cache` (`CARAVEL_NOCACHE=1` forces a full build and export).
The slice (7 minutes) runs only when the structure changes. All the offline checks the caravel's work runs, in one pass:
`dotnet run --project tests/HansaTests -c Release -- caravel-all` (44 s).

**Viewer:** drag to rotate, right-drag to pan, wheel to zoom. **Click** picks a point: it is marked, its cell is
outlined in orange, and the list shows the body, X/Y/Z in mm and the cell (x by its aft face, y from the
centreline with port +, z by its floor). **Copy** puts the list on the clipboard to paste into a chat.
**Double-click** sets the orbit centre (green dot). Buttons toggle the grid, reference, ribs, decks, guns, gun
room, sea, rig, hull and envelope.

## Frame and units

The same as the carrack: millimetres, 1 block = 1000 mm; X toward the bow, Y to port, Z up; Z 0 is the bottom
cell border, and the hull's underside is 125 above it (see Draft). Cell borders on whole metres in X and Z, and at ±500, ±1500 … in Y, so the centreline runs through a
cell's middle and the keel's cells are Y −500..500.

## Vertical rules

| Level | Z (mm) | Note |
|---|---|---|
| Keel's underside | 125 | flush with the planking (see Backbone) |
| Hull underside, flat floor | 125 | 125 above the bottom cell border (the carrack's is on it) |
| Hold floor top | 1000 | bottom block: planking 125–325, floor timbers to 800, hold floor planking 800–1000 |
| Waterline | 2875 | 0.875 above a cell border |
| Main (weather) deck floor top | 4000 | hold 1000–3000 clear; beams 3400–3800, planking 3800–4000 |
| Waist bulwark top | 5000 | 1 block above the deck (the user, 17 October) |
| Quarterdeck (*tolda*) floor top | 7000 | from the transom to X −5000; beams 6400–6800, planking 6800–7000 |
| Quarterdeck side top | 7000 | the solid side ends at the deck (the user, 18 October) |
| Quarterdeck railing top | 8000 | an open railing on the side's top (see Railings) |

Decks are 3 apart: 2 clear blocks and 1 structural block. Freeboard to the main deck is 1125 mm; the gun port
sills (4250) stand 1375 above the water.

**Timber sizes** (the user, 17 October): hull planking and deck planking 0.2 block (hold floor, main deck,
quarterdeck); ribs and deck beams 0.2 wide × 0.4 deep, measured square to the hull; the backbone 0.2 × 0.6.

**Draft** is 2750 mm, keel included (the user, 17 October: a bit under 3 blocks, so she can pass in 3 blocks of water). In game
the water's surface is 0.875 up its top water block, which is why the waterline sits 0.875 above a cell border.
With the underside on a cell border the keel would sit exactly on the bottom of 3-block-deep water; lifting it
125 mm leaves that much clearance while the decks and the waterline stay on the grid. Below the waterline the hull's
shape is squeezed into the 125 mm shorter body (`caravel_lines.shape_z`). The carrack's 4875 mm draft has the
same property: she touches bottom in 5 blocks of water.

**Castle break.** The quarterdeck's side (7.2) comes down to the waist's rail (5.0) in a concave curve over 2.5 m
forward of the quarterdeck's edge (−5000 to −2500), steepest at the edge and easing into the waist.

**Reference** (the user, 17 October): the replica caravela latina *Vera Cruz* (Fundación Nao Victoria photos). Two
masts, the main just forward of midships and a smaller mizzen on the quarterdeck; the quarterdeck about a third of
the length with an open railing; a broad rudder on a straight sternpost.

## Plan

Since 26 October 2026, 2 blocks longer (note 73), the sternpost upright (note 102) and a counter with the transom 1 block
aft of it (the user's sketch); before note 73: 23.6 m overall, 21.3 on the deck, 20.5 on the waterline, 222 t; with
the raked stern: 25.5, 23.8, 22.8, 256 t; upright without the counter: 24.55, 24.2, 23.64, 270 t:

| | Value |
|---|---|
| Length overall, hull (transom to stem head) | 25.63 m |
| Length on the main deck | 25.2 m (transom −15.5 over a counter, the post −14.5; stem +9.7) |
| Waterline length | 23.64 m |
| Beam | 6.60 m (half 3300 at Z 3500; the user: 6.6 blocks) — inside 7 cells, Y ±3500 |
| Draft | 2.75 m (the keel is flush with the planking) |
| Displacement | ≈ 264 m³, ≈ 270 t in seawater, block coefficient ≈ 0.60 |

For comparison the carrack is 36.4 × 12.16 m with a 4.875 m draft: the caravel is about 0.7 her length and half
her beam. L/B is 3.88 overall (25.63 / 6.60) and 3.62 on the waterline (23.64 / 6.53). The reference *Boa Esperança*:
23.8 × 6.6 m, 21.7 on the waterline.

### Cells along X (named by their aft face, in blocks)

Since 25 October 2026 (draft D, the user's OK; see "The fifth trial"):

```
cell  -16 -15 -14 -13 -12 -11 -10  -9  -8  -7  -6  -5  -4  -3  -2  -1   0   1   2   3   4   5   6   7   8   9
       .   .  RIB  .  RIB MIZ RIB  W  RIB  QS RIB  HS RIB MAIN RIB  .  RIB  G  RIB  G  RIB  .  RIB FORE  .   .
               S               S   [galley]|[  hatch   ]     pump gate             capstan
       |<---------- quarterdeck --------->|<------------------------------ waist ------------------------------->
```

S a pair of swivel places on the quarterdeck's cap (X −13.5 and −9.5, both sides), W the wheel, QS the quarterdeck's
stair against her starboard side (z +2), HS the hold's stair on the centreline rising forward, the hatch −7..−4, one clear
block (−4) between it and the main mast (now −3); the galley under the quarterdeck's front, to port of the centreline.

Since 24 October 2026 (chalk note 73, the user: 2 blocks longer aft of the main mast; see "The fourth trial"):

```
cell  -16 -15 -14 -13 -12 -11 -10  -9  -8  -7  -6  -5  -4  -3  -2  -1   0   1   2   3   4   5   6   7   8   9
       .   .  RIB  .  RIB MIZ RIB  W  RIB  QS RIB  HS RIB  .  RIB MAIN RIB  G  RIB  G  RIB  .  RIB FORE  .   .
               G       G                   |   [ hatch  ]      gate pump               capstan
       |<---------- quarterdeck --------->|<------------------------------ waist ------------------------------->
```

G a gun pair (the quarterdeck's at -14 and -12), W the wheel, QS the quarterdeck stair, HS the hold stair (both on the
centreline), the hatch -5..-2. The text below is mostly as written before; where it gives a cell or an X aft of the main
mast's ribs, take 2 off it (the quarterdeck, the mizzen, the wheel and the stern moved 2 aft with her lengthening).

Before 24 October 2026:

```
cell  -13 -12 -11 -10  -9  -8  -7  -6  -5  -4  -3  -2  -1   0   1   2   3   4   5   6   7   8   9
       .  RIB  .  RIB MIZ RIB  G  RIB  .  RIB  G  RIB MAIN RIB  .  RIB G  RIB  .  RIB FORE  .   .
       |<----- quarterdeck ----->|<--------------------------- waist --------------------------->
```

- **Masts** (one cell each, in the clear cells between ribs; the user, 17 October: main about midships, two masts
  as most caravels): main −1 (X −1000..0, stepped on the hold floor), mizzen −9 (stepped on the quarterdeck, so the
  tiller runs clear under it), fore 7 (hold floor; the *redonda* only). Upright; the replica's masts rake forward
  (see Rigs).
- **Ribs** every 2nd cell, one clear cell between (the user, 17 October): −12, −10 … 6. None at 8: the forefoot
  is too narrow for a frame there (the user). This differs from `AGENTS.md`'s rule for the carrack (bays of 3 as
  the base); it is the caravel's. Each rib is 0.2 along X at its cell's middle and a band 0.4 thick square to the
  inside of the skin (10 mm clear of it), from the sheer down one side, across the bottom and up the other; where
  the hull is narrower than two bands it fills to the centreline. Its bottom sits 0.4 into the keel. The lines
  script measures every rib (`caravel-lines.json`, `ribCheck`: 398–400 mm everywhere). Rib −12 stands over the
  sternpost: its band starts at Z 2.9 and crosses the centre at 3.3, under its main-deck beam (3.4–3.8); it also
  carries a quarterdeck beam. The ribs' tops at the centre are at Z 725; the hold floor's
  planking starts at 800, so it stands 75 mm above them (no floor timbers between yet). The ribs end under the
  planking of the deck above them (the user, 18 October): Z 6.8 under the quarterdeck, 3.8 elsewhere, unseen from
  above; the bulwarks above the main deck are planking only.
- **Deck beams** at every rib: under the main deck (Z 3400–3800) and under the quarterdeck (6400–6800), 0.2 along
  X, 0.4 deep, from rib to rib across the ship. The decks' planking (0.2) lies on them.
- **Backbone**: keel, stem and sternpost in one piece, 0.2 wide and 0.6 deep (the user, 17 October), along the
  hull's centreline profile. The ribs' bottoms sit its full 0.4 into it, so with 0.2 planking the keel and post are
  flush with the planking's outside. The stem stands out up to 250 ahead of the planking above the waterline, and
  ends at the bow rail.
- **Stern** (the user, 17 October): the sternpost's slope from the bottom continues as one straight line up to the
  transom's top, the transom in the same plane, so the rudder hangs on one axis (a post and a transom at two rakes
  would give it two hinge axes). The rake is the post's original 0.45 aft per 1 up. The keel's after end moved
  0.3 aft, to X −10.4, so that rib −12 has room for its band and a main-deck beam; the line then reaches X −13.94
  at the quarterdeck rail. The sternpost timber ends under the quarterdeck's floor (Z 6.8), and above the
  transom's foot it lies behind the transom's planking, unseen from aft (the user: as the reference).
- **Bow**: above the waterline the stem's rake turns aft (the user: the bow curves back), from X 10.05 at Z 5.5 to
  9.95 at the rail.
- **Quarterdeck front** on the cell face X −5000, the forward face of rib cell −6, so its edge stands over a rib
  and beam. Moved aft from −3000 (the user, 17 October: the castle was too big); about a third of the length.
- **Main hatch** 1 × 2 on the centreline cell, X −3000..−1000 (the user, 18 October), with a stair down to the
  hold. The deck and the beam at rib −2 stop at its sides. Cut in the master for now; later a replacement block.
- **Stairs** (the user, 18 October): one cell in plan, walked with the stair animation (not climbed as a ladder),
  with the cell ahead of the foot as their approach (1 × 2 to use). Both rise aft: the quarterdeck's in cell −5
  (foot X −4.0 on the main deck, head X −5.0 at the quarterdeck's edge, through the railing's open centre cell);
  the hold's in the hatch's cell −3 (foot X −2.0 on the hold floor, head X −3.0 at the main deck). A tread every
  250 mm of rise, two stringers.
- **Gates** (the user, 18 October): in the waist's bulwark at cell −1 on both sides, the bulwark cut away between
  two posts, with a rope ladder down the side from the gate's sill to below the waterline.
- **Railings** round the quarterdeck (the user, 18 October): a 150 mm post with a cap on every cell border along
  both sides, at the Y cell borders across the transom and the front, and at the corners; a top rail and a bottom
  rail joining all posts round the corners; balusters every 0.2 m. Open (no balusters) over the quarterdeck's gun
  cells, and at the front's centre cell for the stair.
- **Bumpkin**: the mizzen's clew spar comes out through the transom at the quarterdeck's floor (it hung loose).
- **Stem head** rises 1 block over the bow rail on the stem's own line (the user, 18 October: decorative).

### Guns

Gun places (the user, 18 October): main deck cells **1** and **3** in the waist (`GUN_CELLS`), quarterdeck cells
**−11** and **−7** (`QD_GUN_CELLS`), both sides; 8 in all, 4 a side. The parked gun stands in its 1 × 2 cells (|Y|
500..2500) and runs out through its port (main deck) or over the railing's open cell (quarterdeck). Carriage 700 ×
1000 × 500, barrel 1600, 1700 overall.

**In the game (20 October 2026).** The waist's ports are cut in the shell: 750 wide in the cell's middle, from the
sill (250 over the deck) up through the bulwark's top, no lids yet. The quarterdeck's guns fire over its side between
the railing's posts, under its top rail. Each gun in `build_caravel.py` (body `Guns`) is a bed 500 wide between four
trucks (r 150), and a barrel on trunnions 650 ahead of the carriage's rear. Run out, it stands as far out as it can
still be trained ±15° about the upright 150 ahead of its carriage and elevated −8..+12° (waist) or −8..+7°
(quarterdeck: its top rail is 155 mm over the barrel at the muzzle), 20 mm clear of the side, the railing and the gates
(`caravel-report.json`, `guns`: one entry a gun, with its rear, trunnions, pivot, muzzle and recoil):

| Cells | Carriage's rear, run out (|Y|) | Muzzle outside the hull | Runs back |
|---|---|---|---|
| waist 1 | 1880 | 415 | 750 |
| waist 3 | 1670 | 370 | 750 |
| quarterdeck −11 | 1220 | 395 | 700 (to its parked place) |
| quarterdeck −7 | 1470 | 412 | 750 |

A gun the bottom rail stops is bound by the railing, one in the waist by its port's sides. `export_caravel.py` makes
them fixtures `gun-port-1..4` and `gun-starboard-1..4` (aft to fore), bronze, the carrack's 4.1 kg shot and 0.14
bore; moving parts `<gun>-train`, `<gun>` (runs back), `<gun>-barrel`, `<gun>-trucks-fore/-aft`, each drawn from
the model's parts (`part:<code>`); a gun place each (laid; no cells, as she has no deck slots yet); a `gun`
keep-clear box from the carriage's front to 0.3 behind the gun captain, who stands 0.5 behind the carriage.

**Detailed and housed (22 October 2026, chalk notes 14 and 17).** `caravel_layout.py` draws each gun again with
`tools/caravel/caravel_gun.py` and drops the exporter's plain meshes and moving parts for it (the master model's
`Guns` body stays the run-out place's source). The gun is the carrack's (`tools/shipgen/gun.py`, unchanged): its
barrel turned from the carrack's lathe profile (base ring, two reinforce rings, chase girdle, muzzle swell, cascabel
with a turn of the breeching, trunnions and rimbases), its breech end scaled to a button 620 aft of the trunnions and
its chase to the muzzle 1050 forward of them, radii 0.82 of the carrack's (the bore as hers: the shot is); its
carriage two stepped cheeks with cap squares, bolts, a transom, a bed and a stool bed, two axletrees, four trucks
(r 150) with hubs, marks and linchpins; the breeching from ring bolts on her side (beside the waist's ports, on the
quarterdeck's posts) through eyebolts on the cheeks round the cascabel. Axis 625 over the deck, carriage 700 wide from
650 aft of the trunnions to 350 forward: everything except the button and the cheeks' tops inside the master build's
gun, which it found clear of her side, railing and gates. Moving parts as the carrack's: `<gun>-train`, `<gun>`,
`<gun>-barrel`, `<gun>-trucks-fore/-aft`, `<gun>-breeching-a/b` and `-a/b-out`.

Each gun is housed behind a lid (`lid` `<gun>-lid`, source `open:<gun>`; `housed` = its run back): a new ship's guns
are run in by their recoil (700 or 750) with their lids shut; laying one opens its lid and runs it out (2 s), Shift +
right-click houses it again, as on the carrack.

- **Waist** (the user: "maybe can be gate type sideways"): a side-hinged port lid of her planks (tarred outside), 40
  past the port's jambs and under its sill, its top under the cap rail, 60 thick on her side outside; hinged on its
  after edge along her side's lean, two strap hinges, a ring bolt near its fore edge; it swings out 110°.
- **Quarterdeck**: the railing's section between the two posts over the gun, a small gate of two stiles, two rails and
  three balusters as the railing's, between the bottom rail and the top rail; hinged against the after post's fore
  outer edge, it swings out 110° the same way (hinged aft, it stays clear of the mizzen shrouds at −10.45 and −9.55).

The layout checks every gun (and stops if one fails): the barrel laid anywhere clear of its carriage (9 mm in the waist,
17 on the quarterdeck), the breeching off the barrel, inside the master build's gun (0 mm: the linchpins at its width),
each lid swung through its arc clear of her side, the posts and the top rail (4–35 mm), open 170–250 mm from the gun
laid to its limits, and the muzzle housed 300–330 mm behind the shut lid in the waist, 70 on the quarterdeck. Views
(`python tools/caravel/caravel_gun_views.py`): `views/guns_waist.png`, `views/guns_quarterdeck.png` (housed, run out,
laid +15° / full elevation; from her deck and from outside).

## Rigs

Same mast cells for both. Rig bodies are placeholders for proportions, not slicer input. Study:
[rig-study.md](rig-study.md) (`tools/caravel/rig_study.py`).

**Latina rig from the user's sketch** (18 October): each yard balanced on its mast, crossing it near halfway (a
yard slung near its end does not work); the mizzen's sail with a level foot 2.2 m over the quarterdeck and its
after edge over the stern; the main's foot sloping up aft from the tack to a clew over the waist. Points in
`build_caravel.py`, `SKETCH`. The masts are taller than the photos' ratios (main 24.8 m, mizzen 21.8 m over the
bottom border): the mizzen's yard crosses it at 16.1 m. The mizzen goes down to the main deck's beams (Z 3.4) on a
step between two beams; the tiller stops 0.3 short of it. The redonda's main keeps 80 % of the hull.

| | *latina* (two lateen) | *redonda* (square fore and main, lateen mizzen) |
|---|---|---|
| Fore (7) | — | mast to Z 15.5 m; course yard 8.6 m |
| Main (−1) | mast Z 1.0–24.8; yard from the tack (8.1, 6.2) to the peak (−8.7, 29.2), crossing the mast near halfway; clew (−3.8, 9.0) | lower mast to 18.8 m, topmast to 24.8 m; course yard 12.4 m, topsail yard 6.2 m |
| Mizzen (−9) | mast Z 3.4–21.8; yard from the tack (−4.6, 9.2) to the peak (−14.0, 25.7), crossing the mast at 16.1 m; clew (−14.0, 9.2) over the stern, sheeted to the bumpkin | the same |
| Bowsprit | — | from the stem head, 9.5 m |

**Head room** (the user: 2 blocks free for players, also as the sails move): every sail's foot is at least 2.2 m
over the deck below it, in its normal place and with the yard swung up to abeam either side (going about, a
lateen yard is dipped round its mast, not swung further); no sail passes through the other mast; the main's clew
stays clear of a player on the quarterdeck's stair. The head room rule still leaves the lateen sails about two
thirds of a historical caravel's area, and the sail centre aft of the hull's (see the study).

**Mast rake.** The replica's masts lean forward, as lateen masts did (the yard hangs clear ahead of the mast,
the halyard holds it against the mast, and the sail's pull moves forward, balancing the steering). Here they stand
upright so each mast keeps to its cell. Open for the user.

## Bodies in `caravel.FCStd`

| Body | What |
|---|---|
| `Envelope` | Closed solid: outside of the hull, cut at the sheer and the transom plane. `SliceRole` envelope. |
| `Shell` | Envelope minus the inner solid. The inner face is the outer half-breadth field eroded by a 250 mm ball (`caravel_lines.inner_waterline`), so the skin is at least 250 thick in every direction and the stem and post are solid. `SliceRole` structure. |
| `Backbone` | Keel, stem and sternpost, 0.2 × 0.6 (see Plan). The shell is cut around it. `SliceRole` structure. |
| `Ribs` | One frame in every 2nd cell (see Plan), 0.2 × 0.4, taken from the narrowest inner section across its thickness. `SliceRole` structure. |
| `Beams` | Deck beams at every rib under the main deck and the quarterdeck, 0.2 × 0.4. `SliceRole` structure. |
| `Decks` | Hold floor, main deck and quarterdeck planking, 0.2 thick, tops on the levels above; it stops at the ribs. `SliceRole` structure. |
| `Guns`, `GunsParked`, `GunPlaces` | Guns run out (the exporter's source for the guns in the game: bed, fore trucks, aft trucks, barrel a gun), guns parked, and each parked gun's 1 × 2 cell place (see Guns). Not sliced. |
| `Reference` | Cell grid at Z 0 and the main deck, waterline, deck levels, quarterdeck front, mast cells, rib cells, gun port outlines. |
| `Rig_Latina`, `Rig_Redonda` | Masts, yards, flat sails, tops, bumpkin, bowsprit. |
| `Railings` | The quarterdeck's railing (see Plan). Placeholder, not sliced yet. |
| `Stairs`, `Gates` | The two stairs; the gates' posts and rope ladders (the gates' openings are cut in the shell). |
| `Fittings` | Rudder: one straight leading edge on the sternpost's line and one straight trailing edge, 1.5 m wide at the foot and 0.35 at the head, the head 0.5 over the main deck. Tiller from the rudder's head forward under the quarterdeck, stopping 0.3 short of the mizzen. The mizzen's step. |
| `Waterlines/Waterline_nn` | The lines as smooth closed splines, for reference and editing. |

**Faceting.** The solids are lofted through polygons of the lines' points (about 80 a side, waterlines 125–250 mm
apart), so they are exact at every point and linear between. AstoCAD 1.1's booleans failed silently on lofts
through periodic splines, ruled or smooth (the shell came out as the whole envelope, or empty). A smooth
master needs another route (e.g. a sewn B-spline surface with its caps); the carrack's build may meet the same
problem.

## Against the carrack session's constraints (its message of 17 October 2026)

Matches: frame, units, Y cell borders, bottom block (hold floor 750–1000), decks 3 apart, waterline 0.875 above a
border, 125 half-breadth stem and post (250 faces), a y-convex hull, mast cells between two ribs on the odd parity,
quarterdeck edge on its last rib cell's face, ports 750 wide in one block and never in a rib cell, sill 250, lintel
1000, waist bulwark 1250, no OCC offsets, shape-API booleans only.

Differences, still open:
- **Gun spacing.** "One bay apart" was Magelan's 2-block frame spacing, not a rule of the new design (carrack session,
  17 October). Agreed: every port is one hull block and none is in a rib cell; the user said guns may change "in
  positions and maybe quantity". Two ports in a 3-cell bay, 2 cells apart, follows Magelan's waist. Still for the
  user to confirm.
- **Hatch size.** "2×3" was never oriented. With the centreline through a cell's middle, a centred hatch has an odd
  width: 1 or 3 across, and 3 along fits a 3-cell bay. Undecided for the carrack as well. Here the hatch is 1 across
  × 2 along (X −2000..0) between the gun pair at −3 and −1. A 3-across hatch does not fit between those guns' run-back
  room, but would fit in a bay without guns (e.g. 3..5 forward of the mainmast, if the gun at 3 moved).
- **Object names.** The carrack's master uses waterline sketch groups (LowerWaterlines, UpperWaterlines),
  a SheerProfile sketch, RibCells and DeckSlabs boxes (HasBeams), a Parameters spreadsheet and a SliceRole property,
  rebuilt by `tools/master/`. The caravel uses plain Part features. Aligning them would let the same rebuild and
  slicer run on her once the carrack's pipeline settles.
- Masts, guns and fittings are not in the carrack's plain-hull master. Here they are separate placeholder
  bodies and stay out of slicing.

## In the mod (first version, 18 October 2026)

The user: "build first version of caravel into mod; remove the carrack from the new current release". Release
0.1.0-dev.2 carries the caravel (and the skiff); the carracks' files stay in the repo but are left out of the
Release build (`Hansa.csproj`, `CarrackAssets`; the Release output folder is emptied first).

Pipeline, all from the master model:

1. `tools/caravel/caravel_lines.py`, then `build_caravel.py` (AstoCAD): the model (`caravel.FCStd`).
2. `tools/caravel/slice_caravel.py` (AstoCAD): every structure body (shell, backbone, ribs, beams, decks) cut by the
   block grid into one piece per body per cell; a piece under 4 litres joins the neighbouring cell holding the same
   body across the face it touches (the user's direction; it departs from `docs/master-model.md` §7's one block per
   cell, so removing that neighbour takes the sliver with it). A sliver moves at most once, into a face neighbour
   that keeps a piece of its body that is no sliver itself (21 October 2026: the first run judged moved slivers again
   in their new cells, so some travelled two or three cells, some to a diagonal neighbour). Now: 565 cells, 1,114
   pieces, 78 slivers merged, 18 with no such neighbour, kept and flagged; every body's pieces add up to its volume
   exactly. `cells.json`, `cells.brep`.
3. `tools/caravel/export_caravel.py` (AstoCAD): `assets/hansa/config/ships/caravel.json` and
   `config/shipmeshes/caravel.json`.
   - Collision: a 0.25 voxel grid (98 × 33 × 28, 15,883 solid) from rays across the ship through the shell, backbone,
     ribs, beams, decks and railings; its borders on the block grid (y −0.875 + k).
   - Mesh: the sliced pieces' faces, 16 texels a block, UVs local to each piece's cell (planks along X on sides and
     decks, along Y on end faces); a face lying on a cell border is left out only where the neighbouring cell's
     pieces fill the space across it (master-model §7; 21 October 2026: before, every border face was left out, the
     decks' and the bulwarks' tops with them), and one only partly covered is drawn. The shell's outer
     faces below the waterline in `bottom`/`bottomclad` (copper), its inner faces under the main deck in `interior`;
     `hullOut`/`hullIn` give the shell's quads for the hull damage drawing.
   - `shot`: the cast iron ball the game draws for every cannonball in flight (as the carrack generator's; 21 October
     2026, the release had no ship mesh with one, so the balls were not drawn).
   - Rudder, tiller, both lateen yards, the guns and the gate leaves as moving parts; the rope ladders at the gates;
     masts, decks, hull samples; the deck cells (below). It writes a base (`models/caravel/export/`) and runs step 4.
4. `tools/caravel/caravel_layout.py` (plain Python, also run alone in seconds): the fittings, the rigging, the sails
   and the gameplay data on top of the base, drawn with the carrack generator's mesh helpers (`tools/shipgen`'s
   `Design`) in the game's frame; where each fitting stands is ship data (`caravel_lines.FITTINGS`).
5. Kept by hand: `entities/ship/caravel.json`, the charter `shipdeed-caravel` (the only charter in the creative tab
   now), and the lang lines. Commands that name no ship now mean the caravel (`HansaMod.DefaultShip`).

   - Guns (20 October 2026): 4 a side, laid on a new ship, with their ports in the waist (see Guns).

### The user's list after trying her (21 October 2026)

Fourteen chalk notes (11–24) and a list: sails flicker, have no roping and may turn wrong; no plaque, lanterns,
anchor, pump or painting; the helm blind behind the mizzen. What was done:

- **Sails** (item 1): each lateen is a single-sided mesh bellied to starboard, as the carrack's (the mod mirrors it on
  the other tack); the flat sail drawn twice, back to back, flickered. The sail's axis is its tack up along the yard
  and its clew its sheet's tip, so the game knows it's a fore-and-aft sail (before, with no sheet, it took it for a
  square sail hanging from its yard). Running rigging: a sheet (the main's to the quarterdeck's front rail, the
  mizzen's out on the bumpkin), a tack and a halyard per yard, each following its yard; three shrouds a side on the
  main to the cap rails, two on the mizzen to the quarterdeck's rail, also given to the mod to press the sails
  against (`rigCheck`). Yards thicker: 300 at the main's sling, 200 at the mizzen's, gentler tapers. The main yard
  stays 30.9 m (the user's sketch); shorter is the user's call.
- **Plaque, spawn plate, lanterns** (items 2, 3): a bronze plaque on the mizzen's fore side in the steerage, the plate
  place on its port side; a lantern post on the taffrail's middle (a lantern on it), lantern hooks at both masts (a
  lantern on each), and the mod's own ceiling and mast hooks.
- **Anchor** (item 4): a capstan on the foredeck (cell 6), a cathead on the port bow with the anchor hung from it
  (stock, shank, arms, flukes; it drops 8.5 when let go), its cable from the hawse to its ring and inboard to the
  capstan.
- **Pump** (the user: "no pump"): forward of the main mast (cell 0), its tube down into the hold, a head with a spout
  forward, its brake athwartships on a fork (the moving parts `pump-brake`, `pump-spear`).
- **Painting** (item 5): nine zones (topsides with the gates, cap rails, the quarterdeck's railing, the bulwarks
  inside, the frames standing in them, the hold's walls, its timbers, the hold's and the steerage's ceilings), each
  quad of her hull, rails and timbers in one (`paintQuads`).
- **Wheel** (note 21, the user's choice): a wheel on the quarterdeck aft of the mizzen (its rim at x −9.6, where the
  user pointed; "too close to mizzen mast" at −9.3 first), the helmsman standing aft of it at −10.4 on the centreline; the tiller stays under the quarterdeck with its ropes out to blocks at her
  sides. The steering place on the main deck is gone.
- **Stairs** (note 18): the game's walked ship's stairs (`stair` pieces, the steep 1 × 2 one: about 56°, 3 blocks up),
  not ladders. Through a deck it needs a 3 × 1 well (the walker's head) and free landings at both ends, and a ship's
  stair is solid with its own handrails a quarter block either side, so two stairs' walkers keep a row apart; no
  opening may lie in a gun's working room (the guns at cell 3 swing their trails over the centreline). So: the
  quarterdeck's stair one row to starboard (foot cell −4, head −5 at the quarterdeck's front, landing −3), the
  railing's gap with it, its two posts 0.1 out of the gap; **the hatch under the quarterdeck in the port row**, cells
  −7..−5 (1 × 3), the hold stair rising aft in it from its foot in cell −6 out onto cell −8 of the main deck, beside
  the mizzen. (Tried first: the hatch forward of the main mast, cells 2–4 on the centreline: in the gun's room.)
- **Hatch** (note 22): carlings along its sides and headledges across its ends under the deck, the beams' size; its
  cells are deck slots that start empty, with a trapdoor hatch over them (a `hatch` piece, laid shut).
- **Deck cells** (note 16, part): every whole cell of the main deck and the quarterdeck is a slot (`deckSlots`, the
  `slot:deck` groups and `slotQuads`), but those under the masts, the pump, the capstan and the wheel, the gun rows and
  the gates' row: an axe takes them up, planks lay them. Hull panels are not slots yet.
- **Blocks on deck** (note 15): her main deck had 15 floor places, the quarterdeck 1 (now 27 and 5: the rest is the
  guns' working rooms, the pump's and the capstan's room, the wheel, and the cells by her sides that hold her ribs). The stairs as ladders, a boarding
  point on the centreline, the railing's bars and the cap rail's overhang (the mod takes anything with air under it
  near a deck for a stair's tread and keeps the cells round it clear) took the rest. The railings are now solid down
  to what they stand on (and anything of hers standing on air within 1.25 over a deck, a rib's head leaning in), the
  boarding point is gone, the stairs are pieces.
- **Masts** (note 11): solid in her collision.
- **Water** (note 24): the water is held out by her hull's exact outline from her lines (`waterClip.dryOutline`), not
  a box that left gaps round the curved bow.
- **Beams** (note 23): each runs into its rib and is cut by it, ending on the rib's curved face.
- **Grain** (note 20): the wood's grain follows each piece: planks along the keel, beams athwartships, ribs, carlings
  and the backbone along their own length.
- **Cap rail** (note 13): on the waist's bulwark each side, 360 wide (40 proud outside), 80 thick on the sheer, broken
  at the gates; over each gun port it is the port's head.
- **Gates** (note 12): no posts; a leaf in each opening (planks, two ledges, a brace), hinged on its after edge,
  swinging in (`gate-port`, `gate-starboard`). The rope ladders are drawn as rope.

Checks: `dotnet run --project tests/HansaTests -- design caravel` ALL OK (deck slots, the stairs walked up and down,
the hatch over its opening, the gun places' cells); the server self-test ALL OK, 183 checks (release 0.1.0-dev.4).

Since 22 October 2026 the guns are the carrack's, fitted to hers, and housed behind lids (notes 14, 17; see Guns).

### The second trial (22 October 2026, chalk notes 26-40)

- **Magelan's fittings** (`tools/caravel/caravel_fittings.py`: the carrack generator's geometry with her positions):
  the wheel with its posts, drum and ropes; the capstan (whelps, drumhead, pawls, bars); the bronze pump with its
  brake and spear; the mast lantern bands and arms; the stern lantern's crane on the taffrail.
- **Wheel** (note 30): forward of the mizzen at x −6.77, where the user marked it; the helmsman aft of it. The
  quarterdeck's guns moved from −7 to −9, beside the mizzen (the user: "place where cannons are and move them").
- **Tiller** (note 29): up under the quarterdeck's beams (the rudder's head with it), 2.4 long; the wheel's ropes off
  the drum down through the deck, out under the beams to blocks at the sides and aft to blocks abreast the tiller.
- **Anchor** (note 26 and the user's follow-ups): the capstan abreast the cathead (cell 6), the cable from the hawse
  (x 8.2) down to the deck and along it straight to the barrel's foot, on a tangent, wound up it the way it arrives,
  its slack coiled on the deck. The cathead outside the hull, level and square to the side, its heel bolted through
  the planking into rib 6 under the cap rail, an upright knee under it from the side.
- **Railing** (note 32 and the user's follow-up): a post on each cell border and one the posts' size halfway between,
  a 200 × 140 top rail, the bottom rail on the deck, no balusters; the stern's posts upright, not raked.
- **Cap rail** (notes 34, 37): out to half the ribs' depth inboard (400 from the planking's outside), on to the stem
  where the two sides meet; the wood's grain along it. The ribs' heads in the waist narrow from the main deck's top to
  0.2 at the cap.
- **Beams** (note 35): square-ended, seated 0.1 in a notch cut in their rib (an L seat), not cut to the rib's face.
- **Gates** (note 28): the leaf the bulwark's full thickness and height, its own piece of cap rail on top.
- **Dye** (note 40): a hull face is outside where a point just off it is out of her lines' envelope; the bow's inside
  faces, facing aft, were taken for the transom's outside (tarred, in the bottom's group, not paintable).
- **Yards**: 240 at the main's sling, 170 at the mizzen's (300/200 was "too thick").
- **Steerage** (note 38): a room walled by pieces (`"enclosed": true, "pieces": true`), so partitions up to the
  quarterdeck close it (with the partitions session's change to the mod); the hold hatch crosses its front line, so a
  block-wide gap stays over it.

Done in other sessions on their own branches (mod code): dropped items aboard (2), bubbles and particles inside the
hull (4), the wind's no-go zone on the minimap (the image), the one-cell ship's stair and the stair stringers (36, 39),
partitions up to the quarterdeck (38).

Not yet: hull panels as slots (note 16 and the user's item 3), rooms, a hearth.

### The third trial (22 October 2026, release 0.1.0-dev.6, chalk notes 41-52)

- **Hull panels** (the user, the fourth time: "still cannot remove hull elements"): `export_caravel.py` makes her side's
  planking `hullSlots` (docs/legacy/slots.md §9, §11.1): one a cell of her side (a block along by a block up) in the
  rows between her decks (the hold's 1 and 2, the steerage's and the waist's 4 and 5; row 5 only where row 6 has
  planking). Fixed: the bottom row, the decks' rows, a cell reaching the centreline (stem, sternpost), the waist guns'
  ports, the gates, three cells round the anchor and its hawse. A panel owns the shell's voxels in its cell that no
  other body shares (a rib crossing it stays when it is out); its edges toward a neighbouring panel are pairs, the
  fixed pieces' faces toward it are drawn only while it is out. The old `hullOut`/`hullIn` are gone. Taken out in
  building mode with an axe; afloat only over her waterline (rows 4 and 5).
- **Flicker** (the user: "check 3D model overlaps ... for ALL models ... rule for any new or modified models"):
  `tools/caravel/overlap_check.py` finds drawn faces in one plane, facing one way, overlapping (806 pairs on dev.6). Fixed
  at the source: the railings one fused solid (posts' caps, bars' joints), the gate leaves fused, the mizzen step under
  the mast's heel, the wheel's drum turns and the capstan's coil apart; `caravel_layout.deflicker` then spreads what is
  left 2 mm a face along its normal (slots' faces stay). The layout prints how many are left (0 wanted).
- **Textures** (notes 42, 44, the user's item 3): rails, caps and gates take the grain along each face's longest edge from
  one origin for the whole ship (no longer per triangle); the gate leaves' outsides and insides the hull's planking
  (note 41).
- **Quarterdeck gun lids** (note 45): the railing's style, the member the posts' size between two light rails; the port
  between the bottom rail's top and the top rail's underside.
- **Tiller** (note 46): the sternpost's head stops 0.25 under the tiller, which passes over it.
- **Capstan** (note 47): Magelan's size (radius 0.48, bars 0.95); the hawse at x 6.75, just forward of rib 6.
- **Yards** (the user's item 7): 165 at the main's sling and 125 at the mizzen's, tapering to 0.6 and 0.5 of that.

- **Beam lengths** (notes 51, 52, with the stair session's mod change): each main-deck beam's length in a rib cell under
  a deck slot is a beam segment, `beside` the one cell over it (22 of 34; the others aren't clean in their cell). A stair
  along her from the hold takes the length over the cell before its foot out. No carlings are drawn when one is out
  (cosmetic, not done), and a deck cell can be laid again over a length that is out (as carrack-m's).
- **Review for performance** (the user, 22 October 2026: "after build do review by yourself and /codex ... Specifically
  look at performance aspects"; two Codex passes, no P0/P1): the ship's atlas doesn't repeat, so the mod squeezed 230
  quads longer than a tile into one tile and shifted 3,530 that crossed one (rails, caps, yards, tiller: the "texture
  mess"); `caravel_layout.split_tiles` cuts every quad at its tiles' edges (none outside one now). Triangles (81,000 of
  102,000 quads) are paired into quads only where flat, convex, wound alike and under one texture mapping
  (`pair_ok`); the flicker pass never moves a slot's faces, and the layout refuses to write while faces overlap;
  `overlap_check.py` sweeps instead of comparing every pair in a plane and no longer misses faces 0.1° apart far from
  her origin. Her mesh: 88,387 quads, 17.5 MB (dev.6: 103,874, 20.3 MB), 0 overlapping, 0 outside a tile. In the mod:
  a piece's structural answer and the beams in a stair's way kept per plan while `FitVersion` holds (asked every frame
  for the piece in the crosshair); dropped items' frame work without copies. Left: the degenerate second triangle of a
  triangle-quad (the multi-texture upload wants four vertices a face).

In other sessions (mod code): items shuffling on deck (1), partitions through a beam and where stairs were (48, 49),
stairs in the hold (51, 52), building mode without block snapping (5), the telescope taken for a gun (6), lanterns'
light (43). Note 50 (the carlings left after closing a hatch): they are the opening's framing, part of her structure.

### The fourth trial (24 October 2026, release 0.1.0-dev.8, chalk notes 53-84)

**Two blocks longer (note 73: "we should make our ship 1 block longer here so we can place stairs and have decent
layout", just aft of the main mast).** With ribs 1 or 3 cells apart (`AGENTS.md`) every rib keeps its cell's parity, and
both masts need a rib each side and the quarterdeck's front sits on a rib's forward face: one block between the masts
can't keep all three. Two drafts went to the user (`CARAVEL_VARIANT=A|B`, `CARAVEL_OUT=models/caravel/variants/<v>`,
the views by `tools/caravel/variant_views.py`; the folder is git-ignored): A, 1 longer, the front at -6 off a rib, the
wheel behind the mizzen for a centreline stair; B, 1 longer, the waist 2 longer and the quarterdeck 1 shorter. The user
chose a third: **2 blocks longer**, B's waist and the quarterdeck as it was (`caravel_lines.LAYOUTS["C"]`, the default).

- 2 m more parallel middle body (`XM_A` -4.5), the stern 2 aft (the keel's after end at X -12.4, the transom's top at
  -15.94). The bow, the main mast, the waist's guns, the gates and the pump keep their cells.
- Ribs every 2nd cell, -14 .. 6 (11): the quarterdeck's are today's moved 2 aft, one new rib at -4 in the waist.
- The quarterdeck's front at X -7 over rib -8; the mizzen at -11 (its heel now flush under the main deck: note 58), the
  wheel at -9 forward of it; the quarterdeck's guns at -14 and -12 (1 aft of before from the transom: note 62). Her
  lateen sketch's mizzen points move with the mizzen.
- **Stairs on the centreline** (the user: "stairs should be moved to the center of the ship (if we can sort rope
  management)"), rising aft in one line: the hold's in cell -5 up through the hatch (-5..-2, 1 wide, its carlings and
  headledges as before), out onto -6, the landing it shares with the quarterdeck's stair in -7, whose head is the
  quarterdeck's front. A clear cell (-2) between the hatch and the main mast. The railing's gap at the front's centre.
- **A cabin** under the quarterdeck (the user: "add default partition wall with doors and window to rear to form cabin,
  similar to how in current test"): its front on the line x -7, the pieces a player places (`modularPieces`): oak plank
  partitions three rows up to the quarterdeck, a windowed sleek oak door either side of the stair (z ±1, two rows), a
  window between them behind the stair (z 0, the middle row), no partition in the top row behind the stair's head (its
  last tread meets that row's top). The places outboard of the doors (z ±2) are the side's wedge cells, which take no
  window or door (the drafts drew a window there). Removable and movable as any piece.

**The others:**

- **53** (the cap rail's grain across it forward of x 2): its faces take u from the length along the rail's middle line
  (in plan and in height), v across or up it (`export_caravel.cap_uv`).
- **56** (the quarterdeck's rails can't be taken out): each span of its sides between two posts (top rail, bottom rail,
  member) is a solid of its own ending on the posts' faces, and a hull slot of band 7 (`hull:<p|s>:<station>:7:<half>`,
  `room` null, its voxels with the column under its top rail filled down to the deck), taken out and laid again as her
  side's panels. No new slot kind. The spans over the guns (their lids), across the transom and the front, and short ones
  by the corners stay fixed: the transom's would share a station and half, the runtime's hull slot key.
- **58**: no mizzen step in the hold; the mizzen ends flush with the main deck's underside.
- **59** (the user confirmed: the bumpkin): its heel runs in on its line to the after face of the first quarterdeck beam
  (it stopped 0.6 inside the transom, in the air).
- **60**: the gate leaves are plain inside, as her side (no ledges or brace).
- **61** (the fittings session: the open leaves hit her side): each leaf hinged on its inner face at its after edge, no
  cap of its own (the bulwark's cap breaks at the gate), narrowed so its outer forward corner passes the forward jamb
  (15 clear); her collision's gap at the gate cleared to the opening's width (it was 0.5 against 0.82 drawn).
- **62**: the quarterdeck's guns 1 aft (above).
- **63** (the user): no stern lantern, post or crane.
- **64**: a sheet's or tack's end on a sail's corner or a yard has an iron ring; a rope made fast on a rail ends on an oak
  cleat on the rail's top, its turns across the horns: the main sheet and the mizzen's tack on the quarterdeck's front rail
  (both sides: the mod mirrors the ropes' ends on the other tack). The halyards' falls run from a block at the masthead
  down to a cleat on the mast, chest high (they ended in the air). The sail through the mast and the dip round it are the
  lateen session's (`sails[].dip`; each mast's `radius` the widest where its yard sweeps).
- **65**: the mizzen's tack led to the front rail on its yard's side, clear of the stair on the centreline.
- **67**: the main tack to an eyebolt in the stem head's after face (it ended over the bow in the air).
- **68**: the waist lids' insides the hull's inside planking, the quarterdeck's lids the railing's wood; both painted with
  them (`paintQuads` for `part:*-lid`: topsides, waist bulwarks, quarterdeck railing).
- **69** (the cap rail didn't cover the ribs' heads toward the bow, where her side narrows fastest and a rib's section,
  the narrowest across its thickness, stands further in at its after face): the cap's inboard width measured on the built
  ribs, 400 where that covers them and wider at each rib toward the bow, tapering between (`capRail.inAtRibs`).
- **76**: the masts, yards and bumpkin take the wood's grain (u) along each spar, v round it (`add_pole`).
- **80**: the hold's floor drawn as inside everywhere (forward of the quarterdeck's old front it was lit as outside).
- **81**: the pump's pipe brass, 0.09 across, out of the chamber over the deck, aft into the main mast's cell and down
  beside the mast to a strainer on the hold floor: no hold cell and no beam in its way.
- **84**: no caps on the railing's posts; each ends inside the top rail.
- For the runtime sessions: `panelPlaces` false (note 82), the figurehead's hook on the stem (note 78).
- Beam lengths: a beam's length is judged by the beam's own piece in its rib cell, not a hatch's headledge beside it (the
  hatch's after end shares cell -6 with rib -6's beam): 31 of 39 come out (25 before that).

Checks: `tools/caravel/overlap_check.py` 0 of 79,018 faces; `dotnet run --project tests/HansaTests -c Release --` design
caravel, holdstair, steerage, modular, gunclick, partitions, decor, stairs and hull ALL OK; slots all OK on the caravel
(its 2 failures are carrack-m's castle gun ports, not changed here). Test changes: notes 48 and 49 take the hatch from the
design (note 49's hatch under the steerage's front is gone); note 51's hold cell moved with her frames to x -6.241 (under
the new hatch at -2.241; at -8.241 the hold is too narrow aft for a landing); a railing's span on her top deck needs no
frames' strips (its posts are drawn whole). Server self-test (Release, 0.1.0-dev.8 with her new data): 198 ok, 1 FAIL,
the broadsides' share of shots into her hull (18 of 56, 40 % wanted; she floods and founders as wanted). That check fails
the same way on dev.8's own caravel (22 of 68): the balls are spent in the water short of her, not a change of this round.

### The fifth trial (25 October 2026, release 0.1.0-dev.9, chalk notes 85-105)

Note 85 ("now top of rails has holes and not continious. Do you ever check 3d edits?"): from this round every change is
looked at as the game draws it, not only in the CAD viewer. `tools/caravel/game_view.py` renders the exported mesh
(`config/shipmeshes/caravel.json`) with the game's own textures, her slots as a new ship has them and her laid pieces as
plain boxes (cut to her side as the game cuts its places), from any eye in her ship-local frame, posed: `{"rudder": ±1}`
turns the rudder and tiller, `{"tack": -1, "sheet": deg}` mirrors and sheets the rig about each mast with the windward
shrouds only, as the mod does. `python tools/caravel/game_view.py <shots.json> <out dir>`; this round's pictures are
`views/game/01-…24-*.png` (`views/game/shots.json`).

**Draft D (the user's OK, "Yes, build it")**, `caravel_lines.LAYOUTS["D"]`, the default:

- **The main mast 2 aft**, to cell −3 (X −2.5): the middle of her keel (Castro: a caravel's main mast stands on the
  middle of the keel; hers runs −12.4..5.6). The pump beside it at −2, its pipe down the mast's cell.
- **The waist**: one clear block (−4) between the mast and the hatch, now −7..−4; the hold's stair on the centreline in
  −5, rising forward. Carlings beside the hatch run from the nearest whole beam aft of it to the nearest forward (notes
  93, 94: they stopped at the opening's ends).
- **The quarterdeck's stair against her starboard side** (the user: "Stairs to castle we could place on right side next to
  hull. At least on Nao Victoria it is done like that"), in −7 at z +2, its head the quarterdeck's front. The cap rail
  narrows to 330 inboard beside it (over 1 m), so its head isn't under the cap; through the castle's break, where the
  tumblehome brings her side in over the stair's cell, its inner edge stays 15 outboard of that cell (her collision
  there refused the stair).
- **The galley (note 105)**, the user's from ship #227 in their world (read from a copy of the save, never the live file):
  start pieces under the quarterdeck's front, open to the waist on the port side as they built it: plank partitions on
  the front line x −7 at z 0 and +2 (three rows), +1 over a door, +3 (two rows, her side's outer cell: note 87 closed);
  the back line x −9 at z −2..0 (none behind the hearth's bottom row, which it would share a plane with); a fore-and-aft
  wall at z +0.5 from −9 to −7 between the galley and the cabin. The hearth (`game:firepit-extinct`) at (−9, −1) facing
  forward, the oven on its table at (−9, −2) level 1, a second table at (−9, 0), a storage vessel at (−8, 0). All
  ordinary pieces: taken up and moved as any.
- **The quarterdeck's sides a bulwark** (note 103: "should we make hull board on top of castle instead of rails. gun port
  gates are trash here"): her side planked up a block over the quarterdeck from the transom to the front, the ribs up
  through it to the cap, narrowing from the deck; the cap rail one run a side from the transom to the gate and a cap
  across the transom (note 85: no holes, no breaks). Only the front keeps its railing. Its row of planking comes out
  as her side's panels (hull slots, band 7: note 56's spans became panels).
- **No carriage guns on the quarterdeck**: 4 swivel places on its cap, 2 a side at X −13.5 and −9.5, in the fittings
  session's format (`{"type": "gun", "kind": "swivel", "code": "swivel-<side>-<n>", "pos", "out", "trainLimits"
  [-80, 80], "elevationLimits" [-25, 30]}`); the swivel itself is the fittings session's, drawn by the mod. Her 4
  waist guns stay. The self-test counts great guns without swivels and wants at least 4.

**The stern (note 102, then the user's sketch: "something like this, get 1 block overhang in cabin")**: the sternpost
upright at X −14.5 with the rudder on it, a counter over it and the transom a block further aft. In the lines the stern's
after outline (`caravel_lines.stern_profile`, `sternProfile` in the lines file) is the post from the keel to Z 3.2, a
counter rounding out (a quarter sine: nearly level at the post, upright at the top) to the transom's foot at X −15.5,
Z 3.6, and the transom flat and upright from there to the rail; its inner face (`sternProfileInner`) 250 in, square to it,
so the counter's planking is as thick as her side's. The build cuts the hull and its inner solid by these outlines (a
plane before). The cabin, both decks and the quarterdeck run 1 block further aft (25.2 m on the deck); the waterline is
as it was (the counter starts above it). No new rib: cell −15 is the stern's one clear cell after rib −14 and the
transom (cell −16) takes the decks' ends.

The rudder's blade hangs on the post from the keel to Z 3.1, under the counter; a round stock (r 0.1, its fore side on the
blade's leading edge) goes up through the counter and the main deck (a hole 25 round it) to the tiller, level at Z 6.25
under the quarterdeck's beams, 2.4 long (X −14.69..−12.27), inside the cabin: no port in the transom (note 86). The
game's rudder and tiller turn about the stock (X −14.63, `rudderAxis` in the report). The bumpkin's end 1.6 outside the
transom (X −17.1), its heel in to the first quarterdeck beam. Before the counter (25 October 2026) the transom was flat in
the post's plane at X −14.5, the tiller through a slot in it.

**The sails (the user's choice B, then the user's sketch: "I made red triangles around approximate with 2 block
clearance below")**: Boa Esperança's sails were 155 and 80 m² on a 26 m and a 20 m yard. The user's triangles bring the
feet down to about 2 blocks over the deck under them, the main tacked low at the bow head and rising aft, the mizzen's
level; the sail runs to its yard's end (no cut to an area):

| | Yard | Tack | Peak | Clew | Foot over its deck | Area |
|---|---|---|---|---|---|---|
| Main | 29.6 m, crossing the mast at Z 20.0 | (8.1, 6.4), its tackle to the stem head | (−10.2, 29.8) | (−6.4, 9.1) | 2.4 at the tack, 4.4 at the mast | 144.9 m² |
| Mizzen | 19.1 m, crossing at Z 18.1 | (−7.6, 9.25) | (−13.6, 27.5) | (−17.1, 9.25) at the bumpkin's end | 2.25 m, level | 86.7 m² |

The sketch's were about 148 and 91 m²; the mizzen's tack is 0.5 aft of the sketch's, 1.2 aft of the main's clew (0.4 in
the sketch, at nearly one height). Before the sketch (25 October 2026) both feet were level, 3.76 and 3.69 m up, 149.4 and
80 m².

**The others:**

- **85**: the quarterdeck's cap continuous round the bulwark and the transom (above); the railing's spans are gone.
- **86**: the tiller's slot (above).
- **87**: the cabin wall's outer cell at z +3 closed (partitions in the default pieces; the build-place session cuts
  them to her side).
- **91** (the gate didn't line up and had gaps since its leaf was narrowed): the leaf the opening's full width again,
  15 clear at each end, its own cap 40 proud outside as the cap rail; it is hinged on its inner face at the after edge and
  its outer forward corner passes the forward jamb through a rebate in the jamb's planking and the cap's end; a stop
  batten 75 × 25 on each jamb outside hides the gaps. The leaf's cap drawn in the cap rail's wood and grain (the game
  view showed it as a dark band of tarred planking).
- **92**: the cap rail's texture follows its curve into the stem: u along its middle line in plan, v off it.
- **93, 94**: the carlings (above).
- Rib −14, risen through the main deck to the bulwark, reached 40 into the next deck cell inboard: in the deck's planking
  it stops at the cell's border, so that cell's planking stays a whole block (its faces were notched round the rib).
- **101**: the topsides paint zone in block rows (`"rows": true`, the paint session's).
- **104** ("ropes still cross the sails"): windward shrouds only, as lateen ships set them: each mast's and side's in a
  group `rig:shrouds-<mast>-<port|starboard>`, the mod showing the windward set; main 3 a side to the waist's cap, mizzen
  2 a side to the quarterdeck's. The halyards' falls to cleats on the masts; the main sheet to cleats on the quarterdeck's
  front rail (±0.95); the mizzen's tack to one cleat on its centre, clear of the stair.
- Also: `panelPlaces` false and the figurehead's hook on the stem from the pipeline (the build-place and fittings
  sessions); each mast's `radius` its heel's (the widest); no stern lantern; the lateen dip data as the lateen session
  asked.

**The quarterdeck stair against her side (the user's choice B, 26 October 2026).** The game refused to lay it: her
quarterdeck's bulwark leans in (tumblehome), and 0.5 to 1 above the quarterdeck its planking's inside and rib -8's head
came into the outer quarter-block of the stair's cell all along the landing at its head (x -8..-7), where the game wants
a walking channel 0.6 wide and 1.8 high. Over the flight and the landing (x -8..-6), from just over the quarterdeck to the
cap, her planking now stops 15 outboard of the cell (z 2.515; at most 80 off its 200 at the top), the ribs' heads there go
whole from the quarterdeck up on that side (shaved to the cell, rib -8's left a 50 sliver on the planking, as if broken
off), and the cap rail's inner edge, 5 outboard of the cell, overhangs the cut by 10: a shallow recess in the bulwark's
inside beside the stair's head, her outside unchanged (`views/game/27`, `28`). The galley's front wall has no top row
behind the stair (its last tread's top in that partition's plane). The other choice was the stair a cell inboard.

Checks (26 October 2026, the counter build with the stair's landing): `tools/caravel/overlap_check.py` 0 of 84,642 faces; `dotnet run --project
tests/HansaTests -c Release --` holdstair, modular, steerage, gunclick, partitions, decor, stairs, hull, gates and lateen ALL
OK; design caravel ALL OK (the stair laid); slots' 2 failures are carrack-m's, paint's note 68 the paint session's (as in
the fourth trial). The full offline run: 5 failures, none the caravel's but the stair (the carrack's dry hull at her stern,
two of its holed-panel checks, note 68, the archived charters). In the slot layout a cell's side is drawn as one quad where
a rib split it into a fan, and a cell's top and underside take their grain from their largest face (a sliver beside rib
-14 had skewed it); note 83's side looks count only a side met within 2 of the cell's middle (by the sternpost one looked
across the ship). `lateen-dump` isn't in this branch: the lateen checks find no sail within 0.02 of a mast on either tack
or through the dip; the main's clew and the mizzen's tack, each turned about its mast to any sheet within ±25° on either
tack, are never nearer than 1.13 m (through the dip the sail is brailed and the yards part); the main's sheet and the
mizzen's tack, which cross in a view from the quarterdeck, are 0.9 apart (their cleats at z −0.95 and 0). Game views:
`views/game/01-26`, the counter in 13, 25 and 26, the tiller in 10-12.

Server self-test (Release, 0.1.0-dev.9 with her new data, the stair's landing in): 235 ok, 2 FAIL, both the broadsides'
(20 of 60 shots into her hull; 0.6 t in her after 8 broadsides, 0.9 wanted), left to the fittings session's broadside fix.
The hearth check takes an open galley under a deck (warm by the fire, nothing on the waist); the stair is laid; the game
logs the oven refused (the build-place session's stacked places, not merged here) and the hatch `piece-taken`.

With the stair's landing (26 October 2026): the stock's hole through the counter 10 round it (it was 25, a ring seen from
below), and the bumpkin's socket its own taper 5 larger round it (the sky showed round the spar): `views/game/29`, `30`.

### The sixth trial (30 October 2026, dev.14 model notes 106–134)

This round changes the master, its export and its default layout. Runtime interaction changes are on the other
worker's branch; no `src/` changes are included here. The note cameras are kept in
`views/notes-106-134/shots.json`, in ship coordinates, with additional views of the joints from the opposite side.

- **106 — rudder:** 1.5 m constant chord below the waterline, with a straight after edge; only the short part above
  the waterline narrows to the stock. The upright post, stock, axis and level tiller stay in their places.
- **107, 128 — bumpkin:** 2.8 m beyond the transom (X −18.3); its heel is 100 mm into the first quarterdeck beam,
  at Z 6.6, in a stopped socket. Its fitted opening continues through the after edge of the quarterdeck planking.
  The mizzen clew and sheet follow its outer end. Both ends come from the build report, rather than duplicating a
  second position in the layout code.
- **108–110 — galley and cabin front (108/109 deferred, 110 fixed):** the starboard cabin doorway at z +1 has its
  windowed oak door, opening into the cabin, and a window outboard at z +2. Doors, windows, wall rows and anchors
  are layout data. The galley stays in the user's ship #227 arrangement. A focused placement check found why the
  two remaining wall rows cannot be supplied safely by data alone: the hearth occupies its full cell, so a bottom
  partition at (−9, 1.125, −1) is refused with the default anchor; `anchor: "-x"` lays it but leaves two coplanar
  hearth/wall faces. The runtime must clip contact faces between the firebed/kerb and its neighbouring wall.
  The upper outer wedge at (−7, 3.125, +3) has no border mount: the narrowing hull leaves too little free area.
  The adjoining top wall at z +2 would also intersect the last stair tread (0.0377 m²). Runtime needs clipped
  narrow border mounts and stair/wall contact clipping. Those unsafe entries are omitted until that work lands;
  moving the galley or stair to conceal these limitations would change the agreed layout.
  Galley wall anchors put their collision on the cabin side, leaving the table and vessel cells clear. The server
  also reports the oven before its supporting start table is laid, and the hatch as occupied; runtime should retry
  supported modular pieces after laying the start blocks, and resolve the hatch/stair occupancy explicitly.
- **111 — stern ends:** the slicer cuts each solid separately. Intersecting a compound of touching timbers could
  omit a member of a cell, despite the original solid containing it. Every cut piece is checked against its actual
  3D cell with its precise bounding box, and the sliced volumes must add up to each body's volume.
- **112, 114 — beams (partly fixed):** the default hatch's fixed carlings run between whole beams; the revised
  slicer preserves the beam lengths and their cut ends. Picking and replacing a beam length in game remains the runtime worker's part.
  Automatic carlings around player-made openings are not exported: `owner filled AND facing empty` cannot express
  the union of adjoining frame openings, and exporting one set per removed length produces duplicate faces when
  two neighbouring frames are opened. The runtime needs to build one shared frame around each connected opening,
  extending to the nearest remaining whole beams and omitting a divider between two adjacent removed lengths.
  The design supplies the actual beam centres and a 0.2 × 0.4 m section. A single opening and two neighbouring
  frame openings must both pass the overlap check before this is enabled.
- **116, 121, 122, 127 — entry ports:** both gates and their rope ladders move to X −2.5, abreast the main mast.
  Cell −1, their former place, now has a carriage gun port and lid on each side. The old jamb battens move with the
  gates. The leaves have two visible strap hinges with separate fixed and moving knuckles. Their inner planking
  belongs to `waist-bulwarks`, their caps to `rail-caps`, and their outer planking to `topsides`; the hardware is unpainted.
  Hinge barrels sit 20 mm inboard of the leaf's inner face, with 15 mm radius knuckles and seated straps. Fixed
  tabs join the two outer knuckles separately to the curved jamb, leaving the rotating middle knuckle free. Both
  leaves pass the complete swing check, including the ironwork. The old note 122 ray now sees the inner edge
  of the rib beside the replacement gun port; that rib remains part of the structure.
- **119, 120 — rope attachments:** the bow tack ends on an 18 cm forged eye with a shank and seating flange.
  The shrouds end on paired deadeyes with strops and reeved lanyards, above channels outside the hull. Bolted
  chainplates and knees seat the channels against the planking. Ratlines join the shrouds, in the same windward
  rig groups. The mizzen's channel is between the restored gun ports, clear of their openings.
- **124 — transom:** the outer-face test includes the actual stern profile, so its outside cannot be classified as
  inside planking. The transom's inside and the steerage's side insides use `steerage-walls`, a row paint zone,
  separate from the open bulwarks. The main-deck edge closes its bottom.
- **125, 126 — copper:** actual triangles are clipped at one horizontal line, y +0.125 (model Z 3.0), instead of
  assigning a whole triangle by its centre. As on Magelan (read only), the metal uses all five vanilla copper-sheet
  textures, varied by sheet. Its UVs follow distance along each strake and round each section, including across the
  flat floor; faces are cut into atlas tiles, never squeezed into one.
  The overlap check also checks the copper alternative, whose tile cuts differ from the tarred bottom's. It takes
  a face's plane from its largest triangle: a tile edge can have three leading collinear corners, whose rounding
  error formerly made an oblique forefoot face appear to lie in the stem's plane. Regression checks cover
  that false positive and a real overlap of the same oblique face.
- **129–131 — quarterdeck front:** rib ends finish under the quarterdeck planking, which covers their end grain.
  Note 130 points at an upright of the front guardrail, rather than an extra rib or fitting; it remains a guard for
  the deck edge. At the starboard stair the outer edge of its gap is used to decide whether another short railing
  is needed: the stray outboard post in note 131 is gone.
- **134 — stern cap:** the transom cap follows the hull's plan at both corners. The old rectangular piece projected
  beyond the narrowing side caps there. Its joint is fused with the side caps.
- **133 — guns:** four carriage guns on the quarterdeck, two per side, in clear cells −13 and −11 (X −12.5 and
  −10.5), with 750 mm ports through the solid bulwark and plank lids. No port cuts a rib. The waist now has six guns.
  Its original gun IDs stay on cells 1 and 3; the new after pair is `gun-<side>-3`, the quarterdeck's
  `gun-<side>-quarterdeck-1/2`. Two bow sockets at X +8 use the existing runtime swivel fixture format, with ±100°
  train, −25..+40° elevation and measured cap widths in `rail`. Enlarging the gun and its runtime motion belongs to
  the other worker. The current unmodified swivel block shape has three coplanar overlaps that that worker must
  remove: barrel/reinforce at its after end, barrel/muzzle at its muzzle end, and socket/collar at their top.
  At the bow the cap top is y 2.65, with sockets at z ±1.64. For a one-metre muzzle reach at −25°, its trunnion
  should be at least 0.48 m above that datum (including 50 mm clearance). The swept barrel remains well outside
  the stem; no standing rigging is near these sockets. The runtime's current short gun and rail clamp limit the
  elevation and its picking test fails from the new bow position; the enlarged gun must use these socket data.

Verification after the final export (30 October 2026):

- The master build's ten carriage guns fit at their limits; the layout checks all ten lids, housed muzzles and gun
  clearances. The per-solid slice preserves each body's volume (669 cells, 1,281 pieces). There are 136 removable
  hull panels and 31 removable main-deck beam lengths; lengths containing other structure remain fixed.
- `dotnet build -c Release` and `dotnet build tests/HansaTests -c Release` pass, zero warnings and errors. The
  worktree builds used temporary properties for the game install and the existing Sea assembly/source references;
  neither Sea nor the game install was written. Construction checks: **99/99**. The export-data checker
  `tools/caravel/check_notes_106_134.py`: **0 failures**, with 108/109 explicitly deferred. Overlap checker
  regression tests: **2/2**.
- `tools/caravel/overlap_check.py`: **0** overlaps in **99,079** drawn faces; `--clad`: **0** in **104,329**. Three
  removal states (a beam length and its covering deck cell removed, and two neighbouring frame openings)
  also report **0**: 99,080 / 99,078 / 99,080 faces. The fixed hatch carlings finish on the whole beams.
- `caravel-all`: **20 failures** in 47.3 s. Design caravel, hull, stairs, partitions, modular pieces and decor pass;
  all 27 modular entries tested find their places, including the oven after its start table. The failures are
  six gate-picking assertions using the former gate area; nine swivel assertions (old four-gun layout/limits and
  the current runtime rail clamp/picking); one old assertion that every shroud-group face lies on a shroud (there
  are now ratlines); one gun-click check (the two bow swivels); one paint ray through the relocated gate; and the
  two known carrack-m slot failures (49 cell problems, 16 missing top/underside faces). Both gate swing checks pass.
  A focused runtime probe from abreast the new gates picks each leaf correctly both shut and open.
- Full offline run: **22 failures**: the same 18 caravel assertions above, plus the four baseline failures: the
  carrack's stern water cut, hull-hole edges, note 32's great-cabin panel and archived charter inventory.
- Dedicated server self-test: **4 failures**: two gate-picking checks from the former deck position, sheathed
  top-speed synchronization, and the swivel elevation/rail-clamp expectation. It finishes and exits; port 42597
  is free. This run used the final ship definition (its SHA-256 stayed identical after the last visual-only
  fixed-hinge-tab correction). The current unmodified swivel block's three coplanar pairs remain the runtime
  worker's responsibility, as listed above.
- **42 game-mesh closeups rendered and inspected**, including the original note cameras, opposite corners,
  open hinges, run-out quarterdeck guns, copper forefoot joints and removed-beam states. All picture paths
  are listed in [the render index](views/notes-106-134/README.md); cameras and poses are in its `shots.json`.

Round status: **fixed** 106, 107, 110, 111, 116, 119, 120, 121, 124, 125, 126, 127, 128, 129, 131 and 134;
**partly fixed** 112/114 (player-made opening frames need runtime), 122 (hinges fixed; the targeted rib edge retained),
130 (identified as the guardrail upright, retained), and 133 (carriage guns and bow socket data complete; enlarged
swivels and their runtime motion/picking are the other worker's); **not done** 108/109 (runtime geometry/mount limits
described above). No `src/` or runtime block-shape changes are included here.

### Server self-test on the caravel (20 October 2026)

`tools/run_selftest.sh` on the Release build tests the caravel (the configured `carrack` isn't in it). The test is
now ship-agnostic in `src/SelfTest.cs`:

- Scaled to her: the flood load is `min(150 t, 40 % of her reserve buoyancy)`; running into the wall must crack the
  bow of a ship of 600 t or more and scrape a lighter one's; the water-level spots and the bow's distance to the wall
  follow her length and beam; the flooded panel is the one nearest 2 blocks under the waterline amidships.
- The shallow-water grounding: the pool is 4.9 deep and she draws 2.75 with 1.13 freeboard, so she would go under
  before touching. A temporary stone shoal is laid under her (whole blocks, till less than 0.6 of her freeboard is
  left under her keel; 2 blocks for her) and taken away when she has floated off.
- Skipped with a logged note when the ship lacks the feature: gallows boats, the lower deck's chest and cabinet,
  the hearth. (Guns were skipped too until she had them, 20 October 2026.)
- Gunnery, laying and broadsides run on her 8 guns (20 October 2026): the test takes the lowest deck's guns first
  (her waist's; a quarterdeck gun laid down at the chicken would put its ball into the water short of the pool's
  wall), and checks the gun captain's place on the gun's own deck and behind its carriage (1.5 behind the trunnions
  for a carrack as before; 1.05 for her lighter gun).
- Also scaled to her then, each leaving a carrack's numbers as they were: the water in her is read after as many
  broadsides as fire a carrack's three (15 shots: 4 of hers) and wants 0.3 % of a light ship's displacement (0.7 t;
  a carrack 3 t); her wreck's blocks are sampled every 10th and 25th (a wreck of 1,000 blocks or more: every 20th and
  50th); where the wreck lies is measured from her hull's middle (2.7 aft of her origin; a carrack's 1.6 forward is
  close enough to measure from hers, as before); the wreck block broken for its timber is the one with the most.
  Runs on 20 October 2026: ALL OK twice in a row, 182 checks. The water check's margin is small (0.9–2.3 t after
  4 broadsides in six runs, 0.7 wanted): her first holes come with the third or fourth broadside. The sweeps test uses the ship under test when carrack-m is absent.

## Build speed (27 October 2026)

The user asked for a faster CAD build. Profiled statement by statement (`tools/caravel/profile_build.py`, which runs a
script one top-level statement at a time and writes each one's seconds), the build took 54 minutes with the hull taken
from its cache, 34 of them in five cuts of the whole shell (the backbone; the stock and the bumpkin; the stair's landing;
the gates' rebates; a merge after the gates' openings and the waist's ports). Each cut took 5-8 seconds; the rest was
FreeCAD's face merge after it (`removeSplitter`), some 300 seconds a time, which on her faceted shell gave back the very
shape it was given (its BREP text the same). OpenCascade's own merge (`ShapeUpgrade_UnifySameDomain`, 0.8 s) would merge
306 faces: a change, so not used. The export took 12 minutes, 590 seconds of them meshing the cells' pieces and testing
points inside them.

- **The shell cut once.** Every tool that cuts her shell is gathered (`SHELL_TOOLS`), fused into one solid with its faces
  merged (cheap on the tools: a gate's opening and its rebate meet in one plane and, cut apart, left the jamb's face in
  two), and the shell cut by it in one boolean with no face merge after. The lofted envelope and inner solid keep theirs
  (the envelope's merges some 200 faces); they only run on new lines, and their results are kept with the lines' hash.
- **Stages kept between builds** (`memo`, `memo_value` in `build_caravel.py`): an expensive step's result, keyed by its
  name and its inputs' exact BREP text (numbers and strings as they are), is written to `%TEMP%/caravel-cache/stages` and
  read back on every run, the first included, so a fresh and a cached build go on from the same shape. The shell's cut,
  the beams, the cap rail's lofts, the galley's placeholders, the guns' clearance pieces, the report's overlap checks
  (each now one boolean against a compound of the parts, which don't overlap each other: the same sums), the solids'
  validity, and every half-breadth the build asks of the envelope (a section each) are kept. A part intersected with the
  inner solid is intersected with the inner solid's piece round it (the galley: 268 s to 6 s; the beams: 10 s each).
- **The export's cells kept** (`export_caravel.py`): each piece's mesh and the inside tests on it, keyed by the piece's
  BREP text, in `%TEMP%/caravel-cache/export-pieces.pkl`. Two things that depended on whether a piece had been meshed are
  now the same either way: the quick box test before an inside test uses the piece's exact bounding box, 2 mm larger (a
  meshed shape's BoundBox comes from its mesh, a little inside its curved faces), and the grain and deck tests read the
  piece's box as it is once meshed, kept with its mesh.
- **The layout** spreads faces in one plane in a fixed order (which of two equal faces moved 2 mm changed with Python's
  string hashing), and the swivels' `rail` widths come from the cap's section and its drawn faces.

| | Before | After |
|---|---|---|
| Build, new lines | about 64 min | 23 min |
| Build, the hull's lines unchanged | 54 min | 2-4 min |
| Export | 12 min | 12 min fresh, about 2 min with its cache |
| A sail's change (lines, build, export) | about 66 min | 4 min |
| A fitting's change (build, export) | about 66 min | 4 min |
| The caravel's offline checks | 13 runs, 50 s | one pass, 44 s |

Checked against prototype/grid-bays' a953ca2 (`tools/caravel/model_fingerprint.py` on both models, then
`tools/caravel/compare_builds.py`): every body's volume, area, face and edge counts and bounding box the same but for the
shell's edges (28,500 for 28,502: the two edges at each gate jamb's top, where the jamb's face meets the cap, now one);
`cells.json` and the build's report the same. The design: the swivels' `rail` inboard widths 0.01 less than the hand-set
ones (from the cap's section), and 16 numbers in 4 of the stern's hull panels' corners (a corner level with other points
along a top edge is the first of them, and the order of the shell's faces changed). The mesh: 891 of its 96,443 quads,
order aside: the gates' and the stern panels' faces, the few the corrected box test draws otherwise, and the 2 mm
spreading of faces on the guns, the anchor and the ropes, which a953ca2's layout picked at random. A fresh and a cached
export are byte for byte the same, and after a sail's and a fitting's change and back, the design, the mesh and the
report came back byte for byte.

## Codex review, 17 October 2026

Fixed: a 60 mm slot through the stem (the inner skin now comes from erosion and the report checks the centreline
at the stem and post), carriages cutting 48–104 mm into the skin at cells 3 and 5, the rudder not reaching the
tiller, the hatch cut into the master deck, no `SliceRole` on the bodies, and a wrong L/B. Confirmed correct: deck
levels, waterline, mast and rib cells, quarterdeck edge, beam, transom plane shift (258.85 mm), port/starboard
mirroring.

Not done yet:
- **Skeleton:** backbone, ribs, beams and decks are in. No keelson, clamps or knees. The carrack's plan builds those after the plain hull; the
  quarterdeck edge has its rib only as a reference outline.
- **Smooth and editable master:** the solids are polygon lofts, and the spline waterlines in the file do not drive
  them. The hull is edited in `caravel_lines.py` and rebuilt.
- **Rig size:** sail areas are 160 m² (latina) and 319 m² (redonda) on 1.125 m freeboard. The redonda rig may be too
  big for the hull; check balance and stability before using it.

## Open points for the user

1. Bulwark 1250 (as the carrack) or 1000 on a cell border — the carrack's open question 1.
2. Guns: since 25 October 2026 the waist's 4 (cells 1 and 3) and 4 swivel places on the quarterdeck's cap; more or
   fewer is open.
3. A forecastle for the *redonda*? Historical *redondas* often had none; the hull stays common either way.
4. Mast rake (lateen masts raked forward) is left out to keep each mast in one cell.
5. Stairs, the cabin's front and the galley are default pieces (since 24 and 25 October 2026); the stairs' places and
   some of the inner layout may change on the user's word.


## Integrated dev.14 follow-up (31 October 2026)

On `codex/notes-106-integrate`, after the model/runtime halves were combined as dev.15:

- Runtime review 1-7 fixed: server-authoritative eased trim/dip snapshots; one shared rig pose calculation for drawing and placement; designated deck penetration for flues; charged gun removal refused with a message and charges preserved in wreck loot; cached preview volumes, inverses and gun picking; real stair timber checks; a fresh run-in timer after firing.
- 108/109 complete on new ships. Hearth/stair contacts are fitted in the modular mesh; wall faces are also clipped against the real native table/oven elements. The upper starboard cabin wedge has data-authored `borderPlaces` with exact clipping rectangles, rather than a caravel-specific compiler branch. Explicit border anchors can use their support side while both neighbouring deck cells are laid. Stacked floor lookup permits the oven to stand on its start table; default piece initialization captures the new-ship state before fitting initialization writes it.
- 112/114 complete at the marked beam slots already supplied by the model half: `beam:main:-8:-2` and `beam:main:-4:-1`. Generic data-authored `beamFrames` draw and collide with carlings around the union of removed lengths, without a divider between adjacent holes. They seat on intact stations and are trimmed around the default hatch's carlings and both fixed headledges. The exact 112 removal originally exposed four coplanar underside pairs at those headledges; the final contact mesh removes them. Replacement still observes ordinary piece and landing clearance.
- 117 complete. The existing hook codes move to `[-2.5,3.685,-0.7]` and `[-10.5,6.685,-0.7]`, with their iron brackets reaching to port. Both native lanterns clear the merged rig on either tack; saved lanterns migrate by hook identity. Model ratlines remain with the standing shroud set and stow when that lee set is cast off.
- 118 complete. The low proposal is rejected. The native bear sits high on `[10.12,2.6,0]`, raked 20 degrees with normal `[.9397,-.342,0]`, facing forward on the centreline. An exported timber saddle fits its neck/back to the stem under its head; the backing stops below the bear's projecting head. Actual native elements have no penetration there. Side, ahead and underside views were inspected.
- 122/130 reviewed from the exact note cameras and retained: respectively the structural rib under the cap and the front guardrail's main upright. Neither looks like an orphan part or has an overlapping face. The earlier hinge and stray-post corrections remain.
- 133 complete. Bow swivels use measured rail widths `[0.88,0.61]` and the larger merged runtime gun, +/-100 train and -25..40 elevation with rail clearance. Actual barrel/tail faces clear the stem, fittings and posed rig over 294 lays per gun on both tacks. The three coplanar inventory pairs were already fixed in the runtime merge and recheck at zero. Four quarterdeck carriage guns remain; both second gun-captain places move 0.65 aft to clear the mizzen when run in.
- 113 retains a real layout constraint: floor/underside snap is fixed, but the aft-rising short flight at `f:hold:-4:-1` still requires an approach through the mainmast cell. That orientation remains refused. Mast and landing clearance were not disabled.

Final verification: Release mod/tests builds pass (4/3 existing warnings); ConstructionTests 99/99; exported-data checks 0 failures; overlap checker regression tests 2/2. `caravel-all` takes 47.5 s and has only the two known carrack-m slot failures (49 cell problems, 16 missing top/underside faces). Full offline tests have only the four known failures: carrack stern water cut, hull-hole edges, note 32 panel and archived charters. Dedicated self-test on the final package: `SELFTEST ALL OK` at 10:17:29; server exited, port 42597 free. The head-to-wind 5.6 -> 0.5 b/s case, loaded/clad speed synchronization and quarterdeck-2 crew place all pass.

Coplanar checks: wood 99,119 faces / 0 pairs; copper 104,369 / 0; all 24 runtime stair variants and three stair inventory shapes / 0; swivel's 132 faces / 0. The combined ship and runtime cabin have 0 new joint overlaps, both as a new ship and in five removed-beam states, including the two exact notes. Unchanged vanilla assets' internal contact faces are counted separately from new ship joints. Thirty final game-mesh PNGs were inspected personally. Paths and cameras are recorded in `tmp/integrate-views/README.md` and `shots.json` beside each picture group; full per-item results and logs are in `tmp/integrate-notes-report.md`.

Changes are left uncommitted at the user's request. Sea, the installed game, user Mods and saves were read only; the dedicated server ran under this worktree using the existing Sea 0.4.6 package.

## Dev.16 chalk notes 135–150 and creatures aboard (31 October 2026)

Work in `codex/notes-135`; no commit or push. The complete records in `tmp/hansa-notes.json` supply the cameras and
targets. The master was rebuilt and resliced for the hold stair and hatch; hull lines remain the same.

- **135:** an upper chest side and its native 7/8-height lid offer the supported cell above it. The first placement
  candidate uses the supporting piece's grid cell rather than rounding the visual lid down.
- **136, 138:** both shroud sets and ratlines remain attached with sails furled. Under sail the lee set still casts
  off during the dip. Halyards and their falls, sheets, tacks, the mizzen bumpkin's sheet, and the two shroud sets
  remain in the rig data and drawn mesh; the lateen rig has no square-yard braces/lifts to invent.
- **137, 144:** timber UVs use an orthonormal physical surface basis. Drawn partitions stop inside their actual cells
  and at the underside of overhead deck planking; fitting them to a rib cannot extend them over a neighboring wall
  or through a deck. Coplanar contacts between sliced panels are clipped in their own plane, with conditional reveal
  faces retained for the neighbor's removal. No overlap threshold was relaxed.
- **139, 150:** both mast hooks specify bow-facing yaw, including the omni-attached native lantern; start lamps use it.
- **140:** no swivel fixtures or places on this caravel. Swivel item, renderer and mechanics remain for later use.
- **141:** no outside channels, chainplates or deadeyes. Shrouds finish on small forged eyes set inboard on the cap
  rail. The ring, embedded shank, rope end and first ratline meet without coplanar duplicate faces.
- **142:** an open partition door is aimed at by transforming the ray back through its actual leaf rotation. Server
  visibility uses the swung leaf too. The hinge pin is on the opening face, so its thickness clears the adjoining wall.
- **143:** the starboard cabin-front window is replaced by an ordinary partition in the default pieces.
- **145:** the marked `beam:main:-4:-1` remains a removable length; its deck covering must be taken up first. It uses
  a native support beam and retains the existing opening-frame/support rules.
- **146:** the apparent stub below the cabin front was a partition fitted down through the deck. Partitions cannot
  draw below their cell's floor now; the exact hold camera was checked again after this correction.
- **147:** the opening is two cells, x `[-6,-4]`, z `[-0.5,0.5]`. The short hold flight descends forward from x -6
  toward -5. Cell `[-4,-3]` remains clear by the mainmast. A quarter-block coaming surrounds the opening; two plank
  leaves hinge along its port/starboard edges. Coaming collision persists with the leaves open and the saved piece
  keeps its coaming/double-leaf fields. The only edit in `ShipFlooding.cs` passes the coaming-height callback to
  `ShipFlood.Downflooding`; no dry-cut or waterClip changes. This callback must be retained when combining issue 30.
- **148, partly implemented:** every master shell candidate is a hull slot (462), including bottom, counter and
  transom; 197 sliced rib cells are removable structural slots. Thin visible shell faces can be selected independently
  of their shared collision voxel. Taking underwater planking out is permitted and the HUD states the flooding risk.
  Hull replacement/return is four `game:plank-*` boards; rib and beam replacement/return is one `game:supportbeam-*`.
  The repair tool uses the same material count. A sufficiently large, upright panel above the main deck can be
  replaced by a native glass pane; its fitted casing/cames/glass, sealed state and pane material survive saving.
  Material returns currently use the blueprint's oak; the replacement wood variant is not individually saved.
  **Still outside this implementation:** mast replacement with debarked timber, and individual rib damage states/
  a complete deck-collapse model. Masts remain the existing spars, not construction slots. Rib removal is allowed
  around occupied pieces only after clearing those pieces; beam/deck support restrictions remain in force.
- **149:** pumping water is a connected, steady tube following the outflow trajectory, drawn with water texture;
  only its landing has splash particles. There is no random drip sequence or stroke gap. Dry pumps draw no stream.
- **Elk:** native controlled-physics displacement is swept against the ship with the creature's full collision box,
  preserving native AI/gravity and allowing it to fall through a real opening. A held creature follows the ship's
  translation, yaw and tilt; ship-local state is saved and used for client positioning. The data-authored hitch post
  at `[-5.5, mainDeck, -1.75]` accepts a native rope. Cloth endpoints save a local offset and transform it with the ship,
  without applying rope tension to the ship's motion. Empty-hand interaction takes the lead back; native rope detach
  also releases the ship end. Native ownership checks apply when taking a creature's lead.

The headless server leaves creatures inactive without a player, so the focused self-test activates its spawned elk
and drives its real controlled-physics behavior. It checks a grounded elk on the actual caravel and a native cloth
endpoint at the post, rather than relying solely on the pure collision test. No client was launched.

Final logs, per-note status and render manifest are recorded in `tmp/notes135-report.md` and
`tmp/notes135-views/README.md`. Renders use `game_view.page`'s exported mesh, actual runtime modular quads and native
piece shapes, with the recorded eye/look; a CPU rasterizer supplies the PNGs because Chrome cannot load three.js in
this environment. Unchanged native assets' internal contacts are counted separately from new ship/runtime joints.

Verification: Release mod/tests builds pass (4/3 existing warnings), ConstructionTests 99/99, contact/UV and overlap
checker regressions 2/2 each. `caravel-all` has only the two known carrack-m slot failures; the full suite has only
the four known historical failures (stern water cut, hull-hole edges, note 32 panel, archived charters). The focused
notes group passes, including the exact note 145 ray selecting the beam and note 142 ray selecting the open leaf.
The final dedicated run reports `SELFTEST ALL OK` at 16:27:55 and exits; its elk lands at local y=1.125 grounded,
and its native lead follows and detaches from the post. Wood: 118,728 faces / 0 overlap pairs; copper: 120,027 / 0.
Combined runtime joints are zero in default, open hatch/door, pumping, window, removed beam/panel and removed rib
states. Thirty-nine PNG paths/cameras are in the render manifest. No client or user world was opened.
