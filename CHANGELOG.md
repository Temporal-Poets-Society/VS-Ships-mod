# Ships changelog

## 0.1.2 (16 November 2026)

- **The caravel sails like a lateen-rigged ship.** She now points to 50° off the true wind (was 60°) and has a sailing
  curve of her own: her lateens draw from 50° on, she is fastest from a close reach to a broad reach, and slower dead
  downwind. Until now she sailed on the carrack's square-rig curve, which gave her almost nothing until about 75° off the
  wind. To windward she now makes about twice the progress (0.29 of her top speed at about 64° off the wind, against
  0.15 at 70°). The server option `PointCloser` still points her closer, down to 35°.

## 0.1.1 (15 November 2026)

**Sea is optional now.** Ships runs without Sea; with Sea, it needs Sea 0.6.0 or later (Sea 0.5 is too old: Ships says
so in chat and on the wind panel, and runs as without Sea until Sea is updated).

- **The caravel comes without guns.** Her charter is crafted from five bundles (planking, frames, spars, sails and
  rigging), no longer from two batteries of guns. Craft bronze guns and mount them on her ten gun places aboard:
  building mode (B), a gun in hand, right-click an empty gun place by a port. Shift + right-click with an empty hand
  takes a gun up again (run in and cold). A charter crafted in 0.1.0 still launches her with the guns that went into it,
  and a battery bundle kept from 0.1.0 unpacks into its five guns (right-click with it in hand).
- **With Sea 0.6:** she rides a beach's surf and the lee of land, drifts with a beach's longshore current, and the water
  is cut out of her hull up to her rail at a raised bow and stern castle. A current that presses her onto a shore gives
  her one knock, not a knock every moment she lies there.
- **Without Sea:** she floats on calm water (or another mod's waves through the open sea contracts), the water inside
  her hull is hidden, and she sails by vanilla's wind, which blows only from the east or the west; the chat and the wind
  panel say so. With a mod that gives the wind through the open sea contracts (such as Earth), she sails by that.
- An open door can be shut from outside the cabin: from the side it swings away from, aim at its doorway.
- Ice stops a ship as a shore does: vanilla's frozen lakes and Cylinder World's sea ice. A ship held in ice lies still;
  the waves don't grind her on it. Ice that freezes where her hull is turns back to water, and she floats as high in ice
  as in open water. The server option `BreakIce` (off by default, as ships of her time couldn't) lets a ship under way
  break ice one block thick, losing way to it; thicker ice stops her either way.
- Degree signs in `/ships` status and the damage summary show as ° again.
- Fixes: the water lids without Sea can no longer leave the game's drawing state broken after a fault; riding a beach's
  surf costs less (the surf under her is worked out ten times a second, not every tick and frame).

## 0.1.0 (1 November 2026) - first public release

Early development release. The vessel is the caravel.

- **The caravel:** a walkable ship inspired by the caravel Boa Esperança, with a lateen rig, a hold, a galley and cabins, sailed by Earth's winds on Sea's waves.
- **Craftable:** the caravel charter is made from six material bundles (planking, frames, spars, sails, rigging and battery) at about the ship's real material cost. Hold the charter facing open water at least three blocks deep and hold right-click for about two seconds to launch.
- **Working aboard:** helm, sails, anchor, stairs, ladders, ratlines, hatches and doors; storage and furnishings aboard.
- **Animals aboard:** lead a tamed animal on a rope through an open entry port from a quay alongside, or play the flute on deck to call your elk aboard; tie it to a fence post placed aboard. To get one into the hold, take up the hold ladder, open the hatch and use it with the rope in hand: the animal is lowered in a sling and hoisted up again the same way. Animals no longer pitch with the ship's heave in a storm, stop at the edge of an open hatchway, and stay tied where they were after a rejoin.
- **Paint and sheathing:** her sides are painted a block-high stripe at a time, now down to the waterline's row. Her bottom is sheathed in lead, copper or brass plates or sheets: lead lasts but fouls; copper stays clean but dissolves and is renewed plate by plate; brass dissolves slower and fouls a little. Any sheathing halves the wear on her bottom's seams.
- Her planking's grain runs unbroken along the strakes, level with her main deck from stern to stem, inside and out, with a seam along the deck's edge; it no longer looks warped or kinked at the stern and the bow.
- The hold ladder can be put back after it has been taken up with the hatch aboard.
- **Damage and repairs:** grounding, collisions, wear and gunfire damage the hull; water comes in through leaks and open ports and is pumped out; hull panels, ribs and beams can be replaced.
- **Guns:** bronze guns and swivels; load, lay and fire from the gun.
- **Multiplayer:** crew permissions, ship names and a ship-bound spawn point.
- The hold hatch opens wherever its lid is clicked, and the hold hatch and stair sit one block closer to the mainmast. Caravels launched in earlier development builds keep their old opening.
- `/ships board` needs creative mode or admin rights.
- The experimental rowing boat is withdrawn: it can no longer be crafted and is not in the creative inventory or handbook. Rowing boats already in a world are kept.

## Development builds before the first public release

These builds were tested by the author and were not published on the Mod DB.

- **0.1.0-dev.18 to dev.25 (31 October 2026):** renamed from Hansa to Ships (mod ID `ships`); items in the suite's shared Earth+ creative tab; powder-only gun loading; ropes and posts aboard; the rowing boat withdrawn; the charter recipe and material bundles.
- **0.1.0-dev.9 to dev.17 (25-31 October 2026):** the author's chalk-note play tests: the caravel two blocks longer, galley, cabins, entry gates abreast the mainmast, gun ports and swivels, rudder and rigging, wind in knots, flooding and wave sway, an unmanned ship stopping on load.
- **0.1.0-dev.1 to dev.8 (14-24 October 2026):** Hansa, a block-grid rebuild of the earlier Magelan ships, and the caravel built from a master hull model.
