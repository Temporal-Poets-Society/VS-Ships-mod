# Master hull model: constraints and rules

Status: draft for review, 17 October 2026. The user's direction of 17 October 2026 replaces the
generator-built hull (`hansa-carrack` from carrack-m's generator, 16–17 October) and the
construction core's authored faceted form. v1 of the model is built:
`models/hansa-carrack/hansa-carrack.FCStd`. Its build and checks are in section 10.

## 1. The pipeline (the user's plan, 17 October 2026)

1. **A master model** is the ship at full surface resolution, built in AstoCAD (a FreeCAD 1.1 fork,
   same API, installed at `C:\AstoCAD`). It is the stored source and the user may edit it. A
   one-off script builds the first version from Magelan's lines. After that the model, not a
   script, is the truth.
2. **A generic slicer** cuts the model's structure bodies (those whose `SliceRole` is `structure`)
   by the block grid at build time. The envelope and the reference geometry are never sliced. The
   default block of a cell is whatever falls in that cell. It has no ship-specific code.
3. **Baked blocks** are the slicer's output, stored in the mod. The game never slices; it
   assembles stored pieces.
4. **Ghosts.** A cell's default block is its ghost. A missing required block shows as a grey
   guide (AGENTS.md).
5. **Replacement blocks.** A window, gun port or gate is the cell's own default piece with an
   opening cut out, plus its fitting. It changes the ship's data, not the model.
6. **Every hull block is removable** and may be patched with another timber species, then
   replaced.
7. **Mega blocks.** Masts with sails, the rudder, the wheel and the pump are multi-cell structures
   with an anchor cell.
8. **Order.** The plain hull first: the shell, keel, ribs, beams and decks. It has no stairs,
   railings, masts, windows, pumps, capstan, guns or other fittings (the user, 17 October). Then
   replacement blocks, then Magelan's fittings.

This supersedes, in `docs/proposals/ship-design-architecture.md`:
- §4 ("derive the ship blocks at load … does not ship baked megabytes"; "Python and JS stop
  computing geometry");
- decision 2 as authored geometry. The faceted look (corners on a half-block grid, few slopes)
  becomes a slicer setting applied to the smooth master.

The other section 8 decisions stand.

## 2. Units and frame

| | |
|---|---|
| Unit | 1 block = 1000 mm. The model is in millimetres. |
| Model X | Ship's length, toward the bow: game ship-local x. |
| Model Y | Athwartships, positive to **port**: game −z, so the frame stays right-handed. |
| Model Z | Up. Z = design y + 3500 mm, so Z = 0 is the hull's underside. |
| Cell borders | Every whole metre in X and Z. In Y at ±500 mm, ±1500 mm, …: the centreline runs through the middle of a cell, and the keel's cells are Y −500..500. |

## 3. Vertical rules

| Level | Z (mm) | Source |
|---|---|---|
| Hull underside, flat floor; the keel flush in the bottom | 0 | Decision 14 |
| Bottom block: planking 0–250, floor timbers, hold floor | 0–1000 | Decision 14 |
| Hold floor top | 1000 | Decisions 13, 14 |
| Lower deck floor top | 4000 | |
| Waterline: 0.875 above a cell border, so every deck lies on a world block border at rest | 4875 | Decision 13 |
| Main deck floor top | 7000 | |
| Quarterdeck and forecastle deck floor tops | 10000 | |
| Poop deck floor top | 13000 | |

- **Every deck** has 2 clear blocks over its floor and 1 structural block under it (beams and
  planking). Decks are 3 apart. (AGENTS.md; the user, 15 October.)
- **Draft** is 4875 mm. It was Magelan's 4375; decision 14 accepted the difference.

## 4. Plan and length rules

- **Beam:** Magelan's widest is 2 × 6070 mm, just above the waterline. That is within 13 cells
  (|Y| ≤ 6500).
- **Masts** are mega blocks in one cell each:

  | Mast | Cell (X, mm) | Note |
  |---|---|---|
  | Mizzen | −13000..−12000 | |
  | Main | 3000..4000 | Moved one cell forward, decision 12 |
  | Fore | 17000..18000 | |

- **Ribs** (AGENTS.md, decision 12):
  - Each rib occupies one cell along X. The visible timber may be narrower and form a T with the
    hull.
  - Gaps between ribs are 3 clear cells as the base, with 1 clear cell on each side of every
    mast and possibly 1 at the bow and stern ends.
  - So every rib is on an even cell and every mast on an odd one.
  - v1's rib cells (cell start X in blocks): −16, −14, −12, −10, −6, −2, 2, 4, 8, 12, 16, 18,
    20, 22, 24.

    | Ribs | Gaps |
    |---|---|
    | −14 and −12 | Either side of the mizzen |
    | 2 and 4 | Either side of the main mast |
    | 16 and 18 | Either side of the fore mast |
    | −16, and 20, 22, 24 | 1-cell gaps at the stern's and bow's ends (decision 12). The bow's carry the forecastle's overhang. |
    | Everything else | 3 clear cells |

    These are a proposal. The model keeps each rib's cell as its own object, so moving one is an
    edit.
- **Castle deck edges end on a beam** (Magelan notes 184/185: "move floor … to ribs so have proper
  roof beam"). The edge beam is the deck's last rib cell, and the deck ends at that cell's face:

  | Edge | X (mm) | Edge beam's rib cell | Change from Magelan |
  |---|---|---|---|
  | Poop front | −9000 | −10 | Unchanged |
  | Quarterdeck edge | −1000 | −2 | 1 block forward (Magelan −2000) |
  | Forecastle edge | 16000 | 16 | 2 blocks forward (Magelan 14000) |

  The fore mast then stands between the forecastle's beams 16 and 18.
- **Deck edges, fronts and sheer breaks are three different lines.**
  - **Deck edge:** the end of the castle deck's planking, at the edge beam's face (above).
  - **Front:** the cabin's partition under it, standing on a cell face. Magelan's `POOP_FRONT` −8750
    is the waist face of a 250 wall whose other face is on −9000. Fronts are fittings, not in v1.
  - **Sheer break:** as carrack-m, each castle's side stays at full height a quarter block past its
    deck edge, then sweeps down to the waist's height. v1's breaks start at −750, 15750 and −8750
    (Magelan's: −1750, 13750, −8750).

## 5. The hull envelope: Magelan's lines

These are sampled from carrack-m's own functions (`tools/shipgen/carrack.py`, with HANSA, no
facets). That is her shape with decision 14's deeper bottom.

- **Section:** a flat floor 2 × 3600 at Z 0 and a round bilge. The half-breadth is 6070 at the
  waterline and widest at about 6080 at Z 5200. Tumblehome continues all the way up, to about 3400
  at the poop's rail.
- **Bow:** round, its waterlines meeting the stem square. The stem is raked, with a forefoot arc
  of radius 2000 into the flat floor.
- **Stern:** raked, closing to the sternpost below Z 4125 and a flat transom above it.
- **Poop overhang:** above the quarterdeck's underside (Z 9750) the poop cabin's sides carry the
  hull's lines 1250 further aft.
- **Forecastle:** its sides step out over the bow to a point at X 26500. Its underside is level at
  Z 8250 to the stem, then raked up to Z 9500 at the tip.
- **Sheer (top of the side):**

  | Part | Top Z (mm) | Above its deck |
  |---|---|---|
  | Waist bulwark | 8250 | 1250 |
  | Quarterdeck and forecastle | 11250 | 1250 |
  | Poop | 14250 | 1250 |

  Each castle break sweeps between levels on a quarter ellipse.
- **The master is smooth.** Faceting is the slicer's job (section 1).

## 6. Bodies in the model

| Body | v1 | What it is |
|---|---|---|
| `Envelope` | yes | Closed solid: the hull's outside surface, cut at the sheer. The shape the user edits. |
| `Shell` | yes | The hull's skin: everything within 250 mm of her outside (bottom planking 0–250, decision 14). One layer. |
| `Backbone` | yes | The keel, stem and sternpost as one timber, 250 sided (\|Y\| ≤ 125) and 750 moulded inward from the outside. The keel is flush in the bottom block (decision 14), and the shell's planking stops at it. |
| `Ribs` | yes | In each rib cell, a timber 250 sided at the cell's middle, 250 moulded inside the shell, from the bottom to the sheer. Across the bottom it is a floor timber, Z 250–750, butting the keel. The skin across the whole cell plus this timber forms the T. |
| `Beams` | yes | Under each deck, in every rib cell inside that deck's length: 250 sided, 500 deep under the planking, from rib to rib. |
| `Decks` | yes | Planking 250 thick, its top on the deck level. It reaches the shell's inside between the ribs and butts the ribs. Hold floor Z 750–1000 over the whole hold; lower deck and main deck over the whole length; quarterdeck from the stern to −1000; forecastle deck from 16000 to the tip; poop from the stern to −9000. No openings: hatches and wells come with replacement blocks and stairs. |
| `Reference` | yes | Not geometry for slicing: cell grid, deck levels, waterline, mast cells, rib cells, castle edges. |
| `Lining` | later | An optional flush ceiling for chosen regions (decision 10). |

- **Ribs protrude inside** (decision 10). A flush lining stays an option for chosen regions.
- **Editable inputs:**
  - The envelope is lofted through waterline sketches. Each is the port half of a waterline,
    split where Magelan's formulas change: x −6000 (the run starts) and 9000 (the bow starts). Each
    piece is one B-spline fitted within 10 mm of Magelan's samples.
  - Heights: every 125 mm round the bilge and the forefoot (Z 0–1000), every 250 mm from 1000 to
    6000 (where the run and the bow change fastest), one at 4125 (the transom's start), then every
    500 mm. That is 38 lower-hull, 13 upper-hull and 16 forecastle waterlines.
  - Three lofts: the lower hull up to the overhang's underside (Z 9750), the upper hull from it,
    and the forecastle. The upper hull's after pieces are the poop's, and its forward pieces are
    the same curves as the lower hull's, so the two meet exactly.
  - The forecastle's underside and the sheer are profile sketches in the centreline plane.
  - Each rib is a box (its X position and timber width), and each deck is a box (its planking's X
    and Z range, and whether it has beams).
  - A spreadsheet holds the thicknesses.
  - Edit any of these, then run the rebuild macro.
- **Ends:** the stem, the sternpost below the transom and the forecastle's tip are faces 250 mm
  wide. Every waterline's ends blend out to a half-breadth of 125 (w + 125·exp(−w/125)), so the
  sides never meet in a knife edge.
- **How the bodies with thickness are made: half-breadth fields, not CAD offsets.**
  - Why not CAD offsets: OCC's solid offset failed on the lofted hull, on her ends, at the
    near-tangent seams, and even on a plain midship piece. FreeCAD 1.1's document booleans also
    rejected fuses ("hasher mismatch") that the shapes' own booleans make valid.
  - A hull is y-convex: at every (X, Z) she spans |Y| ≤ w(X, Z), tumblehome or not. The rebuild
    slices her uncut half hull every 62.5 mm (1/16 block) of height and samples w every 62.5 mm
    along X.
  - The inside surfaces are erosions of w by a ball (`tools/master/master_fields.py`): the
    shell's inside at 250, the ribs' at 500, the backbone's at 750. They are correct at her ends,
    her bottom and the overhang's and forecastle's undersides. The ball is sampled on the grid, so
    each surface is within about 6 mm of the exact one.
  - Where a body's interval changes by rule across a line, it is made of parts that meet on that
    line. Examples: a deck is narrower inside a rib's cell; a rib's floor timber runs in to the
    centreline; the shell stops at the backbone. Without parts, the node grid would leave wedges
    there.
  - Each body is an interval of |Y| at each (X, Z) node, so no two bodies overlap by construction.
  - The bodies appear in AstoCAD as meshes. The fields are saved next to the model
    (`hansa-carrack-fields.npz`) for the slicer.

## 7. Slicing rules (the next stage, recorded here so the model suits them)

- **Cells.** A cell is a 1000 mm cube on the grid. A body's piece in a cell is body ∩ cell.
- **One master block per cell** (decision 6). The pieces of all bodies in a cell form its one
  block, for example a skin piece plus a rib piece makes a rib block.
- **Slivers stay in their own cell.** A piece is never given to a neighbouring cell: that block
  would reach outside its cube, and removing it would change another cell (AGENTS.md "clip
  geometry to actual 3D cells"; decisions 5 and 6). The slicer flags any piece under a size
  threshold, and the fix is made in the master: a nudged line, a moved edge, or a different
  approximation.
- **Faces on cell borders are drawn only while the neighbouring cell is filled with a matching
  face.** Only the overlapping part of the face is hidden, and openings and glass count as not
  filled. An intact hull shows only its outer and inner surfaces. A removed block shows its
  neighbours' cut edges, and no rims.
- **Shared edges** are meshed once, from the same field samples on both sides of a cell border,
  so there are no cracks. Volume sums alone can't show compensating overlaps and gaps, so the
  slicer also checks for open edges.
- **Textures.** Planks run along X. UVs are metric at 16 texels per block (`docs/grid-contract.md`),
  continuous across cells, with deliberate seams and atlas tiling. Where a face is across the
  grain (end faces), the grain falls back to Z.
- **A cell's block** is one composite: one kind, one condition, its support data, and the walls
  and fittings it reserves on its faces.
- **Approximation.** The fields' resolution is the approximation. Corners on the half-block grid
  (decision 2's faceted look, the accepted default) means sampling the fields every 500 mm. Finer
  sampling is a slicer setting, so we can compare in game.

## 8. Checks on the v1 model

- The `Envelope` is a valid closed solid.
- The envelope matches Magelan's sampled half-breadths within 10 mm.
- The underside is flat at Z 0, the widest point is within |Y| ≤ 6500, and the waterline is at
  4875.
- Report the displacement below Z 4875, the length and the beam.
- No two bodies share any |Y| at any node, and no body reaches outside the envelope.
- Render outside and inside views: profile, plan, body plan, three-quarter views of the bow and
  stern, and cut-away views of the structure.

## 9. Open questions for the user

1. **Bulwark heights.** Magelan's bulwarks stand 1250 above each deck. The first 1000 fills the
   first clear row above the deck, and the last 250 pokes into the second. That gives a row of
   250 mm sliver cells along every rail. Should the bulwarks top out at 1000 (on a cell border)
   instead, with the rail cap inside that block? v1 keeps 1250 until this is decided.
2. **Castle edges and rib cells** (section 4). v1 moves the quarterdeck edge 1 block and the
   forecastle edge 2 blocks forward so each ends on a beam. The alternative for the forecastle is
   12000, which makes the waist 4 blocks shorter.
3. **Ends 250 wide.** The stem, the sternpost below the transom and the forecastle's tip are
   250 mm faces (section 6). Change if wanted.
4. **Draft and shallow water** (raised by the caravel session). Vanilla water stands 0.875 up its top block, so a hull
   whose underside is on a cell border (draft 4875) touches the bottom in 5 blocks of water. The caravel lifts its
   underside 125 mm to pass in one block less (its decks and waterline stay on the grid). v1 keeps the carrack's
   bottom on Z 0, as decision 14 has it.
5. **The caravel's structure rules for the carrack?** On 17 October the user set these for the caravel
   (`models/caravel/caravel-design.md`):
   - a rib every 2nd block (masts in the clear cells);
   - ribs, beams and backbone 0.2 wide × 0.4 deep;
   - the keel slightly proud, more at the bow, the stem head curling back;
   - a beam at every rib under each deck;
   - hull and deck planking 0.2 thick;
   - the bulwark 1 block above the deck.

   The carrack's v1 follows decision 12 (bays of 3) and this spec (250 timbers and planking, the keel flush
   per decision 14, bulwarks 1250) until the user says otherwise.

## 10. v1 build, 17 October 2026

From the Hansa folder, with `C:\AstoCAD\bin\AstoCADcmd.exe`:

| Step | Command | Time |
|---|---|---|
| Sample Magelan's lines (once) | `python tools/master/sample_lines.py` | 4 s |
| Create the model (once; `MASTER_FORCE=1` overwrites it) | `AstoCADcmd.exe -c "exec(open(r'tools/master/create_master.py').read())"` | 5 min |
| After editing it in AstoCAD | Macro `tools/master/Rebuild_master.FCMacro`: rebuilds and saves, with its fields | 4–5 min |
| Views | `python tools/master/ship_view_data.py`: the structure from the saved fields on a 125 mm grid, in the shared ship viewer (`tools/shipview`, the caravel's too), bundled as one page, `models/hansa-carrack/hansa-carrack.html` | 30 s |

The checks report `"ok": true`:

| Check | Result |
|---|---|
| Waterline fits | Within 9.8 mm of their samples |
| Envelope vs Magelan's outside (720 points) | Mean 1.1 mm, 95 % within 3.3 mm, 4 over 10 mm, worst 17 mm (at the forecastle's 25 mm inset where it meets the hull) |
| Envelope | One valid solid. X −18162..26500, half-breadth 6084 at most, Z 0..14250 |
| Displacement below Z 4875 | 1424.8 m³ (Codex checked it independently: 1424.9 m³ from the envelope, block coefficient 0.69) |
| Bodies (closed meshes) | Shell 279 m³, decks 281 m³, beams 35 m³, ribs 26 m³, backbone 9 m³ |
| Overlaps | No two bodies share any grid quad |
| Containment | No body outside her |

Codex reviewed the spec and then the code. Its findings were fixed:
- export roles;
- slivers never moved to another cell;
- the stern notch;
- the overhang's junction;
- mesh winding, which was inside out;
- the wedges and gaps where bodies meet;
- the inflated volume figures;
- sampling above her top;
- the sheer read from the profile's edges;
- the fuse checked by inclusion and exclusion;
- the macro saving the model with its fields.

Found on the way:
- A boolean can fail silently. The forecastle once fused to nothing while staying "valid", because its loft seam lay exactly on the hull's surface. The fix is a 25 mm inset fading out over 2 blocks, and the rebuild now checks every fuse's volume.
- A peer session building a caravel found the same with booleans on periodic-spline lofts.

Not done:
- the slicer;
- replacement blocks;
- fittings.

The viewer draws the meshes double-sided, and the deck and beam meshes keep internal faces where their parts meet. Neither matters for viewing, but the slicer must not copy them.

