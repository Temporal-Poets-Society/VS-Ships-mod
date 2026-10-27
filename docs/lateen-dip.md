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
  apparent wind: the lee of the wind, decided only when the wind is more than 10° off her bow or 20° off her stern and
  has stayed on that side for 3 seconds, only with sail set, and not while a dip is still under way. It is saved and synced in the ship's `hansa` tree as `lee`. A ship
  without it (new, or saved before) takes the lee at once, with no dip.
- **The dip.** Server and clients each ease `LeeShown` from one side to the other over the dip's seconds (default 8).
  Its sign is the side drawn, 1 - |LeeShown| how deep into the dip. In order: the sail is brailed up to the yard and the
  sheet eased to fore-and-aft; the yard is bowsed off the mast (`standOff`) and forward (`ahead`); it is peaked upright
  about its sling and hoisted (`hoist`, so its heel comes no lower than it hangs); it is carried round the mast's fore
  side, its sling on a circle about the mast, not turned, so that before the mast the sail's plane is athwartships. There the yard
  (two spars side by side in that plane) is the same mirrored, and the drawing switches to the other side's pose.
  The way back is the same in reverse. A wind that swings back mid-dip simply reverses it.
- **The sheet** (client, `EntityShip.LateenSheet`): as far out as the crew trims for the apparent wind (half its angle,
  5° to `maxSheet`), always toward the server's lee, held until the wind calls for 3° more or less, and eased.
- **In its frame.** On either side the sheet only takes the yard's after end out to that side's lee (never across the
  mast), the belly is always away from the mast, and the press (`SailPress`) is worked in the sail's own frame with the
  obstacles mirrored to it. A dipping lateen is pressed against its own mast and that mast's windward shrouds only (`SailPress.Around`). The press is off while the yard is peaked up and going round (the
  sail is brailed by then).
- **Sailing.** Speed comes from the polar on either tack, unchanged. While brailed up for a dip the lateens don't
  drive her (`LateenRig.Drive`: the brailed share of her sail area), so she loses way for a few seconds, as she does
  in irons when tacking.
