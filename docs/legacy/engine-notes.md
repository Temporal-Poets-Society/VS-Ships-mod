# Vintage Story 1.22 engine notes (what Magelan relies on)

Read from the decompiled 1.22 DLLs and from GitHub `anegostudios/vsapi`, `vsessentialsmod` (not `vsessentials`) and
`vssurvivalmod` (master = 1.22.7). These are the facts the design rests on. If a game update breaks something, look
here first.

## Mounting (EntityAgent, IMountableSeat)

- `TryMount(seat)`:
  1. calls `seat.CanMount`
  2. copies the player's controls into `seat.Controls` (fires `OnAction` for held keys)
  3. unmounts the previous seat with `DoTeleportOnUnmount = false`
  4. calls `doMount`
  5. writes `WatchedAttributes["mountedOn"]` (from `MountableToTreeAttributes`)
- There is **no mount packet**. Clients follow the `mountedOn` watched attribute (`updateMountedState` →
  `ClassRegistry.GetMountable(tree)`, which dispatches on `className` to the delegate registered with
  `api.RegisterMountable`, on both sides). The delegate must return the same seat instance, and it must be in
  `IMountable.Seats` with `Passenger` set.
- `doMount` calls `seat.Entity.AnimManager.StopAllAnimations()`. It reads `SuggestedAnimation` **before**
  `DidMount`, so `Passenger` is still null at that point.
- Sneak-to-dismount lives only in the seat's `Controls.OnAction`. A seat that ignores Sneak never dismounts. Don't
  return false from `CanUnmount`: it's also used on death.
- **Local player:** `EntityBehaviorPlayerPhysics.OnRenderFrame` runs fixed 60 Hz steps. `SimPhysics` does
  `pos.SetPos(MountedOn.SeatPosition)` while mounted. Magelan's deck step is a Harmony prefix on `SimPhysics`.
- **Server:** players aren't physics-ticked. `Pos` comes from client packets (15 Hz, jumps over 128 blocks are
  rejected).
- **Other clients:** `HandleRemotePhysics` sets `Pos` from the seat. `EntityPlayerShapeRenderer` draws other
  passengers of the same mount at (their seat − my seat). It forces their body yaw to `MountedOn.Entity.Pos.Yaw`,
  then applies `seat.RenderTransform`, which is how Magelan turns them back to face where they look.
- While mounted, keys go into `MountedOn.Controls` (`SystemPlayerControl`) and reach the server through
  MoveKeyChange. `player.Entity.Controls` stays zeroed.
- `ClientMain.UpdateCameraYawPitch` pushes seat-yaw changes into the camera (always in first person; with
  `PushYaw`/`Push` in third). It uses a private `prevMountAngles`, which Magelan sets when attaching.
- **Teleports:**
  - `EntityPlayer.TeleportToDouble` on a mounted player teleports the **mount entity**.
  - `Entity.TeleportToDouble` then teleports every seated player (`Onplrteleported`).
  - So Cylinder World's seam wrap (it wraps `MountedOn.Entity`) carries the whole crew.

## Teleporting a mount with passengers (Cylinder World seam, /tp)

- The server calls `Onplrteleported` for each passenger. That sends entity packet 1 to clients, and
  `EntityPlayer.OnReceivedServerPacket(1)` then **sets the mount entity's `Pos` directly on the client** (not through
  a position packet).
- A client-side smoother that later writes its own pose into `Pos` must notice that change, or it undoes the
  teleport. The client then puts the player back at the old spot, the server believes the player, the ship's new
  area has no player to keep it loaded, and the ship shows as aground. Magelan compares `Pos` with what it last
  wrote each frame.
- `IMountable.ControllingControls` gets overwritten from every position packet
  (`SystemNetworkProcess.HandleSinglePacket`, `ServerUdpNetwork`). Return null unless you want that. Beds do.

## Chunks and entity tracking far from players

- **Server unloading:** map chunks far from every player age every 3 s (`ServerMapChunk.UnloadGeneration` 5 → old
  at ≤ 1), so they unload after about 12 s. Clients are told to drop them.
  - Keep a column alive with `sapi.WorldManager.GetMapChunk(x, z).MarkFresh()` (Cylinder World 0.9.1 does this for
    the seam destination), or with `ChunkLoadOptions.KeepLoaded`.
