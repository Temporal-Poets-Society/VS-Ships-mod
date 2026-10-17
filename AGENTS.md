# Hansa working contract

The user named this project **Hansa: The Last Voyage** and authorized a separate
folder and the first cross-section/two-bay prototype. Magelan is a read-only reference.

## Accepted construction rules

- Keep the carrack's overall character, castles, mast arrangement and useful
  mechanics. The Magelan carrack is the general example for looks and dimensions: decks 3 apart (2 clear +
  1 structural block), the bottom one whole block (planking, floor timbers, hold floor, the keel flush
  inside it), tumblehome, and the waterline 0.875 above a cell
  boundary so decks meet piers level. Redesign the default hull, ribs, windows and openings to fit the grid.
- One ship-local block grid, shared by every deck and component. Minor dimensions
  inside a cell are aesthetic geometry, not alternative placement grids.
- Rib cells occupy one longitudinal block, with 1 or 3 clear cells between them: bays of 3
  as the base, 1 clear cell on each side of every mast, and 1 possibly at the bow and stern ends.
  Visible timber may be narrower and its cell may include hull surfaces.
- Players place ordinary materials. The blueprint supplies the hull shape.
  Do not introduce player-facing hull slope/shape block inventories.
- Planar surfaces may vary by location and a cell may contain multiple planes.
  Clip geometry to actual 3D cells; do not confuse projected width with surface length.
- Texture density follows physical surface distance. UVs may require tiling and
  deliberate seams; never clamp a long face to one atlas tile.
- Keel, ribs and beams must become removable and damageable. Required means a
  construction/support requirement, not invulnerability.
- Default ship comes complete. Players may rearrange interiors, move stairs and
  create deck hatches within support, hull and mast limits.
- Grey missing-part guides show unmet requirements, not every absent default piece.
- Layout changes belong in ship data. The compiler is generic; new component
  behaviour may need code, but moving a window or rib must not need carrack-generator edits.

## Current boundary

The first grid prototype is archived in `archive/gpt-grid-prototype` (not built,
to be deleted later). The ship-design model is in `docs/proposals/ship-design-architecture.md`;
only its section 8 decisions are agreed. `src/Construction` is the pure construction core
(milestone M1): one master block per cell, and every edit changes exactly one cell.
Since 17 October 2026 (the user, after testing that build) the ship is a **master hull model** in AstoCAD
(a FreeCAD 1.1 fork at `C:\AstoCAD`), sliced later into one-block cells: `docs/master-model.md`, the model in
`models/hansa-carrack/`, its tools in `tools/master/`. The model is the source once the user edits it; its plain
hull (shell, backbone, ribs, beams, decks) is built and checked, and the slicer, replacement blocks and fittings are
not done. Until the slicer exists, the game still uses the generator-built `hansa-carrack` below.
From 16 October 2026 `hansa-carrack` was built by carrack-m's own generator, adjusted to stand on the
block grid: `HANSA_MODULAR=1 HANSA_SHIP=hansa-carrack python tools/shipgen/carrack.py` (add
`HANSA_FAST=1` for a 90-second build that skips the guns' arc sweep; ship only full builds). Every
change from carrack-m is behind `HANSA` in the generator and listed in `docs/carrack-m-inventory.md`;
carrack-m's own files stay untouched. The user's direction: carrack-m is the reference, changed with
common sense where blocks need it (windows, ports, guns); stairs and some inner layout will change on
the user's word. The construction-core ship from `src/Ship/GridShips.cs` is now `grid-carrack`
(`"load": false`, offline checks only). Her hull panels are one block each; her bottom row, the wales'
row, castle rows under mouldings and her ends are still fixed. Editing her ribs, beams and keel, guides
and block saves are not connected; keep that explicit.

The inherited baseline is unfinished 0.3.30 source with older assets. Do not
regenerate or repair every historic design while working on the new grid slice.
Keep historical notes under `docs/legacy`, and use `docs/grid-contract.md` for current work.

## Verification

Run `tests/ConstructionTests` after changing `src/Construction`, the kind library or a design.
Build Hansa after runtime changes. Validate previews visually when geometry changes: re-run
`tools/HansaPreview` and inspect the views (headless Chrome can capture `two-bay.html#<view>`).
Sea is referenced through its existing build and must not be modified as a build side effect.
Do not copy bin, obj, test worlds or old release packages from Magelan.
