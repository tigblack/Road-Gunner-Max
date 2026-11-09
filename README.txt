ROAD GUNNER

Portrait walker-combat demo in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

OPEN AND PLAY
Open this repository folder through Unity Hub using the version above. Let Unity
import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. The 6 November layout revision rebuilt Arena 1 around a wide
four-arm crossroads, southeast repair hub, southwest guard outpost, northeast
camp/optional Ashmaw, northwest Warlord and three closed exits. It adapts the
researched historical Butcher Hero topology to our vehicle and wasteland.
Map bounds are350m east-west by310m north-south, with30m main and27m cross roads.
The approximately50% widening is relative to a normalized authoring plan; the
reference world's real dimensions and complete current balance are unknown.
The player speed limit remains 9.36m/s from the preceding +60% speed release.
The player is now an articulated bipedal walker with an armoured torso, shoulder
cannon and arm-mounted harpoon. Automobile body/crew/tyre mesh references are
removed from the saved player. Separate planted-foot impressions replace player
track strips; existing steering and combat tuning are retained.
Large rocks and structures remain solid.

CURRENT LEVEL AUTHORING
Edit Levels/Greenwood/ReferenceArena/Reference layout.asset for encounter and
landmark positions. Its geometry reference points to the common editable
Exploration layout.asset for roads, clearings, regions, supplies and secrets.
Stop Play Mode, save other work, then run:
Tools / Road Gunner / Arena 1 / Install Reference Crossroads Arena
Batch: RoadGunner.Editor.ReferenceArenaInstaller.InstallBatch
The versioned installer rebuilds only the environment, keeps the actual existing
actors/vehicle/workshop/upgrades, checks chassis clearance and reachability,
then saves navigation and a matching map. Reapply this command after layout edits;
legacy full-world/exploration installers describe older geometry.
Acceptance: RoadGunner.Editor.ReferenceArenaVerification.Run (no -quit).
Design, source evidence and fresh verification:
Documentation/RoadGunner/ReferenceArena-2026-11-06/
The arena layout release initially passed372 assertions across gameplay,
exploration, reference-layout drives, respawns and its former wheel physics.
The later instant walker has separate fresh acceptance below. Delivery.txt and
Screenshots/native-overview.png record the saved result and scope. A repeat
installer pass preserves the same geometry/assets without duplicate gameplay.
The current9 November WebGL contains the walker contact/cannon/harpoon feel pass
and rebuilt reference arena. Release evidence:
Documentation/RoadGunner/WalkerFeel-2026-11-09/
Documentation/RoadGunner/WebGL-2026-11-09-WalkerFeel/
Play in a phone browser: https://tigblack.github.io/Road-Gunner-Max/
Tap PLAY to start the game and audio; use the floating touch stick.

ENEMY RETURNS
After a Raider, Buggy or Ashmaw dies, a circular timer appears at its death site.
The gold arc and seconds count down for 30 seconds of active gameplay. The same
enemy then returns there with full health. There are 15 eligible enemies, with
one reusable timer each. The final Boss Raider Warlord still ends the arena.
Pause freezes the timers; Restart clears them and restores the starting roster.
Defeat and Arena Clear cancel pending returns. An occupied site waits at READY
until the vehicle/enemy footprint is clear; enemies do not appear inside a car
or wall. Each new life awards its usual kill and Gear reward once.
Edit ArenaEnemyRespawns.respawnDelaySeconds on the saved GameSession to tune delay.

CONTROLS AND GOAL
Touch or drag in the lower-left control area, or use WASD / arrow keys, to choose
a camera-relative travel direction. The grounded walker immediately faces and
walks in that direction, including S / Down, with no acceleration ramp or coast.
Release the stick/keys to stop immediately. Joystick magnitude still controls
speed; a held joystick takes priority. Space stops movement. Escape / II pauses
the run. Solid obstacles block the walker; falling keeps its vertical physics.

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
The workshop repairs a living damaged walker for free inside its 10 m service area,
restoring 10% of the current maximum HP per second of play.

R / RECOVER returns the walker upright to the nearest clear recovery point,
with a 3-second cooldown. Recover itself does not restore HP or reset the run.
RESTART RUN starts again: score, kills and upgrades reset, enemies return and
player health is restored. Pause/Resume preserves run and tutorial progress.
Pause also provides music volume and a summary of current damage and run stats.
The volume preference is saved locally.

PLAYER WALKER PRESENTATION
The current saved player contains only the bipedal walker presentation; obsolete
car-body, cabin, crew, tyre and weapon meshes have been detached from the player.
The vertical physical rig and combat balance are retained. The player-only
VehicleTuning.walkerDrive profile replaces the old acceleration/coasting behavior. Footprints are separate
mechanical soles emitted on verified left/right foot landings, with step dust;
standing, paused or airborne feet do not paint continuous tracks.
Narrow authoring: Tools / Road Gunner / Player / Install Instant Walker
Batch: RoadGunner.Editor.PlayerWalkerInstantInstaller.InstallBatch
Acceptance (each without -quit):
RoadGunner.Editor.PlayerWalkerInstantVerification.Run
RoadGunner.Editor.PlayerWalkerVerification.Run
RoadGunner.Editor.Arena1Verification.Run
Fresh evidence and actual game/detail captures: PlayerWalkerInstant-2026-11-06/.
The 6 November revision passed256 assertions (33 instant movement +19 gait/visuals
+204 gameplay). Current 9 November reports are recorded separately below.
The upper model has layered armour, fasteners, vents, hydraulic components, hoses
and machined weapon details, combined into the same seven shared-material meshes.
Do not rerun the older full car/art/world installers over this saved version.

