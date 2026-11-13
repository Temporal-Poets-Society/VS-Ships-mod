# Ships: decisions

Ships' own decision records. Suite-wide decisions are in `C:\VS mods\overview\decisions\` (D02, D10, D17, D39, D48,
D49, D53, D71 concern Ships). The construction rules accepted by the author are in `AGENTS.md`.

## The ship framework and the caravel pilot (14 November 2026)

**What the author said** (14 November 2026, chat):

- "rebuild caravel according to Fable session recommendations (new opus session) - if any questions on that ask
  Fable session. Also ask plan on how to do it and so on. After rebuild ask to review. Target is new architecture on
  how to make ships and caravel is first pilot. New branch".
- "architecture and pipeline should support any 3D model input and smaller ships".
- AstoCAD "was just in case if i wanted to change some small things manually but does not need to be there. I
  noticed it took really long time".

**What follows from it.** The recommendations are the proposed answers in section 15 of
`docs/proposals/2026-11-13-large-ship-framework.md`. The author ordered the rebuild by them and did not answer the
24 questions one by one. So each line below has one of four standings:

- **author:** the author's own words settle it.
- **pilot direction:** the proposal's answer, taken as the direction for the pilot on the author's order. The
  author reviews it with the finished pilot and may change it.
- **pilot default:** the proposal gave no answer; this is what the pilot needs. The author is to confirm.
- **not in the pilot:** decided later, with the ship that needs it.

The numbers are the proposal's.

| # | Decision | For the pilot | Standing |
|---|---|---|---|
| 1 | Source of truth | A design file per ship plus one generator. No CAD in the build. The CAD master model of `docs/master-model.md` section 1 is replaced | author (AstoCAD not needed; rebuild by the recommendations) |
| 2 | Structural model | Named members with type, material, stage and supports | pilot direction |
| 3 | Cells | A cell holds several pieces, each clipped to the cell and removable on its own. Replaces "one master block per cell" (suite D17, decision 6; see suite D71) | pilot direction; the shipped caravel already works so |
| 4 | Slivers | Never moved to another cell. The generator moves the edge or stops with an error | pilot direction |
| 5 | Generator and output | Python and numpy. The pilot writes today's asset format; a compact mesh and instanced components come after it | pilot direction |
| 6 | Published caravels | Saved 0.1.x caravels are migrated when the ids change (phase 5), by a table the generator writes | pilot direction |
| 7 | Frame spacing | Per design: the caravel a frame every 2nd cell, as `AGENTS.md` has it | pilot default |
| 8 | What can be damaged and removed | Frames and beams per cell piece, with support consequences; keel, stem and sternpost damageable, replaced only on a slipway or careened; masts as today | pilot direction |
| 9 | Hull form | Two methods in the pilot: an offsets table, and the caravel's own midbody-and-ends formulas with every number in her design. The treatise method (master frame, rising, narrowing) comes with the nao | pilot default |
| 10 | Scale | 1 block = 1 m along and across; decks 3 apart; heights stretched and recorded as a deviation; frames fewer and heavier than real | pilot default (today's caravel) |
| 11 | Order of work | The caravel first | author |
| 12 | Staged building in game | Not built in the pilot. Every member carries its stage, so it can follow | not in the pilot |
| 13 | Retirements | Nothing is deleted in the pilot. The old pipeline is retired after the final review, on the author's word | pilot default |
| 14 | Rig library | The lateen rig only (`caravela-latina`). Square sails, tops and a spritsail come with the carrack or the nao | pilot default |
| 15 | Design docs | `models/caravel/caravel-design.md` becomes a short current spec at the end of the pilot; its history stays in Git | pilot direction |
| 16 | Sources for member names and order | SH-0013's `building-sequence.md`; Hazlett's dissertation when fetched | not in the pilot |
| 17 | Any 3D model as input | The importer is built after the pilot. The pilot keeps its place: the design file's `form.method: offsets`, and a mesh sampler used in tests | author (the requirement); pilot default (the timing) |
| 18 | Imported ships and the grid | Imported ships obey the grid and get generated timbers | not in the pilot |
| 19 | Credit and permission for models | Required fields of an import | not in the pilot |
| 20 | Size classes | The design file has `class`; the pilot implements `decked` only | pilot direction |
| 21 | Boats | Stay with vanilla and Sea; D49 stands | pilot direction |
| 22 | Building traditions | The stage order, member types and support rules are a data file; the pilot writes `iberian-carvel` only | pilot direction |
| 23 | First small ship | Not chosen | not in the pilot |
| 24 | Open ships in play | Not chosen | not in the pilot |

One more, raised by SH-0012 and not chosen by the author:

| | Decision | For the pilot | Standing |
|---|---|---|---|
| L | The default mizzen lantern hangs in the working room of `gun-port-quarterdeck-2` | The default ship must pass her own rules, so the generator refuses a start piece inside a component's working room. The mizzen's hook arm is turned to the nearest side that clears both guns; an `allow` entry in her design can keep it as it is | pilot default |

The plan that carries these out: `docs/proposals/2026-11-14-caravel-pilot-plan.md`.
