# Slots: the contract for removable parts of a ship (0.3.20, step B: deck cells; 0.3.21, step C: gun places §7; 0.3.24: an empty gun place keeps no room; 0.3.25: stern chasers §7.1; 0.3.26, step D1: hull panels §9; 0.3.27: beam segments §10, mending openings §9.4; 0.3.28, step D2: halves, port covers, deck cells taking shots §11)

The contract between the generator (`tools/shipgen/`) and the mod (C#) for the **slot architecture** agreed on
2026-10-04 (backlog: "the slot plan"): every changeable part of a ship is a design-defined **slot** with its own geometry
made by the generator; the mod keeps only a **state** per slot (filled / cracked / empty), saves only the differences from
the design, and draws filled slots baked into chunk meshes rebuilt only where an edit happened. Fixed (never slots): masts,
rigging, steering, the skeleton (keel, frames, beams, carlings, knees).

Step B (this document, §1–§7) makes **deck cells** slots, first on `carrack-m`. Later steps reuse the same machinery:
C cannons as items, D hull panels as slots with unified damage (sound → cracked → empty), E shipbuilding on a slipway.
A design without `deckSlots` (`carrack`, `carrack-a`, the boats) behaves exactly as before.

## 1. What a deck cell is

- **One block cell of a deck's planking**, on the ship's block grid (`grid.origin`; carrack-m: lines at whole x and at
  z = k + 0.5), from the deck's top down through its planking (carrack-m: 0.25 thick, one voxel layer). The beams under
  it, the carlings, the knees stay fixed skeleton: taking a cell up leaves them in place (a beam under the cell's edge
  stays under the opening).
- **Only whole cells** (the full 1 × 1 square is planking). Partial cells at her sides, at a deck's end, the wedge
  between the last whole cell and her inside planking: fixed, not slots.
- **Which decks** (carrack-m): the lower deck, the main deck, the quarterdeck, the forecastle deck, the poop deck. The hold
  floor has no slots (it's at the bottom of her: keep it fixed).
- **Fixed strips** (cells simply not listed as slots; the generator decides): cells under structure standing on the deck
  or passing through it (a fixed bulkhead or rail on the cell, a breast beam over it, a mast's partners: any cell a mast
  passes through or touches, the capstan's and the pump's steps, the helm and binnacle, the rudder trunk and the tiller's
  sweep, the heads, the boats' gallows feet, fixed ladders' feet and heads), the guns' working room (each gun's carriage
  and trail sweep: `MountLayout.GunClear`, its boxes as `-- design` lists them; and the cells the gun captain stands on
  behind the breech), the entry gates' swing. Furniture (pieces, design attach places) need not make a cell fixed: the
  mod refuses to take up a cell while anything stands on it (§4).
- **Openings by design** (hatchways, stair wells): their cells **are** slots, with `"start": "empty"` (§2). A hatch over
  an opening is a piece (§5); a stair well stays a well (`wells`, as today) whose cells are also empty slots.

## 2. The definition (`config/ships/<code>.json`)

```jsonc
"deckSlots": [
  {
    "id": "main:5:-2",              // unique, stable across regenerations: "<deck code>:<i>:<k>", i = x0 - grid.origin[0],
                                    //   k = z0 - grid.origin[2] (whole numbers): saved ships refer to cells by it
    "deck": "main",                 // a code of `decks`
    "box": [5.0, 1.875, -1.5, 6.0, 2.125, -0.5],   // the planking: the cell's square x0..x1, z0..z1 on the grid lines,
                                    //   y1 = the deck's top, y0 = y1 - the planking's thickness
    "solids": [[5.0, 1.875, -1.5, 6.0, 2.125, -0.5]],  // optional, default [box]: the collision voxels the slot owns (each
                                    //   voxel whose centre is inside one of these boxes); nothing else of her may own them
    "material": { "block": "game:planks-oak-ud", "count": 1 },  // optional, this default: what laying it costs and
                                    //   taking it up gives back (any wood's planks lay it; the cost of a ship later = Σ)
    "start": "filled"               // optional, default "filled"; "empty": an opening as designed (a hatchway, a well)
  }
]
```

- The **slot index** is the entry's position in `deckSlots` (0-based): the mesh file refers to slots by it (§3).
- **Collision** (`solid`): her grid as designed, i.e. every `"filled"` slot's solids solid and every `"empty"` one's not.
  The mod clears an emptied slot's solids and sets a filled-in one's.
- Slots don't overlap (no voxel in two slots' solids); two neighbouring cells share an edge exactly (x1 of one = x0 of
  the next, same y).
- `wells` stay as they are (each stair well's box); their cells are `"empty"` slots as well.
- Design ladders through an opening (carrack-m's `holdladder`, `hatchladder`) say `"gate": "opening"` instead of a hatch
  fixture's code: the mod lets them pass at their top while the cell at the top (`top`'s x, z on the deck there) is
  empty and no shut hatch covers it.

## 3. The geometry (`config/shipmeshes/<code>.json`)

The slotted cells' planking is **taken out of the voxel shapes** (`exterior.json`, `interior.json`: the deck elements cover
only the fixed strips there) and goes into two new groups of the mesh file, quads as every group's
(`[texture, x y z u v nx ny nz × 4]`):

