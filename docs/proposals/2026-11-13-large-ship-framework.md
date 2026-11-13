# A framework for large ships: review and proposal

Status: **proposal, nothing here is agreed** (13-14 November 2026, backlog SH-0014, reviewed with Fable 5.2). It
reviews how the ships are built today and proposes a framework for the caravel, a nao, Magelan's carrack (SH-0015)
and a cog. Sections 1 and 2 describe what is **implemented**; section 3 is the review's judgment; everything from
section 4 on is **proposal** unless it says otherwise. The decisions the author must make are numbered in section
13. Until the author decides, the construction rules in `AGENTS.md`, section 8 of
`docs/proposals/ship-design-architecture.md` and suite decision D17 stand.

## 0. The verdict in short

- **The game side is mostly sound. The way a ship is described and generated is not.** The runtime (`src/Ship`,
  about 64,000 lines of C#) reads ships from data and has almost no ship-specific code. What goes wrong is
  everything before it.
- **There is no ship framework today, only two hand-tuned ships.** The caravel comes from five caravel-only scripts
  (about 5,500 lines of Python) driving AstoCAD. Magelan's carrack comes from one carrack-only script of 11,343
  lines. Neither has a design file, a parts library or named structural members. A third system, the C# construction
  core (`src/Construction`), does describe ships as data, but it is not connected to the game and its faceted
  hull form cannot describe the caravel.
- **The author's suspicion is right about the pipeline, less so about the ship in the game.** Through about 150 chalk
  notes the caravel in the game has drifted to a workable model: every cell of her planking, every rib cell and
  every beam length is a separate removable piece. But that model exists only as an export convention squeezed into
  Magelan's panel format. Nothing upstream knows what a frame, a strake or a deck beam is, so each ship must be
  hand-coded again.
- **AstoCAD is on the critical path and adds almost nothing.** A short numpy script written for this review
  (section 1.6) rebuilds the caravel's hull from her committed lines file in 0.2 seconds: the displacement matches
  the CAD build's to 0.03 % and 439 of 526 planking cells match the CAD slice within 2 %. The remaining cells are
  the gun ports, gates and slivers it does not cut. The CAD chain takes about 42 minutes for the same data.
- **Recommendation: a parametric shipwright with named members.**
  - Each ship is a small design file plus overrides, built on a shared library of member types and components
    (guns, pumps, rigs).
  - One generator builds every ship in the order a shipyard does: proportions, master frame and moulds, backbone,
    frames, planking and wales, clamps and beams, decks, castles, masts and rig, fittings.
  - Every timber is a **named member** with a type, material, building stage and supports. Members are cut into
    pieces by the block cells; the game keeps state per piece, addressed by cell.
  - Damage, repair, grey guides, the charter's bill of materials and, later, staged building in game all read that
    one model.
  - No CAD in the build. A hull shape may still be drawn in AstoCAD and sampled in.
- **The building-sequence idea is the right backbone, as a construction order, not as a simulation of carpentry.**
  The Iberian shipwrights' own method was parametric (section 6). One ordered list of stages can drive the generator
  offline and staged shipbuilding in game. Vanilla's boat already works this way.
- **A lot survives:** nearly all of the runtime, the half-breadth field method, the caravel's lines and layout, the
  gun and fitting geometry, the checks and viewers. What is retired is the CAD build, the slicer, the caravel's
  exporter, Magelan's generator (after its helpers are extracted) and `GridShips.cs`.
- **For the carrack (SH-0015): do not copy `tools/caravel`.** That would make a third ship-specific script set. Its
  data can be gathered into a design file now; its build should wait for the generator.

## 1. What exists today (implemented)

### 1.1 Three geometry systems, one of them live

| System | Where | Describes | In the game? |
|---|---|---|---|
| Magelan's generator | `tools/shipgen/carrack.py` (11,343 lines in Magelan, 11,552 here), `shipgen.py`, `gun.py`, `planking.py` | carrack, carrack-a, carrack-m, hansa-carrack's assets | Carrack assets only, left out of the Release build (`Hansa.csproj`, `CarrackAssets`) |
| AstoCAD master model + slicer | `tools/caravel/*`, `tools/master/*`, `models/caravel/caravel.FCStd`, `models/hansa-carrack/hansa-carrack.FCStd` | the caravel (live); the carrack's plain hull v1 (stopped before slicing) | **The caravel: yes, the only shipped ship** |
| Construction core | `src/Construction` (1,945 lines), `assets/ships/config/shipdesigns/{two-bay,carrack}.json`, `config/construction/kinds.json` | faceted forms, one block per cell, kinds, support graph, guides | No. `GridShips.cs` could graft it onto carrack-m's rig, but the only design with a `game` section has `load: false` |

### 1.2 The caravel's pipeline

| Step | Script (lines) | Runs in | Output | Time |
|---|---|---|---|---|
| Lines | `tools/caravel/caravel_lines.py` (954) | Python, numpy | `models/caravel/caravel-lines.json` (833 KB, committed) | seconds |
| Model | `build_caravel.py` (1,297) | AstoCAD | `caravel.FCStd` (6.8 MB), `caravel-report.json` | 23 min on new lines, 2-4 min cached |
| Slice | `slice_caravel.py` (158) | AstoCAD | `cells.json` (117 KB, committed), `cells.brep` (50 MB, ignored) | 7 min |
| Export | `export_caravel.py` (1,691) | AstoCAD | `models/caravel/export/*-base.json` (37 MB, ignored) | 12 min fresh, 2 cached |
| Layout | `caravel_layout.py` (1,381), `caravel_fittings.py`, `caravel_gun.py` | Python | `assets/ships/config/ships/caravel.json` (9.4 MB), `config/shipmeshes/caravel.json` (30 MB) | seconds |
| By hand | | | entity, charter, lang lines | |

What this means in practice:

- **The hull's real source is Python, not the CAD model.** `caravel_lines.py` holds the midship section as a table
  (`SEC_Z`/`SEC_B`, lines 194-195), the stem as a table, the stern and transom as formulas, the sheer, and the
  waterline fullness as superellipse exponents (`bow_q`, `run_q`). It even computes the planking's inner surface
  itself, by eroding the half-breadth field with a ball (lines 419-437), and the rib sections. AstoCAD then lofts
  those polygons, cuts them and measures them.
- **The CAD file is a write-only output.** `caravel-design.md` says so: "the spline waterlines in the file do not
  drive them. The hull is edited in `caravel_lines.py` and rebuilt." The 17 October plan (`docs/master-model.md`,
  section 1) was a master model the author edits in AstoCAD. For the caravel that never happened: every change
  since came as a chalk note, a chat message or a sketch, and was made in the scripts.