- **Tracking:** a client only tracks an entity if it's in range **and** the server already sent that client the
  entity's chunk (`PhysicsManager.UpdateTrackedEntityState`). Otherwise the entity is despawned on the client
  (OutOfRange) and re-sent later.
  - `Entity.AllowOutsideLoadedRange => true` skips the chunk check (and the client's render check). Magelan ships
    use it so they don't pop out and back in after a long jump.

## Entity position sync

- `Entity.ServerPos` is obsolete. There's only `Pos`, and the client writes packets straight into it, then calls
  `OnReceivedServerPos(isTeleport)`.
- Without `interpolateposition`, `Pos` snaps to each packet. Magelan keeps its own smoothed pose and writes it back
  into `Pos`.
- The server sends at up to about 30 Hz while the entity moves, and about once a second otherwise.
- `SimulationRange` defaults to 128. Magelan uses 256.

## What the server sees of a player's view (laying a gun)

Read 2026-10-01 for laying the guns (Magelan 0.3.6, `src/Ship/ShipGuns.cs`).
- **The client sends its view.** `EntityBehaviorPlayerPhysics.OnRenderFrame` calls `capi.Network.SendPlayerPositionPacket()`
  every 4th physics tick (15 a second) while the player is alive, mounted or not (the mount's own packet goes too only
  when the player controls the mount). `ServerPackets.getEntityPositionPacket` puts in `Pos.Yaw`, `Pos.Pitch`, `Pos.Roll`,
  `HeadYaw`, `HeadPitch` and `BodyYaw`; on the server `HandlePlayerPosition` → `EntityPos.SetFromPacket` writes them all
  into the player entity's `Pos`.
- **What they are.** On the client, `ClientMain.UpdateCameraYawPitch` sets the local player's `Pos.Yaw` to the camera's
  yaw (`mouseYaw`, world, pushed along by the mount's yaw in `PushYaw` mode) and `Pos.Pitch` to the camera's pitch
  (vanilla's convention: π looks level, less looks up, clamped 1.5858..4.6974). So a player's `Pos.Yaw`/`Pos.Pitch` on
  the server are where they look, in world axes, at most a fifteenth of a second (plus the ping) old;
  `EntityPos.GetViewVector(pitch, yaw)` = (−cos p·sin y, sin p, −cos p·cos y) turns them into a direction. `HeadYaw` and
  `HeadPitch` are the head's turn from the body (eased and clamped by the head controller): no use for aiming.
- **Mounted players keep it.** The server's physics puts a mounted player at the seat with `Pos.SetPos` (the position
  only), so the yaw and pitch stay the client's. Not so for a creature: `AdjustMountedPositionFor` uses `Pos.SetFrom`
  (the seat's yaw and pitch too), so a mounted chicken (the self-test's stand-in) always "looks" along its seat.
- **The camera can be turned by a mod.** `capi.Input.MouseYaw` (= `IClientPlayer.CameraYaw`) is the camera's yaw;
  setting it and `Pos.Yaw` turns the view. The pitch comes from `Pos.Pitch` (`mousePitch` is copied from it each frame),
  so set `Pos.Pitch` and `CameraPitch` together (as the tilted-deck view below does).
- **Magelan's use:** the server lays a gun toward `GetViewVector(Pos.Pitch, Pos.Yaw)` turned into the ship's frame as
  she really lies. The client's view tilts with the drawn deck (SeaMotion), so with SeaMotion under 1 a still view
  drifts a little in her true frame and the gun follows it; it's laid where the player looks in the world either way.

## The view and passengers on a tilted deck

- **View pitch:** `EntityPos.GetViewVector(pitch, yaw)` = (−cos p·sin y, sin p, −cos p·cos y). π is level; less than
  π looks up. `ClientMain.UpdateCameraYawPitch` adds the mouse to `EntityPlayer.Pos.Pitch`, clamps it to
  1.5858..4.6974 and copies it into `mousePitch` (`IClientPlayer.CameraPitch`); the camera reads `mousePitch`.
- **Vanilla's rocking mounts:** with `EnumMountAngleMode.Push`, `UpdateCameraYawPitch` subtracts the change of the
  seat's pitch and roll since last frame from the player's `Pos.Pitch` / `Pos.Roll` (`prevMountAngles`). Pushing
  changes keeps the mouse free. `Pos.Roll` of the player isn't used by the camera. Magelan pushes the deck's tilt
  along the view into `Pos.Pitch` itself the same way.
- **`IClientPlayer.CameraRoll`** is `MainCamera.Roll`. `Camera.Update` rotates only `CameraMatrixOrigin` by it, about
  the **world X axis** (`Mat4d.Rotate(…, Roll, {1, 0, 0})`), so it's a true roll only when looking along X. The
  frustum is culled with `CameraMatrix` (`ClientMain` loads it before `frustumCuller.CalcFrustumEquations`), which
  doesn't get it, so a rolled view loses chunks at the screen's corners. Magelan leaves `CameraRoll` alone and
  postfixes `Vintagestory.Client.NoObf.Camera.Update` (public fields `CameraMatrix`, `CameraMatrixOrigin`,
  `CameraMatrixOriginf`): both matrices are pre-multiplied by a rotation about the eye's own Z axis, so the image
  turns about the crosshair and culling matches.
- **Order in a frame:** the camera is an `EnumRenderStage.Before` renderer at order 0. Player physics (which reads
  the seat position) runs inside it (`OnSelfBeforeRender`). Sea's wave clock is at 0.05, Magelan's ships at 0.1, so
  seats and the view use the previous frame's ship pose; the ship is drawn relative to the seat, which hides that.
- **Passengers' bodies:** `EntityPlayerShapeRenderer.loadModelMatrixForPlayer` builds
  `translate(other player's offset) · seat.RenderTransform · RotateX(Pos.Roll) · RotateY(bodyYaw + 90°) · …`, so a
  seat's render transform turns the body in world axes about its feet, before the body's yaw. For other players on a
  mount the body yaw is forced to `MountedOn.Entity.Pos.Yaw`. The local player (`isSelf`) gets the transform too,
  without the offset (third person, and immersive first person).

## Clocks

- **Game calendar on clients:** `ServerSystemCalendar` sends a small update (`TotalSeconds` as a whole number of
  game seconds) every 5 s and the full calendar every 60 s. The client sets its time to it (so it jumps back by the
  latency each time) and runs on between them. Sleeping adds a time-speed modifier, so the calendar races while
  everyone sleeps.
- **Server:** `World.ElapsedMilliseconds` is `totalUnpausedTime` (stops while a singleplayer game is paused).
- **Client:** `capi.InWorldEllapsedMilliseconds` stops while paused; `capi.ElapsedMilliseconds` doesn't.
- **Ping:** `IServerPlayer.Ping` is the round trip in seconds.
- Sea's wave clock is built from these (see `../Sea/docs/engine-notes.md`).

## Rendering

- `EntityShapeRenderer` lights a whole entity with one `GetLightRGBs` sample, hence Magelan's own renderer (standard
  shader, `PreparedStandardShader` + `RenderMultiTextureMesh(mesh, "tex")`, like block entity renderers).
- Shape element rotation is `T(origin) · Rx · Ry · Rz · T(from − origin)`, in degrees, standard right-handed. `from`,
  `to` and origin are in 1/16 block.
- `IPointLight.Color` is **(blue, green, red)** × intensity (compare `SystemRenderPlayerEffects`). Entities and
  blocks both receive point lights, capped by the dynamic-lights setting.
- The client's entity picking only scans chunks near the ray, which breaks huge entities. Magelan does its own ray
  test against fixture boxes and ship voxels for clicks; since 0.3.5 the game's own selection gets ships through a
  patch (see "Looking at an entity (crosshair) and spawn points").
- **The standard shader lights a face by its normal** (`shaderincludes/normalshading.fsh`, `getBrightnessFromNormal`,
  used by `standard.fsh` when `normalShaded`): brightness `max(0.45, 0.5 + 0.5·dot(normal, lightPosition))`, at least
  0.95 × the normal's up component, plus 0.2 × its northness (world −Z). A ship's skin mesh is drawn with face culling
  off (Magelan's renderer), so a quad wound the wrong way still shows, but lit as if it faced away: a face away from
  the sun comes out at 0.45, near black for dark wood (0.3.7: the inside of the transom faced aft, and looked tarred).
  `tools/shipgen/texview.py` lights its views the same way (since 0.3.7).
- **Two quads in the same place flicker.** With culling off both are drawn at the same depth, and which one wins
  changes from pixel to pixel and frame to frame; if their normals differ (a sheet's outside and its inside turned
  round) so does the light, and the face flickers light and dark (0.3.7: the hearth's copper hood). A thin sheet seen
  from both sides needs its inside as its own face a little in (the hood: 0.015).

## Shadows (sun shadow maps)

Read 2026-09-30 for the ship's shadow (Magelan 0.3.5, `ShipRenderer.RenderShadows`).
- **When they run:** `ClientMain.MainRenderLoop` triggers `ShadowFar` + `ShadowFarDone`, then `ShadowNear` +
  `ShadowNearDone`, between the `Before` and `Opaque` stages, only if `AmbientManager.ShadowQuality > 0` (the graphics
  setting `shadowMapQuality`, 0..4) and `DropShadowIntensity > 0.01` (it fades with the sun's or moon's height and
  with fog). `ShadowNear` only from quality 2. With shadows off a renderer registered for them is never called.
- **The setup** (`SystemRenderShadowMap`, order 0.0 in each stage): binds and clears the map's frame buffer
  (`ShadowmapFar` 11, `ShadowmapNear` 12, `max(4, quality + 2) × 1024` texels square), `Use()`s the chunk shadow
  shader, pushes an orthographic projection (`PMatrix`, no translation: a box round the camera) and the sun's view
  (`MvMatrix`: `LookAt(sun or moon direction, origin, up)`, camera-relative like every other pass). Ranges: far 60
  blocks at quality 1, else 150 + 120 × (quality − 1) (the box drawn to half that); near 30 + 3 × (quality − 1).
  `shUniforms.ToShadowMapSpaceMatrixFar/Near` and `game.shadowMvpMatrix` (= `IRenderAPI.CurrentShadowProjectionMatrix`,
  projection × view) are set, and the frustum culler is recomputed for the shadow box, in world coordinates: during
  the stage `capi.Render.DefaultFrustumCuller.SphereInFrustum(worldX, worldY, worldZ, r)` says whether something is
  in the map.
- **The Done stages** (order 1.0) call `ShaderProgramBase.CurrentShaderProgram.Stop()` (a renderer that leaves no
  shader in use crashes the game there), unbind the map and pop both matrices.
- **In each stage:** chunks at 0.37 (`ret-sf` / `ret-sn`: the Opaque, TopSoil, BlendNoCull and OpaqueNoCull pools,
  cull face off; not Transparent or Liquid, so glass and water cast no shadow), entities at 0.4, then mods' renderers
  by their `RenderOrder` (Magelan's ships at 0.45, the helve hammer at 0.5).
- **The chunk shadow shader** (`chunkshadowmap.vsh/.fsh`, `EnumShaderProgram.Chunkshadowmap` 32): uniforms
  `mvpMatrix`, `origin` (world position = vertex + origin, `gl_Position = mvpMatrix × world`), sampler `tex2d`; vertex
  inputs position 0, uv 1, colour 2, flags 3 (the standard mesh layout). It applies the flags' wind warping and the
  global (temporal storm) warping to that position, and discards texels with alpha < 0.02, so alpha-tested cut-outs
  (rope, leaves) cast their own shape.
  - **SSBOs:** with `ClientSettings.AllowSSBOs` (`IRenderAPI.UseSSBOs`) program 32 is built to read chunk faces from
    an SSBO and reads no vertex attributes; `Chunkshadowmap_NoSSBOs` (42) is the attribute build, registered when the
    shaders are (re)loaded with SSBOs on (the reload at server-ready).
- **Entities** (`SystemRenderEntities.OnRenderFrameShadows`): for each entity renderer whose entity is in the shadow
  frustum (a sphere of radius 3 at its `Pos`), in a rendered chunk, in the player's dimension:
  `DoRender3DOpaque(dt, isShadowPass: true)` with the chunk shadow shader in use (switched to 42 with SSBOs), where
  `EntityShapeRenderer` draws held items with `mvpMatrix` = `CurrentProjectionMatrix × CurrentModelviewMatrix × model`,
  `origin` 0 and `RenderMultiTextureMesh(mesh, "tex2d")`; then `DoRender3DOpaqueBatched(dt, isShadowPass: true)` with
  `Shadowmapentityanimated` (`projectionMatrix`, `modelViewMatrix` = modelview × `ModelMat`, the `Animation` UBO,
  `entityTex`, discards alpha < 0.01; `EntityShapeRenderer` draws only when the entity has an `Animator`, which every
  shaped entity gets on the client). Afterwards it `Use()`s the chunk shadow shader again (42 with SSBOs) and resets
  its `mvpMatrix` to `shadowMvpMatrix`. So from 0.4 on the attribute build is the one in use.
- **Other renderers:** `HelveHammerRenderer` (Opaque, ShadowFar, ShadowNear; order 0.5) draws its mesh in the shadow
  stages with `CurrentActiveShader`: `mvpMatrix` = projection × modelview × its camera-relative model matrix, `origin`
  0, `"tex2d"`. `AnimatableRenderer` (API) uses `Shadowmapentityanimated`. `MechNetworkRenderer` registers for the
  shadow stages but draws nothing in them.
- **Traps:**
  - `IShaderProgram.Use()` throws if a different program is in use: `Stop()` it first, and `Use()` it again after.
  - `IRenderAPI.CurrentProjectionMatrix` and `CurrentModelviewMatrix` fill and return **the same** `float[16]`: copy
    one before asking for the other.
  - Uniform setters look the name up in a dictionary of the shader's uniforms (`KeyNotFoundException` for any other
    name); `HasUniform` tells.
- **Receiving shadows:** `ShaderProgram.Use()` binds `shadowMapFar/Near`, `shadowIntensity` and the
  `toShadowMapSpaceMatrix`es for every shader that includes `shadowcoords.vsh` / `fogandlight.fsh`, the standard
  shader too, so whatever is drawn with `PreparedStandardShader` receives shadows. `calcShadowMapCoords` takes the
  camera-relative world position (`modelMatrix × vertex`). In the fragment: brightness = 1 − `shadowIntensity` × 0.5 ×
  (the part of a 3 × 3 sample in shadow), plus `blockBrightness` (the block, point or glow light less half the sun
  light), clamped to 0..1. The compare allowance is 0.0009 (far) and 0.0005 (near) of the map's depth range, about
  0.2 and 0.1 blocks. All shaders get `#define SHADOWQUALITY` from the setting.

## Sleeping, warmth, weather

- **Sleeping:** `ModSleeping` counts `EntityBehaviorTiredness.IsSleeping` (a watched attribute). Beds set it in
  `DidMount`. Its tick clears it if the player isn't mounted. The client's closing-eyes screen checks
  `MountedOn is BlockEntityBed` (patched).
- **Body temperature** (server, `EntityBehaviorBodyTemperature`):
  - `getNearHeatSourceStrength` sets `inEnclosedRoom` (from `RoomRegistry`) and sums `IHeatSource` blocks.
  - Enclosed means `tempChange = heat + 1`, ignoring outside cold and wind.
  - Wetness comes from `GetRainMapHeightAt(plrpos) <= plrpos.Y` inside `updateBodyTemperature` (transpiled).
- **Rain, snow and hail particles:** `WeatherSimulationParticles.asyncParticleSpawn`, on the async particle thread.
- **Rain sound muffling:** `WeatherSimulationSound.roomVolumePitchLoss` / `searchComplete`.
- **Hail damage:** `EntityBehaviorHealth.ApplyHailDamage`.
- **Lightning** damages every creature within 8 blocks with no roof check (not patched yet).
- **Not patched:** hunger's cold resistance uses `RoomRegistry.ExitCount`.
- **The vanilla firepit** (for the hearth aboard; read from the 1.22.7 assets and `BlockEntityFirepit`): one block,
  `game:firepit-<burnstate>` (`construct1`..`construct4`, `cold`, `lit`, `extinct`), class `BlockFirepit`, entity
  `Firepit` (a container with fuel, input and output slots; `HeatSource`, `Ignitable` behaviours; lit it gives light
  `[7, 7, 16]` and ember particles). Its `cold-normal` shape is ashes, embers and logs within about 0.9 × 0.9 of the
  block, 0.33 high. A pot or crock cooking in it is drawn from its own `inFirePitProps`: the pot's is a translation of
  0.125 up from the block's bottom (`useFirepitModel: "Wide"`), so a pot's bottom stands 1/8 over the firepit's.
  (But a clay pot is drawn by `PotInFirepitRenderer`, 1/16 up: that path wins for anything `IInFirepitRendererSupplier`.
  The working hearth: "The firehearth: vanilla's firepit aboard" below.)

## Water waves

- **How waves are drawn:** `chunkliquid.vsh` / `chunkliquiddepth.vsh` call `applyLiquidWarping`, which does
  `y += waterWaveIntensity * gnoise / div` (+ wind).
  - `div` is 5 × max(0.2, 1 − oceanity), so 1 on the open ocean.
  - `waterWaveIntensity = 0.75 + windSpeed * 0.9` (`DefaultShaderUniforms`).
  - So ocean crests reach 1–2 blocks. No floor near the waterline stays dry.
- **Vanilla boats** (`EntityBehaviorHideWaterSurface`) draw a hidden plane into depth only before the water. That
  can't hide crests that rise above the floor.
- **The Sea mod edits the shader source** (it used to be Magelan; see `../Sea/docs/engine-notes.md` for wind):
  - A Harmony postfix on `Vintagestory.Client.NoObf.ShaderRegistry.LoadShaderProgram` injects code before
    `vec4 cameraPos = modelViewMatrix * worldPos;`.
  - Then `capi.Shader.ReloadShaders()` runs at `LevelFinalize`.
  - Uniforms are set every frame with `Use()` / `Uniform` / `Uniforms4` / `Stop()`.
- **Chunk shader positions are camera-relative:** `worldPos = xyz + origin`, where origin is relative to
  `EntityPlayer.CameraPos`. `playerpos` is camera − `playerReferencePos`, not absolute.

## Inventories on an entity

- `GuiDialogCreatureContents` sends slot packets with `SendEntityPacketWithOffset(entityId, offset, packet)`, where
  inventory packet ids are < 1000.
- Opening an inventory on the server does not send its contents. Keep them in watched attributes, as block
  entities keep theirs in their data.
- `InventoryGeneric.FromTreeAttributes` keeps the inventory's own slot count: a saved 64-slot tree loads into a
  1560-slot inventory with the rest empty (how the hold grew).
- **Partial watched-attribute updates:** `SyncedTreeAttribute.MarkPathDirty("a/b")` sends only that subtree, and a
  client listener registered on `"a"` still fires (`path.StartsWith(listener.path)`). But on the client
  `PartialUpdate` looks the path up and, if it isn't there, stores the value as a top-level key literally named
  `"a/b"`. So only send a sub-path the client already has; send `"a"` the first time (`EntityShip.SaveInventory`).
- **A big slot grid that scrolls:** the creative inventory's pattern (`GuiDialogInventory`): the grid in a
  `BeginClip`/`EndClip` inside an inset, a vertical scrollbar whose handler sets the grid bounds' `fixedY` and calls
  `CalcWorldBounds()`, `SetHeights(visible, total)`. `ShipHoldDialog` follows it.

## Placing and breaking blocks (ProtectHulls)

- `IServerEventAPI.CanPlaceOrBreakBlock(player, blockSel, out claimant)` is asked for both (from
  `WorldMap.TryAccess` with `EnumBlockAccessFlags.BuildOrBreak`); it doesn't say which. For placing, `blockSel.Position`
  is already where the block goes (an air, water or replaceable spot); for breaking, the block being broken. So a
  solid, non-replaceable block at the position means it's being broken.
- `IBlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid)` clears only the solid layer: water in the fluid layer
  stays (how plants in the hull are cleared).

## Textures

- **Composite paths** (`"game:block/wood/planks/oak1++3~magelan:block/tar"`): `TextureAtlasManager.GetOrInsertTexture`
  with an `AssetLocation` loads through `LoadCompositeBitmap`, which splits on `++`, reads the blend mode before `~`
  (`EnumColorBlendMode`: 0 Normal, 1 Darken, 2 Lighten, 3 Multiply, 4 Screen, 5 ColorDodge, 6 ColorBurn, 7 Overlay,
  8 OverlayCutout) and blends each overlay over the base. Overlays must be the base's size (else a warning and it's
  skipped). Shape textures (`ShapeTextureSource`) go through the same call. Inserting at runtime renders into the
  atlas and queues a mipmap rebuild; `TextureAtlasPosition.RndColors` holds 30 sampled pixels (ARGB) of what went in.
- Vanilla oak planks (32 × 32) have their dark seams on rows 0, 11 and 22, and their mean colour is about
  (129, 98, 70).
- Vanilla's wood textures (planks, `debarked/*` log sides) run their grain along the image's x (u): a timber shows
  its grain along its length only if u runs along it (upright logs are the block turned). Generated meshes set u
  along the long side for that.
- Read 2026-10-01 for the damage drawn (Magelan 0.3.5, 3b):
  - **Several overlays chain:** `LoadCompositeBitmap` blends every `++` part in order onto what's there, so
    `oak1++3~magelan:block/tar++0~magelan:block/dmg-cracked-a` is tarred oak with cracks on it.
  - **Blend modes and the overlay's alpha** (`ColorBlend`): Normal (0) is `ColorOver` (the overlay over the base by its
    alpha); Multiply (3) blends between the base and base × overlay by the overlay's alpha (transparent overlay pixels
    leave the base alone); OverlayCutout (8) sets the result's alpha to 0 wherever the overlay's alpha isn't 0 (RGB
    kept): how a sail's holes are cut out of the linen.
  - Vanilla's `linen`, copper sheets (`metal/sheet/copper1..5`), oak planks and debarked oak are all 32 × 32; the water
    textures (`block/liquid/water`, `saltwater`) are grey (tinted by the liquid shader), alpha 171; `waterportion` is
    blue, 24 × 24.
