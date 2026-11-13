# Ships (`ships`, folder `Hansa`)

Sailing ships you build, sail and live aboard: the caravel now, carracks paused. The project began as Hansa: The
Last Voyage; the folder and C# namespace stay `Hansa`, the mod ID and asset domain are `ships`. Suite rules:
`C:\VS mods\AGENTS.md`. Current state: the card `C:\VS mods\overview\mods\ships.md`. Open issues:
`tools\Update-Backlog.ps1 -Mod ships` from `C:\VS mods` (prefix SH). No decisions file: suite D10, D17, D39, D48,
D53; the construction rules below; `docs\proposals\ship-design-architecture.md` (only section 8 agreed),
`docs\master-model.md`, `models\caravel\caravel-design.md`.

## Repository
- Trunk `main`, tracking the private `origin` (`andrisberzins-1/VS-Ships-mod`): all work and the tags (`v0.1.0`;
  old lines as `archive/*`).
- Public `tps` (`Temporal-Poets-Society/VS-Ships-mod`, with a wiki) gets semi-clean release versions only: `main`
  only, no tags. Push there only on the author's word.

## Build
- `dotnet build Hansa.csproj` (Debug) works without Sea: it warns and leaves out the bridge.
- `dotnet build Hansa.csproj -c Release [-p:SeaRoot=<Sea checkout>]` needs `..\Sea\bin\Release\mod\Sea.dll`
  (build Sea in Release first). It builds `sea-bridge\ShipsSea.csproj` into `native\ShipsSea.dll`, never builds or
  changes Sea, and leaves the carrack assets out of the package.
- It writes `release\ships-<version>.zip` **with overwrite**. `ships-0.1.1.zip` there is the packaged 0.1.1, and
  `main` also says 0.1.1 (SH-0002, SH-0003): bump the version before a Release build.
- Caravel model: `python tools\caravel\caravel_lines.py`, then
  `C:\AstoCAD\bin\AstoCADCmd.exe -c "exec(open(r'C:/VS mods/Hansa/tools/caravel/build_caravel.py').read())"`
  (about 23 minutes on new lines, cached in `%TEMP%\caravel-cache`); the viewer and mesh outputs are git-ignored.

## Test
- `dotnet run --project tests\ConstructionTests\ConstructionTests.csproj` after changing `src\Construction`, the
  kind library or a design.
- `dotnet run --project tests\HansaTests\HansaTests.csproj`: the full pass had 7 known failures (7 November; among
  them the stern dry outline, SE-0027). Groups: `-c Release -- sea-bridge`, `sea-coast`, `wave-trains`,
  `caravel-all`, `grid`.
- Server: `tools/run_selftest.sh` in Git Bash after Release builds of Ships and `..\Sea` (`SEA_ROOT` to override).
  `--with-earth` is stale: it looks for `Earth\release\earthmap-*.zip` and loads no Hydrology.
- `tools\run-sea-contracts.ps1 -SeaPackage <sea zip> [-ModBuild bin\Release\mod]`: the open sea contracts
  against the real Sea on an isolated server.
- `tests\Issue30Tests\run.ps1`: pass `-SeaSourceRoot "C:\VS mods\Sea"` (the default is a removed worktree,
  SH-0005); it uses AstoCAD's Python.
- In game: the AgentBridge bench with the Ships zip, with and without Sea; `"SeaBridge": false` in
  `ModConfig\ships-dev.json` tests the contract path. Not checked: multiplayer, play without Sea.

## Release
- Bump `version` in `modinfo.json`, build Release with Sea's Release DLL, run the self-test, commit, tag `vX.Y.Z`
  on `main`; update `docs\moddb-publishing-info.txt` (it still lists Sea 0.5.1 as required). The last Mod DB set
  is in `C:\VS mods\overview\archive\release-2026-11-01\ships\`. A release may go to `tps/main` on the author's word.

## Construction rules (accepted by the author; docs point here)
- One ship-local block grid for every deck and component; detail inside a cell is geometry, never another
  placement grid. Clip geometry to the real 3D cells; projected width is not surface length.
- Players place ordinary materials; the blueprint (now the master model) supplies the hull shape. No
  player-facing hull-shape block inventories.
- Texture density follows physical surface distance; UVs may tile with deliberate seams; never clamp a long face
  to one atlas tile.
- Keel, ribs and beams are removable and damageable: "required" is a support requirement, not invulnerability.
- The default ship comes complete. Players may rearrange interiors, move stairs and cut deck hatches within
  support, hull and mast limits. Grey guides show unmet requirements only, not every absent default piece.
- Layout changes belong in ship data; the compiler stays generic (moving a window or rib needs no generator edit).
- Carrack (Magelan's carrack as the reference): decks 3 apart (2 clear, 1 structural), the bottom one whole block,
  the waterline 0.875 above a cell boundary; rib cells one block long with 1 or 3 clear cells between (bays of 3,
  one clear cell beside each mast). The caravel shares the frame and vertical rules but not the rib spacing.

## Gotchas
- The `hansa` mod ID is kept for a later quest mod (D39). Ships saved as `hansa:` entities before dev.18 and
  Magelan ships don't load; there is no migration.
- Real consequences stay: don't propose guards such as refusing a turn into land (author). Ships are
  historically accurate (D48).
- `WorkingBlocks` is a dev switch, off; once the feature is finished it becomes always on and the switch goes.
  Don't build "switched off" handling. `src\Construction` is compiled in but not connected to the game.
- Ships saves on `GameWorldSave`: never unsubscribe it in `Dispose`.
- Untracked carrack model outputs and `testserver-grid-pieces.json` belong to other sessions: leave them.