- `"slot:deck"`: drawn with the light outside (the decks' tops under the open sky),
- `"slot:deck:interior"`: drawn with the light in her rooms (tops in the castles' rooms and below decks, undersides).

Each quad has an **owner** and a **facing** slot, in a new top-level map, two ints per quad in the group's order:

```jsonc
"slotQuads": { "slot:deck": [owner0, facing0, owner1, facing1, ...], "slot:deck:interior": [...] }
```

**A quad is drawn while (owner = -1 or the owner is filled) and (facing = -1 or the facing slot is empty).** So:

| the quad | owner | facing |
|---|---|---|
| a cell's top and its underside | the cell | -1 |
| a cell's side toward a neighbouring slot cell (the deck's edge seen in the hole when that one is taken up) | the cell | the neighbour |
| a cell's side toward nothing (open air: a deck's edge at a castle front, a well's edge) | the cell | -1 |
| a cell's side toward fixed planking, a coaming, her side | (none: never seen while the cell is in; leave it out) | |
| fixed geometry a filled cell hides and an empty one shows: the fixed strip's edge facing into the cell, the top of a beam or carling under the cell, a knee's top | -1 | the cell |

Emit both sides of every slot-to-slot edge (A toward B, and B toward A). Then no two coplanar faces are ever drawn: between
two filled cells neither side face is; with one empty, only the filled one's.

- **Cells meet exactly**: cut on the grid lines, which are plank butts (the planks run along the keel, x: a cell is cut
  across them at whole x and along a plank seam at z = k + 0.5). No overlap, no gap, no quad shared between two cells.
- **UVs continuous** across cells (as the merged deck elements had them: from the position on the deck), so a filled deck
  looks as it does today.
- **Paint**: `paintQuads` covers the two groups as any group (each quad's zone: the ceilings' zones on the undersides).
- Quad count: carrack-m's decks are ~1 400 cells; 6 quads a cell + the edges is ~10–12 000 quads, about 2 MB of JSON.

## 4. What the mod does with them (the rules the generator can count on)

- **State per slot**: filled (default), cracked (step D: drawn with damage, still solid), empty. Saved as the differences
  from the design's `start` in WatchedAttributes `magelanSlots` (slot id → state), sent to every client.
- **Taking a cell up** (building mode, an axe in hand, hold left mouse on the deck cell, as a piece is taken up): its
  material into the inventory (all of it or nothing). Refused while anything stands on it (a piece whose place is on that
  cell, or whose boxes rest on it; a design fixture over it; a hatch piece) or it's not a slot (a fixed strip: "this part
  of the deck is fixed").
- **Laying a cell** (building mode, planks in hand, right-click the opening): one planks block of any wood (the cell is
  drawn as the design's planking). Refused while something is in the cell's space (a ladder or a stair through it,
  someone standing in it, a hatch over it).
- **Collision and walking**: an empty cell's voxels are gone from her grid: people fall through, pieces can't stand there
  (a floor place whose cell is empty takes nothing).
- **Openings**: an empty cell not under a shut hatch is a deck opening. A room with one in its ceiling or floor is open
  (unsealed) unless the room on the other side is an enclosed room of the design (as `wells` work today); the rain falls
  through it (her grid); on the main deck it's a downflooding point like an open hatch (the sea over it goes below).
  Stairs and ladders can be put through openings made by taking cells up (their checks are against her grid as it is).
- **Places follow the cells** (0.3.20, step B): her mount points are worked out once with her design's cells and once
  more with every slot laid; the places over her openings that the second finds (floor places, stacked places, borders,
  hooks under a cell) are added with their own ids, and her own places keep theirs. Each place knows its cell (the cell
  under a floor place, a border's anchor cell, the cell a hook hangs under; a well top's panel place the cell it needs
  open): it's usable only while that cell is in (`DeckCells.MountOpen`), on both sides, worked out per place when asked,
  so a cell's change touches nothing else. Ladder-stairs and stairs still go up through openings.
- **Painting** picks only the cells' quads that are drawn; their zones (`paintQuads` on the slot groups) are painted in the
  chunks.
- **Wrecks**: a wreck is laid from her design's cells with her differences (a cell taken up isn't in it; a hatch's
  trapdoors are in her loot, not her cells).
- **Drawing**: per deck, chunks of 8 × 8 cells; a chunk's mesh is built from its cells' quads by the rule of §3 (and her
  paint), and built again only when a cell in it (or next to it, for the facing quads) changes.

## 5. Hatches as pieces

A hatch is a piece laid over a **rectangular opening** of empty cells (1–4 × 1–4 cells, every cell in it an empty slot of
one deck, nothing else of the opening's around it: the rectangle is the whole connected opening). Made with vanilla
trapdoors (building mode, a trapdoor in hand, right-click the opening; one trapdoor a cell):

- `game:trapdoor-solid-*` → a **cargo hatch** (plank leaves, battens under them),
- `game:trapdoor-window-*` → a **glazed hatch** (leaves of glass in leaded frames, the ship's glass).

It lies **flush** with the deck (its leaves' top at the deck's top, 1/8 thick, resting on the opening's edges), hinged on
the opening's two long sides (one leaf for an opening one cell wide), and opens like today's hatches (right-click: the
leaves swing up 100°, the opening is open; shut it's walked on, seals the opening and stops the rain and the sea). A
design ladder with `"gate": "opening"` passes only while it's open.

**Hinges** (0.3.20): optional `"hinge": "x"` (the leaves hinged along x, on the opening's z sides) or `"z"`; without it
along the opening's long sides, **along x when it's square**. The mod checks the leaves' swing (0–100°) against her
collision and her ladders (a ladder whose foot stands on the opening is taken to hang its foot a block over that deck,
`HatchPlanner.HangOver`: draw it so); a player's hatch takes the other hinging if the first one's swing is blocked, a
design's entry with `hinge` doesn't (it's refused: "hatch-no-swing"). `"code"` is allowed as on any entry.

```jsonc
// in modularPieces: "at" = the opening's middle (x, z) at the deck's top (y); "size" = cells along x and along z
{ "kind": "hatch", "at": [7.5, 2.125, 0.0], "size": [3, 3], "block": "game:trapdoor-window-oak-1", "open": false }
```

carrack-m: drop the `hatch-hold` and `hatch-waist` fixtures and their moving parts and glass, and their coamings; make
their openings empty slots on the grid (suggestion: x 6..9 × z −1.5..1.5, 3 × 3 cells, both decks: the ladders at
x 8.27 stay inside), and lay a solid trapdoor hatch over the hold's and a window trapdoor hatch over the waist's.

## 6. What `-- design <code>` checks (offline, for a design with `deckSlots`)

Every slot: its deck exists, its box is on the grid (a whole cell, its top at the deck's top), its solids are solid in
the grid exactly when its start is filled and lie within its box's column, no voxel in two slots, it has quads in the
mesh file (a top and an underside, each owned by it), its quads lie within its box (grown by the planking's edge strips),
neighbouring slots share their edge and both have the facing quads; no `slotQuads` refers to a missing slot. Then: a cell
taken up and laid again round trip; an emptied main-deck cell is a downflooding point and opens the lower deck; a hatch
fits its opening and closes it; the hatch entries fit their openings.

## 7. Gun places (0.3.21, step C: cannons as items)

A **gun place** is where a gun may stand: a port in her side, the deck under its carriage and its run-back, its train. The
gun itself is an item (`magelan:gun-bronze-<side>`, "Bronze gun on its carriage": the block a wreck already gives back),
mounted on a place in building mode and taken up again. A mounted gun is **exactly today's gun**: the place is backed by a
fixture of `"type": "gun"` with everything today's guns have (pos, sel, box, muzzle, out, elevation, limits, rates, trunnion,
pivot, seat, port, wall, lid, housed, gate) and its moving parts (`train:`/`elevate:`/`fire:` + the gun, the trucks, the
breechings, the lid). A design without `gunPlaces` (`carrack`, `carrack-a`, the boats) keeps its fixed guns as before; a
`type: gun` fixture not named by a place is always mounted.

```jsonc
"gunPlaces": [
  {
    "id": "gun-port-1",           // unique, stable across regenerations (saved ships refer to it); suggestion: the gun's code
    "gun": "gun-port-1",          // the `type: gun` fixture this place is (one place per gun fixture)
    "cells": ["main:1:-6", "main:1:-5"],   // optional: the deck slots (ids of `deckSlots`) under the carriage, its run-back
                                  //   and its trail's sweep (MountLayout.GunClear on its deck): each must be filled for a gun
                                  //   to be mounted, and none can be taken up while one is. Fixed cells (not slots) need no
                                  //   listing. (The mod also needs every slot of that deck under the gun's room filled, listed
                                  //   or not: the list is what `-- design` checks against.)
    "start": "laid",              // optional, default "laid": a new ship has its gun mounted; "empty": a place without a gun
                                  //   (castle guns proposed by the generator: she's launched with the port shut, armed later)
    "block": "magelan:gun-bronze-north"    // optional, this default: what mounting takes and taking up gives back
  }
]
```

- **Which parts are the gun's** (hidden while the place is empty): every moving part whose source is `train:`, `elevate:` or
  `fire:` + the gun, every part whose `parent` is one of the gun's, every `follow` part following one of the gun's (the
  breechings, also when anchored at her side). **Not** the lid (`open:` + the gun): it's her side, and stays shut while the
  place is empty. Make the gun wholly of such parts (as now: nothing of the gun in the voxel shapes or the static groups).
- **Its working room** (GunClear: the carriage, the run-back to behind the gun captain's place, the trail's sweep): a
  place that **starts laid** (and a fixed gun) keeps it clear of pieces whether a gun is there or not. **(0.3.24, notes 176,
  181, 182) A place that starts empty doesn't**: its room isn't kept in her layout (no keep-clear; the design's `keepClear`
  boxes named `gun…` over it are ignored), so pieces and stairs may go there. The player chooses: a piece or stair is
  refused in the room while a gun is mounted there (`piece-gun-room`), and a gun can't be mounted while a piece stands in
  its room (`gunplace-blocked`). So **make the castle gun places symmetric on both sides** (`"start": "empty"`) even where a
  stair could stand on one side; only don't lay a default piece in a *laid* place's room. Mirror the guns exactly (z → −z,
  train limits mirrored: a trim on one side's gun is a trim on its mirror). The cells under it: keep them **fixed** as in
  step B, or make them **slots** and list them in `cells` (preferred from now on: a place on a deck whose cells were taken
  up can't take a gun till they're laid again). Either is valid.
- **What the mod does**: an empty place's gun is gone from her collision, the crosshair, her seats (the gun captain's), the
  guns' ticks, laying and firing, broadsides, the shots' targets and her wreck. Mounting (building mode, the gun item in
  hand, right-click the empty place's carriage box): refused while a cell of it is empty, or something stands in its room
  (a piece, a person, a chest). Taking up (building mode, empty hand, Shift + right-click the gun): only cold (not
  reloading), housed (run in, the lid shut; a gun without a lid: not being laid), nobody at it; the item into the
  inventory (else dropped at the player). State saved as differences from `start` in WatchedAttributes `magelanGunPlaces`.
- **Wrecks**: every mounted gun (a place's, or an old design's fixed gun) is laid in the wreck as the gun block, where it
  stood or on the seabed beside her (as since 0.3.5); an empty place gives nothing.
- **Damage (prep for step D)**: shots hit a mounted gun as her collision (as now); a place without its gun is no target
  and no obstacle. The port's lid is her side's (the bulwark over a shut port, as now).
- **carrack-m**: all ten guns become gun places (`id` = the gun's code, `start` laid). **Castle guns** (proposals welcome):
  a place per castle position where a port fits in the castle's side and its room fits on the castle deck (train limits
  shortened where the room is short), each a complete `type: gun` fixture with its port, lid and moving parts, `"start":
  "empty"`. The mod treats them like the waist's.
- **Castle ports may be cut in her collision; the lid seals them while shut** (0.3.22, step C). For her rooms (sealing,
  warmth, a castle room's `RoomEnclosed`/`SealedAt`, the partition walk in `ShipCompartments`) a lidded port (`port` x0 y0
  x1 y1 through her side from the inner to the outer face of `wall`) counts as closed while its lid is shut (the gun
  housed, and always on an empty place) and as an opening of the room the gun stands in while it's open (run out). The sea
  comes in through a castle port only while it's open (a downflood point at its sill, each voxel column across it), as
  through the waist's. `-- design` checks each port in a closed room: sealed with the lids shut, open with one open, with
  the ports as cut and with all of them cut through.
- **Guns on open castle decks** (0.3.23, note 171: "no guns in the cabins"): a place on the quarterdeck, the poop or the
  forecastle (an open deck, its port in a raised bulwark, a lid) works exactly as the waist's. Its port closes no room
  (`PortRoom` is null on an open deck) and the sea through it lands on that deck: no downflood point below for it. Its
  cells (`cells`) are that castle deck's slots under its working room. Drop the great cabin's gun places.
- `-- design <code>` checks (with `gunPlaces`): ids unique, each `gun` a `type: gun` fixture named by one place only, its
  `cells` are slots of its deck under its room (and every slot under its room is listed), the place's gun has its parts,
  the lid isn't one of them; a laid place's cells start filled.

### 7.1 Stern chasers: guns firing aft (0.3.25, note 179)

- **A gun pointing along her length** (`out` mostly ±x: carrack-m's `gun-port-stern`, `gun-starboard-stern`, `out` [-1, 0, 0],
  `yaw` 180) is a stern chaser (a bow chaser with +x). Everything else is as any gun: its moving parts (it trains about
  an upright in the middle of the transom's thickness, `train:`; runs back inboard, i.e. forward, `fire:`; elevates
  `elevate:`), its lid (`open:` + the gun: hinged along its top across her, `axisDir` [0, 0, 1], opening out and up), its
  place (`start: "empty"` as the castles'), its cells (the poop deck's slots under its room).
- **Its port is in the transom**: `port` is **(z0 y0 z1 y1)** (across her, not along her side) and `wall` the transom's
  faces' **x** (inside at the sill, inside at the head, outside at the sill, outside at the head; ship-local, negative at
  the stern). The mod's `ShipCompartments.AlongX`/`PortBox` read it so (the port's box from the transom's outside to its
  inside); the lid shut seals it as a side port's. On the open poop deck it closes no room and is no downflood point.
- **Train**: + turns the muzzle to **starboard** for a stern chaser (`GunLaying.TrainAcross`: +1), to port for a bow
  chaser; the HUD says "trained 5° to starboard" and its limits "to port … to starboard". Limits from her sides inside
  (the generator: `castle_obstacles(stern=True)`).
- **Firing**: every gun is fired by its own gun captain (left-click by it), the stern chasers too. The broadside command
  (`/magelan fire port|starboard`, `FireBroadside(±1)`) is the guns out of that side only; `/magelan fire stern` fires the
  stern chasers, `all` (or `FireBroadside(0)`) everything.
- **Guns in each other's way** (0.3.25): a gun can't be mounted while another mounted gun on the same deck stands in its
  way — either one's carriage (its `box`, else `sel`) inside the other's working room (the trail's sweep slices of
  `MountLayout.GunRoom`) — refused `gunplace-gun-room`; `/magelan gunplaces` says which one is in the way. carrack-m: each
  stern chaser and the poop's aftmost broadside guns on its side (`poop-4` at −16.75 and `poop-1` at −15) — one or the
  other (a gun is shifted aft to chase, as at sea). No pair of guns laid as designed clashes, nor the two chasers; guns
  abreast that only share the crews' room behind them don't count (the generator prints the pairs: `gun_exclusive`).

## 8. Not yet (later steps)

Ports and lids, partitions and railings as slots, deck cells taking damage and places bound to hull panels (step D2),
material costs as a launch requirement (step E). Floor places for furniture on cells filled in where the design had an
opening (the design's places are worked out from the design: an opening laid over later takes pieces only after the
design lists it filled).

## 9. Hull panels (0.3.26, step D1)

Her hull's planking, outside and in, is **panels**: each a frame space (from one frame's strip to the next) by a band of
strakes on one side. Each frame keeps a **strip** of planking 0.25 either side of its middle (`frames.x0 + space·k ± 0.25`,
on the voxel grid); a panel runs strip to strip (1.5 long on carrack-m). Fixed strakes on quarter-block lines: the waterway
at every deck, the wales, the strake under each rail cap (carrack-m, design y: 0.25..0.5, 2.75..3.5 (the two wales and the
main deck's waterway), 4.5..4.75, 6.0..6.75 (the castles' waterway and their mouldings), 7.5..7.75, 9.25..9.5,
10.5..10.75); her flat floor (the hold's floor is its inside) and her keel stay fixed. A panel's band seams are strake seams
on quarter-block lines (her strakes are spaced again to them), so its drawn edges and its collision voxels meet.

**Only whole panels are slots.** Fixed (not listed, the generator counts why): a frame space that isn't whole (her ends:
the stem, the sternpost, the transom, and where her lines curve hard), cut by an opening (a gun port, a window, the entry
port, a scupper), a castle break's sweep, a moulding over it, under the forecastle's overhang, its inside in two rooms, any
mesh of hers within 0.3 of its outside or its ceiling but its own planking (channels, chains, ladders, the gallows, lids) or
a fixture's box (a pin rail, a gate, a hearth...), no collision voxels of its own. Places (chests, lanterns, pieces) don't
make a panel fixed (step D2 binds them).

### 9.1 The definition (`config/ships/<code>.json`)

```jsonc
"hullSlots": [
  {
    "id": "hull:s:12:3",            // unique, stable: hull:<p|s>:<station>:<band>; saved ships refer to it
    "side": 1, "station": 12, "band": 3,   // -1 port / +1 starboard; ShipPartMap.StationAt of its middle; its band from
                                    //   the keel up (0 the lowest; hullBands lists them)
    "box": [x0, y0, z0, x1, y1, z1],         // its shell: the outside planking to the ceiling, every solid of it
    "outer": [[x, y, z], ...],      // its outside's four corners: aft-low, fwd-low, fwd-high, aft-high
    "inner": [[x, y, z], ...],      // its ceiling's (null: none of its own)
    "normal": [nx, ny, nz],         // outward: the outside's mean
    "area": 1.62,                   // its outside (square blocks)
    "sill": -1.875, "head": -0.875, // its outside's lowest and highest y
    "room": "hold",                 // the design room its ceiling faces (null: an open deck's bulwark)
    "solids": [[...], ...],         // the collision voxels it owns (greedy-merged boxes): her shell's cells between its
                                    //   strips' edges and inside its band (never a deck cell's: no voxel in two slots)
    "material": { "block": "game:planks-oak-ud", "count": 3 },   // about its outside's and ceiling's area; any wood's planks
    "start": "filled"               // optional; "empty" for step E (a hull built up on her frames)
  }
],
"hullBands": [ { "band": 0, "y0": -4.125, "y1": -3.125 }, ..., { "fixed": "waterway", "y0": -1.125, "y1": -0.875 }, ... ]
```

The slot index is the entry's place in `hullSlots`; it's also its damage part's index (her hull's parts come first in
`DamageLayout`: part i is panel i, and her fixed planking is her structure). A panel's neighbours are the bands under and
over it in its frame space whose edge is its edge (no fixed strake between); across a strip isn't a neighbour. Her saved
states (`magelanSlots`) hold both families' differences (deck cell ids and hull panel ids don't clash).

### 9.2 The geometry (`config/shipmeshes/<code>.json`)

Four groups, quads as every group's, each quad's owner and facing panel in `slotQuads` (§3's rule: drawn while (owner -1
or filled) and (facing -1 or empty)):

- `slot:hull`: the outside planking over the copper line, the bulwarks' insides under the sky, the reveals (lit outside);
- `slot:hull:bottom` / `slot:hull:bottomclad`: under the copper line, tarred / copper (the mod draws one of the two by `ship.Clad`);
- `slot:hull:interior`: the ceilings in her rooms' light.

| quad | owner | facing |
|---|---|---|
| a panel's outside planking (all the strakes of its band, cut square at its strips' edges) | the panel | -1 |
| its ceiling | the panel | -1 |
| its edge along a seam toward the panel over (under) it, outside to ceiling | the panel | the neighbour |
| a frame strip's face toward it (each strake, outside to ceiling); a fixed strake's or the floor's face toward it | -1 | the panel |

All in, she's drawn as before (the planking only cut at the strips' edges and the seams; no reveal drawn). Where a seam is a
deck's top or underside the fixed face starts 0.023 out from the ceiling (the deck's edge strip runs 0.02 into the
planking there). `paintQuads` covers the four groups; the reveals are bare oak, in no zone. carrack-m's mesh has no
`hullOut`/`hullIn` (the old jagged holes are the archived carracks').

### 9.3 What the mod does with them

- **State** per panel: in, cracked (drawn with the damage overlay, still solid), out. On the server it's always what its
  damage part's hurt says: Holed (0.7) and over out, Cracked (0.35) and over cracked, else in.
- **Damage**: a blow is shared among the panels within its radius (`hullToughness` 80 kJ: 28 kJ cracks one, 56 stoves it
  in); a shot goes on through a panel it has stove in (its opening), and her fixed planking (a strip, a wale) is her
  structure (it takes 30 kJ, the shot goes on); a broad blow (grounding, ramming) with no panel near: the nearest within her
  half beam. Out, a panel's voxels are gone from her collision: shots, rays, people and the rain go through.
- **The sea**: a panel out under her main deck is an opening of `damage.openLeak` (0.2 m²) for a 1.5 m² panel once the sea
  is over its head (0.75 t/s at 2 deep), a weir over its sill while the sea's between; cracked it leaks as cracked planking
  (about a litre a second); its seams wear as before. Over her main deck it's a downflooding point at its sill ("hull:" +
  its index).
