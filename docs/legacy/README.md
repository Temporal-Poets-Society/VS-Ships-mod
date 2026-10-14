# Magelan — ships you can live on (Vintage Story 1.22)

Version 0.3.4: one ship, a **carrack**, and her **ship's boat**, a rowing boat (vanilla's sailboat made one, for
going ashore and up rivers). You can walk
the carrack's decks while she sails, go below deck, and climb the rigging to
the main top. Bring your own vanilla lanterns, chests and beds aboard and put them where the ship has room for them.
Its cabins are sheltered. The ship keeps sailing while you're below deck or asleep. She rides the Sea mod's waves:
she rises and falls with the swell, pitches and rolls, and leans in a side wind when her sails are set. Her yards are
braced round to the wind.

![carrack](docs/carrack-oblique.png)

The carrack is shaped after the Santa Maria and the Vila do Conde nau, about 1.25× a real 15th–16th century carrack so
the decks are livable:

- a round-bellied hull with a flat transom stern and a hanging rudder
- a tall sterncastle
- a pointed forecastle that overhangs the stem

It's 34.4 blocks long on deck (about 46 from the poop's overhang to the bowsprit's end), 10.5 wide and 4.5 deep in
the water, and the mainmast stands 32.5 above the waterline, a little aft of the middle of the keel like the wrecks of
the period. Her proportions follow the period's rules and wrecks (`docs/proportions.png`, `tools/shipgen/proportions.py`):
a flat floor a third of her beam and a full, round bilge (a hold 5 blocks wide at its floor), her draught 0.43 of her
beam, the quarterdeck from just abaft the mainmast so the main shrouds come down on its side, a long poop cabin carried
out over the transom on knees, a raked transom, and a forecastle a third of whose length overhangs the stem, its
underside sloping up from the stem. Her decks are 3 blocks apart, for players; the lower deck and the hold are below
the waterline, and dry. (The first carrack, up to 0.3.2, was 2 blocks shallower in the water, 9.2 wide and V-bottomed:
`carrack.py --variant=1` still builds her, to compare.)

The hull is oak: tarred outside, caulked decks, plain oak inside; the masts, yards and bowsprit are pine. The outside
and the inside planking both follow the hull's curves. Inside you see its skeleton: a stout frame every 1.5 blocks from
the cargo deck up to the rail, a deck beam on every frame with hanging knees, and the keelson down the middle of the
hold, rising to the stem and the sternpost at its ends; outside, the keel, the stem, the sternpost and a broad
hanging rudder. The bottom can be sheathed in copper. A gilded mermaid rides the stem under the forecastle, and her
name goes on a board across the transom.

| Level (floor height above water) | Where | What's there |
|---|---|---|
| 20.0 | main top | the only top, with a railing round it; climb the main shrouds to it |
| 8.0 | poop deck | a post for the stern lantern; a steep ship's stair up from the aft end of the quarterdeck |
| 5.0 | quarterdeck | **helm (wheel)**, turning with the rudder, and in front of it the **binnacle** (the compass: look at it for her heading); a balustrade across its front; on the poop cabin's front, facing the wheel, the place for a **metal plate** (your spawn point aboard) |
| 5.0 | poop cabin (under the poop deck) | long, carried out over the transom: 2 bed spots, chest spot, lantern hook, two side windows, a row of stern gallery windows |
| 5.0 | forecastle deck | the **heads** (two seats of ease near its tip, either side of the bowsprit, facing aft) and the bowsprit coming up through its partners to rest on the saddle of the stem head (right-click: the **figurehead**), lashed down by its gammoning; the stovepipe; the **stern lantern**'s bracket is on the poop's taffrail |
| 2.0 | main deck (waist, 15 long) | along the middle from aft: the mainmast; the bilge **pump** (its brake reaching aft) and right after it the glazed **waist hatch**; the **capstan** in front of the forecastle (port side, near the middle), the anchor cable coming to it from the port hawse through the forecastle bulkhead. Along each side five **bronze guns** on their carriages, firing through square ports in the bulwark (1.25 high) shut by **lids** hinged outside along their tops. The **boat gallows** overhead, level with the castle decks, for **two rowing boats**, its posts the side's frames carried on up over the rail; the **entry ports** with rope ladders down both sides, just aft of the gallows; stairs up the starboard side to both castles; small round lead-lined scuppers at the deck |
| 2.0 | great cabin (under the quarterdeck) | the **captain's cabin**: his bed, a chart table and stools, 2 chest spots, lantern hooks (a beam and the mizzen), the stern gallery (no side windows: too near the water); the wheel ropes run along its walls |
| 2.0 | forecastle (under the forecastle deck) | the ship's **firehearth** in the starboard after corner (a brick hearth with a sunken firebed and raised sides, a vanilla firepit in it that works as vanilla's: fuel, lighting, cooking, a pot's meals; a copper smoke hood and a stovepipe the smoke goes out of), the riding bitts and the hawse holes (through hawse pieces), the bowsprit's heel stepped against the foremast and held down by its bitts, bed spot, 2 chest spots, lantern hook on the foremast |
| −1.0 | lower deck | 13 bed spots, 5 chest spots, 5 lantern hooks, the **hold hatch** under the waist hatch (its ladder's head 1.9 forward of the companionway's foot), the cable tier, the tiller overhead at the aft end; below the waterline, dry |
| −3.5 | the hold | a cargo deck with the keelson down its middle, down a ladder at the after end of the hold hatch; dry below the waterline. **34 places for cargo** either side of the keelson (chests, storage vessels, crates, and trunks across pairs of them; none round the ladder's foot and the pump's pipe) |

That's 16 places for beds (and the captain's own bed), 10 for chests, 34 for cargo in the hold, 14 for lanterns and 2
for boats. The anchor hangs from the port cathead at the bow and goes down with it. Cutaway, deck plans and close views
(with markers where vanilla blocks go): `docs/carrack-cutaway.png`, `docs/carrack-plan-*.png`, `docs/carrack-waist.png`,
`docs/carrack-stern.png`, `docs/carrack-lowerdeck.png`, and perspective close-ups in `docs/views/`.

## Build and install

```bash
dotnet build -c Release
```

This needs only the .NET 8 SDK. It compiles against your game's .NET 10 runtime and `%APPDATA%\Vintagestory`
(override with `-p:VintageStoryPath=...` or the `VINTAGE_STORY` env var).

**Magelan needs the Sea mod** (wind, waves and the water shaders). Its source goes next to this folder
(`C:\VS mods\Sea`), and the same build compiles it. Copy both zips into `%APPDATA%\VintagestoryData\Mods`:

- `release/magelan-<version>.zip` (e.g. `release/magelan-0.3.3.zip`)
- `../Sea/release/sea-<version>.zip` (e.g. `sea-0.3.3.zip`; Magelan needs Sea 0.3.3 or newer: the water in a flooded
  hold)

The version comes from each mod's `modinfo.json`, and a build removes the older zips from `release/`. Take the old
`magelan*.zip` and `sea*.zip` out of your Mods folder when you copy the new ones in.

**Quit the game completely before you swap the zips**, and start it again. Leaving a world for the main menu isn't
enough: the game keeps the old `Magelan.dll` and `Sea.dll` loaded and then refuses the new ones ("Assembly with same
name is already loaded" in `client-main.log`). The world then loads without Magelan and Sea: no charter in the
inventory, and a ship in the chunks that load is dropped for good ("Failed loading an entity ... Will discard" in
`server-main.log`). Every build handed out for testing
gets the next patch number (0.3.0 → 0.3.1 → …), so the zip's name and the client log's mod list say which one the game
loaded.

## Play it (single player, creative is easiest)

1. Face open sea (the ship needs about 35 × 11 blocks and 5 blocks of depth) and either:
   - take **Charter of a carrack** from the creative inventory and right-click, or
   - type `/magelan spawn`.

   The ship appears side-on in front of you, anchored with its sails furled.
2. Get aboard:
   - from the water, only by the **rope ladders** at the entry ports amidships (both sides): swim into one and climb
     up. The **gate** at the top has to be open; right-click it from the ladder. Right-clicking the hull near a ladder
     puts you on it; anywhere else it tells you where the ladders are.
   - from a dock (or a boat alongside), jump onto the deck,
   - or type `/magelan board`.
3. Walk around with the normal keys (sprint, sneak and jump work). Sneaking stops you walking off edges. Jumping over
   the rail puts you over the side.
   - **Doors** are vanilla blocks; the **entry port gates** are hinged sections of the bulwark that swing inboard on
     hinges at their forward edge.
     Right-click to open and close them. A new ship has them all shut.
   - **Going below:** (since 0.3.8) the companionway is a stair in the great cabin: in through its windowed door,
     to port, and down aft along the port side to the lower deck (and up the same way); a railing round its opening.
     The great cabin's door is the way below: shut, it keeps the sea on deck out (open, it gets below there like through
     an open hatch); open, the lower deck isn't a closed room. With the **waist hatch** open you can also drop straight
     down to the lower deck.
   - **Up to the castles:** the stairs run up the starboard side, to the quarterdeck, the forecastle and the poop deck.
     (0.3.23: ship's stairs with their own handrails; craft more from planks, steep 2-long or 3-long, and put them in
     building mode.)
   - **The hold:** open the hold hatch in the lower deck and walk into it: a ladder goes down to the cargo deck. It's
     dry though it's below the waterline (no water, fog, splashing or breath bar). Lantern hooks hang under the lower
     deck. A shut hatch stops you at the top of the ladder. The hold hatch is under the waist hatch, forward of the
     mainmast; from the companion stair's foot walk forward along the lower deck to it.
   - **Climb the rigging:** walk outboard into the main shrouds and you climb them: W or Space up, S or Shift down,
     let go to hang on. At the top you step onto the main top. To come down, walk to the edge of the top above the
     shrouds and keep walking; you go down until you let go.
   - **Leave by the rope ladder:** open a gate, walk out through it and keep walking. You climb down and let go into
     the water.
4. **Right-click** things. A hint above the hotbar says what they do:
   - **Helm (the wheel):**
     - W / S: set more or less sail (furled → half → full; S below furled = sweeps, slowly astern)
     - A / D: rudder (it re-centres when you let go)
     - Space: drop or weigh anchor
     - Shift: leave the helm
     - The readout shows sails, speed (m/s and knots), heading and compass point, rudder, anchor, and whether you're
       aground or going astern. Its second line is the wind: how strong, where from, where it is relative to the ship
       (on the port bow, starboard beam…), your point of sail, and the speed your sails are good for on this heading.
   - **Sailing by the wind:** you steer and set sail; the crew trims the sails to the wind.
     - How fast she goes depends on where the wind is. She's fastest with it on the quarter (a broad reach, about
       5.3 m/s in a medium breeze), a bit slower running before it, slower still with it on the beam, and slowest
       close-hauled.
     - She can't point closer than about 62° to the wind. Head to wind she's **in irons**: the sails won't draw, she
       loses her way, and with the wind right ahead she's taken aback and pushed astern.
     - To get upwind, **tack**: sail close-hauled, then hold the helm over and turn through the wind. The crew backs
       the head sails to push her bow round; in a medium breeze a tack takes about 45 s. Or turn the other way, away
       from the wind (wearing ship).
     - More wind, more speed, up to what the hull allows (7 m/s). In a gale half sail is nearly as fast as full sail
       and heels her much less.
   - **Capstan** (in front of the forecastle, port side; its cable comes straight aft from the port hawse, round the
     riding bitts and through the forecastle bulkhead onto its barrel): drop or weigh the anchor. The anchor at the
     port bow goes down into the water and comes back up to the cathead.
   - **The binnacle** in front of the wheel: look at it for her heading (degrees and the compass point).
   - **The glazed waist hatch** (the cargo hatch): right-click to open or shut it. Shut, it lets light down and the
     lower deck is a closed room; open, the weather comes in there.
   - **The hold hatch** in the lower deck floor, right under it: right-click to open and shut it. (On a ship from an
     older version the hold was one big storage: while that still has something in it, the hatch opens it too.)
   - **The ship's boats:** two places on the gallows over the waist. Hold a **rowing boat** (or a vanilla **raft**)
     and right-click the gallows to put it up (oars shipped along her sides); right-click it to take it down again. (A
     sailed boat is 8 blocks long with an 11-block mast: it doesn't fit.)
   - **The rowing boat** ("Rowing boat" in the handbook, in every wood): vanilla's sailboat made a rowing boat, for
     going ashore and up rivers where the carrack can't go. No mast, rudder or stern seats; a block shorter (7.5 long,
     2.8 wide); a thwart across the middle with a pair of oars on thole pins. At rest the oars lie out over the water;
     rowing, they sweep.
     - **Put her in:** hold her and right-click the water; she lies there with her bow the way you look (not inside a
       ship: looking down from a deck, aim at open water beside the hull).
     - **Get in:** right-click the thwart, or step, jump or swim into her: you sit on the thwart (or on a boat seat put
       in one of her places, when the thwart is taken). There's nothing to stand on in a vanilla boat.
     - **Row:** W/S row ahead and astern, A/D pull round. Shift gets up.
     - **Places** as in vanilla's sailboat: chests, baskets, storage vessels, crates, boat seats (for passengers, and
       animals on a rope tie post), the anchor at the bow, the figurehead, the plaques and the shields; six places for
       cargo, two forward of the rower and four aft.
     - **Take her up:** Shift + right-click her with nobody and nothing in her (from the water or a dock); from a ship's
       deck or rope ladder Shift or Ctrl + right-click with an empty hand. (Ctrl + right-click with a chest or vessel
       in hand puts it in her, as vanilla.)
     - She doesn't pass into a ship's hull: she stops against it like against a quay (and a ship coming onto her
       moves her out).
     - Crafted from 10 planks of one wood and a pair of vanilla oars:

     ```
     P _ P
     P P P
     O _ O
     ```
     (each P is 2 planks, O a crude oar)
   - (0.3.2's ship's boat, the launch you could stand and walk in, is parked: a boat of it in a saved world still
     works, but it can't be made any more.)
   - **The figurehead:** right-click the stem head at the forecastle's tip to carve the next one (a gilded mermaid, or
     none; more to come).
   - **Her name:** right-click the name board across the top of the transom (from the poop deck, leaning over the
     taffrail, or from a boat astern) and write it; it's painted there in gilt letters.
   - **The captain's bed** in the great cabin: sleep in it like a bed you put aboard.
   - **The firehearth** in the forecastle (config `Hearth`; since 0.3.6, not yet seen in the client): vanilla's firepit,
     working as it does ashore, with vanilla's numbers (so food cooks aboard as it does on land). The crosshair on its
     bricks or its firebed picks it (since 0.3.7: before, the bricks hid it and nothing on it could be clicked).
     - **Right-click** opens it: vanilla's firepit window (fuel, input, output, the flame that burns down, the fire's and
       the input's temperatures, the arrow that fills as it cooks; with a clay pot in the input its four cooking slots and
       what it will make). **Shift + right-click** puts one fuel or one thing to cook in from your hand; right-click with a
       clay pot puts it on the fire, with a bowl serves from a cooked pot in it.
     - **Lighting it:** with fuel in it (firewood, sticks, charcoal…), hold a firestarter or a lit torch to it with held
       right-click: after 3 seconds a torch lights it; a firestarter catches one try in four (as vanilla's) and goes on
       trying while you hold it. It burns its fuel (firewood 24 s, four times slower once it's hot with nothing to cook)
       and goes out when it's gone; its embers relight new fuel for 2 game hours (4 minutes), then it's cold.
     - **Cooking:** what's in the input heats toward the fire's temperature and cooks or smelts as on a vanilla firepit
       (raw red meat on firewood: about 43 s from lighting); a clay pot with ingredients makes vanilla's meals (porridge,
       stews, soups…) into the output.
     - **Looks:** the firepit lit, out (ashes) or cold with its logs, a pot (its lid rattling as it boils), a meal or a joint
       on the spit drawn on it; vanilla's flames, embers and smoke at the fire, moving with her; smoke out of the
       stovepipe's mouth on the forecastle deck, drifting off with Sea's wind; its flickering light; vanilla's crackle.
     - **Warmth:** while it burns, whoever is in the forecastle is warmed by it as by a vanilla firepit (the nearer, the
       more), and anyone within 3 blocks of it elsewhere (on the deck above, beside the bulkhead).
     - Going down, the sea puts it out. What's in it goes into her wreck.
   - **Put your own things aboard.** Hold a vanilla lantern, chest or bed and look around the ship. The spots where one
     fits show a hint; right-click to put it there.
     - **Lanterns** hang from hooks under the beams and on the masts, and one stands on the stern lantern's iron bracket
       out over the taffrail (its place says "Stern lantern" in the crosshair even empty). They light the
       ship in their own colour. Right-click to take one down.
     - **Chests** work like their vanilla size. Right-click to open; Shift + right-click to take an empty one back.
     - **The hold's cargo:** a chest, a storage vessel or a crate goes on one place, a trunk (a double chest) across
       two that are side by side. They open and come back like chests.
     - **Beds**: right-click to sleep, if you're tired enough, like a vanilla bed. Everyone asleep speeds up time as
       usual. Shift gets up. Shift + right-click takes the bed back.
   - **Copper sheathing** (an expensive upgrade): from the water or a dock, right-click the hull with copper plates in
     your hand. 48 plates sheathe the bottom in copper, which makes her 8% faster.
   - **Your spawn point aboard** (config `ShipSpawn`): hold any vanilla metal plate on the quarterdeck and right-click
     the place on the poop cabin's front, port of its door (facing the wheel), to put it up. Right-click the plate
     with an empty hand to bind your spawn to her; again to unbind it. Chat says which. When you die you come back
     standing on the quarterdeck in front of the plate, aboard, wherever she has sailed. Shift + right-click takes the
     plate down, and then every spawn bound to her ends; so does her being removed (later: sunk). Then you get the
     spawn you had before back (a temporal gear's, or the world's), at once, or when you next join. Only her crew can
     bind (below); taken off her crew list, your spawn goes back too.
   - **Her crew** (0.3.11): a bronze plaque on the great cabin's front, starboard of its door. Right-click it to read
     her crew list: each name a **captain** or **crew**. Whoever launches her from a deed is her first captain (an
     older ship, or one from a command, has no list until someone signs it: the first to sign is her captain).
     Captains write names on (a player online or one the server has seen), take names off, and make others captains
     or crew; anyone on it can take their own name off; her last captain can't leave while others are on it. Her crew
     has the run of her. Anyone else may come aboard and walk about while one of her crew is online, and open doors,
     gates and hatches and use the heads, and nothing else (no helm, sails, anchor, guns, chests, hearth, oven, paint,
     repairs, pieces or spawn plate). Server admins count as captains.
   - **Laid up** (multiplayer, config `LayUpWhenCrewOffline`): while none of her crew is online she lies at anchor
     with her sails furled. She still rocks in the waves and swings to the wind, and she's solid, so nobody sails
     through her or launches a ship where she lies; but she takes no water, no damage and no wear, and she's closed to
     everyone else (anyone still aboard is put off). When one of her crew comes online she's back in commission.
   - **Logging out aboard** (config `RejoinShipOnLogin`): log out aboard her (on deck, at the helm, in a bunk) and you
     come back aboard her when you log in, wherever her crew has sailed her meanwhile. If you weren't aboard when she
     sailed, you stay where you were. If she's gone meanwhile (sunk, removed), you come back at your spawn.
   - **The watch** (config `ShipWatch`): leave the helm with sail set and it's lashed: she holds her heading (nothing
     changes her course while you sleep). While anyone sleeps in a bunk aboard, the ship's bell turns them out, and
     all hands are told why, for land or a shoal ahead (her next `WatchAheadSeconds` of way), water rising in her,
     running aground, being hit (a gun, a ram, the ground), the wind shifting (taken aback, in irons), the sea getting
     up, or another mod's alarm (the Sea Monsters mod: a Kraken starting to circle her). Each wakes them when it
     begins, not again while it lasts. Sleeping only speeds up the clock, as vanilla's beds do: she sails on at her
     own speed meanwhile.
