# Modular pieces: the default-layout contract (0.3.16, notes 154–161; latest changes 0.3.28, §13: notes 223–231)

The contract between the generator (`tools/shipgen/`) and the mod (C#) for a ship whose cabin fronts, windows, doors,
stair railings, breastwork rails, stairs and horizontal panels are **placeable pieces** laid at launch (the player may
take them up, move them, paint them), not permanent structure. First user: `carrack-m` ("Carrack (modular)", its own
charter); the existing carracks stay as they are.

**Archived designs (0.3.25, the user: "We can keep only modular now and park in archive previous").** `carrack-m` is the
carrack players get: plainly "Carrack" (entity, design name, "Charter of a carrack"), the only charter in the creative
inventory. `carrack` (the first, fixed-cabin design) and `carrack-a` (companion option A) are archived: their entities,
definitions, meshes and charters stay registered so saved ships and deeds already in inventories keep working (an old deed
still launches its ship), but no charter for them is to be had (creative inventory, recipes, traders). The generator still
builds them (`python carrack.py`, `MAGELAN_COMPANION=A python carrack.py`) and their offline tests still run (they guard
the shared code). Different companion layouts are tried on carrack-m by rearranging its pieces in the game.

## 1. The piece kinds (what the player holds)

| kind        | on                                   | block in hand (any wood)                         | default `block`                       | seals | solid | paintable |
|-------------|--------------------------------------|--------------------------------------------------|---------------------------------------|-------|-------|-----------|
| `partition` | a border place, one row              | planks (`game:planks-*`)                         | `game:planks-oak-ud`                  | yes   | yes   | yes       |
| `window`    | a border place, one row              | a glass pane (`game:glasspane-*`)                | `game:glasspane-leaded-oak-ew`        | yes   | yes   | yes (casing) |
| `door`      | a border place, rows 0 and 1         | a vanilla door (`game:door-*`)                   | `game:door-solid-oak`                 | shut  | shut  | yes       |
| `railing`   | a border place, one row              | a wooden fence (`game:woodenfence-*`, `roughhewnfence-*`) | `game:woodenfence-oak-ew-free` | no    | yes   | yes       |
| `stair`     | floor cells F, (M,) H, 1 × 3 or (0.3.23, `length` 2) 1 × 2: a walked ship's stair with its own handrails (§10; 0.3.19 §8) | a ship's stair (`magelan:shipstair-long`, `-steep`) | `magelan:shipstair-long` (`-steep` for length 2) | no | its steps and handrails | yes |
| `ladder`    | two floor cells (foot + head), 1 × 2: the companion ladder-stair (0.3.19, §8; §7.1) | a ladder (`game:ladder-*`) | `game:ladder-wood-oak-north` | no | its ladder | yes |
| `panel`     | a horizontal place (a cell's top or a shelf) | a plank slab (`game:plankslab-*`)        | `game:plankslab-oak-down-free`        | yes   | yes   | yes       |
| `hearth`    | a floor place (0.3.18, §7)           | a block of fire bricks (`game:claybricks-good-fire`; a vanilla firepit in creative) | `game:claybricks-good-fire` | — | yes | no |
| `oven`      | a floor place (0.3.18, §7)           | vanilla's clay oven (`game:clayoven-*`)          | `game:clayoven-north`                 | —     | yes   | no        |
| `flue`      | a flue place over a hearth, one per deck (0.3.18, §7) | vanilla's brick chimney (`game:claybrickchimney-*`) | `game:claybrickchimney-four-red-ns` | — | yes | no |

**0.3.18: building mode** (0.3.19: a toggle, hotkey B or `.magelan build`, not a saw: §8.1). Planks, fences, glass panes, plank stairs and plank slabs are ship structure (partitions,
railings, windows, stairs, panels) only while the player holds a **saw** (vanilla's `saw-*`) in the off hand; without one
they're ordinary decoration (where a block of theirs fits a floor place) or nothing. The hearth, oven and flue blocks
need no saw (fire bricks, a clay oven, a brick chimney are nothing else aboard). Pieces the design lays aren't affected.

All border pieces are the partition's thickness: drawn 1/8 block on the grid line (half each side), picked as a sheet on
the line, solid in the quarter-block voxel layer on their anchor side, so a chest stands right up to them either side.

- **window**: a block-sized glazed panel: a wooden casing inside the block (the pane's wood), lead cames on a
  rectangular ~0.2 grid (5 × 5 panes), clear glass drawn with the ship's glass.
- **railing**: a balustrade one block high: a post at each end, square balusters between, a handrail and a foot rail.
  Beside a placed stair (a railing on the border along the stair's side, on the stair's deck) it is drawn and solid
  **raked**: following the treads, its handrail 0.9 over them. Railings don't seal and don't take wall pieces.
- **stair** (0.3.16–0.3.17; **replaced in 0.3.18 by the companion ladder-stair, §7**): a flight 2 cells long and 1 wide
  (its foot cell and the next one toward `up`), rising from its deck to the
  next deck above (the lowest deck in `decks` 1–4.5 blocks over it whose x range covers the head), or `rise` if given.
  Open underneath: treads one voxel thick on two stringers, 8 treads for 2 cells; walked as any stairs (the deck mover
  steps up ≤ 0.6). It takes the foot cell's floor place and the places over both cells it rises through; the head
  cell's own floor place (under its high end) stays free for a chest (note 158). Its cells and everything it rises
  through must be free of her, with a man's height (1.8) over every tread and over the 0.6 behind it where he stands
  stepping up: **the well in the deck above must be open over both cells and the cell before the foot** (a 3 × 1 well
  for a 2 × 1 stair, with a 3-block rise; the offline test walks a 3 × 1 well up and down with the deck mover).
- **panel** (horizontal): a plank board 1/8 thick lying under its plane: a shelf (plane a block or two over the floor),
  or the top of a stair well closed flush with the deck above (plane = the deck above's height). Things stand on it (the
  stacked place over it holds while it's there).

## 2. Where they go (places the mod works out from the definition)

- **Border places** (vertical): on the block grid's lines, between floor cells, a block high, rows 0, 1, (2 partial
  under the beams), as since 0.3.9/0.3.13. **New in 0.3.16:** also on open decks (not only in closed rooms): rows 0 and 1
  on the main deck's waist, the castle decks, round wells. A border needs its anchor floor cell's quarter-block layer
  along the line free of her collision — **so the generator must leave a modular wall's line open in the collision**
  (no permanent bulkhead or breastwork there), on a grid line (with `grid.origin` [0, 0.125, 0.5]: lines at whole x
  and at z = k + 0.5).
- **Horizontal places** (`hb:<deck>:<i>:<k>:<h>`): over each floor cell (level 0) at h = 1, 2 (where the block under
  the plane is free of her) and h = 3 where the cell is open to the deck above (a well).
- **Ceiling hooks** (note 161, `h:c:<deck>:<i>:<k>`): under the deck over each floor cell in a room, where the deck's
  underside is flat between beams (no beam through the cell) and at least 2 blocks over the floor. A lantern hung
  there gets the iron eye the design's own ceiling hooks have. The generator no longer needs `hooks` for lanterns in
  rooms (it may still give them, and `startPieces` may name `h:c:...` places).
- **Wall spots on placed partitions** (note 159): each partition put up has a wall spot on each face (paintings,
  wallpaper, lanterns, shelves), as her bulkheads have; multi-block paintings span neighbouring partitions on a line.

## 3. The definition fields

```jsonc
// in config/ships/<code>.json
"modularPieces": [
  // a border piece: "at" = the middle of the 1-block segment's foot ON the grid line, "along" = the way the wall runs
  //   (along "x": a line of constant z, at[2] is the line, at[0] the segment's middle; along "z": at[0] is the line)
  //   at[1] = the row's foot: deck y + 0, + 1 (+ 2)
  { "kind": "partition", "at": [-3.0, 2.125, 1.0], "along": "z" },
  { "kind": "window",    "at": [-3.0, 3.125, 1.0], "along": "z" },
  { "kind": "door",      "at": [-3.0, 2.125, 2.0], "along": "z", "block": "game:door-sleek-windowed-oak", "open": false },
  { "kind": "railing",   "at": [-3.5, 5.125, 0.0], "along": "z" },
  // a stair: "at" = the middle of its FOOT cell on its deck; "up" = the way it climbs (+x, -x, +z, -z); "rise" optional
  { "kind": "stair", "at": [-1.5, 2.125, 3.0], "up": "-x" },
  // a horizontal panel: "at" = the middle of the cell, at[1] = the panel's plane (its top): deck y + 1, 2 or 3
  { "kind": "panel", "at": [-5.5, 2.125, 4.0] },
  // (0.3.18, §7) the ship's hearth and oven: "at" = the middle of the floor cell on its deck; "facing" = the way its open
  //   front (the hearth's) or its mouth (the oven's) faces: where the cook stands ("+x", "-x", "+z", "-z")
  { "kind": "hearth", "at": [16.5, 2.125, 2.0], "facing": "-x" },
  { "kind": "oven",   "at": [17.5, 2.125, 3.0], "facing": "-x" },
  // a flue segment: "at" = the hearth cell's middle (x, z) and at[1] = the deck the segment rises FROM (the hearth's deck
  //   for the first; the next deck up for the next)
  { "kind": "flue",   "at": [16.5, 2.125, 2.0] }
],
// rooms whose walls are (partly) placed pieces: sealed only when the pieces close them (a flood fill every time),
// never assumed sealed from the design
"rooms": [ { "code": "greatcabin", "min": [...], "max": [...], "enclosed": true, "pieces": true } ],
// openings in a deck (stair wells): the room under one is open to the deck above while it's open, unless every cell of
// it is closed by horizontal panels (h = 3) — a well opening into another closed room doesn't unseal
"wells": [ { "code": "quarterdeck", "box": [-3.0, 4.875, 2.5, -1.0, 5.125, 3.5] } ]
```

Optional on every entry: `"block"` (the vanilla block it's made of, given back when taken up; default per kind as in
the table), `"paint"` (a dye colour as pieces are painted: `"red"`, `"white"`, ...), `"code"` (a unique key; default
`kind@x,y,z[,along]`).

- Coordinates are ship-local, as everywhere in the definition; the mod snaps `at` to the nearest place within 0.2.
- A door's hinge is the segment's lower end (−x or −z) and it swings into the anchor cell (the mod's rule, as for doors
  put by hand).
- A stair's foot and head cells must both be floor places on the deck; the stair's sides are border places for its
  railings (give them as `railing` entries on the stair's deck, row 0: drawn raked).

## 4. When the mod lays them

- A **new ship** of the design: every entry, after `startPieces`.
- An **existing ship** of a design that has them (a design updated, a ship launched before `carrack-m` got an entry):
  each entry not yet laid on her, if all its places are free (nothing put there by the player); laid or not, it's
  remembered as done (`magelanLaid`), so what the player took up never comes back.
- An entry whose place doesn't exist (her collision closes the line, a typo) is skipped with a warning in the log, and
  the offline test `modular` lists it for the design.

## 5. Rules the pieces follow (the same for laid and hand-placed ones)

- A border piece needs nothing under it (note 160): a top partition stays hanging from the beam/deck when the one under
  it (or a door under it) is taken up; any row may be put first.
- Sealing: partitions, windows, shut doors and panels close; railings, stairs and open doors don't.
- Walking: everything solid in her collision; stairs walkable; railings block (raked ones up to their handrail).
- Taking up: a partition with something hung on it, a panel with something standing on it, a stair with something on
  it: that first.

## 6. Changes in 0.3.17 (the generator's follow-ups)

- **Fixture margins** (a helm's, a binnacle's, a hearth's, a pump's, a hatch's surround: the room grown round a fixture)
  stop at the walls of the closed room the fixture is in (a forecastle hearth's half block no longer reaches through
  the forecastle front into the waist), and wall pieces' places are kept clear of the fixture itself only, never of its
  margin. On an open deck a cell kept from being a floor place by a margin, by a fixture standing in it or by something
  standing on air near it (a binnacle at the quarterdeck's front) still anchors the wall places on its sides at the
  deck's edge: **the quarterdeck-front railings by the helm and binnacle can be modular pieces** (leave that line open
  in the collision). Boxes that only touch (within 0.001) don't count as overlapping: keepClear boxes needn't be trimmed.
- **`into`** on a door entry: the way it swings, `"+x"`, `"-x"`, `"+z"` or `"-z"` (across its line). Without it, the
  rule stays: it swings into its anchor cell. Between two floor cells the anchor is the west/north one, except where one
  side is a closed room and the other an open deck: then the closed room's cell (so `door-companion-cabin` at x -3 opens
  into the landing, the closed room, without `into`).
- **Open decks get partial places too**: along a line past the last floor cell out to her side (the wedge cells, cut to
  what's free of her), as the rooms have, so the castle fronts' corners by her sides take railing pieces. A railing (not
  a window, not a door) may stand on a partial place; it's drawn and solid along the free part only.
- Raked railings beside a stair are solid and picked only up to the deck the stair climbs to, and their quarter-block
  layer is on the side away from the stair, unless that side is a well and the stair's other side isn't (then inside:
  the stair stays three quarters of a block wide). Two stairs side by side on decks one over the other (carrack-m's hold
  and companion stairs) both walk.
- **Paint zones** with no area on a design are left out of `/magelan zones` and `/magelan paint all`, and can't be painted.


## 7. Changes in 0.3.18 (step A of the slot plan: notes 162–166 and the user's chat)

### 7.1 The stair is a companion ladder-stair (note 162)

Real carracks went below by steep ladders (60–70°), not walked flights. A `stair` entry (fields unchanged: `at` = the
middle of its **foot cell** on its deck, `up` = the way it climbs, `rise` optional, default the deck above as before) is
now a ladder-stair:

- **Footprint**: the foot cell F and the head cell H (the next one toward `up`), 1 wide × 2 long, as before. Its ladder
  stands with its feet on F's deck **half a block in from F's near edge** (the edge away from `up`: F's middle) and
  leans up to **H's far edge** at the deck above, where it hooks over the coaming: a run of 1.5 for any rise (≈ 63° for
  a rise of 3). Two stringers 1/16 in from the cell's sides, flat treads about 0.3 apart, iron shoes on the deck and two
  iron hooks over the coaming are drawn.
- **Climbed like a ladder** (`DeckMover`'s ladder climbing, kind `stair`): walk at its foot toward `up` (from the cell
  before F, or from F itself) and you're on it; walk on and you climb, back and you go down; at the top you step off onto
  the deck above **half a block past H's far edge**; from the deck above, walk into the well over its top and you climb
  down. Nobody walks up its treads.
- **The well**: the deck above must be open over **F and H only: a 2 × 1 well** (no longer 3 × 1; don't cut the deck's
  edge beyond it). The `wells` entry is the two cells: `[x0, deckAbove − 0.25, z0, x1, deckAbove, z1]` over F and H.
  What's checked when it's put (and by `-- design`): the climber (0.4 wide, 1.85 tall, his feet 0.2 behind the ladder's
  line; near the top only his head and shoulders, his legs going over the coaming) passes up the ladder free of her
  collision; where he steps off at the top (half a block past H's far edge, on the deck above) and where he stands at
  the bottom (half a block before F's near edge, on the lower deck) are floor with a man's height over them. So: **the
  cell before F (lower deck) and the cell past H (upper deck) must be walkable deck**; over F and H the deck above may
  have a beam or coaming up to 0.75 deep along the well's two ends (its near edge over F's first quarter, its far edge
  under H's last quarter), and nothing else of her lower than the deck itself (carrack-m's 2-cell wells pass).
  The `-- design` test also takes each ladder up and puts it back.
- **Places taken**: F's floor place and the stacked places over F and H; **H's own floor place stays free** (the
  ladder's boxes are over 0.875 there: a chest fits under its high end).
- **Railings beside it** (border places along F and H on its deck, row 0) are raked to its ladder: their handrail
  0.95 over the ladder's line. Railings round the well on the deck above are ordinary railing entries (leave the side
  you step off at, past H, open).
- **Taking it up and putting it back** works as any piece (note 162: it couldn't be put back).

### 7.2 The ship's hearth, oven and flue as pieces (note 165)

The design's fixed `hearth` and `oven` fixtures still work as they always did (existing ships keep theirs). A design may
instead lay them as pieces — **one hearth and one oven per ship** (a ship whose design has a fixed one can't take a
placed one). They work exactly as the fixed ones (vanilla's firepit inventory, fuel, cooking pots, the oven's baking,
lighting with a firestarter or torch, warmth), only they can be taken up and put elsewhere (empty and cold).

- **`hearth`** (`at` = the floor cell's middle, `facing` = where the cook stands): a brick firebed filling the cell
  (fire bricks, 0.25 high, a kerb 0.5 high on its three other sides) with vanilla's firepit on it. Needs a **floor
  place** (level 0) on her solid floor with **2 blocks clear over it** (her collision and pieces). Takes the floor place
  and the stacked places over it. The fire's middle is 0.45 over the deck, the pot's 0.35.
- **`oven`** (`at`, `facing` = its mouth): vanilla's clay oven, a block, on a floor place with a block clear over it.
  Takes the floor place and the stacked places over it. No flue needed (vanilla's oven smokes at its mouth); its smoke
  goes up the hearth's flue when it stands within 1.5 blocks of a flued hearth.
- **`flue`**: a brick chute (0.5 × 0.5) **from over the hearth up through the deck above to a block over that deck**,
  one segment per deck: the first from a block over the hearth's deck (a brick hood over the fire), the next from a
  block over the deck the first came through, and so on. `at` = the hearth cell's middle with `at[1]` = the deck the segment rises from. A flue place exists
  over every floor cell (on the ship grid) where: the 0.5 column over the cell's middle is free of her from the deck to
  the underside of the deck above, that deck is **at most 0.75 thick** there (put the hearth between the beams), and the
  block over that deck at the column is free of her. A segment needs the hearth (first) or the segment under it (next).
  It takes the stacked places over the cell it rises through and the floor place on the deck it comes out on.
- **The fire rule** (0.3.24, note 192: "works fine relocated, we can skip the chimney for a while to simplify"): a placed
  hearth **burns with or without a flue**. A flue is optional: when its segments reach a block over a deck with open air
  over that, the smoke rises from the flue's top; otherwise from the fire. A flue segment can't be taken up while the
  hearth burns. (Until 0.3.23 a hearth without a way out for its smoke couldn't be lit.) The generator may drop carrack-m's
  `flue` entries (note 183).
- **For carrack-m**: drop the fixed `hearth` and `oven` fixtures (and their firebed/chimney in her mesh), lay `hearth`,
  `oven` and `flue` entries instead; her forecastle front is then all pieces. The hearth's `room` is worked out from
  where it stands.

### 7.3 Placing by aim, the saw, dye

- **Placing looks only next to what the crosshair hits** (vanilla's way), never at places in mid-air: the floor (its
  floor place; with a wall piece the border place on the cell's edge nearest the hit), a piece's face (the place next
  to it along the face's normal), a piece's top (the place on it), a beam's or deck's underside (the top-row border
  place under it), a partition's edge (the next border place along its line, over or under it), a wall (its wall spot),
  a shelf's height on a wall (the horizontal place in front). Nothing changes for the generator.
- **A dye container always paints** what it's aimed at (a zone, a paintable piece); Shift + right-click places it.
- **Corners**: two railings meeting at a corner share one post (only one is drawn); a railing ending at a wall across
  it has no post there; two walls (partitions, windows) meeting at a corner fill its square once (the one along x stops
  short, the one along z reaches over); a railing raked beside a stair isn't drawn above the deck it climbs to where that
  deck has its own railing on the line. **Doors** stop their swing 4° short of square (their open leaf never in the
  plane of a wall beside them).
- **Windows**: the leaded window is two by two lights in a wooden mullion and transom, each light leaded two by two
  (4 × 4 small panes, thin lead).


## 8. Changes in 0.3.19 (the user's feedback on 0.3.18)

### 8.1 Building mode is a toggle, not a saw

A saw can't be held in the off hand in VS. **Building mode** is the player's own switch now: the hotkey **B** (Magelan:
building mode in vanilla's controls, rebindable) or `.magelan build [on|off]`. The server sets it (per player, kept with
them), and the HUD says "Building mode" while it's on aboard. In building mode, planks, fences, glass panes, plank stairs,
plank slabs and **ladders** make ship structure. Out of it they're plain decoration: a floor place, or on top of a piece
(that's why planks stack upward but not sideways), and the hint says so. Nothing changes for the generator: laid pieces
are laid in building mode.

### 8.2 Two floor kinds: `ladder` and `stair`

| kind     | footprint                     | way up                       | block in hand              | default `block`                       |
|----------|-------------------------------|------------------------------|----------------------------|---------------------------------------|
| `ladder` | 1 × 2 (foot F, head H)        | climbed, about 63° (§7.1)    | a ladder (`game:ladder-*`) | `game:ladder-wood-oak-north`          |
| `stair`  | 1 × 3 (foot F, middle M, head H) | walked, about 45°         | plank stairs               | `game:plankstairs-oak-up-north-free`  |

Fields as before: `at` = the middle of F on its deck, `up` = the way it climbs, `rise` optional (default the deck above,
half a block past H).

- **`ladder`**: the companion ladder-stair of §7.1, unchanged: a 2-cell well over F and H, or the deck above ending over H.
  It has **its own handrails**, part of the piece (drawn only): on each side over the stringer, three upright stanchions
  (at its foot, middle and head; the last one on the coaming) and a wooden rail parallel to the ladder, 0.9 over its line.
  **Guard rails over a drop**: where a side of its two cells borders a drop (no deck beside it on its deck: a well, an
  open edge), the ladder has its own guard rail on that border line: two posts, a rail on top 1.0 over the deck and one
  halfway, solid in the quarter-block layer outside the line (in the drop, so the way onto the ladder stays a whole cell
  wide). It is worked out from her collision (`PiecePlanner.LadderGuards`); the generator gives nothing for it. carrack-m's
  companion ladder gets one on its well side, over the hold well.
  **No railings beside a ladder**: a railing on a border along its sides (row 0 on its deck, within its two cells) is
  refused, for the player and for the design (a design entry there is skipped with a warning). Railings round the well
  on the deck above are still fine. On an existing ship, the railings beside its ladders are taken off once and their
  fences go into the hold.
- **`stair`**: a walked stair. With a rise of 3 it has twelve treads 0.25 apart (risers at most 0.25), each a voxel thick
  on two stringers, open underneath. It is walked by the deck mover (no climbing). The top tread is flush with the deck
  above at H's far edge.
  - **Up to a deck's edge** (a castle front): **no well** if the deck above ends at the stair's head. Put H's far edge on
    the deck-edge line. The deck above, including any coaming or beam under it, may reach back over H by **at most 0.25**.
  - **Up through a deck**: a **3 × 1 well** over F, M and H. The well's near edge (over F's near edge) may be at most
    about 0.4 thick below the deck top.
  - What's checked (`PiecePlanner.GridClash`, at placement and by `-- design`): a man's height (1.85) over every tread and
    over the 0.6 before it, where he stands stepping up. The cell before F (its deck) and the cell past H (the deck above)
    must be walkable deck with headroom. **Leave the deck's railing place at the stair's head open** (no railing across
    it), or nobody steps off.
  - Places taken: F's floor place and the stacked places over F, M and H. M's and H's own floor places stay free (a chest
    fits under it).
  - **Railings beside it** (border places along F, M and H on its deck, row 0) are raked along it: posts upright, the
    rails parallel to the nosings' line, the handrail 0.9 over it. Their quarter-block solid layer is always on the side
    away from the stair. Where a raked railing's top end meets a railing's end on the deck it climbs to (the castle front's
    railing beside the stair's head), the raked one draws one newel post on the corner and the deck's railing's rails stop
    at it.
- **Older definitions**: until 0.3.18 `stair` meant the ladder-stair. A `stair` entry is still laid as a **ladder** when
  its third cell up the run (where a walked stair's head would be) is under the deck above and not in a well
  (`MountLayout.LaidKind`): a 2-cell well over F and the next cell, or a castle front over its second cell. New
  definitions say `ladder`. Saved pieces keep what they were (a 0.3.16–0.3.18 stair piece is a ladder-stair). A ladder
  piece taken up gives back its block; plank stairs put down again make a walked stair.
- `-- design` walks every walked stair up and down (with all her pieces in her collision), takes it up and puts it back,
  climbs every ladder, checks that no railing stands beside a ladder, and that no side of a ladder's cells over a drop is
  open (walking sideways off its foot both ways, nobody falls more than a block).
- **Guns' keep-clear** (`MountLayout.GunClear`): the trail's swept shape, each side to its own train limit (and the gate-open
  limits where they're wider), kept as one box per eighth of the trail's length. It used to be one box round the whole
  sweep, with the larger limit both ways. Guns can have their full train back beside the stairs' foot cells.

### 8.3 For carrack-m

- The castle stairs (`stair-quarterdeck`, `stair-poop`, `stair-forecastle`) are walked `stair` entries, 3 long, their
  heads at the castle fronts, with no front railing at each stair's head.
- The companion and hold stairs are `ladder` entries (their 2-cell wells unchanged), with no railings beside them.
- Every stair cell must be a free floor place (not in a fixture's keep-clear box, such as a gun's).


## 9. Changes in 0.3.20 (step B of the slot plan: deck cells, hatches as pieces)

The deck cells that can be taken up and laid again, and the hatches over openings, have a contract of their own:
**`docs/slots.md`** (the `deckSlots` definition field, the mesh file's `slot:deck` groups and `slotQuads`, the draw
rule, the fixed strips). For this contract:

- A new `modularPieces` kind, **`hatch`** (§5 there): `"at"` the opening's middle (x, z) at the deck's top (y),
  `"size"` [cells along x, cells along z], `"block"` a vanilla trapdoor (`game:trapdoor-solid-*`: a cargo hatch,
  `game:trapdoor-window-*`: a glazed one; default `game:trapdoor-solid-oak-1`), `"open"`. It lies over a rectangle of
  empty deck slots (1–4 × 1–4) and is laid with the other pieces (after the ladder-stairs, the hearth and the flue).
- A piece can't be put where its boxes would rest on an open deck cell, and a deck cell can't be taken up from under a
  piece (or a design fixture), nor laid while a piece passes through it.
- Wells stay as they are; give their cells as `"empty"` slots too.


## 10. Changes in 0.3.23 (the ship's stairs; notes 169–172)

### 10.1 The ship's stairs: `stair` with a `length`

The user: "Can we make stairs for 2 blocks also? Ladder animation is kind of bad for play" and "maybe for ships stairs
better to have a special block". Stairs are now the mod's own blocks, **ship's stairs**, crafted from planks (grid
recipes `recipes/grid/shipstair.json`: 12 planks for the long one, 10 for the steep one), in the creative inventory and
the handbook. Put aboard in building mode only (out of it the hint says so; ashore they aren't placed at all).
**Vanilla plank stairs no longer make stairs** (plain decoration). A vanilla ladder still makes the `ladder`
(the 1 × 2 ladder-stair, §7.1, §8.2), unchanged.

| block | entry | cells | slope | treads drawn | her collision (steps along the run) |
|---|---|---|---|---|---|
| `magelan:shipstair-long` | `stair` (`length` 3 or none) | F, M, H (1 × 3) | 45° | 12, 0.25 high × 0.25 deep | 12 columns, a tread each |
| `magelan:shipstair-steep` | `stair`, `"length": 2` | F, H (1 × 2) | ≈ 56° | 12, 0.25 high × 1/6 deep | 8 columns a voxel long, tops 0.25 0.75 1 1.5 1.75 2.25 2.5 3 |

Both are walked by the deck mover (no climbing), their top flush with the deck above at H's far edge. Fields as before:
`at` = the middle of F on its deck, `up` = the way it climbs, `rise` optional (default the deck above half a block past
H). The entry's block defaults by length; a `block` naming vanilla plank stairs (an older definition) is replaced by the
ship's stair of the entry's length. An entry **without** `length` is the long one, except that an old entry whose third
cell is under the deck above is still laid as a `ladder` (`MountLayout.LaidKind`, §8.2); with `length` given it's always a stair.

**Their own handrails** (part of the piece, like the ladder's): drawn on each side over the stringer, three stanchions
and a rail parallel to the nosings, 0.9 over them; **solid** in the quarter-block layer outside each side, per voxel
column from a quarter under the nosings to 0.9 over them (nobody steps off the side), left out where her own collision is
already there (a bulwark, a bulkhead) and stopped at the deck above's top where that deck is beside it (a well's sides:
that deck's railings). So **no railing goes beside a ship's stair** (border places along its cells on its deck, row 0):
refused for the player (`railing-by-stair`) and skipped with a warning for a design entry. Raked railings keep working
beside the walked stairs saved by 0.3.19–0.3.22 (pieces without handrails of their own).

**Where they go** (checked by `PiecePlanner.GridClash` when put, and walked by `-- design`):
- **Up to a deck's edge** (a castle front): no well. H's far edge on the deck-edge line; the deck above (planking or
  coaming) may reach back over H by at most 0.25.
- **Up through a deck**, the long one: a 3 × 1 well over F, M and H (its near edge at most ~0.4 deep below the deck top).
- **Up through a deck**, the steep one: the walker (1.85 tall, 0.6 long) stepping onto its third column has his head
  over the well's near edge. The deck above must be **clear a quarter block before F's near edge** (nothing of her from
  the deck above's top down there): either a **3 × 1 well over P (the cell before F), F and H**, or a 2 × 1 well over F
  and H with the deck above cut back 0.25 before F (the edge beyond that at most 0.4 deep). A plain 2 × 1 well with its
  edge at F's near edge is refused (`flight-no-well`): walking up, the head hits it.
- Landings: the cell before F (its deck) and the cell past H (the deck above) walkable with a man's height; leave the
  deck's railing place at the stair's head open.
- Places taken: F's floor place and the stacked places over all its cells; the other cells' own places stay free.

### 10.2 Putting a stair or ladder back by hand (notes 170, 172)

A stair or ladder taken up couldn't be put back by hand: the client sent the server the plan's direction, which for a
stair or a ladder is the way **up** (away from the player), and the server took it as the way **toward** the player, so
it planned the piece turned round (the quarterdeck stair "no room", the companion ladder climbing into the great cabin's
deck). The client now sends the direction the plan was made toward (`PiecePlan.Toward`). Nothing for the generator.

### 10.3 Hatches from below (note 169)

A shut hatch is picked from below (the crosshair meets its cells' planking first, her collision) and seen there, so a
right-click opens it from below. A ladder whose way up comes out under a shut hatch stops the climber's head under it
(`DeckMover.CoverUnder`), and nobody steps onto it from above through it. Nothing for the generator (remove the hatch
ladders as planned).

### 10.4 Guns on the castle decks (note 171)

Gun places on an open castle deck (quarterdeck, poop, forecastle) with lidded ports in raised bulwarks work as the
waist's: complete `type: gun` fixtures and `gunPlaces` entries (§7 of `docs/slots.md`). A port on an open deck closes no
room, and the sea through it lands on that deck (no downflood point below). No guns in the cabins.


## 11. Changes in 0.3.24 (chalk notes 176–193)

- **Stairs and gun places (notes 176, 181, 182)**: a gun place that starts **empty** keeps no working room in her layout
  (`docs/slots.md` §7): its floor places are free for pieces and stairs; a piece is refused there only while a gun is
  mounted (`piece-gun-room`), and the gun can't be mounted while a piece stands in its room (`gunplace-blocked`). The
  player chooses. A laid place still keeps its room. **Generator**: make the castle gun places symmetric; mirror the waist
  guns' train trims exactly (gun-port-1's aft train as gun-starboard-1's, gun-port-5's forward as gun-starboard-5's), or
  the main deck's port cell aft of gun-port-1 stays in its sweep (the port mirror of the starboard stair spot, note 176).
- **Doors from a slant (note 177)**: a border piece seen through its own quarter-block solid layer (from that side,
  slanting) is picked (`ShipPieceSet.OwnHit`, the client's crosshair and the ship's ray). Nothing for the generator.
- **Aiming at a cell's edge (note 178)**: with a wall piece in hand (planks in building mode, a pane, a fence, a door),
  a hit on a floor within 0.3 of a cell's edge (`AimPlace.EdgeBand`) offers the border place on that edge before the
  floor place; the cell's middle still offers the floor place.
- **Railings (note 188)**: balusters 0.07 square at a fixed 0.2 pitch from the segment's grid end (three a block); a cut
  segment keeps its neighbours' rhythm and leaves out what doesn't fit between its posts.
- **Ship's stairs (note 189)**: stringers 0.1 thick, 0.36 deep (were 1/16 × 0.3); the handrails' balusters 0.06 square
  about every 0.3 along the run (7 a side on the steep stair, 10 on the long one; were 3 stanchions 0.045), the rail
  0.09 × 0.08. Both stairs stay. Their solid guards are unchanged.
- **The oven and the hearth (notes 191, 192)**: a firestarter or a lit torch on a placed oven or hearth lights it, Shift
  or not (it's never put down on the floor there). A placed hearth **burns without a flue**: the flue is optional (its
  smoke then comes out at the flue's top), §7.2's fire rule is gone.
- **Lanterns (note 193)**: with a lantern in hand a free hook is aimed at directly (its box, as when empty-handed: the
  mast's hook, the stern lantern's post), and put back there. New hooks the mod finds: on every **mast's four sides**,
  2.6 over each deck it stands through, 0.3 out from its face (`h:m:<mast>:<deck>:<east|south|west|north>`; not where a
  design hook is within 0.6), and **ceiling hooks under any flat ceiling on an open deck** too (not only in rooms). The
  design's mast hooks are optional now; the generator moves the mainmast's above the plaque.


## 12. Changes in 0.3.27 (chalk notes 198–222)

- **Stairs and beams (notes 211, 222; the user's question)**: the frames stay every 2 blocks. A deck beam's collision is the
  voxel column aft of its line, so **a stair across her** (its run along z) goes up through a 3 × 1 well between two beams
  with no beam cut; **a stair along her** through a well across a beam takes that beam's length out with it (the beam
  under a well is in lengths a cell wide that come out once the deck either side is open: docs/slots.md §10), its
  carlings drawn along the opening's sides from beam to beam. The player opens the deck (an axe in building mode on the
  cells), then puts the stair: the beam comes out by itself (its planks back), or with an axe on it in building mode.
  Laid back with planks or boards. Refused with a reason ("a deck beam is in the way...", "a beam that can't come out...").
  Nothing for the generator's `modularPieces`: the wells and stairs as before.
- **A ship's stair's guards (note 215)**: beside a walkable deck its solid guard (the quarter-block layer outside its side)
  starts a man's height (1.9) over its deck: whoever walks along the stair isn't stopped by an invisible wall a quarter
  block into the next cell; over a drop (a well, an open edge) it's solid all the way as before. Saved stairs get it on
  loading.
- **Its balusters (note 200)** reach down 0.04 into its stringers (they stopped 0.25 over them).
- **Mast hooks (note 202)**: the mod's hooks on a mast's other sides at the design's own mast hook's height over its deck
  and its reach (the mainmast's 2.94 over the main deck, 0.76 out), each drawn with the iron arm (and a band round the mast
  where the design's has none) when a lantern hangs there. A lantern saved on a mast hook moves up with it.
- **Ceiling hooks (note 221)**: carrack-m has no ceiling hooks of her own over her rooms (their iron eyes fixed the deck
  cell over them, so it couldn't be taken up): her lanterns hang on the mod's ceiling hooks (`startPieces` name
  `h:c:<floor's deck>:<i>:<k>`, in the beam-free cell half a block aft of where they hung). A cell with a lantern under it
  is refused till the lantern is taken down (as before); a lantern saved on a design hook that's gone is hung on the
  nearest free hook.
- **The wrench (note 199)**: vanilla's wrench, right-click a piece: a wall piece goes round its cell to the next side, a
  floor piece (a chest, a bed, a stair) faces the next way it fits (its contents, paint and id kept).
- **Hatches (note 201)**: in building mode with anything but planks, boards or an axe in hand, the crosshair on an opening
  says what lays it (a planks block or 4 boards) and what covers it (trapdoors, one a cell: a cargo hatch; window trapdoors:
  a glazed one); the building-mode message says trapdoors make hatches.
- **The brow (note 219)**: with her anchored and an entry port's gate open, boards in hand, right-click the gate: her brow
  runs out from the port's sill to the nearest solid footing (a quay, the shore) within 5 blocks of her side, not steeper
  than about 40°: walked off her and on (a ladder of kind "brow", the walking animation); the same click takes it in, and
  it comes in by itself when she weighs anchor or gets under way, the gate is shut or the footing is gone. The gate swings
  inboard as it did (that's the historical way: an entry port had a half door opening inboard, or none, and people came
  aboard over a brow from the quay). Nothing for the generator.



## 13. Changes in 0.3.28 (chalk notes 223–231)

- **The castle fronts on the edge beams (notes 225, 227)**: carrack-m's great cabin front on x −2 and the forecastle's on
  x 14 (the quarterdeck's and the forecastle deck's edge beams' lines, the frames there), not a block in. Each moved entry
  carries `"code"` = the key it had on its old line (`partition@-3,2.125,1,z`...), so a ship laid before doesn't get a second
  front. The rooms (`greatcabin`, `forecastle`) and the stairwell's inboard wall go with them.
- **The poop stair on the centreline (note 231)**: `stair-poop` at z 0, `door-poopcabin` at z 1, the poop front's railing
  open over the stair's head (k 0).
- **Walls round an opening (note 230)**: a border place anchored on a deck cell that's been taken up has a twin on the same
  segment, anchored on the cell across the line and usable only while the first one's cell is out (`MountPoint.Twin`,
  `TwinSide`): a wall round a well stands with its solid layer on the deck beside it, and a door at a stair's head swings
  out onto the landing. Design entries never land on a twin.
- **A hood's roof (note 230)**: on open decks every floor cell has a roof place (`hh:<deck>:<i>:<k>`): a plank slab lying
  on the two-row walls' tops (deck + 2), solid in the voxel layer over that, not tied to the cell under it (a stair walks
  under it). Offered by aiming at a wall's upper part or at a placed roof board; design `panel` entries never snap to it.
- **Border places over a low bulwark** or by an open gun port count as inside her when at least a block inboard of her side
  lower down (the waist's top row and its doors were refused before).
- **A walked stair's landings** (where you step off at its foot and onto the deck past its top) are checked as a man's
  0.6 × 0.6 ("flight-no-landing"): the stern stair by the mizzen whose top stepped you onto her side's curve is refused.
- **Pieces take shots** (docs/slots.md §11.3): partitions, doors, windows and railings crack and are shot away; mended
  cracked with boards and nails.


## 14. Changes in 0.3.29 (chalk notes 232-233)

- **The companion beside the quarterdeck stair (notes 230, 233)**: carrack-m's companion is where the user laid theirs on
  #12600: a steep stair from the lower deck's centreline cell x -2..-1 (`f:lower:-2:-1`) up forward to the waist at x 0,
  through a well of the centreline cells x -3..0 (`main:-3:-1`, `main:-2:-1`, `main:-1:-1`) and the main deck beam's
  length at -2 across it (`beam:main:-2:-1`), all deck slots that start out (no fixed framing: the beam's carlings are the
  segment's). Walled in: its sides on z -0.5 (anchored to port) and z 0.5 (anchored on the quarterdeck stair's cells, the
  well's own being out), two rows on the waist and three in the great cabin; its after end on x -3 inside the cabin; a door
  at its head on x 0 (anchored forward: it swings out onto the waist); a roof of two hood boards over the waist's cells.
  The great cabin's front opens into it at k 0 (its door a cell to port, k -1); the old stairwell in the cabin's starboard
  corner is gone. A wall on the line beside the quarterdeck stair is a quarter block into its cell: that stair is walked a
  hand off its middle.
- **Entries**: a wall entry may name its **`anchor`** ("+x", "-x", "+z", "-z": the way from its line into the cell it
  stands on; round a well, the twin across the line); a `panel` entry with **`"hood": true`** goes on a hood's place; every
  entry may say **`since`** (the layout revision that brought it).
- **Layout revisions** (`layoutRev` 1, `layoutBefore`: what each revision changed): a ship laid before keeps her deck
  slots' states where the start changed (her old stairwell's well stays open, her deck where the new companion is stays
  laid, or as she had it: #12600's own well), her castle ports' boards read as 0.3.28's covers (a port in wherever the
  board is out), and the entries since then are remembered as laid, not put: nothing of her own build is touched. A new
  ship has the new companion.
- **Guns and the deck's edge (note 232)**: a gun's working room counts railings (a breastwork) only against its carriage's
  part (`GunPlaces.CarriageSlices`, 5 of its 8 slices): its crew step round a waist-high rail. The generator keeps every
  castle gun's room on her deck, clear of the breastworks' quarter-block layer.
- **A stair's guard beside a design well** (the companion's beside the quarterdeck stair) starts over a man's height, as
  beside a deck; a wall stops at the face of a door across its end; a hood's board lies a hair over the walls' tops.
- **A walked stair's landing past its head** must be her deck itself (a beam's top a quarter under it doesn't count).
