# Caravel rig, stability and sailing study

Status: **study**, 18 October 2026, from `tools/caravel/rig_study.py` on the built model. Estimates for game design; densities and loads are assumptions, stated where used.

## Findings

1. **Stability is sound.** GM 0.66 m with the ballast low; under all sail, beam wind, she heels 10.3° at 12 m/s (latina) or 15.7° (redonda).
2. **The lateen sails: 188 m² (SA/D^⅔ 5.2).** The 2-block head room keeps each sail's foot 2.2 m over the deck (9.2 m up over the quarterdeck), so the lowest, widest part of a lateen sail is lost: about two thirds of a historical caravel's sail remains. The redonda's square sails keep theirs (325 m²). In game, sail force can be tuned apart from the drawn area.
3. **Balance.** The latina's sail centre is -18.2 % of LWL from the hull's (-3.78 m): weather helm, milder than the photo slings' (−20.9 %).
4. **Swinging is clear.** Up to abeam either side no sail comes within 2.2 m of a deck and none passes through the other mast; the main's clew stays above a player on the quarterdeck's stair.

## Hull

| | |
|---|---|
| Hull length (transom top to stem head) | 23.54 m |
| Waterline length × beam | 20.74 × 6.53 m |
| Draft | 2.750 m |
| Displacement | 218.8 m³, **224 t** in seawater |
| Block / waterplane coefficient | 0.59 / 0.80 |
| Centre of buoyancy | X -1.20, KB 1.68 m over the bottom border |
| BM (transverse) | 1.35 m |
| Tonnes per cm of immersion | 1.11 t |
| Lateral plane (underwater profile) | 52.8 m², centre X -1.44, Z 1.54 |
| Hull speed (1.34 √LWL ft) | 11.1 kn (5.7 m/s) |

## Weights and stability

| Item | Mass t | X m | Z m | Basis |
|---|---|---|---|---|
| Shell | 48.9 | -2.54 | 2.81 | oak 0.75 t/m3 |
| Backbone | 2.8 | -1.31 | 1.85 | oak 0.75 t/m3 |
| Ribs | 6.6 | -3.31 | 2.60 | oak 0.75 t/m3 |
| Beams | 3.5 | -3.67 | 4.44 | oak 0.75 t/m3 |
| Decks | 30.5 | -2.94 | 3.64 | oak 0.75 t/m3 |
| Railings | 1.1 | -9.11 | 7.60 | oak 0.75 t/m3 |
| Stairs | 0.2 | -3.57 | 3.97 | oak 0.75 t/m3 |
| Gates | 0.1 | -0.50 | 4.41 | oak 0.75 t/m3 |
| Fittings | 1.0 | -10.63 | 2.58 | oak 0.75 t/m3 |
| Rig_Latina | 4.7 | -2.79 | 13.00 | pine 0.50 t/m3 (masts, yards; sails ~0.6 kg/m2) |
| Guns | 5.4 | -5.17 | 6.34 | 6 light guns, ~0.65 t bronze + 0.25 t carriage each |
| Crew | 1.6 | -4.00 | 5.00 | 20 at 80 kg |
| Stores | 2.4 | -2.00 | 2.00 | 20 days: water and food, 6 kg a person a day |
| Ballast and cargo | 115.5 | -1.20 | 1.10 | whatever makes up the displacement; stone on the hold floor |
| **Total** | **224** | -2.07 | **KG 2.38** | |

**GM = KB + BM − KG = 1.68 + 1.35 − 2.38 = 0.66 m.** Trim: LCG -2.07 against LCB -1.20; the ballast's X is set to the LCB, so any trim comes from the rest.

The hull's timber weighs 95 t: the 0.2-block planking and timbers are two to three times a real caravel's (planks 6–8 cm), so she needs less ballast than a real one, and her weight sits higher. A lighter density for the planking would only change the ballast line.

## Rig: latina

| Sail | Area m² | Centre X | Centre Z |
|---|---|---|---|
| latina main | 112.1 | -1.41 | 14.72 |
| latina mizzen | 76.3 | -10.82 | 14.61 |
| **Total** | **188** | **CE -5.22** | **14.68** |

