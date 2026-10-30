ROAD GUNNER — ARENA 1
Unity 6000.6.4f1 • URP 17.6.0 • portrait wasteland vehicle combat

CURRENT START
Open Assets/RoadGunner/Scenes/Level01_Greenwood.unity and press Play.
Use this saved Arena 1 scene for the current game. RoadGunner_Prototype.unity,
Level01_Wasteland.unity and LevelEditor_Demo.unity are earlier reusable scenes
and examples, with different layouts/tuning; their old notes are historical.

The 31 October exploration revision extends the open Arena 1 with Dust Flats,
Salvage Yard, Rustworks and Ash Ridge, alternate routes and optional rewards.
The player's four WheelColliders still produce driving, steering and braking.
The finite enemy roster, free terminals and workshop remain part of the run.

PLAYING THE ARENA
DEFEAT WARLORD to clear Arena 1. Monster Ashmaw is an optional mini-boss.
The saved roster is 12 basic Raiders, two physical Rust Vulture Buggies, one
Monster and one final Boss Raider Warlord. Defeating every enemy is optional;
there is no mandatory 16/16 clear objective or victory timer.

The goal card keeps Warlord's win condition visible. After the first driving
input it points to the nearest live ordinary Raider while KILLS is zero, then
to Warlord. The arrow stays within the card, follows camera-relative bearing
and displays actual metres. The player chooses a route through the open arena.

The white 10 m ring is the player's horizontal gun range. The roof turret seeks
unobstructed targets and fires automatically; there is no separate fire button.
World geometry blocks shots. Ordinary enemies have world health bars; the boss
has a compact panel with health and attack state in the upper HUD area.

CONTROLS
WASD / arrow keys: requested travel direction relative to the fixed camera.
All eight keyboard directions and the floating joystick request nose-first
travel. The wheels turn the car along an arc while it rolls; it does not pivot
its body in place. S / Down requests a forward U-turn toward screen-bottom, not
reverse. Leave room for that manoeuvre. A held joystick takes priority over
keyboard direction.
Touch/mouse drag in the lower-left control region: floating analog joystick;
its magnitude controls desired speed. Release to coast; Space is service brake.
Escape / II: Pause. Resume keeps the run, upgrades and tutorial progress.
R / RECOVER CAR: upright return to the nearest clear recovery point, 3-second
cooldown. Recover itself gives no HP, score or upgrade reset.
RESTART RUN: reset score, kills, upgrades, player health and enemy encounters.

The initial DRAG TO DRIVE gesture and auto-fire explanation disappear after
directional input. The first target prompts a one-time explanation of the white
gun ring. Gear/score and contextual workshop hints follow existing run events;
repair reminders have a cooldown. These graphics do not capture input.

SCORE, FREE UPGRADES AND REPAIR
KILLS is the number of defeated enemies, not the number remaining on the map.
SCRAP SCORE is the run's score. Human Raiders drop Metal Gear worth one Scrap;
Buggies award three, Monster five and Warlord ten. Gear is collected by driving
near it or by the companion harpoon. The final result includes earned rewards
still awaiting collection when Warlord is defeated.

Scrap is not a spendable currency in this demo. Both one-use stat terminals are
free proximity pickups, with a 3.5 m collection radius:
- ATTACK +1: ATK 1 -> 2, +20% damage for this run. The configured reduced base
  bullet damage is 5.25; ATK 2 gives 6.3. The same outgoing scaling covers ram.
- HEALTH +1: HP 1 -> 2, base maximum 100 -> 120. Current HP also rises by 20,
  keeping damage already taken. A damaged 60/100 car becomes 80/120.
An upgrade toast explains the actual benefit and THIS RUN duration. Pause shows
current damage per shot, levels and maximum HP. Restart removes run upgrades.

The workshop has a green wrench minimap marker and a 10 m service area. A living,
damaged player inside it heals for free at 10% of current maximum HP per second
of play. Pause stops repair; the upgraded maximum is used automatically.

The original Arena music loops during play. Pause offers a saved local volume
preference. The run itself has no permanent inventory or saved stat progression.
Developer telemetry remains an opt-in F3 debug view; it is omitted from the
ordinary player-facing controls text.

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

ANTON FEEDBACK INSTALLER
Stop Play Mode and save the open scene before running:
Tools > Road Gunner > Arena 1 > Apply Anton Feedback Improvements
Batch entry: RoadGunner.Editor.AntonFeedbackInstaller.ApplyBatch
Source: Editor/Arena1/AntonFeedbackInstaller.cs

