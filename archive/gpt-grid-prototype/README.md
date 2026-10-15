# Archived: first grid prototype (GPT, 14 October 2026)

Superseded reference, kept for comparison and to be deleted later. It is not compiled into
the mod (the build only includes `src/**`), and its scripts and tests are not run.

This is the first two-bay construction slice: Python compiler, canvas preview, matplotlib
and textured renders, the C# `Hansa.Grid` state model and its tests. The files were moved
here as they were. Relative paths inside the scripts (`ROOT = parents[2]`, test project
references) still point at the old layout, so they do not run from this folder.

`docs/grid-contract.md` is a copy taken at archive time. The live contract stays in
`docs/grid-contract.md` at the project root.

Why it was set aside: `docs/proposals/ship-design-architecture.md`, section 2. In short:

- one state per cell while cells mix several parts;
- accidental ownership and slivers from chopping solids along cell planes;
- a single extruded section that cannot express bow, stern or castles;
- 1.35 MB of baked output for 9 cells, with geometry in three languages;
- a painter-sorted canvas preview.

Worth reusing:

- the geometry invariants: planar faces, metric UVs measured after clipping, exclusive
  collision voxels, stable cell ids;
- the stair headroom and landing check with a 0.4 × 1.85 player envelope.