- Sail area / displacement^(2/3): **5.2** (traditional cargo ships ~10–15, fast sailing ships 15–20).
- Lead of the sail centre over the lateral plane's: -18.2 % of LWL (-3.78 m). Positive: forward, lee helm tendency; a small negative to small positive value balances a traditional hull.
- Heeling arm (sail centre over lateral plane centre): 13.13 m.
- Steady heel, wind on the beam, all sail set: 5 m/s → 1.8°, 8 m/s → 4.6°, 12 m/s → 10.3°, 16 m/s → 17.9°.

| Yard | Lowest head room over a deck, swung up to abeam | Other masts, swung up to abeam |
|---|---|---|
| latina main | 2.20 m (bearing -10°) | mizzen (8.0 m away, top 21.8 m): clear |
| latina mizzen | 2.20 m (bearing -90°) | main (8.0 m away, top 24.8 m): clear |

| Mast | X | Heel Z | Top Z | Length | % of hull |
|---|---|---|---|---|---|
| main | -0.5 | 1.00 | 24.80 | 23.80 m | 101 % |
| mizzen | -8.5 | 3.40 | 21.80 | 18.40 m | 78 % |

| Yard | Length | % of hull | Slung at Z | Tack (X, Z) | Peak (X, Z) |
|---|---|---|---|---|---|
| latina main | 28.4 m | 121 % | 18.02 | 8.1, 6.3 | -8.7, 29.2 |
| latina mizzen | 18.9 m | 80 % | 16.10 | -4.6, 9.3 | -14.0, 25.7 |

## Rig: redonda

| Sail | Area m² | Centre X | Centre Z |
|---|---|---|---|
| redonda fore course | 66.0 | 8.16 | 10.22 |
| redonda main course | 130.2 | 0.16 | 11.44 |
| redonda main topsail | 52.2 | 0.17 | 20.03 |
| redonda mizzen | 76.3 | -10.82 | 14.61 |
| **Total** | **325** | **CE -0.79** | **13.32** |

- Sail area / displacement^(2/3): **8.9** (traditional cargo ships ~10–15, fast sailing ships 15–20).
- Lead of the sail centre over the lateral plane's: +3.1 % of LWL (+0.64 m). Positive: forward, lee helm tendency; a small negative to small positive value balances a traditional hull.
- Heeling arm (sail centre over lateral plane centre): 11.78 m.
- Steady heel, wind on the beam, all sail set: 5 m/s → 2.8°, 8 m/s → 7.1°, 12 m/s → 15.7°, 16 m/s → 26.5°.

| Yard | Lowest head room over a deck, swung up to abeam | Other masts, swung up to abeam |
|---|---|---|
| redonda mizzen | 2.20 m (bearing -90°) | fore (16.0 m away, top 15.5 m): clear; main (8.0 m away, top 18.8 m): clear |

| Mast | X | Heel Z | Top Z | Length | % of hull |
|---|---|---|---|---|---|
| fore | 7.5 | 1.00 | 15.50 | 14.50 m | 62 % |
| main | -0.5 | 1.00 | 18.84 | 17.84 m | 76 % |
| mizzen | -8.5 | 3.40 | 21.80 | 18.40 m | 78 % |

| Yard | Length | % of hull | Slung at Z | Tack (X, Z) | Peak (X, Z) |
|---|---|---|---|---|---|
| redonda mizzen | 18.9 m | 80 % | 16.10 | -4.6, 9.3 | -14.0, 25.7 |

## Options for the latina rig

Each keeps the user's proportions (main mast 80 % of the hull, yard 105 %; mizzen mast 50 %), 2.2 m of head room over every deck in its normal position and swung up to abeam, and no sail through the other mast. Stair: the lowest margin of any sail over a player's head on the quarterdeck stair or its approach.