- **Rooms**: a panel out opens its `room` (warmth, rain, sealing).
- **Repairs**: cracked: the repair tool (planks and nails, held right-click). Out: laid again in building mode with planks
  in hand (right-click the opening; `material.count` planks; under her waterline only from aboard, or by a diver). Taken
  out: building mode, an axe, left mouse held 1.5 s (its planks back); refused under her waterline afloat, or with a piece
  or something put aboard against it. `/magelan hull [take|lay|crack|reset] <id>`.
- **Saves**: `magelanDmgF` "v": 2 (hull codes: side, station, band of strakes). An older save of a design with hull panels
  has its hull parts' damage moved onto the panels where they were (the most overlapped gets its hurt, the others at most
  cracked).
- **Wrecks**: a panel out isn't in her wreck; broken up, 2-4 whole panels between her bilge and her main deck are out.
- **Drawing**: chunks of a side's two frame spaces, built again where a panel's state or its damage's look changed (every
  half second at most), her paint on them.

### 9.4 Mending an opening (0.3.27)

The user: "made holes in ship but cannot repair — does not give that option"; note 209: "says that to repair is needed
planks but actually boards". An opening (a hull panel out, a deck cell taken up) is laid again **out of building mode
too**, with **planks blocks or boards** (vanilla's `game:plank-*` items, `ShipRepairs.BoardsPerPlanks` = 4 to a block, as
vanilla's 2 × 2 recipe): right-click the opening with them in hand (the crosshair's pick: from inside her, from the water
outside, from her deck at a bulwark's), or hold right-click on it as any mending (the repair job "lay": its kits
`material.count` planks blocks, or 4 × that in boards, taken from the hand and then the inventory). The look-at text says
"Stove in: lay it again with N planks blocks or 4N boards". Taking a panel or a cell out is still building mode's (an axe).
The repair texts call the boards boards (`item-plank`) and the blocks planks blocks (`item-planksblock`).