- **Shapes as loaded** (`Shape.TryGet`): `[OnDeserialized] TrimTextureNamesAndResolveFaces` moves each element's faces
  into `FacesResolved[6]` (North, East, South, West, Up, Down; a disabled face is null) with the `#` taken off the
  texture code. `ShapeTextureSource[code]` looks the code up in `shape.Textures` (an `AssetLocation`, composite paths
  allowed) and inserts it into the block atlas. So a mod can give some elements of a loaded shape other texture codes
  (or null faces) and tesselate it again (Magelan's torn sails).

## Rendering meshes of our own (updated each frame)

Read 2026-10-01 for the water drawn in a ship's hold (Magelan 0.3.5, 3b).
- **`UploadMesh(MeshData)`** sizes its buffers by the data's counts (`XyzCount`, `UvCount`, `IndicesCount`...), not its
  capacity. **`UpdateMesh`** writes the new data into them with `BufferSubData` (at most that much) and sets the index
  count drawn to the new data's `IndicesCount`. So upload once with room to spare, then update with as much as there is;
  `MeshData.Clear()` resets the counts for filling again.
- **`GetOrLoadTexture(AssetLocation)`** (`textures/` is put in front) loads a PNG as its own GL texture with clamp mode
  0: no wrap mode set, so OpenGL's default, repeat; linear minification, nearest magnification, no mipmaps. A mesh
  drawn with it (`prog.Tex2D = id`, `RenderMesh`) can run its uv past 1 to repeat it (the block atlas can't).
- **The standard shader** (`standard.vsh`) warps vertices by their flags (`applyVertexWarping`) unless
  `dontWarpVertices` (0 from `PreparedStandardShader`): wind mode 12 (`flags | 12 << 25`, "LiquidWarp") is vanilla's
  water warp, `applyLiquidWarping(true, worldPos, 5)`, only with the "waving stuff" setting on (`WAVINGSTUFF`). Its
  fragment stage discards texels under `alphaTest` (the uniform keeps what it was last set to; Magelan sets 0.25 for a
  torn sail and 0 after, as for the name board). `rgbaTint` multiplies the colour (white from `PreparedStandardShader`).
- **Under water in a flooded hold** (Sea's hooks, read in `../Sea/src/Game/Hooks.cs`): the eyes-in-water postfixes and
  the breathing postfix only overrule vanilla where vanilla already found a liquid at the eyes, and splashes are skipped
  only where it's dry. So below the water in a hold (not dry there) vanilla's own under-water view, drowning and
  splashes stand; water in her hold higher than the world's water outside is no water to vanilla. Players aboard are
  mounted on deck seats: never swimming or `FeetInLiquid`, so no swimming there, and no splashes from walking.

## Boats

**In 0.3.2 Magelan's ship's boat was one of our own vessels (`EntityShip`, design `skiff`); since 0.3.3 it's vanilla's
sailboat made a rowing boat (`EntityRowBoat : EntityBoat`), the user's choice** (the launch is parked). About vanilla's
`EntityBoat` (decompiled from VSSurvivalMod.dll with ilspycmd):

- **Motion:** `updateBoatAngleAndMotion(dt)` (protected virtual) runs on the server in `OnGameTick` and on the client
  every frame (`OnRenderFrame`, the boat registers itself as a renderer). It reads the controlling seat's keys
  (`SeatsToMotion`), eases `ForwardSpeed` and `AngularVelocity` to them (x `speedMultiplier`), sets `Pos.Motion` from
  the view vector and turns `Pos.Yaw`; nothing happens unless she's `Swimming`. An override that runs after it can
  take the way off (our hull keep-out).
- **Her frame:** the multibox physics turns the collision boxes by `Matrixf.RotateY(yaw + pi)` (box centres); the
  sailboat's model runs along the model's x with the prow toward -x, its boxes along z with the prow toward -z (entity
  z = model x - 0.5, the renderer's (-0.5, 0, -0.5)), and the bow points along (sin yaw, cos yaw) in the world.
- **The sailboat's model** (`shapes/entity/nonliving/boat/sailed.json`): everything under `ORIGIN` (turned 180 about
  y: its +x is the model's -x): `ORIGIN-Prow`, `-Section1`, `-Section2` (a block each), `-Prow2` (the stern, turned
  180 again, with the two seats on plank children and the tiller), `MastORIGIN`, the backstay (`MastRope1` under the
  stern post), the `hideWater` planes (flat, `#null`, at 0.625 and 0.875 over the model's floor), and the storage
  places as top-level boxes with attachment points (`LStore1AP`..). The seats' points have no turn (the rider should face the bow: not yet seen in the game).
  Sections come out cleanly: take one out and move the stern's parts up by a block.
- **Shape transforms** (`ShapeElement.GetLocalTransformMatrix`, animVersion 0): parent x T(origin) x Rx x Ry x Rz x
  T(from - origin), the element's box from 0 to (to - from), sixteenths; children in the parent's frame from its
  `from` corner. An animation's degrees are added to the element's own. `tools/shipgen/vsshape.py` does the same.
- **A seat's click box** (`EntityBehaviorSelectionBoxes`) is the box of the model element its attachment point sits on
  (the element's own from–to, posed like it), not a box round the point: an attachment point on a speck of an element
  can't be clicked. Vanilla's seats sit on 10 × 16 × 16 elements.
- **Nothing to stand on:** the multibox physics collides the boat with terrain only; players pass through her. Magelan
  seats a player who steps, jumps or swims into a rowing boat (a packet; the server mounts them on a free seat).
- **Pickup vs placing:** vanilla picks a boat up on Shift + right-click and puts a chest, basket or vessel in a place
  on Ctrl + right-click with it in hand: a mod's own gesture mustn't take either.
- **Item → entity:** `ItemBoat` spawns at the block selection (any face: on top of a water block with
  `liquidSelectable`), yaw the player's, the entity type with the item's own code.

- **Picking a vanilla boat up** (`EntityBoat.OnInteract` → `tryPickup`): only with `rightClickPickup` in its
  attributes, empty (no passengers, no accessories), and `byEntity.Controls.ShiftKey`. A player mounted on one of our
  seats has zeroed `Controls` (the keys go to the seat), so from a ship's deck or ladder vanilla's pickup never fires.
- **Mounted animations:** `EntityAgent.OnGameTick` reads `MountedOn.SuggestedAnimation` every tick and swaps the
  animation when its code changes, so a seat can change it on the fly. The player's `sitboatidle` (a meta code in
  `player.json`) is what vanilla's boats sit you in; `crudeOar*` are paddling arm animations (blend "Add").

- **Placing:** vanilla `ItemBoat.OnHeldInteractStart` spawns the entity type with the item's own code, so an item
  `magelan:boat-skiff` spawns the entity `magelan:boat-skiff`. `EntityBoat` gives the same item back on pickup
  (`World.GetItem(Code)`).
- **What EntityBoat needs:** `mountAnimations` (idle, ready, forwards, backwards; the rider's animations) must be
  there; `requiresPaddlingTool`, `speedMultiplier`, `swimmingOffsetY` are optional; the seats come from
  `creaturecarrier` with `apName` attachment points in the model. The raft's model has no animations: `turnLeft` and
  `turnRight` just don't play.
- **Where the model sits:** `EntityShapeRenderer` translates to the entity, turns by its yaw, then translates by
  (−0.5, 0, −0.5): the model's (8, 0, 8) (in 1/16) is on the entity's position. Which way the model faces for a yaw
  isn't checked yet (the raft's model is long along x, its collision boxes along z), so our boat is double-ended: it
  reads right whichever way round it comes out.


## Looking at an entity (crosshair) and spawn points

Read 2026-09-30 for the damage model (`docs/backlog.md`, "Damage, sinking and wrecks").
- **The client's selection:** `SystemMouseInWorldInteractions.UpdateCurrentSelection` calls
  `GameMain.RayTraceForSelection(IWorldIntersectionSupplier, Ray, …)` with the filter `e.IsInteractable`.
- **Candidates:** `GetEntitiesAround(ray.origin, range, range)` walks only the chunks within `range` of the eye and keeps
  entities whose `InRangeOf` (virtual) passes.
- **Intersection:** each candidate's `Entity.IntersectsRay(ray, tester, out dist, ref selectionBoxIndex)` (virtual;
  behaviours can take over with `trickleDownRayIntersects`). The default tests `SelectionBox`, and `dist` is the square
  distance from the entity's centre, not to the hit.
- **The hit:** `HitPosition` comes from `tester.hitPosition` minus the entity's position, and `Face` from
  `tester.hitOnBlockFace`. The entity wins over the block when its hit point is nearer the eye.
- **The look-at box:** `HudElementBlockAndEntityInfo` shows `GetName()` and `GetInfoText()`. `EntityInView` rebuilds
  it only when the entity changes, and ignores `SelectionBoxIndex` (blocks do check the index).
- **Server, entity interaction packets:** the entity must be within PickingRange + 10 of the player
  (`GetEntitiesAround`). Attacks are also checked against `SelectionBox` distance, and with anti-abuse on
  `IsInInteractionRangeOf`.
- **`IsInteractable` on the server:** `EntityProjectile` hits only interactable entities (their `SelectionBox`), and
  AI targeting and retaliation check it too.
- **Spawn points:**
  - Only `ItemTemporalGear` sets one. It calls `IServerPlayer.SetSpawnPosition(new PlayerSpawnPos { x, y, z, yaw,
    pitch, RemainingUses = temporalGearRespawnUses })` at the block the player stands in. Beds don't set spawn.
  - Respawn: `ServerMain.TriggerPlayerRespawn` runs the server's own handlers first (`EventManager`: the one that
    teleports to `GetSpawnPosition(consumeSpawnUse: true)`), then mods' `sapi.Event.PlayerRespawn`.
  - The spawn is looked up in this order: the role's `ForcedSpawn`, then the player's `SpawnPosition`, then the role's
    `DefaultSpawn`, then the world spawn with `spawnRadius`.

What Magelan 0.3.5 relies on (the crosshair on ships, the spawn point by the helm), read 2026-09-30:
- **`GameMain` is shared:** `Vintagestory.Common.GameMain` is the base of both `ClientMain` and `ServerMain`, and in
  single player both run in one process, so a Harmony patch on it runs for the server too: check
  `__instance is IClientWorldAccessor`. Its `RayTraceForSelection(IWorldIntersectionSupplier, Ray, ref BlockSelection,
  ref EntitySelection, BlockFilter, EntityFilter)` is what every overload ends in; the client calls it every frame
  (`SystemMouseInWorldInteractions.UpdateCurrentSelection`, with `this` as the supplier). `Ray.dir` is as long as the
  picking range. Magelan's prefix wraps the entity filter (Harmony: `ref` on the argument, by position `__5`) to leave
  ships out, and the postfix tests them itself.
- **Nearest wins:** vanilla keeps the entity with the smallest `intersectionDistance` (for the default `IntersectsRay`
  the square of the distance to the entity's centre, not to the hit), then compares that entity's hit point with the
  block's (`blockSelection.Position` + `HitPosition`); the nearer one stays, the other is set to null. The entity's
  `HitPosition` is `tester.hitPosition` minus its `Pos` (X, Y, Z), `Face` is `tester.hitOnBlockFace`, `Position` its
  `Pos.XYZ`.
- **What vanilla does with a selected entity** (`HandleMouseInteractionsNoBlockSelected`): left: `InWorldAction`
  (a mod can stop it here), the held item's attack start, the swing, `ClientMain.TryAttackEntity` (only within
  attack range of its `SelectionBox`: then `OnInteract(Attack)` and an entity-interaction packet). Right:
  `InWorldAction`, then the held item's use (with the entity as its entity selection), and only if nothing uses it,
  `OnInteract(Interact)` and a packet. The server (`HandleEntityInteraction`) looks for the entity within
  PickingRange + 10 of the player, checks attacks against the selection box, then `TriggerPlayerInteractEntity` and
  `OnInteract`.
- **Reach by the selection box:** `ServerPlayer.isInInteractionRangeOf(IPlayer, Entity, float)` (internal static,
  behind `IPlayer.IsInInteractionRangeOf`) measures to the entity's `SelectionBox`. With `AntiAbuse` on (off by
  default) it refuses entity interactions and held-item use (`HandleHandInteraction`: eating while the crosshair is on
  an entity) out of reach. The client's `DrawAim` shows the red "in reach" crosshair when the selection box is within
  attack range. Neither looks at `IntersectsRay`.
- **The look-at box:** `HudElementBlockAndEntityInfo` (Vintagestory.Client.NoObf) recomposes every 500 ms anyway;
  its private `ComposeBlockInfoHud` redraws only when the title or text changed, so calling it is cheap.
- **Items that act on any selected entity:** an animal trap (`BlockBehaviorCreatureContainer`) says "too wild to
  catch"; the creative entity mover (`ItemEntityMoverTool`) grabs whatever entity is selected.
- **Despawning:** `Entity.Die` only sets `Alive = false` and `DespawnReason`; the server despawns it (the entity's
  `OnEntityDespawn`, then `sapi.Event.OnEntityDespawn`) in its next tick. Reasons: Death, Combusted, OutOfRange
  (clients), PickedUp, Unload (the chunk), Disconnect (players), Expire, Removed.
- **A player's spawn:** `IServerPlayer.SetSpawnPosition` stores it in `ServerWorldPlayerData.SpawnPosition` (not in the
  API: read it by reflection) and sends the player's data; `ClearSpawnPosition` doesn't. `RemainingUses` -1 is
  unlimited and makes no respawn message. At respawn the position becomes (x + 0.5, y, z + 0.5) with its yaw (a y of
  null or 0 means the terrain's height); the server's handler teleports there (`TeleportTo`, which loads the chunks,
  then revives the player in its callback) before mods' `PlayerRespawn` handlers run.
- **Save data:** `sapi.WorldManager.SaveGame.StoreData(key, byte[])` / `GetData`, written on `GameWorldSave`, read
  after `SaveGameLoaded`.
- **`IPlayer` can't be implemented outside the game:** it has an `internal` member (`IsInInteractionRangeOf(BlockPos,
  float)`), so neither a class nor a `DispatchProxy` can stand in for an `IServerPlayer` (the self-test uses its own
  stand-in behind `ISpawnHolder`).

## Damage, pumps, repairs and sinking

Read 2026-10-01 for the damage model (Magelan 0.3.5, `src/Ship/ShipFlooding.cs`).
- **Held right-click on an entity:** while a mouse button is held the client runs its world interaction again every
  `BuildRepeatDelay` (0.25 s; `SystemMouseInWorldInteractions.HandleMouseInteractionsNoBlockSelected` when an entity or
  nothing is selected), each time through `TriggerInWorldAction(InWorldRightMouseDown)` first, so a mod that handles the
  first click gets the repeats too (and must take them, or vanilla uses the held item). Whether it's still held:
  `capi.Input.InWorldMouseButton.Right`. Magelan's repairs and pumping count their own time from the first click.
- **Saving without syncing:** `Entity.ToBytes` writes `Attributes` only when not for a client (`!forClient`), after the
  watched attributes: a mod's server-only state saved with the entity (the parts' exact hurt and wear). `TreeAttribute`
  takes `FloatArrayAttribute` / `IntArrayAttribute` values; `SyncedTreeAttribute.SetBytes` marks the key dirty.
- **Putting someone off a mount on the server:** `EntityAgent.TryUnmount()` calls the seat's `DidUnmount` and removes
  the watched `mountedOn`; clients follow that attribute and unmount too, and the player stays where the seat had them
  (vanilla's boat seats move the rider to a free spot in their own `DidUnmount`; Magelan's seats don't). A non-player
  agent (the self-test's chicken) can be mounted on a ship's deck seat with `TryMount` as well.
- **Boats back from items:** vanilla's `ItemBoat` spawns the entity type with the item's own code, so
  `World.GetEntityType(stack.Collectible.Code)` gives the boat an item on the gallows stands for (a rowing boat, a raft).
- **Under water:** `EntityAgent.IsEyesSubmerged()` (the block at the eyes is a liquid): a diver.
- **Vanilla items and sounds used:** `game:plank-*` (the single plank item; `planks-*` is the block),
  `game:metalnailsandstrips-*`, `game:flaxfibers`, `game:resin`, `game:sail` (one item, no variants: the windmill's sail, made of linen), `game:sail-large-<wood>` (a boat's
  sail part), `game:cloth-<colour>` (13 colours, plain .. white), `game:linen-<pattern>-<side>` (a decor block), `game:rope`; `sounds/block/pump`, `sounds/effect/woodgrind`, `sounds/effect/water-pour`,
  `sounds/block/cloth`, `sounds/effect/ropestretch`, `sounds/block/planks`.
- **The game's calendar for wear:** `World.Calendar.TotalHours` runs on while a ship's chunk is unloaded and races while
  everyone sleeps; Magelan counts wear only while she's ticked, at most an hour at a time.

## Cannons: shot, particles, damage to creatures, breaking blocks

Read 2026-10-01 for the guns (Magelan 0.3.5, `src/Ship/CannonShots.cs`).
- **Particles from the server:** `ServerMain.SpawnParticles(provider)` serialises the provider (its class must be in
  `ClassRegistry.ParticleProviderTypeToClassnameMapping`: vanilla's `SimpleParticleProperties`, `WaterSplashParticles`,
  `BlockCubeParticles`, `StackCubeParticles` are) and sends it to every playing client that `DidSendChunk` the chunk of
  `provider.Pos` (so as far as they see, not a fixed range); each client then makes its own particles from it.
  `SpawnCubeParticles(blockPos, pos, radius, quantity, scale)` is a puff of that block's own texture.
- **Particle sizes and speeds** (`ParticleGeneric`): velocities are blocks a second (`motion = Velocity × dt`); a quad of
  size 1 is 0.25 block (its model is a 0.25 quad), so size 4 is a block; `SizeEvolve` / `OpacityEvolve` LINEAR add their
  factor over the particle's life; `GravityEffect` scales the pull down (negative rises); `VertexFlags` 255 glows.
  Vanilla's smoke (the bloomery's) is `SimpleParticleProperties` with `SelfPropelled`, opacity fading, size growing.
- **`WaterSplashParticles`** (Vintagestory.API.Common): `BasePos` + `AddPos` (area), `AddVelocity`, `QuantityMul` (× 30
  droplets), gravity 1, 1.25 s; vanilla's own splashes use it with `sounds/environment/smallsplash`, `mediumsplash`,
  `largesplash`.
- **Hurting creatures with a projectile:** `Entity.ReceiveDamage(new DamageSource { Source, Type, SourceEntity,
  CauseEntity, SourcePos, HitPosition, KnockbackStrength }, damage)`. `EntityPlayer.ShouldReceiveDamage` itself refuses
  damage whose `CauseEntity` is a player when the server's `AllowPvP` is off or he lacks `attackplayers` (and creative or
  spectator players take none). Vanilla's `EntityProjectile.CanDealDamage` also asks the firer's `attackcreatures`
  privilege for creatures; Magelan's balls follow both.
- **Breaking blocks for someone:** `IBlockAccessor.BreakBlock(pos, byPlayer, dropMultiplier)` breaks and drops like a
  player breaking it (a null player is allowed). `ILandClaimAPI.TestAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)`
  answers `EnumWorldAccessResponse.Granted` or why not (without telling the player); `Claims.Get(pos)` lists the claims
  there (for a shot fired by no one: none may be there). `Block.GetCollisionBoxes(blockAccessor, pos)` are the boxes a
  ray is tested against (block-local, 0..1); `Block.Resistance` (planks 3.5, stone 6+) and `BlockMaterial` tell soft
  from hard.
- **Sounds used:** `sounds/effect/mediumexplosion` (the boom; `PlaySoundAt(..., range 256)` sends it to the players in
  range), `sounds/environment/largesplash`, `mediumsplash`, `sounds/thud`; the gun block `block/anvil` and
  `block/heavymetal-hit`. A path without its number is a sound set in vanilla's `sounds/soundconfig.json`
  (`block/anvil` → anvil1..3, `environment/largesplash` → 1, 2; `ClientMain.ResolveSoundPath` picks one), else the
  file itself.
- **A block shape bigger than its block:** a shape's elements may reach outside 0..16 (the gun is 3 blocks long); its
  collision and selection boxes stay in the block. `HorizontalOrientable` with `side` variants and `shapebytype` `rotateY`
  (north 0, east 270, south 180, west 90) turns it to face the way it's placed.
- **C# trap:** inside a lambda whose parameter is named `_`, `out _` is that parameter, not a discard (the self-test's
  `WaitFor(..., _ => ... out _)` failed to compile); name such parameters.

## Wrecks: a block entity that draws itself, placing many blocks, containers

Read 2026-10-01 for the wrecks (Magelan 0.3.5, `src/Ship/BlockWreck.cs`, `ShipWrecks.cs`).
- **Placing a block with its data:** `IBlockAccessor.SetBlock(id, pos, stack)` → `Block.OnBlockPlaced(world, pos, stack)`
  → `SpawnBlockEntity(EntityClass, pos, stack)`; on the server that creates the block entity, `Initialize`s it, adds it to
  the chunk, calls `be.OnBlockPlaced(stack)` and then `MarkBlockEntityDirty`, so what the entity reads from the stack's
  attributes there reaches clients with it. Worldgen's accessor keeps the stack as `stackForWorldgen`, and
  `BlockEntity.Initialize` calls `OnBlockPlaced(stackForWorldgen)`: the same code works for both. Setting the same block
  id again doesn't call `OnBlockPlaced` (no new entity): change the entity itself and `MarkDirty(true)`.
- **Water stays:** setting a solid-layer block clears the fluid layer only if `Block.DisplacesLiquids` (by default
  `SideSolid.SidesAndBase`): a block with `sidesolid: false` (a wreck block, a chest, the gun) sits in the water with the
  water kept; sand clears it. Breaking a block (`SpawnDropsAndRemoveBlock`: `GetDrops` while its entity is still there,
  `be.OnBlockBroken`, the drops spawned, then `SetBlock(0, pos)`) leaves the water.
- **Per-position block behaviour:** `Block.GetCollisionBoxes`, `GetSelectionBoxes`, `GetParticleCollisionBoxes`
  `(blockAccessor, pos)` and `GetBlockMaterial(blockAccessor, pos, stack)` are asked per position (vanilla's microblocks
  read their entity there); mining speed uses `GetBlockMaterial` at the position (`CollectibleObject.GetMiningSpeed`).
  `OnPickBlock` (middle click) and `GetPlacedBlockName` can be overridden the same way.
- **A block entity's own mesh:** `BlockEntity.OnTesselation(mesher, tesselator)` returning true replaces the block's
  shape. Its `MeshData` goes through the chunk tesselator's JSON path (`AddJsonModelDataToMesh`): lit per face from the
  light there, and a face whose `XyzFaces` names a facing and that lies on the block's edge (within 0.01) is left out
  when the neighbour there is opaque on that side, as for JSON blocks. `ModelCubeUtilExt.AddFace(mesh, facing, centre,
  size, originUv, sizeUv, atlasTextureId, colour, shade, flags)` (in the API) adds one face of a box the way the shape
  tesselator does: its four corners are `CubeMeshUtil.CubeVertices` of that face (scaled about the centre), each corner's
  uv `originUv + sizeUv × CubeUvCoords`, `XyzFaces` the facing's `MeshDataIndex` when shaded; the mesh needs
  `.WithXyzFaces().WithRenderpasses().WithColorMaps()` (vanilla's microblocks make theirs like that, with
  `BlockFacing.AllVertexFlagsNormals[face]` as the flags). Vanilla's microblocks read their neighbours' block entities
  on the tesselation thread to leave out the faces between them. When a block goes, the chunks next to it are marked for
  tesselating again if it was on their edge (`MarkAdjacentNeighboursDirty`).
- **A block's textures:** a texture source falls back to the block's `all` for a code its shape asks for and the block
  hasn't (`TextureSource`); `capi.BlockTextureAtlas.GetPosition(block, code, true)` is a texture of the block's own by its
  code (every texture in a block type's `textures` is in the block atlas).
- **Chiselling:** `ItemChisel.IsChiselingAllowedFor` allows a `BlockMicroBlock` only if it's a `BlockChisel` (the
  chiselled block); any other block only by `IsValidChiselingMaterial`: its `attributes.canChisel` if it has one, else
  only a cube or a block with the basic cube shape. A `canChisel: false` block is never chiselled.
- **Vanilla containers placed by code:** `chest-<side>` with the stack attribute `type` (`BlockEntityGenericTypedContainer
  .OnBlockPlaced` reads it and sizes the inventory: `normal-generic` and `normal-aged` 16 slots, both drop themselves;
  `collapsed1..4` are retrieve-only); a storage vessel (`storagevessel-<colour>-fired`, 12 slots) has `UnstableFalling`,
  which drops it only when the block under it is replaceable (over 6000). A trunk (`BlockGenericTypedContainerTrunk`)
  is two blocks: not placed with a plain `SetBlock`. Their inventory is `BlockEntityContainer.Inventory`; set a slot's
  `Itemstack`, `MarkDirty` it and the entity.
- **Entities:** `Entity.TeleportToDouble` moves it once its chunk column is loaded (a callback; on a loaded one within a
  tick). `Entity.Alive` stays true when its chunk unloads (Magelan's `ServerShips` follows the despawns).
  `IWorldAccessor.GetEntitiesAround(pos, horRange, vertRange, match)`.
- **Vanilla items and textures used:** `game:plank-oak`, `plank-pine` (the plank item also has `aged`, `veryaged`),
  `firewood`, `stick`, `resin`, `metalplate-copper`, `metalbit-copper`, `metalbit-iron`, `metalnailsandstrips-iron`;
  sand and gravel blocks' first code part is `sand`, `gravel`, `muddygravel`; textures `block/wood/planks/aged/aged1`
  (grey weathered planks), `aged/veryaged1` (near black, greenish), `block/wood/debarked/aged`,
  `block/metal/tarnished/tarnished-copper` (verdigris), `block/metal/tarnished/rusty-iron`; sounds `walk/wood`,
  `block/planks`.

## Screenshots, data folders, clicks and dialogs (the surveyor's chalk)

Read 2026-10-02 for the surveyor's chalk (Magelan 0.3.6, `src/Client/SurveyorsChalk.cs`).
- **F12:** `SystemScreenshot` (VintagestoryLib) sets the handlers of the hotkeys `screenshot` (F12) and `megascreenshot`
  (Ctrl + F12). The handler only sets a flag. The shot is taken at the end of that frame (`EnumRenderStage.Done`, from
  the default framebuffer, so with the GUI and the HUD in it); with the client setting `ScaleScreenshot` on it's taken
  at `AfterFinalComposition` from the primary framebuffer instead (before the GUI). At most one a second (a flag set
  sooner waits). It plays `sounds/camerasnap` and says `screenshottaken-normal` in chat with the file's name.
- **Where:** `Screenshot.SaveScreenshot` writes `GamePaths.Screenshots` (= `MyPictures\Vintagestory`, created if
  missing), named `DateTime.Now` as `yyyy-MM-dd_HH-mm-ss.png` (to the second: a second shot in the same second
  overwrites the first). With `screenshotExifDataMode` > 0 the player's position, yaw and pitch, the seed and the world
  config go into the PNG's EXIF.
- **From a mod:** `capi.Input.GetHotKeyByCode("screenshot")?.Handler?.Invoke(null)` takes one exactly as F12 does
  (vanilla's own `hotkey://` links in VTML call handlers the same way). The API doesn't return the file's name: look for
  the newest PNG in `GamePaths.Screenshots` written since. (`capi.Render.GrabScreenshot(w, h, scale, flip)` reads
  whatever framebuffer is bound when it's called, `GL.ReadPixels`, and `BitmapRef.Save(path)` writes it: it would have
  to be called at the right render stage with the right framebuffer; not used.)
