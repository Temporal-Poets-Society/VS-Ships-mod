# Step D plan: hull panels as slots, unified damage (carrack-m)

Design pass of 2026-10-13 (read-only study of 0.3.24's code). Apply on top of 0.3.25 (gun places, transom ports, carrack.py changes). The user said go for step D on 2026-10-13.

## 0. Decisions

1. **Panel = frame space × strake band × side.** Frames stay skeleton, each with a fixed frame strip of planking ±0.25 round its centre (`frames.x0 + space·k ± 0.25`, on the voxel grid); a panel runs strip to strip (1.5 long on carrack-m). Fixed horizontal lines: wales, the waterway strake at every deck level (deck top − 0.25 .. top), the strake under each rail cap, the keel strip (|z| < 0.25 on the flat floor). A lattice of timbers with panels between them: no panel spans a deck, wale or frame, so an opening never shows a deck edge, beam end or frame side.
2. **Band seams are strake seams on quarter-block voxel lines.** For carrack-m the generator re-spaces strakes (equal girth inside each band, between fixed seams snapped to 0.25 lines) so a panel's drawn edges and its collision voxels coincide.
3. **Only whole panels are slots.** Fixed in D: partial frame spaces at stem/sternpost, panels cut by a gun port/window/entry port/hawse/scupper, panels with a fitting on either face, castle-break sweeps, panels whose inside is in two rooms, the whole transom. Expect ~200 panels below the main deck + a few dozen above; most waist-bulwark panels end up fixed (ports, channels, gallows) — fine, ports become slots later.
4. **Step B's machinery unchanged**: states filled/cracked/empty, delta in `magelanSlots`, owner/facing draw rule, chunked baked meshes. Ids `hull:<p|s>:<station>:<band>`.
5. **Damage keeps the existing ladder, slot state derived from it.** Each panel is a `DamagePart` (part index = hull slot index; hull parts come first in `DamageLayout`). `Hurt`/`Wear` floats as today; state: `hurt ≥ D.Holed` → Empty, `≥ D.Cracked` → Cracked, else Filled. Collision and drawing follow the slot state only — no runtime cutting.
6. **Flooding stays one space** (`ShipFlood`). Empty panel below the main deck: orifice `OpenLeak` (default 0.2 m² × area/1.5) when the sea is over its head, a weir over its sill when the sea is between sill and head. Cracked: `CrackedLeak`. Seam wear as today. Empty panel above the main deck: downflood point onto its deck (`OpenFixture "hull:n"`).
7. **Repair:** cracked → existing repair tool (held right-click, 2 planks + 1 nails) → Filled. Empty → building mode, planks in hand, right-click the opening, costs the slot's `material` (like a deck cell). Taking up: axe in building mode; refused below the flood line while afloat (step E's slipway needs taking).
8. **Step E-ready:** `Init` builds `FullGrid` (design grid with every slot filled); `DryMask`, `DrySweepMask`, `PartMap`, hydrostatics, `MountLayout` from it. Saved states only take away, so a ship could later start all-empty (`magelanSlotsBase: "frame"` in E).
9. **carrack-m drops the old jagged-hole system**: the mesh file stops emitting `hullOut`/`hullIn`/`hullThickness`, so `HullDamageSkin` is null for her. carrack/carrack-a (archived in 0.3.25) keep the old system.

## 1. Code facts that drive this