- **The layout is code.** Ribs `range(-14, 7, 2)`, masts, gun cells, stairs, hatch, cabin, galley and fittings are
  module constants in `caravel_lines.py`, in variants A-D (`LAYOUTS`) plus `if VARIANT == "D"` branches. Rig
  points are in `build_caravel.py` (`SKETCH`, `RIG_P`), cleats and shroud stations in `caravel_layout.py`.
- **The structure is five lumped bodies.** `caravel.FCStd` has `Shell`, `Backbone`, `Ribs` (18 solids in one
  compound), `Beams` (20) and `Decks`. No member has a name. There are no wales, clamps, keelson, stringers or
  knees. Carlings are boxes added to `Beams`.
- **The slice records overlaps.** A `cells.json` record is
  `{"cell": [i,j,k], "pieces": [{"body": "Shell", "litres": 85.36, "index": 0}, ...]}`, with no kind, material or
  flags. The geometry lives only in the ignored `cells.brep`. Of 670 cells, 308 hold one body, 264 two, 65 three and
  33 four. 76 pieces under 4 litres were moved into a neighbour cell (`SLIVER_M3`, lines 113-133) and 17 had nowhere
  to go. `docs/master-model.md` section 7 forbids that move ("a piece is never given to a neighbouring cell").
- **Slow, and fragile to reproduce.**
  - The full chain is about 42 minutes on new lines.
  - A fresh clone cannot run the seconds-long layout step until the ignored `export/` base and `cells.brep` exist.
  - The caches in `%TEMP%\caravel-cache` are keyed on inputs but not on the code or the AstoCAD version.
  - AstoCAD's booleans failed *silently* on spline lofts, so the hull is a ruled loft through polygons
    (`caravel-design.md`, "Faceting").
- **Helpers are repeated.** Half-breadth lookups exist five times (`caravel_lines`, `build_caravel`,
  `export_caravel`, `caravel_layout`, `rig_study`), the model-to-game transform three times, `transom_x` twice with
  different methods. Every script has `models/caravel` and `C:/VS mods/Hansa` in it, the "generic" slicer included.
- **Determinism is good where it was worked on.** There is no randomness, a fresh and a cached export are byte for
  byte the same, and `model_fingerprint.py` / `compare_builds.py` exist. No automated golden test runs them.
- **The design record is a diary.** `caravel-design.md` is 1,074 lines of trial rounds and chalk notes 11-158. The
  current state of a detail has to be dug out of the history. Some of it is stale (it says 565 cells; there are 670).

### 1.3 What the caravel is in the game

The export turns the slice into Magelan's slot format, and since 31 October (chalk note 148, commit `28db557`) it
goes further than any doc or decision says:

| Piece | Count | Id | Removable | Damage |
|---|---|---|---|---|
| Hull planking in one cell | 462 | `hull:<p\|s>:<station>:<band>:<half>`, plus a `cell` field | yes, 4 boards | sound, cracked, holed; leaks |
| Rib in one cell | 197 | `rib:<i>:<j>:<k>` (the cell) | yes, 1 support beam | none: in or out only |
| Deck beam length | 31 | `beam:main:<x>:<z>` | yes, under an open deck cell | none |
| Deck cell | 91 | `main:<x>:<z>` | yes, planks | takes shots |
| Keel, stem, sternpost | fixed | none | no | no |
| Masts, sails, rigging, rudder | | damage parts | no | yes |

- So a cell of the caravel already holds **several separately removable pieces**: a plank panel "owns the shell's
  voxels in its cell that no other body shares (a rib crossing it stays when it is out)" (`caravel-design.md`, third
  trial). That is not "one master block per cell" (D17, decision 6). The author's play-testing replaced that rule in
  practice, and no decision records it.
- **The hull panel id is a forced fit.** It is Magelan's frame-space key. Where a cell is not a side panel (bottom,
  counter, transom), the station is made up: `station = 64 + (ci + 16) * 7 + key[3] + 3`
  (`export_caravel.py:1320`; line 406 calls it "only an encoding key").
- **What a rib piece supports is not modelled.** `caravel-design.md`, note 148: "individual rib damage states / a
  complete deck-collapse model" are "still outside this implementation".

### 1.4 The runtime

- **Data-driven.** `HansaMod.AssetsLoaded` loads every `config/ships/*.json` into `ShipDefinition`
  (`src/Ship/ShipDefinition.cs`), and `Init()` (lines 158-225) derives the rest: the collision `VoxelGrid` (0.25
  block), dry masks, slot layouts, damage parts, mount places, gun maps. "caravel" appears in C# only as
  `DefaultShip` (`MagelanMod.cs`).
- **Few hard-coded names remain:**
  - pennants by mast code `main`/`fore` (`PennantRenderer.cs:58`);
  - castle collapse looks up decks `quarter` and `forecastle` (`Wreck.cs:317`); the caravel's deck is
    `quarterdeck`, so it finds none;
  - deck code `top` skipped in `ShipMounts.cs`, `ShipPieces.cs`, `ShipParts.cs`;
  - carrack-only legacy places (`ShipPieces.cs:2001-2048`);
  - the rigging damage box `m.X ± 3.5` (`ShipDamage.cs:576`); damage defaults tuned for the carrack.
- **Flooding:** one hold volume (`EntityShip.Water`, tonnes) plus deck water; leaks per holed panel. No watertight
  compartments, which is right for these ships.
- **Pieces:** stairs, partitions, doors, hatches, hearth, oven and vanilla furniture are `ShipPiece`s on mount
  places (`f:`, `w:`, `p:`, `h:` ids). Generic, and it works across ships.
- **Rendering:** each group of the baked mesh file becomes a mesh; slot chunks are rebuilt where something changed.
  All geometry is baked offline. Nothing is instanced: the caravel's 10 guns are 10 baked copies of about 10 moving
  parts each.
- **Saves:** slot states as differences by slot id (`hansaSlots`), damage by `ShipPart` code (side, station, band,
  half), versioned 1-4 (`ShipFlooding.cs:351`), pieces by mount id, a layout revision (`layoutRev`). There is no
  definition hash; an id the design no longer has is dropped, and a renumbered station moves damage to another part.
  Ships 0.1.0 has been on Mod DB since 1 November, so **players have saved caravels tied to today's ids**.

### 1.5 Magelan's carrack: why its modularity failed

From Magelan's `tools/shipgen` and docs (read only):

1. **No design file.** All three carracks come out of one script, chosen by environment variables (`VARIANT`,
   `MAGELAN_COMPANION`, `MAGELAN_MODULAR`). About 108 lines test `MODULAR`, 161 `GRID`, 28 `PROPOSAL`, 32 `VARIANT`.