| Option | Main m² | Mizzen m² | Total m² | SA/D^⅔ | CE lead % LWL | Heel at 12 m/s | Head room | Masts | Stair margin |
|---|---|---|---|---|---|---|---|---|---|
| A (photo slings) | 79 | 48 | 127 | 3.5 | -20.9 | 5.6° | 2.20 m | clear | +2.19 m |
| B main clew aft | 81 | 48 | 129 | 3.6 | -25.1 | 6.1° | 2.20 m | clear | +0.44 m |
| C B + smaller mizzen | 81 | 36 | 118 | 3.2 | -21.1 | 5.4° | 2.20 m | clear | +0.44 m |
| D C + tack at stem head | 80 | 36 | 116 | 3.2 | -19.8 | 5.2° | 2.20 m | clear | +0.46 m |
| E D + yard slung a quarter down | 97 | 36 | 133 | 3.7 | -16.1 | 6.2° | 2.20 m | clear | +0.46 m |

- **B**: the main's clew goes aft over the quarterdeck's front at 9.8 m (2.8 over its deck, clear of the stair's top), still forward of the mizzen.
- **C**: the mizzen's yard 50 % of the hull instead of 65 %: less sail aft.
- **D**: the main's tack at the stem head instead of 1.1 m inside it.
- **E**: the main yard slung a quarter of the mast down instead of a third: the yard sits higher and steeper.

## Sweep: higher, steeper yards and a sloped foot (the user, 18 October)

Asked: a steeper yard, slung higher for more area, the main sail's foot sloping up aft, the main yard reaching
further to the bow. Every combination was checked against the same rules (2.2 m head room over the decks, normal
and swung up to abeam; no sail through the other mast; the quarterdeck stair clear). Main sail, 64 combinations,
24 pass; mizzen with the chosen main, 32, all pass.

- **Higher and steeper pays:** the main slung 1/8 of its mast down instead of 1/3 grows its sail 48 % (79 → 117 m²);
  its peak rises ~4.8 m over the masthead, as on the replica.
- **A sloped foot** (tack low at the stem head, clew higher aft) helps up to a clew at (−4.0, 7.0), over the waist
  above the stair's approach. Further aft over the quarterdeck the clew must be 9.4 high and loses area again.
- **Past the bow** (tack 1–2 m ahead of the stem, lower over the water) fails: swung toward abeam, the low tack
  comes in over the waist under 2.2 m. The stem head is as far as it goes.
- **A steep, high mizzen yard** (slung 20 % down, 72°, 60 % of the hull) keeps its sail over the quarterdeck
  instead of 2.5 m past the transom, for a little more area.

Chosen by the user next (18 October): main slung 1/8 down, tack at the stem head, clew (−4.0, 7.0); mizzen slung
20 % down at 72°, yard 60 %: 127 → 171 m², lead −20.9 → −11.7 % of LWL. **Superseded**: its mizzen yard crossed
the mast 1.4 m from its tack, a yard that does not work unbalanced on its mast (the user).

## The user's sketch (18 October), now built

Each yard balanced on its mast, crossing it near halfway; the mizzen's sail with a level foot over the
quarterdeck and its after edge over the stern; the main's foot sloping up aft. Taken from the sketch at the
model's scale (mm): main tack (8100, 6200), peak (−8700, 29200), clew (−3800, 9000), mast to 24.8 m; mizzen tack
(−4600, 9200), peak (−14000, 25700), clew (−14000, 9200), mast to 21.8 m. The masts are taller than the photos'
80 % and 50 % (about 101 % and 78 %): the mizzen's yard now crosses its mast at 16.1 m, above its old masthead.
The main's clew and the mizzen's tack lie 0.8 m apart; set on the same side they do not touch, but swung
differently they could.

## Reading it

- **Swing.** A lateen yard turns about its mast with the sail; the check turns each one up to abeam either side in 5° steps and samples its foot over the decks. Rigging ropes are left out: players pass through them.
- **Head room** is measured from the deck under each point of the sail's foot; the target is 2.2 m (2 blocks and a margin).
- **Heel** uses a flat-plate force (Cf 1.2) on all sail with the wind on the beam, and small-angle stability (GM). Real ships reefed or struck sail long before large angles.
- **Game speed.** The hull speed is the natural top speed in displacement mode; a game maximum near it, scaled by the carrack's, keeps the two consistent.