ARGON VISUAL AUTHORING (HISTORICAL VEHICLE RELEASE)
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

The preceding 5 November arena had241 crushable pebble clusters and91 small boulders.
Boulders
up to 0.6m high and 1m across crumble with stone fragments, dust and an impact
sound. Larger boulders, rock spires, cliffs, hulls and buildings are solid and block
chassis and shots. Grass, dunes and ground drifts remain surface detail.
The6 November layout explicitly replaces the old environment. Its new scenery
reuses finished models/materials and the same vehicle-only destruction runtime;
new small stones, cacti, crates, drums and tyres have reusable crush probes.
The current placement/handling manifest is ReferenceArena-2026-11-06/Authoring.json.

The following narrow authoring commands and counts describe the5 November world;
do not replay that historical catalog against the rebuilt6 November geometry:
Tools / Road Gunner / Scenery / Install Vehicle Crush Collisions Only
Batch: RoadGunner.Editor.DecorationCollisionInstaller.InstallBatch
Read-only saved-catalog check: DecorationCollisionInstaller.DumpManifestBatch
Do not rerun legacy full art/world installers just to package this saved scene.
Current stone authoring and verification: Documentation/RoadGunner/StoneFix-2026-11-05/.
Size is measured from actual world-space triangle vertices, so rotating a small
rock cannot incorrectly turn it into a hard obstacle. Five previously solid
small boulders now have crush probes, including one near the start.
The catalog preserves all 136 original protected solids. There are now 140
generated and 3 reused hard colliders; the preceding 327 Stone items remain
crushable alongside the five corrected boulders.
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

Edit the common Exploration layout.asset through the current Reference layout
workflow above. Exploration tuning.asset retains reward defaults, reveal/range
settings and counterplay. The earlier exploration installer and its loop-based
geometry remain historical authoring code. Earlier implementation evidence:
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
Tools / Road Gunner / Level Editor opens the scene-based authoring tools.
Per-level settings live under Assets/RoadGunner/Levels/Greenwood/Arena1.
The current player uses walkerDrive=true with instant grounded screen-relative
movement at 9.36m/s, analog stick speed and immediate release/Space/reversal.
Compound collision sweeps stop inward wall motion and permit sliding and exit;
the dynamic body and four hidden suspension contacts retain vertical physics.
Buggies retain their own wheeled profile. The player has no visible tracks/car.

The 9 November feel pass links valid planted feet to one footprint, dust puff
and mechanical footfall. World stance anchors, shortened replants and bounded
pelvis/torso/landing reactions express the immediate movement. The cannon resolves
aim, muzzle, hitscan, flash and tracer after the shoulder pose, with cosmetic
barrel recoil. Harpoon launch/slack/latch/tension/pull/dock follow its real pool
reservation; invalid targets and session transitions clear retrieval feedback.
Presentation tuning: body settle .20s, motion lean 2.2deg, landing compression
.09m, footfall intensity 1; cannon settle .06s, recoil .09m/kick .018s/return .14s;
cable 8 points, slack .16m, tension settle .06s. Gameplay remains gun10m/5Hz/
5.25 base actual damage and harpoon8m/36m/s/30m/s/.08s collection delay.
Narrow audio authoring: RoadGunner.Editor.WalkerFeelInstaller.InstallBatch.
Acceptance: WalkerFeelVerification.Run, CannonFeelVerification.Run,
HarpoonFeelVerification.Run, WalkerArenaRunVerification.Run in RoadGunner.Editor.
Use -batchmode -rgMuteAudio without -quit/-nographics for quiet native checks.
This flag mutes only the test game's listener; it never changes desktop volume
or the published player's audio. Evidence: WalkerFeel-2026-11-09/.

The differential-track speed measurements and torque/yaw-assist setup are
historical: Speed60-2026-11-05/ and TrackedVehicle-2026-11-03/. Do not replay
PlayerTrackVisualInstaller or old car/art/world installers over the walker.
After terrain changes refresh the static map and navigation through the current
ReferenceArena authoring workflow. No regeneration is needed for packaging.

WEB BUILD AND PUBLISHED DEMO
Use Tools / Road Gunner / Web / Build Mobile Web, or batch entry
RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/.
Rebuild WebGL after saving changes to the scene. Gzip with JavaScript
decompression fallback supports static hosting.

WebBuild/ contains the 9 November walker/weapon feel pass, Unity6000.6.4f1.
Seven static artifacts total21,318,552 bytes. All368 native checks pass, with
zero runtime Console errors/exceptions/asserts. The input-only actual-AI arena
run defeats Warlord and settles earned Scrap; actual enemy Defeat and Restart
are also exercised. Controlled fixtures and browser/device limits are disclosed.
Build/input hashes and source preservation:
Documentation/RoadGunner/WebGL-2026-11-09-WalkerFeel/WebBuild.json
Delivery and final native evidence:
Documentation/RoadGunner/WalkerFeel-2026-11-09/Delivery.txt
The earlier 6 November instant walker player981233b and 5 November stone fix
c0934fe remain historical snapshots in their named release folders.
The preceding respawn release 8a38955 is documented in EnemyRespawn-2026-11-05/.
The preceding small-stone release c65fd56 is documented in SmallStones-2026-11-05/.
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
Exact modern reference balance remains unavailable. The6 November layout adapts
observed historical connectivity; it does not claim exact reference coordinates.
Unity caches, local IDE state and generated build
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