## 10. Beam segments: trimmed beams under wells (0.3.27)

The user (notes 211, 222): walked stairs need a 3-block opening and carrack-m's deck beams are every 2 blocks, so a fore and
aft well always crosses a beam: "rebuild with 3-block rib spacing, or make partial beam removal possible?" The frames stay
every 2; a beam under a well comes out in **lengths a cell wide**, and a stair across her fits between two beams.

- **A beam's collision** (carrack-m, `BEAM_SEGMENTS`): the one voxel column **aft** of its frame's line (`xc - 0.25 .. xc`),
  its depth (BEAM_D 0.5; the hold's 0.75), not the two columns either side. The cell forward of a beam (`xc .. xc + 1`) is
  free of it under the deck: a stair across her (along z, its well three cells along z in that column) goes up between
  two beams with nothing cut (note 222). The beam is drawn centred on its line as before (0.23 wide).
- **The definition**: beam segments are `deckSlots` entries with `"kind": "beam"`:

```jsonc
{ "id": "beam:main:4:2", "deck": "main", "kind": "beam", "beam": "main:4",      // the beam: its deck and its line's x
  "beside": ["main:3:2", "main:4:2"],                                              // the deck cells aft and forward of it
  "box": [3.75, 1.375, 2.5, 4.0, 1.875, 3.5],                                      // its collision: the voxel column, a cell's width
  "material": { "block": "game:planks-oak-ud", "count": 1 } }
```

  They share the deck cells' states, saving (`magelanSlots`), draw rule and chunks (a segment's quads are in its first
  beside cell's chunk); the mod keeps them out of everything that's about a deck's planking (openings, rooms, downflooding,
  hatches, gun places' cells, places). Which beams have segments (the generator, `beam_segments`): a beam drawn by
  `deck_beam` with a deck (main, lower, quarter, forecastle, poop; not a castle deck's edge beam), at each cell's width
  across her where both cells beside it are deck slots, its collision whole there, the beams two blocks aft and forward
  whole over that width and a quarter more each side, and no fitting on it (anything but skeleton: hooks, a mast's
  coat...). carrack-m: see the backlog for the count.
- **The mesh**: the beam's timber is split at its segments, each segment's faces owned by it (`slotQuads` owner = the
  segment, facing -1; no faces where two pieces meet). For each segment two **carlings**, a beam's section (0.23 × its
  depth), along the opening's sides from the whole beam aft to the whole beam forward (0.005 into each), on the side of
  the next length of the beam: owner = that length if it's a segment (else -1), facing = this segment: drawn while this
  one is out and the next is in. Where it passes through the beam's next length only its face toward the opening is
  drawn. Two lengths of one beam out: the carlings at the opening's outer edges. Group: "slot:deck:interior" (or
  "slot:deck" outside), as the cells'.