- **Data folders:** `GamePaths.DataPath` is `%APPDATA%\VintagestoryData` (the game's folder in a dev environment, or
  `--dataPath`). `api.GetOrCreateDataPath(name)` (`APIBase`, both sides) is `DataPath\name`, created if missing:
  `ModData` is where Magelan writes its notes. `GamePaths.ModConfig`, `Logs` (or the custom log path), `Saves` are
  beside it.
- **World clicks repeat:** while a mouse button is held the client runs its world interaction again every
  `BuildRepeatDelay`, and each time calls `TriggerInWorldAction` first (`InWorldLeftMouseDown` / `RightMouseDown`, `on`
  true): a mod acting on a click must act once a press. `PreventDefault` there stops everything after it: the held
  item's use and attack, breaking a block (in creative at once), using a block, attacking or interacting with an
  entity. An item's own `OnHeldAttackStart` / `OnHeldInteractStart` with `PreventDefault` stops the swing and the
  breaking too.
- **Shift:** `capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft]` (or `ShiftRight`), whatever the player is mounted
  on (a seat's controls take the movement keys).
- **Dialogs that scroll:** a static text (`AddStaticText`) is drawn once into the composer's static texture: a clip
  (`BeginClip`) doesn't cut it and moving its bounds doesn't move it. Rich text (`AddRichtext`, its own texture the size
  of its bounds) and text areas (`AddTextArea`) are clipped and scroll: vanilla scrolls them by setting their
  `Bounds.fixedY` from a scrollbar (`AddVerticalScrollbar`, `SetHeights(visible, total)`, the text area's height in
  `SetNewTotalHeight` as it grows: `GuiDialogTextInput`, the story-generation failure dialog, the chat). VTML decodes
  only `&lt;`, `&gt;` and `&nbsp;`. `GuiDialog` has `OnOpened` and `OnClosed` events.
- **A texture from pixels:** `capi.Render.LoadOrUpdateTextureFromRgba(int[], linearMag, clampMode, ref tex)` needs a
  `LoadedTexture` that already has its width and height (`new LoadedTexture(capi, 0, w, h)`), on the main thread; the
  ints go to GL as RGBA bytes, so `ColorUtil.ToRgba(a, r, g, b)` (a << 24 | r << 16 | g << 8 | b) swaps red and blue
  (grey is grey). Mipmaps are built.
- **Vanilla's chalk:** the loose stone `game:stone-chalk` is drawn with the shape `game:item/stone` (one texture,
  `#material`) and the texture `game:block/stone/rock/chalk1`; it's also vanilla's cave-art pigment (`ArtPigment`).
  The surveyor's chalk looks the same.

