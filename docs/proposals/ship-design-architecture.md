# Ship design architecture — proposal

Status: **proposal; decisions in section 8 are agreed, the rest is not.** Written 15 October 2026 after reviewing the first
grid prototype (`src/Grid`, `tools/gridship`, `designs/two-bay.json`) and the inherited
Magelan runtime. Nothing here changes `docs/grid-contract.md` until the user accepts it.

## 1. Is the fork right?

Yes. The fork itself is sound:

- The new geometry cannot load old saved ships anyway. Slot ids, stations and bands all change.
  A separate identity lets the rebuild break things without risking Magelan players.
- Roughly half of the inherited runtime does not depend on hull geometry and is worth keeping
  (section 7).
- The name and lore fit.

What went wrong is **how** the fork was done. All of Magelan's geometry stack was copied:
generator, hull slots, deck slots, bands, the surfaces map and the damage skin. The new grid
was then added *beside* it. Two geometry systems now coexist, and the copied one is the only
one connected to the game. A clean slate should mean one geometry core. The kept mechanics
should be moved onto it one by one, and the old geometry code should be deleted once its last
user has moved.

## 2. Why the first prototype is not the right base

Its invariants are good and worth keeping: planar faces, metric UVs, one owner for each
collision voxel, and stable cell ids. Its model has five structural problems. The numbers below
were measured from the prototype's compiled output, now
`archive/gpt-grid-prototype/assets/gridships/grid-two-bay.json`.

1. **What a cell contains is a side effect, not a declared kind.** 168 of 435 cells contain 2–4
   different kinds of part. For example, 18 cells hold beam + deck + hull + rib, and 60 hold
   beam + deck. One state per cell is fine in itself; section 3 keeps it. The problem is that
   nobody chose these combinations. They are whatever the solids happened to overlap, so no rule
   can say what that cell is, what replaces it, or whether a hatch may go there.
2. **Ownership is accidental.** `compile.py` generates hard-coded solids (shell band, rib,
   bottom, keel, deck, beam) and then chops them along cell planes. Whatever falls into a cell
   belongs to it. This produces slivers:
   - 6 rib-foot cells own geometry but no collision at all;
   - 183 cells have more than six planes.
3. **The format cannot grow into a ship.** The design is one constant `halfSection` extruded
   along the length, with exactly one stair (`+z` only) and one hatch hard-coded in the schema
   and in `GridAssembly`. A bow, stern, castles or a second stair need new compiler code. That
   breaks the rule that layout changes belong in data.
4. **The output is baked, and in three languages.** Nine cells of length compile to 1.35 MB of
   JSON. A 36-block carrack would be roughly 6 MB or more before castles. Geometry exists in
   Python (compiler), JavaScript (the viewer redraws the section itself) and C# (state). These
   will drift apart.
