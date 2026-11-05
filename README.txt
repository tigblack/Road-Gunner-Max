ROAD GUNNER

Portrait vehicle-combat demo in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

OPEN AND PLAY
Open this repository folder through Unity Hub using the version above. Let Unity
import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. The latest 5 November update makes small stones crumble under cars.
The player speed limit remains 9.36m/s from the preceding +60% speed release.
Detailed Argon art, tracked steering and existing combat rules are retained.
Large rocks and structures remain solid.

CONTROLS AND GOAL
Touch or drag in the lower-left control area, or use WASD / arrow keys, to choose
a camera-relative travel direction. The tracked car turns its nose toward that
direction and drives forward. S / Down first turns the chassis around; the tracks
can counter-rotate on the ground. It does not select reverse. Joystick magnitude controls
speed; a held joystick takes priority. Release input to coast. Space applies the
brake. Escape / II pauses the run.

DEFEAT WARLORD to clear Arena 1. The Ashmaw Monster mini-boss is optional; a
16/16 enemy clear is not required. The goal card first points toward a nearby
Raider fight, then toward Warlord, with a camera-relative arrow and distance.
The white 10 m ground ring marks gun range. The turret automatically attacks
unobstructed enemies within range; there is no fire button.

The arena contains 12 basic Raiders, two Rust Vulture Buggies, Ashmaw and the
final Boss Raider Warlord. KILLS counts defeated enemies. SCRAP SCORE is the
score for this run: collect Metal Gear drops directly or let the companion's
harpoon retrieve nearby drops. Scrap is not spent on upgrades in this demo.
Defeating Warlord settles the run's earned remaining rewards into the result.

Drive close to the free, one-use upgrade terminals:
- ATTACK +1: +20% damage for this run, 5.25 -> 6.3 damage per bullet at ATK 2.
- HEALTH +1: +20 maximum and current HP, 100 -> 120 maximum at HP 2, retaining
  damage already taken.
The workshop repairs a living damaged car for free inside its 10 m service area,
restoring 10% of the current maximum HP per second of play.

R / RECOVER CAR returns the car upright to the nearest clear recovery point,
with a 3-second cooldown. Recover itself does not restore HP or reset the run.
RESTART RUN starts again: score, kills and upgrades reset, enemies return and
player health is restored. Pause/Resume preserves run and tutorial progress.
Pause also provides music volume and a summary of current damage and run stats.
The volume preference is saved locally.

ARGON VISUAL AUTHORING
The saved scene already contains the new models and refreshed scenery collision
catalog. Build this saved scene directly. The coordinated art regeneration command
RoadGunner.Editor.ArgonVisualOverhaul.ApplyBatch is an authoring tool and is not
needed for packaging; it replaces art and rebuilds scenery/nav data. Release
validation and source preservation: Documentation/RoadGunner/WebGL-2026-11-05-Argon/.

SCENERY CONTACT
Drive into small stones, cacti, small wooden/metal crates, barrels, loose pipes, tyre stacks
and dead branches to crumble them into short-lived cosmetic fragments. Player
and living enemy Buggies can crush these props. Decorative destruction does
not change HP, kills, upgrades or Scrap. Restart restores the props and clears
fragments. Marked gameplay reward caches retain their existing rules.

The saved arena has 241 crushable pebble clusters and 86 small boulders. Boulders
up to 0.6m high and 1m across crumble with stone fragments, dust and an impact
sound. Larger boulders, rock spires, cliffs, hulls and buildings are solid and block
chassis and shots. Grass, dunes and ground drifts remain surface detail.
Terrain, roads and all original visual geometry/materials/poses are retained.

The saved scene already includes this wiring. Narrow authoring only:
Tools / Road Gunner / Scenery / Install Vehicle Crush Collisions Only
Batch: RoadGunner.Editor.DecorationCollisionInstaller.InstallBatch
Read-only saved-catalog check: DecorationCollisionInstaller.DumpManifestBatch
Do not rerun legacy full art/world installers just to package this saved scene.
Current stone authoring and verification: Documentation/RoadGunner/SmallStones-2026-11-05/.
The catalog preserves all 136 original protected solids. Its 64 former small-rock
hard proxies become crush probes; 145 generated and 3 reused hard colliders remain.
All 14 full-car routes (736 samples) and navigation paths remain clear after one
existing-configuration navigation bake. Vehicle tuning and visual assets are unchanged.
Historical scenery implementation: Documentation/RoadGunner/SceneryCrush-2026-11-03/.

