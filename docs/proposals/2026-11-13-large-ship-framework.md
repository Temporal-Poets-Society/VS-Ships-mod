# A framework for building ships: review and proposal

Status: **proposal, nothing here is agreed** (13-14 November 2026, backlog SH-0014, reviewed with Fable 5.2). It
reviews how the ships are built today and proposes a framework for the caravel, a nao, Magelan's carrack (SH-0015),
a cog and smaller ships such as a knarr. Sections 1 and 2 describe what is **implemented**; section 3 is the
review's judgment; everything from section 4 on is **proposal** unless it says otherwise. The decisions the author
must make are numbered in section 15. Until the author decides, the construction rules in `AGENTS.md`, section 8 of
`docs/proposals/ship-design-architecture.md` and suite decision D17 stand.

Amended 14 November 2026 for two requirements from the author: "architecture and pipeline should support any 3D
model input and smaller ships", with an example model, `Knarr.bbmodel`. Sections 5 and 6 are new; sections 0, 1.7,
4 and 7 to 15 were updated for them. The file keeps its first name.

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
  - No CAD in the build. The author, 14 November: AstoCAD "was just in case if i wanted to change some small things
    manually but does not need to be there. I noticed it took really long time".
- **Two ways in, one ship out.** A ship's design can be written by hand and rule (the generator), or lifted from
  any 3D model: a Blockbench or Vintage Story shape, glTF, OBJ, FBX, STL, a scan, a CAD file (section 5). The
  importer turns the model into the same design data. From there the same stages make the same members, pieces and
  game data, so the runtime never knows where a ship came from.
  - Measured on real files (section 5.9): the caravel's lines come back out of her own 297,000-triangle game mesh
    within 7 mm (median) in 2 seconds, and none of the downloaded models needed to be watertight.
- **Smaller ships fit, with two changes** (section 6). The building order and the member types become data per
  shipbuilding tradition (clinker hulls are built shell-first). And the framework names three size classes: decked
  ships, open ships such as a knarr, and boats. Block cells work down to about 4 m of beam; boats below that stay
  with vanilla and Sea.
- **The building-sequence idea is the right backbone, as a construction order, not as a simulation of carpentry.**
  The Iberian shipwrights' own method was parametric (section 8). One ordered list of stages can drive the generator
  offline and staged shipbuilding in game. Vanilla's boat already works this way.
- **A lot survives:** nearly all of the runtime, the half-breadth field method, the caravel's lines and layout, the
  gun and fitting geometry, the checks and viewers. What is retired is the CAD build, the slicer, the caravel's
  exporter, Magelan's generator (after its helpers are extracted) and `GridShips.cs`.
- **For the carrack (SH-0015): through the generator, not through the mesh importer.** Her hull form is already
  on file as lines sampled from Magelan's own formulas, which is better than anything sampled from her baked mesh.
  The importer serves her only as a check and a measuring tool. Do not copy `tools/caravel`: that would make a third
  ship-specific script set. Her data can be gathered into a design file now; her build should wait for the
  generator.

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

### 1.7 Small craft and outside models today

- **The runtime has a boat mode, used by one withdrawn boat.** The rowing boat (`skiff`, 5.6 × 1.9 m) was withdrawn
  from the release on 31 October (D49); her files stay so saved boats load.
  - She is drawn from Vintage Story shape files (`assets/ships/shapes/entity/ship/skiff`), as the carracks are.
  - She has no hull or deck slots: one hull, damaged as a whole.
  - `ShipDefinition` has the fields she needs: `MaxAboard`, `BoardFromWater`, `RowSpeed`, `PickUpItem`. Her entity
    class is `EntityRowBoat`.
