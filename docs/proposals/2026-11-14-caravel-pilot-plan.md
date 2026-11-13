# The caravel pilot: implementation plan

Status: **plan for work the author ordered on 14 November 2026** (backlog SH-0016). It carries out
`docs/proposals/2026-11-13-large-ship-framework.md` ("the proposal") for one ship, the caravel. The decisions it runs
on are in `docs/decisions.md`; several are pilot directions or defaults the author has yet to confirm. Nothing in
this plan is implemented yet.

Who does what:

- **The rebuild session** (Opus) executes this plan on a new branch, one work package at a time.
- **The review session** (Fable, session id `local_7cbf98ae-d1ad-4887-9d38-c6b46a8a3205`) wrote the plan, answers
  questions about it, and reviews at the points in section 9.
- **The author** looks at the stop points in section 9 and decides what section 9.3 lists.

## 1. Outcome and scope

**Outcome.** The caravel in the game is made by the new framework: a design file, a shared library and one
generator, with named members cut into cell pieces. She looks and plays as she does today, except where the author
agrees to a change. The framework is ready for a second ship's design file.

**Two halves, each ending where the branch can be merged:**

- **Part 1, offline (phases 0-4).** The generator makes the caravel's game files in *today's format*, with today's
  ids. No C# changes. No AstoCAD. Saved caravels keep working unchanged.
- **Part 2, runtime (phase 5).** The game reads members and pieces. Frames, beams and the backbone take damage,
  missing pieces have consequences, grey guides work. Hull piece ids change, with a save migration.

**Not in the pilot** (after the final review): instanced components and a compact mesh; retiring the old pipeline;
the mesh importer; the treatise hull method; open ships and other traditions; staged building in game; any ship but
the caravel.

## 2. Ground rules

1. **Branch.** One branch, `claude/caravel-pilot`, from Ships `main`, in a worktree under
   `C:\VS mods\Hansa\.claude\worktrees\`. No push without the author's word.
2. **`main` stays shippable.** Nothing from the branch is merged into `main` before a stop point says so, with two
   exceptions that touch no asset and no C#: phases 0 and 1 may be merged as soon as they are done (SH-0015 needs
   phase 1).
3. **The old pipeline is frozen, not deleted.** `tools/caravel`, `tools/master`, `tools/shipgen`, `models/caravel`
   and `src/Construction` are not edited in the pilot. The one exception is listed in work package 3.3.
4. **No C# changes before phase 5.** Part 1 must pass the existing tests with the existing code.
5. **Ids are sacred in part 1.** Every slot, fixture, gun place, hook, moving part, deck and mast keeps its code.
6. **One work package, one session, one or more commits.** Each ends with its "done when" commands run and their
   output summarised in the commit message. A work package that cannot meet its "done when" stops and asks.
7. **No ship names in the generator.** `tools/shipwright` must not contain the word "caravel" outside comments and
   tests. A check in the test suite enforces it.
8. **Determinism.** Fixed iteration order, numbers rounded on output, no dependence on hash order or random
   numbers. Two builds give byte-identical files; a test builds twice and compares.
9. **Suite rules apply** (`C:\VS mods\AGENTS.md`): nothing installed into the player's game without the author's
   word; test worlds and bench output stay out of Git; a Release build overwrites `release\ships-<version>.zip` in
   the worktree, so the version in `modinfo.json` is not bumped by the pilot until a stop point agrees a release.

## 3. The framework's layout

```
shipyard/                            new: what the framework owns that is not code
  README.md                          how to check, build and compare a ship (short)
  SCHEMA.md                          the design file's schema and its change log
  ships/
    caravel/
      design.json                    the ship (schema 0)
      lines.json                     her hull as offsets: the 52 waterlines of models/caravel/caravel-lines.json
      baseline/                      frozen reference numbers from the CAD pipeline (small files, committed)
      golden.json                    hashes of the generator's outputs, updated when a phase is accepted
      carryover.json                 phase 3 only: parts still copied from the frozen assets
  library/
    traditions/iberian-carvel.json   stage order, member types used, support rules
    members.json                     member types: scantling keys, material, bill of materials, damage and repair class
    components/<kind>/<id>.json      a component's parameters, anchor rule, moving parts, clearance envelope, bom
    rigs/caravela-latina.json        the rig plan
  build/                             git-ignored: build/<code>/ holds every output of a build
tools/
  ship.py                            launcher: puts tools/ on the path and calls shipwright.cli
  shipwright/                        the generator, a Python package
    cli.py                           check, build, parity, views
    design.py                        schema 0: load, defaults, validate
    frame.py                         coordinates and cells; conversions to the old model's and the game's frames
    form.py                          hull form methods: offsets, midbody-ends
    field.py                         half-breadth fields: build, erode, intervals, cut by cells, voxels
    stages/                          one module per stage: backbone, frames, planking, inner, decks, topsides, rig,
                                     fittings, layout
    members.py, pieces.py            member and piece records; one owner per voxel; slivers
    mesh.py                          polygons clipped to cells, metric UVs, tile splitting, border faces
    components.py, clearance.py      the library's components and the generic clearance check
    gamedata_v0.py                   writes the game's files in today's format, with today's ids
    checks.py                        invariants of any ship
    parity.py                        comparisons with a ship's baseline