5. **Look at her:** the box at the top of the screen (vanilla's block info) says what the crosshair is on: her name and
   what she is, and the part: "Hull, port side, forward, below the waterline (frames 17–18)", "Main deck",
   "Underside of the quarterdeck, in the great cabin", "Mainmast", "Woodwork (the great cabin)", or a fixture or
   what's put there ("Helm", "Copper plate"). Her hull and decks hide the blocks behind them from the crosshair, so
   nothing is placed or broken through her. Left-clicking her does nothing.
6. **The pin rails** (0.3.13, note 131; by the main shrouds, inside the bulwark): a second place for her crew to work
   the sails: right-click sets more sail (furled → half → full), Shift + right-click takes sail in (full → half →
   furled); the line over it says what she has set. As W/S at the helm: her crew only, not while she's laid up or her
   mast is lowered. (A fixture `pinrail` with a `sel` box over each rail; clicking the bulwark away from a gun's port no
   longer lays that gun by its breech.)
7. Let go of the helm with the sails set: the ship holds its course with the rudder centred. When nobody is aboard
   (everyone left or logged out), it drops anchor and furls its sails after 5 seconds.
8. **In a seaway** she rides the waves. To try it, raise a storm: `/sea setwind W 1 0 built` (Sea's command; see its
   README), and `/sea clearwind` to calm it again.
   - She **rises and falls** with the swell. Short chop hardly moves her; the long swell of a storm does.
   - She **pitches** in head and following seas and **rolls** with waves on the beam, most when they come about as
     often as she rolls by herself (every 7 or 8 seconds).
   - Under sail in a **side wind** she **heels** to leeward: under full sail on a beam reach about 3° in a moderate
     breeze (force 5), 5° in a strong breeze (6), 7° in a near gale (7), 10° in a gale (8), 15° in a storm (10), up to
     20°. More sail in more wind, more heel. Close-hauled she heels most (the sails feel her own way as extra wind);
     running before the wind she stays upright.
   - Her **sails steady her**: under sail she rolls about half as much as lying ahull (in a storm on the beam about
     5° against 14°).
   - `dotnet run --project tests/MagelanTests -c Release -- motion` prints the whole table (every Beaufort force: the
     sea, her speed and heel, the roll and pitch from the waves at rest and under way, how near the water comes to her
     lee deck edge and her stern windows).
   - Your view tips with the deck. If that makes you queasy, type `.magelan` (with a dot): a small window has a
     **Sea motion** slider (how much ships move on your screen) and a **Steady horizon** switch (your view stays
     level). Also `.magelan seamotion 0.5` and `.magelan steadyhorizon on`.
9. **Damage, leaks and sinking** (config `Damage`; since 0.3.5, not yet seen in the client; the damage isn't drawn yet,
   it's read in the look-at box):
   - **Running aground** hurts her where she touches, by how fast that part of her was going: about 1 m/s scrapes the
     planking, 2.5 cracks it, 4 holes it. Driven on with her sails set she keeps bumping and grinding; lying on the
     ground in a seaway she grinds on it. **Running into another ship** hurts both.
   - **Look at a part** of her: the look-at box says its state (sound, worn seams, scraped, cracked, leaking, holed;
     for a sail torn or shredded, a mast sprung or broken, rigging frayed or parted, the rudder damaged or wrecked),
     roughly how many litres a second it lets in, and what mends it. At a mast: its sails and rigging too; at the helm:
     the rudder; at the pump: the water in her.
   - **Leaks:** cracked, leaking and holed panels below the water let the sea in, more the deeper they are (and when
     she rolls or heels a panel near the waterline dips under); worn seams a trickle. Her hold is one space (carracks
     had no watertight bulkheads). At 2 blocks deep a cracked panel lets in about 1 litre a second, leaking seams 3, a
     hole 110 and more.
   - **The water in her** floats her deeper, runs to the low side (she lists toward the side she heels or the side it
     came in, and rolls slower), and makes her slower and less handy. The helm's readout says how much there is
     ("Water in the hold: 12 t, rising") and warns when she's **foundering**: the sea coming in over her side (her
     deck's edge, the rail, an open hatch or gate under the waves), or her reserve buoyancy nearly gone.
   - **Pumping:** the bilge pump on the middle of the waist at the glazed hatch's forward end. Hold right-click at it:
     a stroke a second, 80 litres a stroke (config `Pumps` scales it); two can work its brake at once. One holds a few
     leaking or cracked panels, two a fresh hole below the waterline. While it brings water up, the water gushes out of
     its spout onto the deck in time with the strokes, more with more at the brake, with a pouring sound; a dry hold, it
     only clanks.
   - **Mending:** hold right-click for 4 seconds at the part with the items in your hand (they're taken from your hand
     first, then your hotbar and backpack; creative takes nothing):
     - planking (hull, decks): 1 plank for a scrape, 2 planks and 1 nails and strips for a crack, 4 and 2 for a hole
     - seams: flax fibres and resin (1 and 1 worn, 2 and 1 leaking)
     - a sail (looking at it, or at its mast): torn, 1 sail (vanilla's windmill sail or a boat's large sail) or 4 cloth
       or linen of any colours; shredded, 2 sails or 1 sail and 4 cloth
     - rigging (at its mast): 2 rope frayed, 4 parted
     - a sprung mast: fish it with 4 planks and 2 rope; a broken one needs a port (to come)
     - the rudder (at the helm): 2 planks and 1 nails and strips, 4 and 2 when wrecked
     - below the water: from inside her, or dive to it.
   - **Wear:** her seams age at sea (about 60 days to wear out, more in rough seas), so caulking is a periodic job; an
     unsheathed bottom fouls and slows her (copper sheathing keeps it clean); set sails chafe slowly (a few % in many days).
     In a gale with too much sail set (full sail from force 8, reefed from force 11) or taken aback hard they **strain**
     (0.3.12): the wind panel warns in colour ("Gale: shorten sail!", "Storm: furl…"), everyone aboard is told once (a
     straining spell: a gusty wind at the threshold isn't said again until the strain has been off a minute, 0.3.13), the
     watch wakes sleepers; only after minutes of it (2½ real minutes at full sail in force 10–12, 12 in force 8; less sail
     or wind, longer) do they start to tear, and a strain eases off again once sail is shortened.
   - **Sinking:** when her reserve buoyancy is gone she settles till her deck is at the water, and goes down: everyone
     aboard is put off into the water, the boats on her gallows float free, the spawns bound to her end. She settles on
     the bottom, listing, and becomes her wreck (see 10; with `Wrecks` off she lies there as she is, and a diver can still
     open her chests and cargo). In shallow water she may come to rest on the bottom before her deck is under: then
     she's foundered, not sunk, and pumped out and mended she floats off.
10. **Guns** (config `Cannons`; since 0.3.5, not yet seen in the client): bronze guns along each side of the waist (five
   a side since 0.3.8), cast muzzle-loaders of about 1500 like the Mary Rose's demi-culverins, on truck carriages, firing
   through square ports in the bulwark with lids.
   - **Housed or run out** (since 0.3.8): a new ship's guns are housed: run in, their ports' lids shut, and they don't
     fire ("Open the port first"). Hold right mouse on one to lay it: its crew first opens the lid and runs it out (two
     seconds; the HUD says so), then lays it. **Shift + right-click** a gun runs it in and shuts its port. A shut lid
     keeps the sea out of the port; an open one lets it in over the sill as the old open ports did.
   - **Lay one** (since 0.3.6): stand by it (within about 2.5 blocks of its breech with nothing else in the crosshair, or
     with it in the crosshair) and hold **right mouse** (since 0.3.8; it was left): its crew lays it toward where you look, at their pace (10° a second trained, 6° elevated), as far as it
     goes: trained up to 12° toward the bow or the stern (less toward a gun close beside it or an entry port's gate
     standing open: the HUD says when you look further than it goes), elevated from 5° down to 15° up.
     Let go and it stays laid. The HUD shows how it's laid ("trained 5° to the bow, elevation 3°") and whether it's
     ready, and so does the look-at box on a gun. Ctrl + right-click a gun to stand to it at the gun captain's place
     behind its breech (the same keys there; Shift leaves it).
   - **Fire one:** **left-click** (since 0.3.8; it was right): the gun you're laying (wherever you look), the one in the
     crosshair, or the one you laid a moment ago and are still by. The click is the gun's: no swing at her, nothing
     broken. It fires along its lay as she lies: when she rolls toward the enemy it fires lower,
     away from him higher. With a boom heard far off, a flash and a thick cloud of smoke that drifts off with Sea's wind,
     it runs back a block on its trucks along the way it's laid (its breeching goes taut) and is run out again over a few
     seconds. It's ready again after 8 seconds. (Loading it — powder, shot, ramming, running out — comes later.)
   - **The shot:** a 9-pound iron ball, seen in flight (60 blocks a second). Fired level from her waist it meets the
     sea about 42 blocks out; raised 10° it goes 130, 20° 220. Into a ship's side close by it holes the plank it strikes
     and goes on across her, tumbling and throwing splinters (it keeps three quarters of what's left after breaking
     through), so her far side is usually cracked and leaking rather than holed; a second ball there, or one in through a
     hole already made, holes it (and it hurts whoever stands in its way inside her). A hit
     below her waterline lets the sea in like any hole: ten of them sink a carrack in a few minutes unless her crew
     pumps and mends (see 8). It kills a creature or a player it strikes (players only where PvP is on) and goes on.
     Into the sea (since 0.3.7 as a real ball, not as in the air): a splash, the blow on the water takes two thirds of
     its energy and the water's drag three fifths of what's left each block, so it's spent within about two blocks: a
     shot that falls short does nothing, one that strikes her right at her waterline holes her as in the air, one through
     half a block or a block of water under it cracks a plank and starts its seams. Into a block: it stops there in a puff of dust (with
     `CannonsBreakBlocks` on it breaks up to three soft blocks, wood or earth but no stone, not in a land claim you
     can't build in).
   - **Commands** (creative or admins): `/magelan fire [gun|port|starboard|all] [elevation] [train]` fires the guns of the
     ship you're on (or the nearest), laid first, at once, as far as each goes, if an elevation or a train is given (`-5`
     lays them 5° down, `2 -8` 2° up and 8° toward the stern, `- 6` only trains them 6° toward the bow); `/magelan target [distance] [port|starboard]` puts a carrack on
     the water abeam of her as a target (60 blocks off by default, where her level shot meets the target's waterline).
   - A sunk ship's guns lie in or beside her wreck as blocks, `magelan:gun-bronze-<side>` (the same gun; break one to
     take it home; placeable, no use yet).
11. **Wrecks** (config `Wrecks`; since 0.3.5, not yet seen in the client): a ship that sinks to the bottom becomes her
    wreck, a second or so after she gets there.
    - **Her hull as world blocks:** wreck blocks of 4 × 4 × 4 small cells (her own collision grid's), laid as she lies,
      heeled and down by the head: tarred oak outside (verdigris copper below her waterline if she was sheathed),
      weathered oak inside and on her decks, pine for the stumps of her masts. She's broken up: holes where she was
      holed (and a few where she was cracked or struck the bottom), planks gone here and there, her upper works ragged,
      one castle fallen in. Her masts lie beside her on the seabed as two or three long timbers each, fallen to the side
      she lies over to; her anchor lies by her bow. She's settled into the seabed (nothing of her in the ground; rock
      stays rock), plants give way, and the water stays (also in her blocks' empty cells). A little sand or gravel is
      heaped against her where the seabed is sand or gravel.
    - **Treasure:** her chests and storage vessels go down as themselves, with what's in them, where they stood in her
      hold or cabins (if that place is still there), else on the seabed beside her; a trunk's or crate's contents go into
      an old chest (the trunk or crate with the loose things); what she carried loose (the hold's big storage, lanterns,
      beds, the plate, copper plates for an unfinished sheathing) goes into old chests on the seabed beside her middle.
      Nothing is lost: what finds no place is dropped there.
    - **Her guns** lie on her deck where they stood if there's room, else they've tumbled onto the seabed beside her
      ports: the bronze gun blocks, recovered whole.
    - **Breaking a wreck block** gives part of its raw materials, never the block (and it can't be chiselled):
      - weathered oak: 3 ± 1 oak planks a full block (64 cells), 15% a firewood, 20% 1–2 sticks
      - tarred oak: 2.5 ± 1 oak planks, 10% a firewood, 15% a stick, 12% a resin, 8% iron nails and strips
      - pine (masts): 3 ± 1 pine planks, 15% a firewood, 20% 1–2 sticks
      - copper sheathing: 0.35 ± 0.15 copper plates and 2 ± 1 copper bits a full block (her 48 plates come back as
        about 25 plates and 150 bits)
      - iron (the anchor): 2 ± 1 iron nails and strips and 5 ± 2 iron bits
      - a block with fewer cells gives that share of it (a fraction is the chance of one more). An axe breaks the wood
        quickest; a block that's mostly copper or iron is metal to the tools. The whole carrack gives back about 1100
        oak planks, 70 pine planks, 110 sticks, 60 firewood, 25 copper plates and 150 bits.
    - A wreck never happens twice to one ship (the wrecks made are saved with the world), waits for its chunks to be
      loaded, and anyone inside its cells is moved up out of them.
12. **Reporting what you find: the surveyor's chalk** (since 0.3.6, not yet seen in the client). Take **Surveyor's
    chalk** from the creative inventory (it looks like vanilla's lump of chalk; no recipe). With it in hand:
    - **Right-click** what you want to report: a part of a ship (anything the look-at box names), a block, a creature,
      or nothing (then it's where you stand). A window shows what was captured at the click; write your comment (Enter
      makes a new line, long lines wrap, the box scrolls to keep the caret in sight and with the mouse wheel; Ctrl + Enter
      or **Save the note** saves it, Cancel drops it). A moment after the window closes a
      screenshot is taken with the game's own F12 code (no window in it), and its file name goes into the note.
    - **The chalk marks:** while you hold the chalk, each note's spot shows a small white X with its number. A mark on a
      ship stays on her at that spot, moving and tilting with her (shown while she's loaded); a mark on a block or a
      creature stays where it was in the world.
    - **Left-click a mark:** its note in chat. **Shift + left-click:** the note is rubbed out (chat says so). The line
      above the hotbar says which mark is in the crosshair, or what a right-click would write about.
    - **The files**, on your own disk, in `VintagestoryData/ModData/` (`%APPDATA%\VintagestoryData\ModData` on Windows):
      `magelan-notes.json` (every note with every number, a JSON array) and `magelan-notes.md` (a readable list: the
      number, time, target, your comment, the screenshot and the key numbers). The screenshots are the game's own, in
      `Pictures\Vintagestory`.
    - **What a note holds:** Magelan's, Sea's and the game's versions, the real time and the game's calendar; the
      target: on a ship her design, name and number, the part as the look-at box says it (and its index), where the
      crosshair met her in her own frame (the design's coordinates: x toward the bow, y up from her waterline, z to
      starboard) and in the world, the face and its normal, the fixture there, the part's damage and the whole look-at
      box; a block's code, position, face and hit point; a creature's code, number and position; you: where you stood
      and looked (in the world, and in her frame with your seat, deck and room when aboard); the ship you're on and the
      one you look at: speed, sails, anchor, the wind and the waves there (Sea), her drawn heave, pitch, roll and heel
      (and as she really lies), the water in her hold, list, trim, sinkage, stage and what of her is hurt.
    - `.magelan notes` lists them (number, time, target, first words); `.magelan notes show <n>`, `.magelan notes
      delete <n>`, `.magelan notes clear` (asks for `.magelan notes clear yes`). Numbers go on from the highest kept.
    - **How Claude uses them:** tell Claude "see my notes"; it reads `magelan-notes.md` (and the JSON for the numbers),
      opens the screenshots, and `python tools/shipgen/view.py notes` draws each note's spot on the carrack, marked and
      numbered, from where you stood (`docs/views/notes/note-<n>.png`, and `overview.png` with all of them);
      `python tools/shipgen/texview.py notes [n...]` draws them with the game's textures, lit by their normals as the
      game lights them (`tex-note-<n>.png`).

**Decorating her** (since 0.3.8; anyone aboard may, there are no access rights yet). Any vanilla block can go aboard:
- **Put a block:** hold it and look at a free place: a deck's floor (every deck, the cabins, the forecastle, the lower
  deck, the hold, the castle decks and the waist), a spot on a room's wall (rows 0.75 and 1.75 over the floor; it hangs
  flush, tilted as her side leans), or a hook (the lantern hooks, the stern lantern's crane). Its outline shows as a
  wireframe box, as vanilla outlines a block: for a block that takes two places (a bed, a trunk, a 2 × 1 painting) the
  whole footprint, for the way it would face. **Right-click** puts it there, facing you (snapped to her four ways): a
  block with its own orientation takes it as vanilla would (a painting or a wall lantern toward the wall, a lantern on a
  hook hanging, on a floor standing), anything else is turned to face you. In survival it's taken from your hand.
  Doorways and a block each side of a door, ladders, stairs, hatches, the companionway, the guns' room to train and run
  back, the capstan's bars, the gates' swing, the pump, the helm's standing spot and the masts are kept clear; a place
  a piece takes is taken (the hint says why a place won't do).
- **What it does:** a chest, trunk, labeled chest, crate, reed chest or storage vessel is a storage of vanilla's size
  (right-click opens it; saved with her; sunk, it goes into her wreck with what's in it); a bed is slept in as her bunks
  are; a chair, stool or bench is sat on (right-click; Shift gets up); a lantern, lamp, candle or torch holder gives its
  light; anything else is drawn. Pieces are solid (their own collision boxes) for walking aboard.
- **Take it up:** look at it and **hold left mouse** (0.6 s; its hit sound and a puff of it as it goes): its item goes
  back into your inventory. Not a container with something in it, nor with your inventory full (nothing is ever dropped
  into the sea). Looking at a piece shows its selection box and what a click does.
- **Inspecting:** with the **surveyor's chalk** in hand the free place, wall spot or hook under the crosshair is
  outlined too, and the chalk's line above the hotbar adds its kind, room and id (`f:lower:3:-2`, `w:greatcabin:s:-9:0`,
  `h:lantern-stern`), to note placement ideas with; the chalk's clicks stay its own. With an empty hand nothing is outlined.
- **Paint:** her paintable parts are zones (the topsides, the wales, the rail caps, the great cabin's walls, the lower
  deck's inside... the design names them). Hold vanilla **dye** in a bucket, jug or bowl and right-click a surface: its
  whole zone takes that colour (the 11 vanilla dyes), using up **0.02 L of dye a square metre** (the hint says the zone,
  its area and the cost, and what you have; creative is free). **Gold plates** (`game:metalplate-gold`) gild a moulding
  or carving, **one plate per 10 m²** (at least one). A **knife** scrapes a zone back to how she was built. Painted wood
  keeps its grain; the deck's caulked seams stay dark under it; gilt is vanilla's gold plate.
- A new ship starts with lanterns on her hooks (when the design names them). An older ship's lanterns go onto the
  hooks as pieces; what stood on a chest, cargo or bed place her design no longer has becomes a piece where it stood
  (a container with what was in it); what can't be placed goes into the hold as before.
- Commands (creative or admins; the ship you're on, else the nearest): `/magelan pieces [list|clear]`,
  `/magelan place <block> <mount id> [bow|starboard|stern|port]` (no item needed), `/magelan paint <zone|all>
  <colour|gold|none>`, `/magelan zones` (each zone's area, cost and paint), `/magelan mounts [ids [filter]]` (the places
  in each room and deck, free and taken).

`.magelan blocks` (client) and `/magelan blocks` (server, since 0.3.16): working blocks aboard (config `WorkingBlocks`)
for the ship in the crosshair (or the one you're on, or the nearest): her record (sub-dimension, each working block's
piece, cell and block), whether this game has her dimension and its chunks and what block it sees at each cell, whether
the piece is still drawn as a piece; the server's side adds her chunks and which players they were sent to and what
each player's client says it has. Clients tell the server how many of a ship's working blocks they have; her chunks are
sent again every few seconds until they have all. The log lines start `[magelan] working blocks:`.

`.magelan damagecheck` (client, since 0.3.7): what drawing the damage has for the ship in the crosshair (or the nearest):
her mesh file and her hull's planking sorted into panels (its chunks), whether drawing damage has failed this session,
her damage state against her parts, how many panels look other than sound (and holed), the chunks built and still to
build, her sails as drawn. If damage you know she has isn't drawn, this says where it stops.

Other commands: `/magelan info` (nearest ship's state, and her water and damage) and `/magelan remove` (the cargo is
dropped); `/magelan wreck` makes the nearest sunk ship's wreck now, `/magelan wreck here [seed]` lays a made-up
carrack's wreck (holed, heeled, no cargo) on the seabed 24 blocks ahead of you (creative or admins). For testing damage
(creative or admins; the ship you're on, else the nearest):
- `/magelan damage look [energy]`: a shot (120 kJ by default) where you look (at a mast, the helm: that part, half wrecked)
- `/magelan damage part <name> [0..1]`: hurt a part by name: `hull-p-12-2` / `hull-s-12-2` (side, frame space from the
  stern, band from the keel), `deck-main-10`, `rudder`, `mast-main`, `sail-main-course`, `rigging-main`
- `/magelan damage at <x> <y> <z> [energy] [radius]`: a blow at a ship-local point (a radius of 0.5 or less is a shot)
- `/magelan damage age <days>`: her seams and bottom that many days older; `/magelan damage list`: what's hurt
- `/magelan repairall`: every part mended, pumped dry, a clean bottom; `/magelan flood <tonnes>`: water into her hold

### What to look at when you test

- **Visuals:**
  - hull, sails and rigging
  - tar, caulking and the inside planking. The client log says what the texture atlas really holds: look for
    `[magelan] Hull textures in the atlas`. Tarred oak should come out near black, caulked oak a little darker than
    plain oak.
  - nothing poking through the hull anywhere: deck edges at the waterline and at the bow, the castle sides, the
    entry ports, the frames at the ends
  - vanilla doors in their frames; the side windows' own glass; the entry port gates swinging in; the hatches' leaves
  - lanterns, chests and beds where you put them; the rowing boats on the gallows (oars shipped along the sides)
  - the rowing boat in the water: the oars lying out at rest and stroking while you row, which way you face on the
    thwart, no waves inside her, stopping against a ship's side, picking her up from the water, a deck and a rope
    ladder, chests, vessels and boat seats in her places
  - how she sits in the water, the quarterdeck at the mainmast with the shrouds on its side, the overhanging poop
    cabin, the forecastle's raked underside, room in the waist and the hold; no water or bubbles on deck in a storm
  - the wood's grain along the frames, beams, masts and posts (it ran across them before)
  - the captain's bed, table and stools, and the firepit standing in the forecastle's hearth (vanilla meshes)
  - the stern gallery's glass (plain, stained in the top panes)
  - interior light: cabins darker, lit by your lanterns at night
  - sails furling and unfurling; the yards braced round with the wind, the sails under them, braces and sheets
    following; the wheel, rudder and tiller turning, the tiller ropes following the tiller
  - the anchor going down and coming up, its cable following
- **Walking while moving:** smooth or jittery? Stairs, the companionway ladder, doors, the ship turning under you.
- **Riding the waves** (raise a storm with `/sea setwind W 1 0 built`):
  - Does the hull sit in the water, rise and fall with the swell, and pitch and roll with it? Is there water inside
    the hull or on the lower deck? Does the water next to the hull look right (no flat ring, no gap under the hull)?
  - The view on deck: pitch and roll, the horizon, clicking doors and fixtures while she rolls (the crosshair should
    stay on what it points at). Chunks missing at the screen's corners while the view is rolled?
  - Other players aboard: do they lean with the deck and stay on it?
  - Heel: set full sail with the wind on the beam.
  - `.magelan`: the slider and the steady-horizon switch.
  - Multiplayer: two players looking at the same ship should see it move the same way at the same moment.
- **Rope ladders:** getting aboard from the water, and back down.
- **Cabins** in rain and snow: no rain falling inside, rain sounds muffled, you don't get wet. Below decks (0.3.13) by
  what's overhead: no rain on the lower deck or in the hold with the companion door open; under the open waist hatch
  it comes down through the opening only.
- **Sleeping** in a bunk while the ship sails on.
- **The crosshair on her:** what the look-at box says along her hull, on the decks and below; no blocks selected
  through her; a rowing boat alongside still selectable from the deck.
- **The spawn point:** the plate by the helm (how it looks), binding and unbinding, dying and coming back aboard at
  the plate after she has sailed on, and the old spawn back when the plate comes down.
- **Damage** (`/magelan damage look`, `/magelan flood 200`): the look-at box on a hurt part, mending with held
  right-click (the progress above the hotbar), the pump (its brake rocking, the sound, the water going down), the
  helm's water line and foundering warning, her sitting deeper and listing, and a sinking: put off into the water, the
  boats floating free, her going down and lying on the bottom, diving to her chests.
- **Damage drawn** (`/magelan damage part hull-p-12-2 0.2`, then 0.4, 0.8, 1; `/magelan damage age 30`): the hull panel's
  look (worn, scraped, cracked, leaking, holed) outside and on the ceiling inside, a real hole through her planking
  with its broken edge, the water in her hold (level while she rolls, sloshing a little), a torn and a shredded sail.
- **Guns** (`/magelan target`, then hold right mouse by a gun on the side facing her, look at her and left-click, or
  `/magelan fire port`): the gun and its carriage, the ports from outside; laying it (holding right mouse by it, the gun
  turning and its barrel rising and falling as you look about, others seeing it turn); firing: the boom, the flash, the smoke drifting with the wind, the gun running
  back and being run out, its trucks turning, the breeching going taut; the ball in flight, its splash, the splinters
  where it strikes her, the hole appearing; she floods and goes down after enough hits low down.
- **Her wreck** (sink one in deep water, or `/magelan wreck here`): her hull as wreck blocks on the seabed (how they look
  up close and from afar, the textures on the small cells, no holes or flicker where blocks meet), broken up, her masts
  as timbers beside her, the anchor, the guns, her chests and vessels in her hold and beside her; swimming through
  her, breaking a block (planks, not the block).
- **Her shadow** (the game's shadows on in the graphics settings): on the water, a dock, the rowing boat alongside and
  the people aboard; her sails' and rigging's shadow on her own decks, moving as she rolls and the yards turn; no
  stripes or speckles on her sunlit hull; the cabins a little darker by day. `.magelan shadows off` to compare.
- **The firehearth:** right-click it (vanilla's firepit window), put firewood and raw meat in, hold a firestarter or a
  lit torch to it; the flames, smoke and light, the stovepipe's smoke drifting off with the wind, the crackle; the meat
  cooked; a pot with two grain making porridge (its lid rattling); its warmth in the forecastle (the body temperature in
  the character window); it going out, its embers.
- **The surveyor's chalk:** the window (the captured lines, scrolling when long; the comment box), the note in
  `ModData/magelan-notes.json` and `.md`, the screenshot taken without the window and named in the note; the marks
  (the X and its number, on a ship moving and tilting with her, visible below decks), left-click and Shift + left-click
  on a mark, `.magelan notes`; no block broken or door opened with the chalk in hand.
- **Multiplayer (later):** other players walking on the deck (they should face where they look), a crew of 8.

## Config — `VintagestoryData/ModConfig/magelan.json`

| Key | Default | Meaning |
|---|---|---|
| `StopWhenUnmanned` | true | anchor and furl when nobody is aboard |
| `UnmannedStopSeconds` | 5 | …after this long |
| `LayUpWhenCrewOffline` | true | a ship with a crew list is laid up while none of her crew is online: anchored, furled, closed to others, no water, damage or wear |
| `RejoinShipOnLogin` | true | someone who logged out aboard a ship comes back aboard her wherever she is now (if she's gone: at their spawn) |
| `ShipWatch` | true | the watch turns out anyone asleep aboard for land ahead, water rising, grounding, being hit, the sails straining in a gale, the wind shifting, the sea getting up, or another mod's alarm |
| `WatchAheadSeconds` | 45 | how far ahead the watch looks for land: this many seconds of her way (16 to 96 blocks) |
| `SpeedMultiplier` | 1 | scales every ship's top speed (carrack: 7 m/s ≈ 13.6 kn at 1) |
| `SailByWind` | true | ships sail by Sea's wind (speed from the wind's angle and strength, no sailing into it); false = the sails are a throttle, full sail = top speed whatever the wind |
| `PointCloser` | 0 | degrees closer to the true wind every ship points than her design (carrack 67°, skiff 55°), her speed on the wind stretched to match; negative = less close. 0 historical; 7 ≈ the best square-riggers ever (≈ 60°); past 10 beyond any square rig; never closer than 35° |
| `CozyCabins` | true | cabins keep out rain, hail and cold |
| `ProtectHulls` | true | no placing blocks inside a ship (breaking the rock or soil a ship ran into is allowed: dig her free) |
| `ShipSpawn` | true | a metal plate on the wall by a ship's helm binds your spawn to her (right-click it empty-handed); false = the plate binds nothing, and spawns bound before go back to what they were as players join |
| `Damage` | true | ships take damage (running aground, into each other; later cannon), leak, take water, founder and sink; seams and sails wear, bottoms foul; pumps and repairs. False: none of it (what she has stays as it is) |
| `GroundingDamage` | 1 | how hard running aground and into each other hurts (a factor) |
| `Leaks` | 1 | how fast water comes in through damage and worn seams (a factor) |
| `Wear` | 1 | how fast seams, sails and an unsheathed bottom wear and foul with time (a factor; 0 = no wear); the four below multiply it |
| `SailWear` | 1 | how fast set sails chafe (a factor; 0 = never) |
| `SailDamage` | 1 | how hard sails tear: after minutes of straining in a gale with too much sail set or taken aback (real time, not the calendar's: sleeping doesn't speed it), and from shot (a factor on the tearing; 0 = they never strain or tear) |
| `SailStrainForce` | 9 | (0.3.24) the Beaufort force (apparent wind; 8.5 = halfway through force 8) from which full sail strains: the panel warns "shorten sail" and the strain builds; reefed sails stand nearly twice the wind. 0 = the design's own |
| `SailTearForce` | 10 | (0.3.24) the force from which strained sails tear (after minutes of strain); below it they strain but hold. Never below `SailStrainForce`. 0 = the design's own |
| `SeamWear` | 1 | how fast the planking's seams work loose (a factor; 0 = never) |
| `Fouling` | 1 | how fast an unsheathed bottom fouls (a factor; 0 = never) |
| `HullDamage` | 1 | how much a blow hurts the hull, decks, rudder, masts and rigging, whatever it was (a factor on top of `GroundingDamage` or `CannonDamage`; 0 = never) |
| `CannonDamage` | 1 | how much a cannon ball hurts a ship (a factor on top of `HullDamage` and `SailDamage`; 0 = shot does no harm) |
| `Cannons` | true | ships' guns are laid (hold right mouse by one) and fired (left-click): cannon balls hole ships, hurt creatures and players (only where PvP is on) and splash into the sea. False: they're neither laid nor fired |
| `CannonsBreakBlocks` | false | cannon balls break up to three soft blocks in their way (wood, earth, leaves, glass; not stone, ore or metal; not in a land claim the one who fired may not build in). False: they stop at the first block |
| `Wrecks` | true | a ship that sinks to the bottom becomes her wreck: world blocks of her hull, broken up, her cargo in vanilla chests and vessels in and beside her, her guns and anchor on the seabed. False: she lies on the bottom as she is, with her cargo in her (switched on later, the sunk ships that are loaded become wrecks) |
| `Hearth` | true | a ship's firehearth works as vanilla's firepit: fuel, lit with a firestarter or a lit torch, cooking and smelting by vanilla's numbers (a clay pot's meals too), its flames, smoke, light, crackle and warmth. False: it doesn't burn and can't be lit (a fire burning goes out; what's in it stays and can be taken out) |
| `Oven` | true | a ship's clay oven (a design with an `oven` fixture) works as vanilla's: up to six firewood in while it's cold and empty, lit with a lit torch or a firestarter (at once), it burns 45 s and 5 a log and heats toward 300 °C; once it's out, up to four things to bake (or one large) heat from it and bake by vanilla's numbers (bread dough to bread, pies), rising as they bake; an empty hand takes out; drawn with its fuel and what's in it, its crackle and smoke out of the hearth's stovepipe. False: it doesn't burn or take anything in (what's in it can be taken out) |
| `WorkingBlocks` | false (for now) | a forge, bellows, anvil, quern, barrel, crock or bloomery (with its chimney) put on a floor place aboard is the real vanilla block (in a mini-dimension of her own, carried with her) and works as ashore: right-click and left-click it as you would ashore (fuel and an ingot into the forge, pump the bellows, smith on the anvil, grind, fill the barrel); mine it to take it up (it and what's in it go into your inventory). Pieces of these put before work too once her ship loads. False: they're put as inert pieces as before, and none of it runs. The firepit stays a piece (her hearth is the fire aboard) |
| `WorkingBlockCodes` | forge, bellows, anvil, quern, barrel, crock, bloomery* | which blocks work aboard (`WorkingBlocks`): a block code's first part (`anvil` for `game:anvil-iron`), or a prefix ending in `*` |

## Client config — `VintagestoryData/ModConfig/magelan-client.json`

Each player's own; `.magelan` changes it in game.

| Key | Default | Meaning |
|---|---|---|
| `SeaMotion` | 1 | 0..1: how much ships move with the waves and lean in the wind on your screen (0 = a steady ship on a moving sea). Everyone else still sees the real motion |
| `SteadyHorizon` | false | your view stays level on a pitching, rolling deck (the ship still moves under you) |
| `ShipShadows` | true | ships cast shadows on your screen (the game's own shadows must be on in the graphics settings); `.magelan shadows on/off` |
| `WindPanel` | true | the wind panel aboard (Ctrl+U or `.magelan wind on/off`); while it's up the helm's line leaves out her speed and the wind (they're on the panel), off it shows them again |
| `Pennants` | true | the masthead pennants streaming down the apparent wind; `.magelan pennants on/off` |

## How it works

- **The ship is an entity** (`EntityShip`). The server sails it (sails, rudder, anchor, speed, turning) and tests
  199 hull points against world blocks every tick. At those points it runs aground on shallows, docks, cliffs and
  ice, and it won't sail into chunks that aren't loaded. It follows the water surface. Clients dead-reckon between
  position packets and blend toward the server's pose.
- **Sailing by the wind** (`Sailing`): the server takes Sea's wind at the ship (the same on every side).
  - **Speed:** her target speed is the design's polar (best speed by the wind's angle off the bow; `SailingDef`,
    which a design can override with `sailing` in its JSON) × how well the wind fills the sails. The push grows with the sail set and the wind squared, the hull's
    drag with speed squared, so the speed goes with √sail × wind until the hull limits her. She speeds up and slows
    down at the design's rates.
  - **In irons:** closer than 62° to the wind she makes no way; right ahead she's taken aback (a little sternway).
    Rudder authority falls off with speed and reverses going astern, but while she's head to wind with sails set the
    backed head sails turn her the way the helm is put, so a tack always comes round.
  - **Trim:** clients work out how the crew braces the yards (square before the wind, hard round close-hauled with
    the lee yardarm hauled aft, so the sail meets the wind nearly edge-on and fills from aft) and sheets the mizzen lateen (out to leeward), from the apparent wind. Each yard is its own
    mesh, turned about its mast; its sail is drawn under it, and the braces and sheets stretch from the turned yardarm
    or clew to where they're made fast. The generator checks the yardarms clear the shrouds, backstays and stays at
    full brace: the main yard touches a topmast backstay at 45°, so the carrack braces to 44°
    (`sailing.maxBrace`); the lateen meets its lee shrouds past 31°, so it's sheeted out no further than 30°
    (`sailing.maxSheet`). (0.3.14, notes 150–152: the lateen through the mizzen mast on one tack, "main sails penetrate
    the ratlines", the fore course through its shrouds) The cloth is pressed against what it meets, as a sail lies against
    the lee shrouds braced sharp up (`SailPress`): her masts, the shrouds and backstays of the generator's rig check
    (`rigCheck.standing`; not the stays on her middle line) and lines along the ratlines between each two shrouds of a set.
    Each vertex of the sail as set (furled, the lateen bellied) is moved across the sail only, off each obstacle on the side
    it was on before the yard turned (the mast and the shrouds behind a square sail, the lateen's mast on its belly's
    other side; the lateen's lee shrouds before it) by the obstacle's radius and 0.04, draped away from it at a slope of
    0.6. The renderer does it when the trim (to half a degree), the furl or the lateen's belly changes, at most every
    80 ms, and draws the sail turned with its yard (its shadow too). Offline, over the crew's trims on both tacks, full and
    reefed, no vertex is left inside a mast or rope or through one (as made: the main course 122 inside and 508 through, the
    fore course 24 and 270, the lateen 1566 inside its mast); the main course is pushed out up to 1.3 blocks at its lee
    clew, braced sharp.
  - **Heel** comes from the apparent wind (the true wind less her own way): stronger and further forward
    close-hauled, none running before it, none when the sails luff.
  - The HUD's speed readout and the heel are worked out on each client from the same synced state and shared wind.
- **The wind panel and the helm's line** (clients; `WindPanel`, `ShipHud`; 0.3.10, made clearer in 0.3.12):
  - Aboard and awake (Ctrl+U, `WindPanel` in the client config), at the screen's right: a compass card with her outline
    bow up, north (red), east, south and west on the rim (0.3.24); arrows for the true wind (pale), the apparent wind on her sails (orange) and her course over
    ground (green); a **red no-go wedge** (`HelmMath.NoGoWedge`): the bow directions she can't point, the true wind ± the
    design's `sailing.noGo` (67° on the carrack): she's in irons when it covers her bow. A legend under the card names the
    colours.
  - Every line labelled, every speed in knots with blocks a second in brackets (0.3.24, the user: "b/s hides the fake
    speed better"; until then "m/s"): "Ship: 9.0 kn (4.6 b/s) ahead", "Wind: force 8 gale, 36 kn (19 b/s) from the SW",
    "On her sails: 38 kn (20 b/s), on the port beam";
    at the helm "Beam reach, port tack" and "Best she can make here: 7.2 kn (3.7 b/s)" (`Sailing.TargetSpeed` under the
    sail set; furled, what full sail would make), "The sails won't draw: bear away" in irons, going astern that the helm
    works the other way.
  - A warning in colour under it when the wind is too strong for the sail she has set (`SailWear.Warning`, the server's
    load from the shared wind and her synced sail, way and strain): orange "Gale: shorten sail!" while the strain builds,
    red "Storm: furl, or she'll lose her sails!" when even reefed she'd be over-pressed, and "about to tear" / "tearing"
    once the strain is high.
  - The helm's line above the hotbar says only what the panel doesn't (the sails set, her heading, the helm, anchored,
    aground, why she's slow, the warnings, water and damage, the keys); with the panel off it shows her motion, the wind
    and how she lies to it again, in the panel's words. The map's hover says speeds the same way.
- **Everyone aboard is "mounted"** on one of the ship's seats:
  - a free **deck seat** per player, whose position is relative to the ship
  - the **helm**
  - the **bunks**

  So the engine keeps you with the ship on every client, other players are drawn relative to you, and teleports
  (including Cylinder World's seam jump) take the ship with its whole crew.
- **Deck walking** is done in ship-local space against the ship's collision voxels (a 0.25-block grid). The walking
  step runs inside the player physics step (60 Hz), right before the engine reads the seat position, so it's in
  lockstep. Your client sends your deck position 15 times a second, and others interpolate it.
- **Each ship has its own collision**: the design's voxels plus its shut doors and gates and the chests and beds put
  aboard. It's rebuilt when one of them changes. That state is saved with the ship and synced to everyone.
- **The crosshair on a ship** (clients; `EntityShip.IntersectsRay`, `ShipParts`, Harmony patches):
  - Vanilla tests an entity against one box round its middle, and only entities whose middle is within reach, from the
    chunks round the eye. A ship's own test raycasts her collision voxels in her frame, tilted as she's drawn, or a
    fixture's box in front of them (an empty place only while you hold what goes there).
  - The game's selection ray (`GameMain.RayTraceForSelection`) leaves ships out and a postfix tests them after it: the
    nearest hit point wins among the block, another entity and the ships. So blocks behind her hull aren't selected.
  - The hit's "selection box index" is the part of her (`ShipPart`): the hull by side, station (the frame space
    along the keel, from the design's `frames`, every 1.5) and band (whole blocks up from the keel); a deck (the
    design's `decks`), top or underside; a mast (`masts`); other woodwork; a fixture. A hull part is the planking
    between two frames by a block of her height, what the damage model will keep a state for. The hull is what lies
    within half a block of her outside, where her side, stern or bow runs on up from below the main deck.
  - The look-at box is rebuilt when you look at another part of her (a postfix on vanilla's `EntityInView`) and
    `GetInfoText` names the part. `IsInteractable` is true only on clients (the server's arrows and creature AI
    ignore her); left-clicks on her are dropped; vanilla's red "in reach" crosshair and the server's anti-abuse reach
    check don't use her middle's box any more (a speck under her keel on clients; a patch measures to her hull).
- **Spawn points on ships** (server; `ShipSpawns`, config `ShipSpawn`): vanilla has one spawn position a player comes
  back to (a temporal gear sets it). Bound to a ship at her plate, it's kept at the spot in front of the plate (a
  block, at her still-water level), checked every 2 s and moved when she's more than 2 blocks off, also across
  Cylinder World's seam. Vanilla's respawn teleport goes there and loads her chunks; Magelan's respawn handler, which
  runs after it, then puts the player aboard at the spot once they're alive and she has loaded. The bindings (her
  entity id, the plate, the spawn they had before) are kept in the save game, so someone offline is found when she's
  gone; clients get the list of who's bound to her (the plate's look-at text and hint).
- **The ships on the map** (`ShipMapLayer`, a vanilla map layer, 0.3.10): since 0.3.12 drawn under every marker (the
  user: the player's arrow was hidden under the ships on the minimap): vanilla draws the map's layers in the order they're
  registered, so ours is moved in vanilla's registry to right after the terrain, before the players' layer
  (`HelmUiSystem.DrawShipsUnderPlayers`), and its depth (55–56) is under the markers' (60). The same on the world map.
  (0.3.24, the user: "while aboard, show on the minimap the same sailing info as the dial") Aboard, round her on the
  minimap and the world map: the true wind's arrow (at the player, as before), her **no-go wedge** (red, translucent,
  the dial's `HelmMath.NoGoWedge`) and her **course over ground** (a green arrow from her, longer the faster she goes),
  a fixed size in pixels at every zoom (`ShipMapLayer.DrawSailing`).
- **Crew, laid up, logging out, the watch** (server, 0.3.11; `ShipCrew`, `ShipLogouts`, `ShipWatch`):
  - Her crew list is in her WatchedAttributes (`magelanCrewList`: uid, name, captain), changed only by the server
    (`ChangeCrew`, from the plaque's `CrewPacket`); clients draw the plaque's dialog from it. Every packet handler that
    uses her (fixtures, boarding, naming, repairs, laying guns, the hearth, decorating, sheathing) asks `MayUse` /
    `MayBoard`; a guest's fixture is let through only for doors, gates, hatches without a store and the heads.
  - Laid up is worked out every second (none of her crew online, playing); `magelanLaidUp` goes to clients. Laid up,
    `FloodTick` (leaks, pouring in, pumps, wear, grinding, foundering) doesn't run and `Damage` does nothing; her wear
    clock restarts when she's back. Her waves, heel and swinging go on as before.
  - `ShipLogouts` keeps in the save game who logged out in one of her seats, her entity id and their place aboard. On
    joining they're taken to her (loaded: where they were aboard; unloaded: her last-seen place on the ships' map,
    which now keeps her height too) and put aboard as soon as she's loaded (30 s at most). A ship that's gone from the
    map (sunk, removed) sends them to their spawn.
  - The watch runs every second while someone's in a bunk: conditions are edge-triggered (a set of what's so now
    against what was), events (`RaiseAlarm`, any damage) at most every 20 s. Land ahead is her own hull's samples
    (`CheckPose`) at the waves' troughs, every 4 blocks along her heading. Waking is `StandUp` by the bunk. Since 0.3.12
    also "sails": her sails' strain building (`SailStrain` > 0), minutes before they'd tear.
- **Sails strain before they tear** (server, 0.3.12; `SailWear`, `EntityShip.SailStrainTick` in `ShipFlooding.cs`): the
  user's sails were torn by a storm in moments while they weren't watching (the old tearing ran on the calendar's hours,
  and sleeping runs the calendar fast).
  - Every quarter second, in real seconds: the load on her sails from the apparent wind, sail set² × (wind / 0.832)² in a
    gale (all sails), set² × (wind / 0.832)² taken aback (the square sails); past 1 they're over-pressed. (0.3.24, the
    user: "sails strain already at force 7–8 ... not fun: after a calm I want to go fast": until 0.3.23 0.69 and 0.6, full
    sail strained from force 8.) Full sail is fine through force 8, reefed (0.55) through force 12. The server settings
    `SailStrainForce` (9) and `SailTearForce` (10) move the limits (`SailWear.WithForces`; sent to clients for the panel).
  - Over-pressed, her strain (one for the ship, 0..1, synced as `sailStrain`) builds at min(1, load − 1) / 150 a second;
    under, it eases off at 1/90 a second. Only at 1 do the sails tear, and (0.3.24) only from force 10 on full sail
    (`SailTearWind` 0.98; in force 9 they strain, the panel warns, but they hold): 0.04 × (load − 1) of a sail a real
    minute in a gale, 0.12 × (load − 1) aback, times `SailDamage`. Full sail starts tearing after 4 minutes in the middle of
    force 10, 2½ in force 11–12; reefed, never short of a hurricane; gusts of a quarter minute now and then never add up.
  - Those aboard are told once when the strain begins (gale or storm wording) and once when a tear is close (0.7), again
    only after it has eased off (0.3.13: and stayed off a minute, 30 s for the tear warning: `EpisodeNotice`, so a gusty
    wind hovering at the threshold doesn't say it every few seconds; the watch turns out sleepers once a spell too); a sail torn or blown out is said and sent at once. Chafe stays slow and on the calendar.
- **Vanilla blocks aboard:**
  - The design says where they go: doorways and window openings sized for vanilla blocks, attachment points for
    lanterns, chests, beds, a raft and a metal plate (by the helm), and props (vanilla blocks drawn for show: the
    captain's table and stools, the captain's bed; the firepit in the hearth, a `hearth` fixture drawn by how it burns:
    see the firehearth below).
  - The mod draws each one from the block's own mesh. The item you put aboard is kept, so its material, glass and
    type are what you see.
  - A chest's storage is its vanilla size.
  - A lantern's light is its vanilla colour.
- **Rendering** uses the standard block shader:
  - **Glass** (0.3.12, note 109: the sea wasn't recognisable through the stern windows) is drawn last, blended, and leaves
    the depth buffer alone: the game draws its water after the ships (its transparent pass, against the depth drawn so
    far), so glass that wrote its depth hid the sea behind it. Its alpha is scaled to 0.35 (`GlassTint`, whatever texture
    it has) and it's lit by the light outside. A vanilla pane's frame (its solid texels) is drawn with the solid things;
    see-through blocks put aboard as pieces (glass panes and blocks: vanilla's transparent pass) are drawn with the glass.
  - The exterior is lit by the light where the ship is.
  - The interior (a separate mesh) gets a quarter of the daylight plus lantern warmth.
  - Sails scale toward their yards as they're furled.
  - Lanterns are moving point lights.
  - Moving parts (`movingParts` in the design): the wheel, rudder and tiller turn with the helm; the gates and
    hatches swing open; the anchor slides down; the yards turn with the trim; ropes that join a moving part to
    something fixed (the tiller ropes, the anchor cable, braces, sheets) are stretched between the two each frame.
  - The bottom is drawn tarred, or in copper once it's sheathed.
  - **Her damage** (0.3.5, 3b; `HullDamageSkin` in `src/Ship/ShipDamageSkin.cs`, drawn by `ShipRenderer`):
    - The generator marks her planking in the mesh file (`hullOut`, `hullIn`: the outside planking, wales and rail caps;
      the ceiling inside), with its thickness. When the mesh loads, each of those quads is given the hull panel its middle
      is in (the ceiling's: the panel outside it), and they're grouped in chunks (one side, three frame spaces) per mesh
      group: 76 on the carrack.
    - Each panel's look comes from its state: worn, scraped, cracked, leaking, holed. Its quads get an overlay composited
      on their own texture in the atlas (`…++0~magelan:block/dmg-cracked-a`, painted by `tools/shipgen/textures.py`, two of
      each so neighbouring strakes differ): worn is subtle (the pitch greying, pale oakum in the seams), scraped shows long
      gouges of bare oak, cracked dark splits with a pale lip, leaking oakum working out of the seams (inside: dark wet
      stains), holed broken planks with pale splinters round the hole. The ceiling shows only cracks, wet and holes.
    - **Real holes** (broken planking since 0.3.8): a holed panel's strakes round its middle are broken through from
      seam to seam, each off at its own length either way along the planks (the hole up to about 1.2 long; more strakes
      and longer as she's stove in, not always in the panel's middle), the ends splintered (teeth, a slanting break,
      sometimes a splinter sticking out into the hole); the ceiling inside through the same strakes its own way, a wale
      or rail cap over a broken strake with it. Faces of raw oak, the planking's thickness, close it round its outline.
      The same hole every time for a panel, a bigger one holding the smaller. The collision grid stays whole: you can't
      climb or swim through a hole. Through a hole you see into her.
    - A ship with no visible damage is drawn from the design's meshes as before. When her damage changes (at most every
      half second) only the chunks whose panels changed, or a hole reaches, are built again, six a frame; she draws the
      rest of the group and the design's sound chunks. Her shadow uses the same meshes (so light comes through a hole).
    - **Sails:** a torn sail has a fifth of its cloth pieces holed, a shredded one over half with big holes and one in
      eight gone (a linen texture with holes cut out, `sail-hole-*`, drawn alpha-tested; its shadow has the holes too).
    - Anything failing there is logged once and her damage isn't drawn for the rest of the session; she still is.
  - **The water in her hold** (`ShipWater`): a surface level in the world while she rolls and pitches, through her
    middle at her flood level (eased as it rises and falls), out to her planking at that height (from the generator's
    exact outline of her hull), sloshing a little toward the side she rolls to. Vanilla's salt water texture, tinted
    murky and see-through, drifting slowly; its middle bobs with vanilla's liquid warp (the "waving stuff" setting).
    It's drawn from both sides (from under water too), in her interior's light. Sea is told its level (0.05 under it),
    so the sea isn't cut away under it (see "Dry below the waterline"); the same level decides where it's wet in her.
  - When you're aboard, the ship is drawn relative to your seat, like vanilla mounts, so ship and camera never drift
    apart.
  - **Shadows** (`ShipRenderer.RenderShadows`, what goes in: `ShipShadows`): with the game's shadows on, the renderer
    is also called in the engine's two shadow stages (far and near) and draws each ship into the sun's shadow map there.
    - The same model matrix as the normal pass (her place, heading, heave, pitch and roll; relative to your seat when
      you're aboard), so the shadow lines up with her.
    - It uses the engine's own shadow shader for chunks and held items (`chunkshadowmap`; its NoSSBOs build when the
      game draws chunks from SSBOs): `mvpMatrix` = the map's projection × the sun's view × her model matrix. The shader
      drops see-through texels, so the rigging and cut-outs cast their own shape.
    - In both maps: the decks, castles and bulwarks, her planking outside, the bottom, the masts and standing rigging,
      the sails as set and trimmed (furled toward their yards, braced round), the yards, the rudder, the wheel, the
      gates and hatches as they stand, the anchor, the doors, and the boats on the gallows. Only in the near map (about
      30 blocks round you): the ropes that move (braces, sheets, the anchor cable), the figurehead, and the small things
      on deck (lanterns, the plate). Never: the inside of the hull (its inside planking, the cabins' faces, the tiller
      under the quarterdeck, the hold hatch, the cargo, the bunks, chests and lanterns in the cabins), and no glass
      (vanilla glass casts none either). The name board neither.
    - Only ships within the shadow map's box (a sphere round her tested against the engine's shadow culler) and within
      the distance she's drawn at.
    - Her image in the map is pushed 0.1 (near) or 0.3 (far) blocks away from the sun. That changes only the depth
      she's stored at, not where the shadow falls, and keeps her curved sunlit sides from shadowing themselves.
    - **Receiving:** the standard shader she's drawn with already samples the shadow maps (like every shader with the
      game's fog-and-light code), so the world's shadows fall on her and now her own sails and castles shade her
      decks. The cabins, already lit darker, get up to about a fifth darker by day (in her own shadow).
    - With shadows off in the graphics settings the engine doesn't run the shadow stages, so nothing happens. If
      something goes wrong in the shadow pass it's logged once and ship shadows stay off for the session; the normal
      pass is untouched. `ShipShadows` in `magelan-client.json` (or `.magelan shadows off`) turns them off.
    - The rowing boat (`EntityRowBoat`) is drawn by vanilla's entity renderer, which puts every shaped entity into the
      shadow maps itself.
- **Stairs:** the collision takes a step at once, but your seat (and so your camera) glides over it at a steady
  rate a bit faster than you walk. Stairs feel like a ramp, and walking down snaps onto each step instead of
  falling.
- **Riding the waves** (`ShipMotion`):
  - Sea's waves are a fixed set of travelling sine waves that every side knows, running on a clock the server
    shares, so the sea is the same on the server and every client. A ship's motion is worked out from those waves at
    its place, its heading and speed, and the time: the same answer everywhere, nothing extra is sent.
  - For each wave, the push up is the surface averaged over the waterplane, and the tip is its best-fit slope along
    and across it, so waves much shorter than the ship hardly move her. The hull answers each one like a damped
    spring with its own period (heave 3.5 s, pitch 3.8 s, roll 7.5 s; the design's `motion` settings), at the rate
    she meets the waves (faster when sailing into them). Pitch and roll are soft-capped at 12° and 25°.
  - **Heave is real:** the ship's height is its still-water level plus the heave, on the server too, so everyone
    aboard really goes up and down with her. Grounding and the water level are checked at the still-water level.
  - **Pitch, roll and heel are drawn:** the ship, the people aboard (their bodies lean with the deck), lanterns and
    the local camera tilt. Walking, collision and the doors stay level in the ship's own frame, and clicking works
    against the tilted ship.
  - **Heel:** from Sea's wind at the ship and how much sail is set: full sail with a strong breeze on the beam gives
    about 6°, at most 20°.
  - **Sheltered water:** ships on lakes move with the smaller waves drawn there (Sea's ocean-map factor).
  - **The view:** the deck's tilt along where you look turns your view pitch (as a change, like vanilla's rocking
    mounts, so the mouse still aims where you look). The tilt across it rolls the view about its own axis, so the
    crosshair stays on what it points at; a Harmony postfix on the game's camera rolls the view and the frustum it
    culls with. `SteadyHorizon` turns both off; `SeaMotion` scales everything this player sees.
- **Dry below the waterline** (Sea's dry hulls): the world has water inside the hull (the ship is an entity, not
  blocks), so Magelan tells Sea two things.
  - Its water clips are marked dry, so no water is drawn inside the hull's outline from under the keel up to the main
    deck; the waves run right up to the hull. The outline is the hull's own at four heights round the waterline
    (−0.75 at the transom's foot, 0.25, 1.25, 2.25), 32 stations from the transom to the stem (Sea's detailed dry
    outline), and it leans with her pitch and roll. Its stern end is a plane through the transom, raked like it (Sea
    0.3.2's `DryRake`, −0.25 a block up on the carrack); aft of the transom the outline runs on square to that plane.
    The generator writes the hull's exact width on a fine grid (`waterClip.dryOutline`, an eighth of a block along
    the hull); the mod fits the 32 × 4 so that Sea's interpolation never falls inside the hull, and then lowers every
    value as far as that allows. So the water is cut off square at the transom (at most 0.36 behind it, 0.34 wider
    than her quarters from the waterline up; in 0.3.1 a triangle of water up to 2 blocks beside her stern quarters and
    a wedge behind the transom were cut away), and a heeling ship's sides don't show waves inside.
  - It registers the inside of its hulls (`ShipDrySpaces`, both sides): each design gets a mask on the collision grid,
    built when it loads, of the empty cells below the main deck that a flood from outside the hull can't reach, plus
    the hull's own cells. Sea asks it before putting the camera under water (fog, tint, muffled sound), before a
    creature's head in water runs out of air, and before vanilla makes splashes and glowing plankton round someone
    walking with their feet in water. That's the hold, and the lower deck in a storm's trough too.
  - **With water in her** (0.3.5, Sea 0.3.3): under the water in her hold it's wet (you drown there and see it as under
    water), over it dry. Its level is level in the world, as it's drawn (`EntityShip.FloodWorldY`: through her middle at
    her flood level, with her drawn pitch and roll; on the server she's level). Her water clip carries that level
    (`Flooded`, `FloodY`, 0.05 under the water drawn, so the two surfaces don't flicker): under it Sea doesn't cut the
    sea away, so where the waves inside her outline dip below the water in her they show under it. Sea's under-water
    view and breathing only overrule vanilla where the world has water at the eyes: water drawn higher in her than the
    sea outside (poured in over her side) isn't "under water" for the view or the breath.
- **No waves inside the hull:**
  - The Sea mod owns the water shaders. The ship renderer is one of Sea's "water clip sources": every frame it tells
    Sea where each hull is (`ShipRenderer.AddWaterClips`).
  - Inside each nearby ship's waterline footprint, the drawn water can't rise above her waterline, which tilts with
    her. (Dry hulls, like the carrack's now, don't use this: no water is drawn inside them at all.)
  - The footprint is measured from the collision voxels between 0.5 below and 1.5 above the waterline when the
    ship loads: 16 half-widths from stern to bow, plus 1 block. The square stern is covered too.
  - Over the 1.5 blocks around it the waves settle to the level she rides at (without her lean in the wind), so no
    crest reaches in between the 1-block water vertices. That ring was 4 blocks while ships stayed level; it widens
    back toward 4 for a player who turns `SeaMotion` down.
- **Cozy cabins** (Harmony patches): enclosed ship rooms count as vanilla "enclosed rooms" for body temperature (while
  sealed: warmth and the stoves' heat). The weather is by what's overhead (0.3.13, the user: "rain is in the lower
  deck": it went by the room being sealed, so an open waist hatch or companion door let rain fall all over the lower
  deck): a ray straight up (the world's up, in her frame as she rolls) from your head through her collision as it is now
  (her hatches and doors, what's on her gallows) meets her within 8 blocks (`ShipCover.Above`, `ShipShelter.Covered`):
  then you don't get wet and hail doesn't hurt, anywhere aboard; in one of her rooms (a cabin, the lower deck, the hold)
  no rain, snow, hail or storm dust is drawn and the rain is muffled. In a room with the sky overhead (under the open
  waist hatch) the game's weather boxes, tens of blocks wide, are drawn over the opening only (`ThroughOpening`: started
  4 blocks over your head, upwind by their drift, as many as fall on that much of the box); splashes on the water inside
  her are still left out by Sea's `DryParticles`. On her open deck vanilla's weather, wet unless something's over you
  (under the boats on their gallows, a castle's overhang: dry, though the rain still falls round you).
  Beds set vanilla's sleeping state, and the closing-eyes screen works in them.
- **One source for the geometry:** `tools/shipgen/carrack.py` builds the ship on a voxel grid. It writes the render
  shapes, the collision voxels, the rooms, the fixture spots, the ratlines and the hull sample points, so they can't
  disagree.
- **Smooth parts are a mesh, not boxes:** these are quads the generator writes to `config/shipmeshes/<code>.json`:
  - the planked hull, outside and in
  - wales and rails
  - 16-sided masts and the top
  - yards and all rigging (each yard with its lifts in its own group)
  - the wheel, the capstan, the pump, the hatches, the companionway, the anchor and its gear
  - the stern gallery's frames and glass (drawn blended, last)

  - the frames, deck beams, knees and clamps, the stairs, the balustrades, the side windows' glass, the figurehead,
    the lateen sail (a smooth mesh, so its edges are straight)

  The mod parses it off the main thread and draws it with block textures. Each strake shows exactly one plank of
  the vanilla oak texture. Tar and caulking are small overlays of our own that the game composites onto vanilla oak
  (`oak1++3~magelan:block/tar`, made by `tools/shipgen/textures.py`): the tar is multiplied over the planks to near
  black, the caulking lays black seams twice as wide as the planks' own. (The first caulking sat exactly on the
  vanilla plank seams, so decks looked like plain oak, and the first tar only darkened the planks to dark oak.) The
  voxel hull underneath is collision only; the voxel shapes still draw the decks and the cabin walls. Nothing of
  them may reach past the inside planking: those cells are collision only, and each deck's and wall's edge is closed
  to the planking by a mesh strip, so no square corner pokes through the curved hull (`clip_to_ceiling`,
  `edge_strips`). The hull's collision is the planking's full thickness, so you can't walk into it either. Since 0.3.7:
  a deck's strips carry its planks on (their seams from the cells' own, square to the deck's planks, in narrow pieces
  where the hull curves in fast), close the deck right out to the tip of the bow (the lower deck's and the forecastle
  deck's tips were open), and the row of cells along the inside of the transom isn't cleared any more (a slot between the
  poop deck and the transom).
- **Lit by what they face:** a mesh quad of the "auto" group is drawn with the inside's light or the outside's by the
  space just in front of it (since 0.3.7; by its middle before, so planking over a cabin's corner was lit as outside).
  And the flat transom, outside and in, is wound to face out and in (it faced the other way till 0.3.7, and the game
  lights a ship's mesh by its normal: the inside of the stern looked dark, as if tarred).
- **The hull's skeleton** (`framing` in the generator, drawn only): a frame (0.25 wide, 0.2 deep) under every deck
  beam, every 1.5 blocks, from the cargo deck past every deck to the rail. Each face of a frame follows the inside
  planking exactly, so the frames never sink into it or poke through where the hull curves in toward the ends or
  leans in above. The windows, entry ports and scuppers sit between frames. Hanging knees under each beam's ends,
  clamps along the sides, the keelson rising at both ends along the bottom; the stem and the sternpost outside. The
  keelson and the deck beams (but not the lower deck's, for the hold's headroom) are collision too. Since 0.3.7: the
  aftmost frame hugs the transom's corner where the inside planking ends against it (a fashion piece); a broad hawse
  piece takes the frame's place round each hawse hole; the gallows' frames run on up into their posts through the rail
  cap; on the centreline where the two sides' inside planking meets, the apron up the inside of the stem, a knighthead
  inside the forecastle's tip and the inner sternpost (`centreline_timbers`: deep enough that the rudder's stock, its
  gudgeons and the sternpost, on the straight line the rudder turns on a little inside the stern's curve, stay inside
  it; solid where it stands); upright stern timbers between the gallery's lights inside the transom (`stern_timbers`).
  The generator checks what these fixes rely on and stops the build if one fails (`fit_checks`).
- **The castle breaks:** the sheer sweeps up from the waist to each castle in a quarter ellipse, planked outside and
  in with a rail cap along it, instead of stepping up square.
- **Ratlines and rope ladders** are paths:
  - The ratlines run from the rail to under the top.
  - The rope ladders run from the water up to the entry ports. Getting off one at the bottom puts you in the water.
    A shut gate blocks its top.
  - The companionway's ladder runs from the lower deck up through the waist.

  While climbing, your deck seat follows the path instead of walking. Others see you with the vanilla climbing
  animation.
- **Sheets follow their sails:** a sheet's end is on its sail's clew, so as the sail is furled up to its yard the
  sheet goes up with it (`sail` on a follow part); the topsail's sheets run between the two yards (`anchorFollow`).
- **The wheel** turns smoothly: the rudder arrives from the server a few times a second, and the wheel and tiller
  follow it at the helm's own rate instead of jumping.
- **Her name and figurehead** are saved with the ship (`magelanFit`). The name is drawn as a text texture on the
  board's face; the figurehead is a mesh group per choice.
- **Magelan's own boat** (`tools/shipgen/boat.py`, the design `skiff`, the entity and item `magelan:boat-skiff`) is an
  `EntityShip` like the carrack, with a smaller design: the same planked mesh, collision voxels, dry outline and mask,
  wave motion. What's new for her in the design and the mod:
  - `oars` fixtures (the rowing thwarts, `RowSeat`): whoever sits there drives her with W/S (`ShipDefinition.RowSpeed`
    for one rower, √2 as much for two: `Oars`), A/D turn her when nobody has the tiller; the oars are moving parts
    drawn only while someone rows there (`MovingPartDef.Shown` "manned:…"), sweeping with the stroke (source
    "stroke:…"), and stowed on the thwarts otherwise ("unmanned:…", kind "static")
  - a `mast` fixture that opens and shuts like a door (open = lowered, `StartOpen` on a new boat); the sail on it
    (`SailDef.Mast`) and its yard ("shut:mast") only while it's up, and no sail set while it's down
  - `MaxAboard` (4), `BoardFromWater` (swim into her, right-click her, or come down a ship's rope ladder into her),
    `PickUpItem` (Shift + right-click, nobody aboard and nothing stowed: the item goes to the player)
  - the helm and the rowers sit (`FixtureDef.Pose` "sit")
  - ships and boats don't pass through each other: a move that would put a hull sample of one inside the other's
    collision (either way round) is stopped (`EntityShip.HitsShip`)
  - on a ship's gallows she's drawn from her own mesh, mast lowered, oars stowed, rudder unshipped

  Parked in 0.3.3 (the user chose the rowing boat): still loadable, no recipe, not in the creative inventory.
- **The rowing boat** (0.3.3; `tools/shipgen/rowboat.py`, `rowboat_assets.py`; entity and item `magelan:rowboat-<wood>`)
  is vanilla's sailboat model cut down (`tools/shipgen/vsshape.py` reads, edits and draws the game's shape files): the
  mast, rig, rudder, tiller, stern seats and the second midships section taken out, the stern closed up, vanilla's
  water-hiding planes shortened the same way, a thwart with the seat's attachment point and two oars on thole pins
  added, a "row" animation for them; the item's model (`rowboat-stowed`) has the oars shipped. The entity is
  `EntityRowBoat : EntityBoat` (vanilla's does the rowing, the places and the pickup): it starts and stops the oars'
  stroke with her way, keeps her out of ships' hulls (her outline against `EntityShip.HullAt`, on both sides: her way
  into a hull is taken off, a turn into one undone, and she's moved out if a ship comes onto her), and gives Sea a
  footprint (not dry) so the waves are held down under her. `ItemRowBoat : ItemBoat` won't put her in the water inside
  a ship. Picking her up from a ship's deck or rope ladder (where the keys are the ship's) goes by our own packet.
- **The carrack's shape** (0.3.4: the proposal of 2026-09-30 with the long waist, chosen by the user from three built
  side by side in 0.3.3): `carrack.py` builds variant 3 as `carrack`; `--variant=1|2` (or `MAGELAN_CARRACK`) still
  build the first carrack and the short-waisted one as `carrack-v1`, `carrack-v2`, to compare (not shipped). The
  entity codes `carrack2` and `carrack3` of 0.3.3 are aliases of `carrack`. The design is drawn in the first carrack's
  heights and lowered by its `WATER` when written (`Design.translate_y`), so every ship's waterline is at 0. The poop's overhang
  (`POOP_OVERHANG`, the hull's plan stretched aft above the quarterdeck: `poop_hw`) and the forecastle's raked
  underside (`FC_LOW`: outside it steps out from its underside, inside the planking still steps out at `FC_Y0`) are
  in `half_width`; `inner_width` is what the inside planking follows. Sea's dry outline starts its raked stern plane a
  grid step further aft where a transom that leans aft more above the waterline than below needs it. Sea's dry cut
  reaches up to the waist's rail (a crest higher than the deck isn't drawn inside the bulwarks), and Magelan's dry
  places (`ShipDrySpaces`) take in the decks and castles over the hull's inside: heaving in a big sea she dips her
  main deck under the world's still-water level, and that isn't water aboard.
- **Rolling under sail:** the waves' roll is damped more with the sails set and drawing (`MotionDef.SailRollDamping`,
  `ShipMotion.SailSteadiness`), as sails steady a ship.
- **Grounding:** a ship already lying against something (the water fell under her, she was turned into a bank,
  someone built there) may still move as long as she touches no block she isn't already touching, so she can be
  worked off; and `ProtectHulls` only stops blocks being put inside a hull, so you can dig her free.
- **Plants in the hull:** seaweed, sea grass, corals and water lilies (blocks without collision, plants or in water)
  that a ship's hull reaches are cleared as she moves (`EntityShip.SweepPlants`); the water stays. They showed through
  the dry hold before.
- **Wood grain:** vanilla's wood textures run their grain (and planks) along u. The generator's box meshes run u along
  each face's longer side and are drawn in pieces of at most 2 blocks; the frames run it up the frame; poles of a
  wooden material (`Material.grain`) run it along their length (the masts, yards, posts, balusters).
- **Damage, leaks and sinking** (config `Damage`; `src/Ship/ShipDamage.cs` is the model, pure and tested offline;
  `src/Ship/ShipFlooding.cs` runs it on the ship):
  - **Her parts** (`DamageLayout`, worked out once per design from her collision grid): each hull panel (the planking
    of one side between two frames by a band a block high, the crosshair's hull part: 476 on the carrack, 172 below the
    waterline; the lowest band takes in the keel and flat floor), each deck between two frames, the rudder, each mast,
    sail and mast's rigging. Each has a hurt (0..1) and the hull panels a seam wear; the state is the worst step of its
    ladder reached. The numbers are the design's `damage` section (`DamageDef`; the generator writes the carrack's
    toughness and her rudder's box, the rest are defaults in C#).
  - **Kept on the server** as floats saved with her by part code; clients get two bytes a part (the watched attribute
    `magelanDmg`, sent when one changes) and the water, sinkage, list, trim, stage and pumpers with her sailing state.
  - **A blow** (`EntityShip.Damage(point, dir, energy, radius, cause)`, energy in kJ) is shared among the parts within its
    radius, the nearest most, each taking at most what it has left (toughness × (1 − hurt): a hull panel 80 kJ, a deck
    60, a mast 400, a sail 25), and starts the planking's seams. A blow on a spot of radius 0.5 or less is a shot: what
    the parts it holed couldn't take goes on along its direction through her voxels to the next thing in its way.
  - **Grounding:** the hull samples that newly touch the ground (`CheckPose`) each take ½·m·v² × 0.06, v that point's
    own speed, spread over 1.25 blocks; ship on ship both take ½·μ·v² (μ the reduced mass) where they meet.
  - **Leaks:** each panel's effective hole (worn seams 5·10⁻⁶ m², cracked 3·10⁻⁴, leaking seams 8·10⁻⁴, holed 0.03
    up to 0.2 stove in) lets in 2.66 × area × √(head outside − head inside) m³/s. The head outside is Sea's shared waves
    at the panel with her true pitch, roll, heel and list (the server keeps her level for walking, not for this); the
    water inside stands level.
  - **The water:** her hull's section and her hold's air layer by layer (the dry mask) give how deep a weight of water
    puts her (the carrack: 905 t displacement, 642 t reserve buoyancy to her main deck's edge, 1133 t in a full hold), where
    it stands inside, and its free surface, which eats her metacentric height (1.26, from her roll period): she lists
    toward her heel (or the side it came in), 2° at 100 t, 12° at 300 t, lolls past that, at most 35°; her roll is slower
    and more damped (only the roll: the server's heave and the clients' agree); speed and turning ÷ (1 + 2 × water /
    displacement). Pos.Y is her still-water level − her sinkage + the heave.
  - **Downflooding:** once the waist's deck edge (a fifth as freely, through the scuppers), its rail, or a hatch or gate
    in the main deck standing open is under the local wave surface, the sea pours in (1.7 × length × depth^1.5, half of
    it below): foundering.
  - **Sinking** (`SinkStage`: afloat, foundering, sinking, sunk): her reserve buoyancy gone she settles till her deck's
    edge is at the water, then goes down, faster and faster (up to 1.5 blocks/s), over to 30° and 6° by the head, till
    her hull samples meet the bottom: `ShipWreck.OnSunk` (the wreck's hook). Going down, everyone aboard is unmounted
    into the water, the boats on her gallows are spawned as the entities of their items beside her, spawns bound to her
    end, and Sea's dry cut-out, her dry places and the keep-out for swimmers stop. On the bottom she's frozen; only
    her chests and cargo can be opened. On the bottom in shallow water with her deck's edge above the still water she
    isn't sunk: she sits upright on her floor, foundered, and floats off when pumped out.
  - **Pumps:** a `pump` fixture; the client sends a stroke a second while right-click is held at it; each stroke keeps
    that player counted for a stroke and a half, at most two at a brake; 10 litres a stroke. Its brake is a moving part
    (source `pump:<fixture>`) that rocks while anyone pumps. The server syncs what the pumps take out (`PumpedRate`,
    nothing while the hold is dry) and which are worked (`PumpsWorked`); the client (`PumpClient`, geometry in the pure
    `PumpJet`) gushes vanilla's watering-can water from each worked pump's `spout` along `spoutDir` (fixture fields; else
    0.65 over it and 0.45 to its right) onto the deck, in gushes with the brake, and plays `effect/water-pour` once a
    stroke.
  - **Repairs** (`ShipRepairs`): the held item picks the job; the client counts 4 s of held right-click on the same part,
    then asks the server, which checks the reach (below the water: aboard, or a diver with his eyes under it) and takes
    the items (the hand first).
  - **Wear:** game hours while she's loaded and afloat (a jump of more than an hour is left out).
  - **Drawn** (clients): each hull panel's look and real holes, torn and shredded sails, the water in her hold (see
    Rendering); the look-at box says what state a part is in.
- **Guns** (config `Cannons`; `src/Ship/ShipGuns.cs`, `Cannons.cs`, `CannonShots.cs`, `src/Client/CannonClient.cs`):
  - **The model** (`tools/shipgen/gun.py`, placed by `carrack.py`'s `guns`): the barrel a lathe profile (base ring,
    reinforces and their rings, chase girdle, muzzle swell, the bore dark inside it; trunnions and rimbases, the
    cascabel), the carriage stepped cheeks with iron cap squares, a transom, two beds (on the axletrees, and a stool bed
    on the deck the breech comes down onto at full elevation; no quoin since 0.3.6: a wedge can't follow the barrel from
    5° down to 15° up without the cascabel coming down into it), two axletrees with four solid trucks; vanilla tin bronze
    plate for the barrel, debarked oak and oak planks for the carriage, iron. Since 0.3.8 (the user's note 45) the barrel
    is 2.34 long (a cut demi-culverin, `gun.SHORT` of the 3.0 before), the carriage likewise. The breeching from two
    ring bolts beside the port to an eyebolt on each cheek's after end, and from there round the cascabel. Run out, the
    carriage's fore end is close to the side (the trunnions half a block in from the inside planking, the muzzle 0.45
    out). Each gun is a fixture of type `gun` (its muzzle, the level way it points
    `out`, `elevation` (a new ship's lay), `material` "bronze", `bore`, `shot` in kg; `box` where it stands, solid, run
    out; and for laying it (0.3.6) its `pivot`, `trunnion`, `seat`, `trainLimits`, `elevationLimits`, `trainRate`,
    `elevationRate`, `port` and `wall` (the offline checks'), `gate` and `trainLimitsGateOpen`) and its moving parts,
    each riding on the one before (`parent`, composed by `PartMotion.Place`): `<gun>-train` (rotate about the upright
    through its port, source `train:<gun>`, degrees toward the bow; no mesh of its own), `<gun>` (the carriage: slide 1.1
    blocks inboard at value 1, source `fire:<gun>`, along the way it's laid), `<gun>-barrel` (rotate about the trunnions,
    source `elevate:<gun>`, degrees up), the trucks (rotate, their angle the recoil over the truck's radius, so they
    roll) and the breeching (`follow` ropes: `<gun>-breeching-a/b-out` from the ring bolts to the cheeks' eyebolts,
    following the carriage, and `<gun>-breeching-a/b` from the eyebolts, their `anchorFollow` the carriage, to the
    cascabel, following the barrel). The ports are notches cut
    through the waist's bulwark from 0.25 over the deck up through the rail and its cap (planking outside and in, the
    cap cut square and closed at its ends, the collision's shell) between frames, lined with oak; no scupper under a
    port. A "shot" group in the mesh is a cast iron ball at her origin, not drawn
    with her: the clients draw the balls in flight with it.
  - **Laying** (0.3.6; `src/Ship/GunLaying.cs` pure, `ShipGuns.cs`): holding right mouse by a gun (`GunButtons`; its
    breech within 2.5 blocks with nothing else in reach in the crosshair, it in the crosshair, or at its gun captain's
    place) the client sends a `GunLayPacket` (and again twice a
    second while it's held; let go, the last one says so). The server (`EntityShip.StartLaying`, within 3.5 blocks)
    takes the player's view (their `Pos.Yaw` and `Pos.Pitch`, which their client sends 15 times a second, mounted or
    not: see docs/engine-notes.md), turns it into her frame as she really lies (her yaw and true pitch and roll), and
    the lay that points the barrel that way (`GunLaying.FromView`) within the gun's limits is where its crew lays it
    (`GunState.Aim`); every tick the lay (`GunState.Lay`) goes toward it at the crew's rates (`GunLaying.Step`). Let go,
    gone off or out of reach, the gun stays as it is. The lays are saved and sent in WatchedAttributes `magelanGuns`
    ("lay": 4 bytes a gun, hundredths of a degree), marked dirty five times a second while one moves and once when it
    stops. Clients ease their drawing of each gun toward the sent lay at a little over its rates; the player laying it
    lays their own drawing at once from their own view (prediction, the same sums) and is drawn back toward the server's
    only when further off than its lag explains. The limits keep it clear (the offline checks): a gun trains 12° either
    way, less toward a gun 1.5 away (7.25°: both trained toward each other and run back, they'd meet) or an entry port's
    gate swung open (0.25°; the gate won't open while the gun's trained further:
    `EntityShip.GunInGateway`); 5° down (the muzzle just clears the sill as it runs back in through the port) to 15° up.
    The gun captain's place (`GunSeat`, Ctrl + right-click a gun): standing just behind the breech, facing out along
    the gun (a fixed facing: turning it with the gun would turn the view and lay it further), moving with it as it's
    trained and stepping back as it runs back at him; Shift leaves it at that spot.
  - **Firing** (server, `EntityShip.FireGun`): right-click (a `FixturePacket`; the client sends one per click, not while
    it's held, and not while it knows the gun is reloading) or `/magelan fire`. It's ready when its cooldown
    (`CannonDef.Cooldown`, 8 s) has passed; the loading steps (powder, wad, shot, rammed, run out, primed) will go in
    `GunState` and be asked for there. The muzzle and the barrel's direction come from its lay through her frame as she
    really lies (`GunLaying.Muzzle`: the barrel turned about its trunnions and the gun about its upright, then her yaw,
    and her true pitch and roll: the waves', her heel, list and trim; the server keeps her level for walking, not for
    this), with her own way added to the ball's.
  - **The flight** (`Ballistics`, pure): gravity (9.81, a block is a metre), a v² air drag (0.0006 per block, a 9-lb
    ball's own), fixed steps of 1/60 s on every side (so the server's ticks and a client's frames give the same path). The
    ball leaves at 60 blocks a second (a fifth of a real gun's, so it's seen); its energy is ½·m·v² × `EnergyFactor`
    (20: a real ~270 m/s shot's), 148 kJ for the 9-lb ball at the muzzle. The numbers are the design's `cannon` section
    (`CannonDef`, defaults in C#).
  - **What it meets** (server, `CannonShots`, every tick): each step's stretch of its path is tested, nearest first,
    against the ships (their collision as they really lie, `EntityShip.ShotHit`; not the one that fired it in its first
    6 blocks), creatures and players (their selection boxes), blocks (their collision boxes, cell by cell along it) and
    the sea (the water column's top plus Sea's waves). A ship: `EntityShip.Damage(point, direction, energy, 0.12,
    "cannon")`: the plank it strikes takes up to its 80 kJ, and a shot that holed it goes on through her voxels (3a's
    penetration) with `DamageDef.ThroughKeeps` (0.75) of what's left when her planking took some of it (tumbling, its
    splinters: the 144 kJ ball from 30 blocks holes her near side and leaves her far side cracked, hurt 0.60, where it
    was holed before; a second one holes it, and so does one in through the first one's hole); if it comes out of her (`Pierced`, `Exit`) it flies on from there with what's left, after striking
    whoever stood in its way inside her. A creature: vanilla blunt damage, half a hit point a kJ, from the one who fired
    it (vanilla's projectile rules: PvP and the attack privileges), and it goes on 25 kJ poorer. A block: it stops, with a
    puff of the block's dust (or `CannonsBreakBlocks`: up to 3 soft blocks, 15 kJ a point of resistance, through the
    land claims). The sea: a splash; under 6° it skips off (rare from a ship's waist: a level shot comes down at about
    7°), else it goes in with 0.6 of its speed (`WaterEntry`: a real ball's blow on the surface), the water's drag (0.45 a
    block, `WaterDrag`: a real 9-lb ball's 3·Cd·ρ water / (8·ρ iron·r) is about 0.42 a metre) spends it within about 2
    blocks, and below 13 blocks a second (`Spent`, 7 kJ: it can't hurt planking any more) it sinks. (Until 0.3.7 it kept
    0.9 of its speed, 0.3 a block, spent at 6: it went on 3 to 4 blocks as if in the air.) Every step of it is recorded
    (`CannonShots.Records`: the self-test reads them).
  - **Clients** (`ShotPacket`: fired, going on from here, stopped; to players within 320 blocks) fly the ball in the same
    steps between packets and draw the design's "shot" mesh there (`ShipRenderer.DrawShots`); a fired gun's ship notes
    when (`GunFiredAt`) and its moving part runs it back (`PartMotion.Recoil`: back in 0.18 s, held 0.6 s, run out over
    3.5 s); a point light flashes at the muzzle for a seventh of a second. The boom (`CannonDef.Report`: vanilla's large
    explosion since 0.3.8, a mono sound, so the game places it at the gun and it fades with distance, heard out to 200
    blocks; the medium explosion before it is stereo, which the game plays unplaced, as loud everywhere), the spurt of flame, the smoke (white-grey quads growing to a few blocks, blown out along the barrel
    and off with Sea's wind, fading over 5–7 s), the splash (vanilla's water splash particles and large splash sound),
    the splinters and the dust are vanilla particles and sounds from the server, which reach every client that has the
    chunk.
  - **The gun as a block** (`magelan:gun-bronze-<side>`, `blocktypes/gun.json`, the shape `shapes/block/gun-bronze.json`
    written by `gun.py`): the same gun and carriage of boxes (octagons for the round parts), placeable facing the way you
    look, for a wreck to give back; no use yet.
- **Wrecks** (config `Wrecks`; `src/Ship/Wreck.cs` builds one, pure and tested offline; `WreckCells.cs` is a block's
  cells, its faces and the drop table; `BlockWreck.cs` the block and its block entity; `ShipWrecks.cs` makes a sunk
  ship's wreck on the server):
  - **The block** (`magelan:wreck`, `blocktypes/wreck.json`; class `magelan.BlockWreck`, block entity `magelan.Wreck`):
    a 4 × 4 × 4 grid of 0.25 cells, each empty or of a material (weathered oak, tarred oak, pine, copper, iron), kept as
    layers (a material and its 64-bit mask; one ulong for a block of one material) with the wreck's id. Its collision
    and selection are its cells merged into boxes. Clients draw it from its block entity: each cell's faces that no cell
    is against (in the block, or in a wreck block beside it) merged into rectangles of one material per slice, each
    handed to the game as vanilla's `ModelCubeUtilExt.AddFace` does for shapes and microblocks, the material's vanilla
    texture laid as on a vanilla cube (a small face shows the part of the texture its place on the block would), so the
    chunk tesselator lights it and culls a face against a solid block like a JSON block's. Textures: aged planks
    (weathered oak), very aged planks (tarred), aged debarked wood (pine), tarnished copper (verdigris), rusty iron. It
    isn't a microblock (vanilla's chisel works only on its own chiselled blocks and on plain cubes) and says
    `canChisel: false`; it drops part of its raw materials (`WreckDrops`), never itself; wood to the tools unless mostly
    metal. It isn't in the creative inventory or the handbook.
  - **The builder** (`WreckBuilder.Build(WreckInput, IWreckSite)` → `WreckPlan`, pure): from a design, her origin in the
    world, her yaw, list and trim, each damage part's hurt, whether she's clad, a seed, the fixtures that held
    containers and how many extra chests. Her design's cells first: what's over her castles (masts and tops) and forward
    of her stem (the bowsprit) goes, the masts above a stump (0.5–1.5 over the main deck); the outermost cell of her
    planking (`ShipPartMap.IsOuterSkin`) is tarred (copper under her waterline + 0.75 if clad), the rest oak, the mast
    stumps pine. Then broken up, all from the seed: decay by an evened-out noise (coarse along her length, so planks go
    in runs), 3% anywhere, 13% for her woodwork inside, from 17% over the main deck rising 7% a block; one castle (the
    stern castle or the forecastle) nearly all gone from its deck up (85%) and a third of its walls under it; holes as an
    ellipse on each holed panel's outer face (0.5 × 0.35 to 0.95 × 0.65 as she's stove in, ragged), reaching 0.9 in
    through her planking and frames, smaller ones in 40% of her cracked panels, holes in holed decks, and 2–4 panels of
    her sides stove in at random. Then laid in the world: every world cell (0.25) whose middle falls in a cell of hers,
    turned by her pose (offline: her cells are kept exactly at right angles, within 2% at others), settled 0.3 into the
    seabed; nothing in a ground block (anything solid with collision that isn't a plant: rock, soil, sand, someone's
    blocks, another wreck) and nothing under the seabed's surface (the top of the first ground block down her column);
    her own blocks from a run before are free. Her masts above their stumps lie on the seabed as two or three level
    pine timbers each (at most 24 blocks of each mast, tapering), from her side outward to the side she lies over to,
    across her give or take 40°, a small kink and gap between them; her anchor (shank, stock, arms, flukes) in iron
    on the seabed outboard of her cathead. Cells left with no neighbour go. Then places: each container where it stood
    (the block its middle is in, if it isn't ground, isn't buried and has at most 24 of her cells, which go), else the
    nearest free block on the seabed beside her; each gun on her deck if its block has at most 16 cells and the blocks
    either side along its barrel 8 (the gun's shape is 3 blocks long), facing out of its port, else on the seabed 2.5
    outboard of its port, facing anyhow with room for its barrel; the extra chests on the seabed beside her middle on
    her low side; sand at up to 24 free blocks on the seabed against her hull's lowest blocks, outside her.
  - **On the server** (`ShipWrecks`): `ShipWreck.Sunk` starts it (and a look every second finds sunk ships that aren't
    wrecks yet: after a reload, or with `Wrecks` switched on since). She's lowered till her hull meets the bottom (if
    she stopped in sea that wasn't loaded), and her wreck is worked out with the world as its site, which stops at an
    unloaded chunk (tried again 2 s later): so only with every chunk it touches loaded. Anyone whose box would be in her
    cells is moved up out of them. Her blocks go down bottom up, 400 a tick (`SetBlock` with a stack whose attributes
    carry the cells; the block entity reads them as it's placed, also in worldgen); water stays in the fluid layer.
    Then in one tick: the registry entry (saved with the world, `magelanWrecks`), her cargo taken out of her and she's
    removed (with nothing in her nothing drops), her containers placed (a chest or vessel as itself, filled; a trunk or
    crate: an aged chest for its contents, the trunk with the loose things), the loose things in aged chests (16 a
    chest), her guns, the sand (the seabed's own block, only on sand or gravel); anything left dropped beside her. A
    save in between finds her with her cargo and some of her blocks; made again, the same seed and input make the same
    wreck over them. `ShipWrecks.Place(WreckInput, loot)` makes one that was never a ship (worldgen, `/magelan wreck
    here`), with a loot list (`WreckLoot`: a fixture's container and its contents, or loose things).
  - **For the Earth map** (later): build a `WreckInput` (a design from `MagelanMod.GetShip`, a place on the seabed: her
    origin about `seabed − Grid.OY` so her keel rests on it, a yaw, a list and trim, a hurt per damage part, a seed, the
    cargo fixtures to fill) and a loot list, and call `ShipWrecks.Place` once the chunks there are loaded (it waits for
    them); or, inside worldgen, `WreckBuilder.Build` with a site over the generating chunks and the plan's blocks set
    with the worldgen block accessor and `BlockEntityWreck.Stack` (the block entity reads `stackForWorldgen` when it's
    initialised).
- **The firehearth** (config `Hearth`, 0.3.6; `src/Ship/HearthFire.cs` the fire, pure and tested offline; `ShipHearth.cs`
  on the ship; `src/Client/HearthDialog.cs`, `HearthClient.cs`, and `ShipRenderer`): vanilla's firepit only works as a
  block in the world, and her deck is an entity, so it's rebuilt aboard from vanilla's own pieces (no hidden world block).
  The fixture of type `hearth` gives the firepit's place (`pos`, its bottom centre on the firebed, `yaw`), the fire's middle
  (`fire`: the light), where a pot stands (`cook`), the stovepipe's mouth (`chimney`: the smoke) and the room it warms.
  - **Its inventory is vanilla's** `InventorySmelting` (fuel, input, output, the four cooking slots a clay pot opens), kept
    on the ship (`HearthInventory`, id `magelanhearth-<ship>`): vanilla's slots, shift-clicking, liquids in the pot's slots,
    what it will make. A pot taken out with something still in its cooking slots: that goes to whoever has the hearth open
    (vanilla drops it at the firepit, which aboard would fall through her deck).
  - **Its fire is vanilla's burn tick** (`BlockEntityFirepit.OnBurnTick`) ported number for number (`HearthFire.Tick`) and
    run on the server every 100 ms against the inventory through vanilla's own collectible methods (`HearthContents`:
    melting points and durations, `CanSmelt`, `DoSmelt` and the cooking pot's recipes, item temperatures): the furnace
    heats toward its fuel's burn temperature (1 °C/s plus 1/28 of the gap a second), the input toward the furnace and cooks
    when it's been at its melting point long enough (faster the hotter), the fuel burns four times slower when the fire is
    hot with nothing to cook, the next fuel catches when one's done, and out of fuel it goes out; its embers relight new
    fuel for two game hours, then it's cold. The numbers: one raw red meat on firewood cooks 43 s after lighting (150 °C
    after 13 s, then 30 s of cooking), a pot of porridge 47 s, a firewood with nothing to cook lasts 24 s, four of them 198 s.
  - **Lighting** (`TryIgniteHearth`): as vanilla, with fuel in it and not burning, a firestarter or a lit torch (anything
    with vanilla's `CanIgnite` behaviour) held to it more than 3 s: the client counts the held right-click and asks the
    server (`HearthPacket`, one try in 2.5 s a player); a torch lights it, a firestarter one time in four (and a point of
    durability). Right-click otherwise opens it, Shift + right-click puts one in, a pot goes on, a bowl is served
    (`UseHearth`, vanilla's firepit's clicks).
  - **Saved and sent:** WatchedAttributes `magelanHearth` (the inventory and the fire by vanilla's own keys), sent when a
    slot changes, when it lights or goes out, and twice a second while it burns or its heat changes (as vanilla's firepit
    sends itself); `magelan` carries whether hearths are on.
  - **The dialog** (`HearthDialog`): vanilla's firepit dialog rebuilt (it talks to a block position): the same layout,
    flame, arrow and readouts, from the values vanilla's firepit gives its own (`HearthDialogValues`); its slot clicks go to
    the ship like the storages', and it closes when you walk away.
  - **Drawn** (`ShipRenderer`): the firepit's own shape for its burn state and what's cooking
    (`block/wood/firepit/{lit|extinct|cold}-{normal|spit|wide}`, a cold one with no fuel as ashes), built again when that
    changes; a pot in it as vanilla's `PotInFirepitRenderer` draws one (the lid rattling as it boils), a cooked pot's meal, a
    joint on the spit by its `inFirePitProps`. Its light: the lit firepit's own ([7, 7, 16], on the lanterns' scale),
    flickering, a dim glow of embers. `HearthClient`: the lit firepit block's own particles (flames, embers, smoke; round a
    pot, vanilla's ring) cloned and spawned on vanilla's particle thread at the firepit as she's drawn, moving with her (the
    smoke under the hood lives 3.5 s, not 18, so it doesn't rise through her deck); smoke out of the stovepipe's mouth
    drifting with Sea's wind (0.7 of it); vanilla's looped crackle and a pot's bubbling following it.
  - **Warmth** (`ShipShelter.HearthHeat`, in the body temperature patch, cabins on or off): vanilla's firepit's heat (10
    burning, 0.25 embers) by vanilla's falloff from its block, for whoever is in its room while that's a closed room, or
    else within 3 blocks of it.
  - **Sinking:** going down, the sea puts it out (cold, no embers). Its contents go with her loose things into her wreck's
    chests (`ShipWrecks.LootOf`), and drop if she's removed.
- **Older saves:** a ship saved before this version keeps what was in its old built-in chests: those things are
  moved into the hold's old storage when it loads, which the hold hatch opens while it isn't empty. So is a chest,
  vessel, crate or trunk put on a place the design no longer has (since 0.3.6 the hold's ladder comes down where
  the cargo places at x 5 and 6 were), with what was in it.
- **The surveyor's chalk** (0.3.6; `src/Client/SurveyorsChalk.cs`, the dialog `ChalkNoteDialog.cs`, the item
  `src/Items/ItemSurveyorsChalk.cs`; the notes' model, files and frame maths `src/Client/SurveyNotes.cs`, pure and tested
  offline):
  - All on the client: nothing goes to the server, and the notes are the player's own. The item itself only stops
    vanilla swinging, breaking, placing or using anything with it; `ShipClientSystem`'s world-click handler gives every
    click with it in hand to the chalk first (before the guns, fixtures and boats), once a press (the game repeats a
    held click every quarter second).
  - **Capture** at the click: the game's own selection says what the crosshair is on. On a ship, her
    `EntityShip.RayHit` from the eye along the view gives the point in her frame and the world, the face's normal in her
    frame (`ShipRayHit.LocalNormal`) and the fixture; the part's name is `PartText` of the selection's part
    (`ShipPart`), its damage `DamagePartFor` / `StateOfPart` / `PartDamage` / `LeakEstimate`, the look-at box
    `GetName()` + `GetInfoText()`. The ship's motion is as drawn on this screen (`Heave`, `Pitch`, `Roll`, `Heel`,
    scaled by the player's Sea motion setting) and as she really lies (`TruePitch`, `TrueRoll`); the wind and the waves
    are Sea's at her (`WindAt`, `SeaAt` × `WaveFactorAt`, `WaveHeightAt`). Numbers are rounded to 3 decimals (angles
    to 2).
  - **Files** (`SurveyNoteFile`): the whole JSON array is read and written each time (written to a temporary file and
    swapped in); camelCase keys, a point on one line; the Markdown list is written next to it. A file that can't be read
    is set aside (`magelan-notes.json.unreadable-<time>`, kept) when the next note is written. The ModData folder is
    `api.GetOrCreateDataPath("ModData")`.
  - **The screenshot:** the game's F12 hotkey handler (`GetHotKeyByCode("screenshot").Handler`) is called 0.25 s after
    the window closes; the game saves the next frame to `GamePaths.Screenshots` as `yyyy-MM-dd_HH-mm-ss.png`. The newest
    PNG written there since is looked for for 6 seconds and named in the note (else the note says none was found: match
    one by its time).
  - **The marks:** a 32 × 32 chalk X drawn on a quad 0.3 across, 0.02 off the surface along its normal (its strokes along
    the diagonals: level ones on a wall), lit full, and its number as a text texture turned to the eye above it, drawn
    with the standard shader in the opaque pass while the chalk is in hand (marks within 64 blocks, in this world and
    dimension). On a ship the point is put through her pose as the renderer draws her (`ShipRenderer.RenderOrigin`, her
    yaw, drawn pitch and roll: `NoteMath.ShipMarkWorld`). A mark is in the crosshair within 0.22 blocks of the view ray
    (more further off) and not behind what the crosshair is on.

### Decorating: mount points, pieces, paint

Since 0.3.8 (`src/Ship/ShipMounts.cs`, `ShipPieces.cs`, `VanillaPieceBlock.cs`, `ShipPaint.cs`, `ShipDecor.cs`,
`src/MagelanDecor.cs`, `src/Client/DecoClient.cs`, `PieceRenderer.cs`, `PaintRenderer.cs`).
- **Mount points** (`MountLayout`, worked out once per design from the definition alone, so the server and every client
  agree): floor places are whole blocks on each deck (not the main top) on the alignment of the 0.25 collision grid that
  gives that deck the most (16 tried; ids `f:<deck>:<i>:<k>`), with solid floor under all of it and a block of room
  over it, outside everything kept clear: doors (a block each side), gates' swing (1.5 inboard), hatches (0.75 round),
  each gun's room (from its port to behind its gun captain's place, as wide as its trail sweeps trained as far as it
  goes), the capstan (a ring of 1.9), the pump (1 round), the helm's standing spot and the binnacle, the hearth, the
  plate and its spawn spot, ladders and a block and a half round their ends, boarding points, masts (radius + 0.35),
  every other fixture's boxes, the design's `stairs` and `keepClear` boxes (windows), and any cell within a block of
  something standing on air a little over the deck (a stair's tread). Wall spots (`w:<room>:<p|s|f|a>:<i>:<row>`) are in
  the closed rooms, along her sides (the hull's inside, found from outside in) and their ends (bulkheads, the transom),
  in rows 0.75 and 1.75 over the floor where the room is high enough, where the wall is even within half a block over
  the spot (sampled 3 × 3) and the 0.6 in front of it free; the wall's lean and turn (a least-squares plane through the
  samples) tilt what hangs there. Hooks (`h:<code>`) are the design's `hooks` and an older design's lantern places.
- **Pieces** (`ShipPiece`, saved in WatchedAttributes `magelanPieces`): what was put (one, given back), the blocks drawn
  (a bed's head and feet), each with the corner of its cell and its quarter turns, the places taken, its selection and
  collision boxes (ship-local, turned with it), and its role. `PiecePlanner` places a block as vanilla would at that side
  of it: HorizontalOrientable (and any side variant): the side the placer looks toward; HorizontalAttachable: the side
  toward the wall (walls only); OmniAttachable: up on a floor, down from a hook, on a wall away from it; a bed: feet on
  the place, head a block on, `bed-<wood>-head/feet-<side toward the placer>`; vanilla's Multiblock cells for the variant
  (a trunk's two, a 2 × 1 painting's); a typed container's or crate's mesh turned to face the placer as vanilla's mesh
  angle does; anything else turned to face them. The server checks the reach (picking range + 1.5), that every place is
  free and nothing's in the way (other pieces' boxes, her collision, someone standing there), and the block in hand.
- **What pieces do:** a container gets a storage (`InventoryGeneric` of vanilla's slot count, among her storages as
  `piece-<id>`, saved in `magelanInv` like the hold's, opened in the same dialog through a stand-in fixture whose packet
  ids are `(0x10000 + id) << 11`); a bed a `BunkSeat`, a chair a `PieceSeat` (sat on as on a block's edge, legs over its front; the heads too; 0.3.13, note 139: the sitter showed standing, as the game starts a seat's animation before the seat knows who sits there: the sitter's own `sitflooredge` is started as they sit and stopped as they get up); a light gets a point
  light of its block's own colour and level; every piece's collision boxes go into her collision grid. The crosshair
  (her `IntersectsRay`) meets a piece's selection box in front of her (the part `ShipPartKind.Piece`, the look-at box
  names it).
- **The client** (`DecoClient`): each frame the nearest of a piece, a free mount point (holding a block; the stern
  lantern's hook even empty-handed) and a paint zone (holding dye, gold plates or a knife), if nearer than any fixture,
  is the target: its hint above the hotbar, its outline (`WireframeCube` in her drawn frame), its clicks (`DecorPacket`).
  Holding left mouse on a piece for 0.6 s asks the server to take it up.
- **Stacking** (0.3.9; fixed in 0.3.12, note 124): over each floor place, a block up and two, where she has a block of
  room, a stacked place (`f:<deck>:<i>:<k>:<level>`) that holds while the piece under it has a top to stand on: solid up to
  three quarters of the block over at least half of it each way (`ShipPieceSet.MinTop`; it was 0.7, and vanilla's
  cabinet is 0.6875 deep, so nothing could go on a cabinet).
- **Partitions, doors and closed rooms** (0.3.9; `ShipCompartments`): on the grid lines of the closed rooms' floor cells,
  border places a block high (row 0 on the floor, row 1 over it, under the deck structure's band): planks there are a
  partition 1/8 thick (its collision the quarter block on its anchor side), a vanilla door takes both rows and opens.
  Within a design room that's open (its hatch or door standing open), the space partitions, shut doors, her sides and
  bulkheads close off is a closed room (weather, warmth): a flood fill of her collision from the floor two blocks up.
  Since 0.3.12 (note 123) a partition counts as reaching her without panels: a line ending at her side is carried on
  through the wedge to her planking (each voxel row met within 2.5 blocks), also through the next floor cell along it when
  her side or a timber cuts into that cell's border (`CutByHer`: no partition can go there); and where no partition can go
  over one (the top row, a beam, her side leaning in over the outermost cell) it's closed up to her timbers over it, each
  voxel column to the first solid within 1.25 (`CloseToCeiling`). An empty border place over a partition is an opening.
  (0.3.13, note 137: "cannot still fill till ceiling timber partition walls, also cannot do it to curved hull") Border
  places also go up under her beams and deck (a top row, row 2, up to the room's top) and out along a partition line
  past the last floor cell to her curved side (the wedge cells, up to 3), where the line's quarter-block layer is only
  partly free of her: such a place's `Clip` is the free part (its voxel columns' runs, `MountLayout.ClipRuns`; at least
  an eighth of the block), highlighted, picked and solid there only; planks there are drawn as the planks' cube laid on
  the whole block and cut to it, its texture where a whole one's would be (`PieceRenderer.ClipMesh`), so a wall meets her
  timbers and planking with no gap. They seal as partitions do and are painted as they are; no door goes on one. A row
  over a cut row, or over her, needs nothing under it. One place per border segment from either side.
  (0.3.14, notes 147–149) Her collision is on quarter blocks and covers each timber's cells whole, so a cut partition
  stopped up to a quarter block short of a beam's side (note 148: "a small gap around beams") and a whole one a cell short
  of her ceiling (note 149: the wedge cell beyond was her planking, and the "free" cells past it the air outside her side:
  a free cell must now be inside her, between her first and last cells across her, `MountLayout.InsideHer`). A
  partition is now drawn out to her real surfaces (`MountLayout.DrawnClip`, 1/32 block cells): each free run's end is
  walked on, up, down and along, by rays at both faces of the partition into her cells next to it, to the farthest of
  her surfaces met within a block (`ShipSurfaces`: her mesh file's interior and planking, and her shapes' faces, built on
  the client once the mesh file is in; her pieces are built again then), past the place's own block where her cells go
  on; then once more a little from what that drew (the corner where a deck's edge meets her side). Where none of her
  surfaces is in her cells (a deck drawn on them) it ends at her cell. Offline: no slit over 0.02 between the lower
  deck's beams by the user's spot, at most 0.075 at her side there (the deck's edge, a knee: the partition's two faces
  meet her surface at different heights), the slits over 0.02 overall down from 46 700 edges to 10 400 (the rest where
  her cells have nothing of her mesh in them: a fixture's collision, the stem's deadwood). Picking (note 147): a partition
  goes up over a door (the door is the row under it); with planks in hand the crosshair near an edge of a partition or a
  door put up (within a quarter block, or on its thin end) takes the free place past that edge on its line
  (`MountLayout.NextToPiece`), and on her timber a partition would butt against (a beam, a frame) the border place
  nearest where it meets her, within 0.4 (`MountLayout.NearTimber`).
- **No water specks in the hold** (0.3.13, "there are still air bubbles from water in the cargo deck"): Sea's
  `DryParticles` left out vanilla's water specks spawned inside her, but a speck lives two seconds standing still in the
  world's water while she sails, rolls and heaves through it: those spawned ahead of her bow, beside her flaring sides or
  under her keel were inside her hold a moment later. Sea now asks whether a speck's place will be dry at any moment of
  its life (`IDrySpaces.IsDryWithin`, Sea 0.3.5): Magelan answers from where she'll be each quarter second (her way over
  the ground: speed along her heading and leeway) against her inside grown half a block every way for her roll and heave
  (`ShipDefinition.DrySweepMask`, `DrySweep.SweptDry`). Water in a flooded hold still has its specks.
- **Doors put aboard** (0.3.12, note 123): picked from either side (a border piece's sheet is on its line, its collision
  a quarter block to one side: from that side the collision came first and the door couldn't be clicked), and drawn shut
  and swung about its hinge as it opens and shuts, eased as the design's doors are (`Piece.DoorId`, `d.Open["piece:<id>"]`).
- **Painting walls put aboard** (0.3.12, the user: placed walls can't be painted): a partition, a panel or a door
  (`ShipPiece.Paintable`) is painted like her zones with dye in hand (right-click; a partition's two faces, 2 m², a door's
  4, a panel's 1: `PaintArea`), scraped back with a knife, not gilded; saved with the piece (`paint`). Drawn from its
  block's mesh with each texture swapped in its UVs for the paint's (`PieceRenderer.PaintedMesh`: any wood's planks take
  oak's plank grain, anything else the timber grain, times the dye).
- **Modular pieces** (0.3.16, chalk notes 154–161; the contract with the generator: `docs/modular-pieces.md`;
  `ShipPieces.cs`, `ShipMounts.cs`, `ShipDecor.cs`, `src/Client/ModularRenderer.cs`). The user: cabin fronts, windows,
  stairs and railings as placeable structures of the partition's thickness, so each player chooses, and chests go
  right up to them (the permanent walls are thicker). What's in hand makes what:
  - a **glass pane** (vanilla's leaded pane) on a border place is a **window** (note 154): a block-sized glazed panel 1/8
    thick, its casing inside the block in the pane's wood, lead cames on a rectangular grid of 5 × 5 small panes
    (~0.18), the ship's clear glass (drawn as her glass is); it seals as a partition does;
  - a **wooden fence** on a border place is a **railing** (notes 154, 155, 157): a balustrade a block high (half posts at
    its ends, so two side by side make one, four balusters, a handrail and a foot rail); solid as a partition, but it
    doesn't seal. On the border along a placed stair's side (its deck, row 0) it's **raked**: drawn and solid following
    the treads, its handrail 0.95 over their nosings (`ShipPiece.Rake`, worked out on the server whenever her pieces
    change);
  - **plank stairs** on a floor place are a **2 × 1 stair** (note 154): the place and the next cell away from whoever puts
    it, climbing to the deck above (the lowest deck 1–4.5 blocks up at the step off its head; a design's entry may give
    `rise`), 8 treads a voxel thick on two stringers (tops on the quarter grid: 0.5 0.75 1.25 1.5 2 2.25 2.75 3 for a rise
    of 3, every step ≤ 0.5, walked as any stairs by the deck mover), open under it: the head cell's own floor place stays
    free for a chest under its high end (note 158). It takes the places over both cells and needs a man's height over
    every tread and over the 0.6 behind it: the well over it must be open over both cells and the one before its foot;
  - a **plank slab** on a horizontal place is a **plank panel** (note 159): 1/8 thick under its plane, over each floor cell
    a block and two up in a closed room (a shelf: things stand on it, the stacked place over it holds while it's
    there), and three up where the cell is a well's (its top closed flush with the deck above). It seals.
  Rules (note 160: "cannot remove bottom partition wall if top is still on"): a border piece needs nothing under it, it
  hangs from the beam or deck over it: the bottom partition (or the door) comes down with the top one on, and any row may
  go up first. Wall spots on placed partitions (note 159: "Partition walls should accept paintings, wallpapers,
  lanterns, shelves"): each whole border place has one on each face (`w:b:<border>:+|-`, keyed along its line so a
  2 × 1 painting spans two partitions), a place while a partition stands there; the partition doesn't come down with
  something hung on it. Ceiling hooks (note 161): under the deck over each floor cell of a closed room where the
  underside is flat within 0.2 of the cell's middle (between the beams) and at least 2.25 over the floor (`h:c:<deck>:<i>:<k>`;
  440 on the carrack); a lantern on one gets the iron eye the design's ceiling hooks have (a plate and a ring). Border
  places are also on the open decks now (two rows; the closed rooms' places come first, so their ids are as before).
  The chest's look (note 159: "chest is placed bit differently if partitions are placed before the chest"): a piece
  faced whoever put it from the place's middle, so a player standing off to one side (beside a partition) got a chest
  turned side-on (its side's handle where the lock should be); it's now from where the crosshair meets the place, as
  vanilla turns a chest. Painted as partitions are (a window's casing 0.5 m², a railing 1, a stair 3, a panel 1).
  Sealing: windows, shut doors and panels close; railings, stairs and open doors don't (their voxels are cleared in the
  flood fill's copy). A design room flagged `pieces` (its walls partly placed pieces) is sealed only when they close it
  (a flood fill every time, never taken as sealed from the design); a design's `wells` opening onto an open deck leave
  the room under them open unless panelled over. A design's `modularPieces` are laid on a new ship, and once on an older
  one of that design where their places are free (`magelanLaid` remembers what was laid, so what the player takes up
  never comes back); an entry with no place is logged.
- **0.3.17 follow-ups for the modular carrack** (`carrack-m`; `docs/modular-pieces.md` §6): fixture margins stop at
  their closed room's walls, and wall pieces' places are kept clear of a fixture's own box only (`KeepClear.Core`,
  `MountLayout.ClearHitCore`); on an open deck a cell kept clear by a margin or a fixture in it still anchors places at
  the deck's edge (`marginCells`: the quarterdeck's front by the helm and binnacle); touching boxes don't overlap (0.001);
  open decks get wedge (partial) places out to her sides, and a railing may stand on a partial place; a door entry's
  `into`; raked railings solid only up to the deck their stair climbs to, on the side away from it unless that's a well
  (`PiecePlanner.ApplyRakes`); in the sealing flood fill only a railing's or stair's own voxels are cleared (not her deck
  nor a partition beside it); zones with no area aren't listed or painted. Offline: `dotnet run -- design carrack-m` runs
  the modular design's own checks (entries placed and all laid, doors and their swing, every stair walked up and down
  with `DeckMover.WalkStair`, wells, the piece-walled rooms sealed with the doors shut, a door opening a room); the server
  self-test launches a carrack-m (all pieces laid, rooms sealed, a door opening one, a stair walked, a partition taken
  up staying up after a reload; the self-test config's `Only: "modular"` runs it alone).
- **0.3.18, step A of the slot plan** (notes 162–166 and the user's chat; contract `docs/modular-pieces.md` §7; `AimPlace.cs`,
  `ModularGeometry.cs`, `ShipPieceFire.cs`, `DeckMover.ClimbStair`):
  - **Placing by aim** (the user: "the place search is too aggressive, mid-air walls"): a block in hand is offered only
    the place next to what the crosshair hits, as vanilla does (`AimPlace.Candidates`): her woodwork or a piece, the
    face's normal from her collision (`VoxelGrid.RaycastCell`'s axis) or the piece's box. On a floor: its floor place (a
    stacked one on a piece's top), or with a wall piece the border place on the cell's edge nearest the hit; under a deck
    or a beam: a hook, the top-row border place under it; on a wall: its wall spot, a panel place, the border there, the
    floor place in front; on a partition, door, window or railing put up: the place past its nearest edge (along its
    line, over or under it: a second row on top of the first); on a hearth or a flue: the flue's next segment. The first
    of those the block fits (its plan without an error) wins; nothing among places the ray only passes through. A piece
    used with a click (a chest, a bed, a door, a working block, the hearth) takes a block next to it only with Shift.
  - **Building mode**: planks, fences, glass panes, plank stairs and plank slabs are ship structure (partitions,
    railings, windows, ladders, panels) only with a **saw** in the off hand (`VanillaPieceBlock.HasSaw`, the client's
    crosshair and the server's placing alike); without one they're ordinary blocks (decoration on a floor place), and the
    HUD says to hold a saw when one of them is in hand aboard. The design's laid pieces don't need it. (0.3.19: the saw is gone, building mode is a toggle, below.)
  - **Dye always paints**: a dye container (any liquid container with dye) paints the zone or the piece it's aimed at;
    Shift + right-click puts the container down instead (placing isn't even looked for without Shift).
  - **The companion ladder-stair** (note 162: "steeper, a 2-long well, top and bottom attachments"): plank stairs on a
    floor place are a steep ladder (≈ 63°: feet at the foot cell's middle, hooked over the coaming at the head cell's far
    edge, two stringers, flat treads about 0.3 apart, iron shoes and hooks), needing a 2-cell well; it's climbed as her
    ladders are (`PiecePlanner.StairLadder`: a `LadderDef` of kind `stair` in `EntityShip.Ladders`, walked onto from its
    foot cell or the one before it, stepped off half a block past the well), so the head goes up the well clear of its
    edge. Placing checks the climber's room up the ladder and both landings (`StairClimbRoom`); a stair saved by
    0.3.16/0.3.17 gets the ladder's boxes on load (`RefreshStair`); railings beside it are raked to the ladder and not
    drawn over the upper deck where that deck has its own railing on the line; taking it up and putting it back works
    (note 162).
  - **Windows** (note 163: "looks like jail bars"): four lights in a wooden mullion and transom, each leaded two by two
    (4 × 4 panes in all), thin lead, clear glass.
  - **Flicker** (notes 164, 166): the modular pieces are drawn from parts (`ModularGeometry`), which the offline tests
    search for faces of two parts lying in one plane: two railings meeting at a corner share one post (the lower id
    draws it, centred on the corner), a railing ending at a wall across it has no post there, the rails stop at the posts'
    faces, two walls meeting at a corner fill its square once, partly free ones under a beam too (`WallEnds`, `AdjustClip`: the one along x stops short, the one along z
    reaches over), the window's cames don't overlap, and a door put aboard swings 86° (its open leaf never in the plane
    of a wall beside it).
  - **The hearth, the oven and the flue as pieces** (note 165): fire bricks (`claybricks-*-fire`; a firepit in creative)
    on a floor place are the ship's hearth (a brick firebed and kerb, vanilla's firepit on it), vanilla's clay oven her
    oven, a brick chimney (`claybrickchimney-*`) on a flue place a segment of the hearth's flue (a brick chute from a
    block over its deck up through the deck above to a block over that; flue places `fl:deck:i:k` wherever the deck above
    is at most 0.75 thick over a cell). One of each per ship; a design's fixed hearth or oven wins. Each gets a stand-in
    fixture (`EntityShip.PlacedHearth`/`PlacedOven`, a piece's packet index) that becomes her `HearthFixture` or
    `OvenFixture`, so the inventory, fire, dialog, lighting, drawing, light, smoke and warmth are the fixed ones' own
    code (`SetHearth`, `SetOven`). The fire rule: a placed hearth draws (can be lit, keeps burning) only while its smoke
    gets out, open air straight over it or up its flue to open air (`HearthFlue.FlueTop`); its smoke rises from the
    flue's top. It's taken up empty and cold, and not from under its flue; a flue segment not while the fire draws.
    carrack-m lays hers (no fixed hearth or oven any more).
- **0.3.19, the sixteenth client test's feedback** (building mode, stairs and ladders, the charter; contract
  `docs/modular-pieces.md` §8):
  - **Building mode is a toggle** (the user: "the saw can't be held in the offhand"). Press **B** (Magelan: building mode
    in vanilla's controls) or type `.magelan build [on|off]`. The server sets it per player
    (`VanillaPieceBlock.SetBuilding`: the player entity's watched attribute `magelanBuild`, kept with them). The HUD shows
    "Building mode (B toggles)" while it's on aboard, the place hints say so, and chat says when it changes. While it's on,
    planks, fences, glass panes, plank stairs, slabs and ladders are ship structure. While it's off they're decoration:
    a floor place, or on top of a piece, which is why planks stack upward but not sideways. The hint now says "Building
    mode off: this is decoration … B toggles building mode". No saw is looked for anywhere.
  - **Two kinds of stairs** (the user: "stairs and ladders; stairs can take more space"):
    - A vanilla **ladder** makes the companion **ladder-stair**: 1 × 2, about 63°, climbed, a 2-cell well. It now has its
      own handrails: stanchions on its stringers and a rail parallel to it, 0.9 over its line (the user: "rails to ladder
      … are wrongly constructed").
    - **Plank stairs** make a **walked stair** (`PiecePlanner.Flight`, role `Stair`): 1 × 3, about 45°, twelve treads
      0.25 apart, walked by the deck mover. Its top is flush with the deck above at its head, so it needs no well up to a
      castle's front (the deck may reach back over its head by 0.25). Through a deck it needs a 3 × 1 well. Its room is
      checked by `GridClash`: a man's height over each tread and over the 0.6 before it, plus both landings. A chest
      fits under its middle and head cells.
    - Saved stair pieces stay ladder-stairs (role 10, now `Ladder`). An old design `stair` entry whose third cell is
      under the deck above is still laid as a ladder (`MountLayout.LaidKind`).
  - **A ladder's guard rail over a drop** (`PiecePlanner.LadderGuards`): where a side of its cells has no deck beside it
    (carrack-m's companion ladder beside the hold well), the ladder has its own guard rail on that line. It is drawn, and
    solid in the drop's quarter-block layer, so nobody steps sideways off its foot into the hold.
  - **Guns' keep-clear** (`MountLayout.GunClear`) is the trail's swept shape, each side to its own train limit, eight
    boxes along the trail instead of one round the whole sweep.
  - **No railings beside a ladder** (the user: they "look wrong; ladders look better without rails"). One can't be put
    there (`PiecePlanner.BesideLadder`), and none is raked to it. On an existing ship the ones beside its ladders are
    taken off once and their fences put in her hold.
  - **Railings beside a walked stair** are raked along its nosings' line, with the handrail 0.9 over it. Their solid
    layer is outside the stair. At the stair's head, a newel post on the corner meets the deck's railing.
  - **The charter's double launch** (the user: "often spawns two on each other", "I release immediately and still get a
    double spawn"; "it spawns much later than 1.5 s"):
    - The cause is the engine. On the server, the client's stop packet replays the use's steps (`ServerSystemBlockSimulation`,
      StopHeldItemUse). The replayed step that ends the use calls `OnHeldInteractStop`, and then the packet handler calls
      it once more, both with the full hold time. So one use launched two carracks.
    - Now there is one launch per use (`LaunchGuard`, keyed by the use's start time `EntityControls.UsingBeginMS`), no
      second launch by the same player within 2 s, and none on a spot where another ship lies or one was just launched
      (`MagelanMod.OverlapsShip`: footprints, the last 10 s of launches).
    - The rowing boat item (vanilla's boat item, which spawns at every start of a use, including while the button stays
      down) gives one boat per press.
    - The delay was the client building her meshes and reading her mesh file (megabytes of JSON) only once she was there.
      They're loaded while a charter is held now (`ShipClientSystem.PreloadDesign`), so she appears when she's launched,
      about 1.5 s after the press.
- **0.3.20, step B of the slot plan: deck cells you can take up** (contract with the generator: `docs/slots.md`;
  `src/Ship/DeckSlots.cs`, `ShipSlots.cs`, `src/Client/SlotRenderer.cs`). Every changeable part of a ship becomes a
  design-defined **slot** with its own geometry; the mod keeps only a state per slot and saves only what differs from the
  design. Step B does it for deck cells, first on `carrack-m` (the other designs have none and work as before).
  - **A deck cell** is one block cell of a deck's planking on her grid (the beams under it stay). The design lists them
    (`deckSlots`: id, deck, box, solids, material, start filled or empty); cells it doesn't list are fixed (under her
    structure, the guns' working room, the masts, the capstan, the pump...).
  - **Taking one up**: building mode (B), an **axe** in hand, hold left mouse on the deck for a second: its planks (one
    planks block) go into the inventory. Not while anything stands on it, hangs under it or passes through it (a piece, a
    fixture of hers, a hatch). **Laying one**: building mode, **planks** in hand, right-click the opening (one planks
    block of any wood; drawn as her planking). Not while something is in it (a ladder through it) or someone stands in it.
  - **Saved** as the differences from the design in WatchedAttributes `magelanSlots` (cell id → state: 1 cracked, 2 out),
    sent to every client.
  - **An open cell** (out, no shut hatch over it) is an opening: her collision has no voxels there (people fall through,
    no piece stands over it); the room under or over it is open unless the other side is one of her enclosed rooms (as a
    well); the rain falls through (her grid); on the main deck out in the open it's a downflooding point like an open
    hatch (`DownfloodPoint.Cell`, the sea over it goes below; a sixth of a hatch toward the deck's share). A design ladder
    whose `gate` is `"opening"` passes while the cell at its top is open.
  - **Hatches are pieces** (`PieceRole.Hatch`): building mode, **vanilla trapdoors** in hand (one a cell), right-click an
    opening: a hatch lies over the whole opening if it's a rectangle of at most 4 × 4 cells, flush with the deck, its
    leaves hinged on the long sides. A solid trapdoor makes a cargo hatch (plank leaves, iron straps, a ring), a window
    trapdoor a glazed hatch (frames, the ship's glass, lead cames). Right-click opens and shuts it (it won't shut on
    someone in the opening); shut it's walked on and closes the opening (rooms, rain, sea); held left mouse takes it up
    (its trapdoors back). A design lays hers with `modularPieces` `{ "kind": "hatch", "at", "size", "block" }`.
  - **Drawing** (`SlotRenderer`): the mesh file's groups `slot:deck` (outside light) and `slot:deck:interior` (room light),
    each quad with an owner and a facing slot (`slotQuads`): drawn while the owner is in and the facing one is out. Per
    ship they're baked into chunks of 8 × 8 cells of a deck, built again only where a cell changed (a few a frame), on her
    paint's textures. Offline (`-- slots`, the test's own carrack-m cells): an edit costs 0.03 ms on the CPU before the
    upload, a chunk at most ~280 quads.
  - **Places follow the cells**: her mount points also include those over her openings (worked out with every cell laid;
    her own keep their ids), each tied to its cell: a floor place, the places stacked on it, the borders anchored on a
    cell and the hooks under it are usable only while it's in. Painting picks only drawn cell quads; a wreck leaves out
    the cells she had taken up.
  - `/magelan slots [take|lay|reset] [id]`: her cells, and taking up or laying one without tools.
- **0.3.21, step C of the slot plan: guns as items** (contract: `docs/slots.md` §7; `src/Ship/GunPlaces.cs`,
  `ShipGunPlaces.cs`). A design lists **gun places** (`gunPlaces`: id, its gun fixture, the deck cells it needs, start
  laid or empty); each is backed by a `type: gun` fixture with everything today's guns have. A ship can be launched
  unarmed (places `"start": "empty"`) and armed later. Fixed guns (`carrack`, `carrack-a`: no `gunPlaces`) are as before.
  - **The gun item**: `magelan:gun-bronze-north`, "Bronze gun on its carriage" (the block wrecks already gave back; in the
    creative inventory). Recipe (grid): three stacks of 8 bronze ingots (any bronze: tin, bismuth or black) for the barrel
    over 2 iron nails-and-strips, 2 rope, 2 iron nails-and-strips (the trunnion caps, the breechings and tackles), on two
    stacks of 4 planks (the carriage's cheeks and trucks). 24 ingots is about what a 1500-pound demi-culverin's bronze is
    worth in the game's economy, deliberately dear: a gun is worth recovering from a wreck.
  - **Mounting one**: building mode (B), the gun in hand, right-click an empty gun place (its carriage's box shows):
    refused while a deck cell under its carriage, run-back or trail sweep is taken up, or something stands in its working
    room (a piece, a chest, someone). Its room is kept clear of pieces whether a gun is there or not.
  - **Taking one up**: building mode, an empty hand, Shift + right-click the gun (run out, the first such click runs it in
    and shuts the port): only cold (not reloading), housed, nobody laying it or at its gun captain's place. The item goes
    into the inventory (else drops at your feet).
  - **A mounted gun is today's gun**: laying by look, firing, the gun captain's seat, lids and ports, running in and out,
    reloading, broadsides. A place without its gun has nothing of it: its moving parts aren't drawn (the gun's: `train:`,
    `elevate:`, `fire:` + the gun, their children, the breechings; not the lid, which stays shut), it isn't in her
    collision (no obstacle, no target for a shot), the crosshair, her seats, the guns' ticks or her wreck. A deck cell
    under a mounted gun can't be taken up.
  - **Saved** as the differences from the design in WatchedAttributes `magelanGunPlaces` (place id → 1 / 0), sent to every
    client. **Wrecks** lay every mounted gun as the gun block (where it stood, else on the seabed beside her); a place
    taken up gives nothing.
  - `/magelan gunplaces [mount|take|reset] [id|all]`: her gun places, mounting or taking up without the item.
- **0.3.26, step D1: hull panels as slots** (contract: `docs/slots.md` §9; plan and what D1 did: `docs/step-d-plan.md`;
  `src/Ship/HullSlots.cs`, `ShipHullSlots.cs`). carrack-m's hull planking (outside and ceiling) is **panels**: a frame
  space (strip to strip: each frame keeps 0.25 of planking either side) by a band of strakes, on one side; 154 of them,
  the rest (her ends, ports and windows, the wales, the waterways, castle breaks, fittings) fixed. carrack and carrack-a
  keep the old jagged holes.
  - **Damage**: a blow shared among the panels near it (80 kJ each: 28 cracks one, 56 stoves it in: out of her collision,
    a clean opening with bare-oak edges); a shot goes on through it, her fixed planking is structure. The panel's state is
    always what its damage's hurt says; cracked ones are drawn with the damage overlay.
  - **The sea**: a panel out under her main deck lets in 0.2 m² of opening (a 1.5 m² panel) once the sea's over it, about
    0.75 t/s 2 deep (two at the pumps take 0.4), and pours over its sill while the sea's lower; one over her main deck is a
    downflooding point. The room behind it is open.
  - **Mending**: cracked, the repair tool (2 planks and nails); out, building mode (B), planks in hand, right-click the
    opening (as many planks blocks as it says). Taking one out: building mode, an axe, hold left mouse 1.5 s (not under her
    waterline afloat).
  - **Saves**: both families' differences in `magelanSlots`; `magelanDmgF` "v" 2; an older carrack-m's hull damage is moved
    onto the panels where it was.
  - `/magelan hull [take|lay|crack|reset] [id]`: her panels without tools.
- **0.3.23, the seventeenth client test's feedback** (notes 167–175; contract `docs/modular-pieces.md` §10):
  - **Ship's stairs** (the user: "Can we make stairs for 2 blocks also? Ladder animation is kind of bad for play", "maybe
    for ships stairs better to have a special block"): two blocks of the mod's own, crafted from planks (grid: 12 planks
    in a staircase for the long one, 10 for the steep one; in the creative inventory and the handbook),
    `magelan:shipstair-long` (3 cells, 45°) and `magelan:shipstair-steep` (2 cells, about 56°, twelve small treads).
    Building mode (B), right-click a deck cell: it climbs away from you to the deck above (`PiecePlanner.Flight`,
    `BlockShipStair` refuses placing ashore). Both are walked, no climbing; the steep one's collision is 8 voxel columns
    (risers a quarter or a half, `PiecePlanner.FlightColumns`). Up to a castle's front no well; through a deck the long
    one needs a 3 × 1 well, the steep one its 2 cells and a quarter block before its foot (the walker's head as he steps
    up: a 3 × 1 well, or the deck cut back 0.25). Vanilla plank stairs are plain decoration now; a vanilla ladder still
    makes the ladder-stair. A design's `stair` entry takes `"length": 2 | 3` (default 3). A stair laid or put before
    0.3.23 (of plank stairs) gives back the ship's stair it is when taken up; a 0.3.16–0.3.18 ladder-stair saved with plank
    stairs gives back a ladder.
  - **Their own handrails**: drawn over each stringer (stanchions and a rail 0.9 over the nosings,
    `ModularGeometry.FlightHandrails`) and solid in the quarter-block layer beside the treads (`PiecePlanner.FlightGuards`:
    nobody steps off the side; none where her own side is there). No railing goes beside one (`railing-by-stair`);
    raked railings stay beside the walked stairs saved by 0.3.19–0.3.22 (pieces saved `cells`, `rails`).
  - **Putting a stair or a ladder back by hand** (notes 170, 172: "trying to place stairs here (where stairs were
    before) but cannot"): the client sent the plan's direction, which for a stair or a ladder is the way up, and the
    server took it as the way toward the player, planning the piece turned round ("no room", "no well"). The packet now
    carries the direction the plan was made toward (`PiecePlan.Toward`).
  - **Hatches from below** (note 169: "Hatches cannot be opened from bottom. Can climb through closed hatch"): a shut
    hatch is picked and seen from below by its cells' planking (`ShipPiece.PickBoxes`, `SeenBox`), so right-clicking its
    underside opens it; a ladder coming up under a shut hatch stops the climber's head under it and can't be stepped
    onto from above through it (`HatchCover.Under`, `DeckMover.CoverUnder`, `EntityShip.LadderTopClosed`).
  - **Guns on the castle decks** (note 171: "no guns in the cabins"): gun places on an open castle deck with lidded ports
    work as the waist's; the sea through such a port lands on that deck (no downflood point below it).
- **Paint** (`ShipPaint`, WatchedAttributes `magelanPaint`: zone → dye colour or `gold`): the design's `paintZones` and,
  in the mesh file, `paintQuads` (per group, quad ranges with a zone) and `paintFaces` (per voxel shape, element → face →
  zone). `PaintPlan` (read in the background on both sides) has each zone's quads and faces, its area (m²), and finds the
  zone along a ray. A painted quad's texture is `magelan:block/paint/oak<n>` (vanilla's oak planks in grey, lifted to a
  mean of 210 so white reads white) or `paint/timber` (debarked oak), multiplied (`++3~`) by `magelan:block/dye/<colour>`
  (the mean of vanilla's dye texture), a caulked deck's seams (`++0~magelan:block/caulk`) over that; gilt: vanilla's gold
  plate. `PaintRenderer` splits a design's meshes once into what no zone has and each zone's part (in every group of her
  smooth mesh and in her two voxel shapes); a painted ship draws her painted zones from her own meshes; a hull chunk with
  a painted zone in it is built for her as a damaged one is (`HullDamageSkin.Build` takes the paint as each quad's base
  texture), so damage overlays and holes go over the paint. A change of paint builds again only the zones it changed and
  the chunks they reach.

## Changing or adding ships

```bash
python tools/shipgen/carrack.py        # needs numpy + pillow; writes assets/magelan/... and docs/carrack-*.png
python tools/shipgen/carrack.py --variant=1    # or 2: the first carrack, the short-waisted one (to compare; not shipped)
python tools/shipgen/rowboat.py        # the rowing boat's models from vanilla's sailboat (reads the game's assets)
python tools/shipgen/rowboat_assets.py # its entity, item and recipe
python tools/shipgen/proportions.py    # docs/proportions.png: the first carrack against the proposal
python tools/shipgen/textures.py --preview docs/views/damage-textures.png   # tar, caulk and the damage overlays
dotnet run --project tests/MagelanTests -c Release -- damage-views          # her hull as the mod draws it damaged...
python tools/shipgen/texview.py damage   # ...rendered with the game's textures: docs/views/carrack-damage-*.png, -flooded-*
python tools/shipgen/gun.py            # the gun block's shape (assets/magelan/shapes/block/gun-bronze.json)
python tools/shipgen/view.py gun-close gun-recoil guns-out guns-deck guns-port guns-port-recoil   # the guns, run out and fired
python tools/shipgen/view.py gun-lay-deck gun-lay-out gun-captain   # one laid at its limits, run out and back; the gun captain's view
python tools/shipgen/view.py waist-hatches ladders-cutaway hearth capstan-cable gallows-out   # the 0.3.6 layout (the views draw
                                           # the rowing boats on the gallows and the vanilla firepit from the game's own models)
python tools/shipgen/view.py hearth-pot hearth-pot-side   # the hearth lit with a clay pot on it, as the mod places the pot
dotnet run --project tests/MagelanTests -c Release -- wreck-views   # a carrack's wreck as the mod lays and draws it...
python tools/shipgen/texview.py wreck    # ...rendered with the game's textures on a sand seabed: docs/views/carrack-wreck-*.png
python tools/shipgen/view.py notes [path]  # the surveyor's chalk's notes (default: VintagestoryData/ModData/magelan-notes.json):
                                           # each spot on the carrack marked and numbered, seen from where the player stood,
                                           # and an overview: docs/views/notes/ (try it with tools/shipgen/notes-sample.json)
```

Each ship is data: shapes plus `assets/magelan/config/ships/<code>.json` (collision, rooms, fixtures, sails,
lights, sailing numbers), plus an entity type with `attributes.ship: "<code>"`. To add one, write a new design
script, add the entity JSON, and give the charter item a new variant. (0.3.14: `carrack-a`, the carrack with the
companion door and stairs a block inboard (option A), is the second: its own entity and charter, `shipdeed-carrack-a`.
The offline checks run their general part on it after the carrack's (`dotnet run --project tests/MagelanTests -c Debug
-- design carrack-a` runs the carrack's whole set on it; the walk into the great cabin's wall is the carrack's alone),
and the self-test finds it loaded and presses its sails. The sails' pressing reads a design's `rigCheck`, so a design
without one has its sails kept off its masts only.)

For decorating, a design gives (all optional): `hooks` [{code, pos, hang down|up, kinds ["light"], shown}],
`startPieces` [{block, mount "h:<hook>", yaw, attributes}], `stairs` [{code, foot, head, width}], `keepClear` [{what, box}]
(windows and anything else no piece may block) and `paintZones` [{code, name, kind, default}]; its mesh file
`paintQuads` {group: [start, end, zone, ...]} and `paintFaces` {"exterior"|"interior": {element: {face: zone}}}. Floor
places and wall spots are worked out from the collision and the fixtures (`/magelan mounts` counts them; `-- decor` with
`MAGELAN_DECOR_DUMP=<file.json>` writes them all with what's kept clear, for drawing).

## Tests

```bash
dotnet run --project tests/MagelanTests -c Release
dotnet run --project tests/MagelanTests -c Debug -- gunplaces   # (0.3.21, step C) gun places on carrack-m: contract, take/mount, refusals, wreck guns
dotnet run --project tests/MagelanTests -c Debug -- stairs      # (0.3.23) the ship's stairs (steep and long), hand placing, a hatch from below
```

```bash
tools/run_selftest.sh
```

- **Ship's stairs and hatches from below** (0.3.23, `-- stairs`, also in `-- modular` and the full run): the steep and
  the long ship's stair on the carrack's lower deck up to the main deck: their collision's columns (risers ≤ 0.5), their
  wells (the steep one refused and not walked through its 2 cells alone, walked with a quarter cut back or a 3 × 1 well),
  a deck's edge (0.25 over the head, not 0.5), walked up and down with the deck mover, their guards (walked sideways off
  the middle, nobody falls), no railing beside them (an old walked stair's still raked), the handrails drawn parallel to
  the nosings, out of building mode refused, saved and loaded, the entry's length picking the block, and each put back
  **by hand** (`HandPutBack`: the crosshair on its foot cell from before it, `AimPlace.Candidates`, the client's plan sent
  as its `Toward`, planned again as the server does; the 0.3.22 packet shown turned round). `-- design carrack-m` puts each
  of her laid stairs and ladders back by hand the same way. A shut hatch over a ladder: the head stops under it, open the
  climber gets out, from above nobody steps onto the ladder through it, the crosshair picks it from below and it's seen.
  The server self-test (`SelfTestModular`): the ship's stair blocks read in and out of building mode (plank stairs not
  stairs), her ladder and her walked stair taken up and put back by hand (`HandPlace`, with the people check), a steep
  stair put by hand at the quarterdeck's front and walked, her shut hatch from below (picked, seen, a ladder's climber
  stopped under it).
- **Modular pieces** (0.3.16, `-- modular`, also in the full run): border places on the open decks, a pane a window and
  a fence a railing (the partition's boxes), the top partition hanging and the bottom one coming down (note 160), wall
  spots on both faces of a partition and a painting flush on one, the ceiling hooks under the planking between the
  beams, the horizontal places and a shelf holding a chest, the stair's treads (steps ≤ 0.5 on the quarter grid) and
  the deck mover walking up a 2 × 1 stair through a 3 × 1 well onto the main deck and down again, a railing raked
  beside it and flat elsewhere, modular entries finding their places; `carrack-m`'s entries, once it's there. The
  server self-test: the vanilla blocks known, a window and a railing on a border, a painting on a partition's face,
  the bottom partition taken from under the top one, a shelf, a lantern on a ceiling hook.
  (0.3.18) The stair is a ladder-stair: its plan (≈ 63°, its boxes, the head cell free for a chest), its climber's room
  free through a 2 × 1 well and not without one, climbed up and down with the deck mover (`DeckMover.ClimbStair`, the
  head clear of the well's edge), grabbed from its foot cell, taken up and put back, a railing raked to it. Step A
  (`StepATests.cs`): placing by aim (a wall piece on the floor's nearest edge, a chest on the cell, nothing in mid-air
  where the old search took a place 2.4 blocks before the floor, the top-row place under a deck, a partition's row over
  it and the place past its end), the window's four lights leaded 4 × 4, two railings at a corner sharing one post and
  one ending at a partition (no faces in one plane; two half posts had one), the door's 86°, the hearth, oven and flue
  planned (the flue only over the hearth or a segment, the hearth drawing only with its flue, aiming at the hearth with a
  chimney). `-- design carrack-m` also climbs, takes up and puts back each of her ladders, checks her one hearth and
  oven and that the hearth draws through its flue, and lists faces in one plane among her laid pieces' parts (none on
  railings, windows, ladders, the hearth and its flue). The server self-test (`SelfTestModular`): the blocks read in
  building mode or not, the hearth, oven and chimney blocks and the saw known, carrack-m's ladder climbed and put back,
  her laid hearth and oven hers, the hearth lit, its flue and itself staying while it burns, unflued without its flue.
  (0.3.19) A ladder is the ladder-stair, with its own handrails (rails parallel to it 0.9 over its line, stanchions
  upright on its stringers, none through a tread), and a railing beside it is refused. Plank stairs are the walked stair:
  12 treads for a rise of 3, its room free through a 3 × 1 well and not without one, up to a deck's edge with 0.25 of
  overhang but not 0.5, walked up and down with the deck mover, a chest under its head, taken up and put back. The raked
  railings along it are checked as drawn: every part on the side's line within its run, the rails parallel to the
  nosings, the handrail 0.9 over them, posts upright, none through a tread. The charter's launch rule: once per use, not
  again within the cooldown. `-- design carrack-m` lays old `stair` entries as ladders where they were ladders (and says
  so), walks each walked stair up and down with all her pieces in her collision, and checks no railing stands beside a
  ladder. The server self-test drives the charter as the engine does (the stop called twice for one use): one launch,
  none for a second use within the cooldown, none for the use going on with the button held. It checks a launch refused
  on carrack-m's spot, the ladder and plank-stairs blocks read in building mode, and her walked stair (once the generator
  lays one) walked, taken up and put back.
- **Offline** (`dotnet run --project tests/MagelanTests -c Release`), for each of the three carracks (where things
  are is read from each one's doors, decks and stairs):
  - the definition loads
  - local↔world frame math
  - the renderer's rotation matches the collision frame
  - sweeps never end inside geometry
  - walking the real carrack: stairs to every deck (past the castle breaks), doors into every cabin, walls,
    bulwarks, jumping overboard, over the shut hatches, down through the open waist hatch, the hold's cargo under the
    open hold hatch, a clear ring round the capstan, the mizzen down to the keel, the main top's railing; from the hold
    up its ladder and straight on up the companionway to the waist, and back down the same way (the hold hatch just
    forward of the companionway's foot); the companion house (0.3.7): room to stand in its doorway upright, walls and a
    roof round the ladder, its shut door stopping you at it and at the ladder's top
  - shut doors and gates stop you, and chests and beds put aboard take up room; you walk under the boats on the
    gallows; the cargo places in the hold come in pairs; the figurehead and the name board; the hatches, the captain's
    bed and the props are where they should be; the firehearth in the forecastle's starboard after corner, its firepit on
    the sunken firebed, its fire, pot place and chimney given
  - moving parts: the tiller ropes stay on the tiller at full helm, the tiller clears the mizzen, the anchor goes
    down with its cable, the gates swing inboard and the hatch leaves up
  - the sails turn: every sail hangs from a yard turned about its mast, stays under it, the yardarms clear the
    standing rigging at full brace, the braces and sheets stay on; furled, the sheets go up with the clews
  - the dry hull: every air cell of the lower deck and the hold is in the mask, nothing 0.3 outside the hull skin
    is, and the dry outline in the water covers it at the waterline; the detailed dry outline covers the hull
    everywhere below the main deck, stays close to her sides at the waterline and follows the transom; down the hold's
    ladder and round the cargo deck
  - every spot for a lantern, chest or bed has room for it; the stern lantern's place, out over the taffrail, is shown
    even empty (0.3.7)
  - right-clicking a boat on the gallows from the deck picks it (its collision cells reach past its box)
  - (0.3.14, notes 147–152; also `-- partitions`, `-- sails`) partitions: no border place outside her side; a partition
    over a door; the crosshair near a partition's end, on its thin end and near its top takes the place past it, and on
    the beam over a top-row place cut under it that place; drawn out to her real surfaces, no slit over 0.02 between the
    lower deck's beams by the user's spot and at most 0.08 at her side there, the slits overall down by over four times
    (`MAGELAN_DUMP_IDS=<id,...>` draws places as text, `MAGELAN_ROOM`/`MAGELAN_ROW` narrow the count); sails: over the
    crew's trims on both tacks, full and reefed, no vertex of any sail inside a mast, shroud or backstay or through one or
    a set of ratlines once pressed (and the same for a sheet of points over each sail's outline, as the self-test does);
    then carrack-a's general checks (its definition, entity, charter, mesh; partitions; sails)
  - round the stern the water is cut close to her: beside her quarters, under the transom and behind it
  - the ship's boat: board points, walking in her over the thwarts, not out over her sides or ends unless you jump,
    dry inside and not beside her, her raked transom in the dry outline, the tiller, two rowing thwarts whose oars
    show only while rowed, the folding mast (lowered on a new boat), the sail only with the mast up, the bow locker and
    the cargo place, the rowing speeds (one and two rowers, backing, with the sail), and that two of her fit the
    carrack's gallows
  - climbing the main shrouds up onto the top and back down, walking around the top, and not being pulled into the
    shrouds when walking past them; the companionway ladder down and back up
  - the rope ladders: up from the water onto the deck, down into the water, and a shut gate stopping both
  - riding the waves:
    - the tilted frame: round trip, the drawn tilt, passengers leaning with the deck, the water clip's tilted plane
    - she rides a long swell (heave and pitch with it, no roll), rolls in a beam sea, ignores short chop
    - surfing a wave at its speed she stays on it; sailing into the waves she meets them more often
    - a violent storm stays within the roll and pitch limits; lakes move her less
    - the server and a client at another frame rate get exactly the same motion
    - heel: which way, how much sail and wind, none head to wind or furled, never more than the limit, more
      close-hauled with way on, none running
  - sailing by the wind: the wind's angle off the bow and its side; in irons and taken aback; fastest on a broad
    reach; more wind more speed up to the hull's limit; half sail in a gale; the yards' trim; rudder authority; a
    minute on a beam reach, a tack through the wind, and losing way head to wind, tick by tick
    - the view: which way it pitches and rolls for where you look, and that the roll turns about the view axis
  - the crosshair: a part's index round-trips; from the water her sides below the waterline, the topsides, the
    bulwark, a castle's side, the bottom, the stem and the transom are hull; from aboard the decks (the lower deck, its
    underside overhead, the quarterdeck in the poop cabin, the main top), the mainmast, a stair as woodwork, the helm
    and the plate; the same rays with her turned, pitched and rolled meet the same parts at the same places; the
    raycast that tells the face agrees with the plain one. `dotnet run --project tests/MagelanTests -c Release -- parts`
    prints what it meets from a few places aboard and round her.
  - the spawn point: the plate's place hangs on a wall facing the helm, a metal plate goes there, the spot in front
    of it is free with the deck under it, and vanilla's spawn block is the one the spot is in wherever she lies and
    however she's turned, at her still-water level
  - damage (`-- damage` prints her parts, her hydrostatics and a table of what water does to her, and runs these
    alone): her parts (476 hull panels, the decks, the rudder, masts, sails, rigging) with codes of their own; a panel's
    size and outer face; where the crosshair meets her hull is a damage panel's; a blow's spread, a shot through her
    planking, a weak one staying in it; running her stem into rock at 0.5, 1, 2.5 and 4 b/s; the leak math up the
    ladder; how deep and how far over water puts her, her slower roll (heave unchanged); the sea coming in over her side
    (not dry, awash, heeled, a wave's crest, an open hatch, the door below open (the great cabin's since 0.3.8: whichever
    door on the main deck unseals a room below), the gun ports' lids shut or open); two pumpers against a
    moderate leak and a hole; repairs
    (what's held picks the job, the hand first, not enough takes nothing, a mast's fish, rigging and sails, a broken mast
    needs a port); the state round trip (bytes to clients, floats saved by part code)
  - her shadow (the carrack and the launch): the sphere she's culled with holds every voxel, fixture and mesh vertex,
    the moving parts at the ends of their travel; what the far and the near map get (nothing inside her, no glass, the
    ropes only near); and the shadow's matrix, built from the engine's own shadow projection and sun's view, puts her
    at the same place in the map as she's drawn, only stored the push further from the sun
  - her damage drawn: the mesh file marks her planking (outside, wales and ceiling) and nearly all of it belongs to a hull
    panel, each quad the panel its middle is in; each state its overlay outside and inside; one cracked panel rebuilds
    only its own chunks and changes only its quads; a hole is cut out of the planking outside and the ceiling (its area
    what lies between its strakes' broken ends), nothing left inside it, cut where they're broken off, closed by its
    reveal the planking's thickness deep; it's broken planking (whole strakes from seam to seam, each its own length,
    jagged ends, the ceiling its own way), the same every time and growing with its step; with
    a quarter of her hurt the chunks built are exactly those that differ from the design (and how long it takes); every
    overlay asked for is there; the user's note 32's panel (the great cabin's side over the main deck, starboard), holed:
    its tarred planking outside is its own and gets the hole and the holed overlay, the ceiling inside the hole too
  - the water in her hold: from 20 to 1000 t, level and pitched and rolled up to 25°, its surface is level in the world,
    inside her hull, its edges on her planking, from end to end of her hold; nothing under her bottom; her water clip
    tells Sea its level, and under it the sea isn't cut away
  - the guns (`-- cannons` prints the range table and how fast shots below the waterline sink her, and runs these
    alone): three a side, each a moving part that runs back inboard with its trucks and breeching; run out and run back
    clear of her fittings (the capstan, pump, hatches, companionway, stairs, gallows, the gates swung open), on the deck,
    their ports clear of the main shrouds' channels; each muzzle out past her planking and its shot's way clear of her;
    the shot leaves where the drawn muzzle is, however she's turned, pitched and rolled, and a roll lays a broadside down
    or up by as much; the energy, the ranges (level 42, 10° 130, 20° 220, 45° 316 blocks), a little drag, the same path
    stepped in the server's ticks and drawn in a client's frames; into the water (0.3.7: two thirds of its energy gone
    on the surface, three fifths of the rest in a block, spent in about 2, half a block in still about cracking a plank)
    and skipping off at a shallow angle; a ball from 30 blocks meeting her hull, holing it and going across her into
    her far side, a heavier one out of it; the recoil's curve and the trucks rolling, not sliding; the gun block's shape
    and textures; ten and twenty shots below the waterline sinking her in 2–4 minutes
  - laying the guns (0.3.6, `LayTests.cs`): each gun's limits, rates and moving parts in order (the turn, the carriage
    on it, the barrel on that, the trucks, the breeching following the carriage and the barrel); the lay held within its limits and laid
    at its crew's pace, never past them; the lay toward where someone looks and back, from her frame and from a view as
    she lies pitched and rolled; at every corner of the lay range the shot leaving the muzzle as she's drawn, tilted; the
    moving parts composed as the renderer composes them putting the barrel's tip (from its mesh) where the shot leaves,
    and the gun captain where his seat says, run out and run back; and at every corner of the lay range, run out and run
    back a quarter at a time (with the gate beside it shut and open), every point of the gun's mesh 0.02 apart clear of
    her side round its port (an open notch), her collision (within half a cell, away from her side), her fittings, the
    gates swinging and the main channels, the gun captain with room to stand; each muzzle out of its port and its shot's
    way clear; and neighbouring guns at every pair of their corners, run out or back, never meeting (their meshes in
    0.04 cells). `python tools/shipgen/gun.py` checks the barrel clear of its own carriage at -5, 0 and 15°
  - her wreck (`-- wreck` runs these alone and says what breaking all of her would give): laid in the world's cells at
    0°, 90°, 180° her cells are kept exactly, at 30°, 45°, 138°, 229° and heeled and trimmed within 2% (every cell laid is
    one of hers in her own frame); a wreck block's cells round-trip through its layers and saved attributes, its boxes
    cover exactly its cells, its faces (a full block 6, a cell 6 small ones, none between two materials, none against a
    wreck block beside it) carry the texture as a vanilla cube's would, and its mesh as the client makes it (vanilla's
    AddFace) has each corner, atlas piece and normal right; the drop table's averages and ranges (a full oak block 2–4
    planks, 3 on average), only vanilla raw materials, never the block; the same seed makes the same wreck, another seed
    another, built again over its own blocks the same; on a sand seabed (heeled, by the head, holed, clad, with cargo):
    nothing under the seabed, her sides mostly there and her bulwarks less, one castle fallen in, little left round her
    holed panels, tarred and copper outside and oak inside, her masts as timbers to the side she lies over to, her anchor
    by her bow, rock left standing and plants giving way, her guns, containers and extra chests each in a free block of
    their own, sand against her lowest blocks outside her
- the surveyor's chalk (0.3.6, `NoteTests.cs`; `-- notes` runs these alone): a ship-local point at a tilted pose goes to
  the world where the renderer's model matrix draws it, and back; headings; a ray at her starboard side, the same in her
  frame and tilted, keeps the same ship-local point, the world point at the pose and her side's normal (and the world
  face to the nearest axis); the mark is drawn 0.02 off her side along it and lies in the crosshair it was made from, one
  0.6 along her side doesn't; the view's bearing and elevation in her frame, tilted; a note's JSON round-trips (every
  field; camelCase, a point on one line, nothing written for what isn't there); saved and read back from disk, deleting
  and the next number; the readable list (number, time, target, the comment quoted, the screenshot, the key numbers);
  a file that can't be read; the list line and the note in chat; the hand-made sample `tools/shipgen/notes-sample.json`
  (what `view.py notes` was checked with) reads as notes
- the chalk note's comment box (0.3.8, `ChalkTextTests.cs`; `-- chalktext` alone): the game's own text area and Cairo
  (the font measured as in the game, GUI scale 1.5) in a narrow box: typing a sentence that wraps keeps the caret after
  each character (vanilla's text area scrambles it), typing at the start, Enter at the end of a wrapped line, up and down
  keeping the caret's place across, Backspace joining lines, Delete; the lines always what wrapping the text gives; the
  box's height and the scroll that keeps the caret in sight
- the bilge pump's water (0.3.8, `PumpTests.cs`): the spout from her design and the water falling clear of the pump onto
  the deck, the fallback without one, the flow (none dry or unworked, shared between pumps), its particles and gushes
- the firehearth (0.3.6, `HearthTests.cs`; `-- hearth` runs these alone and prints the times): vanilla's temperature step;
  one firewood burns its 24 s and gets the fire to about 430 °C, four with nothing to cook get it within 50 °C of 700 at
  62 s and then burn four times slower (out at 198 s, as the sum says); out of fuel it goes out with embers that relight new
  fuel, cold two game hours later; cooling to the air; the crosshair on its bricks or its firebed picks it (0.3.7: from
  where the user's note 1 was written, from over it, on its end; not the side over its hood nor the deck by it); not lit
  without fuel, held to it more than 3 s it catches, lit the
  first fuel catches at the next tick and it can't be lit again; doused it's cold with the fuel left; raw red meat on
  firewood cooks in 42.6 s (150 °C at 12.7 s), a stack of four in 133 s, on sticks in the same time, a pot of porridge in
  47 s never over the pot's 200 °C, nothing unlit; its warmth by vanilla's falloff (full by it, less 4 blocks off, less in
  the open), the room it warms; drawn as vanilla draws its firepit; the fire's state saved by vanilla's keys and loaded
  (and an older hearth loads cold); vanilla's inventory (the slots' kinds, a pot opening the four cooking slots, its
  contents saved and loaded slot by slot)
  - decorating (`-- decor` runs them alone): mount points on every deck and in every room, every floor place on her deck
    with room over it, every wall spot on her wall, nothing in doorways, the capstan's sweep, the guns, hatches, the
    helm, ladders' ends, masts or by a stair's tread, the same ids worked out again; placing a chest (its front toward
    the placer), a trunk (two places across the placer's view), a bed (head and feet), a 2 × 1 painting on a wall (two
    spots), a lantern (hung, standing, on a wall), a chair (sat on); pieces and a container's contents saved and loaded;
    the 0.3.7 chest, cargo, bed and lantern places kept for older ships, each with a free place near it without them;
    painted textures, the dye and gold cost, the paint saved; paint zones on her real meshes (test zones until the
    generator writes hers): areas, the crosshair on a zone, a painted zone in a cracked hull chunk drawn with the
    cracks over the paint
- **Server** (`tools/run_selftest.sh`, a fresh small world on a local dedicated server; see the script):
  - the surveyor's chalk is registered with its own class, in the creative inventory, and no recipe makes it (its
    notes, marks, window and screenshot are the client's: not tested here)
  - (0.3.14) carrack-a loaded, its entity and charter registered; every design's sails (a sheet of points over each
    outline) pressed against her masts and rigging over the crew's trims, none left inside or through (notes 150–152);
    from the carrack's mesh file and shapes, the partition at note 149's spot drawn out to her ceiling past its block and
    the one at note 148's out to the beam's side, no border place outside her side; on a beam over a cut top-row place the
    crosshair finds it, near a partition's end the place past it (note 147); a door and planks over it (note 147)
  - decorating (since the built-in chests, beds, cargo places and lantern places went, the fittings' checks use pieces):
    the carrack's mount points; a vanilla chest on a lower-deck floor place is a 16-slot storage, solid, named by the
    crosshair, things put in and taken out, saved with her, refused taken up full and taken up empty (its place walkable
    again); a trunk (two places), a storage vessel and a crate in the hold; a bed on the lower deck (two places, a bunk a
    stand-in lies in, not taken up while it does); the design's lanterns on their hooks as a new ship has them (lit,
    hanging or standing, one taken down and hung again); a zone painted red from a bucket of dye (the dye its area costs
    taken, too little refused), scraped back with a knife, a carving gilded with gold plates (a panel refused); a chest
    piece in the hold carried through the sinking voyage; her cargo for the wreck as pieces
  - the client's patches (the crosshair on ships, the look-at box, the view on a rolling deck) find what they patch
    (put on and taken off again; there's no client), and the server's reach check measures to a ship's hull
  - ships' shadows (drawn on clients only): for every design the server loaded, the sphere she's culled with holds her
    voxels and fixtures, and nothing inside her (the hold's cargo included) goes into the shadow maps while the hull,
    rudder, yards, gates and doors go into the far one
  - her damage drawn (on clients only), for every design, from the assets as shipped: her planking is in chunks by
    panel, a hole rebuilds a few of them (cut round, with its reveal), its overlays are there
  - the spawn point, with a stand-in player: the plate put up, the spawn bound (the old one kept), vanilla's spawn
    position following her when she moves more than a couple of blocks, a temporal gear's spawn set since ending it,
    unbinding, the plate taken down, her gone while they're away (given back when they join), `ShipSpawn` off, the
    bindings saved; and a ship removed for good ending the spawns bound to her
  - builds a walled pool in the sky and launches a carrack
  - checks doors change collision, and vanilla chests, beds and lanterns can be put aboard and taken back (a chest
    only when empty); the waist hatch (solid shut, the lower deck open to the weather when it's open); the companion
    house's door (0.3.7: shut on a new ship, the lower deck a closed room; open, the way down and the lower deck open);
    the crosshair on the firehearth picks it, through her own ray (0.3.7); the hold's old
    storage; a trunk across a pair of cargo places, a storage vessel and a crate on single ones; a raft and our own
    boat on the gallows (two places; a sailed boat doesn't fit), the captain's bunk; the firehearth (the vanilla firepit
    it's drawn with, its bricks solid round the fire, in the forecastle; its working fire: see below); the figurehead's
    choices; naming her
  - aground at the wall: a rock where she lies (she can be worked off it, but not into another), digging it out is
    allowed and putting blocks inside her hull isn't; seaweed inside her hull is cleared, beside her it stays
  - launches the ship's boat: her tiller, thwarts and bow locker, the mast lowered and raised, dry inside her, a chest
    stowed in her, and alongside the carrack, overlapping her side, she's stopped
  - the rowing boat: entity, item and recipe registered, the item refuses to put her in overlapping the carrack, a new
    one is empty (can be picked up), and one put against the carrack's side is moved out of her hull
  - checks a chicken on the cargo deck, its head below the pool's water level, breathes, and one just outside the hull
    at the same depth doesn't
  - checks what was in an older version's chests ends up in the hold, and a chest on a place the design no longer has
  - sails it into the wall and checks speed, course, grounding, water level, turning, and that state and cargo are
    saved
  - checks copper sheathing adds up and is saved
  - running into the wall under full sail holed her bow (nothing aft), and the damage is synced and saved;
    `/magelan repairall` mends her
  - damage and flooding, with another carrack: a hole below the waterline; water comes in, she floats deeper and lists
    toward it; 150 t more: deeper, further over, slower, wet under the water in her hold (level: wet just under it and
    dry over it at her middle, sides and ends; Sea's cut shows the sea under it; its surface drawn); mended with planks and nails,
    caulked with flax and resin, taken from an inventory as planned; two at the pump (a third can't get at the brake)
    bring the water down; flooded full in the shallow pool she settles on the bottom, foundered, not sunk, and floats
    off pumped dry
  - sinking, in a deep pool: a carrack with a rowing boat on her gallows, a chest in her hold and a chicken aboard is
    flooded; she settles, goes down, the chicken is put off, the boat floats free, a spawn bound to her ends, her hold is
    wet; on the bottom the wreck's hook gets her and (with `Wrecks` off) she lies still with her cargo, no wreck
  - the guns, in a third pool (14 deep) with two carracks 17 apart (and the gun block registered, dropping itself): a
    port gun kills a chicken on a platform and its ball goes on into the pool's wall and stops there (the wall stands);
    a gun can't fire again while it reloads, nor with `Cannons` off; with `CannonsBreakBlocks` on a plank in the way breaks; one fired down into the sea splashes and is
    spent; a gun laid through the server's API fires along its lay (the ball's first velocity), no further than it goes,
    toward where a view looks, at its crew's pace; the lays saved with her and a carrack loaded with them laid so;
    `/magelan fire` with an elevation and a train; a stand-in (a chicken) at the gun captain's place behind the breech,
    with room to stand up there, unable to lay it; then starboard broadsides laid at the other's waterline (the test
    gives them the elevation, lower than they're laid by hand): since 0.3.7 some balls are spent in the water short of
    her and those that get there through it strike her weaker, but they hole her below it and crack more,
    water comes in, she founders, goes down and sinks, and the wreck's hook gets her; the one firing takes no hurt
  - her wreck (she had a chest and a storage vessel in her hold, a trunk across a pair of cargo places, things in the
    hold, a lantern and the plate): begun again half way, as after a reload (the test makes it forget the wreck under
    way), the ship is gone and her wreck is recorded; her blocks are in the world as planned, once, where she lay, the
    lowest on the pool's floor; a wreck block's collision and selection are its cells; every item she had is in the
    containers put down (her chest and vessel as themselves, the trunk's contents in a chest, the rest in extra chests;
    counted before and after, none dropped); her six guns are there as blocks; water in her blocks and round her;
    breaking a wreck block gives planks (raw materials from the table), leaves water there, and never the block; asked
    again she isn't made a wreck twice, and the registry round-trips; a chicken put inside her cells is moved up out of
    them
  - a wreck that was never a ship (`ShipWrecks.Place`: a design, a place, a yaw, a damage state, a loot list) on the deep
    pool's floor: its blocks, its six guns, its loot in a chest beside it
  - the firehearth working, on a carrack in the first pool: vanilla's firepit inventory on her; not lit without fuel, with
    a stick, or held only a second; a firestarter's try that doesn't catch (its durability kept) and one that does (a point
    worn); two firewood and a raw red meat: it catches at the next tick, clients told, can't be lit again; the meat cooks in
    the time vanilla's numbers give (the offline sum, within 8%), the forecastle warm meanwhile for a stand-in by the fire
    (a closed room) and not on the waist; out of fuel it goes out with its embers (a little warmth); a clay pot with two
    grain and new fuel on the embers: they catch by themselves, the pot opens its cooking slots and says what it will make;
    saved and a carrack loaded with it has it burning with the pot, the grain and the fuel; the pot makes a pot of porridge
    (one serving) in the output in the time the sum gives; switched off it goes out, clients are told, and it can't be lit
  - (and in the deep pool her hearth was burning when she went down: the sea put it out; in the gun pool the target's
    hearth held firewood and raw meat, which ended up in her wreck's chests with the rest)
  - raises a storm at anchor and checks she heaves on the server, as her answer to Sea's shared waves, around a
    still-water level that stays at the surface and is synced to clients, and what that costs a tick
  - sails her on a fixed beam wind (a strong breeze, about 5.3 m/s), then turns the wind right ahead and checks she
    loses her way

## Known gaps and next steps

- **Not tested in the client yet:** everything visual and everything about walking was checked offline and on a
  headless server only. Expect tuning:
  - interior light levels
  - walk feel
  - the camera when boarding at an angle
- **Multiplayer:** built for it (server-authoritative ship, relayed deck positions, relog puts you back aboard) but
  not yet tested with several players.
- **Building ships:** v0.1 has a creative charter item and a command. Next: a slipway / construction site with stages
  like the vanilla sailboat, then more ships (caravel, galleon, a man-o'-war for big crews).
- **Sailing is simple so far:** no leeway (drift to leeward close-hauled), no damage from too much sail in a gale,
  and the sails trim themselves. The polar and the tack are first guesses; expect tuning.
- **Riding the waves** works in the client (seen 2026-09-28). The response settings (periods, damping, heel) are
  still first guesses. The deck doesn't make you slide or stagger when she rolls, and waves don't break over the
  deck.
- **Heating and cooking:** the firehearth in the forecastle (0.3.6) works as vanilla's firepit: fuel, lighting,
  cooking and smelting, a pot's meals, flames and smoke, its light, crackle and warmth (not yet seen in the client).
  The clay oven (0.3.8, `ShipOven.cs`, its fire `OvenFire.cs` pure): vanilla's `BlockEntityOven` ported as the hearth was
  (its `InventoryOven`, burn tick, heating and baking number for number, vanilla's BakingProperties, smelting and
  freshness), synced in `magelanOven`; not yet seen in the client. Still to come: fire spreading aboard, the hearth fed
  from the hold.
- **Guns** (0.3.5, laid since 0.3.6): they're laid by holding right mouse by one and fire on a left-click with a cooldown;
  loading (powder, shot, ramming), a quoin or elevating block under the breech, swivel guns on
  the castle rails and guns of other metals are still to come.
- **Damage** (0.3.5): the hull's damage is drawn (overlays, real holes; the decks' damage isn't drawn yet, and the
  collision stays whole through a hole), cannon fire it (0.3.5), and a sunk ship becomes a wreck of world blocks (0.3.5;
  `docs/backlog.md`, "Damage, sinking and wrecks"). Careening and dry docks (cleaning a foul bottom, a new mast) later.
  The Earth map's worldgen can place wrecks with loot later (`ShipWrecks.Place`, see Wrecks under How it works).
- **The hold** takes your own chests, vessels, crates and trunks. It floods as one space (carracks had no
  watertight bulkheads); the water in it is drawn as one level surface.
- **Decks are level:** real decks have camber (higher in the middle) and sheer (higher toward the ends), so water runs
  to the sides and out through the scuppers. Walking here is on a 0.25-block collision grid, so a real slope would be
  a staircase of small steps; the decks stay flat.
- **The figurehead** has one carving so far (the mermaid); more can be added as mesh groups.
- **Sleeping** speeds up time the vanilla way, but the ship doesn't sail further while time is fast.
- **Shadows:** since 0.3.5 ships cast shadows (see Rendering), not yet seen in the client: the depth push against
  stripes on the hull, and the cost of drawing her twice more a frame, may need tuning.
- **Decorating** (0.3.8, not seen in the client yet): no access rights (anyone aboard may place, take up and paint);
  storage vessels aboard don't slow spoilage as vanilla's do; paint is a solid colour per zone (patterns as stencils
  later); a piece's own block entity look (a sign's text, a labeled chest's label, a pot's contents) isn't drawn; a
  vanilla door put aboard as a piece doesn't open.
- **Getting aboard from a dock:** there's no gangway yet. Jump onto the deck.
- **Teleports:** `/tp`, `/earth tp` or a translocator used while you're aboard takes the whole ship with you. This
  is vanilla mount behaviour, the same thing that makes Cylinder World's seam jump carry the ship and crew. Step off
  first if you want to go alone.
- **Unloaded sea ahead:** right after a teleport or the seam jump, the ship may wait a moment for the sea ahead to
  load. The helm readout says so. That isn't running aground.
