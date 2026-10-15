# Hansa working contract

The user named this project **Hansa: The Last Voyage** and authorized a separate
folder and the first cross-section/two-bay prototype. Magelan is a read-only reference.

## Accepted construction rules

- Keep the carrack's overall character, castles, mast arrangement and useful
  mechanics. Redesign the default hull, ribs, windows and openings to fit the grid.
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
(milestone M1): one master block per cell, and every edit changes exactly one cell. No grid ship
exists in game yet; keep that explicit until targeting, rendering, collision, inventory,
damage, water clipping, support validation and multiplayer adapters are connected.

The inherited baseline is unfinished 0.3.30 source with older assets. Do not
regenerate or repair every historic design while working on the new grid slice.
Keep historical notes under `docs/legacy`, and use `docs/grid-contract.md` for current work.

## Verification

Run `tests/ConstructionTests` after changing `src/Construction`, the kind library or a design.
Build Hansa after runtime changes. Validate previews visually when geometry changes.
Sea is referenced through its existing build and must not be modified as a build side effect.
Do not copy bin, obj, test worlds or old release packages from Magelan.
