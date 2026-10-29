ROAD GUNNER

Portrait vehicle-combat demo in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

OPEN AND PLAY
Open this repository folder through Unity Hub using the version above. Let Unity
import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. Arena 1 keeps the restored open geography from 7c8b54c and the Anton
feedback HUD. The 30 October wheel-physics revision drives and turns the player
through WheelCollider tyre forces. The current work concerns this Unity game.

CONTROLS AND GOAL
Touch or drag in the lower-left control area, or use WASD / arrow keys, to choose
a camera-relative travel direction. The car steers toward that direction while
rolling forward. S / Down requests a forward U-turn toward screen-bottom; allow
room for its turning arc. It does not select reverse. Joystick magnitude controls
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
Configured player maxSpeed is 11.7 m/s. Tight turns request a manoeuvring speed
of 4.2 m/s at full input, rather than pivoting the body in place. Ackermann
steering uses the actual 3.05 m wheelbase and 2.07 m front track. Yaw countersteer
acts through front-wheel angles. Ordinary driving does not directly set body
rotation or velocity; Recover/Restart still reset pose explicitly. Outgoing base
damage uses multiplier 0.35. The feedback installer authors presentation only.
Wheel-physics revision notes and subsequent evidence belong under:
Documentation/RoadGunner/WheelPhysics-2026-10-30/
After editing terrain or roads, refresh the static map through the existing
minimap authoring workflow and rebake navigation as required by the Level Editor.
Legacy rebalance/prototype/polish commands have their own authoring scopes and
may replace presentation or tuning; apply the Anton feedback installer last
when retaining the current HUD. See Assets/RoadGunner/README.txt for details.

WEB BUILD AND PREVIOUS PUBLISHED DEMO
Use Tools / Road Gunner / Web / Build Mobile Web, or batch entry
RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/.
Rebuild WebGL after saving changes to the scene. Gzip with JavaScript
decompression fallback supports static hosting.

WebBuild/ has been rebuilt with the new wheel-physics revision. Its gzip,
JavaScript, WASM and Unity data checks passed. Publication is recorded separately
under Documentation/RoadGunner/WheelPhysics-2026-10-30/Publication.txt.
Previous demo: https://tigblack.github.io/Road-Gunner-Max/?build=2310eee
That release contains the restored open arena and Anton HUD with the earlier
handling. Its 30 October 2026 (Asia/Yerevan) Pages deployment, artifact identity
and hashes are historical evidence for that build:
Documentation/RoadGunner/AntonFeedback-2026-10-29/Publication.txt
The Anton feedback installer itself does not publish a Web build.

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
acceptance of the new wheel physics. The current revision passed 20 dedicated
physics checks plus 204 full Arena 1 regression checks, with zero failures and
zero Console errors/exceptions/asserts. Reports: WheelPhysics-2026-10-30/PlayMode.txt
and Gameplay.txt. No FPS, retention or cross-platform physics determinism is
asserted here.
Exact The War reference data was unavailable; Arena 1 remains the documented
authored fallback layout. Unity caches, local IDE state and generated build
folders can be recreated by the editor.