- Hull drawing: smooth ring-based planking (`tools/shipgen/planking.py` `skin()`, `transom()`, `raised_strip()`, `sweep_patch()`) from `carrack.py` `hull()`; quads tagged `hull="out"/"in"` → `hullOut`/`hullIn` (`shipgen.py` `_write_skin`). Rings resampled 64× along the hull, so quad edges are not at frames today. `_cut_ranges(..., splits)` already splits at x values (ceiling at walls).
- Hull collision: hidden-material shell from outside to ceiling (`carrack.py` `build()`: `shell`, `d.mid("hidden")`); deck boxes overwrite at deck layers; the hold floor is the bottom's inner layer. Frame stations `-16 + 2k` (`frame_stations()`); definition `frames` `{x0: -24, space: 2}`. Strakes ≈0.3 equal girth (`strake_levels()`); wales one strake each (`wale_bands()`, `WALES` (2.475, 3.025)).
- Today's damage: `DamageLayout` (`src/Ship/ShipDamage.cs`) builds hull panels from shell voxels per (side, `StationAt(x)`, `BandAt(y)`); `ShipDamage.Blow` shares energy within the radius and pierces via `grid.RaycastCell` + `PartMap.Classify`. `HullDamageSkin` (`src/Ship/ShipDamageSkin.cs`) cuts jagged holes with reveals; `Wreck.Holes()` carves ellipses.
- Step B template: `DeckSlotDef`/`DeckSlotLayout`/`SlotStates`/`SlotMeshPlan`/`DeckCells` (`src/Ship/DeckSlots.cs`); `src/Ship/ShipSlots.cs` (`InitSlots`, `SaveSlots`, `ApplySlots`, `TakeProblem`, `TryTakeCell`, `TryLayCell`, `CellLeaks`, `DownfloodOpen("cell:n")`); `src/Client/SlotRenderer.cs`; `shipgen.py` `slot_quads()`/`mesh_voxels()`; `carrack.py` `deck_slots()`/`slot_checks()`.
- Sealing: `EntityShip.RoomEnclosed` needs an explicit hull-leak check; `SealedAt` → `ShipCompartments.Sealed` (`src/Ship/ShipMounts.cs`) flood-fills the live `Grid` (cleared voxels already unseal). Downflood: `DamageLayout.DownfloodPoints`.

## 2. Data contract (new docs/slots.md §9)

### 2.1 Definition (`config/ships/<code>.json`)

```jsonc
"hullSlots": [
  {
    "id": "hull:s:12:3",            // side p|s, station = ShipPartMap.StationAt, band = strake band from the keel up (0 lowest)
    "side": 1, "station": 12, "band": 3,
    "box": [x0,y0,z0,x1,y1,z1],     // its shell (outer planking to ceiling): prefilter, reach, binding
    "outer": [[x,y,z] ×4],          // outer face corners: aft-low, fwd-low, fwd-high, aft-high
    "inner": [[x,y,z] ×4] | null,   // ceiling corners (null: no ceiling of its own)
    "normal": [nx,ny,nz],           // outward mean
    "area": 1.32,                   // outer face m²
    "sill": -1.875, "head": -0.875, // lowest/highest y of the outer face (ship-local)
    "room": "lowerdeck" | null,     // design room its inside faces (null: open deck / bulwark)
    "solids": [[...boxes]],         // owned collision voxels (greedy-merged)
    "material": { "block": "game:planks-oak-ud", "count": 2 },  // ≈ round(outer + inner area); any wood's planks
    "start": "filled"               // D: always filled; "empty" for step E
  }
],
"hullBands": [ { "band": 0, "y0": -2.75, "y1": -1.75 }, { "fixed": "waterway", "y0": 0.25, "y1": 0.5 } ]  // optional
```

Slot index = position in the list. Adjacency derived from ids (band ±1, same station); across a frame strip is not a neighbour. No voxel belongs to two slots of either family.

### 2.2 Mesh file (`config/shipmeshes/<code>.json`)

New groups, same quad format + `slotQuads` pairs: `slot:hull` (outer planking above the copper line, bulwark insides under the sky, reveals lit outside), `slot:hull:bottom` (tarred, below `COPPER_TOP`), `slot:hull:bottomclad` (copper; the mod draws one of bottom/bottomclad per `ship.Clad`), `slot:hull:interior` (ceiling and reveals in room light; group by `point_zone`). `paintQuads` covers them.

Draw rule unchanged: drawn while (owner = −1 or owner filled) and (facing = −1 or facing empty).

| quad | owner | facing |
|---|---|---|
| a panel's outer planking (all strakes in its band, split at strips) | panel | −1 |
| its ceiling | panel | −1 |
| reveal on a band seam toward the next panel | panel | neighbour (both ways) |
| a frame strip's / wale's / waterway's / rail-cap strake's face toward the panel | −1 | panel |
| fixed geometry only an opening shows (hold-floor underside over band 0, knee/clamp backs) | −1 | panel |

All filled she looks exactly as before (quads only split, UVs interpolated).

## 3. Generator (`carrack.py`, `planking.py`, `shipgen.py`, `meshcheck.py`)

