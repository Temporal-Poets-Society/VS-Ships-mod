# Caravel charter: bill of materials (proposal)

Status: proposal, 31 October 2026. For the release the caravel's charter (`ships:shipdeed-caravel`) becomes
craftable in the grid; a real building process comes later. The amounts below are measured from the current model
(`assets/ships/config/ships/caravel.json`, `assets/ships/config/shipmeshes/caravel.json`) and converted with
vanilla's own ratios. How they are fitted into a 3x3 grid is not decided yet.

## Vanilla ratios used

| Source | Ratio |
|---|---|
| Sawing a log (`recipes/grid/plank.json`) | 1 log = 12 boards (`plank-*`) |
| Planks block (`planks.json`) | 4 boards = 1 planks block |
| Support beams (`supportbeams.json`) | 1 debarked log = 3 support beams |
| Linen (`linen.json`) | 4 flax twine = 1 linen |
| Ships repair of a holed hull panel (`ShipDamage.cs:1807`) | 4 boards + 2 iron nails and strips per panel (about 1 m²) |
| Vanilla sailboat (`boat-construction.json`, about 10 x 3 m) | 298 boards, 45 support beams, 9 logs, 8 firewood, 27 rope, 21 linen, 2 plumb and square |

The vanilla sailboat comes to about 5 boards per m² of hull and deck, so the repair ratio of 4 boards per m² agrees with
it.

## What the caravel has (measured)

25.6 m long, 6.6 m beam, 2.75 m draft. Three decks (hold, main, quarterdeck).

| Part | Measure |
|---|---|
| Hull planking, outside (sides 331 m² + bottom 211 m²) | 542 m² |
| Deck planking (main deck 122 m², hold floor and undersides 144 m²) | 266 m² |
| Castles, bulwarks and partitions, one face | about 130 m² |
| Ribs and deck beams (cells in the grid) | 197 rib + 31 beam cells, 11 frame stations |
| Keel, stem and sternpost | about 30 m |
| Masts | main 23.8 m (Ø 0.6 m), mizzen 18 m (Ø 0.44 m) |
| Lateen yards | main about 30 m, mizzen about 19 m |
| Rudder, tiller, wheel | rudder 6.2 m tall |
| Sails | main 145 m², mizzen 98 m² (lateen) |
| Standing and running rigging (rope surface 53 m², about 4 cm rope) | about 210 m, plus boltropes about 110 m and anchor cable |
| Guns | 10 bronze guns on carriages (existing recipe: 24 bronze ingots, 2 rope, 2 nails and strips, 8 boards each) |
| Fittings | anchor (iron), pump, helm, 2 gates, hatch, door, 2 stairs, 25 partition panels, hearth, oven, 2 lanterns, 2 tables, a storage vessel |
| Copper sheathing | optional (`clad`), not part of a new ship |

## Bill of materials (rounded, before fitting to the grid)

| Material | Amount | From |
|---|---|---|
| Boards (`game:plank-*`) | about 3,700 (= about 310 logs sawn) | 930 m² of planking at 4 per m² |
| Support beams (`game:supportbeam-*`) | about 285 | ribs 197, beams 31, yards 49, rudder 6 |
| Debarked logs (`game:debarkedlog-*`) | about 50 | keel/stem/sternpost 30, masts 21 (one per 2 m) |
| Linen (`game:linen-*`) | about 245 | 243 m² of sail at 1 per m² |
| Rope (`game:rope`) | about 170 | rigging about 100, boltropes about 55, anchor cable about 16 |
| Bronze guns (`ships:gun-bronze`) | 10 | equal to 240 bronze ingots, 20 rope, 20 nails and strips, 80 boards |
| Iron nails and strips (`game:metalnailsandstrips-iron`) | about 400 | 1 per 2 m² of hull and deck planking |
| Iron ingots (`game:ingot-iron`) | about 10 | anchor and ironwork |
| Resin (`game:resin`) and flax fibres (`game:flaxfibers`) | about 70 each | caulking the outside hull, 1 per 8 m² (the same pair as a seam repair) |

Not counted: the furnishings that come with her (lanterns, tables, storage vessel, hearth, oven), which are vanilla
blocks; and the copper sheathing.

## Fitting it to a 3x3 grid (to decide)

One grid holds 9 stacks. The boards alone (about 3,700) exceed 9 full stacks, so the true amounts need either
intermediate items crafted first ("bundles": for example a hull timber kit, a spar kit, a sail, a rigging coil, with the
charter crafted from those), or the same proportions scaled down to fit one grid.