2. **No objects for decks, castles, masts, rig or fittings.** They are module globals in absolute ship coordinates
   (`MASTS`, `GUN_X`, `CASTLE_GUNS`, `CAPSTAN_X`, `HATCH`, `PUMP`), read directly by about 120 feature functions.
   Only `gun.py` is a reusable part, and even the gun's placement, port, lid and train limits are carrack code
   (about 1,000 lines of sweep solver).
3. **Hidden coupling.** The main shrouds are "centred on the first gun". The channel is sized by a gun lid's swing.
   The cathead was moved so the bow guns swing clear. Moving one thing breaks others, and it takes a build of about
   12 minutes to find out.
4. **History kept as code.** `COMPANION` is assigned 13 times and `CASTLE_GUNS` 6 times, each with a
   "(0.3.x, note N)" comment; the last assignment wins.
5. **A hull change means re-placing every fitting by hand** (Magelan's `docs/views/hull/hull-proposal.md` section 5,
   the "moved by" table) and re-banding the hull panels, whose bands are absolute height tables (`HULL_BANDS`).
6. **Saves are tied to generated geometry.** Panel ids come from bands, cell ids from grid indices, piece ids from
   coordinates (`kind@x,y,z`). Every relayout needed `old_key()`, `layoutRev`/`layoutBefore`, damage save versions
   1-4 and orphan rescues.
7. **Baked outputs drift from the generator.** About 15 MB of mesh per variant, guns and sails duplicated. The
   shipped carrack-m still has poop guns that the generator removed.

The runtime was not the trouble: Magelan's C# is data-driven too. **The trouble is that a ship is a program, and her
parts have no names.** The caravel's pipeline repeats points 2-6 at a smaller scale and answers point 1 with a
second ship-specific script set.

### 1.6 A measurement: the CAD step can go

A throwaway script, `docs/proposals/2026-11-13-large-ship-framework-spike.py` (numpy only), reads the committed
`models/caravel/caravel-lines.json`, builds the outer and inner half-breadth fields on a 12.5 mm grid and cuts the
planking by the block cells. Run on this branch:

| | numpy, from the lines file | AstoCAD build and slice |
|---|---|---|
| Time | 0.2 s | 23 min build + 7 min slice |
| Displacement below the waterline | 263.98 m³ | 263.9 m³ |
| Envelope volume (the spike does not cut the transom and counter) | 715.96 m³ | 710.17 m³ |
| Planking cells | 559 | 526 (after 76 slivers were merged away) |
| Cells within 2 % of the CAD's volume | 439 of 526, median difference 0.02 litres | |
| Largest differences | cells -3, -1, 1, 3 at the bulwark: the gates and gun ports, which the spike does not cut | |
| Pieces under 4 litres | 37, listed before anything else runs | found after 30 minutes |

It proves one point only: for the hull's structure, the lines file already holds everything, and cutting it by
cells is arithmetic. Openings, ribs, beams and decks are the same kind of arithmetic. The carrack's v1 bodies were
already made this way (`tools/master/master_fields.py`; its 1,424.8 m³ displacement was checked independently).

## 2. What is right (keep)

1. **One ship-local block grid, 1 block = 1 m.** Decks 3 apart (2 clear, 1 structural), the waterline 0.875 above a
   cell border, the centreline through a cell's middle. These rules work in game: quays line up, players fit, and
   the construction rules depend on them.
2. **Locality:** an edit changes only its cell, and a save stores differences from the design, by address.
3. **Pieces per cell, removable and repairable with ordinary materials** (section 1.3). Play-testing arrived at it,
   and it matches the construction rules in `AGENTS.md`.