- **Vanilla boats are Sea's.** Sea puts vanilla rafts and sailboats on its waves, with wind sailing and flooding
  (Sea's card). Ships does not touch them.
- **Nothing reads a 3D model.** Every ship's geometry comes out of its own generator. The nearest things are
  `tools/master/sample_lines.py`, which samples Magelan's formulas, and `master_fields.sample`, which reads a
  half-breadth field off a CAD solid.
- **Models now at hand** (not in the repo):
  - `Knarr.bbmodel`, the author's example of such an input. Another player, Gary, made her for his own
    work-in-progress mod and sent her to the author to show how such a mod is made ("feel free to make whatever
    changes you want"). She is an example, not a ship planned for Ships. A Blockbench file in Vintage Story's
    format: 1,010 cubes in named
    groups (`HullR`, `HullL`, `Keel`, `Deck`, `Mast`, `Sail`, `Rigging`, `Rudder`, `Collision`, `HideWater`,
    `Seats`, `Controls`, `AtachmentPoints` and more). Section 6.7 has her measures.
  - Seven glTF files of naos from 3D Warehouse, gathered by SH-0013 in `C:\VS mods\data\ship-research\nao\3d`:
    1,700 to 57,000 triangles each, in different units, axes and positions, some with a sea or a base under them.

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

9. **There is no way in for a model somebody else made.** A modeller's ship in Blockbench, a museum's scan or a
   3D Warehouse nao can only be looked at while someone retypes it as formulas.

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
7. **Two ways in, one ship out.** A design is written by hand and rule, or lifted from a 3D model by the importer
   (section 5). Everything after the design is the same.
8. **Size and tradition are data.** How many decks, whether there is a deck at all, the planking's kind and the
   order of building come from the design and the library (section 6), not from the generator's code.

### 4.2 Data model

```
ships/<code>/design.json      the ship: proportions, form, framing, decks, castles, openings, rig, fittings, layout
library/members/*.json        member types: keel, stem, sternpost, floor, futtock (frame), keelson, plank, wale,
                              clamp, beam, carling, knee, deck plank, bulwark, cap rail, mast step, partners...
library/components/*.json     guns, swivels, pump, capstan, wheel, whipstaff, rudder, anchor, cathead, hatch,
                              gate, stair, lanterns
library/rigs/*.json           rig plans: caravela latina, redonda, nao, carrack, one mast with a square sail
library/traditions/*.json     building traditions: the member types a ship uses, their stage order and support rules
ships/<code>/import.json      only for a ship lifted from a 3D model: the model's file and how to read it (section 5)
```

A design file, sketched (field names are illustrative):

```jsonc
{
  "code": "caravel", "rev": 13,
  "class": "decked",                    // or "open" (a knarr, a small cog); section 6.2
  "tradition": "iberian-carvel",        // or "norse-clinker", "cog"; section 6.3
  "principal": { "lengthDeck": 25.2, "beam": 6.6, "keel": 18.0, "depth": 4.0 },
  "levels": { "underside": 0.125, "hold": 1, "waterline": 2.875, "main": 4, "quarterdeck": 7 },
  "form": {
    "method": "masterFrame",            // or "offsets": a table from plans, or written by the importer (section 5)
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
members. The table is the order of an Iberian carvel ship. The stages are the same for every ship, but their order
and the member types they use come from the ship's tradition (section 6.3), and a stage with nothing to make is
skipped: a knarr has no castles and no guns.

| Stage | Makes | From |
|---|---|---|
| 0 Import (only for a ship lifted from a model) | the design's `form`, `levels`, `decks`, masts and fittings | a 3D model and `import.json` (section 5) |
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
  - any 3D model, through the importer (section 5).

  The *structure* is made by the generator in every case, unless the model brings its own (section 5.8).
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
- **Small ships** keep the same scale. What changes for them is in section 6.2.
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

## 5. Any 3D model as input (proposal)

The author's requirement of 14 November: the pipeline takes any 3D model, beside the generator and not instead of
it. A ship may arrive as a Blockbench or Vintage Story shape, glTF or GLB, OBJ, FBX, STL, a Sketchfab or 3D Warehouse
download, a photogrammetry scan, or an AstoCAD or FreeCAD file.

### 5.1 The idea: the importer writes a design, not a ship

A ship is four things, and a model can supply some of them:

| | From the generator | From a model |
|---|---|---|
| **Form:** the hull's outside surface, the sheer, the deck levels | rules or an offsets table in the design | sampled from the model, written into the design as offsets |
| **Structure:** members and their pieces | stages 3-9 | the same stages, run on the sampled form; or the model's own timbers where it has them and they are tagged |
| **Rig and fittings** | library components | library components, put where the model has its own |
| **Look:** what is drawn | the generator's meshes with game textures | the generator's (depth `reference`); or the model's own geometry cut by the cells (depth `look`) |

So the importer is stage 0 of the same pipeline. Its output is an ordinary `design.json`, a report and pictures.
Everything after it (members, pieces, collision, damage data, the game's files) is made by the same code as for a
generated ship, and the runtime cannot tell the two apart.

**Import depth** is chosen per ship:

- **`reference`:** the model gives form and measures only. The ship looks like a generated ship with that hull.
  Nothing of the model's mesh is in the mod. Right for scans, rough downloads and models used as a measuring stick.
- **`look`:** the model's own geometry is what players see, cut into the same cell pieces. Right for a model made
  for the game, as `Knarr.bbmodel` is.

### 5.2 What each kind of file gives

| Input | Geometry | Names and parts | Units and axes | Read with |
|---|---|---|---|---|
| Blockbench `.bbmodel`, Vintage Story shape JSON | cubes, each with nested rotations | groups named by the modeller; game textures | 16 pixels = 1 block; y up | plain JSON (done in the spike) |
| glTF, GLB | triangles | node and material names, often useless | metres by the standard, in practice anything; y up | numpy (done in the spike) |
| OBJ, STL | triangles | OBJ: groups; STL: none | unknown | small readers |
| FBX, Sketchfab's other formats | triangles | a hierarchy, sometimes good | unknown | converted to glTF first (Blender or the site's own glTF download) |
| Photogrammetry or laser scan | one dense skin, outside only, no parts | none | the scan's | as a mesh, `reference` depth only |
| AstoCAD, FreeCAD | exact solids | named objects | mm | meshed on export, or `master_fields.sample` |

### 5.3 The importer's steps

1. **Read** the file into triangles with their part and material names. A cube becomes 12 triangles.
2. **Frame.** Find up, the centre plane, the length axis, the bow's end and the scale (section 5.5), and bring the
   model to ship coordinates: blocks, X to the bow, the centre plane through a cell's middle.
3. **Roles.** Give every part a role: planking, backbone, deck, mast, yard, sail, rigging, rudder, a named
   component, a prop, or ignore. From names where they mean something, from shape where they do not, from the
   sidecar where neither works.
4. **Form.** From the parts with a hull role, take at every point along her length and height the outermost
   surface on each side. That is the half-breadth field `w(x, z)` of section 4.3. The two sides check each other.
   Short gaps are filled along the length; long ones are real (the waist between two castles). The field's top
   edge is the sheer.
5. **Levels.** Find the decks as large level surfaces. Put them on the block grid (section 5.10).
6. **Rig and fittings.** Measure each mast (cell, height, thickness), each yard and sail. Write them into the
   design's `rig`. Write each part tagged as a component into `fittings`, with the deck, cell and side its box
   stands in.
7. **Write** `design.json`, a report (dimensions, asymmetry, gaps filled, parts with no role, slivers) and views.
8. **Look** (depth `look` only). Cut the model's hull and deck geometry by the cell borders and give each cut part
   to the piece of its cell.

The first run writes a proposed sidecar with everything it found. The author corrects it and runs again. The same
model and sidecar always give the same design.

### 5.4 The sidecar

`ships/<code>/import.json` says how to read one model. For the Knarr, were she imported, it would look like this:

```jsonc
{
  "source": "models/knarr/Knarr.bbmodel", "sha256": "...",
  "credit": "Gary", "permission": "sent as an example, free to change (14 November 2026); publishing not asked",
  "depth": "look",
  "units": "blocks",                 // a mesh: "m", "mm", "ft", or "lengthOnDeck": 16.5 to set the scale by a measure
  "scale": 1.0,
  "up": "+y", "bow": "-z",           // found by the importer (the rudder is at the +z end); here to be corrected
  "roles": {
    "HullR|HullL": "planking:clinker",
    "Keel": "backbone",
    "Deck": "deck",
    "Mast": "rig:mast", "Sail": "rig:sail", "Rigging|Pully": "rig:rigging",
    "Rudder": "component:rudder/side",
    "AtachmentPoints": "places",     // suggested places for chests on her floor
    "Collision|HideWater|Seats|Controls|BalistaAttachment": "ignore"
  },
  "levels": { "floor": -0.83, "platforms": 0.40 },   // found, then confirmed or moved
  "waterline": -0.4,                 // not in the model: the author's, or from a draft
  "boxes": []                        // for models with useless names: a role for everything inside a box
}
```

- Roles match part names by pattern. Where a file has no useful names, a role is given to a material, to a height
  band ("hull up to 6.5") or to a box.
- `credit` and `permission` are required fields. The importer refuses a model without them.

### 5.5 What is found automatically and what the author must say

"Measured" means the spike of section 5.9 did it on real files. The rest is proposed and untested.

| Thing | Found automatically | The sidecar must say |
|---|---|---|
| Up axis | from the format (glTF, Blockbench: y) | for OBJ and STL |
| Centre plane | by symmetry. Measured: the two sides agree within 2 mm (caravel), 18 mm (Knarr), 4 mm (a Santa María), median | nothing so far |
| Length axis | the hull's longer way across. Measured | nothing so far |
| Bow's end | the end away from the rudder, once a rudder has a role | otherwise, which end |
| Scale | exact for Blockbench and CAD | for a mesh, one number: the units, or a known length |
| Hull envelope | the outermost surface. Measured, also on meshes that are not closed | which parts are hull where guns, anchors or channels stick out; where the hull stops and the rig starts |
| Sheer | the top of the hull parts at each station | where the rails are separate parts |
| Deck levels | large level surfaces, by height | which are decks and which are hatch covers or cargo |
| Masts | long thin upright parts on the centre plane | their names, if not main, fore, mizzen by position |
| Yards and sails | long thin level parts on a mast; thin sheets | the rig plan to use |
| Rigging | thin parts running from a mast to her side | nothing: the rig plan makes its own |
| Rudder, guns, pump, capstan, anchors, lanterns | not reliably | each one, by name or box, with its library component |
| Cargo, figures, a sea or a base under her | flat ground planes only. Measured | the rest, as `ignore` |
| Waterline | no | a height or a draft |
| Timbers inside the hull | no | their role, if the model has them |

A model with honest group names needs a short sidecar: the Knarr's is the one above. A download whose parts are
split by material, not by function, needs boxes and height bands. Of the seven naos from 3D Warehouse only one has
names that say anything (`main deck`, `forecastle`, `FRAME`, `mast 23m`).

### 5.6 Meshes that are open, messy or heavy

- **Not watertight.** In the traced Santa María all 19 parts have open edges; in one Victoria, 110 of 124. It does
  not matter: the field needs only the outermost surface at each point, not a closed solid. Collision voxels, dry
  spaces and hydrostatics are then made from the field, which is closed by construction.
- **Holes.** A gap of up to half a block along the length is bridged with a straight line and counted in the
  report. A longer gap is left open.
- **Lopsided models.** The report gives the difference between the two sides. The larger half-breadth is used.
  The traced Santa María's sides differ by 0.19 m at the worst twentieth of points: usable as a reference, not as a
  look.
- **Things that stick out.** Sampled with her guns, anchor and rig left in, the caravel comes out 7.52 m wide
  against 6.74 m for her hull parts alone. Her displacement barely moves (265.3 against 264.2 m³), but the beam
  matters for cells. So protruding parts need a role, or the importer's report must show them.
- **Heavy meshes.** Sampling is linear in triangles: the caravel's 297,000 took 0.6 s. A scan of millions is fine
  for the form. For depth `look` there is a budget (today's whole caravel is 148,000 quads); a model over it is
  thinned in Blender first, or imported as a reference.
- **Thickness.** A scan has an outside only, and a game model's planks are often paper-thin (the Knarr's are 0.125
  block). The inside surface always comes from the scantlings, by eroding the field (section 2, item 4).
- **Shapes one field cannot hold.** One half-breadth per point fits any single hull, with tumblehome or without.
  It does not fit two hulls side by side. A beakhead, a gallery, a side rudder or an outrigger is a separate part
  with its own role.

### 5.7 Library components replace the model's parts

- **The rig is always the library's.** Sails must set, furl, brace and tear; masts must take damage. A model's
  rig is static, or animated in its own way (the Knarr has three sail states as separate groups). So the importer
  takes measures from it (mast cell, heights, yard lengths, sail sizes) and the rig plan builds the working rig.
- **A fitting tagged as a component is removed from the model and replaced.** `"Rudder": "component:rudder/side"`
  takes the rudder's cubes out of the look and the field, and puts the library's side rudder at their place. Every
  ship then shares one gun, one pump, one rudder mechanism.
- **A part with no component can stay as a prop:** drawn as it is, solid, anchored to one cell, not functional. A
  figurehead, carving or stern gallery keeps the model's character this way.
- **Game-specific helpers are dropped.** The Knarr's `Collision` boxes (for another mod's physics), `HideWater`
  planes, `Seats` and `Controls` have no use here: walking collision comes from the pieces, the water cut from the
  hull's outline, the helm from a component. Her 40 chest attachment points would become suggested chest places on
  her floor; her maker designed her as "a slow moving ocean transport ship".

### 5.8 What is lost, and how damage, flooding and repair work

- **At `reference` depth, nothing.** Planking pieces, frames, beams and decks are generated inside the sampled
  form exactly as for a designed ship. Only the model's own look is gone.
- **At `look` depth, the structure is still all there.** Each plank piece shows the model's own surface in its
  cell. What the model does not have is made by the generator: the planking's inside face, the cut edges that show
  when a neighbour is out, and the timbers inside. The Santa María replica was built the same way: a moulded shell
  first, stiffeners and bulkheads fitted into it after (SH-0013's `building-sequence.md`).
- **What really is lost at `look` depth:**
  - *Exact fit.* The model's surface and the generated timbers behind it agree to a few centimetres, not exactly.
  - *The model's own parts as units.* 402 of the Knarr's 431 hull and keel cubes cross a cell border, so her
    strakes are cut at the borders and stop being Blockbench cubes. They are drawn as mesh, as all ships are.
  - *Texture rules.* The construction rules want texture density to follow real surface distance. A model made
    for the game (the Knarr: 16-pixel game textures) can keep its mapping; anything else is re-textured by role.
  - *Real strake and frame names.* A model's strakes are whatever the modeller drew. Stages and support use the
    generated members, not the model's cubes.
- **Damage and repair** work per piece, as on any ship: a plank piece is holed and mended with boards, a frame
  piece with a support beam. The cost comes from the library by member type.
- **Flooding** uses one volume, found by flood-filling the collision voxels. Those come from the field, so even a
  mesh full of holes gives a hull that holds water. An open ship floods over her side (section 6.5).
- **A quick import with no generated timbers** is possible: `"structure": "shell"` gives planking and deck pieces
  only, as the caravel was before 31 October. Support rules then reduce to "planking under the waterline is
  required".

### 5.9 Measured

A second throwaway script, `docs/proposals/2026-11-13-large-ship-framework-spike-mesh.py` (numpy only), reads a
mesh, samples the field, and cuts planking by the cells. Three runs:

| Model | Size | Result | Time |
|---|---|---|---|
| The caravel's own game mesh, every group (hull, guns, rig, sails, anchor) | 296,756 triangles | lines against her true lines: 7 mm median, 48 mm at 95 %; displacement 265.3 m³ against the CAD's 263.9; the two sides agree within 2 mm | 2.3 s for both runs |
| The same, hull groups only, picked by name | 209,868 triangles | 6 mm median, 40 mm at 95 %; displacement 264.2 m³ | |
| `Knarr.bbmodel`, the `Hull*` and `Keel` groups | 431 cubes of 1,010 | 25.35 × 7.35 blocks; sides agree within 18 mm; 299 planking cells a side, 9 cells across, 20 pieces under 4 litres | 0.2 s |
| A Santa María traced from plans (3D Warehouse) | 1,685 triangles, 19 parts, all open | 30.55 × 8.67 m against the replica's 28.30 × 7.96; 483 planking cells a side | 0.2 s |

What it does not prove:

- **Planking per cell is only roughly right from a mesh.** For the caravel, 231 of the CAD slice's 285 cells (a
  side) come within 25 % of its volume, and the sampled field gives 414 cells: her hull groups also hold rails,
  caps and the stem head, the ports are not cut, and the thickness is assumed. The shape comes across well; what is
  planking and what is not needs roles.
- Finding decks, masts and fittings by shape (section 5.5) was not tried.
- Cutting a model's own geometry by cells for the `look` depth was not tried. The construction core's
  `Geometry.cs` already clips convex solids to cells exactly.

### 5.10 Rules for imported ships

1. **The grid still rules.** An imported ship's walkable levels go on block borders, 3 apart where one is above
   another, with the waterline 0.875 above a border. The importer stretches heights between the model's own levels
   to get there and records it under `deviations`, as the generator does (section 4.6). At depth `look` the
   model's vertices are stretched with it.
2. **One scale.** 1 block = 1 m. A model built to another scale is scaled once, in the sidecar.
3. **Credit and permission are data.** Whether a model, or lines taken from it, may be published is decided per
   source by the author. Neither the 3D Warehouse naos nor the Knarr have such a decision; here they are test
   inputs only.
4. **The importer does not repair models.** It reports what it could not read and stops.

## 6. Smaller ships (proposal)

The author's example is a knarr: one mast, a square sail, an open hold with cargo, high stem and stern. The first
version of this proposal was written with the caravel, a nao and a carrack in mind. This section checks what it
assumed and what small ships change.

### 6.1 What the proposal assumed, and the change

| Assumed | Where | Change |
|---|---|---|
| Frames go up before planking | the stage order in 4.3 | the order comes from the ship's tradition (6.3) |
| Carvel planking, flush | plank pieces in 4.2 | planking has a kind (carvel, clinker), set per region of the hull. The 1991 Victoria herself is carvel below the main deck and clinker on her castles (SH-0013) |
| Several decks 3 apart, a hold to walk in | `levels`, `decks` | a class `open`: one floor, half-decks or none (6.2) |
| Castles, bulwarks, gun ports | stages 6 and 9 | stages with nothing to make are skipped |
| A stern rudder with tiller or wheel | components | a side rudder is another component; the steering place is wherever it stands |
| Three masts, Iberian rig plans | the rig library | a plan for one mast with a square sail |
| A pump clears the hold | fittings | an open ship is bailed (6.5) |
| The hull form comes from a master frame and rules | stage 2 | Norse hulls were built by eye, shell first, with no moulds: their form comes from a find's survey or a model, as offsets |
| One main deck seals the hull | the runtime: `DeckY`, the dry mask "up to just under the main deck" (`ShipDefinition.cs:179`), `WaterClip.DryTop = DeckY + 1.0` | an open hull is dry up to her rail at each station. 0.1.1-rc.1's `DryTops` already cuts the water up to the rail |

Nothing else in sections 4 and 5 depends on size. Members, pieces by cell, components, saves and tests are the
same for a 12 m ship.

### 6.2 Three size classes, and where the block grid stops working

A player is 0.6 wide and 1.85 tall, so needs one cell across and two up. From that:

- **A deck with room under it** needs 4 blocks from her underside to the deck's top: 1 for the bottom, 2 clear, 1
  for beams and planking. The caravel has 3.9. A real knarr is about 2 m deep. Below 4 blocks there is no hold to
  walk in: cargo stands in an open hold, which is what such ships had.
- **Cell pieces across her** need about 4 m of beam. With 0.2 planking and 0.4 frames a side, a 4.2 m beam leaves
  three clear cells: a way along the middle and a row either side. Narrower than that, the side turns through a
  whole cell at every station, every plank piece is a wedge, and taking out "one panel" removes half her side.
- **Along her** there is no hard limit. Under about 10 cells the bow and stern curves are most of the ship.

| Class | Size | Examples | Cell pieces | Walking | Flooding | In the runtime today |
|---|---|---|---|---|---|---|
| **Decked ship** | 4 blocks or more from underside to deck; beam about 6 m up | caravel, nao, carrack, a large cog | yes | decks and hold | one hold volume, pump | yes: the caravel |
| **Open ship** | beam about 4 to 7.5 m; under 4 blocks deep | knarr, small cog, a fishing boat with half-decks | yes: the bottom and 2-3 rows of side | one floor, half-decks a block up | over the side and through holes; bailing | partly: `DryTops`; no open-hold handling yet |
| **Boat** | beam under about 4 m | the skiff, a ship's boat, vanilla's sailboat and raft | no: one hull | seats | whole hull | boat mode (the skiff); vanilla's own |

- The class is a field of the design (`"class": "open"`), not something the generator guesses. The generator
  checks it: a deck asked for in a hull too shallow is an error.
- **An objective test for "do cells still work":** the share of planking pieces under 4 litres. The caravel has
  37 of 559 (7 %). The Knarr at her size has 20 of 299 a side (7 %). The generator reports the share for every
  ship; a design where it climbs steeply is a boat.
- **No finer grid for small ships.** One block grid is a construction rule. A half-block grid would double every
  rule, tool and placement path for the smallest ships only.

### 6.3 Building traditions

The stage list is data, one file per tradition, holding the order, the member types and the support rules.

| Tradition | Ships | Planking | Order of building |
|---|---|---|---|
| `iberian-carvel` | caravel, nao, carrack | carvel; clinker allowed on castles | keel; stem and sternpost; master frame and the frames between the tail frames; ribbands and end frames; keelson and mast steps; clamps and waterways; planking and wales; beams and knees; decks; castles; rudder; masts; rig |
| `norse-clinker` | knarr | clinker, lapped and riveted | keel; stem and sternpost; strakes from the garboard up, shaped by eye; floor timbers and frames fitted into the shell; crossbeams and knees; keelson and mast partner; bottom boards and half-decks; side rudder; mast; rig |
| `cog` | cog | flush bottom, clinker sides | keel plank; stem and sternpost; bottom planks; floor timbers; clinker sides; through-beams; deck; castle (later cogs); stern rudder; mast; rig |
| vanilla's sailboat (implemented in vanilla, for comparison) | | | rollers; keel and props; a mould; planks; a second mould; planks; ribs (the moulds come out); floor beams; floor |

- The Iberian order is SH-0013's, from the treatises and the 1991 Victoria. The Norse and cog orders are from
  general knowledge of the Skuldelev and Bremen finds and **must be checked against sources** when such a ship is
  researched.
- **The generator's geometry does not change with the order.** One form, one field, the same clipping. What
  changes is each member's `stage`, what it `restsOn`, and so what a missing piece does:
  - carvel: planking is held by frames; a broken frame loosens the planks on it;
  - clinker: each strake hangs on the one below, from the keel up; frames stiffen the shell and a missing one
    opens no hole.
- **Staged building in game follows the same file.** A knarr on the slipway shows her shell first, as vanilla's
  boat does.

### 6.4 What small ships add to the library

- **Member types:** clinker strake (drawn with its lands; still cut into pieces by cell), keel plank, crossbeam
  and thwart (beams with no deck on them), mast partner, bottom boards (floor pieces a player can lift), half-deck.
- **Components:** side rudder with its tiller, stern rudder with a tiller, windlass, oars and oar ports (the
  runtime's `RowSpeed` and oar fixtures exist), a tent or awning (later).
- **Rig plan:** one mast, one square sail on a yard that lowers, shrouds to her sides, braces, sheets and tacks.
  The square sail is the same component a nao's courses need.
- **Already there:** keel, stem, sternpost, floor timber, frame, knee, keelson, deck plank, the piece and mount
  system for cargo.

### 6.5 An open ship in the game

- **One floor on the grid**, with half-decks a block above it fore and aft. The waterline rule stays: 0.875 above
  a cell border, so her floor is level with a quay's blocks when she lies alongside.
- **Cargo** stands on floor places, as chests stand on the caravel's decks. No hold inventory is needed.
- **Flooding.** There is no deck to keep the sea out. Water comes in over her rail when a wave or her heel puts
  it under, and through holed planking. That is a real consequence and stays. It leaves by bailing with a bucket,
  a small new mechanic; a pump is a component a larger open ship may carry.
- **Dry space.** Inside her is dry up to her rail at each station, not up to a deck. `DryTops` (0.1.1-rc.1) does
  this for the water cut; the dry mask and the damage layout still read one `DeckY` and need the same change.
- **Steering** from wherever the rudder's tiller is. **Rowing** with sweeps, using the boat mode's fields.
- **No castles, guns or pump** are assumed anywhere in the runtime's data; the fixture lists are simply shorter.
  The castle-collapse code in `Wreck.cs` finds nothing, as on the caravel.
- **Not thought through here:** shelter and sleeping aboard an open ship, beaching her, and how crews behave with
  no deck. These are play questions for the first open ship.

### 6.6 Vanilla boats, Sea, and the withdrawn rowing boat

- **Boats stay out of the cell framework.** Vanilla's raft and sailboat are vanilla entities that Sea puts on its
  waves; that works and needs nothing from Ships. Ships' own rowing boat stays withdrawn (D49).
- **The offline side still serves boats.** The same design file, generator and importer can make a boat's hull
  mesh, collision and hydrostatics for the runtime's boat mode: one hull, no pieces. A ship's boat for the nao
  would be made this way when it is wanted.
- **A model made as a vanilla-style boat is not a walkable ship.** `Knarr.bbmodel` has seats, a control point,
  attachment points for chests and collision boxes for another mod's physics. In its own mod it is sailed from a
  seat. Brought into Ships it becomes an open ship one walks on: those helpers are dropped (section 5.7) and
  walking collision, floor places and a helm take their place. The two can exist side by side; they are different
  entities from different mods.
- **Sea needs nothing new.** An open ship reports her hull, dry places and water cut through the same bridge and
  contracts as the caravel.

### 6.7 The example: a knarr model

`Knarr.bbmodel` is the author's example of a small ship and of a model as input. Another player made her for his
own mod and sent her to show how it is done; she is not a ship planned for Ships. Measured by the spike:

| | |
|---|---|
| File | Blockbench, Vintage Story format; 1,010 cubes, 644 of them rotated; 22 game-style textures |
| Hull (`HullR`, `HullL`, `Keel`) | 25.35 blocks long, 7.35 wide; keel's underside to her side's top amidships about 2.7; stem and stern posts 4.9 over the keel's underside |
| Planking | 190 cubes a side, 0.125 block thick; the keel 0.475 |
| Floors | `Deck`: 125 thin cubes, the fore and aft platforms' top 2.2 over her lowest point; her side stands only 0.5 over them |
| Rig | one mast to 15 blocks over the keel; a yard about 14 across; the sail in three states (furled, half, set) |
| For another mod | `Collision` 21 boxes, `HideWater` 147 planes, `Seats`, `Controls`, 40 `AtachmentPoints`, `BalistaAttachment` |
| As cells | 9 cells across at her widest; 299 planking cells a side; 20 pieces under 4 litres |

- **Class:** open. She is 2.7 blocks deep, so there is no deck to walk under.
- **Scale:** she is about one and a half times a real knarr (Skuldelev 1 is about 16 × 4.8 m), which gives her the
  cargo room she was designed for. At 1 block = 1 m she is either kept as a large knarr or scaled by 0.65.
- **How she would be imported:** depth `look`, the sidecar of section 5.4. Her strakes are clinker cubes already,
  in game textures. The generator would add what she lacks: planking thickness, floor timbers, crossbeams, a
  working square rig and a side rudder from the library.
- **What she shows:** a model made by a player for the game reads cleanly (named groups, block units, game
  textures) and needs a ten-line sidecar. A knarr of the author's own would more likely be designed from a find's
  survey with the `norse-clinker` tradition, and a model like this one used as a reference for her look.
- **If she herself were ever wanted in Ships:** Gary's word on publishing and credit first; then her scale and her
  waterline.

## 7. What survives

| Keep as is | Adapt | Retire, once the replacement works |
|---|---|---|
| `ShipMotion`, sailing, helm, crews, `DeckMover`, `VoxelGrid`, pieces and mounts, gun laying, wrecks, paint, SeaLink, the self-tests | `ShipDamage`, `ShipFlooding` (parts from pieces), `ShipRepairs` (library `bom`), `ShipRenderer` (instanced components), `ShipDefinition` (pieces instead of slot lists), the hard-coded deck and mast names | `HullSlots`, `DeckSlots`, station/band codes (after migration), `GridShips.cs`, `GridShipSlots.cs` |
| `master_fields.py` (its `sample` is the importer's seed); `caravel_lines.py`'s form functions; `gun.py`, `caravel_gun.py`; `caravel_fittings.py`'s geometry; `shipgen.Design`'s mesh writer; `overlap_check.py`, `mesh_contacts.py`, `game_view.py`, `tools/shipview`; `rig_study.py` as the hydrostatics stage; `model_fingerprint.py`, `compare_builds.py` | `caravel_layout.py` (its rig rules into the rig library, its UV and tile code into stage 13) | `build_caravel.py`, `slice_caravel.py`, `export_caravel.py`; the CAD parts of `tools/master`; `tools/shipgen/carrack.py` after its fitting and rig helpers are extracted; the `hansa-carrack`, `carrack` and `carrack-a` assets |
| `src/Construction`'s ideas and tests: kinds as rules, the support graph, guides, saves by address, face walls, one voxel owner | `src/Construction/Geometry.cs` (exact clipping, UV tests) | its faceted `HullForm`, the lattice, `two-bay.json`, the construction `carrack.json` |

Also kept: the runtime's boat mode (`EntityRowBoat`, the skiff's fields) for the boat class, unused until a ship's
boat is wanted.

Data that survives:

- the caravel's lines, layout, rig points and fitting places, and the chalk notes' decisions as overrides in her
  design file;
- Magelan's sampled lines (`models/hansa-carrack/magelan-lines.json`, within 1.1 mm mean of carrack-m) and the
  fittings inventory (`docs/carrack-m-inventory.md`).

## 8. The building-sequence idea: verdict

**The idea:** start the model build from a realistic building sequence (keel, stem and sternpost, frames,
stringers and clamps, planking, decks and beams, castles, masts, rigging), as Fundación Nao Victoria built its
replicas.

### 8.1 What the sources say

The foundation's pages (copies in `C:\VS mods\data\ship-research\nao\fnv-pages`, gathered by SH-0013) do not give a
timber-by-timber sequence. SH-0013's own summary, `building-sequence.md` in that folder, puts it in one line: only
the 1991 Victoria was built the traditional way (oak keel, stem, sternpost and frames, then planking; keel laid
March 1991, launched 22 November). The two later replicas are moulded shells with stiffeners, bulkheads and wood
sheathing added after.

- **Nao Santa María replica** (Punta Umbría, launched 16 March 2018 after about 14 months; the foundation's own page still says 2017): a **fibreglass hull lined
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

### 8.2 Meaning (a): the master model made by a construction-sequence program

**Verdict: yes. It is section 4.3.**

- The stage order gives every member a place in a dependency chain. The same chain is the support graph the game
  needs for damage and guides.
- A parameter set plus rules is historically honest for the 15th-16th century ships this mod builds, and it is the
  only way to get a Victoria, whose plans do not exist.
- Where plans or a survey exist (the 1892 Santa María, the Bremen cog), stage 2 takes an offsets table instead, and
  everything after it is unchanged.
- **Not worth doing:** bending planks, scarfs, treenails, the order of single planks, the temporary ribbands. The
  value is in named members and their order, not in simulating carpentry.
- **Frame-first is one tradition, not the rule.** A knarr or a cog is built shell-first, and so is vanilla's boat.
  The order is data per tradition (section 6.3).
- **A caution:** the rules give a plausible hull, not a pretty one. The caravel took six trial rounds of the
  author's eye to look right. The design file must keep hand overrides (a point on the stem, the counter's curve)
  on top of the rules.

### 8.3 Meaning (b): staged shipbuilding in game

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

### 8.4 One framework for both

The member list with its `stage` and `restsOn` fields **is** the building sequence. The generator runs the stages
to make geometry. The slipway runs them to make a ship. The support graph that orders them also decides what a
broken frame brings down.

## 9. Migration path from today's caravel (proposal)

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

The two new requirements add three pieces of work. None of them stands in the caravel's way:

- **A. The importer, depth `reference`** (section 5.3, steps 1-7). It can be built right after step 2, because it
  only writes a design that step 2's generator reads. First uses: a check of the generator (the caravel's game mesh
  in, her lines out, as the spike did), and the nao's form from the traced Santa María.
- **B. Traditions and the open class** (sections 6.2-6.5): the stage order as data, the clinker plank kind, the
  one-mast square rig, a side rudder; in the runtime, a dry space up to the rail instead of one `DeckY`, and
  bailing. Wanted before the first open ship (a knarr or a small cog), not before.
- **C. The importer, depth `look`** (step 8): cutting a model's own geometry by cells. Wanted only when a ship is
  to keep a modeller's look.

## 10. The carrack from Magelan (SH-0015) on the framework

- **Which way in: the generator.** Her form is `offsets` read from `models/hansa-carrack/magelan-lines.json`,
  which was sampled from Magelan's own hull formulas and matches carrack-m within 1.1 mm (mean). That is better
  than her baked 18.5 MB mesh can give (the caravel's mesh gave her lines back within 6-7 mm), and it carries no
  fittings, slots or bands with it.
- **The importer is for checking and measuring only:** sample `assets/ships/config/shipmeshes/carrack-m.json` to
  confirm the lines file, and to lift the places of fittings the inventory does not give. Not depth `look`:
  carrack-m's mesh is cut by Magelan's bands and panels, which is what is being left behind.
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

## 11. A nao on the framework

- **Form:** `masterFrame` from the treatise rules (section 8.1), scaled to a replica's published dimensions; or
  `offsets` from the 1892 Santa María plan. Santa María replica: 28.30 × 7.96 m, depth 3.49 m.
- **Or through the importer, as a reference.** SH-0013 has seven 3D Warehouse models. The one traced from plans
  (1,685 triangles, with parts named `main deck`, `forecastle`, `FRAME`) samples to 30.55 × 8.67 m, about 8 % over
  the replica both ways, so one scale factor brings it to her size. The Victoria models are rougher and their parts
  are split by material. All are references for form and deck levels at best; none is clean enough for depth
  `look`, and their licences are not checked.
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

## 12. Data each ship needs

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
| Member sequence and names | from this proposal | the same | SH-0013's `building-sequence.md`; Hazlett 2007 (to fetch) | needed |
| A 3D model to import or check against | her own game mesh | carrack-m's game mesh | seven 3D Warehouse models (reference only) | none |
| Deviations for play | have | have | to write | to write |

SH-0013's folder had, at the time of the amendment: the foundation's pages, photos, the 1892 commission's plates,
the two treatises, the Visiers and Wells papers, a kit's plans, press pages, seven 3D Warehouse models, and its own
summaries `building-sequence.md` and `dimensions.md`. Lines, keel length, draught, deck heights and mast positions
of the replicas are not published anywhere it found.

A knarr or a cog has no data yet: no survey offsets, no sources for the building order. `Knarr.bbmodel` is a
player's model, useful for her look and proportions, not as a historical source.

## 13. Risks

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
7. **"Any 3D model" invites bad input.** A model can be lopsided, mis-scaled or wrong for the period, and an
   import can look finished while being none of the ship it claims. *Mitigation:* the importer's report (asymmetry,
   gaps, parts with no role, slivers) is read before the design is accepted; depth `reference` is the default;
   credit and permission are required fields.
8. **Models made for other mods carry other mods' assumptions** (seats, attachment points, physics boxes, their own
   sail states). Dropping them is easy; expecting them to work is the mistake.
9. **Open ships are a different game.** No deck, no hold, swamping. The runtime's `DeckY` is read in many places.
   *Mitigation:* one open ship first, after the carrack, with the author's eye on how she plays.
10. **The treatise rules may not suit the grid.** The 3 m deck pitch and one frame per 2-4 m are far from a real
   nao. The generator must stretch and say so, not pretend.

## 14. Effort (rough)

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
| A. Importer, depth `reference`: readers for Blockbench, glTF and OBJ, frame, roles, form, sidecar, report | 3-5 |
| B. Traditions as data, clinker planking, one-mast square rig, side rudder; the open class in the runtime | 5-8 |
| C. Importer, depth `look`: a model's geometry cut by cells, props, re-texturing by role | 4-7 |
| A first open ship (a knarr), designed or imported, after B | 4-6 |

Steps 1-4 (17-28 sessions) put the caravel on the framework. Steps 1-3 alone are enough to start the carrack's
hull and rig offline. The importer's first depth (A) is small because the generator does the rest; B and C are
needed only when an open ship or a modeller's look is wanted.

**The cheaper alternative, not recommended:** keep the CAD pipeline, give each frame, beam and deck its own CAD
object, and make the scripts take a ship code. It is perhaps half the work up front. It keeps the 20-40 minute
loops, the silent boolean failures and AstoCAD in the build, and it still leaves each ship's placement as code.

## 15. Decisions for the author

1. **Source of truth.** A design file plus the generator. The author's words of 14 November settle half of it:
   AstoCAD "does not need to be there". To confirm: the design file replaces the CAD master model of
   `docs/master-model.md`, section 1. *Proposed: yes.*
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
17. **Any 3D model as input** through an importer that writes a design (section 5), with two depths. *Proposed:
    yes; `reference` first, `look` when a ship needs it.*
18. **Imported ships obey the grid** (levels on block borders, heights stretched and recorded) and get generated
    timbers inside by default. Or they may stay as modelled, off the grid, with planking pieces only.
    *Proposed: the grid and the timbers.*
19. **Credit and permission** recorded for every imported model and required by the importer. Who decides whether
    a download may be used: the author, per source.
20. **Three size classes** (decked ship, open ship, boat) with the limits of section 6.2. *Proposed: yes.*
21. **Boats stay with vanilla and Sea;** Ships makes decked and open ships, and a ship's boat only when one is
    wanted. *Proposed: yes; D49 stands.*
22. **Building traditions as data** (Iberian carvel, Norse clinker, cog), each with its own stage order and support
    rules. *Proposed: yes.*
23. **The first small ship:** a knarr or the campaign's cog (D15), and whether from a find's survey or from a
    model. `Knarr.bbmodel` is an example only unless the author and her maker decide otherwise.
24. **Open ships in play:** swamping over the rail and bailing as proposed, or a simpler rule.