EXPLORATION AND WORLD REWARDS
Five supply caches are destructible auto-fire targets. Unobstructed enemies
have targeting priority. A fresh cache takes two baseline bullets (10.5 HP).
Two caches each add 5% damage, one adds 10% ram damage and two each add 5 flat
maximum/current HP. Bonuses last for this run and preserve damage already taken.
Three optional secret chests reveal on the map within 20 m. Drive close, with a
clear approach, to collect 3 Scrap plus up to 15 / 20 / 15 HP of repair.
Gold chest markers are shootable supply caches; cyan chests are nearby secrets.
Neither cache type counts as an enemy kill. Restart restores all rewards.

Rust Vulture takes 25% extra ram damage. After a committed Warlord strike,
bullets deal 20% extra damage during recovery; the boss HUD shows that window.
The new routes provide room to manoeuvre and optional approaches to encounters.

Edit Levels/Greenwood/Exploration/Exploration layout.asset for routes, widths,
turning clearings, regions, rewards and recovery laybys. Edit Exploration
tuning.asset for reward defaults, reveal/range settings and counterplay.
After edits, stop Play Mode and run:
Tools / Road Gunner / Arena1 / Install Exploration Arena
Batch entry: RoadGunner.Editor.ExplorationArenaInstaller.InstallBatch
This rebuilds the saved scene, road surface, navigation, map and region HUD.
Apply any legacy presentation installer before this exploration installer.
Implementation and acceptance evidence:
Documentation/RoadGunner/Exploration-2026-10-31/

ANTON FEEDBACK AUTHORING
To apply or reapply the current presentation, stop Play Mode and save the open
scene, then use:
Tools / Road Gunner / Arena 1 / Apply Anton Feedback Improvements
Batch entry: RoadGunner.Editor.AntonFeedbackInstaller.ApplyBatch

The installer opens and saves the existing Greenwood scene. It authors the
readable start gesture and contextual hints, persistent Warlord goal, score and
upgrade feedback, compact boss HUD, and Pause / Wrecked / Arena Clear screens.
It retains the circular baked map, arena geometry, navigation, roster, tuning,
free upgrades and repair. Runtime ArenaGuidance caches arena references per run;
GameSession.RunSerial resets presentation on Restart without repeating it on
Resume. Implementation notes:
Documentation/RoadGunner/AntonFeedback-2026-10-29/Implementation.txt

ARENA AUTHORING AND TUNING
Tools / Road Gunner / Level Editor opens the existing scene-based authoring
tools. Per-level settings live under Assets/RoadGunner/Levels/Greenwood/Arena1.
The Arena 1 player profile has trackedDrive=true, maxSpeed=9.36m/s and unchanged
acceleration=2.2m/s2. The speed limit is60% above the previous5.85m/s profile. The
same15-second flat-course comparison measured5.31100 ->8.45277m/s (59.16% faster),
approximately19.1 ->30.4km/h actual cruise. Initial wheel-only and half-speed
track conversion measurements remain historical in their own release folders.
Four existing
WheelColliders provide suspension and ground contacts; visible tyres are hidden
behind two animated track assemblies. Motor torque differs between the sides;
wheel steering stays zero. A bounded 9000 Nm yaw assist acts only with support
on both sides, never by replacing body velocity/rotation. Recover/Restart still
reset pose explicitly. Buggies retain their wheeled profile. Combat HP/ATK and
arena geometry are retained. Current speed evidence: Documentation/RoadGunner/Speed60-2026-11-05/.
Track conversion history: Documentation/RoadGunner/TrackedVehicle-2026-11-03/.

The saved scene already has tracks. To regenerate only their visual assets:
Tools / Road Gunner / Player / Install Tracked Visuals Only
Batch: RoadGunner.Editor.PlayerTrackVisualInstaller.InstallBatch
This preserves body, crew, weapons and the four suspension contacts. Do not run
legacy world/presentation installers just to build or publish this saved scene.
Historical wheel-only revision evidence remains in WheelPhysics-2026-10-30/.
After editing terrain or roads, refresh the static map through the existing
minimap authoring workflow and rebake navigation as required by the Level Editor.
Legacy rebalance/prototype/polish commands have their own authoring scopes and
may replace presentation or tuning; apply the Anton feedback installer, then the exploration installer
when retaining the current HUD and map. See Assets/RoadGunner/README.txt for details.

WEB BUILD AND PUBLISHED DEMO
Use Tools / Road Gunner / Web / Build Mobile Web, or batch entry
RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/.
Rebuild WebGL after saving changes to the scene. Gzip with JavaScript
decompression fallback supports static hosting.

