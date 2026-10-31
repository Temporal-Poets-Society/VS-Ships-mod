**Caravel wave motion — 31 October 2026**

The dev.16 reproduction identifies excessive **roll**, with pitch in head seas. It does not reproduce whole-hull yaw about a vertical mast axis: dev.16's server yaw is constant with the helm amidships, and replaying its packets through the client preserves that heading. This is evidence for the controlled conditions below; the original play session was not recorded. No game client was started.

The measurements use Sea **0.4.7-pre.10**, read from `C:/VS mods/Sea/.claude/worktrees/codex-storm-waves`. The main Sea folder contains 0.4.6, so compiling against its compatible API alone would not reproduce the reported spectrum. Hansa was built with `-p:SeaRoot="C:\VS mods\Sea"`; numerical wave tests use the pre.10 source, and the dedicated tests load its existing release ZIP. Sea was neither built nor edited.

**Cause and exclusions**

- `ShipMotion.Waves` applies a damped frequency response to the wave slopes. The caravel had a 4.5 s natural roll period and only 0.20 critical damping. Around resonance its gain is approximately 2.5; substantial energy in the new storm spectrum's shorter trains excites that response. This produces conspicuous lateral travel of the tall rig even without yaw. Increasing damping addresses the resonant gain while retaining the natural period and sea response.
- `EntityShip.ServerTick` previously assigned `YawRate` from `HelmMath.YawRate`, or the sweeps. There was no wave yaw moment. In the baseline dedicated test, all eight server yaw amplitudes are zero. The renderer's transformed keel also keeps its heading: pitch/roll are what the player sees in this reproduction. The tiny apparent crossing period in the baseline's zero-amplitude drawn-yaw log is floating-point matrix noise, not a real oscillation.
- `Sailing.Windage` supplies translation and leeway, not yaw. The unattended/lashed helm tends amidships; it is not a heading controller fighting waves. The dip and sheet code transforms the rig inside the hull matrix; it does not rotate the hull. The camera follows deck tilt, and the aboard render origin follows the same seat transform. None supplies a hidden whole-hull yaw in this path.
- A real origin defect did exist for commanded turns: `ServerTick` and client prediction turned about model x=0. On this design x=0 is several metres forward of the waterplane centre. Pitch already had a first-order centre correction, but used the waterline bounding-box midpoint and `tan(pitch)` rather than the rigid vertex's `sin(pitch)` displacement. Roll already used the waterline keel axis.
- The main mast at x=-2.5 is genuinely close to amidships in these model coordinates. The measured flotation centroid is **-2.832 m**, and the immersed lateral-profile centroid is **-3.051 m**. Their proximity to the mast is not itself a mast-pivot defect. Neither centre is taken from a mast attachment.