## The firehearth: vanilla's firepit aboard

Read 2026-10-02 for the working hearth (Magelan 0.3.6, `src/Ship/HearthFire.cs`, `ShipHearth.cs`, `src/Client/HearthDialog.cs`,
`HearthClient.cs`), from the 1.22.7 `BlockEntityFirepit`, `InventorySmelting`, `GuiDialogBlockEntityFirepit`, `BlockFirepit`,
`BlockCookingContainer`, `ItemFirestarter`, `BlockBehaviorCanIgnite` and the firepit's block JSON.
- **The inventory works without a block entity.** `InventorySmelting` (7 slots: 0 fuel `ItemSlotSurvival`, 1 input
  `ItemSlotInput`, 2 output `ItemSlotOutput`, 3–6 `ItemSlotWatertight`) uses its public `pos` only in
  `discardCookingSlots`, which `DidModifyItemSlot` calls when slot 1 gets something without a `storageType` attribute (the
  pot taken out: its cooking slots' contents are dropped at `pos`, which must not be null). `CookingSlots` is empty unless
  the input's attributes have `cookingContainerSlots` (the fired clay pot: 4, `maxContainerSlotStackSize` 6, `storageType`,
  `maxTemperature` 200); `Slots` (its `ISlotProvider`) is always the four. Vanilla's firepit makes it with `(null, null)`
  and `LateInitialize("id-instance", api)`. With `Api` null (offline) `SlotsFromTreeAttributes` loads stacks unresolved and
  calls no `DidModifyItemSlot`; with an api its modified-slot check compares a slot's stack with itself, so loading never
  calls it either.
