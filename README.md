# Ships

[Wiki](https://github.com/Temporal-Poets-Society/VS-Ships-mod/wiki): with and without Sea, the wind, the open sea contracts and in-game test results (7 November 2026).

Publication materials: [HTML description](docs/moddb-description.html), [publishing information](docs/moddb-publishing-info.txt),
[local preview](docs/moddb-preview.html) and [wheel logo](docs/publishing-assets/ships-logo-moddb-480.png).
The public release focuses on the Boa Esperança-inspired caravel. The overall goal is historically accurate
ocean-going ships, with gameplay deviations stated explicitly; the planned carrack/nao references
[Nao Victoria](https://www.fundacionnaovictoria.org/history-nao-victoria/). Possible shared ship development with
hoblin remains a future possibility. The earlier construction/carrack notes below describe previous prototypes,
not additional finished ships in the current public package.

**Current package: Ships 0.1.0** (first public release, 1 November 2026). The experimental rowing boat is
withdrawn from crafting, creative inventory and the handbook at the author's request. Existing saved boats keep
their item/entity/model support. The caravel and its charter, crafted from six material bundles, remain.
On this branch Sea is optional (0.1.0 required Sea `0.5.1`): Ships uses Sea `0.6.0-pre.1` or newer when it is
installed and runs without it. Build against a Sea checkout's existing Release DLL:
`dotnet build -c Release -p:SeaRoot="<Sea checkout>"` (the bridge below needs it; Ships.dll itself doesn't).
Ships reads the waves through its own `WaveTrains` (`src/Ship/WaveTrains.cs`): from Sea's typed API whenever Sea
is loaded, otherwise from the open sea contracts' `seacontracts:surface` (`trains`, `seconds`) and
`seacontracts:seastate` (`shelter`); `ShipMotion` works from `WaveTrains` alone
(`dotnet run -c Release -- wave-trains` in `tests/HansaTests` pins it bit for bit to the earlier arithmetic).
Near a beach (Sea 0.6.0's surf) the trains are still the open sea's; over coastal water (Sea's `CoastAt(...).Coastal`,
or the contract's `seastate.surf` shallower than 32) `ShipMotion.Surf` fits a plane to the drawn surface less the open
sea's waves at 3 × 3 points over her waterplane (Sea's `WavePlace.HeightAt` with and without its coast, or
`surface.height` less the sheltered trains) and adds its mean, slope along and slope across to her heave, pitch and roll,
eased over 0.4 s; off the coast nothing of it is worked out. On the server she drifts with Sea's longshore current
(`CurrentAt`, or `seastate.current`) except at anchor, aground or on the bottom; clients follow her synced position. A
contract surface with no trains but a `pose` gives her attitude from the pose. Without Sea's typed API the wind is
`seacontracts:weather`'s (m/s ÷ 25), else the wind the sea follows (`seastate.at`'s), and the chalk's sea state
`seastate.at`. `dotnet run -c Release -- sea-coast` checks the surf, current, pose and contract wind offline.

**Sea is optional (7 November 2026).** The game (1.22.7) has no optional dependencies, and an assembly naming Sea's
types can't load them without Sea.dll, so Ships.dll has no reference to Sea at all and `sea` is no longer in
`modinfo.json`'s dependencies. Everything typed against Sea is in a bridge, `sea-bridge/` (ShipsSea.dll, references
Sea.dll and Ships.dll), built by `Hansa.csproj` after Ships.dll and shipped in the zip as `native/ShipsSea.dll`: the
game's mod loader leaves a mod's `native/` folder alone (any other `.dll` outside the root refuses the mod). In
`HansaMod.Start` (order 0.2, after Sea's own Start at 0.15, all mods' assemblies already loaded)
`src/SeaLink/SeaBridgeLoader.cs` looks for the mod `sea`; if it is there and at least `0.6.0-pre.1` it loads the bridge
with `Assembly.LoadFrom` (once per process) and binds it per side, getting Ships' own `ISeaLink`. Ships' own types
(`Wind`, `Beaufort`, `WindCompass`, `HullWaterClip`) are Sea's field for field and the bridge copies between them.

- **With Sea**: as before. Sea's wind, waves, sea state, dry places and water cut (the hulls' clips go to Sea every frame
  through the bridge, unchanged); the self-tests reach Sea through the same link.
- **Without Sea**: one note in the log. The wind is `seacontracts:weather`'s if some mod publishes it, else the wind of
  a `seacontracts:seastate` owner's sea (`at`'s `WindX`, `WindZ`), so sails and waves agree, else vanilla's
  wind just above sea level (vanilla gives only an east-west strength, so it is always from the west or the east). On
  vanilla's wind Ships warns that there is no wind model: a chat line to the player when the world is up, a warning in
  the client's and server's logs, and an orange line on the wind panel ("No wind model: vanilla wind, east or west
  only") while the sails need no other warning. The waves are `seacontracts:surface`'s, else calm (she floats level on still water). The water
  in her hull: if some mod publishes `seacontracts:hulls`, Ships registers each hull there as a record (the detailed
  record, every field of the clip, when the owner publishes `recordLength`, as Sea does, with the cut's tops along her
  rail when it takes them; `register`;
  the footprint less its inset, no detailed outline) and its dry places (`registerDry`, both sides), checked every 2 s.
  Otherwise Ships does it itself: no drowning in her hold (a postfix on vanilla's breathing check), no underwater fog
  and tint for a camera in it (postfixes on the client's eyes-in-water checks), and a level lid drawn into the depth
  buffer only just above the still water, inside her hull's outline as she lies, before the game draws its water (as
  vanilla's boats hide their water: `EntityBehaviorHideWaterSurface`); the rowing boat gets vanilla's own
  `hidewatersurface` behaviour with her shape's `hideWater` element. What a player sees without Sea and without a hull
  cut: from above, no water surface inside her; with the eyes below the still-water level in her hold (her lower deck)
  the water's surface still shows overhead (a lid is not a cut); a sea higher than the lid (another mod's waves,
  or her settling while she floods) shows inside her.
- **Development switch**: `ModConfig/ships-dev.json` with `{ "SeaBridge": false }` makes Ships ignore an installed Sea's
  typed API and read it through the open contracts only, as another mod's boat would (one warning in the log). Absent
  in play.
- **Tested in game (7 November 2026, AgentBridge bench, singleplayer)**: with Sea, the bridge binds and she sails as
  before. With the switch off, the caravel's hull is cut through `seacontracts:hulls` (dry deck from above) and the
  wind panel shows Sea's wind from `seastate.at` (before this fix it showed vanilla's). Without Sea, the warning is in
  the log, in chat (5 s after the world is up, so it isn't lost while the chat starts) and in orange on the wind panel.
  **Not tested in game**: vanilla wind under sail at length, the lids, the breathing and camera patches, the rowing
  boat's lid, and multiplayer. `dotnet run -c Release -- sea-bridge` in `tests/HansaTests` (after a Release build)
  checks offline that Ships.dll names nothing of Sea, that without Sea its code takes the contracts or vanilla's wind,
  the contract surface or calm, and the hull owner (Ships.dll loaded where Sea can't be found, and never asked for),
  that the bridge's every reference into Sea resolves against the Sea build, the clip copy and the contract record, and
  the lid's outline.

Ships began on 14 October 2026 as Hansa, a separate grid-based rebuild of Magelan; it was renamed Ships (mod ID
`ships`) at 0.1.0-dev.18, and the C# namespace stays `Hansa`. The sailing, waves, walking aboard, crew, working
vanilla blocks, cooking, guns, flooding, repairs and persistence code came from Magelan; the ship geometry is
rebuilt around a data-defined block grid.

## Earlier construction prototype status

The first grid prototype (two-bay slice, Python compiler, canvas preview, `Hansa.Grid`)
is archived under `archive/gpt-grid-prototype` as superseded reference and is not built.
The replacement ship-design model is described in `docs/proposals/ship-design-architecture.md`;
its section 8 lists the agreed decisions. Milestone M1, the construction core, lives in
`src/Construction`: a pure C# library (no game types) that turns a few-KB design
(`assets/ships/config/shipdesigns/two-bay.json`) and the block kind library
(`assets/ships/config/construction/kinds.json`) into one master block per cell, with
geometry, collision, support rules, grey-guide queries and saves, plus walls and doors on cell
faces and two-cell ladder-stairs. It is compiled into the mod
but not yet connected to the game. The inherited carrack remains a reference design under
Hansa's independent identity.

Ideas taken from other walking-deck and sea mods (proposals only): `docs/ideas-from-other-mods.md`.

## Earlier carrack prototype notes

The **Hansa carrack**, `hansa-carrack`, is carrack-m as Magelan's generator builds it, adjusted to
stand on the block grid (`tools/shipgen/carrack.py` with `HANSA_MODULAR=1 HANSA_SHIP=hansa-carrack`;
the changes are listed in `docs/carrack-m-inventory.md`). Her charter is no longer in the creative
inventory (Ships 0.1.0 publishes the caravel only). She has everything carrack-m has: its stairs,
companionway, cabins, fittings and mechanics. Each hull panel is one block. Her ribs, beams, keel and
the other fixed structure can't be edited yet.

The construction-core ship of sections 12 and 13 of the proposal is now `grid-carrack`, checked
offline only.

## Build and checks

Requires the existing Vintage Story installation and .NET SDK used by Magelan.
Ships.dll references nothing of Sea; its bridge `sea-bridge/ShipsSea.csproj` (built by `Hansa.csproj`, shipped as
`native/ShipsSea.dll`) references an existing Sea build (`-p:SeaRoot=<Sea checkout>`, default the sibling `..\Sea`);
neither builds or alters Sea.

```powershell
dotnet build Hansa.csproj
dotnet run --project tests/ConstructionTests/ConstructionTests.csproj
dotnet run --project tests/HansaTests/HansaTests.csproj -- grid
dotnet run --project tests/HansaTests/HansaTests.csproj
```

`tools/run_selftest.sh` runs the self-test on a local dedicated server. Its config's
`"Only": "grid"` (`testserver/ModConfig/hansa-selftest.json`) runs just the grid carrack:
launched, laid, climbed, sealed, afloat, sailed, her doors, hatches, deck cells, a holed panel and
a broadside. `"Ship": "hansa-carrack"` runs the whole inherited self-test on her.

`ConstructionTests` checks the construction core in plain .NET. `-- --dump` prints the cell
slots of two cross-sections; `-- --export <file>` writes visible faces as JSON.

## Preview

```powershell
dotnet run --project tools/HansaPreview/HansaPreview.csproj [design.json] [--out dir]
```

The command builds a design with the same construction core and writes `docs/preview/<code>.glb`
(opens in Blender or Windows 3D Viewer) and `docs/preview/<code>.html`. The HTML file is a
self-contained WebGL2 viewer: depth-buffered, using the game's own textures with one repeat per
block, with part toggles, cut planes, a cell-edge overlay and a waterline plane. Serve the folder
locally (for example `python -m http.server 8765 --directory docs/preview`) and open
`two-bay.html`. The page address can pick a view and options: `#exterior`, `#cutaway`,
`#section` or `#lower`, plus `,edges` or `,water`. Textures are read from the installed game
(`VINTAGE_STORY` or `%APPDATA%\Vintagestory`).

## Fork provenance

`docs/fork-manifest.json` records the source files and their SHA-256 hashes.
The source was labelled 0.3.30 but contained parked work and 0.3.29 generated
assets. It is a development snapshot, not a trusted release. Historical notes are
under `docs/legacy`; `tools/shipgen` is the retained generator for reference.
Build output, ZIPs, test worlds and parked experiment copies were not carried over.

Asset domain, namespaces, entity classes, channels, saved attribute keys and commands
are `hansa`/`Hansa`. Existing Magelan ships are not automatically migrated.
The new project has no Git remote. Baseline commit: `c6c217f` on `main`;
prototype work is on `prototype/grid-bays`.

See `docs/grid-contract.md` for the accepted direction and remaining work.