The installer opens Level01_Greenwood and rebuilds its presentation in place:
goal/tutorial card, score and kills labels, score gain feedback, one upgrade
toast, compact boss presentation and readable Pause / Wrecked / Arena Clear.
It keeps the existing circular baked map, music controls and gameplay references.
The portrait HUD is authored at 1080 x 1920 and fitted to the actual safe area.
It saves the scene and writes implementation notes under:
Documentation/RoadGunner/AntonFeedback-2026-10-29/Implementation.txt

Scripts/ArenaGuidance.cs caches enemies, terminals, workshops and camera on a new
run. It reads real DriveInput, turret targets, health and score events. No scene
search runs each frame. GameSession.RunSerial increments on Restart; Pause and
Resume do not reset the tutorial. All guidance graphics are noninteractive.
Scripts/HudView.cs reads the existing session/run stats for displayed values.
Its presentation feedback does not grant upgrades, score, healing or damage.

Legacy rebalance and polish installers have separate scopes. A rebalance may
re-author placements/tuning; a prototype rebuild replaces its generated scene.
If using those tools deliberately, apply the Anton feedback installer followed by the exploration installer to
restore the current HUD and map. The normal saved scene does not execute authoring builders.

TUNING AND PHYSICS
Current per-level assets: Levels/Greenwood/Arena1/, especially
Arena 1 vehicle handling.asset and Arena 1 player combat.asset.
Configured maxSpeed is 11.7 m/s, mass 1400 kg, totalDriveTorque 2700 and requested
acceleration 4.4 m/s2. Tight turns request turningSpeed 4.2 m/s at full input;
analogue magnitude scales the speed request. These are controller settings,
not measured acceleration, turn speed or device performance.
Player outgoing base damage is multiplied by 0.35. The base 15-damage bullet
therefore deals 5.25 before attack upgrades. Shared horizontal gun range is 10 m.

VehicleController uses one interpolated dynamic Rigidbody, four WheelColliders,
bounded anti-roll and rear-biased drive torque. Front-wheel Ackermann angles are
calculated from the actual wheel transforms: wheelbase 3.05 m and track 2.07 m
on Dust Jackal. headingSteerGain maps requested heading to steering;
yawCounterSteer .4 feeds actual yaw rate back into the front-wheel angle.
It does not add a body-yaw rotation or artificial yaw torque.

Ordinary driving does not call MoveRotation, set the transform or replace linear
or angular velocity. Wheel steering and forces produce the turn; tyre braking
reduces speed into tight manoeuvres. Release retains inertial coasting, with
rolling/air resistance applied in the horizontal plane. Vertical suspension,
gravity and impact motion remain with the Rigidbody. Service brake torque is
1100, coast brake torque 50 and low-speed hold torque 450 per wheel. The coast
brake blends toward hold torque near rest. Recover/Restart use ResetPose to
place the car safely and clear its velocities deliberately.
Wheel radius is 0.481 m, suspension travel 0.3 m, spring 35000 and damper 4500.
Edit the per-level settings rather than assuming Settings/Vehicle handling.asset
or other older prototype assets control this Arena's player.

Useful Inspector fields include torqueBySpeed / driveResponseSpeed / turningSpeed;
steeringBySpeed / steeringRate / headingSteerGain / yawCounterSteer /
ackermannStrength; rollingResistance / aerodynamicDrag;
centerOfMass / spring / damper / suspensionTravel / antiRoll; and independent
forwardFriction / sidewaysFriction WheelCollider curves.
rotationSpeed, rotationAcceleration and steeringResponse remain hidden legacy
serialization fields for older installers; they no longer rotate the body.
Revision documentation and subsequent verification belong under:
Documentation/RoadGunner/WheelPhysics-2026-10-30/

PLAYER, LOOT AND ART
Dust Jackal is the existing muscle pickup: long hood, enclosed glazed cabin,
driver, roof machine gun and rear-bed companion. Its rear pedestal harpoon
automatically retrieves nearby landed Gear with a flying tip and cable. Normal
driving pickup stays available. Harpoon launch/retract speeds are configured
as 36 / 30 m/s, with search interval 0.05 s and collection delay 0.08 s.
Pause freezes collection; Recover and Restart cancel a claim safely. The capped
pickup pool merges overflow rewards rather than dropping their value.

Rust Vulture Buggies use Rigidbody / WheelCollider driving guided by NavMesh
routes and obstacle probes. Their roof gun aims independently; cover blocks
bullets. Destruction uses the existing explosion, debris, sound and loot paths.
Finite Arena 1 encounters do not automatically replenish. Older prototype
scenes may have pooled repeating encounters; use their own documentation.