- **The burn tick** (`OnBurnTick`, a 100 ms listener, server; ported as `HearthFire.Tick`): fuel burns `dt`, or `dt / 4`
  while the input is empty and the fire within 50 °C of its `maxTemperature`; at 0 the next fuel is lit in the same tick if
  `canIgniteFuel` (else the block goes `extinct` and `extinguishedTotalHours` is noted); extinct for more than 2 game
  hours: `cold`, `canIgniteFuel` false. `changeTemperature(from, to, dt)` steps `dt × (1 + gap / 28)` (a 28 s time
  constant plus 1 °C/s). The input (`heatInput`) heats with step `(1 + clamp((furnace − t) / 30, 0, 1.6)) × dt`, /11 once
  at its melting point, averaged over the stack size, never over its `maxTemperature` (combustible props or the
  `maxTemperature` attribute); at its melting point or over, `oreCookingTime += clamp((int)(t / meltingPoint), 1, 30) × dt`,
  below it −1 a tick; cooked when that passes `GetMeltingDuration` (30 with no input). A pot's temperature is its cooking
  slots' coldest (0 if they're empty). Burning means `fuelBurnTime > 0`; lit fuel sets `maxTemperature` and the burn time
  from its `CombustibleProperties` (`BurnTemperature` int, `BurnDuration` s, `SmokeLevel`): firewood 700 °C 24 s, a stick
  700 °C 8 s. Raw red meat: melting point 150, 30 s, `smeltingType` cook. The cooking pot: melting point max(100, its
  ingredients'), duration max(40, Σ (20 × count, or meltingDuration × count / ratio) / 3); `CanSmelt` is a recipe with 1–6
  servings (porridge: 2–4 grain stacks, each the same count = servings); `DoSmelt` puts the cooked pot (`claypot-<colour>-cooked`,
  `SetContents`) into the output slot without looking at what's there.
- **Heat:** `IHeatSource.GetHeatStrength` 10 burning, 0.25 smouldering (`canIgniteFuel`). Body temperature sums sources
  `× min(1, 9 / (8 + d^p))`, d the squared distance from (x, feet + 0.9, z) to the block's nearer edge
  (`BlockPos.DistanceSqToNearerEdge`: 0 across its width, from 0.75 up it in height), p 0.875 in an enclosed room (it walks
  the room's blocks) else 1.25 (a ±3 block cube).
- **Lighting:** `GetIgnitableState`: no fuel or burning → `NotIgnitablePreventDefault`; held more than 3 s → `IgniteNow`.
  `ItemFirestarter`: on releasing it at `IgniteNow`, on the server, only if `Rand.NextDouble() <= 0.25`: `DamageItem` 1 and
  `OnTryIgniteBlockOver` (sets `canIgniteFuel`, `extinguishedTotalHours`); its `igniteAnimation` attribute ("startfire") and
  `sounds/player/handdrill` after 0.5 s. A block with `BlockBehaviorCanIgnite` (lit torches, `torch-*-lit-up`):
  `sounds/torch-ignite` at once, after 3 s always. Both do nothing without a block selection (an entity in the crosshair).
  Vanilla's right-click on a lit-able firepit with a torch defers to the torch; Shift + right-click puts one fuel or one
  thing with a melting point in (`EnumMergePriority.DirectMerge`); with a `mealContainer` in hand a bowl is served from a
  cooked pot in the input or output (`BlockCookedContainerBase.ServeIntoStack`, public), a pot goes into an empty input.
- **The dialog** (`GuiDialogBlockEntityFirepit`) sends its slot clicks with `SendBlockEntityPacket` and its base closes by
  `IsInInteractionRangeOf(BlockPos)`: not reusable for an entity. Its layout (bounds, `capi.Gui.Icons.DrawFlame` and
  `DrawArrowRight` clipped to the fuel left and the cooking done, the `outputText` from `InventorySmelting.GetOutputText`) is
  rebuilt in `HearthDialog`; `DrawDelegateWithBounds` takes Cairo types (reference `Lib/cairo-sharp.dll`).
- **Drawn:** the shape `game:shapes/block/wood/firepit/{burnstate}-{normal|spit|wide}` tesselated with the firepit block;
  a cold firepit with no fuel is drawn `extinct` (ashes). Wide for anything `IInFirepitRendererSupplier` (the clay pot,
  cooked or not: `PotInFirepitRenderer` draws `shapes/block/clay/pot-opened-empty` 1/16 up and `pot-part-lid` 13/32 up,
  rattling from 50 to 100 °C, with the cooked variant's textures; in the output the meal from `MealMeshCache` with the food
  5/32 up, a cached ref not to dispose), else the content's `inFirePitProps.useFirepitModel` (meat: Spit; its item drawn by
  `FirepitContentsRenderer`: translation, then (origin.x, 0.6 + origin.y, origin.z), rotate X Y Z, scale, −origin).
- **Particles:** a block's `ParticleProperties` (`AdvancedParticleProperties`, from `particlePropertiesByType`; the lit
  firepit's: embers, smoke quads living 18 s rising 0.2 b/s, flames living 0.25 s; the extinct one's faint smoke) are
  spawned by the async particle thread every 33 ms at block + `TopMiddlePos` (the selection box's top middle: (0.5, 0.5,
  0.5) for the firepit) plus `PosOffset`; with a pot vanilla spreads them along four edge strips a quarter as many each.
  `BeginParticle` sets `ParentVelocity` to vanilla's wind only when `WindAffectednes > 0` (a null one counts as none);
  `baseVelocity` is added to every particle's. `capi.Event.RegisterAsyncParticleSpawner` runs a delegate there (false
  removes it). Particles collide with world blocks only.