WebBuild/ contains the 5 November crushable-small-stone update, built with Unity 6000.6.4f1.
The player limit remains 9.36m/s with the current tracked handling.
Seven static artifacts total 26,771,979 bytes. Fresh native acceptance:
scenery31 + gameplay204 + exploration88 = 323 PASS with zero failures and runtime
Console errors. Small stones crumble, large obstacles retain hard collision,
and Restart restores the original stones. Vehicle and combat tuning are unchanged.
Build/input hashes, source audits and publication evidence:
Documentation/RoadGunner/SmallStones-2026-11-05/.
Play: https://tigblack.github.io/Road-Gunner-Max/?build=c65fd56
Publication evidence: Documentation/RoadGunner/SmallStones-2026-11-05/Publication.txt.
The preceding +60% speed release 51d237e is documented in Speed60-2026-11-05/.
The preceding Argon visual release c5531b3 is documented in WebGL-2026-11-05-Argon/.
The previous scenery release 42a3746 and tracked-player release 466ff61 remain
historical snapshots in their corresponding documentation folders.
Earlier audio player d3c19c3, Arcade player 63dc9c0 and exploration player fc89028
remain historical snapshots; their own publication reports are retained.

Previous wheel-physics demo d6a9ba3 and Anton HUD demo 2310eee remain historical
snapshots. Their reports describe those releases rather than this revision:
Documentation/RoadGunner/WheelPhysics-2026-10-30/Publication.txt
Documentation/RoadGunner/AntonFeedback-2026-10-29/Publication.txt
The authoring installers themselves do not publish a Web build.

PROJECT CONTENTS AND EVIDENCE
Assets/            Game assets, scenes, editor tools and runtime code
Packages/          Dependency manifest and lockfile
ProjectSettings/   Unity project settings
AgentScripts/      Asset authoring and gameplay verification helpers
Documentation/     Art direction, implementation notes and historical reports
_anton/            AntonSkill review and its evidence

The pre-change feedback review is:
_anton/road-gunner-unity-2026-10-29/РАЗБОР.md
Historical validation reports describe their named snapshots; diagnostic
captures are not natural playthroughs. The earlier Anton feedback revision
recorded 204 Unity runtime checks, zero failures and zero Console errors:
Documentation/RoadGunner/AntonFeedback-2026-10-29/PlayMode.txt
Its archive/JavaScript/WASM/HTTP checks and unavailable post-build visual browser
run (CUA cgWindowNotFound) also concern that earlier build. They do not establish
acceptance of the later tracked conversion. The 30 October revision passed 20 dedicated
physics checks plus 204 full Arena 1 regression checks, with zero failures and
zero Console errors/exceptions/asserts. Reports: WheelPhysics-2026-10-30/PlayMode.txt
and Gameplay.txt. No FPS, retention or cross-platform physics determinism is
asserted here.
Exact The War reference data was unavailable; Arena 1 remains the documented
authored fallback layout. Unity caches, local IDE state and generated build
folders can be recreated by the editor.

EXPLORATION ACCEPTANCE — 31 OCTOBER 2026
The saved revision passed 88 exploration, 204 Arena 1 and 20 wheel-physics
checks: 312 total, zero failures and zero Console errors. See
Documentation/RoadGunner/Exploration-2026-10-31/Delivery.txt and its named reports.
These are controlled Unity fixtures, not FPS measurements or a natural full run.

TRACKED PLAYER ACCEPTANCE — 3 NOVEMBER 2026
Two animated tread assemblies, wider ground imprints and grounded differential
steering replace the visible player tyres. The 15-second straight-course A/B
measured 10.49496 -> 5.31100 m/s (50.6% of the old speed). Configured maxSpeed
is exactly halved, 11.7 -> 5.85 m/s. Fresh native checks: physics21 + tracked9 +
gameplay204 + exploration88 = 322 PASS, zero failures and runtime Console errors.
See Documentation/RoadGunner/TrackedVehicle-2026-11-03/Delivery.txt. These are
controlled fixtures, not a measured browser FPS or physical-phone benchmark.

ARCADE AUDIO + VISUAL OVERHAUL — 1-2 NOVEMBER 2026 (branch feature/audio-visual-overhaul)
Goal: natural generated sound, thematic music and a bright idle-arcade look with
cel shading, ink outlines, juicy VFX and a chunky HUD. Presentation only: gameplay,
tuning, colliders and navigation are unchanged.