- **The rules** (`BeamRules`, the ship's `BeamTakeProblem`): out only with **both cells beside it open**; at most **two
  lengths of one beam** out, next to each other; not with a piece hung on it or standing in it (a partition under it, a
  lantern, a stair) or a fixture of hers. Taken out: an axe in building mode (left mouse held), planks back. **By itself
  with a stair**: a stair or ladder-stair put through a well that a beam's length is in the way of takes it out (its planks
  to the player) when the rules allow; the hint says so ("the deck beam in its way comes out with it"). Laid back: planks
  or boards, right-click it (not while a stair's solids, its picked boxes or the room a man climbing it needs are in it). Refused stair: "a deck beam is in the way: open the deck
  on both sides of it and put the stair here again" or, for a beam with no lengths there, "put the stair elsewhere, or
  across her".
- `-- design` and `-- notes0327` check them; meshcheck `--slots wells` draws random wells with their beams out.
- **A rib cell's beam (22 October 2026, the caravel's notes 51, 52).** The block-grid ships carry a deck's beam in the
  middle of each rib cell (the caravel's main deck: x `r + 0.25 .. r + 0.75` for rib cell `r`, 0.5 deep under the
  planking's voxel, `y` deck − 0.75 .. deck − 0.25), under one deck cell rather than on a line between two. A segment
  of such a beam names **one** cell in `beside`: the cell over it, and it comes out only with that cell open. Its
  `box` is the beam's voxel columns within that cell (a cell's width along the beam):