1. **Seams**: `HULL_SLOTS = MODULAR`, `hull_seams()`. Fixed strakes on quarter lines: waterway at each deck meeting the side (castle decks too); wales snapped (≈2.5–2.75 and 3.0–3.25, merged with the strake between and the main waterway into one fixed band 2.5–3.5); top strake under each rail cap. Free bands ≈1 block girth between. Suggested carrack-m (design y): floor..−1.75, −1.75..−0.75, −0.75..0.25 | waterway | 0.5..1.5 (seam at the waterline, game 0.125) | 1.5..2.5 | fixed 2.5..3.5 | bulwark 3.5..4.5 | cap | castle bands per storey. `seamed_levels(seams)` replaces `strake_levels()` for carrack-m (n = round(girth/0.3) per band); `wale_bands()` returns the snapped wales; check port/window cut y0/y1 are still ring levels.
2. **Strips**: `strip_edges()` = each frame `xf ± 0.25` → `splits=` on every hull `skin()` call (outside and inside) and `sweep_patch()`; split flat-bottom cap quads at z = ±0.25 (keel strip).
3. **Classify**: `hull_slots(d)` after `hull()` and fittings, beside `deck_slots(d)`. Each `hull="out"/"in"` quad by centroid (ceiling pushed out by `shell_t`): side by sign z, station by `StationAt`, band by seams; fixed if in a strip, fixed band, or a panel failing §4. Panel quads → slot groups with `q["slot"] = (n, -1)`. No `hull=` tags for carrack-m.
4. **Fixed-panel rules** (count reasons like `deck_slots()`): not a whole frame space (stem, sternpost, transom); intersects `SIDE_CUTS`, `PORT_X`, `GUNPORTS`, `CASTLE_PORTS`, the 0.3.25 transom ports, `STERN_CUTS`, hawse, scuppers; any non-planking non-skeleton quad within 0.3 of its faces (channels, chains, gallows posts, ladders, nameboard, figurehead, cleats, hooks, beds, hearth; frames/beams/knees/wales are skeleton); a fixture box touching its faces; castle-break sweep or under poop/forecastle overhang; inside in two rooms; the transom.
5. **Collision**: `d.hull_slot_grid` — a voxel is owned if hidden shell material, on the panel's side, x inside its strip edges, centre y inside its band. Deck-material voxels never owned. Greedy-merged `solids`. Assert no voxel in a deck slot and a hull slot.
6. **Reveals**: `hull_slot_reveals(d)` — at strip lines per strake outer ring → ceiling ring (−1, panel); band seams both ways; fixed bands (−1, panel); band 0: hold-floor underside (−1, panel). Normals into the hole; bare oak like `side_reveals()`.
7. **JSON**: outer/inner corners, normal, area, sill/head, room (`room_grid`/`point_zone`), material.
8. **Checks** `hull_slot_checks(d)` as `fit:` lines: unique ids; every slot has outer quads and solids; quads within box + 0.05; every edge has a strip/fixed face or a reveal pair; all-filled area per group = pre-split area; no voxel in two slots. Extend `meshcheck.py` (reads `slotQuads`) for hull groups: all filled (no new leaks) and random states (no coplanar double draw, no edge gaps).
9. **Views**: `view.py`/`texview.py` `--empty=hull:s:12:3,...`.

## 4. Mod runtime

### 4.1 Layout and state (new `src/Ship/HullSlots.cs`, partial `src/Ship/ShipHullSlots.cs`)
- `HullSlotDef` (JSON fields + `[JsonIgnore] Index, Centre, Below, Room, Mat, DesignState`).
- `HullSlotLayout`: `Build(def)`, `IndexOf(id)`, `AtVoxel(g,i,j,k)`, `Neighbours`, `ChunkOf`/`Chunks` keyed (side, station/2), `PickOpening(o, d, range, Func<int,bool> open)` (ray vs outer and inner corner quads), `Problems`.
- `ISlotLayout` (`Count`, `IdOf`, `DesignStateOf`, `SolidBoxesOf`, `IndexOf`, `ChunkOf`, `Chunks`) implemented by `DeckSlotLayout` and `HullSlotLayout`; `SlotStates` and `SlotMeshPlan(ISlotLayout, file, groups)` on the interface. `ToTree(t, clear)`: `SaveSlots()` clears once then writes both families (today `ToTree` removes every key — would wipe the other family).
- `ShipDefinition`: `HullSlotDef[] HullSlots`, `[JsonIgnore] HullSlotLayout Hull` built in `Init()` after `Slots`; `FullGrid` for `DryMask`, `DrySweepMask`, `PartMap`, `DamageLayout`.
- `EntityShip`: `SlotStates HullPanels`; `InitHullSlots` (same `magelanSlots` tree); `ApplySlots` applies hull states (Grid and hullGrid); `HullOpen(n)`, `HullTakeProblem`, `HullLayProblem`, `TryTakeHull`, `TryLayHull`, `TakeHull(n, player)`, `LayHull(n, checkPeople)`, `SetHullState(n, state)` (server: writes `Hurt`), `HullLeaks(RoomDef)`, `HullText()`.
- `HullSlotSynth.Panels(def)` (test only, like `DeckSlotSynth`) — panels from shell voxels per (side, station, whole-block band), so C# can be built/tested before the generator lands.

