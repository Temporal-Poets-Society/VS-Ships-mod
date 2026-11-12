# Ideas from other mods (proposals, 30 October 2026)

Read from decompiled copies of public mods; nothing was run, and nothing here is agreed. Each mod's paths are its
own source files.

| Mod | How players stay aboard | Deck surface |
|---|---|---|
| PhysicsLib 1.3.0 (Davis99) | Postfix on `CollisionTester.ApplyTerrainCollision`: a foot anchor on the boat, its world movement added each tick | Oriented boxes from the shape's cuboids |
| Walkable Entities 1.0.44 (Omikronlex) | Prefix on `EntityBehaviorPlayerPhysics.SimPhysics`, client only: `newPose.ToWorld(oldPose.ToLocal(pos))` | Precompiled local boxes from the shape, spatial hash |
| Walkable Deck 0.9.99 (imcanida) | The player stays seated; `SeatPosition` is moved by a hull-local walk offset | Authored 2D layers (chalk zones, polygons, a shape heightfield), no walls |

Hansa already does what none of them do: collision against the ship's own voxels, server-side pitch and roll, and
ladders, ratlines and hatches (`src/Ship/DeckMover.cs`, `src/Ship/ShipSeats.cs`).

## Walking aboard

- **Camera re-projection every render frame** (Walkable Entities `CameraAlignment`: a transpiler after
  `CameraPosOffset` in `PlayerCamera.OnBeforeRenderFrame3D`). Removes stutter above the physics rate.
- **Other players sent in ship-local coordinates**, lerped in local space, drawn through
  `EntityPlayerShapeRenderer.GetOtherPlayerRenderOffset` (Walkable Entities `PlatformVisuals`). Check against how
  `DeckSeat` interpolates remote passengers now.
- **Exit points chosen in ship-local coordinates on the server** and applied by the client through its own current
  pose (Walkable Entities `SafeDismount`): latency-robust.
- **The carry run through world collision**, so a pier or shore scrapes a walker off instead of the ship carrying
  them into blocks (Walkable Entities `MovingFrame` 364–373).
- **Leaving with the ship's velocity**: a jump or walk off the side keeps the ship's motion (Walkable Deck
  `DeckWalker` 2633–2678).
- **Opt-out for other walking mods**: Walkable Deck honours a `deckNative` attribute. Untested whether either mod
  touches `EntityShip` seats; if one does, set it on our ships.

## Sea and damage (from Rough Seas 3.2.0, hoblin)

- **Shipped water and slamming**: individual waves drawn from a Rayleigh distribution, breaking by depth or whitecap
  odds, green water into the bilge, slam pressure damage (`EntityBehaviorSeaForces`, `Waves`). Would feed
  `ShipFlooding`, which has pumps but no water from waves. Must stay deterministic on the server for multiplayer.
- **Roll period from the hull** (an ITTC-style empirical formula, `HullResponse` 57–63) instead of the constants in
  `MotionDef` (`src/Ship/ShipMotion.cs`).
- **Floating dropped items riding the waves** (`FloatMotion`).
- Rough Seas' own boat motion is client-side and cosmetic; Hansa's server-side frequency response stays the model.
  Rough Seas only patches `EntityBoat`, so it doesn't move `EntityShip`. Its shader and weather conflict with Sea and
  Earth: `Sea/docs/compatibility.md`.

## Full ship mods (30 October 2026)

Seaworthy 0.9.0 (MisterMandarine), Shipworks 2.4.3 and Shipworks+ 1.1.2 (TYLERRR), read from decompiled copies.
The user's verdicts on what they offer:

- **Animal tethering** through vanilla's `IRopeTiedCreatureCarrier` (Seaworthy `TetherPatch`): already planned.
- **A building-site preview** (Seaworthy `RollerSiteHighlight`: cells shown valid or not before building): wanted
  when Hansa's building stage comes.
- **Any block aboard, working blocks included**: wanted, not achieved so far (`WorkingBlocks` dev switch). Shipworks+
  does it for every block, as a reference only. It uses no Harmony. It swaps the server's block-entity packet handler
  (`ServerMain.PacketHandlers[22]`, `ShipBlockEntityPackets`), moving the player into dimension space for the range
  check. It also swaps each block-entity renderer for a proxy every frame, overwriting the renderer's position fields
  found by reflection (`ShipBlockEntityBridge`). It redirects particles and container dialogs too. All of this is
  brittle across game versions, and it restores block-entity trees without `BlockSchematic` remapping.
- **Furling with nobody near**: Hansa has it already.
- **Rejected: refusing a turn that would push the hull into land** (Seaworthy `ResolveTurn`). Grounding is part of
  the game.
- **Rejected: design-as-JSON with stats shown live, and variants made by switching shape elements by name**
  (Shipworks+ `ApplyDesign`, `ShipComposer.Toggle`). These don't fit Hansa's block-grid ship, where players change
  cells, not pick from module catalogues.

Shipworks' generated hulls (18k cubes) are not a model for Hansa. They are shallow. Every hull has the same float
setting (`swimmingOffsetY` 3.75, hitbox height 4), so the waterline is 0.25 above the shape origin whatever the size.
The garboard planking then reaches about 1.7 blocks below it on the sloop (24 × 7), 2.0 on the galleon (46 × 11) and
2.6 on the man o' war (58 × 14), the keel about 0.6 more. Measured from the shape files ignoring element rotation;
their screenshots were not checked. Hansa stays the most detailed of these for realism: its own deck walking, motion computed on
the server from Sea's waves, hole-based flooding, wrecks, crews and logout handling.
