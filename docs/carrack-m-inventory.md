# Carrack-m inventory and the grid carrack's gaps

Inspection made on 16 October 2026 after the user tested `hansa-carrack` in a client. The user found
that half of carrack-m was missing, things floated, decks had holes at the stern, partitions were
thick, and the bow and stern were badly shaped.

Sources:

- `tools/shipgen/carrack.py`: the generator that builds carrack-m. It is 11,343 lines and also in
  Hansa's baseline. Line numbers below refer to it.
- `assets/hansa/config/ships/carrack-m.json`: carrack-m's definition.
- `assets/hansa/config/shipmeshes/carrack-m.json`: carrack-m's mesh.
- `src/Ship/GridShips.cs`: how the grid carrack is made from them.

## Why so much is missing

`GridShips.Build` takes carrack-m's rig and mechanics. It throws away everything it treats as
carrack-m's hull, and puts the construction core's blocks in its place. Four rules did most of the
damage:

1. **The whole `interior` mesh group was dropped.** That is 13,426 quads:
   - ceiling lining and the ribs between layers;
   - bulwark frames, knees and riders;
   - stern timbers, side stiles;
   - the companion and hold-stair framing;
   - the cable tier.
2. **Every `exterior` quad with a planking or debarked-oak texture below her top rail + 0.25 was
   dropped** (game y 9.375). Iron, bronze, rope and pine parts of the same objects stayed, so they
   now float. Examples:
   - the wheel's posts went, the wheel stayed;
   - the binnacle cupboard went, the compass stayed;
   - the channels and deadeyes went, the chain plates stayed;
   - the cathead beams went, their hooks stayed;
   - the boat gallows went, the boats stayed.
3. **The `glass` group was dropped,** along with lead and black textures in `exterior`: the stern
   gallery's and side windows' panes and cames, the hawse discs, and the scuppers.
4. **All 125 of carrack-m's layout pieces were dropped** and replaced by 5 ladders, 3 doors and the
   galley. The layout pieces are 81 partitions, 27 railings, 5 stairs, 4 doors, 2 windows,
   2 panels, 2 hatches, a hearth and an oven.

On top of that:

- Her hull lines are my own coarse stations, not carrack-m's lines, so her bow and stern are wrong.
- Carrack-m's fittings, rudder and rig stay where carrack-m's hull had them. Her hull is a
  different shape, so they float or end up buried in her.

**Carrack-m is already on a block grid.** It is generator variant 3 (`GRID=True`).

- Deck tops are at game y −3.875, −0.875, 2.125, 5.125 and 8.125, all on block boundaries.
- Every deck cell is a 1×1 slot.
- Every hull panel is a one-block half frame space.
- The grid origin is `[0, 0.125, 0.5]`: block borders at whole x and at z = ±0.5, ±1.5, …

What it doesn't have on the grid is its hull and castle surfaces. They are smooth lines that cross
cells, so changing one block isn't local.

## Feature by feature

Status in `hansa-carrack` (built from commit 9c25f35):

- **missing**: not there at all.
- **floating**: some or all of it hangs where carrack-m's hull was.
- **wrong**: there, but not as carrack-m has it.
- **ok**: as carrack-m has it.

