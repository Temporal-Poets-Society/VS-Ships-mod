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

The first build of a set of lines takes about 12 minutes, mostly the shell boolean. The envelope, inner solid and
shell are then cached under `%TEMP%/caravel-cache/<hash of the lines>`, so rebuilding fittings, rig or ribs takes
a few minutes (`CARAVEL_NOCACHE=1` forces a full build).

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

| | Value |
|---|---|
| Length overall, hull (transom top to stem head) | 23.6 m |
| Length on the main deck | 21.3 m (transom −11.6, stem +9.7) |
| Waterline length | 20.5 m |
| Beam | 6.60 m (half 3300 at Z 3500; the user: 6.6 blocks) — inside 7 cells, Y ±3500 |
| Draft | 2.75 m (the keel is flush with the planking) |
| Displacement | ≈ 216 m³, ≈ 222 t in seawater, block coefficient ≈ 0.56 |

For comparison the carrack is 36.4 × 12.16 m with a 4.875 m draft: the caravel is about 0.65 her length and half
her beam. L/B is 3.58 overall (23.58 / 6.59) and 3.13 on the waterline (20.47 / 6.53).

### Cells along X (named by their aft face, in blocks)

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
2. Gun cells −7, −3, −1, 3 (one under the quarterdeck), pending the carrack session's "one bay apart" rule.
3. A forecastle for the *redonda*? Historical *redondas* often had none; the hull stays common either way.
4. Mast rake (lateen masts raked forward) is left out to keep each mast in one cell.
5. Stairs to the quarterdeck, the tolda's front bulkhead and cabin are not modelled yet.