- **Sound and light:** `sounds/environment/fireplace.ogg` looped at volume 0.66 while burning (`BEBehaviorFirepitAmbient`),
  `sounds/effect/cooking.ogg` (range 10) at clamp((t − 50) / 50) with a pot; `sounds/effect/extinguish` when doused. The lit
  firepit's light `[7, 7, 16]`, the extinct one's `[7, 7, 3]`.
- **The calendar:** `SpeedOfTime` 60 × `CalendarSpeedMul` 0.5: a game hour is 2 real minutes (embers go cold 4 minutes
  after the fire goes out).

## Decorating: how vanilla places and orients blocks, containers, dye, outlines

Read 2026-10-02 for decorating ships (Magelan 0.3.8, `src/Ship/ShipPieces.cs`, `VanillaPieceBlock.cs`, `ShipDecor.cs`,
`src/Client/DecoClient.cs`), from the 1.22.7 `Block`, `BlockFacing`, `BlockBed`, the behaviours below and the block JSON.
- **The way a placer faces:** `Block.SuggestedHVOrientation(player, sel)[0]` is `BlockFacing.HorizontalFromAngle(atan2(dx,
  dz) + π/2)` of (player's eye − the hit point), `HORIZONTALS_ANGLEORDER` = east, north, west, south: the horizontal way
  **from the player toward the block** (the way they look). North is −z, east +x.
- **HorizontalOrientable** (`variantCode` "horizontalorientation", else "side"): the variant is that look direction; it
  drops its "north" variant (`dropBlockFace`). The trunk is one (with Multiblock).
- **HorizontalAttachable** (paintings, …): needs a wall; the variant (`CodeWithParts`, the last code part) is
  `blockSel.Face.Opposite`: the side **toward the wall**. `painting-*-north` hangs on its block's north side (selection
  box z 0..1/16). Drops "north".
