# Grid construction contract

Status: first prototype implemented; engine binding pending.
Date: 14 October 2026.

## Scope

The default hull itself changes. The old carrack is a visual and mechanical
reference, rather than an immutable outer mesh. Preserve its overall character,
castles and mast arrangement. Standardize the rebuilt hull, ribs, aft windows,
deck openings and default interiors around the grid. Players receive a complete
default layout and can alter supported interiors and openings.

## Ownership and visible shape

Every persistent module is identified by integer cell address `cell:x:y:z`.
Cell origin is the blueprint grid origin plus that address. The compiler clips
convex prescribed volumes against all six cell planes, including curved-hull
approximations that cross more than one transverse cell. A cell can own multiple
planar faces and kinds of visual parts. Inventory materials are ordinary planks
and glass, with the blueprint determining their geometry.

The first slice has rib cells 0, 4 and 8. They own complete longitudinal cells;
the visible timber is initially 0.375 wide, and the viewer also compiles 1-wide
timber from the same data. Hull faces may occupy the rest of the rib cell. No
inventory items are created for these geometry variants.

The profile has a small crease within a cell to demonstrate multiple planes in
one owned cell. That shape detail does not create another placement grid.
The cross-section is a proposed section, 13 cells across at its widest, with
hold/lower/main floors 3 blocks apart. Full-ship dimensions remain to be reviewed.

## Textures

Each planar polygon carries an orthonormal surface basis. U follows the projected
longitudinal wood grain; V is perpendicular on the same plane. UV distances equal
physical distances at 16 texels per block. Tests measure every polygon edge after
clipping and serialization precision, including a hand-calculated diagonal face.
UVs are unwrapped. Any texture-atlas adapter must tile/split at repeat boundaries,
not normalize or clamp these coordinates. Facet seams are deliberate; a surface
curved in two directions cannot generally have both distortion-free UVs and no seams.

## Construction state

`GridAssembly` uses the inherited `SlotState` sound/cracked/empty values. Required
keel/rib/beam cells are damageable and removable. Missing required cells can be
queried for a shape guide; the intended blueprint does not restore removed pieces.
Collision is a derived union of the placed cells' quarter-block voxels. Collision
voxels have exactly one cell owner; rendered selection should eventually use faces.

Windows, ports and gates are restricted to suitable non-structural upper hull cells.
The first model validates material class and sealing/open state. Their specific
meshes, hinges and inventory transactions remain engine-adapter work. Hatch cells
are optional deck modifications; a shut hatch is solid and an open one is not.

The transverse stair has a three-cell run and three-block rise. It fits within
the first clear bay and can be moved to the second. The state model checks all
12 treads with a 0.4-wide, 1.85-high player envelope and verifies both landings.
A stair through the middle rib is rejected. Reinforced alternatives have not yet
been implemented; add explicit support alternatives rather than bypassing that check.

Saves store changes by cell address and the stair location. Visible rib width
changes preserve cell identities. Save/load does not yet migrate old Magelan ships.
The preview's plank replacement is a construction demonstration; C# state is the
runtime authority once the engine adapter is connected.

## Preserved mechanics

The independent fork retains sailing, steering, wave motion, deck movement,
boarding, crew, sleep/logout handling, map, vanilla working blocks, cooking,
cannons, damage, repairs, flooding, wrecks, painting and network code. It builds
against the existing Sea mod, without rebuilding or changing that sibling project.
Legacy definitions and tools remain reference fixtures. Four inherited obsolete
API warnings are not geometry or construction failures.

## Next implementation slice

1. Review the actual cross-section, visible timber width and clear-bay stair fit.
2. Connect the generic grid state to an in-game construction test entity: material
   transactions, face targeting, derived collision and chunked face rendering.
3. Add shaped missing-part guides in ship-local coordinates and authoritative state sync.
4. Adapt shot/repair damage to cell identity so a rib hit damages the rib itself.
5. Connect live sealing and water clipping; the open-ended slice is not buoyancy evidence.
6. Define support alternatives around larger wells before allowing framing substitutions.
7. Extend the same definition format to a bow, stern, castles and standardized aft windows.

Do not claim the two-bay slice is launchable: it has open ends and no masts,
steering, hydrostatics or multiplayer binding. The preserved mechanics have not
yet been validated against this new geometry.
