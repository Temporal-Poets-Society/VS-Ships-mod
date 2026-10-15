# Hansa: The Last Voyage

A separate grid-based rebuild of Magelan, started 14 October 2026.

The sailing, waves, walking aboard, crew, working vanilla blocks, cooking,
guns, flooding, repairs and persistence code is preserved in the independent
`hansa` mod. The ship geometry is being rebuilt around a data-defined block grid.

## Current status

The first grid prototype (two-bay slice, Python compiler, canvas preview, `Hansa.Grid`)
is archived under `archive/gpt-grid-prototype` as superseded reference and is not built.
The replacement ship-design model is described in `docs/proposals/ship-design-architecture.md`;
its section 8 lists the agreed decisions. Milestone M1, the construction core, lives in
`src/Construction`: a pure C# library (no game types) that turns a few-KB design
(`assets/hansa/config/shipdesigns/two-bay.json`) and the block kind library
(`assets/hansa/config/construction/kinds.json`) into one master block per cell, with
geometry, collision, support rules, grey-guide queries and saves, plus walls and doors on cell
faces and two-cell ladder-stairs. It is compiled into the mod
but not yet connected to the game. The inherited carrack remains a reference design under
Hansa's independent identity.

## Build and checks

Requires the existing Vintage Story installation and .NET SDK used by Magelan.
Hansa references the existing sibling Sea DLL; it does not build or alter Sea.

```powershell
dotnet build Hansa.csproj
dotnet run --project tests/ConstructionTests/ConstructionTests.csproj
dotnet run --project tests/HansaTests/HansaTests.csproj
```

`ConstructionTests` checks the construction core in plain .NET. `-- --dump` prints the cell
slots of two cross-sections; `-- --export <file>` writes visible faces as JSON for a preview.

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
