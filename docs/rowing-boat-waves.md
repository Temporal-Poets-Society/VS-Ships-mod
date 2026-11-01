# Rowing boat water and waves — Ships 0.1.0-dev.23

**Superseded for new acquisition by Ships dev.25:** the author withdrew this experimental boat from crafting,
creative inventory and the handbook. The implementation below remains for saved-boat compatibility.
[Withdrawal and package verification](../../overview/verification/ships-publication-dev25.json).

The author's pre.13/dev.22 playtest showed the cut spilling outside the rowing hull. The old centre-only native
check did not establish boundary correctness. Dev.23 replaces the rounded waterline profile with a fitted 4 × 32
height-dependent dry volume baked from the existing oriented Gunwhale/HullUpper/ProwBeam wood boxes. The true
beam centre is 0.0625 blocks across from the native origin. The cut spans model heights 0.02–0.98, stopping above
the floor and below the gunwale; no flattening ring is added.

`tools/shipgen/rowboat_waterclip.py` writes `assets/ships/config/rowboat-waterclip.json`, including the source model
hash. The runtime loads this asset rather than duplicating dimensions/formulas. The native `hidewatersurface`
client behavior is removed in the entity asset and asset generator: Sea is the single water cut. The generated
hull, paint, oars, boarding and collision are unchanged.

Sea pre.14's `BoatWaves.ApplyToClip` moves the outline with the actual renderer's pitch/roll, including native
turn/forward lean, offsets and pivot. Native seats/camera/interaction targets share the same wave integration.
The manifest requires Sea pre.14 because this is a compiled integration. Custom ships/caravel retain their own
motion; no legacy identity/save migration is implied.

Verification: 192,421 geometric sections show no exterior cut or uncovered free interior at the sampled resolution.
328 actual native-renderer boundary points pass with combined lean and wave angles; hidden-GPU tests compare the
fitted CPU/shader volumes at level, tilted and flooded poses. The native Sea-only control contains no Ships package
or assembly. Exact package/native records are in `overview/verification/boat-storm-fixes.json` and the boundary plot
in `overview/verification/rowboat-hull-fit.png`. Full graphical-client acceptance remains pending.