tests/shipwright/                    pytest: one file per module; tests marked "parity" need the baseline
```

- **Python 3.12 with numpy** (and pytest for tests). Nothing else. No AstoCAD, no FreeCAD module.
- **Commands** (from the worktree's root):

  ```bash
  python tools/ship.py check caravel
  ```
  ```bash
  python tools/ship.py build caravel
  ```
  ```bash
  python tools/ship.py parity caravel
  ```
  ```bash
  python -m pytest tests/shipwright -q
  ```
- **Code that may be imported from the old tools**, read-only: `tools/shipgen/shipgen.py` (`Design`, the mesh
  writer), `tools/shipgen/gun.py`, `tools/caravel/caravel_gun.py`, `tools/caravel/caravel_fittings.py`,
  `tools/caravel/surface_uv.py`, `tools/caravel/overlap_check.py`, `tools/caravel/mesh_contacts.py`,
  `tools/caravel/game_view.py`. Geometry worth keeping is copied into the library in phase 4; until then it is
  imported. `tools/shipgen/carrack.py` is never imported directly: what `caravel_fittings.py` takes from it
  (`rope_path`, `ring_points`, `disk`, `annulus`, `block_at`, `helix`) is copied into `shipwright` when first needed.

### 3.1 One frame for everything new

| | New frame (design, members, pieces) | Old model (`caravel-lines.json`, `cells.json`) | Game (`config/ships/*.json`) |
|---|---|---|---|
| Unit | m = blocks | mm | blocks |
| Along | x to the bow | X to the bow | x to the bow |
| Up | y from the bottom cell border | Z from the bottom cell border | y from the waterline |
| Across | z to starboard | Y to **port** | z to starboard |

- A cell is `[i, j, k]`: `i = floor(x)`, `k = floor(y)`, `j = round(z)`. The centre cell `j = 0` spans z -0.5 to
  0.5. `j` is positive to starboard.
- The old model's cell is `[i, -j, k]`. Today's rib slot id `rib:-14:-2:3` is the new cell `[-14, 2, 3]`.
- Game y = new y - `levels.waterline` (2.875 for the caravel).
- `frame.py` is the only place these conversions are written. `gamedata_v0.py` and `parity.py` use it.

## 4. The design file, schema 0

`shipyard/ships/caravel/design.json`. Every number comes from today's scripts; the table after it says where.
Where this plan and a script disagree, the script wins and the rebuild session says so.

```jsonc
{
  "schema": 0,
  "code": "caravel", "name": "Caravel", "rev": 1,
  "class": "decked",
  "tradition": "iberian-carvel",
  "principal": { "lengthOverall": 25.63, "beam": 6.6, "draft": 2.75 },      // checked against the build, not inputs
  "levels": { "underside": 0.125, "hold": 1.0, "waterline": 2.875, "main": 4.0, "quarterdeck": 7.0 },
  "form": {
    "method": "offsets", "file": "lines.json",          // phase 2 starts here; "midbody-ends" replaces it in 2.2
    "midbodyEnds": {
      "section": { "y": [0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 5.25, 6.5, 8.25, 9.5],
                   "halfBreadth": [1.6, 2.05, 2.35, 2.72, 2.96, 3.12, 3.22, 3.28, 3.30, 3.27, 3.10, 2.92, 2.62, 2.42] },
      "midbody": [-4.5, 0.5],
      "stem": [[0.0, 5.6], [0.25, 6.85], [0.6, 7.55], [1.0, 8.0], [1.6, 8.45], [2.5, 8.95], [3.5, 9.45],
               [4.5, 9.88], [5.0, 10.0], [5.5, 10.05], [6.0, 10.02], [6.5, 9.95], [8.0, 9.6], [9.5, 9.2]],
      "stern": { "postX": -14.5, "rake": 0.0, "transomFoot": 2.4,
                 "counter": { "postTop": 3.2, "transomX": -15.5, "footY": 3.6 },
                 "transom": { "y": [2.4, 2.6, 3.0, 3.5, 4.0, 5.25, 7.0, 8.25, 9.5],
                              "halfWidth": [0.0, 0.75, 1.35, 1.75, 1.98, 2.2, 2.25, 2.15, 2.0] } },
      "fullness": { "bow": [0.95, 1.75, 5.0], "run": [0.8, 1.0, 2.4] },
      "postHalfWidth": 0.1, "loftTop": 9.5
    },
    "sheer": { "waist": 5.0, "quarterdeck": 8.0, "breakFrom": -7.0, "breakLength": 2.5,
               "bowRise": { "from": 4.0, "over": 6.0, "by": 1.0 } }
  },
  "scantlings": { "plank": 0.2, "deckPlank": 0.2,
                  "frame": { "sided": 0.2, "moulded": 0.4, "floorTop": 0.8 },
                  "backbone": { "sided": 0.2, "moulded": 0.6, "stemProud": 0.25, "stemHead": 1.0 },
                  "beam": { "sided": 0.2, "moulded": 0.4, "seat": 0.1 },
                  "bulwark": 1.0 },
  "framing": { "frames": [-14, -12, -10, -8, -6, -4, -2, 0, 2, 4, 6] },
  "decks": [
    { "id": "hold", "level": "hold", "kind": "floor", "beams": "none" },
    { "id": "main", "level": "main", "from": "stern", "to": "stem", "beams": "frames" },
    { "id": "quarterdeck", "level": "quarterdeck", "from": "stern", "to": -7.0, "beams": "frames",
      "side": "bulwark", "front": "railing" }
  ],
  "openings": [
    { "id": "hatch-main", "type": "hatch", "deck": "main", "x": [-5.0, -3.0], "z": [-0.5, 0.5], "coaming": 0.25 },
    { "type": "gunport", "deck": "main", "cells": [-1, 1, 3], "side": "both", "sill": 0.25, "head": 1.0, "inset": 0.125 },
    { "type": "gate", "deck": "main", "cells": [-3], "side": "both" },
    { "type": "hawse", "x": 6.75, "side": "port" }
  ],
  "rig": {
    "plan": "caravela-latina",
    "masts": { "main":   { "cell": -3,  "heel": 1.0 },
               "mizzen": { "cell": -11, "heel": 3.8 } },
    "sails": { "main":   { "tack": [8.1, 6.5],   "peak": [-10.2, 29.8], "clew": [-6.4, 9.1] },
               "mizzen": { "tack": [-7.6, 9.35], "peak": [-13.6, 27.5], "clew": ["bumpkin", 9.25] } },
    "bumpkinOut": 2.8
  },
  "fittings": [
    { "c": "gun/carriage-bronze", "deck": "main", "cells": [-1, 1, 3], "side": "both",
      "codes": "gun-{side}-{n}" },
    { "c": "gun/carriage-bronze", "deck": "quarterdeck", "cells": [-13, -11], "side": "both",
      "codes": "gun-{side}-quarterdeck-{n}", "crewOffset": { "2": [-0.65, 0, 0] } },
    { "c": "pump/bronze", "deck": "main", "cell": [-2, 0] },
    { "c": "capstan/plain", "deck": "main", "cell": [6, 0] },
    { "c": "wheel/drum", "deck": "quarterdeck", "cell": [-9, 0] },
    { "c": "rudder/stern-stock", "tiller": "under-quarterdeck" },
    { "c": "anchor/cathead", "side": "port", "x": 8.0 },
    { "c": "figurehead/hook", "pos": [10.12, 5.475, 0.0], "rake": 20.0 }
  ],
  "stairs": [
    { "id": "quarterdeck", "cell": [-7, 2], "foot": [-6.0, 4.0], "head": [-7.0, 7.0] },
    { "id": "hold", "cell": [-5, 0], "foot": [-4.0, 1.0], "head": [-5.0, 4.0] }
  ],
  "layout": { "pieces": "see work package 4.4: the galley and cabin of caravel_lines.KITCHEN, hooks, start pieces" },
  "overrides": [],                                       // chalk-note details the rules do not produce; each names its note
  "allow": [],                                           // rule failures the author accepts, each with a reason
  "deviations": [
    { "what": "decks 3 blocks apart", "why": "2 clear blocks for a player" },
    { "what": "underside 0.125 above a cell border", "why": "she passes over a bottom 3 blocks down" },
    { "what": "sails' feet 2 blocks over the decks", "why": "head room for players" },
    { "what": "masts upright", "why": "each mast keeps to its cell" }
  ]
}
```

Where the values come from:

| Design field | Source today |
|---|---|
| `levels` | `caravel_lines.py`: `LIFT`, `Z_HOLD`, `Z_WL`, `Z_DECK`, `Z_QD`; `caravel-lines.json` `levels` |
| `form.midbodyEnds.section` | `SEC_Z`, `SEC_B` |
| `form.midbodyEnds.midbody` | `XM_A`, `XM_F` with `LONGER` = 2.0 |
| `form.midbodyEnds.stem` | `STEM` (pairs are height, x) |
| `form.midbodyEnds.stern` | `LAYOUTS["D"]`: `stern_post_x`, `counter`; `TRANSOM_FOOT_Z`, `TR_Z`, `TR_E` |
| `form.midbodyEnds.fullness` | `bow_q`: `smooth(0.95, 1.75, z / 5.0)`; `run_q`: `smooth(0.8, 1.0, z / 2.4)` |
| `form.sheer` | the function `sheer(x)`: `Z_WAIST_RAIL`, `Z_QD + QD_SIDE`, `QD_FRONT`, `BREAK_LEN`, the bow rise |
| `scantlings` | `SKIN`, `DECK_T`, the rib, backbone and beam constants (lines 440, 541-544, 627-628), `RAIL` |
| `framing.frames` | `RIBS` |
| `decks` | `QD_FRONT`, `QD_BULWARK`; `caravel-design.md`, "Vertical rules" |
| `openings` | `HATCH`, `GUN_CELLS`, `PORT`, `GATE_CELL`, `FITTINGS["hawse"]` |
| `rig` | `MASTS`, `LAYOUTS["D"]["sails"]`, `bumpkin_out`; `build_caravel.py` `SKETCH`, `RIG_P` for heights and radii |
| `fittings` | `GUN_CELLS`, `QD_GUN_CELLS`, `FITTINGS`; gun codes and gun places from the frozen `caravel.json` |
| `stairs` | `STAIRS` with `stairs_y` (old Y -2.0 is new z +2, the starboard side) |
| `layout` | `KITCHEN`; `caravel_layout.py` for hooks, start pieces, cleats and the rest |

- Heights in `rig.sails` and `stairs` are from the bottom cell border, as in `caravel-lines.json` `layout`
  (divided by 1000).
- The figurehead's `pos` above is the frozen game position `[10.12, 2.6, 0.0]` moved to the new frame (y + 2.875).
- `caravel-lines.json` `layout` already holds most of these values resolved for variant D. Work package 1.2
  compares the design with it field by field.
- Unknown keys are an error. Every key has a default or is required; `SCHEMA.md` lists them.

## 5. What a build writes

In `shipyard/build/<code>/`:

| File | What |
|---|---|
| `members.json` | one record per member: `id`, `type`, `material`, `stage`, `restsOn`, `carries`, `required`, `pieces` |
| `pieces.json` | one record per piece: `id` (`<type>:<i>:<j>:<k>`), `member`, `cell`, `litres`, `voxels`, `bom`, mesh range |
| `field.npz` | the half-breadth fields and each member's interval, for tests and views |
| `definition.json`, `mesh.json` | the game's two files in today's format (phase 3 on) |
| `report.json` | dimensions, hydrostatics, volumes per member type, slivers, clearances, bill of materials, timings |
| `parity.json` | every comparison of section 8 with its number, its limit and pass or fail |

A build of the whole ship must take under 60 seconds on the author's PC. `report.json` records the time.

## 6. Phases and work packages

Each work package is sized for one session. "Depends" names what must be done first.

### Phase 0: freeze and baseline

**0.1 Freeze.** *Depends: nothing.*

- Do:
  1. Create the worktree and branch. Tag the branch point: `git tag -a baseline/caravel-cad-2026-11-14 -m "The
     caravel as the CAD pipeline last built her"`.
  2. The exporter's base files are git-ignored and exist only in `C:\VS mods\Hansa\models\caravel\export\`
     (`caravel-def-base.json`, `caravel-mesh-base.json`, 1 November) with `models\caravel\cells.brep`. Copy all
     three to `C:\VS mods\work\ships-caravel-baseline\` and record their sha256.
  3. Prove the base is the one the committed assets came from: run the old layout step on it into a scratch folder
     and compare with the committed assets.

     ```bash
     CARAVEL_LAYOUT_BASE="C:/VS mods/work/ships-caravel-baseline" CARAVEL_LAYOUT_OUT="<scratch>" python tools/caravel/caravel_layout.py
     ```
     If the two files are not byte-identical to `assets/ships/config/ships/caravel.json` and
     `assets/ships/config/shipmeshes/caravel.json`, stop and ask: the base must then be rebuilt with the old
     exporter in AstoCAD (about 12 minutes), which needs the review session's word.
  4. Run and keep the baseline test results (the full output as text files under `baseline/`):

     ```bash
     dotnet run --project tests/ConstructionTests/ConstructionTests.csproj
     ```
     ```bash
     dotnet run --project tests/HansaTests -c Release -- caravel-all
     ```
     ```bash
     python tools/caravel/overlap_check.py
     ```
     and the server self-test (`tools/run_selftest.sh` in Git Bash, after Release builds of Ships with
     `-p:SeaRoot="C:\VS mods\Sea"` and of Sea). Expected today: `caravel-all` all groups ok but `slots` (2 known
     carrack-m failures); self-test 396 ok, 2 failed (SH-0011).
  5. Write `shipyard/ships/caravel/baseline/`: `README.md`; `hashes.json` (sha256 of the two assets, the three base
     files, `caravel-lines.json`, `cells.json`, `caravel-report.json`, `caravel.FCStd`); `bodies.json` (volumes and
     centroids from `caravel-report.json` `massProps`, the envelope, the waterline block); `slots.json` (every hull
     and deck slot's id, cell, box and material from the frozen definition); `collision.json` (the frozen `solid`,
     grid origin, size, voxel size); `codes.json` (every fixture, gun place, hook, moving part, mesh group, paint
     zone, deck, mast and room code); `shots.json` (the cameras of `models/caravel/views/game/shots.json` and of the
     note folders).
- Done when: the tag exists; the layout step reproduces the committed assets byte for byte; the baseline folder is
  committed; the test outputs match the expected results above or the difference is reported.

### Phase 1: the design file

**1.1 Package, schema, validator.** *Depends: 0.1.*

- Do: `tools/ship.py`, `tools/shipwright/{cli,design,frame}.py`, `shipyard/SCHEMA.md`, `shipyard/README.md`,
  `shipyard/library/traditions/iberian-carvel.json` (stages and member types only, no rules yet),
  `tests/shipwright/`. Add `shipyard/build/` to `.gitignore`. `check` loads a design, applies defaults, validates
  types, ranges and references (a deck's level exists, a frame cell is inside the hull's length, a mast's cell
  holds no frame, an opening's cells exist), and prints the resolved design.
- Done when: `python -m pytest tests/shipwright -q` passes; `check` rejects each of ten deliberately broken copies
  of a sample design with a message naming the field; the no-ship-names check exists.

**1.2 The caravel's design.** *Depends: 1.1.*

- Do: `shipyard/ships/caravel/design.json` and `lines.json` (the `hull` and `sheer` of `caravel-lines.json`,
  converted to the new frame: metres, z = -Y). A parity test compares every design value with
  `caravel-lines.json` (`levels`, `layout`) and with the frozen definition (masts, decks, gun codes).
- Done when: `python tools/ship.py check caravel` passes; the parity test passes; every row of section 4's source
  table has been checked against its script and any difference from this plan is listed in the commit message.

Phases 0 and 1 may be merged into `main` now. **SH-0015 can start gathering the carrack's data from here**
(section 10).

### Phase 2: the hull as members and pieces

**2.1 Form and field.** *Depends: 1.2.*

- Do: `form.py` method `offsets`; `field.py`: the outer field `w(x, y)` on a node grid (default 12.5 mm; a setting),
  linear along and between waterlines as the CAD's ruled loft; the sheer cut; the stern profile, counter and
  transom cut. Hydrostatics in `report.json`. The spike
  `docs/proposals/2026-11-13-large-ship-framework-spike.py` is the starting point.
- Done when: displacement, envelope volume, bounding box, waterline length and beam meet section 8.

**2.2 The parametric form.** *Depends: 2.1.*

- Do: `form.py` method `midbody-ends`: port `caravel_lines.py`'s `B`, `shape_z`, `real_z`, `x_stem`, `x_transom`,
  `x_post`, `e_transom`, `smooth`, `bow_q`, `run_q`, `g`, `waterline`, `stern_profile`, `offset_profile` and
  `sheer`, taking every constant from the design. Then switch the caravel's design to it; `lines.json` stays as the
  reference.
- Done when: every point of the 52 generated waterlines is within 0.01 mm of `lines.json`; work package 2.1's
  numbers are unchanged.

**2.3 Backbone and frames.** *Depends: 2.1.*

- Do: stages `backbone` (keel, stem, sternpost as three members; the stem proud of the planking and its head) and
  `frames` (one member per frame cell, as a band square to the skin, with its floor across the bottom; the rules of
  `caravel_lines.py` `rib_profiles`, lines 466-538, and `backbone`, 550-592). The inner surfaces by ball erosion
  (`caravel_lines.inner_waterline`, lines 419-437, and `tools/master/master_fields.erode`).
- Done when: volumes and cell sets of Backbone and Ribs meet section 8; the frames' moulded depth measures 0.398 to
  0.400 everywhere, as `ribCheck` in `caravel-lines.json`.

**2.4 Planking.** *Depends: 2.3.*

- Do: stage `planking`: the skin as the outer field minus its erosion by the plank thickness; cut by the backbone;
  openings cut out (gun ports, gates and their rebates, the hawse, the rudder stock's hole, the bumpkin's socket).
  The quarterdeck stair's recess in the bulwark is the first entry in `overrides` (`caravel-design.md`, "The
  quarterdeck stair against her side"). One plank piece per cell.
- Done when: the Shell's volume and cells meet section 8; no piece crosses a cell border; the sliver list is in
  the report.

**2.5 Beams and decks.** *Depends: 2.3.*

- Do: stages `inner` (deck beams at every frame under the main deck and the quarterdeck, seated 0.1 in their
  frames; carlings and headledges round the hatch) and `decks` (hold floor, main deck, quarterdeck; planking stops
  at the frames; the hatch's opening).
- Done when: volumes and cells of Beams and Decks meet section 8; every deck's top is exactly on its level.

**2.6 Topsides, pieces, voxels.** *Depends: 2.4, 2.5.*

- Do: stage `topsides` (cap rails with their inboard width at each frame, the quarterdeck's front railing, the
  transom's cap); `pieces.py` (each solid 0.25 voxel has exactly one owning piece; slivers under 4 litres are
  listed and never moved); `checks.py` (watertight below the waterline, every `restsOn` resolves, no voxel with two
  owners, no piece outside its cell); `parity.py` against `cells.json` and the frozen collision grid;
  `iberian-carvel.json` gets its support rules.
- Done when: `python tools/ship.py parity caravel` reports every hull line of section 8; the build takes under 60
  seconds; two builds are byte-identical.

**Review R1 here** (section 9.1).

### Phase 3: the game's files for the hull

**3.1 Mesh.** *Depends: 2.6.*

- Do: `mesh.py`: each piece's faces from the lines' own polygons, clipped exactly to its cell; a face on a cell
  border is drawn only where the neighbour does not cover it (`docs/master-model.md`, section 7); metric UVs at 16
  texels a block with the grain rules of `export_caravel.py` (`cap_uv`, the strake parametrisation at lines
  481-536) and `surface_uv.py`; quads split at atlas tiles (`caravel_layout.split_tiles`).
- Done when: `overlap_check.py`'s test passes on the hull mesh with 0 overlapping pairs; the quad count of each
  hull group is within 10 % of the frozen mesh's; views of the bare hull from the baseline cameras exist.

**3.2 The old-format writer.** *Depends: 3.1.*

- Do: `gamedata_v0.py` writes what `export_caravel.py` writes for the hull: collision `solid`, `hullSlots`,
  `deckSlots` (deck cells, rib cells, beam lengths), `beamFrames`, `frames`, `decks`, `masts`, `hullSamples`,
  `waterClip` with its dry outline, `rooms`, the `slot:*`, `exterior` and `interior` mesh groups, `slotQuads`.
  Every id is the baseline's. This writer is the **only** place that knows today's accidents: the station/band
  hull ids come from `baseline/slots.json` by cell, and a sliver with no slot of its own is folded into the
  neighbouring slot here and nowhere else.
- Done when: the id sets equal `baseline/slots.json` and `baseline/codes.json` for everything this package writes;
  the collision grid meets section 8.

**3.3 The hybrid build.** *Depends: 3.2.*

- Do: `carryover.json` lists every mesh group and definition entry that the generator does not make yet (rudder,
  tiller, yards, gun parts, gate leaves, rope ladders, the gun fit data). `gamedata_v0.py` copies those from the
  frozen base files and writes complete base files into `shipyard/build/caravel/base/`. The old layout step then
  runs on them, unchanged, through its `CARAVEL_LAYOUT_BASE` and `CARAVEL_LAYOUT_OUT` variables. If
  `caravel_layout.py` needs a change to run from the new base, that one change is allowed, small, and reported.
- Done when: the layout step writes a full definition and mesh in the build folder; a structural comparison with
  the frozen assets (every code, every count, the grid) is in `parity.json`.

**3.4 The switch on the branch.** *Depends: 3.3.*

- Do: copy the built assets over `assets/ships/config/ships/caravel.json` and `shipmeshes/caravel.json` on the
  branch. Run everything of work package 0.1's step 4. Render the baseline cameras from the new mesh
  (`tools/caravel/game_view.py <shots.json> <out dir>`) beside renders of the frozen mesh.
- Done when: the test results equal the baseline's, group by group; the render pairs are in
  `shipyard/build/caravel/views/` with an index page.

**Stop point S1 here** (section 9.2).

### Phase 4: the library

**4.1 Components and clearance; the gun.** *Depends: 3.4.*

- Do: the component format (`library/components`), `components.py`, `clearance.py`. A clearance envelope is the
  volume a component sweeps through its motions. The check intersects it with member pieces, other components'
  solids and envelopes, start pieces and the rig. The carriage gun becomes the first component, from `gun.py` and
  `caravel_gun.py`; `build_caravel.py`'s run-out search (lines 1009-1113) becomes the generic placement search.
  Lids and ports follow the gun.
- Done when: the ten guns' fixtures, moving parts, gun places and keep-clear boxes equal the frozen ones (codes
  identical; positions within 1 mm, or explained); the guns leave `carryover.json`.

**4.2 Steering, ground tackle, pump.** *Depends: 4.1.*

- Do: components for the rudder with its stock and tiller, the wheel with its ropes, the capstan, the cathead and
  anchor with its cable, the pump with its pipe. Geometry from `caravel_fittings.py` and `caravel_layout.py`.
- Done when: their definition entries and mesh groups match the frozen ones as in 4.1 and leave `carryover.json`.

**4.3 The lateen rig.** *Depends: 4.1.*

- Do: `library/rigs/caravela-latina.json` and stage `rig`: masts, yards, bellied sails, shrouds and ratlines,
  halyards, sheets, tacks, cleats, the bumpkin, `rigCheck`, the dip data. The checks of `rig-study.md`: 2.2 of head
  room under every sail in every pose, no sail through a mast, clews clear of the stair.
- Done when: `sails`, `masts`, `rigCheck`, the `rig:*` and `sail:*` groups and the yards' moving parts match the
  frozen ones; `dotnet run --project tests/HansaTests -c Release -- lateen` passes.

**4.4 Deck furniture and the default layout.** *Depends: 4.1.*

- Do: gates, the hatch with its coaming and leaves, both stairs, rope ladders, hooks, start pieces, the modular
  pieces of the cabin and galley, `keepClear`, paint zones, `borderPlaces`, `layoutRev`. The lantern rule of
  section 7.
- Done when: `modularPieces`, `hooks`, `startPieces`, `keepClear`, `paintZones` and `paintQuads` match the frozen
  ones, except the mizzen lantern's hook if section 7 moves it.

**4.5 The generator alone.** *Depends: 4.2, 4.3, 4.4.*

- Do: `carryover.json` is empty and deleted. `caravel_layout.py` is no longer run. `gamedata_v0.py` writes the two
  asset files itself. The bill of materials is summed from members and components and compared, in the report
  only, with `docs/proposals/caravel-charter-bom.md`. `golden.json` is written.
- Done when: all of work package 0.1's step 4 equals the baseline; `python tools/ship.py build caravel` is the
  whole build, under 60 seconds, byte-identical twice.

**4.6 The timbers she lacks (shown, not merged).** *Depends: 4.5.*

- Do: as design options that are off by default: a keelson, clamps under the beams' ends, wales, knees. Render
  each from the baseline cameras, inside and out, with the change in volume and bill of materials.
- Done when: the renders and numbers are ready for the author. Nothing is switched on.

**Stop point S2 and review R2 here.** Part 1 can be merged into `main` after them.

### Phase 5: the runtime reads members and pieces

Starts only after S2 and R2. Its work packages are outlines; the review session details them at R2, from what part 1
taught.

- **5.1 The data and the interface.** The generator also writes members and pieces for the game. C# types load
  them. `IShipStructure` (the proposal, section 4.4) is added as an adapter over today's slot layouts. No behaviour
  changes. *Done when:* every existing test result is unchanged.
- **5.2 Pieces as the source; new ids; migration.** Slot layouts are built from pieces. Hull ids become
  `plank:<i>:<j>:<k>`. The generator writes the table from old ids to new; the runtime applies it once to a saved
  ship and stamps the design's `rev`. *Done when:* copies of real saved caravels load with every slot state, damage
  value and laid piece where it was. **Stop point S3 before this is merged.**
- **5.3 Damage and repair for timbers.** Frame and beam pieces take states; keel, stem and sternpost become pieces;
  repair costs come from `library/members.json`.
- **5.4 Support and guides.** The member graph decides what a missing piece weakens or drops; grey guides show
  missing required pieces.
- **5.5 Names and leftovers.** The hard-coded deck and mast codes (`Wreck.cs:317`, `PennantRenderer.cs:58`, the
  `top` deck) become data.
- **5.6 Close.** `caravel-design.md` as a short current spec; the Ships card; `AGENTS.md`'s build section; the full
  self-test; the final parity report.

**Final review R3 and stop point S4 here.**

## 7. The mizzen lantern and the gun

SH-0012 found that the default lantern on the mizzen's band (hook `lantern-mizzenmast`, game position
`[-10.5, 6.685, -0.7]`, its arm to port) hangs in the working room of `gun-port-quarterdeck-2`. A caravel launched
without guns refuses that one gun until the lantern is taken down. The author has not chosen between leaving it,
moving the arm, and hanging the start lantern elsewhere.

- **The rule (pilot default).** The default ship is checked by the same rules as a player's change. A start piece
  inside a component's working room is a build error.
- **In work package 4.4:** the clearance check must report this conflict by itself; that is its first real test.
  The generator then tries the hook's arm aft, forward and to starboard of the mast, and takes the first that
  clears both quarterdeck guns' rooms and the rig on both tacks. The hook keeps its code, so a saved lantern stays
  on it.
- **For the author at S2:** renders of the lantern before and after, and the three choices. Leaving it as it is
  means one `allow` entry in her design.

## 8. Parity: what is compared, and the limits

The limits are targets set from the proposal's spike. A limit that cannot be met is a question for the review
session, with the measured number and the reason, not something to loosen alone.

| What | Reference | Limit |
|---|---|---|
| Waterlines, method `midbody-ends` | `lines.json`, 52 waterlines | every point within 0.01 mm |
| Displacement below the waterline | 263.9 m³ (`caravel-report.json`) | within 0.1 % |
| Envelope volume | 710.17 m³ | within 0.2 % |
| Length overall, beam, draft | 25,628 mm, 6,600 mm, 2,750 mm | within 5 mm |
| Volume of each body | Shell 78.2887, Decks 47.3064, Ribs 11.4221, Beams 6.1995, Backbone 3.7668 m³ | within 1 % each; over 3 % stops the work |
| Cells holding each body | `cells.json`, ignoring its 93 sliver records | the same set; every difference listed with its litres |
| Volume per cell and body | `cells.json` | 95 % of cells within 2 % or 2 litres; every other cell listed with a reason |
| Collision grid | the frozen `solid` | differing voxels under 0.5 % of the solid ones, listed by region; none on a deck's top |
| Slot, fixture, gun place, hook, moving part, group, zone, deck, mast and room codes | `baseline/codes.json`, `slots.json` | identical sets |
| Positions of fixtures and moving parts | the frozen definition | within 1 mm, or explained |
| `ConstructionTests`, `caravel-all`, the server self-test | the baseline outputs | the same result for every group and check |
| Coplanar overlaps (`overlap_check.py`) | 0 | 0 |
| Quads per mesh group | the frozen mesh | within 10 %; reported, not a gate |
| Renders from the baseline cameras | the frozen mesh's renders | looked at pair by pair by the session, then by the author at S1 and S2 |
| Build time | | under 60 s |
| Two builds | | byte-identical |

## 9. Reviews, stop points, questions

### 9.1 Reviews by the review session

| | When | What is reviewed |
|---|---|---|
| **R1** | after phase 2 | the generator's structure before three phases are built on it: the schema, the member and piece records, `field.py`, the parity report. This is the cheap moment to change course, so it is worth an intermediate review |
| **R2** | after phase 4 | part 1 as a whole; the plan for phase 5 is detailed here |
| **R3** | after phase 5 | the finished pilot, as the author asked |

For a review the rebuild session sends: the branch and commit, `parity.json`, the test outputs, the list of
deviations from this plan, and its open questions.

### 9.2 Stop points for the author

| | When | The author sees | The author decides |
|---|---|---|---|
| **S1** | after 3.4 | render pairs old against new; the test results; on request a bench zip of the branch to try her in game | whether the generated hull is the caravel; whether phase 4 goes ahead |
| **S2** | after 4.6 | the fully generated caravel; the lantern's three choices; the timbers she lacks; the bill of materials against the charter's | the lantern; which timbers to add, if any; merging part 1 into `main`; whether a release follows |
| **S3** | in 5.2 | the migration's test on copies of saves | that published caravels are migrated so, or that old ones stop loading |
| **S4** | after R3 | the review's findings; the pilot in game | merging part 2; the directions and defaults of `docs/decisions.md`; retiring the old pipeline; the next ship |

Work does not continue past a stop point on its own. Between S1 and S2 the offline work of phase 4 may go on while
the author looks, since it changes nothing she has seen until 4.5.

### 9.3 Who answers what

- **The review session:** what the plan or the proposal means; the schema; names; how an old script should be
  read; a parity limit that cannot be met; any deviation from this plan; whether a detail is an `override` or a
  rule.
- **The author, through the stop points or the coordinating session:** anything that changes how she looks or
  plays; the lantern; added timbers; save migration; merges into `main`; versions, releases and pushes; installing
  into the game; deleting or retiring anything; every line of `docs/decisions.md` marked direction or default.
- **Nobody needs asking** for: internal structure of a module, test design, refactoring inside `tools/shipwright`.

## 10. The carrack's data (SH-0015)

- **When:** as soon as phases 0 and 1 are in `main`. It needs only `python tools/ship.py check`.
- **Against:** schema 0, as `shipyard/ships/carrack/design.json` with her `lines.json` from
  `models/hansa-carrack/magelan-lines.json`. Data only: levels, frame cells, decks, openings, rig measures,
  fittings and their places, deviations, and the open questions of `docs/master-model.md` section 9 put to the
  author. No build, no generator changes.
- **Schema changes:** the pilot bumps `schema` when it changes a key and records the change in
  `shipyard/SCHEMA.md` with how to update a design. The carrack's file follows. What she needs that schema 0 lacks
  (castles, a forecastle's overhang, frames in bays of 3, a second gun deck, a square rig) is written down as
  requests for the review session, not added by SH-0015.
- **Her build** starts after R1 at the earliest, better after S2.

## 11. After the pilot

In this order, each on the author's word: instanced components and a compact mesh; the importer at depth
`reference`; retiring `build_caravel.py`, `slice_caravel.py`, `export_caravel.py`, `caravel_layout.py`, the CAD
parts of `tools/master`, `GridShips.cs` and the unused carrack assets; the carrack's build.

## 12. Things that will bite

- **`CARAVEL_ROOT`.** The old scripts default to `C:/VS mods/Hansa`. In a worktree set it, or they read and write
  the main checkout.
- **Port and starboard.** The old model's Y is to port; the game's z and the new frame's z are to starboard. Use
  `frame.py`, never a bare minus sign. The id test of work package 3.2 catches a flipped side.
- **Heights.** The old model and the new frame count from the bottom cell border; the game counts from the
  waterline.
- **The ruled loft.** The CAD hull is linear between waterline polygons and along them. The field must
  interpolate the same way, or cell volumes drift by more than the limits.
- **Slivers.** The CAD slice moved 76 pieces into neighbouring cells. The new pieces do not move; only
  `gamedata_v0.py` folds them, to keep today's slots.
- **`caravel-design.md` is partly stale** (it says 565 cells; there are 670). The scripts and the frozen files are
  the truth.
- **Large files.** The two assets are 39 MB together. Do not commit build output; `shipyard/build/` is ignored.
- **Untracked files in the main checkout** (`models/hansa-carrack/*`, `testserver-grid-pieces.json`) belong to
  other sessions.
- **The `Release` build** needs Sea's Release DLL (`-p:SeaRoot="C:\VS mods\Sea"`) and overwrites the worktree's
  zip of the same version.