- **OmniAttachable** (lanterns; `facingCode`, the lantern's "position"): the variant is the clicked face of the block it
  hangs on: "up" stands on a floor, "down" hangs from a ceiling, north/east/south/west on a wall **away from** the wall
  (the opposite of HorizontalAttachable). Drops "up". `BlockLantern.GetLightHsv(ba, null, stack)` gives a lantern's light
  from the stack's lining and glass.
- **Beds** (`BlockBed.TryPlaceBlock`): the feet go where it's put, the head a block further in the look direction; both
  blocks' side is the look's opposite (toward the player): `bed-<material>-head-<side>`, `bed-<material>-feet-<side>`.
  `attributes.sleepEfficiency` by type. Vanilla beds don't set a spawn.
- **Multiblock** (`BlockBehaviorMultiblock`, VSEssentials): sizes and the controller's place (`cposition`) per variant;
  `IterateOverEach(controllerPos, …)` (public) walks the cells. The trunk: north (+x), east (+z), south (−x), west (−z):
  the second cell lies across the placer's view.
- **Typed containers** (`BlockGenericTypedContainer`, trunk, labeled chest, storage vessel): the block entity draws
  `GenMesh(capi, type, attributes.shape[type], tesselator, new Vec3f())` turned by its `MeshAngle` about the block's middle
  (`MeshData.Rotate(0, MeshAngle, 0)`); `DoPlaceBlock` sets it to atan2(dx, dz) of (player − block), in 22.5° steps
  (`rotatatableInterval`): unturned the front (the latch) faces +z, toward a player standing south. Its boxes are
  `attributes.collisionSelectionBoxes[type]` when there are any. Slots `attributes.quantitySlots[type]`. A crate
  (`BlockCrate`) draws `GenMesh(capi, contents, type, label, "closed", Shape, rotation)`.
- **Chairs** have no seat in vanilla (a plain block, `UnstableFalling`); the player's animations include `sitflooredge`
  (`sitidle`) and `sitboatidle`.
- **Dye** is a liquid item, `game:dye-<colour>` (`ItemLiquidPortion`; blue, red, yellow, purple, woad, green, orange,
  black, gray, pink, white; 100 items a litre), carried in liquid containers: `ILiquidSource.GetContent(stack)` is the
  portion (its `StackSize` in items), `TryTakeContent(stack, items)` takes it. Its texture `block/liquid/dye/<colour>`
  (32 × 32) is one flat colour; woad's is blue's. `game:metalplate-gold` exists (it smelts into two gold ingots).
- **A tinted texture:** a composite `base++3~overlay` with a flat-coloured overlay multiplies the base by that colour
  (Magelan's paint: a grey wood grain times the dye).
- **Outlines:** `Vintagestory.Client.NoObf.WireframeCube` (in VintagestoryAPI.dll) `CreateUnitCube(capi, colour)` and
  `Render(capi, Matrixf modelView, lineWidth, colorIn)`: shader program 25, the matrix the camera's (`CameraMatrixOriginf`)
  times the model; depth test on, depth writes off, blended. Black at half alpha reads like vanilla's block outline.
- **Shapes:** `Shape.TryGet` parses the asset each time (no cache). `Shape.Clone()` copies the elements and their
  `FacesResolved` arrays but **shares the face objects and the Textures dictionary**: copy a face before giving it another
  texture.
- **Lanterns' stacks** (`BlockLantern`, read 2026-10-03): name (`block-lantern-small-up-<material>`), mesh and textures
  come from the stack's `material`, `lining`, `glass`; a stack without `material` is "lantern-small-up-" with missing
  textures. `BELantern`'s defaults: copper, plain, quartz; the materials are the block's `Textures` keys (no plain
  "bronze": tinbronze, bismuthbronze, blackbronze).
- **Tapestries** (`BlockTapestry`, HorizontalAttachable): the picture is `genMesh(rotten, type, rotVariant, inventory)`
  (public) by the stack's `type`. **Wallpaper** (`wallpaper-*`): the "Decor" behaviour, drawtype SurfaceLayer, put on a
  world block's face as decor (not a block of its own). **Planks**: `planks-<wood>-<ud|ns|we>` (Pillar), drawtype cube.
- **Drawing text yourself:** `new Cairo.ImageSurface(Format.Argb32, w, h)`, `ctx.SelectFontFace(GuiStyle.DecorativeFontName,
  …)`, `TextPath` + `FillPreserve` + `Stroke`, then `capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref tex)`.
- **The Release build zips** into `release/` and deletes the other `magelan-*.zip` there (the tested build's zip with
  it): check code with `dotnet build -c Debug` between handed-out builds.
- **Chat commands from another file:** `api.ChatCommands.GetOrCreate("magelan").BeginSubCommand(...)` adds to a command
  made elsewhere.