The physical reference is the waterplane centroid for pitch, as described by [Wärtsilä](https://www.wartsila.com/encyclopedia/term/centre-of-flotation). Separating horizontal yaw from damped wave-driven heave/pitch/roll, with added inertia and immersed-hull damping, is consistent with the simplified vessel models documented by [SINTEF's FhSim](https://fhsim.no/doc/group__marine__elements__WaveHull.html). The numerical coefficients here are a gameplay approximation, not measured hydrodynamic coefficients for a historical caravel.

**Changes**

The caravel's roll damping is now **0.55**; the 4.5 s natural period, wave amplitudes, heave/pitch response and angle limits remain active. The exporter carries the same setting so a future caravel export preserves it.

`ShipWaveHull` measures flotation and lateral centres from the design's waterplane outline and submerged collision envelope. Pitch samples around the flotation centroid and uses the exact vertical sine correction. Its small-angle horizontal approximation remains: the flotation point moves less than 6 cm horizontally at ±11.5° pitch. Roll stays on the waterline axis. Pure yaw turns fix the lateral centre to numerical precision, instead of fixing the model origin.

`ShipMotion.WaveYawRate` adds a small first-order yaw response to the longitudinal variation in transverse wave orbital velocity. Hull length sets the inertia timescale (about 2.43 s here); hull/draft and forward flow supply damping, and the immersed rudder's area and lever add resistance. Uniform head or beam trains have no asymmetric moment; the directional spread supplies the small sheering in an irregular sea. There is no artificial spring to a compass heading. The sea contribution approaches a 2°/s rate limit smoothly. The anchor, grounding and sweeps suppress that free wave yaw. Damage to the rudder removes its damping contribution without removing the hull's resistance.

Yaw remains server-authoritative in the existing yaw/rate snapshots. `HelmMath.Advance` supplies the same circular-arc motion and lateral pivot to the server, client prediction and packet extrapolation, including leeway and sternway. Smoothing blends the lateral centre before recovering the model origin. Clients do not add another independent wave yaw. `ShipFrame.ModelMatrix` is shared by the renderer and the headless pose measurement.

**Identical seeded open-sea comparison**

`wave-sway` runs 240 s at 30 Hz with spectrum seed 4242, initial world position (1000,2000), waves towards +Z, rest or 3 m/s, and head or beam orientation. Moderate: Hs=0.7 m. Storm: Hs=6 m, Lp=180 m. Half sail supplies roll steadiness under way; the table excludes steady wind heel. Amplitude is half the observed peak-to-peak range. Period is the mean upward mean-crossing interval in an irregular sea, not a single prescribed sinusoid. Baseline yaw is **0 in every row**.

| Sea / way / heading | Roll before ° / s | Roll after ° / s | Pitch before ° / s | Pitch after ° / s | Yaw after ° / s |
|---|---:|---:|---:|---:|---:|
| Moderate / rest / beam | 7.03 / 4.30 | 3.18 / 4.10 | 1.05 / 4.18 | 1.05 / 4.04 | 0.189 / 4.20 |
| Moderate / rest / head | 0.63 / 4.37 | 0.31 / 4.38 | 2.53 / 4.49 | 2.53 / 4.49 | 0.100 / 4.61 |
| Moderate / way / beam | 6.26 / 4.32 | 3.57 / 4.10 | 1.00 / 4.16 | 1.05 / 4.10 | 0.177 / 4.17 |
| Moderate / way / head | 0.24 / 3.37 | 0.15 / 3.15 | 2.32 / 3.85 | 2.31 / 3.85 | 0.048 / 3.23 |
| Storm / rest / beam | 17.59 / 6.46 | 13.30 / 7.31 | 6.20 / 5.13 | 6.26 / 5.13 | 1.255 / 5.51 |
| Storm / rest / head | 7.51 / 5.24 | 3.75 / 5.49 | 10.59 / 7.05 | 10.59 / 7.06 | 1.166 / 5.64 |
| Storm / way / beam | 16.25 / 6.85 | 12.84 / 7.51 | 6.06 / 4.96 | 6.05 / 4.75 | 1.290 / 6.65 |
| Storm / way / head | 4.25 / 4.49 | 2.41 / 4.42 | 11.02 / 5.07 | 10.94 / 5.19 | 0.688 / 4.48 |

Storm beam heave stays approximately ±5.6 m in this severe open-sea test. The ship continues to ride the sea. A separate single 90 m oblique swell gives ±1.54° yaw at **7.59 s**, matching its wave period. At 4 m/s into that swell, the result is ±0.928° with the intact centred rudder, versus ±0.954° without its resistance.

**Dedicated-server and client-pose evidence**

The `Only: "wave-sway"` self-test creates its own deep cross-shaped pool and runs eight 30 s cases. Fixed Sea wind overrides build moderate (Hs 0.7) and storm (Hs 5.5) seas. This test world's oceanity gives a **0.20 shelter factor**, logged explicitly; it is not the open-sea table above. At rest the ship is unanchored with zero forward speed; under way a fixed half-sail throttle gives about 3.08 m/s even into the wind. Windage and damage are disabled to isolate motion. The rig/heel paths remain enabled.

Each real server tick is recorded. Snapshots at 5 Hz are delayed 100 ms and fed to `ReceiveMotionPose` and `ClientMotionFrame` on a headless replica. The drawn heading is extracted from the exact renderer matrix; drawn pitch, roll and heave are logged too. The server's level collision pitch/roll are logged separately as zero. This exercises production smoothing and pose math, not a second implementation or a running game client.

Baseline: `testserver/wave-sway-before/Logs/server-main.log` and `Logs/wave-sway/*.csv`. Its motion source is HEAD/dev.16; only the packet/frame entry points were exposed for the same replay harness. Every server heading stayed constant, with no client heading oscillation. The source snapshot and the original four-minute CSVs are preserved with the local diagnostic artifacts.

Final run: `testserver/wave-sway-after-final/Logs/server-main.log` and `Logs/wave-sway/*.csv`. All eight cases pass. The independent 30 s windows start at slightly different wave phases; use the identical four-minute comparison above for before/after response ratios. Runtime pitch/roll include wind heel and its start-up settling, unlike the wave-only table.

| Case | Server yaw ° / s | Drawn yaw ° / s | Maximum heading error ° | Settled position-lag range m |
|---|---:|---:|---:|---:|
| Moderate / rest / beam | 0.032 / 4.45 | 0.034 / 4.45 | 0.010 | 0.001 |
| Moderate / rest / head | 0.014 / 4.50 | 0.015 / 4.50 | 0.004 | <0.001 |
| Moderate / way / beam | 0.041 / 3.92 | 0.044 / 3.92 | 0.010 | 0.018 |
| Moderate / way / head | 0.005 / 2.74 | 0.006 / 2.74 | 0.003 | 0.032 |
| Storm / rest / beam | 0.269 / 4.87 | 0.281 / 4.87 | 0.051 | 0.003 |
| Storm / rest / head | 0.204 / 5.91 | 0.215 / 5.92 | 0.042 | 0.002 |
| Storm / way / beam | 0.245 / 8.06 | 0.254 / 8.06 | 0.040 | 0.023 |
| Storm / way / head | 0.079 / 4.12 | 0.085 / 3.61 | 0.026 | 0.024 |

Mean-crossing period estimates are least reliable for very small motions over these short irregular-sea windows. The longer seeded trace and single-train test provide the stronger period checks. The underway client replica has 0.66–0.69 m of position lag with the deliberately delayed packets; its variation after settling is at most 3.2 cm. This is network latency, not a large alternating yaw correction. Baseline underway lag was 0.66–0.68 m as well.

**Validation**

- Hansa Debug build against the existing main Sea build: passes, with four existing obsolete `ShapeElement.Faces` warnings.
- New `wave-sway` numerical checks with pre.10: **44 pass**. Checks yaw amplitude/period, pure-wave symmetry, frame-rate agreement at 30/144 Hz, rudder resistance, visible pitch/roll bounds and yaw jitter in all eight conditions, measured centres, waterline roll, pitch-centre height, and identical turn arcs at server/client timesteps. Pitch-centre vertical error in the actual drawing matrix is below 0.00001 m; yaw pivot error is below 1e-10 m.
- Construction core: **99/99 pass**. Helm group: **ALL OK**.
- Dedicated baseline and final motion self-tests: **ALL OK**, eight cases each. Both server processes exit. Shared port 42597 was awaited before each launch. The preliminary final run's overly tight 0.70 m position bound rejected one delayed-packet sample; the final harness bounds lag by speed and separately checks its variation, and passes all eight cases.
- Broad inherited checks: four failures also reproduced with the baseline motion source: stern water cut, two old damage-skin/reveal checks, and the archived creative-charter expectation. These concern untouched historical areas. A first run additionally failed the gun texture/native Cairo checks because APPDATA had been redirected to work around sandbox NuGet access; normal APPDATA resolves those two environment failures.

Local build/diagnostic snapshots and logs are retained in `testserver/wave-sway-artifacts/`; generated current numerical traces are in `testserver/wave-sway-offline/`. The final broad-suite comparison is recorded there too. ShipWater, ShipFlooding, construction behaviour, chalk-note code and Sea source are unchanged.

**Reproduction commands**

```powershell
dotnet build Hansa.csproj -p:SeaRoot="C:\VS mods\Sea"
dotnet run --project tests/HansaTests -p:SeaRoot="C:\VS mods\Sea\.claude\worktrees\codex-storm-waves" -- wave-sway
dotnet run --project tests/HansaTests -p:SeaRoot="C:\VS mods\Sea" -- helm
dotnet run --project tests/ConstructionTests
./tools/run-wave-sway.ps1
```

The runner waits for shared port 42597, uses a fresh worktree-local data directory, launches only `VintagestoryServer.exe`, and waits for its self-test shutdown. Its timeout cleanup targets only the process it started. It copies the existing Sea package into that isolated test directory. It never installs a mod or changes the user's game configuration/worlds. No commits are made.