```jsonc
{ "id": "beam:main:-2:-1", "deck": "main", "kind": "beam", "beam": "main:-2",
  "beside": ["main:-2:-1"],
  "box": [-1.75, 0.375, -0.5, -1.25, 0.875, 0.5],
  "material": { "block": "game:planks-oak-ud", "count": 1 } }
```

  Why: along her a stair's room needs the cell before its foot, and with ribs every other cell that cell is a rib
  cell, its beam over the walker's head (the short stair steps 0.75 up its first quarter: his head 2.6 over the floor
  where the beam's underside is 2.25). Across her the cells between ribs are clear, and the stairs go up there with
  nothing cut. The mesh as above: the segment's quads owned by it; the carlings along the opening's sides from the
  beam aft to the beam forward. Anything else of hers under that cell (a hatchway's carling: the caravel's old hold
  hatch, x −6 .. −3.25 at z −0.5 .. −0.25 under `main:-4:-1`) stays fixed and still refuses a stair there.
- **The beam refusal says what is really in the way (notes 51, 52).** A stair whose room clashes is told "a beam" only if
  something is still in its way with the deck's cells over it taken up (`BeamRules.ClashSays`); then the fixed one if
  something fixed remains with the beam's lengths out as well. Before, any clash just under a deck was "a beam that can't
  come out", so a stair across her between two beams, under her deck still laid, was told to go across her.


## 11. Step D2 (0.3.28): one-block hull panels, port covers, deck cells taking shots

### 11.1 Hull panels in halves (D2.1, D2.2)