### Hull and planking

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Midship section "hull R": flat floor 3.6, widest 6.08 just over the waterline, tumblehome to 3.3 at the poop | `R_POINTS` 144, `SECTION` 184 | wrong: approximated by my stations |
| Bow: elliptical run, forefoot arc r 2, stem raked to x 22.3 | `BOW_X` 196-211, `hull_hw` 810 | wrong: coarse stations, a flat stem cap |
| Stern: raked transom (~15°), sternpost ~17°, rudder axis | `STERN_X` 214-217, `stern_x` 893 | wrong: flat cap at x −17.5 |
| Poop overhang: 1.25 aft over the transom from y 6.25, its underside and timber knees | `poop_hw` 847, `overhang_underside` 4730, `overhang_brackets` 4754 | missing |
| Forecastle overhang: sides to the tip at 26.5, raked underside from 4.75 at the stem | `fc_tri` 832, `fc_under` 840, `raked_underside` 4700 | missing (her bow stops at 21) |
| Castle breaks swept as quarter-ellipses (quarterdeck −2..−0.25, forecastle 12.25..14, poop −8.75..−6) | `rail_y` 925, `_sweep` 918, `sweep_patch` 4422 | wrong: square steps |
| Strakes ~0.3 high between banded seams, cut at every frame and half frame | `seamed_levels` 970, `frame_strips` 990 | wrong: one plain face per cell |
| Wales: two raised tarred strakes at 2.75-3.25, 0.1 proud | `HULL_WALES`, 4473 | missing |
| Castle mouldings: raised strips on the sterncastle, poop and forecastle | 4482 | missing |
| Frieze band under each rail cap | `paint_zones.outside_zone` | missing |
| Rail caps: waist, quarterdeck, forecastle, poop; sweep caps on the breaks | 4487-4512 | missing (no separate cap) |
| Tarred outside, oak inside, caulked decks, copper bottom (`bottomclad`) | `build` 3484 | wrong: plain oak everywhere, no tar, no copper |
| Inside: ceiling lining 0.25 in from the skin, ribs between the layers, steps at x −9, −2, 14 | `lined_skins` 4237, `LINED_X` 1080 | missing |
| Ribs (frames) tapering as they rise; bulwark frames inside the bulwarks | `grid_framing` 7347-7414 | wrong: constant section, no taper (user) |
| Reveals: window, entry port, gun port and stern port linings | `side_reveals` 4559 | missing |
| Backbone: tarred keel, heel arc, sternpost along the rudder axis, stem | `keel_and_stem` 7097 | wrong: keel flush in the bottom, no sternpost or stem timber |
| Apron, knighthead, inner sternpost | `centreline_timbers` 7632 | missing |
| Mast steps on the keelson | 3621 | missing |

### Stern

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Stern gallery: great-cabin row of 5 lights and poop row of 4. Each light has jambs, mullion, two transoms, 6 panes (stained top), lead cames, pilasters with capitals, cornice and rail, sill rail. Show balustrade under the great-cabin row. | `stern_gallery` 8137, `GALLERY` 1378 | missing (2 plain window cells instead) |
| Stern timbers between the lights inside | `stern_timbers` 8206 | missing |
| Taffrail cap and corner blocks | `taffrail` 4630 | missing |
| Name board: tarred board, frame rails, end posts, scroll discs. The fixture's text sits on it. | `nameboard` 8417 | missing board (the text-only fixture moved onto her flat stern) |
| Stern lantern bracket: timber post, cap, cheeks, iron crane arm and brace | `fixtures()` 11134 | floating (only the iron is left) |
| Stern chaser ports with lids under the taffrail | `STERN_PORTS` 408, `stern_port_lid` 9060 | missing |