4. **Half-breadth fields** (`tools/master/master_fields.py`, and `caravel_lines.py`'s erosion): a hull is y-convex,
   so every body is an interval of |Y| at each (X, Z). Erosion by a ball gives inner surfaces that never fail at the
   ends, where CAD offsets did. This is the most valuable geometry idea in the repo.
5. **A data-driven runtime** with derived masks, the piece and mount system, gun places and slot states.
6. **Measured, checked output:** overlap checks (`overlap_check.py`), metric UVs and tile splitting, model
   fingerprints, the game-texture renderer (`game_view.py`), the shared ship viewer, the rig and stability study
   (`rig_study.py`), a bill of materials measured from the model (`docs/proposals/caravel-charter-bom.md`).
7. **Historical intent with recorded deviations:** head room under the sails, the draft lift, the deck pitch.
8. **Fittings with real mechanisms:** guns with a run-out search, lids, breechings and moving parts; the pump, the
   capstan, the wheel with its tiller ropes.

## 3. What is wrong, and why it matters for the next ships

1. **No members.** Upstream of the export, structure is five lumped bodies (caravel) or module globals (Magelan).
   - Nothing can say "frame -6" or "the timbers this deck stands on".
   - A rib cell can be taken out with no consequence, because nothing knows what it carries.
   - The keel, stem and sternpost are fixed, against the construction rule that they are removable and damageable.
   - The bill of materials is estimated from areas.
   - Staged building has nothing to stage.
2. **Cell contents come from overlaps, not from intent.** Slicing lumped bodies decides what a cell is. The
   15 October review rejected this for the first prototype (its section 2, points 1 and 2). Its symptoms here: 93
   slivers, cross-cell merges that break locality, and hand-made "fixed" lists in the exporter.
3. **A ship is a program.** Each ship needs its own scripts. Variants are environment variables and `if`s. Moving a
   mast is a coding task with a loop of 4 to 42 minutes, and every nearby fitting is checked by hand.
4. **The ids that saves depend on are encodings.** Station/band for hull panels, with made-up stations where a cell
   is not a side panel. Every relayout costs a migration, as Magelan found seven times over.
5. **Nothing is shared by reference.** Guns, sails, rope and fittings are baked into each ship's mesh. The caravel
   alone is 39 MB of JSON, and each further ship adds 15-40 MB.
6. **CAD is on the critical path but is not the source** (section 1.6). It costs the long builds, the silent
   boolean failures and a Windows-only external tool. In return it gives a file nobody edits.
7. **Two hull pipelines disagree.** The carrack's FCStd holds editable sketches and field bodies. The caravel's is
   polygon lofts from Python. Their objects and scripts are not aligned (`caravel-design.md`, "Object names").
8. **Decisions and code have drifted apart.** D17 says one master block per cell; the shipped caravel has several
   pieces per cell. `docs/master-model.md` says slivers never move; the slicer moves them. `AGENTS.md` says the keel
   is removable; it is fixed. The docs that record this are diaries.

None of these is a runtime bug. Each multiplies the cost of every new ship and of every change to an existing one.

## 4. Proposed architecture (proposal)

### 4.1 Principles

1. **A ship is data: a design file plus a shared library.** The generator has no ship names in it.
2. **Members are first-class.** Every structural timber and every run of planking has a name, a type, a material,
   a building stage and the members it rests on.
3. **Cells are the address; members are the content.** A member is cut into pieces by the cells. A piece never
   crosses a cell border and is never moved to another cell.
4. **The game keeps state per piece**, addressed by type and cell, as the caravel's rib pieces already are.
5. **One construction order** builds the model offline and, later, the ship in game.
6. **Fast, deterministic, headless:** a full ship in under a minute, no CAD in the build, byte-identical output for
   identical input.

### 4.2 Data model

```
ships/<code>/design.json      the ship: proportions, form, framing, decks, castles, openings, rig, fittings, layout
library/members/*.json        member types: keel, stem, sternpost, floor, futtock (frame), keelson, plank, wale,
                              clamp, beam, carling, knee, deck plank, bulwark, cap rail, mast step, partners...
library/components/*.json     guns, swivels, pump, capstan, wheel, whipstaff, rudder, anchor, cathead, hatch,
                              gate, stair, lanterns
library/rigs/*.json           rig plans: caravela latina, redonda, nao, carrack, cog
```

A design file, sketched (field names are illustrative):

```jsonc
{
  "code": "caravel", "rev": 13,
  "principal": { "lengthDeck": 25.2, "beam": 6.6, "keel": 18.0, "depth": 4.0 },
  "levels": { "underside": 0.125, "hold": 1, "waterline": 2.875, "main": 4, "quarterdeck": 7 },
  "form": {
    "method": "masterFrame",            // or "offsets" (a table from plans, a scan, or sampled from a CAD model)
    "midship": { "z": [0, 0.25, 0.5], "halfBreadth": [1.6, 2.05, 2.35] },
    "rising":    { "x": [-10, 0, 6], "dz": [0.9, 0, 0.6] },   // how the floor rises toward the ends
    "narrowing": { "x": [-10, 0, 6], "dy": [1.2, 0, 0.9] },   // how the floor narrows toward the ends
    "stem": [[0, 5.6], [0.25, 6.85]], "stern": { "post": {}, "counter": {}, "transom": {} },
    "sheer": [[-16, 8.0], [-7, 8.0], [-4.5, 5.0], [10, 5.0]]
  },
  "scantlings": { "plank": 0.2, "frame": [0.2, 0.4], "backbone": [0.2, 0.6], "beam": [0.2, 0.4] },
  "framing": { "frames": "every 2 from -14 to 6" },
  "decks": [ { "id": "main", "level": "main", "from": "stern", "to": "stem", "beams": "every frame" },
             { "id": "quarterdeck", "level": "quarterdeck", "from": "stern", "to": -7 } ],
  "openings": [ { "type": "hatch", "deck": "main", "cells": [[-5, 0], [-4, 0]] },
                { "type": "gunport", "deck": "main", "cells": [-1, 1, 3], "side": "both" } ],
  "rig": { "plan": "caravela-latina", "masts": { "main": { "cell": -3 }, "mizzen": { "cell": -11 } } },
  "fittings": [ { "c": "gun/carriage-bronze", "at": { "deck": "main", "cell": 1, "side": "both" } },
                { "c": "pump/bronze", "at": { "deck": "main", "cell": -2 } } ],
  "layout": { "pieces": [] },               // today's modularPieces: cabin walls, galley, stairs
  "overrides": [ { "note": 69, "what": "capRail.inboard", "atFrames": { "4": 0.47, "6": 0.55 } } ],
  "deviations": [ { "what": "decks 3 blocks apart", "why": "2 clear blocks for a player" } ]
}
```

Generator output, per ship:

```jsonc
// members: one record per member
{ "id": "frame:-6", "type": "frame", "material": "oak", "stage": "frames",
  "restsOn": ["keel"], "carries": ["beam:main:-6", "plank:*:-6"], "required": true,
  "pieces": ["rib:-6:-3:1", "rib:-6:-3:2", "rib:-6:-2:0"] }

// pieces: one record per member piece in a cell; this is what the game keeps state for
{ "id": "rib:-6:-3:2", "member": "frame:-6", "cell": [-6, -3, 2], "type": "frame",
  "bom": { "game:supportbeam-oak": 1 }, "voxels": "...", "mesh": 1182 }
```

- **Piece ids are a type and a cell address**: `rib:-6:-3:2`, `plank:-6:-3:2`, `wale:-6:-3:4`, `beam:main:-6:-1`,
  `deck:main:-6:1`, `keel:-6:0:0`. The grid is the contract, so these do not change when the lines, the scantlings
  or a fitting change. The caravel's rib and deck ids already have this form. They change only when the layout
  changes (a frame moves, the ship is lengthened), and the design then carries an explicit migration (section 4.8).
- **Member ids are names from the design**: `keel`, `stem`, `sternpost`, `frame:-6`, `wale:p:1`, `clamp:p:main`,
  `beam:main:-6`, `mast:main`. The game uses them for support, stages and messages ("frame -6 is broken"), not as
  save keys.
- **Planking is cut per cell on purpose.** A plank piece is today's hull panel: one block of her side that a player
  takes out and repairs. Real strakes stay as the direction of the texture and as building stages (garboard,
  bottom, bilge, side, topsides). Wales are members of their own because they are thicker and carry the frames'
  heads.
- **A cell's "kind" is not stored.** It is the set of pieces in it, and the library's rules read that: "a cell
  with a frame piece takes no hatch", "a plank piece above the waterline with no frame in its cell may become a
  window or a port". This replaces the overlap accidents and the exporter's fixed lists.

### 4.3 Pipeline: the generator follows the yard

One generator, the same code for every ship. Each stage reads the design and the members made so far, and adds
members:

| Stage | Makes | From |
|---|---|---|
| 1 Proportions | principal dimensions; checks (length to beam, depth, the deck pitch the grid needs) | `principal`, `levels` |
| 2 Moulds | the master frame and its rising and narrowing to every frame station; or an offsets table | `form` |
| 3 Backbone | keel, stem, sternpost, deadwood, keelson | `form`, `scantlings` |
| 4 Frames | floors and futtocks at each frame station, fitted to the moulds | `framing` |
| 5 Hull surface | the fair outside surface through the frames (a half-breadth field), sheer, transom, counter | stages 2-4 |
| 6 Planking | plank pieces and wales over the frames; ports, gates and hawse holes cut out | `openings` |
| 7 Inner structure | clamps, beams, carlings and headledges round openings, knees, mast steps and partners | `decks`, `openings` |
| 8 Decks | deck planking per cell, hatches, wells | `decks`, `openings` |
| 9 Castles and bulwarks | castle decks, fronts, bulwarks, cap rails, railings | `decks`, `form.sheer` |
| 10 Rig | masts, yards, sails, standing and running rigging from the rig plan; head room and swing checks | `rig`, library |
| 11 Fittings | library components at their places; clearance checks | `fittings`, library |
| 12 Layout | default pieces (cabins, galley, stairs) on mount places | `layout` |
| 13 Game data | collision voxels, meshes, UVs, paint zones, hull samples, water clip, hydrostatics, rooms | everything |

- **Geometry.** One function `w(x, z)` is the hull. Every member is an interval of |Y| on it, or a swept section,
  as `master_fields.py` already does for the carrack. Volumes, voxels and checks use the field on a fine grid
  (section 1.6 used 12.5 mm; a whole ship is a few million nodes). Meshes use the lines' own polygon points, clipped
  exactly to the cells, so the triangle count stays where it is today.
- **Slivers are handled before anything is written.** The generator knows the grid, so it can put deck ends,
  opening sides and plank edges on cell borders, or stop with "the deck's end leaves 2.4 litres in cell
  (5, -3, 3)". The fix is a number in the design. Nothing is merged across cells.
- **Where a hull's shape comes from is the designer's choice:**
  - the historical rules (stage 2);
  - an offsets table from plans;
  - a surface drawn in AstoCAD and sampled (`master_fields.sample` does this for the carrack).

  The *structure* never comes from CAD.
- **Language: Python and numpy** (recommended). It reuses `caravel_lines.py`'s form, `master_fields.py`, `gun.py`,
  `caravel_gun.py`, `caravel_fittings.py`, `shipgen.Design`'s mesh writer and the checks. The alternative is C#
  shared with the runtime, so the game could build a ship at load from a few KB. That is a larger rewrite with no
  gameplay need yet (decision 5).

### 4.4 Members and pieces in the game

| Concern | Today | Proposed |
|---|---|---|
| Collision | one voxel grid; a voxel shared by two bodies stays when one is taken out | the same grid; each solid voxel has one owning piece (the construction core's tested invariant) |
| Hull planking | `HullSlotDef`, station/band/half, a `cell` field | plank and wale pieces by cell |
| Ribs | `rib:<cell>` slots, in or out | frame pieces by cell, with damage states; the frame as a member knows what it carries |
| Beams | lengths under open deck cells only | every beam is a member; its pieces by cell |
| Keel, stem, sternpost | fixed | pieces; damageable; replaced only on a slipway or careened (decision 8) |
| Support | none | the member graph: deck plank ← beam ← clamp and frame ← keel. A piece out weakens or drops what it carries, as `src/Construction` already checks |
| Damage parts | one per hull slot and deck cell, plus mast, sail, rigging, rudder | one per piece that can be hit, plus the rig and rudder as today |
| Flooding | one hold volume, leaks per holed panel | the same |
| Repair | per part kind (`ShipRepairs.Needs`) | per member type from the library (`bom`, tools) |
| Grey guides | not wired | a missing required piece shows its own geometry in grey |
| Charter cost | estimated from areas | the sum of `bom` over all pieces and components |

The mechanics should depend on one interface, close to section 5 of the 15 October proposal but keyed by pieces:

```csharp
interface IShipStructure {
    Piece PieceAt(Vec3d localPoint);              // ray hits, targeting
    IEnumerable<Piece> PiecesIn(CellId c);
    PieceState State(PieceId p);                  // sound, cracked, broken, absent
    bool Supported(PieceId p);                    // the member graph
    VoxelGrid Collision { get; }                  // restamped per piece
    event Action<PieceId> Changed;
}
```

`HullSlots`, `DeckSlots` and the station/band code first become an adapter behind it, then go away.

### 4.5 Shared components

A component is a library entry with:

- parameters (a gun's bore and length, a mast's height and diameter);
- an anchor rule ("stands on a deck cell facing outboard", "steps on the keelson");
- its mesh, made once and **instanced** by the game, not baked into each ship;
- its moving parts in its own frame (train, elevate, recoil, lid, pump brake);
- a clearance envelope ("sweeps this volume when laid ±15° and elevated -8 to +12°");
- a runtime type (today's `FixtureDef.Type`) and a bill of materials.

A ship places a component by deck, cell and side. The generator checks its envelope against members and other
components, as `build_caravel.py` does today for the guns only. Magelan's hidden couplings (shrouds centred on a
gun, a cathead moved for the bow guns) become checks that fail with a message.

The first entries come from what exists:

- guns: `gun.py`, `caravel_gun.py`, the swivel;
- pump, capstan, wheel, cathead and anchor: `caravel_fittings.py`, which is Magelan's geometry with positions
  passed in;
- stairs, hatches, gates and lids: `ModularGeometry.cs` and the exporter;
- rigs: the lateen rules in `caravel_layout.py`, the square rig in Magelan's `masts_and_rigging`.

### 4.6 Scale

- **Keep 1 block = 1 m along the length and across.**
- **Heights are quantized, and that is a recorded deviation.** 2 clear blocks under each deck make the deck pitch
  3 m; real tween decks were about 1.6 to 2 m. The caravel is 3.9 m from her underside to the main deck. The Santa
  María replica's depth of hold is 3.49 m, with more decks above it in less height. The generator should keep the
  real lines along and across and stretch heights between fixed levels, as `caravel_lines.shape_z` already does
  below the waterline, and report the stretch in `deviations`.
- **Frames are fewer and heavier than real.** A real nao had a frame every 40-50 cm. The game has one per rib cell,
  every 2 or 4 m, with 0.2 × 0.4 m timbers. One game frame stands for several real ones.
- **Planking is 0.2 m thick** (real: 6-8 cm). The caravel's hull timber weighs 95 t, 2-3 times a real one
  (`rig-study.md`). Fine for looks. The weight model should use a corrected density so stability stays plausible.

### 4.7 Assets, determinism and size

- **Outputs per ship:** members and pieces, a compact mesh of the hull members only, and the `ShipDefinition`
  fields still needed (motion, sailing, damage tuning, rooms, mounts). Component meshes live once in the library.
- **Determinism:** fixed iteration order, rounding at output, no dependence on hash order. One golden test per ship:
  build, hash every output, compare with committed hashes.
- **Caches**, if any are still needed, are keyed by inputs *and* the generator's source.

### 4.8 Versioning and saves

- **Design revision** `rev` plus migrations in the design: `shift: {"x<-4": -2}`, `renamed`, `added`, `removed`.
  This generalises today's `layoutRev`/`layoutBefore` and `Since`.
- **A save stores differences by piece id** and the design revision it was laid at. On load, an older revision runs
  the migrations. An id that still cannot be found is dropped with a log line, never mapped to a neighbour.
- **Published caravels (0.1.x):** one table maps `hull:p:<station>:<band>:<half>` to `plank:<cell>`. Each hull slot
  already carries its `cell`, so the exporter can write that table today. Rib, beam and deck ids keep their form.
  Whether to migrate or break is decision 6.

### 4.9 Testability

- Each stage is a pure function of the design and earlier members, so each gets offline checks: frames fair,
  planking covers the frames, one owner per voxel, no sliver, watertight under the waterline, every support
  resolves. Most exist already in some form: `ConstructionTests` (99 checks), `HansaTests -- caravel-all`,
  `overlap_check.py`, the rib check in `caravel_lines.py`.
- Every ship gets the same suite: build, invariants, hydrostatics, head room, rig swing, component clearances, bill
  of materials, golden hashes. A new ship needs only her design file and a look in game.
- `game_view.py` renders stay the check before the author sees a ship.

## 5. What survives

| Keep as is | Adapt | Retire, once the replacement works |
|---|---|---|
| `ShipMotion`, sailing, helm, crews, `DeckMover`, `VoxelGrid`, pieces and mounts, gun laying, wrecks, paint, SeaLink, the self-tests | `ShipDamage`, `ShipFlooding` (parts from pieces), `ShipRepairs` (library `bom`), `ShipRenderer` (instanced components), `ShipDefinition` (pieces instead of slot lists), the hard-coded deck and mast names | `HullSlots`, `DeckSlots`, station/band codes (after migration), `GridShips.cs`, `GridShipSlots.cs` |
| `master_fields.py`; `caravel_lines.py`'s form functions; `gun.py`, `caravel_gun.py`; `caravel_fittings.py`'s geometry; `shipgen.Design`'s mesh writer; `overlap_check.py`, `mesh_contacts.py`, `game_view.py`, `tools/shipview`; `rig_study.py` as the hydrostatics stage; `model_fingerprint.py`, `compare_builds.py` | `caravel_layout.py` (its rig rules into the rig library, its UV and tile code into stage 13) | `build_caravel.py`, `slice_caravel.py`, `export_caravel.py`; the CAD parts of `tools/master`; `tools/shipgen/carrack.py` after its fitting and rig helpers are extracted; the `hansa-carrack`, `carrack` and `carrack-a` assets |
| `src/Construction`'s ideas and tests: kinds as rules, the support graph, guides, saves by address, face walls, one voxel owner | `src/Construction/Geometry.cs` (exact clipping, UV tests) | its faceted `HullForm`, the lattice, `two-bay.json`, the construction `carrack.json` |

Data that survives:

- the caravel's lines, layout, rig points and fitting places, and the chalk notes' decisions as overrides in her
  design file;
- Magelan's sampled lines (`models/hansa-carrack/magelan-lines.json`, within 1.1 mm mean of carrack-m) and the
  fittings inventory (`docs/carrack-m-inventory.md`).

## 6. The building-sequence idea: verdict

**The idea:** start the model build from a realistic building sequence (keel, stem and sternpost, frames,
stringers and clamps, planking, decks and beams, castles, masts, rigging), as Fundación Nao Victoria built its
replicas.

### 6.1 What the sources say

The foundation's pages (copies in `C:\VS mods\data\ship-research\nao\fnv-pages`, gathered by SH-0013) do not give a
timber-by-timber sequence:

- **Nao Santa María replica** (Punta Umbría, launched 16 March 2017, about 12 months): a **fibreglass hull lined
  with wood**. That is a modern build. Her figures: length 28.30 m, beam 7.96 m, depth 3.49 m, about 200 t; main
  deck, forecastle, tolda and toldilla; main, mizzen and fore masts and a bowsprit; about 300 m² of sail.
- **Nao Victoria replica** (1991, designed and built by Ignacio Fernández Vial): the research page lists her
  sources (chronicles, 18 shipbuilding treatises of the 16th-17th centuries, 126 drawings, 3 wrecks) and
  materials: oak for the structure, pine for planking and spars. It gives no lines and no steps. No original plans
  of the Victoria survive.

The period method is in the treatises the replica's designer worked from, and SH-0013 has already saved several
(`C:\VS mods\data\ship-research\nao\research`): García de Palacio's *Instrucción náutica* (1587), Tomé Cano's
*Arte para fabricar naos* (1611, with an English translation), and Visiers' 2016 paper that rebuilds García de
Palacio's 400-ton nao as a 3D model. From Visiers, citing them:

1. **Proportions from the beam.** The rule "as, dos, tres": beam 1, keel 2, length 3. García de Palacio's nao has
   beam 16 codos, keel 34, length 51⅓, so 1 : 2.1 : 3.2.
2. **The master frame** is drawn from the flat of the floor (a fraction of the beam) and the depth to the main
   deck, as an arc of a circle.
3. **The sternpost is a straight line, the stem an arc of a circle.** The length on deck sets their rakes.
4. **Two tail frames** (redeles, Portuguese almogamas), one forward and one aft of the master, are drawn with
   moulds made from the master frame. The moulds change two things: the floor narrows and it rises (the astilla;
   on that nao 1.5 codos forward, 2.5 aft). Later a third correction, the joba, opened the frame's head.
5. **The frames between** (maderos de cuenta) get their narrowing and rising by a geometric rule of distribution.
6. **Beyond the tail frames** the shape was left to the shipwright, who faired it with flexible battens.

So the historical method is itself a small parameter set plus rules, with free-hand ends. The caravel's
`caravel_lines.py` is the same idea reinvented: a midship table, formulas toward the ends, fairing exponents.

A precedent for the whole idea: Wells' 2008 thesis (in the same folder) cites Alex Hazlett's dissertation *The Nau
of the Livro Nautico* (Texas A&M, 2007), which rebuilt a Portuguese nau **timber by timber from 16th-century texts,
with an illustrated construction sequence showing every timber's placement**. It is not in the research folder
yet; it is the closest thing to a ready data source for stages 3-9.

For a nao from plans instead of rules, SH-0013 has the 1892 Spanish commission's lines plan and sail plan of the
Santa María (`plans/ia-1892-comision-arqueologica-monleon`, public domain) and a model kit's plans.

### 6.2 Meaning (a): the master model made by a construction-sequence program

**Verdict: yes. It is section 4.3.**

- The stage order gives every member a place in a dependency chain. The same chain is the support graph the game
  needs for damage and guides.
- A parameter set plus rules is historically honest for the 15th-16th century ships this mod builds, and it is the
  only way to get a Victoria, whose plans do not exist.
- Where plans or a survey exist (the 1892 Santa María, the Bremen cog), stage 2 takes an offsets table instead, and
  everything after it is unchanged.
- **Not worth doing:** bending planks, scarfs, treenails, the order of single planks, the temporary ribbands. The
  value is in named members and their order, not in simulating carpentry.
- **A caution:** the rules give a plausible hull, not a pretty one. The caravel took six trial rounds of the
  author's eye to look right. The design file must keep hand overrides (a point on the stem, the counter's curve)
  on top of the rules.

### 6.3 Meaning (b): staged shipbuilding in game

**Verdict: yes, later, on the same data.**

- Vanilla already builds its sailboat this way. `EntityBoatConstruction`
  (`devtools/vs-ref/src/vssurvivalmod/Systems/Boats/EntityBoatConstruction.cs`, using `RightClickConstruction`) is
  an entity with ordered `stages` in `assets/survival/entities/nonliving/boat-construction.json`. Each stage
  requires stacks and adds named shape elements: rollers, keel and props, a mould, three runs of planks, a second
  mould, four more runs of planks, then ribs (which *remove* the moulds), floor beams, floor. That is a
  shell-first yard sequence, in data.
- A large ship can do the same with members. A **slipway** entity holds a design. Its stages are the generator's
  stages 3-11, grouped for play: backbone; frames in a few groups; planking by bands; beams and decks; castles;
  masts; rig; guns and fittings.
  - Each stage's cost is the sum of its members' `bom`.
  - Built members are drawn; the next stage's members are grey guides.
  - Launching needs every required member.
- A ship at sea is the same data with pieces missing or damaged. Repair is "rebuild this piece". The charter is
  "all stages at once", and its cost comes from the same sums.
- Magelan planned this as step E ("lay the skeleton on a slipway, place the panels, decks and fittings slot by
  slot"; `docs/legacy/slots.md`), and its runtime was prepared for a ship that starts empty (`FullGrid`).

### 6.4 One framework for both

The member list with its `stage` and `restsOn` fields **is** the building sequence. The generator runs the stages
to make geometry. The slipway runs them to make a ship. The support graph that orders them also decides what a
broken frame brings down.

## 7. Migration path from today's caravel (proposal)

The caravel stays shippable throughout. Her current assets are frozen until the new build matches them.

1. **Freeze and record.** Tag the current assets. Write the caravel's design file by reading the values out of
   `caravel_lines.py` (`LAYOUTS["D"]`, `RIBS`, `MASTS`, `FITTINGS`, `KITCHEN`, `STAIRS`), `build_caravel.py`
   (`SKETCH`, `RIG_P`) and `caravel_layout.py`. Turn `caravel-design.md` into a short current spec; the diary stays
   in Git.
2. **Generator, hull stages 1-9**, for the caravel from her design file. Check against two references:
   - the caravel's CAD output: volumes per body, collision voxel by voxel, `cells.json`, hydrostatics, `game_view`
     renders from the note cameras;
   - the carrack's v1 field bodies (`docs/master-model.md`, section 10).
3. **Library, part 1.** Move the gun, pump, capstan, wheel, anchor and cathead geometry into components with
   clearance envelopes, and the lateen rig rules into a rig plan. Generate the caravel's stages 10-12 from them.
4. **Game data and the runtime layer.** Write `ShipDefinition` from members and pieces. In C#, add the piece layer
   behind `IShipStructure`, first as an adapter that still feeds today's slots, then switch damage, repair, support
   and guides to it. Fix the hard-coded deck and mast names.
5. **Save migration** for published caravels, if decision 6 says so.
6. **Instanced components and a compact mesh.**
7. **Retire** the CAD scripts, the slicer, the exporter, `GridShips`, the unused carrack assets and the faceted
   construction design. History stays in Git and `archive/*` tags.

After step 4 the caravel is the first ship on the framework. The carrack and the nao are then design files plus
whatever library entries they add.

## 8. The carrack from Magelan (SH-0015) on the framework

- **Form:** `offsets`, from `models/hansa-carrack/magelan-lines.json`.
- **Levels and framing** as `docs/master-model.md` and `AGENTS.md` set them: hold 1, lower deck 4, waterline 4.875,
  main deck 7, quarterdeck and forecastle 10, poop 13; ribs in bays of 3 with a clear cell either side of each
  mast; masts in cells -13, 3 and 17; castle deck edges on a beam. All design data.
- **Rig:** a `carrack` rig plan: square fore and main with a top, lateen mizzen, bowsprit. Its measures come from
  carrack-m (`carrack.py`, `MASTS` and `YARDS`, lines 1429-1500), copied once into the design.
- **Fittings:** `docs/carrack-m-inventory.md` lists what carrack-m has. Each item is a library component placed by
  deck, cell and side. The guns are the ones the caravel already uses.
- **From Magelan:** lines, proportions, the fitting inventory, the rig's measures, the look. **Not from Magelan:**
  `carrack.py`'s code, its band and slot model, its baked meshes, its save ids. D39 stands: no save migration.
- **Open questions carried over** (`docs/master-model.md`, section 9): bulwark 1,250 or 1,000 mm, the forecastle's
  edge, the 250 mm ends, the draft lift, and whether the caravel's timber sizes apply.
- **What SH-0015 can do now, under any decision:** write the carrack's design data (form reference, levels, frame
  cells, decks, openings, rig measures, the fittings list with places) as one file, and settle the open questions
  with the author. **What it should not do:** copy `tools/caravel` into `tools/carrack`, or revive `carrack.py`.

## 9. A nao on the framework

- **Form:** `masterFrame` from the treatise rules (section 6.1), scaled to a replica's published dimensions; or
  `offsets` from the 1892 Santa María plan. Santa María replica: 28.30 × 7.96 m, depth 3.49 m.
- **Levels:** hold, main deck, then tolda and toldilla aft and a forecastle forward, all 3 apart. Expect her to
  stand about 2-3 m taller than the replica for that reason (section 4.6).
- **Rig plan:** `nao`: fore and main square, a main topsail, lateen mizzen, bowsprit with a spritsail. The head
  room rule will cut the courses as it cut the caravel's lateens.
- **Reused unchanged:** guns, swivels, pump, capstan, anchor, stairs, hatches, cabin pieces.
- **New library entries:** a whipstaff (the Victoria's research names one), square sails with a top if the carrack
  has not added them yet, perhaps a ship's boat.
- **A cog** (the campaign opening, D15) tests the framework differently: clinker planking built shell-first, one
  mast, one square sail, a sternpost rudder. It adds a clinker rule to the plank type, a stage order with planking
  before frames (as vanilla's boat has), and a cog rig plan.

## 10. Data each ship needs

| Data | Caravel | Carrack | Nao | Cog |
|---|---|---|---|---|
| Principal dimensions | have | have | Santa María replica: have; Victoria: SH-0013 | needed |
| Lines: offsets, or master frame with rising and narrowing | have (formulas) | have (`magelan-lines.json`) | rules: García de Palacio, Cano (have the texts); plan: 1892 Santa María (have the scan) | needed: a surveyed cog |
| Deck levels and lengths, castles | have | have | needed (photos, deck list) | needed |
| Frame stations and scantlings | have | have | proposal: as the caravel | needed |
| Openings: ports, hatches, gates, windows | have | in the inventory | needed (photos) | few |
| Rig: masts, yards, sail plan | have | have (carrack-m) | 1892 sail plan (have); replica photos | needed |
| Fittings and their places | have | have (inventory) | needed (photos, deck plan) | few |
| Interior layout | have | have (carrack-m's pieces) | needed | small |
| Member sequence and names | from this proposal | the same | Hazlett 2007 (to fetch) | needed |
| Deviations for play | have | have | to write | to write |

SH-0013's folder had, at the time of writing: the foundation's pages, photos, the 1892 commission's plates, the two
treatises, the Visiers and Wells papers, a kit's plans, and press pages. Its `_notes` folder was still empty. No 3D
model of a replica had been found.

## 11. Risks

1. **Losing the caravel's finish.** About 150 chalk notes of detail live in caravel-only code: the cap rail's
   width at each rib, the stair's recess in the bulwark, gate rebates, the counter. *Mitigation:* frozen assets
   until a side-by-side check passes (voxels, renders from the note cameras, the self-test); details the rules
   cannot produce become `overrides` in her design; the author sees renders before the switch.
2. **Over-building the generator.** A shipwright program invites endless historical refinement. *Mitigation:* the
   caravel's parity first, then one new ship at a time; nothing the game does not show or use.
3. **The piece layer touches the mod's core.** `HullSlot`, `DeckSlot` and `SlotState` appear in 29 source files outside the self-tests.
   *Mitigation:* the adapter step; the existing self-test (about 390 checks) and offline tests run unchanged against it.
4. **Save migration** for published caravels can lose damage or laid pieces. *Mitigation:* a generated table,
   tested on copies of real saves.
5. **Two pipelines for a while.** Until step 7 the old export must stay runnable for the shipped caravel.
6. **Support rules change play.** A frame that brings down a deck is a new consequence at sea. That fits the
   author's rule that real consequences stay, but it needs tuning and the author's eye.
7. **The treatise rules may not suit the grid.** The 3 m deck pitch and one frame per 2-4 m are far from a real
   nao. The generator must stretch and say so, not pretend.

## 12. Effort (rough)

In agent sessions of about a day each, with the author's review between. A guess, not a plan.

| Step | Sessions |
|---|---|
| 1 Freeze, the caravel's design file, a current spec | 2-3 |
| 2 Generator, hull stages, parity with the CAD output | 4-7 |
| 3 Component and rig library, caravel stages 10-12 | 5-8 |
| 4 Game data, the piece layer, support, damageable framing | 6-10 |
| 5 Save migration (if wanted) | 2-3 |
| 6 Instancing and a compact mesh | 2-4 |
| 7 Retire the old pipeline | 1-2 |
| The carrack (SH-0015) on the framework | 6-10 |
| A nao, once her data is complete | 5-8 |
| Staged building in game (slipway) | 6-10 |
| A cog | 4-6 |

Steps 1-4 (17-28 sessions) put the caravel on the framework. Steps 1-3 alone are enough to start the carrack's
hull and rig offline.

**The cheaper alternative, not recommended:** keep the CAD pipeline, give each frame, beam and deck its own CAD
object, and make the scripts take a ship code. It is perhaps half the work up front. It keeps the 20-40 minute
loops, the silent boolean failures and AstoCAD in the build, and it still leaves each ship's placement as code.

## 13. Decisions for the author

1. **Source of truth.** A design file plus the generator, with AstoCAD optional for drawing a hull shape and
   viewing. Or the CAD master model as in `docs/master-model.md`, section 1. *Proposed: the design file.*
2. **Named members** with types, materials, stages and supports as the structural model. *Proposed: yes.*
3. **Several pieces per cell**, each clipped to its cell and removable on its own, in place of "one master block
   per cell" (D17, decision 6). The shipped caravel already works this way. *Proposed: record it.*
4. **Slivers** are never moved across cells; the generator moves the edge or stops. *Proposed: yes, as
   `master-model.md` section 7 already says.*
5. **Generator language and output.** Python (proposed) or C#. A compact mesh and instanced components in place of
   the baked 30 MB JSON.
6. **Published caravels.** Migrate saved 0.1.x caravels to the new ids, or let them stop loading, as D39 did for
   `hansa:` ships. *Proposed: migrate; the table is cheap.*
7. **Frame spacing** per design (the caravel every 2nd cell, the carrack in bays of 3), as `AGENTS.md` has it, or
   one rule for all ships.
8. **What can be damaged and removed.** Proposed: frames and beams per cell piece, with support consequences; keel,
   stem and sternpost damageable but replaced only on a slipway or careened; masts as today.
9. **Hull form.** Both methods supported: master frame with rising and narrowing (caravel, nao) and offsets
   (carrack, a nao from the 1892 plan, a cog).
10. **Scale.** Confirm 1 block = 1 m along and across, the 3 m deck pitch, heights stretched and recorded as a
    deviation, frames fewer and heavier than real.
11. **Order of work.** The caravel onto the framework first (proposed), with SH-0015 gathering the carrack's design
    data meanwhile. Or the carrack first as the test bed, with nothing to compare it against.
12. **Staged building in game:** after the carrack, or before.
13. **Retirements:** the faceted form in `src/Construction` (its invariants and tests kept), `GridShips`, the
    `hansa-carrack`, `carrack` and `carrack-a` assets, `tools/shipgen/carrack.py` after extraction.
14. **Rig library** for the next two ships: square sails with a top, spritsail, whipstaff.
15. **Design docs.** Each ship's doc becomes a short current spec; trial history goes to its changelog and Git.
16. **Hazlett's dissertation** and the treatises as the reference for member names and order: ask SH-0013 to fetch
    it and extract the sequence.