Warm sand, rose sandstone, teal glass, cream armor, rust and raider scarves
define the stylized 3D art. Vehicles, props, lights and effects are real scene
content. Shared materials, combined decoration, pooled tire tracks, dust,
tracers, impacts and Gear support reuse. The feedback pass retains this art.
Existing sounds and procedural meshes require no new paid asset package.

LEVEL EDITOR AND MAP
Tools > Road Gunner > Level Editor opens scene authoring.
LEVEL: open/create a level, Prepare Current Gameplay Scene, Save / Save As,
Place Player Spawn and Play Level.
ENEMIES / OBSTACLES: choose an existing prefab, press Place and click Scene View.
Escape cancels. Unity Move / Rotate / Duplicate / Delete and Undo stay available.
Create a per-instance LEVEL OVERRIDE for native tuning; DEFAULT VALUE retains
the shared settings. Editable player, recovery and encounter markers are saved
with the scene. The existing roster/session remain authoritative at runtime.
TERRAIN: Raise / Lower / Smooth / Flatten with brush radius, strength and Undo.
Saving/playing through the Level Editor can bake navigation after geometry edits.
VALIDATION checks references, bounds, navigation and collisions.
Full authoring documentation: Documentation/RoadGunner/LEVEL_EDITOR.txt.

The circular minimap uses one static top-down bake of actual terrain, roads and
environment, plus live player/enemy/upgrade/workshop markers. No additional
map camera or RenderTexture is required. Refresh the bake after world edits via
the existing Arena minimap authoring tools, then reapply Anton feedback if a
legacy installer rewrites the HUD. Dead enemies and consumed terminals leave
the marker set. MAP KEY explains the symbols.

BUILDING AND VERIFICATION
Use Tools > Road Gunner > Web > Build Mobile Web or batch entry
RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/.
Save/apply the feedback scene before rebuilding. The template supports portrait
input and static hosting with gzip / JavaScript decompression fallback.
WebBuild/ has been rebuilt for the wheel-physics revision and passed gzip,
JavaScript, WASM and Unity data checks. Building locally does not publish the
GitHub Pages demo; the publication record is separate. Pages build 2310eee is
the earlier Anton release with the old handling; its evidence is historical.
Current published wheel-physics release: d6a9ba3, with successful Pages deployment
and matching public-file hashes. See WheelPhysics-2026-10-30/Publication.txt.

Opt-in acceptance tools are under Tools > Road Gunner > Validate. Diagnostic
runners can stage actors, feed the Input System and exercise gameplay systems;
their screenshots are not natural full playthroughs. Consult the report for
the exact snapshot, method and limitations. The earlier Anton feedback revision
recorded 204 runtime checks, zero failures and zero Console errors/asserts:
Documentation/RoadGunner/AntonFeedback-2026-10-29/PlayMode.txt
Those native results and the earlier WebGL gzip/JS/WASM/HTTP checks do not
validate the new wheel model. The unavailable post-build visual browser run
(CUA cgWindowNotFound) also belongs to that earlier release. Use the new
WheelPhysics-2026-10-30/PlayMode.txt and Gameplay.txt for the current results:
20 dedicated physics checks and 204 full gameplay regression checks passed,
with zero failures and zero Console errors/exceptions/asserts. No FPS,
retention or cross-platform physics determinism is asserted here.
Physical-device touch, performance and platform builds need their own evidence.

HISTORICAL REFERENCES
The review motivating this presentation pass, including source/evidence labels:
_anton/road-gunner-unity-2026-10-29/РАЗБОР.md
Earlier documents describe their dated versions, not current acceptance status:
- Documentation/RoadGunner/Arena1-2026-10-24/
- Documentation/RoadGunner/MobileRevision-2026-10-25/
- Documentation/RoadGunner/CasualPolish-2026-10-25/
- Documentation/RoadGunner/LEVEL_EDITOR.txt
- Documentation/RoadGunner/HERO_COMPANION_HARPOON.txt
- Documentation/RoadGunner/RANGED_ENEMIES_AND_GEAR.txt
- Documentation/RoadGunner/ENEMY_BUGGY.txt
- Documentation/RoadGunner/ArtDirection/
- Documentation/RoadGunner/Concept-Wasteland-2026-10-16/
The earlier reference video was inaccessible; no exact The War first-arena
layout/count/balance was verified. This is the documented authored fallback.

EXPLORATION ACCEPTANCE — 31 OCTOBER 2026
The saved revision passed 88 exploration, 204 Arena 1 and 20 wheel-physics
checks: 312 total, zero failures and zero Console errors. See
Documentation/RoadGunner/Exploration-2026-10-31/Delivery.txt and its named reports.
These are controlled Unity fixtures, not FPS measurements or a natural full run.