### Bow

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Stem head post and saddle for the bowsprit | 4899-4907 | missing |
| Catheads: beams, end blocks, iron hooks, knees | `anchor_gear_grid` 5775 | floating (hooks only) |
| Hawse holes: bolsters, black discs, inner pipes and hawse pieces | `anchor_gear` 5615 | missing |
| Riding bitts: two posts, iron caps, crosspiece | 5730 | floating (iron caps only) |
| Port anchor catted under its cathead (`part:anchor`) with cable; starboard anchor fished on a bill-board; cables round the bitts, aft to the capstan and down the deck pipe | 5571-5788 | floating (at carrack-m's bow, x 20.75-22.25) |
| Bowsprit with heel chock and cheeks, bitts, partners, iron bands, gammoning | 1476-1499, 4897-4947 | floating (pine and iron left, chocks gone) |
| Heads: two seats with boxes, lids, hole discs, hand rope; seat fixtures | `heads` 8088 | missing (seats dropped) |

### Decks and the way between them

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Decks at game y −3.875, −0.875, 2.125, 5.125 (quarterdeck, forecastle), 8.125 (poop) | `deck_spans` 3553 | ok in height |
| Deck edge strips closing each deck to the ceiling | `edge_strips` 6724 | wrong: **holes at the stern on every deck** (fixed now, see below) |
| Beams on every even x, 0.5 deep (0.75 in the hold band), sloped ends, beam segments under wells, carlings and headers round openings | `main_beams` 1181, `beam_segments` 2264, `hatches()` 6217 | wrong: beams per rib cell, no segments or carlings |
| Hatchways x 6..8, z ±1.5 on the main and lower decks, trapdoor hatches (glazed in the waist) | `OPENING` 757, 1969 | ok |
| **Companionway:** well x −3..0 on the centreline in the main deck; stair down to the lower deck; partitions enclosing it; door at x 0; two hood panels | `COMPANION_WAIST` 729, 1880-1925 | missing |
| **Hold stair:** a real stair from the hold floor, its well at x −6..−3 in the lower deck, with rails round it | 1939-1947, 6375 | wrong: a ladder |
| **Stairs to the castles:** quarterdeck, poop and forecastle, `magelan:shipstair-long/-steep` pieces with handrails | 1922-1929 | wrong: ladders through a well, with a scuttle |
| **Railings:** breastworks along the quarterdeck (−2), poop (−9) and forecastle (14) fronts, skipping the stair heads; rails round the hold-stair well | 1939-1960 | missing |

### Steering

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Rudder: tarred blade, pintle straps, gudgeons, head; moving part `rudder` on the raked axis | `rudder_and_tiller` 4778 | **missing in view**: its axis at x −12.3 is inside her hull |
| Tiller with iron bands, moving part; its sweep kept clear of deck slots | 4778, 2083 | inside her hull, not in the room carrack-m gave it |
| Wheel: two tapered timber posts, base board, axle, rope drum, rim with 8 spokes and handles | `helm` 5306 | floating (rim and spokes left; posts and base gone) |
| Wheel ropes through the quarterdeck, along the great cabin walls, down to the tiller | 5306-5409 | partly floating (follow parts left) |
| Binnacle: oak cupboard with a glazed face, compass with gold bowl, card and needle | `binnacle` 5413 | floating (compass only) |

### Rooms and walls

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Cabin fronts as placed pieces: 81 thin partitions on x −2 (great cabin), −9 (poop cabin) and 14 (forecastle), rows 0-1 plus a part row under the deck edge, with doors and windows | `modular_layout` 1755-1993 | wrong: thick block walls (castle front cells), 3 doors only |
| Doors: great cabin, poop cabin, forecastle, companion (`game:door-sleek-windowed-oak`) | 1880-1993 | 3 of 4, no companion |
| Windows in the great cabin and poop cabin fronts | `modular_layout` | missing |
| Poop cabin side windows: four lights a side with casings following the hull, panes and cames, pilasters, cornice | `side_lights` 8332 | missing |
| Straight knees under every beam end; riders where unlined | `straight_knee` 7268 | missing |
| Crew plaque on the mainmast (bronze plate with timber backing) and spawn plate | 10944-10976 | floating (backing gone; the main mast moved a cell) |

### Fittings

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Pump with chamber, spout, brake, pipe down to the keelson and strainer | `pump` 5846 | partly floating (pipe gone) |
| Capstan with drumhead, whelps, pawl ring, bars, cable turns and coil | `capstan_and_cable` 5468 | partly missing (timber gone) |
| Boat gallows: posts carried up from frames 4 and 10, beams, knees, chocks, lashing rings; two boat places | `boat_gallows` 5931 | floating (boat places, no gallows) |
| Entry gates: planked leaves with their own rail cap, hinges, latch | `entry_gates` 8744 | wrong: open gateway cells, no leaves |
| Rope side ladders from the entry ports to the water | `ladders` 5200 | floating (carrack-m's side, z 6.77) |
| Gun ports with lids: waist x 1, 3, 5, 9, 11; quarterdeck −7, −5, −3; forecastle 17, 19; stern | `GUNPORTS`, `CASTLE_PORTS`, `port_lid` 9134 | wrong: 8 waist ports (no x 3), no lids, no castle or stern ports |
| Guns: 10 waist laid, 6 quarterdeck, 4 forecastle and 2 stern places starting empty | `guns` 8854 | wrong: 8 waist only |
| Ring bolts beside each port | `guns` | missing |
| Scuppers: lead-lined holes at x 0.25..12.25 | `scuppers` 9207 | missing |
| Channels, deadeyes and chain plates for the main shrouds | 4967-4999 | floating (chain plates only) |
| Pin rails with belaying pins (fixtures kept) | 4995-5106 | floating |
| Shroud rails for the fore and mizzen shrouds | 5001-5014 | missing |
| Lantern hooks: mast collars and arms, stern bracket; 14 start lanterns | 11084-11133 | ok on the masts; stern floating |
| Hearth and oven pieces in the forecastle | 1974-1980 | ok |

### Rig

| Carrack-m | Where | Grid carrack |
|---|---|---|
| Masts: main at 2.5, fore at 17.5, mizzen at −12.5, with collars at each deck and masthead caps | `MASTS` 1429 | main moved to 3.5; the collars went with the timber |
| Main top: planked floor, stave tub, capping rail, battens, cheeks | `main_top_tub` 5125 | ok (above her rail) |
| Yards, sails (red-cross courses, topsail, lateen), braces and sheets | 1459, 10676 | ok, but warped near the main mast |
| Shrouds with ratlines, stays, backstays | 4954-5034 | ok above the rail; their feet end where carrack-m's channels were |

## Holes found by search

`tests/HansaTests -- holes` scans the grid carrack at every quarter block. For each deck inside her
hull, and for her side planking from bottom to sheer, it checks two things:

- her collision is solid there;
- a face is drawn there.

Designed openings (wells, hatchways, ports, windows, gateways) are skipped. Ray hits on moving parts
(guns, pump) are ignored. Each finding has a cluster position. `-- probe x y z` shows the cells,
faces and quads at a point.

Before the fix:

- **Every deck had a gap at her stern:**
  - quarterdeck x −17.1..−15.9 (188 samples);
  - main deck at −15.6;
  - lower deck at −14.4..−13.9;
  - hold floor at the bow, x 15.1..15.6.

  The cause: `HullForm.Slab` drops a whole strip between two stations if the deck's height is
  below her bottom at either end. At a raked stern or a rising bow, that is the last strip.
  **Fixed:** the strip is now trimmed to where the hull holds the deck. All deck collision holes
  are gone.
- **Still open: 8 single-voxel pinholes in her side planking.** At x −15.6 y 13.25,
  −14.4 y 3.2, −13.4 y 1.5, 16.1 y 9.5 (design y), mirrored on both sides.
- **Still open: no outer face drawn at about 90 small places,** mostly at the stern and bow
  facets. For example, a rib cell at x −12, z 5.5, y 6.6-7.4: its outer faces are left out of
  the mesh because the face-sharing rule takes them for faces between two fixed blocks.

The pinholes and missing faces belong to my construction-core hull and would go with it in the
redo.

## Redo plan (proposed, not agreed)

Start from carrack-m as the generator builds it, not from the construction core with carrack-m's
rig on top:

1. Run `tools/shipgen/carrack.py` as variant carrack-m inside Hansa. That gives the complete ship,
   including all 125 layout pieces, every fitting and every mesh detail.
2. Make her hull blocks local. The planking between two block borders becomes that block's own
   mesh and slot, at the same lines carrack-m has now. The aim is that taking out a block takes
   out only that block, and every opening is a cell. Ribs keep their taper (user, 16 October).
3. Change only what doesn't fit a block:
   - the main mast moved a cell (decision 11);
   - her bottom one whole block (decision 14);
   - the draft that follows from that.

   Every change is listed against the tables above.
4. Before handing her over, check:
   - every row above is present;
   - nothing floats (each fitting touches her);
   - the hole search is clean;
   - the full self-test passes;
   - renders of carrack-m and her from the same views have been compared.

## Redo status, 16 October 2026 (evening)

The user agreed to redo her from carrack-m with common-sense changes: windows and ports moved
and resized onto blocks, guns moved or fewer where blocks need it. Stairs and some inner layout
will change later, as the user wants.

**She is now built by carrack-m's own generator.**

- Command: `HANSA_MODULAR=1 HANSA_SHIP=hansa-carrack python tools/shipgen/carrack.py`. It writes
  `config/ships/hansa-carrack.json`, `config/shipmeshes/hansa-carrack.json` and
  `shapes/entity/ship/hansa-carrack/`.
- Carrack-m's files are untouched. Every change below is switched on only by `HANSA`.
- The construction-core ship from `GridShips` is now `grid-carrack`. It has `"load": false`, so
  it only runs in the offline checks.
- Everything carrack-m has is back, because it is carrack-m's build:
  - all 125 layout pieces: real stairs to the castles, the companionway, thin partitions,
    railings, cabin windows;
  - the wheel and its posts, binnacle, rudder and tiller;
  - name board, taffrail, stern gallery, side windows with stained glass;
  - wales, mouldings, rail caps, sweeps;
  - catheads, hawse holes, bitts, bowsprit, heads, channels, deadeyes, pin rails, gallows;
  - the interior lining, ribs and knees;
  - the poop and forecastle overhangs, her bow and stern lines.

**What changed from carrack-m, all to stand on blocks:**

| Change | Carrack-m | Hansa carrack |
|---|---|---|
| Hull bands (one slot panel each) | 11 bands, a quarter block off the block rows, fixed strips between | the block rows from her bottom to the poop's rail |
| Hull panels | 652, mixed heights | 684, each one block |
| Her bottom (decision 14) | 0.25 under the hold floor, keel 0.25 below that; draft 4.375 | one whole block under the hold floor, keel flush; draft 4.875; the bilge under G −2 stretched down |
| Waist gun ports | on the frame spaces' middles (x 1, 3, 5, 9, 11), each across two panels | in one block each, aft half of the frame space (x 0.5, 2.5, 4.5, 8.5, 10.5), 5 a side as before |
| Castle gun ports | −7, −5, −3; 17, 19 | −5.4375, −3.4375; 16.5625, 18.5625 (in one block, a sixteenth clear of the frames) |
| Stern gallery | great cabin 5 lights y 4.0-5.75; poop 4 lights on the blocks' borders | great cabin 5 lights in the row 4.5-5.5; poop 3 lights at z −1, 0, 1 in 7.5-8.5 |
| Poop cabin side lights | y 7.25-8.75 | y 7.5-8.5 (one block) |
| Panels at a deck's level | rejected for having their inside in two rooms | their room is the one under the deck; the deck's own edge is not a "fitting" |
| Hull lines (decision 8; the user: "very round hull without more faceted approach", then "cross section look zig-zag") | smooth splines | faceted: one flat plane per block row, its creases on the block rows' borders and on every whole x, each corner at carrack-m's exact width (a straight chord of her curve: rounding the corners made the side zig-zag); the first and last facet of each row end at her transom and stem; her stem and sternpost lines as carrack-m's; the transom as wide as the faceted sides where they meet it |
| Entry gates and their side ladders (the user: "1 block width i would expect") | 6.125..7.875, two blocks | 6.125..6.875, one block, hung on frame 6 |
| Quarterdeck guns | three a side (−7, −5, −3) | two a side (−5.44, −3.44): the aftmost would lie under the poop break's sweep, whose planking takes no port |
| Stern chasers | z ±1.875 | unchanged: the port, 1.5..2.25, is already within one block |
| Ribs (frames), decision 10 and the user: "rib spacing as looks to be also magelan" | a frame on every even line (every 2 blocks) | the mixed spacing on carrack-m's lines: −16, −14, −10, −6, −2, 2, 6, 10, 14, 16, 20, 22 (bays of 3 clear cells, 1 clear cell either side of each mast, 1 at the ends); each rib's cell the block aft of its line. Bulwark frames, knees, overhang brackets and gallows posts follow |
| Deck beams | every even line | unchanged (those between ribs bear on her side), so her hatchways, wells, companion and stairs keep their beams |
| Hull panel grid and ids | the frames' grid | the same two-block naming grid (`panel_stations`), so the mod's "frames" contract and panel ids are unchanged |
| Main mast | x 2.5 | x 3.5 (cell 3..4, between ribs 2 and 6); the main shrouds and channel a cell forward with it; the pump from 3.5 to 4.5 |
| Boat gallows posts | frames 4 and 10 | ribs 6 and 10 (no rib on line 4); the gate's opening a sixteenth clear of the post (6.1875..6.875) |
| Poop cabin side lights | 4 a side (−13.5..−10.5) | 3 a side (−13.5, −12.5, −11.5): the cell −11..−10 is a rib's |

**Still fixed (not panels):**

- her bottom block, which has no ceiling of its own;
- the main deck's row with the wales over it;
- castle rows where a moulding or a castle break's sweep runs over them;
- her very ends.

These are carrack-m's fixed structure too. Making them panels means letting a panel own the
moulding over it. That is a later step.

**Checks, quick build (`HANSA_FAST=1`: gun arcs not swept, 88 s instead of 22-32 min):**

- the generator's own fit checks pass;
- the panel checks pass: ids, quads, solids, edges closed;
- the mesh check is clean: no visible z-fighting and no light leaks. The faceted hull needed two
  fixes: a crease filter for her lining's ring grid, and each row's last facet ending at her stem
  and transom.
- full build (gun arcs swept), 17 October early morning: 684 panels; gun arcs 40 limits, 0 wrong;
  mesh check clean.
- in game (dedicated server):
  - the grid-only self-test is ALL OK: all 14 start lanterns hung (the hold's moved to cell 2..3,
    clear of the mainmast and the pump's pipe), 5 stairs, 4 doors, 2 hatches, 10 guns;
  - the full inherited self-test: 319 pass, 2 fail. Both failures are carrack-m's own and fail the
    same way for the inherited carrack.

The full build sweeps the guns' arcs: 44 limits, 0 wrong.

**Tools added:**

- `tools/HansaPreview -- --skin <mesh> --def <ship> --shapes <dir>` renders any in-game mesh as the
  game draws a new ship: her decks from the voxel shapes, slot faces toward a slot hidden while it's
  in, her fittings marked. Viewer hash options `cam=`, `cut=`, `hide=` script the views. Stairs,
  partitions, railings and doors are pieces the game places: not in the preview.
- `tests/HansaTests -- holes` and `-- probe` search the construction-core ship for holes.