- **Looking at the sail** (to mend it) finds the sail mesh as it was last drawn (`EntityShip.SailMeshRay`, the
  renderer's pressed mesh where it was placed) or within 0.25 of its yard (`LateenRig.RayYard`); before it is drawn, the
  box round it (`DamageLayout.SailAt`).

### Note 104 (25 October 2026, 0.1.0-dev.9)

"when sailing i had for part some wobble around main mast axis"; "i point to main mast, but tooltip says mizzen sail.
There is weird triangle sticking out. ropes still cross the sails."

- **Wobble.** The yard's sheet came from the side the wind was on that moment. Running, with the wind wavering across
  her stern, it swung the yards between 25° one way and fore-and-aft (in its frame the sheet never goes across the mast),
  and the lee flipped with only 10° of margin: dips back and forth. Now the lee waits for 20° off the stern and 3 s, the
  sheet always goes toward the server's lee, and the crew trims in 3° steps. Simulated ten minutes with the wind wavering
  about 175° off the bow: before, 4 dips and 278 turn-backs of the main yard by over half a degree (1429° of swinging);
  now, no dip and none (24°, the first trim).
- **The mizzen looked at instead of the main.** From the quarterdeck the ray to the main passed the mizzen yard's fore
  end 1.5 blocks away, inside the box round the mizzen sail (0.9 either side of its plane, 0.4 past its outline). Now
  the drawn mesh decides.
- **The triangle.** The main's leech folded out in a jog where it was pressed against the lee shrouds that cross it. A
  dipping lateen is pressed against the masts only now; the shrouds cross the sail until the model leads them clear.
- **Ropes crossing the sails** (both tacks, mirrored alike; `tests -- lateen-dump` and a mesh check): the main sail is
  crossed by the lee main shrouds from (-1.25 / -2.05 / -2.85, 2.205, ±3.0567) to the masthead (-0.5, 20.725, ±0.18): the
  two after ones at every sheet from 5°, all three from 15°. The mizzen sail by the lee mizzen shrouds from
  (-11.75, 5.105, ±2.3472) and (-12.25, 5.105, ±2.3188) to (-10.5, 18.025, ±0.18), from about 15° of sheet. The sheets
  touch their own sails only at the clews they belay. No rope crosses the other sail.
- **Shrouds windward only** (the user's decision, 25 October): as on real lateen ships only the windward shrouds of a
  lateen's mast are set up. The lee set is cast off and hangs from just aft of the masthead down the after side of the
  mast (`LateenShrouds.HangMatrix`: its eye at the mast's radius + 0.05 aft, its foot 0.12 + 0.07 a rank further aft and
  0.3 above its channel, 0.03 to its side). They change over in the middle of a dip (the sign of `LeeShown`, which the
  server's synced lee drives). A dipping lateen is pressed against the masts and the windward set only
  (`LateenRig.PressIn`, `SailPress.Around`: its own mast and that mast's windward shrouds, in the sail's frame). No shroud, set up or hanging, crosses a sail on either tack or
  through a dip; the yard's surface stays 0.15 (main) and 0.18 (mizzen) from the nearest shroud, also while it dips
  (it went 0.04 into the lee shrouds before).

Code: `src/Ship/LateenDip.cs` (`LateenDipDef`, `LateenRig`, `LateenPose`), `EntityShip` (`Lee`, `LeeShown`,
`ServerLateens`, `LateenSheet`), `ShipRenderer` (`ComputeLateens`, `SailMeshRay`, the yard, ropes and sail),
`SailPress.Mirrored`, `SailPress.Around`; `LateenShrouds` (split, which are set up, the hanging ones), drawn by
`ShipRenderer.ShroudPlace`.

## Data: `sails[].dip`

A lateen dips only when its sail has a `dip` object (`{}` is enough). Without it the sail stays on the side it is
made on, as the carracks' mizzen lateens do. Positions are ship-local blocks (x = X/1000, y up, z = -Y/1000).

| Field | Default | Meaning |
|---|---|---|
| `mast` | the mast nearest the yard's axis | the mast it dips round |
| `pivot` | the tip of its halyard (a rope on the yard from the mast's head); else the yard part's axis point at the sail's z | the yard's sling: the point on its centre line nearest the mast's axis |
| `standOff` | 0.4 | blocks the yard is bowsed off the mast while it comes round; its surface must clear the mast's widest radius where it sweeps |
| `ahead` | 0.2 | blocks the yard is carried forward as it's bowsed off (more than about 0.3 drags the yard's after side into the mainmast) |
| `hoist` | its heel no lower than it hangs set | blocks it is hoisted while upright |
| `seconds` | 8 | how long a whole dip takes |

What the model export has to keep for the mirror: `yard-<code>` turns about its mast's axis (source `sheet`); every
rope anchor on a lateen has a mirrored partner on the hull; `masts[].radius` is the mast's widest radius where the yard
sweeps (the checks measure against it).

The caravel's values now (put in `caravel.json` by hand until `tools/caravel` exports them): both lateens
`{"mast": <its mast>, "standOff": 0.4, "ahead": 0.2, "seconds": 8}`. For the mast alone the main needs
standOff >= 0.3 + 0.165 + 0.05 - 0.35 = 0.17 (mast radius, the yard's radius at the sling, clearance, the yard centre's
distance from the axis) and the mizzen >= 0.05; the checks pass with 0.2 as with 0.4, and the shrouds are met the same
(0.04) either way. (An earlier note named "crosstree bars" at y 18.87 and 16.17 reaching 0.5 from the axis: those were
the shrouds' upper ends, reed-rope quads in the `exterior` group, not a part on the mast. There are no crosstrees.)

## Data: the lateens' shrouds

Each shroud of a dipping lateen's mast is one line in `rigCheck.standing` (x y z of either end, then its radius): its
head within the mast's radius + 0.5 of the mast's axis, its foot more than 0.2 off the mast's centre plane. The line
runs along the shroud's tube, through its middle.

The model should export each lateen mast's shrouds as two mesh groups in `shipmeshes/<ship>.json`, one a side:
`rig:shrouds-<mast>-port` and `rig:shrouds-<mast>-starboard` (`<mast>` its `masts[]` code: `rig:shrouds-main-port`,
`rig:shrouds-mizzen-starboard`, ...), and leave those quads out of `exterior`. Only the tubes go there; deadeyes,
lanyards, chainplates and channels stay in `exterior`. Every quad in these groups must lie along one shroud's line
(all four corners within its radius + 0.06): the runtime gives each quad to that shroud and turns each shroud on its
own when it's cast off. Quads along no line are dropped. Ratlines would need their own handling; the caravel has none.

Until the model exports them, the runtime finds the shrouds in `exterior` itself: rope-textured quads along a shroud's
line (`LateenShrouds.Split`). It collapses them to a point there, so `exterior` keeps its quad count and its paint and
planking ranges. For the caravel today that is 440 quads: 3 shrouds a side on the main (50 quads each) and 2 a side on
the mizzen (35 each).

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
