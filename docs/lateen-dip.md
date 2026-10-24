# Lateen dip (chalk note 64)

Note 64 (24 October 2026, the caravel, 0.1.0-dev.8): "Main sail penetrates the mast. How lateen sails should work
when changing sides?" The user chose the real lateen's way: the yard is dipped round the fore side of its mast, so the
sail always lies on the lee side and never passes through or presses into the mast.

Status: implemented on branch `claude/caravel-lateen-dip-cbb277` with offline checks and a self-test check. Not yet
tried in the game by the user.

## How it works

- **Two poses.** Each dipping lateen has its pose as exported (the caravel's are on the port side of their masts) and
  the other tack's pose: the yard, the sail and every rope that follows the yard (sheet, tack, halyard, anchors too)
  mirrored across the mast's centre plane (z = the mast's z). The rig is symmetric, so no second mesh is needed.
- **The lee.** The server decides which side the lateens lie on (`EntityShip.Lee`, +1 starboard, -1 port), from the
  apparent wind: the lee of the wind, decided only when the wind is more than 10° off her bow or stern, only with sail
  set, and not while a dip is still under way. It is saved and synced in the ship's `hansa` tree as `lee`. A ship
  without it (new, or saved before) takes the lee at once, with no dip.
- **The dip.** Server and clients each ease `LeeShown` from one side to the other over the dip's seconds (default 8).
  Its sign is the side drawn, 1 - |LeeShown| how deep into the dip. In order: the sail is brailed up to the yard and the
  sheet eased to fore-and-aft; the yard is bowsed off the mast (`standOff`) and forward (`ahead`); it is peaked upright
  about its sling and hoisted (`hoist`, so its heel comes no lower than it hangs); it is carried round the mast's fore
  side, its sling on a circle about the mast, not turned, so that before the mast the sail's plane is athwartships. There the yard
  (two spars side by side in that plane) is the same mirrored, and the drawing switches to the other side's pose.
  The way back is the same in reverse. A wind that swings back mid-dip simply reverses it.
- **In its frame.** On either side the sheet only takes the yard's after end out to that side's lee (never across the
  mast), the belly is always away from the mast, and the press against the rigging (`SailPress`) is worked in the
  sail's own frame with the rigging mirrored to it. The press is off while the yard is peaked up and going round (the
  sail is brailed by then).
- **Sailing.** Speed comes from the polar on either tack, unchanged. While brailed up for a dip the lateens don't
  drive her (`LateenRig.Drive`: the brailed share of her sail area), so she loses way for a few seconds, as she does
  in irons when tacking.
- **Looking at the sail** (to mend it) finds it where it is drawn on either side (`DamageLayout.SailAt` with the lee
  shown).

Code: `src/Ship/LateenDip.cs` (`LateenDipDef`, `LateenRig`, `LateenPose`), `EntityShip` (`Lee`, `LeeShown`,
`ServerLateens`), `ShipRenderer` (`ComputeLateens`, the yard, ropes and sail), `SailPress.Mirrored`.

## Data: `sails[].dip`

A lateen dips only when its sail has a `dip` object (`{}` is enough). Without it the sail stays on the side it is
made on, as the carracks' mizzen lateens do. Positions are ship-local blocks (x = X/1000, y up, z = -Y/1000).

| Field | Default | Meaning |
|---|---|---|
| `mast` | the mast nearest the yard's axis | the mast it dips round |
| `pivot` | the tip of its halyard (a rope on the yard from the mast's head); else the yard part's axis point at the sail's z | the yard's sling: the point on its centre line nearest the mast's axis |
| `standOff` | 0.4 | blocks the yard is bowsed off the mast while it comes round; it must clear the mast and what's on it (hounds, crosstree bars) |
| `ahead` | 0.2 | blocks the yard is carried forward as it's bowsed off (more than about 0.3 drags the yard's after side into the mainmast) |
| `hoist` | its heel no lower than it hangs set | blocks it is hoisted while upright |
| `seconds` | 8 | how long a whole dip takes |

What the model export has to keep for the mirror: `yard-<code>` turns about its mast's axis (source `sheet`); every
rope anchor on a lateen has a mirrored partner on the hull; `masts[].radius` is the mast's widest radius where the yard
sweeps (the checks measure against it).

The caravel's values now (put in `caravel.json` by hand until `tools/caravel` exports them): both lateens
`{"mast": <its mast>, "standOff": 0.4, "ahead": 0.2, "seconds": 8}`. Main: crosstree bar at y 18.87 reaches 0.50 from the
mast's axis, yard radius 0.165 at 0.35 off the axis, so standOff >= 0.37; mizzen: bar at 16.17 reaches 0.52, yard
radius 0.125, so >= 0.35.

## Checks

- `dotnet run --project tests/HansaTests -c Release -- lateen` (also in the full run): both caravel lateens dip; on both
  tacks over the crew's trim (every 5°, full and reefed) the pressed sail is at least 0.02 clear of every mast and lies
  to leeward, bellied; through the dip both ways the sail stays clear, the yard comes no closer to its mast than it is
  slung or than the mast's radius, goes round the fore side, and the two sides meet within 0.04 where the dip turns;
  the crosshair finds the sail on its other side; the lee, the ease and the drive.
- Self-test (`tools/run_selftest.sh`): after her run with the wind on her port beam, `lee` is starboard and synced, and
  each sail as posed is to starboard of its mast and clear of every mast.

Not covered: the yard against the shrouds. Set, the yard's surface already comes within 0.01 of the aftmost shroud;
while it is bowsed off and peaked up it passes up to 0.04 into it (about the rope's radius), near y 16.7 on the main
and 14.1 on the mizzen. The test prints it, it doesn't fail on it. Real lateen ships slack off the lee shrouds for this.
Also not covered: how it looks in the game.