### 4.2 Save
- `WatchedAttributes["magelanSlots"]` gains hull ids (differences from `start`).
- `Attributes["magelanDmgF"]` gets `"v": 2` (hull codes now = side, station, strake band). Bytes `magelanDmg` as before.
- Server invariant in one helper `SyncSlotsFromHurt()`: `HullPanels[i] == StateFor(Hurt[i])`.

### 4.3 Collision and sealing
- `SlotStates.Apply` clears/sets solids → shots, rays, walking, rain pass openings automatically.
- `RoomEnclosed(r)`: false if `HullLeaks(r).Count > 0` (empty panel with `Room == r.Code`). `SealedAt`: add `HullLeaks(r)` boxes to leaks.
- `DownfloodPoints`: each hull slot with `sill ≥ DeckY − 0.05` → `{X centre x, Y sill, Z outer z at sill, Length panel length, Weight 1, OpenFixture "hull:" + n}`; `DownfloodOpen` handles `hull:`.
- `DryMask` unchanged (full hull); the flood mesh shows the water.

### 4.4 Rendering (`SlotRenderer.cs`, `ShipRenderer.cs`)
- Generalise `SlotRenderer` to families (deck, hull): `SlotDesign` per (design, family), `SlotChunks` per (ship, family). Hull groups drawn right after the matching `DrawHull` calls; shadows for `slot:hull` and the bottom group.
- Owner quad texture = base (painted if zoned) + overlay `HullLook.Overlay(HullLook.Of(layout, part, hurt, wear), inside, variant)` composited `"++0~magelan:block/dmg-…"` via `SlotTex`. Looks: cracked, scraped, worn, leaking (holed = empty, not drawn).
- Rebuild on `TakeChanged()` + `DamageVersion` (looks at most every 0.5 s like `UpdateDamage`), `plan.Affected(changed)`.
- Paint: `PaintSplit` already skips `slot:` groups; `ShipPaint` must use hull states for `slot:hull*`; `ShipSurfaces` takes hull slot groups' owner ≥ 0, facing −1 quads as ceiling/planking.
- carrack-m: `m.Hull` null; `UpdateDamage` still runs for sails; `RebuildChunk`/`EnsureSplit`/`ChunkPaint` unused for her.

### 4.5 Damage flow
1. `CannonShots.HitShip` → `ship.Damage(local, dir, E, HitRadius, "cannon")` (unchanged).
2. `ShipDamage.Apply`/`Blow`: skip parts whose slot is Empty; share (1 − d/r)². Panel toughness T = 80 kJ: cracks at hurt 0.35, empties at 0.7. Surplus × `ThroughKeeps` continues through the grid; Classify of a slot-owned voxel → the slot's part (`Hull.AtVoxel`). For shots (radius ≤ `PierceRadius`) a fixed hull voxel with no slot part within the radius counts as structure (`left -= StructureToughness`), drop the `PartAt` fallback there; keep the fallback for grounding/ramming (radius 1.25).
3. `EntityShip.AfterDamage`: `SyncSlotsFromHurt()`; on a state change `SaveSlots()`, `RebuildGrid()`, `SaveDamage(true)`, woodgrind sound, notice ("panel stove in" / "cracked"). (D2: a slot something hangs on stops at Cracked.)
4. `FloodTick` → `ShipFlood.Leaks`: empty → area `OpenLeak × area/1.5` or `Pour(len, 1, surface − sill)` between sill and head; cracked → `CrackedLeak`; wear as today. One open panel 1–2 m deep ≈ 0.75 m³/s vs ~0.16 m³/s for two pumpers → founders in ~14 min unless laid. Tunable (`Leaks` config, `DamageDef.OpenLeak`).
5. Grounding/collisions the same way (`DamageKinds.Bottom`/`Planking`); small grinding blows only add seam wear.

