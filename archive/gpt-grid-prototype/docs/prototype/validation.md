# First prototype verification

14 October 2026. Design work remains uncommitted for render review.

- Hansa builds under its own assembly, asset domain, registration names and channel.
- 9 geometry tests pass: real 3D cell clipping, exclusive collision ownership,
  planar faces, metric UVs, atlas tiling without area/density loss, deck/beam fit,
  stable identities across visible rib widths, deterministic output and data-only bay changes.
- 19 C# construction checks pass: default hatch, stair headroom/landings and
  relocation, blocked framing, damage/removal, repair with ordinary planks,
  fitting restrictions, sealing and operation, required guides and save/load.
- The actual preview script runs with a DOM/canvas adapter; its controls work and
  all checked canvas coordinates are finite. This is a smoke check, not a full browser test.
- Textured perspective renders use the preserved ship preview renderer and installed
  game textures. The cutaway, structural-width comparison, section and interior/stair
  views were visually inspected. Unwrapped UVs are split into atlas tiles before sampling.
- The inherited mechanics suite still ends with the 3 previously documented failures:
  Sea stern dry outline, legacy hole reveal edges and note 32's panel.
- SHA-256 audit: all 273 copied source/assets/test/tool files in Magelan are unchanged.

The construction model and browser preview are implemented. Engine targeting,
material transactions, live rendering, damage/repair integration, water clipping,
multiplayer and launch validation for the new geometry remain pending. Open slice
ends and absent rigging make these renders construction evidence, not sailing evidence.