5. **The 3D preview cannot be correct.** `viewer.template.html.in` draws with Canvas 2D,
   sorting faces by average depth (painter's algorithm). Long decks, ribs passing through decks
   and intersecting faces will always overlap wrongly, and the faces are flat colours.
   Separately, the matplotlib `two-bay.png` uses the same kind of sorting. The textured PNGs come
   from yet another renderer.

Because these problems are in the model itself, more patching of this prototype would repeat
the grind you had in Magelan. The recommendation is to keep its tests and lessons and replace
its model.

## 3. Proposed model: one master block per cell

```
 ShipDesign (authored, a few KB)                  Runtime, per ship
 ┌─────────────┐  shapes   ┌───────────────┐     ┌───────────────┐
 │ Form        │─────────▶ │ Ship blocks    │     │ State         │
 │ hull shape  │           │ one per cell:  │◀────│ changes by    │
 ├─────────────┤  kinds    │ kind + shape,  │     │ cell address  │
 │ Layout      │─────────▶ │ faces, voxels, │     │ (kind, cond-  │
 │ block map   │           │ support rules  │     │ ition, open)  │
 └─────────────┘           └───────────────┘     └───────────────┘
 ┌─────────────┐                    │
 │ Block kinds │                    ▼
 └─────────────┘      render · collide · target · seal · damage · guides
```

**Locality is the core rule.** Changing one cell, in game or in the design file, changes only
that cell. Its neighbours update only the faces they share with it. Removing the aft windows
means changing those cells' kind from `window` to `planks`. Nothing else is redesigned.

### Form: the shape of hull blocks only

- **Frames:** the rib cells along x, validated to have 1 or 3 clear cells between them.
- **Stations:** half-section outlines at chosen x positions.
- **Profiles:** keel line, stem, sternpost, sheer, and castle outlines.

The form only decides the *shape* of blocks on the hull envelope. It never decides what kind of
block sits in a cell.

**Faceted look (decided).** The hull is deliberately blocky and faceted, with visible edges on
curves, suiting Vintage Story. It does not copy Magelan's smooth curves.
- Corners sit on a half-block grid.
- Slopes come from a small fixed set.
- Most blocks have 1–2 planes; only tight turns at the bow and stern get more.
- Flat faces of neighbouring blocks that line up are merged when meshing, so the hull costs few
  triangles.
- A validator flags any cell that receives only a tiny clipped corner. The designer nudges a
  point in the data.

Roughness is one parameter, so it can be refined later on the real ship.

### Ship blocks: one master block per cell

Each cell holds at most one ship block. A block has a **kind**. Its **shape** is derived from
where it sits: hull shape from the form, deck height from the deck, and so on. The block is
damaged, broken and repaired as a whole.

| Kind | Typical place | Contains |
|---|---|---|
| `planks` | hull side | skin following the form |
| `rib` | rib cell on the hull | rib timber + skin; visible rib width is a parameter (0.375 or 1) |
| `deck` | deck cell | deck planking |
| `beam` | deck cell in a rib column | beam + deck planking above it |
| `deck-edge` | where deck meets side | deck planking + skin |
| `keel` | bottom centre | keel timber + bottom skin |
| `window`, `gunport`, `gate` | hull side | skin with an opening and its fitting |
| `hatch`, `grating` | deck cell | an opening fitting in the deck |

Walls, doors and ladder-stairs are **fittings**, not cell kinds (section 8, items 9 and 11).
A wall or door stands on a cell face and takes neither cell. A stair takes the cells it stands
in.

Consequences, accepted:

- A shot that breaks a rib block also opens a hole there.
- A hatch cannot go through a beam block. Hatches and stairs go in the clear cells between ribs,
  and 3 clear cells leave room.
- `deck-edge` blocks are needed to keep the hull watertight, so the rules do not let players
  remove them freely.

Loose furniture and vanilla blocks aboard (chests, beds, lanterns, working blocks) stay separate
from ship blocks, as today. A cell holding a ship block cannot also hold furniture.

Kinds come from a **block kind library**. Each kind declares:

- where it may go (hull, deck, rib column, interior);
- accepted material (planks, glass, …);
- a **shape generator**: generic code with parameters, never ship-specific code;
- sealing, collision, hit points and openable state;
- **support rules**.

| Generator | Produces |
|---|---|
| `hullSkin` | the form's surface inside this cell, thickened inward |
| `ribTimber` | skin + timber following it |
| `deckPlank`, `beamDeck` | deck planking, optionally over a beam |
| `opening` | skin with a cut-out, plus a fitting model (window, port, gate) |
| `shapeFile` | a VS shape JSON (fittings with their own model) |

Faces, metric UVs and collision voxels are produced per block. The archived prototype's
invariant tests (planar faces, metric UVs, one owner per collision voxel, stable ids) still apply.
A new behaviour needs code only when it is a new *kind*.

### Layout: the default ship as a block map

The design file lists the default kind for each cell. Ranges and fills keep it short:

```json
{ "fill": "planks",  "region": "hull" },
{ "fill": "rib",     "region": "hull", "x": [-16, -12, -8, -4, 0, 4, 8, 12] },
{ "fill": "deck",    "deck": "lower" },
{ "set": "window",   "at": [[-18, 9, -2], [-18, 9, -1], [-18, 9, 1], [-18, 9, 2]] },
{ "set": "gunport",  "at": [[1, 5, -6]] },
{ "stair": { "foot": [-3, 5, -1], "dir": "+x" } },
{ "wall": { "from": [5, 5, 2], "to": [7, 7, 2], "side": "+z" } },
{ "door": { "at": [[6, 5, 2]], "side": "+z" } },
{ "set": "hatch",    "at": [[6, 6, -1], [6, 6, 0]] },
{ "mast": "main",    "x": 2, "step": "hold" }
```

The default layout is checked by the **same rules** a player's edit is.

### Support rules, guides and damage

Supports form a graph derived from the kinds' rules:

```
deck ← beams ← ribs ← keel
hull skin ← ribs on both sides within span
opening ← an allowed hull span
stair ← a deck well, both landings and headroom
mast ← step + partners
```

"Required" means a block is in that graph under the current layout. That one definition drives
three things:

- **Grey guides:** shown only for missing required blocks, never for absent optional defaults.
- **Launch validation:** strict.
- **Damage at sea:** consequences such as leaks or weakened decks. Never invulnerability.

Alternative supports, such as headers around a large well, become extra kinds that satisfy the
same rule.

### State: changes by cell address

State stores, for each changed cell: kind, material, condition and open/closed. It stores
nothing about geometry. Changing the form, the roughness or the visible rib width keeps every
save valid, because cell addresses do not change.

### Items and people aboard

**Dropped items stay where they were dropped.** Players have no trade window, so dropping items
on deck is how they hand things over. An item dropped aboard:

- lands on the deck or floor below it;
- stays at that spot relative to the ship while it sails, rolls and turns;
- is picked up the vanilla way, by walking over it.

The likely approach keeps the vanilla item entity, for its pickup, rendering and despawn rules.
The ship pins it in ship-local coordinates each tick, after it settles on a ship surface. It
goes back to normal physics if its block is broken or the ship sinks.

Items aboard despawn by **vanilla rules**, the same as items dropped anywhere else (decided).

Animals and mobs aboard are a later, related problem.

## 4. One implementation, in C#, used everywhere

- The form → ship-block compiler lives in a pure C# library with no VS dependency, so tests run
  in plain .NET. The archived `src/Grid` shows the shape of such a library.
- The mod **derives the ship blocks at load** from the few-KB design and caches it. It does not
  ship baked megabytes.
- A small CLI (`tools/HansaPreview`) uses the same library to export glTF/OBJ. Previews are then
  correct by construction:
  - a real depth-buffered WebGL viewer, a few hundred lines and still self-contained, with game
    textures;
  - or Blender / Windows 3D Viewer.
- Python and JS stop computing geometry. The Python invariant tests are ported to C#.
- The earliest real preview should be an **in-game static test entity** (section 6). It is the
  only render that matters in the end.

## 5. Interface the mechanics consume

```csharp
interface IShipStructure
{
    ShipBlock Block(CellId c);                    // kind, derived shape, current state
    bool Solid(CellId c);
    bool Seals(CellId c);
    VoxelGrid Collision { get; }                  // incrementally restamped per block
    IEnumerable<Compartment> Compartments();      // flood fill over sealing blocks
    bool Raycast(Vec3d from, Vec3d dir, out BlockHit hit);
    double DeckTop(string deck);
    event Action<CellId> Changed;
}
```

Damage, flooding, dry spaces, walking, placement, guides and items aboard all depend on this
interface, not on generator output.

## 6. Engine binding: reuse what already works

The explorer pass found these inherited pieces that fit the new model directly:

- **Collision and walking:** `VoxelGrid` (0.25 block, ship-local) with `DeckMover`
  (`DeckMover.cs:78`). Block voxels stamp into it the same way slot states do today
  (`EntityShip.RebuildGrid`, `EntityShip.cs:1301`). Items aboard settle against the same grid.
- **Rendering:** the `SlotRenderer` pattern. Chunked meshes contain only the faces of blocks that
  are present, and only touched chunks are rebuilt (`SlotRenderer.cs:8`). Generalise its chunks
  from 8×8 deck cells to 8³ cell chunks.
- **Pieces:** `PieceRenderer` and `ShipPiece` for furniture and vanilla decor, placed in cells
  that hold no ship block.
- **Working blocks:** the engine mini-dimension in `ShipBlocksSystem` stays for vanilla working
  blocks such as the anvil. It is off by default today. The hull does not move into a
  mini-dimension.

## 7. What to keep, adapt and retire

- **Keep as is (geometry-agnostic):** `Sailing`, `ShipMotion`, `HelmMath`, `ShipSeats`,
  `ShipCrew`, `ShipGuns` (fixture and moving-part based), hearth, oven, `DeckMover`,
  `VoxelGrid`, the network packets and the mini-dimension working blocks.
- **Adapt:**
  - `ShipDamage`: hits resolve to cell addresses, replacing `HullSlotNear` and station/band.
  - `ShipFlooding` and `ShipDrySpaces`: compartments come from sealing blocks, replacing the
    single `DeckY` dry-mask cut.
  - `ShipPieces` and `ShipMounts`: mount points become cells.
  - Remove hard-coded names: deck codes `top`, `hold`, `quarter`, `forecastle`; mast codes
    `main`, `fore`; `"yard-" + code`; default ship code `carrack`. These become tags in the
    design.
- **New:** items aboard (section 3).
- **Retire once their last consumer is ported:**
  - `tools/shipgen/carrack.py` output: hull and deck slots, bands, `slotQuads`, paint quads;
  - `HullSlots`, `DeckSlots`, `ShipBeams`, `ShipSurfaces`, the `*Synth` fallbacks and
    `ShipDamageSkin`'s mesh-group assumptions.

## 8. Decisions

Decided by the user on 15 October 2026:

1. **Interior look:** smooth interior made of flat planes. A cell may hold several planes where
   the shape turns sharply.
2. **Hull roughness:** faceted. Corners on a half-block grid, a small set of slopes, 1–2 planes
   in most cells. To be refined later if needed.
3. **No real-block hull:** the hull is not moved into a mini-dimension. Hull blocks are never
   inventory items, and repairs use ordinary planks.
4. **First prototype:** archived in `archive/gpt-grid-prototype`, to be deleted later.
5. **Locality:** removing or moving a block (for example the aft windows) changes only that
   place, in game and in the design data.
6. **One master block per cell:** the user's model. It replaces the earlier proposal of four
   layers per cell.
7. **Dropped items stay where dropped:** this is how players hand items to each other, since
   there is no trade.
8. **Despawning:** items aboard follow vanilla despawn rules.

User feedback on M1, 15 October 2026:

9. **Stairs: two cells in plan, flat straight underside (approved and implemented).** The first
   M1 stair (three solid stepped blocks under a well) used too much deck. Replaced by the stair
   Magelan settled on
   (`docs/legacy/modular-pieces.md` §7.1):
   - a companion ladder-stair as a **fitting** spanning a foot cell and a head cell, 1 × 2 in
     plan;
   - a **2 × 1 well** in the deck above;
   - two stringers with treads, open underneath, the underside a straight line;
   - climbed with `DeckMover`'s existing ladder mode;
   - the head cell's floor stays free under the high end.
10. **Inner hull:** protruding ribs stay. They look good and keep the space between ribs usable.
    Flush inner planking (a "ceiling" over the ribs) would still be one master block per cell.
    It could later become an optional design setting for selected regions, for example the
    hold.
11. **Partition walls stay (approved and implemented).** Walls stand on **cell faces**, between two cells, as
    Magelan's do, so a room does not lose a row of cells. A door is a wall variant. Where a wall
    meets the sloped hull, its end is trimmed to the hull's inner surface. That surface is static
    form data, so the trim keeps locality: a wall edit changes only that face. Walls are a
    second kind of address next to cells, like furniture; they do not share a cell's master
    block.

Still open:

- Final rib spacing (1 or 3 clear cells are allowed as design data).

## 9. Milestones

| Step | Result | Done when |
|---|---|---|
| M1 | C# core: form → ship blocks, kind library, block map, state, support graph | Invariant tests ported; locality test: changing one cell changes only that cell and its shared faces |
| M2 | glTF exporter + depth-buffered viewer | Two-bay slice renders correctly from any angle, with textures and the faceted hull |
| M3 | Two-bay slice on the new model | Window/port/gate, stair moved in data and by edit, hatch, guides |
| M4 | In-game static test entity | Render, collide, walk, target a block, break/place with ordinary planks, grey guides, save/load, MP sync; dropped items stay where dropped and ride with the ship |
| M5 | Full form: bow, stern, castles, masts as data | Sliver validator clean; carrack character reviewed in game |
| M6 | Mechanics on `IShipStructure` | Sailing, damage, flooding, guns, pieces; old geometry code deleted |

## 10. M1 status, 15 October 2026

Implemented in `src/Construction`, checked by `tests/ConstructionTests` (80 checks, all pass):

| File | Contents |
|---|---|
| `Geometry.cs` | convex solids, exact cell clipping, metric UVs |
| `ShipDesign.cs` | design data and the faceted hull form: corner-grid and slope validation, mitred skin offsets, interpolation between stations |
| `BlockKinds.cs` | the kind library: fit rules and materials |
| `ShipLattice.cs` | compiles a design into cells and their structural parts; joins sliver cells to their neighbour; gives every collision voxel one owner |
| `BlockShapes.cs` | per-block geometry: structure, openings split into frame and panel, stairs, partitions |
| `ShipConstruction.cs` | the default block map from layout data; place, remove, damage, repair and toggle; support graph; grey guides; saves |
| `Fittings.cs`, `ShipConstruction.Fittings.cs` | face-addressed walls and two-row doors, trimmed to the hull and stopping under decks; ladder-stair fittings with their well, landings, passage and ladder line for climbing |

The checks cover:

- design validation, including rib spacing as data;
- planar faces, UV scale and exclusive voxel ownership;
- no slivers, and deterministic output;
- a watertight hull below the waterline;
- locality, both in game and in design data;
- rib damage, guides and repair with ordinary planks;
- openings, a ladder-stair moved between bays, walls and doors;
- saves surviving a rib-width change.

The sample two-bay slice:

- 466 blocks, a ladder-stair, and a cabin of face walls with a door, on the lower deck;
- 12,755 collision voxels;
- 10,238 block triangles before merging coplanar faces;
- 12 corner pieces joined to neighbouring cells, overhanging their cell by at most 0.124 block.

Visually checked with the preserved depth-buffer renderer (`tools/shipgen/view.py`) from
exported faces.

Known limits and choices to review:

- The hold floor shares its block with the bottom planking (one master block, kind `hull`), so
  the hold floor cannot take a hatch.
- Decks and beams stop against the rib timber in rib cells instead of overlapping it.
- Support uses a tolerant rule: framing within `plankSpan` cells along the keel, allowing one
  cell of offset in the section. A single rib cell can usually be removed while the cells above
  and below it remain.
- Internal faces between differently sized deck pieces in rib cells are not yet removed. The
  renderer's coplanar merge (M2/M4) is expected to cut the triangle count.
- An open lid is simply absent. Hinged lid models, glass rendering and texture atlas tiling are
  engine-adapter work.
- A ladder-stair has no collision of its own. It is climbed along its `LadderLine`, which the
  engine adapter hands to `DeckMover`'s ladder mode.
- A wall's collision is one voxel thick, on the side of its `Cell`.
- An open door is drawn swung square to its wall about one jamb. The real door model comes
  with the engine adapter.
- Bow, stern, castles, masts and the existing carrack's adjustment to the grid are M5.