### 4.6 Repair and building
- Cracked: `ShipRepairs.Needs` planks job (2 planks + 1 nails) → `HurtAfter 0` → `Repair()` + `SyncSlotsFromHurt()`.
- Empty: new `What = "lay"` job (`AtSea = true`, not via repair tool), hint "lay N planks in building mode"; `RepairPick` still finds empty panels.
- Lay (`TryLayHull`): building mode, planks (`MaterialFits`), `Mat.Count`; reach aboard within 6 of the box, from outside/swimming within 4; below the flood line needs aboard or eyes submerged (`RepairReach`); refused while someone is in its solids (`BoxOccupied`). → Filled, `Hurt = Wear = 0`.
- Take (`TryTakeHull`): axe in building mode, hold LMB ~1.5 s; refused if fixed (`hull-fixed`), below the flood line afloat (`hull-below-water`), or a piece/item/wall spot bound to it (`hull-under-piece`). Gives material back; Empty, `Hurt = 1`.
- Client (`DecoClient.cs`): `HullTarget()` beside `CellTarget()` — axe → `Grid.RaycastCell` → `Hull.AtVoxel` → take (or "fixed"); planks → `PickOpening` over empty panels nearer than the first wall hit → lay. Outline the outer quad. Packets `DecorPacket.HullTake`/`HullLay` (`src/Network/DecorPackets.cs`). Lang: `hint-hull-take`, `hint-hull-lay`, `hull-*` errors, `state-open`, `repair-hint-lay`.
- `PartText` height zone from the slot's centre y, not `BandY`.
- Command `.magelan hull [take|lay|crack|reset] <id>` beside `.magelan slots` (`src/MagelanDecor.cs`).
- Step E cost = Σ deckSlots.material + Σ hullSlots.material.