Apply (after any legacy installer), stop Play Mode first:
Tools / Road Gunner / Arcade / Apply Full Arcade Overhaul
Batch entry: RoadGunner.Editor.ArcadeOverhaulInstaller.ApplyBatch
The saved scene already includes the Arcade passes. Building and publishing
do not require rerunning authoring installers, which regenerate scene assets.
It runs four idempotent passes in order (each also has its own menu/batch entry):
- ArcadeVisualInstaller: "RoadGunner/Arcade Toon" cel shader on opaque materials,
  upgraded "RoadGunner/Concept Desert" ground (procedural sand detail, painted roads,
  toon shadows), arcade palette atlases (separate team-red enemy atlas), ink outline
  FullScreenPassRendererFeature on Mobile_Renderer and PC_Renderer (SSAO off),
  sun/ambient/haze, "Arcade Sunny Grade" volume (tonemapping stays None), regenerated
  world texture, collider-free region dressing, workshop/station restyle, minimap rebake.
- ArcadeVfxInstaller: ArcadeVfx sink (muzzle stars, tracers, sparks, damage numbers,
  death poofs, explosions within the 3-slot budget, telegraph zones, loot halos,
  station beacons, repair pluses, confetti), hit flashes, camera shake (FollowCamera).
- ArcadeHudStyleInstaller: Lilita One font (OFL, UI/Arcade/Fonts), generated UI
  sprites, two panel families, world-label styling, ArcadeHudJuice sink (flying gears,
  chip punches, damage vignette, repair chip, result-screen celebration).
- ArcadeAudioInstaller: ArcadeAudio sink (voice pool, variant banks, tyre roll, skid,
  wind, stingers) and music wiring. Each legacy one-shot (gun, impact, explosion,
  pickup) keeps playing until its own generated clips exist; music stays Dustlight
  Road until Desert Run exists.
Runtime hub: Scripts/Arcade/ArcadeFeedback.cs receives gameplay moments (shot, hit,
kill, damage, loot, upgrade, repair, victory...) from small hooks in gameplay scripts
and forwards them to the sinks; sinks never change gameplay state.

Generated audio: raw ElevenLabs files go to Tools/Audio/Generated/raw/<id>.wav|mp3
(ids are listed in Tools/Audio/process_generated_audio.py). Then run on the Mac:
python3 Tools/Audio/process_generated_audio.py   (slices variants, normalises, makes
WebGL-safe loops with guard + smpl chunk, builds the "Desert Run" music loop) and
RoadGunner.Editor.ArcadeAudioInstaller.ApplyBatch.

Owner audio feedback, 2 November 2026 (after playing the published build):
- No engine drone. With the arcade sink active the car's legacy Engine.wav source is
  held silent; movement is a quiet tyre-on-gravel roll (Tire_Roll_Loop) that is silent
  when parked or airborne and rises with speed to about 6 LU under the music.
  Without the sink the legacy engine only hums quietly while moving.
- Casual gun sounds: the player turret is a soft toy-blaster "pop" (Gun_Player, 3
  variants, ~0.13 s), enemies a cartoon "pew" (Gun_Enemy), hits a soft "tock" with a
  quiet tin "tink" (Impact_Bullet). Hits were hand-picked from the raw generations
  (see the takes in process_generated_audio.py); sfx_enemy_gun was rejected.
- Still legacy: explosion, pickups, crash, UI, stingers, boss warning; still Dustlight
  Road music. The remaining generations need the owner's approval for each paid run.
Listening check: VisualCaptureRunner.Run -rgCaptureShots audio -rgAudioRecord out.wav
records a fixed 25 s scene (parked, accelerate, cruise, brake, combat) with phase
marks in CAPTURES.txt; per-phase loudness can be measured with ffmpeg ebur128.

Visual review: RoadGunner.Editor.VisualCaptureRunner.Run (batch, without -quit;
-rgCaptureLabel name -rgCaptureSize 720x1280 [-rgCaptureShots start,combat,...])
stages 22 gameplay moments and writes outputs/visual-capture/<label>/.
Suite reports for this revision: Documentation/RoadGunner/ArcadeOverhaul-2026-11-01/.
New audio update verified on 2 November: 204 gameplay, 88 exploration and
20 wheel-physics checks passed, with zero failures and Console errors. The
saved scene built successfully for WebGL; a fixed native audio scene was
recorded separately. No production source/asset was altered during delivery.
Evidence: Documentation/RoadGunner/WebGL-2026-11-02-Audio/.
Ten generated WAVs now replace player/enemy gun, bullet impact and tyre roll.
Other absent banks retain legacy fallback; music remains Dustlight Road.
The earlier 80-variant shader check belongs to the previous Arcade visual release;
shader sources remain unchanged in this audio update.
