# Ships

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
Ships requires Sea `0.6.0-pre.1` (on this branch; 0.1.0 shipped against `0.5.1`). Build against a Sea
checkout's existing Release DLL: `dotnet build -c Release -p:SeaRoot="<Sea checkout>"`.
Ships reads the waves through its own `WaveTrains` (`src/Ship/WaveTrains.cs`): from Sea's typed API whenever Sea
is loaded, otherwise from the open sea contracts' `seacontracts:surface` (`trains`, `seconds`) and
`seacontracts:seastate` (`shelter`); `ShipMotion` works from `WaveTrains` alone
(`dotnet run -c Release -- wave-trains` in `tests/HansaTests` pins it bit for bit to the earlier arithmetic).

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
Hansa references the existing sibling Sea DLL; it does not build or alter Sea.

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