### 4.7 Wrecks (`Wreck.cs`, `ShipWrecks.cs`)
- `WreckInput.Slots` = merged delta of both families; `DeckCells.Applied` → `SlotGrids.Applied(def, delta)` over both layouts (empty panels' solids gone; cracked stay). `Holes()` skips hull parts with `Slot ≥ 0`. Decay breaches: 2–4 whole panels between bilge and main deck.

## 5. Migrating saved carrack-m ships
Server, `InitDamage`, when the design has `HullSlots` and `magelanDmgF.v < 2`; pure `HullMigration.Map(def, codes, hurt, wear)`:
1. Old Hull code area = side, x in `[X0 + s·Space, +Space]`, y in `[KeelY + band, +1]`; target hull slots of that side/station overlapping > 0.25 in y (or nearest). Largest overlap gets the old hurt; others `min(hurt, Cracked)` if `hurt ≥ Cracked`; wear = max.
2. Old deck/spar codes load as before (D2: per-station deck hurt → nearest unoccupied deck cell).
3. `SyncSlotsFromHurt()`, `SaveSlots()`, `v = 2`, `SaveDamage(true)`, audit log line. Old jagged holes become clean openings (say so in the changelog). Existing world wrecks untouched.

## 6. Obsolete for carrack-m (delete once carrack/carrack-a are retired or slotted)
`HullDamageSkin` hole geometry (`HoleOf`, `HoleStrake`, `HullHole`, `Cut`, `Reveal`, `HoleValue`, Static/Sound split — most of `ShipDamageSkin.cs`); `ShipRenderer` `HullSplit`, `RebuildChunk`, `EnsureSplit`, `SplitTextures`, hull part of `UpdateDamage`; `PaintRenderer.ChunkPaint`, `ChunksOfZone`; voxel hull scan in `DamageLayout` and `ShipPartMap.BandAt` for slot designs; ellipse carving in `Wreck.Holes`; `ShipSkinFile.HullOut/HullIn/HullThickness`; `DamageSkinTests.cs`. Per-station deck parts go in D2.

## 7. Tests
**Offline** (`tests/MagelanTests/HullSlotTests.cs`):
- `-- design carrack-m` → `DesignHullSlots(def, file)`: ids parse and match side/station/band; solids solid, shell voxels (`PartMap.IsShell`), in no other slot; outer + ceiling quads owned and within box; band neighbours have reveal pairs, strip/fixed faces exist; `slotQuads` lengths; 50 random states with no coplanar overlapping drawn quads; all-filled area = design's; no `hullOut`; ~150–300 slots.
- `-- hull`: `PickOpening` outside/inside; mixed deck+hull `ToTree`/`FromTree` round trip; `Apply` clears/refills voxels; `DamageLayout` part i = slot i; `ShipDamage.Apply` at 20/40/100/400 kJ (nothing/scraped, cracked, empty, through + far panel hit); leak rates (cracked ≈ 1 l/s, open ≈ 0.75 m³/s at 2 m, weir partly submerged); bulwark downflood point; `HullLeaks` opens the lower deck; `Needs` cracked → planks, empty → lay; `HullMigration.Map` cases; wreck `CellsHoled` = empty panel's solid voxels, decay breaches whole panels; chunks affected by one change; chunk build time.
- `slots` mode runs hull checks too; carrack's `damage`/`damage-skin` tests unchanged.

**Server self-test** (`src/SelfTestHull.cs`, from the carrack-m run in `SelfTestModular.cs`): launch carrack-m, starboard panel amidships below the waterline; 200 kJ empties it (voxel cleared, `magelanSlots[id] == 2`, `Hurt ≥ Holed`, water rises over simulated `FloodTick`s, `RoomEnclosed(lowerdeck)` false); `LayHull` → filled, solid, water stops, no deltas; 30 kJ cracks, `Repair` restores; take refused below waterline, allowed above, item returned; reload round trip; migration from old-format `magelanDmgF` → panel empty, `v == 2`; a second ship's gun fires → panel cracked/empty and the shot exits the far side; 8 panels empty below the waterline → founders and sinks, wreck plan lacks those cells.

## 8. Risks
- Overlap with 0.3.25's carrack.py edits — rebase on its output; transom and castle ports stay fixed.
- Re-spaced strakes and snapped wales change carrack-m's look slightly — render views (side, bow, cutaway) for the user before shipping.
- Mesh size: +~30 % hull quads plus reveals — watch file size and load time.
- Bow/stern ends fixed in D1; ramming relies on the radius fallback. Partial end panels = D2 option.
- Things on a shot-out panel's ceiling float until D2's binding.
- Tuning `OpenLeak` and thresholds needs a client test.
- Swimmers: dry cut-out and keep-out unchanged (nobody swims in through a hole; mending from outside works by reach).
- Save semantics: hull codes change meaning in `magelanDmgF`, guarded by `v`; migration before `Load`.
- Per-frame cost fine (~1000 parts per blow, ~300 hull parts per FloodTick every 0.25 s).

## 9. Test builds
- **D1 (0.3.26)** (built 2026-10-13: §10) hull panels exist and take shots: generator (seams, strips, classification, reveals, JSON, checks, views), contract docs/slots.md §9, `HullSlotLayout`, `ISlotLayout`, states/save, `FullGrid`, collision, hull family in `SlotRenderer` with overlays and paint, damage → panel states, leaks/weir, bulwark/castle downflood, `RoomEnclosed`/`SealedAt`, cracked repair, lay/take in building mode, `.magelan hull`, migration, wrecks, tests.
- **D2 (0.3.27)** unified damage beyond the hull: deck cells as damage parts (cracked overlay, migrate per-station deck hurt); partitions/windows/doors/railings take shots (`ShipPiece.Hurt`, cracked overlay, gone when holed, via a structure-hit callback in `Blow`); places bound to hull panels unusable while empty, their panels can't be taken; "occupied stops at cracked"; optional partial end panels.
- **D3 (0.3.28)** clean-up and tuning: remove per-station deck parts and voxel hull scan for slot designs; decide carrack/carrack-a (archived → likely delete obsolete code once no saves need it); balance toughness/`OpenLeak` after a client test; docs/backlog.

Critical files: `tools/shipgen/carrack.py`, `planking.py`, `shipgen.py`, `meshcheck.py`; `src/Ship/DeckSlots.cs`, new `HullSlots.cs`/`ShipHullSlots.cs`; `src/Ship/ShipDamage.cs`, `ShipFlooding.cs`; `src/Client/SlotRenderer.cs`, `ShipRenderer.cs`, `DecoClient.cs`.

## 10. What D1 built (0.3.26, 2026-10-13) and where it went another way

Built as §9's D1 list (backlog: "Magelan 0.3.26 (2026-10-13): step D1 — hull panels as slots"; contract docs/slots.md §9).
Where the plan proved wrong or the simpler working option was taken:

1. **The flat floor and the keel are fixed** (not a keel strip with panels across the floor). The bottom's planking under
   the hold is the hold's floor itself (one voxel layer, deck material): a panel there would open nothing. Band 0 is the
   bilge, from the floor's edge (y −2.75) up to −1.75; no hold-floor-underside faces are needed.
2. **Strips by sampling, not `splits=`.** The arclength rings' quads aren't square across at a frame (each ring's points sit
   at other x), so a split at a strip's edge would be skewed. `planking.xgrid_rings`: every ring has a point at each strip
   edge and each frame space's middle within its own run (where it doesn't curve hard: |dz/dx| < 1), the same grid x at the
   same index on every ring; its ends by arclength as before. Quads between two grid x both rings have are square across;
   the others are marked `xend` and make their panels fixed (her ends).