carrack-m's frames stay 2 apart, but each frame space is **two panels**, each a block long: `"half": "a"` from the frame's
middle to the space's middle, `"f"` from there to the next frame's middle. Ids `hull:<p|s>:<station>:<band>:<a|f>` (§9.1's
plus the half; `station` the frame space's, the same for both). No strips: the planking over a frame is split on the frame's
middle, each half its panel's. Each half's quads as §9.2; its edge toward the other half of its space a pair (each its own,
facing the other); at its frame the **rib**: faces across her side's thickness 0.11 from the frame's middle (owner -1,
facing the panel), and the rib's outside and inside faces over that half's 0.11 (the same rule): two panels out either
side of a frame leave only the rib. Collision: the "a" half owns its shell from the frame's line to the space's middle, the
"f" half from there to a quarter short of the next frame; the voxel column aft of each frame's line is fixed (the rib).
Damage codes: the fore half's hull code has bit 17 set (`ShipPart.Hull(side, station, band, half)`); an older save
(magelanDmgF "v" under 4) puts a whole panel's hurt on both halves.

### 11.2 Port covers (D2.4, notes 212, 223)

A castle side port can be **planked over**: a deck slot of kind `"port"` over its hole.

```jsonc
{ "id": "port:gun-port-poop-1", "deck": "poop", "kind": "port", "gun": "gun-port-poop-1",
  "box": [x0, sill, z0, x1, lintel, z1],          // the hole through her side
  "solids": [[...]],                                 // the voxels the port's cut took out of her shell
  "material": { "block": "game:planks-oak-ud", "count": 1 }, "start": "filled" }    // filled: planked over; empty: a port
```

and its gun place names it: `"port": "port:gun-port-poop-1"`. Its quads (owner it, facing -1) are the planking over the
hole, flush with her side outside and with her bulwark's inside ("slot:deck" groups). Filled: no lid (the gun's `open:`
part not drawn), no gun can be mounted (`gunplace-port-covered`); empty: the lidded port as before. It shares the deck cells'
states, saving and drawing (a chunk of its own) and nothing else of theirs (not a cell: no openings, rooms, places,
damage). Taken out with an axe in building mode (its planks back); planked over with planks or boards (refused with a gun
mounted, `port-has-gun`, or its lid open). A gun mounted on a place whose cover is in (a ship saved before covers) opens
its port. carrack-m: 16 covers, the poop's six start filled.

### 11.3 Deck cells and pieces take shots (D2.5)

Her plain deck cells (deckSlots without a kind) are damage parts of their own, as her hull panels are: 80 kJ each
(`damage.cellToughness`), cracked at 0.35 (drawn with the cracked overlay, still solid) and stove in at 0.7 (out of her
collision: people fall through, on the main deck a downflooding point); a shot goes on through a cell it stove in. A cell
with something on it (a laid gun, a piece, a fixture) stops at cracked; a cell that's out stays out until it's laid again
(laying or taking it up leaves it sound); beam lengths and port covers stay her structure. A partition, a door, a window or
a railing in a shot's way takes what it can (30/40/15/20 kJ): cracked it's drawn cracked and mended with boards and nails
(held right-click); shot away it's gone (its planks splinters, the shot goes on), unless something hangs on it (then it
stops at cracked). A cracked cell is mended with the repair tool (2 boards, 1 nails); a stove-in one is laid again with
planks blocks or boards as any opening; the crosshair names it ("Main deck planking, amidships"). Saves: magelanDmgF "v" 3;
cell codes are a part kind of their own (deck, cell i, k); a piece's hurt is kept with the piece; an older save's per-station
deck hurt cracks the nearest cell (never more than cracked), the rest is dropped.


## 12. Castle ports: a board and a port (0.3.29)

The user on 0.3.28's covers: "gun ports now are placed and taking off inner planks reveal them. I thought removable side
board as with hull and then we place port inside it." Each castle side port's place in her side (the quarterdeck's three a
side, the poop's three, the forecastle's two) is two deck slots now:

```jsonc
{ "id": "port:gun-port-poop-1", "kind": "port", "gun": "gun-port-poop-1", ... }        // the board over its hole (0.3.28's cover):
                                                                                       //   filled, boarded over; its voxels the hole's
{ "id": "gunport:gun-port-poop-1", "kind": "gunport", "gun": "gun-port-poop-1",
  "cover": "port:gun-port-poop-1", "box": [...], "solids": [],                          // the port itself: filled, the lidded port is in
  "material": { "block": "game:trapdoor-solid-oak-1", "count": 1 }, "start": "empty" }  //   (its lid, its gun place); no voxels
```

and its gun place names both (`"port"`: the board, `"gunport"`: the port). The three states: **boarded over** (board in,
no port: her side whole there, no lid), **an opening** (both out: a hole), **a port** (the board out, the port in: the lid,
a gun can be mounted). In building mode an axe takes the board out (its planks back) or the port out (its trapdoor back;
not with a gun on it or its lid open); a **trapdoor** (any vanilla trapdoor) puts a port into an opening; planks or boards
board an opening over (not with the port in it: `port-in-there`). A gun needs the port in (`gunplace-no-port`; boarded
over: `gunplace-port-covered`); without it the lid isn't drawn. The poop's start boarded over, the quarterdeck's and the
forecastle's with their ports in. (The castle ports' frame spaces couldn't all be her side's ordinary panels: the castle
breaks' sweeps, the poop's moulding and the forecastle's stepped-out sides keep those panels fixed, so the board is the
port-sized one 0.3.28 had.)

A ship saved before 0.3.29 (layout revision 0, docs/modular-pieces.md §14) has a port in wherever its board is out (0.3.28's
"open" port); a gun saved where no port is in gets its port (and its board out).

## 13. Her side in two layers (0.3.29, step D2.3)