3. **The wales snapped to 2.75–3.0 and 3.0–3.25** (two strakes over each other as 0.3.25's 2.6–3.2, not 2.5–2.75 and
   3.0–3.25 with a strake between): the fixed band is 2.75..3.5 with the main deck's waterway, band 4 is 1.5..2.75.
4. **The castles' fixed band is 6.0..6.75**: the forecastle's moulding at 6.0, the sterncastle's at 6.5 (over the poop's
   overhang: carrack-m has POOP_OVERHANG, so its moulding is 6.5..6.75, not on the waterway).
5. **The fixed planking is her structure** in `ShipPartMap.Classify` (not a hull part): its whole-block codes would name
   panels by mistake. A shot on it loses `StructureToughness` and goes on; the crosshair calls it her structure.
6. **`FullGrid`** fills the hull panels only (the deck cells' design openings stay open); it's `Grid` itself while every
   panel starts in.
7. A shot that stove a panel in goes on through its opening at no cost (the grid it was given still has its voxels); the
   pierce loop's guard is 24 steps (12).
8. **Grounding and ramming** where no panel is near (the keel, the floor, her ends): the nearest panel within her half
   beam (`PartAt` reach + 4), so running aground still hurts her bilge.
9. A panel over her main deck that's out is a downflooding point with the deck's share (it lands on a deck), not
   straight below; under her main deck it's never a downflooding point (its leak is the orifice or the weir).
10. Places don't fix panels (a chest place or a lantern hook against the ceiling: D2 binds them); a fixture's box does
    (pin rails, gates, the hearth). The outline of a panel in the crosshair is its box (not its outer quad).
11. The self-test's gun is the archived carrack's (fixed guns) into carrack-m's port side over her waterline: a 9-lb ball
    (~145 kJ) stoves a panel in and goes on, but can't come out through two panels (80 + 80 kJ); the check is that it goes
    on through her (in the run it was stopped by the mainmast in the lower deck). The eight panels out are the deepest
    amidships (band 0, both sides): she went down in 57 s.
13. Taking a panel out afloat is refused while its sill is less than 0.1 over her waterline (band 4, 0.125 over it, can
    be taken out; everything under it can't).
12. Not done in D1 (as planned for D2): deck cells as damage parts, pieces taking shots, places bound to panels, partial
    end panels. carrack/carrack-a keep the old jagged holes (`HullDamageSkin`).

## 11. D2 additions from the user's 0.3.26 test (2026-10-13)

- **No strips left over:** taking out two neighbouring panels must leave only the rib between them — no planking strip round the rib. The planking over a frame belongs to the panels on either side (each owns half, split at the frame's centreline on the quarter grid). Like the decks: take the cells out and only the beams stay.
- **Thick wall, ribs inside (the user's choice):** ribs 0.5 × 0.5 (like the deck beams; now 0.25 sided, 0.2 proud of the ceiling); the hull wall 0.5–0.75 thick (now `HULL_T` 0.3) with the ribs inside it between the outer planking and the ceiling. A smooth inside wall: the ribs show only where a panel is out (then their full depth). Rooms get about 0.25 narrower a side; check pieces, stairs, gun places, beds, the hold's stowage and hydrostatics.
- **Placeable gun ports (note 212):** a castle-side hull panel slot takes either planking or a lidded port piece; a port makes a gun place. The default castle ports (at most 3 a side aft after 0.3.27's note 203) become such panels.
- Show the user renders (outside, hold, a few panels out) before shipping.
- **One-block panels:** the ribs stay 2 blocks apart, but each frame space holds **two** panels, each 1 block long (rib centre → the space's middle → next rib centre), not one panel bridging the 2-block gap. Ids gain a half: `hull:<p|s>:<station>:<band>:<a|f>` (aft/fore half); migration maps a 0.3.26 panel to both halves.

### Also in 0.3.28 (not D, from the user)
- **ON HOLD (user, 2026-10-13: "hold with capstan movement") — do not move the capstan.** ~~Capstan under the forecastle (note 198):~~ the user agreed: "we are strong and can turn it standing on one side". Move it to the spot of note 198 (forecastle, main deck, about x 15.2–16.25, z 0), worked standing beside it (hold right-click; more players faster) — no walking ring; bars ~0.9, shown only while worked, clear of the forecastle front and the foremast. Put the forecastle stair back where the user had it (0.3.24 shifted it for the old ring). Keep the cable/messenger working.
- The lower-deck stair at the stern (foot near x −12) that can be placed but not walked off (something ~0.5 high where you step off, maybe the tiller's sweep): fix the obstacle or refuse the spot.

## 12. What D2 built (0.3.28, 2026-10-13)

Backlog: "The twentieth client test … (→ 0.3.28)"; contract docs/slots.md §11.
- **D2.1, D2.2** built: one-block panels (`:a`/`:f` halves, 321 panels), no strips, the rib drawn in her side's thickness
  while a panel beside it is out (0.22 wide, a hair inside the riders' faces), its collision the voxel column aft of the
  frame's line; saves v4.
- **D2.3 (thick wall, ribs inside) not built**: every ceiling 0.25 inboard touches every room's places, stairs, gun places,
  beds, the hold's stowage and the hydrostatics; it wants a round of its own (0.3.29), with renders first.
- **D2.4** built as **port covers** (deck slots of kind "port", filled = planked over) rather than a second geometry for the
  castle panels: the castle ports' panels stay fixed (cut by an opening), the cover is the hole's planking. A port can't be
  put where the design has none yet (only the castles' existing port places: the quarterdeck's three a side, the poop's
  three, the forecastle's two).
- **D2.5** built: deck cells as damage parts (80 kJ), pieces taking shots (30/40/15/20 kJ), saves v3.
- **D2.6** built: the stern stair's landing was her side's curve; landings checked 0.6 wide, the spot refused.
- Not yet: places bound to hull panels (a lantern or chest on a ceiling that's shot out), partial end panels, D3's tuning.


## 13. What 0.3.29 built (2026-10-14)

Backlog: "The twenty-first client test … (→ 0.3.29)"; contract docs/slots.md §12, §13.
- **D2.3 as the user chose it on 0.3.28** ("maybe thick hull is just two panel blocks in thickness placed. in open decks
  just one placed"; "on open decks … we can keep hull thickness maybe original and have ribs protruding"; "this also would
  count to hull damage resistance"): under her weather decks two layers of panels, her planking (`hull:`) and a ceiling
  (`ceil:`) LINING further in, the ribs 0.5 between them, the inside smooth; the bulwarks one layer, their frames standing
  inside as before. 642 hull panels: 321 planking, every one with its ceiling (the bulwarks' sides were never panels). The ceiling has its own toughness (40
  kJ), backs the planking (0.3 of a blow), halves the leak of a holed planking panel in front of it, isn't reached by
  grounding while the planking's sound; a room opens only through both.
- **D2.4 again** (the user: "removable side board … then we place port inside it"): each castle port's place a board
  (0.3.28's cover) and a port (`gunport:`, a trapdoor puts it in): docs/slots.md §12. The halves round the ports stay
  fixed (sweeps, mouldings, the forecastle's stepped sides).
- Still not: places bound to hull panels, partial end panels, D3's tuning.