The user: "maybe thick hull is just two panel blocks in thickness placed. in open decks just one placed", and "this also
would count to hull damage resistance and damage mechanics". Under her weather decks (the hold, the lower deck, the great
cabin and the poop cabin, the forecastle: below each open deck's underside, `weather_deck` in the generator) her side is
**two layers of panels**: her planking outside (`hull:<p|s>:<station>:<band>:<a|f>`, `"layer": "outer"`) and a **ceiling**
inside the frames (`ceil:<p|s>:<station>:<band>:<a|f>`, `"layer": "inner"`), each naming the other (`"partner"`), the
same size and place. The ceiling is LINING (0.25) further in than her side's inside was (the rooms 0.25 narrower a side;
eased in over the hold's floor), the frames (ribs) RIB_SIDED (0.5) wide between the two, the inside smooth (no riders, no
inlays: the knees under the beams against the ceiling). Over her open decks (the bulwarks) one layer as before, the frames
standing inside them. Where the weather deck steps (x -9, -2, 14) a face closes the ceiling's step; at a weather deck's
underside the deck's own underside covers it.

- **Collision**: across her side the innermost voxel is the ceiling's, the rest the planking's (the space between the frames
  with it); the frames' two columns (their line +- 0.25) are hers.
- **Drawing** (`slotQuads` as §9.2): each layer's own face (outside / ceiling), its **back** (PLANK_T in: owner it, facing
  the other layer: drawn while it's in and the other's out), its edges across its own thickness toward its neighbours (the
  other half, the panel across the frame, the bands under and over it; else her fixed planking's face toward it); the space
  between the layers walled always (the rib's side and faces, the other half's side, the seams under and over it, a hair
  inside it): through an opening only that space is seen. One out: the ribs and the other layer's back; both: a hole.
- **Damage** (`DamageDef`): the ceiling takes `ceilToughness` 40 kJ (the planking `hullToughness` 80). A blow on the
  planking with its ceiling in shares `ceilBacking` 0.3 of it with the ceiling (each up to what it has left; a shot goes on
  with the rest, through the ceiling's own voxels next): a lined spot cracks at 40 kJ, holes at 80 (one layer: 28, 56), a
  9-pounder's 145 kJ holes both and goes on; the ceiling out, the planking takes it all. A grounding or scraping blow (not
  a shot) doesn't reach a ceiling while its planking is sound (what it would take is only absorbed). Both layers go sound,
  cracked, out and are mended alike (the repair tool; laid with planks or boards); taken out with an axe (a ceiling below
  her waterline too while her planking's in front of it).
- **The sea** (`linedLeak` 0.5): a planking panel out behind its ceiling lets in half (the ceiling slows it); both out, all
  of it; a ceiling out alone nothing. A room is opened (warmth, rain, sealing) only where both are out. Over her main deck
  (the castles' cabins' sides) the planking's opening is the downflooding point.
- **Saves**: a ceiling's damage code has bit 18 set (`ShipPart.Hull(..., inner: true)`); a ship saved before has every
  ceiling in and sound (no state or damage of hers names one; old hull damage goes onto her planking only).

## 14. 0.3.30: fixed ports again; shots crack or knock out slots; plugging from inside

- **Ports** (note 239: "remove placeable ports. they don't work at every location, textures does not align"): §11.2 and §12
  are dropped. carrack-m has no deck slots of kind `port` or `gunport` any more and her gun places name none
  (`port`/`gunport` null): every gun place's port is cut in her side and lidded, fixed, as the waist's. The poop's side ports
  are gone altogether (note 236): her poop's bulwark is plain both sides, no lid, no gun place (the stern chasers through the
  transom stay). The mod's port-slot code stays for a design that has them (none now). Saves: a 0.3.28/0.3.29 ship's
  `port:`/`gunport:` slot states are dropped as she loads (their ids are gone), so a port that was in is the fixed port, a
  quarterdeck or forecastle port boarded over is the fixed port again, the poop's is plain bulwark; a gun saved on a poop
  place goes into her hold (`RescueOrphanedGuns`).
- **Ports square to her side** (note 237): where her side runs aslant (the bow: the forecastle's ports, the waist's
  forward one) a port's jambs are square to the side board, so its inside opening is HULL_T·sin(side angle) aft of the
  outside's (the cut's `x0in`/`x1in`, the reveals, the hole's voxels from one to the other), and the gun stands along the
  side's normal there (its fixture's `out`, `yaw`, `box`, `sel` turned with it): its train 0 is the board's perpendicular.
  Each gun's limits are swept (generator `sweep_gun_limits`): its barrel (gun.PROFILE's outer radius) and carriage (boxes)
  against points over every surface of hers near it (every quad but the moving parts', 0.02 apart), trained, elevated and
  run back; the fixture's `trainBound` says what stops each way ("hull", "lid", a neighbour, its room, a gate, a stair, its
  breeching), `sweepElevations` the elevations it's checked at; the design's `gunSweep` the model for the offline test.
- **Shots** (note 234: "my mind we had idea that panel has damaged texture and then removed. Nothing about holes from
  cannon"; "a ball can go through a panel and damage the panels next to it"): on a design with slots a shot leaves only two
  marks on a hull panel or a deck cell: **cracked** (its damaged overlay; mended with the repair tool) or **out** (laid again
  with planks or boards). Its hurt on a slot is at least `cracked` (no scraped panel from a shot, no seams worn by it).
  Her fixed planking struck (a frame's strip, a wale, a cap): the nearest slot within `routeReach` (0.9) takes it. The slots
  round where it strikes, within `splashReach` (0.8), take `splashShare` (0.3) of its energy between them as hurt (not lost
  to the shot), cracked or knocked out the same way; the ball goes on through what it holed as before (the energy model).
  **No scars** (0.3.27's ShipScars): a save's scars become cracked slots as she loads (the nearest slot to each), then none
  are kept.
- **Plugging from inside** (the user: "repairing missing hull elements from inside plugs only inner layer"): laying a
  ceiling whose planking behind it is out lays the planking first, then the ceiling, from the one right-click (or the repair
  tool's held click) if the planks in hand go round; a ceiling that's in with its planking behind it out (or cracked): its
  spot is the planking's (the crosshair says "Her planking behind it is out: the sea comes in here"; planks right-click lay
  it through the ceiling; the repair tool mends a cracked one).
