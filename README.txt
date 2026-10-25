ROAD GUNNER

Portrait mobile arcade action game set in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

Open this repository folder with Unity Hub using the version above. Let Unity import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. Drive with the on-screen joystick or WASD / arrow keys; Space brakes. Weapons fire automatically at nearby enemies.

Play the published Web version:
https://tigblack.github.io/Road-Gunner-Max/
Tap PLAY, then touch and drag anywhere in the lower-left control area. The site is published from gh-pages through GitHub's built-in Pages deployment. See Documentation/RoadGunner/CorridorRevision-2026-10-25/Publication.txt for verification and browser/device testing limits.

Arena 1 contains 12 basic Raiders, two Rust Vulture Buggies, the Ashmaw Monster miniboss, and a final Boss Raider. Its winding north road has an early right repair workshop, a left boss branch, a visible closed north gate and a far-right Monster pocket. Roads are14m wide, boss branches16m, with broad combat clearings. Three independent destructible HEALTH +1 objects at spawn use the existing run-stat system (HP1→4); ATTACK +1 remains on the left branch.

Drive speed remains11.7m/s; outgoing damage remains multiplied by0.35. Releasing directional input applies explicit planar braking11.5m/s², preserving gravity and the floating camera-relative nose-first controls. Ordinary enemies and upgrades share red dots on the circular512px actual-geography map; workshop uses a green wrench, Monster an orange skull, Boss Raider a red skull, player a white/cyan arrow. Roof gun, white10m range, world health bars, Metal Gear/fast harpoon, companion, driver and saved music preferences remain. The current layout follows the latest supplied brief; exact The War reference placements are not claimed as verified.

Large gameplay props now receive solid primitive colliders through the existing Level Editor; tiny dressing stays nonblocking. The closed north gate has no runtime unlock condition because the project contains no corresponding quest system. See Documentation/RoadGunner/CorridorRevision-2026-10-25/Delivery.md for positions, implementation and verification limits.

Assets/            Game assets, scenes, editor tools and runtime code
Packages/          Dependency manifest and lockfile
ProjectSettings/   Unity project settings
AgentScripts/      Asset authoring and gameplay verification helpers
Documentation/     Art direction, implementation notes and validation reports

Latest validation:
Documentation/RoadGunner/CorridorRevision-2026-10-25/PlayMode.txt
The actual Unity Play Mode report records continuously driven routes, real release stops, representative collision matrices, navigation, upgrades, combat, touch controls, music and completion. Physical mobile-device profiling is not part of that validation.

Web publishing:
Use Tools / Road Gunner / Web / Build Mobile Web, or run RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/. Gzip with JavaScript decompression fallback supports static GitHub Pages hosting. The latest Publication.txt records the final build and live URL.

Arena authoring:
Tools / Road Gunner / Arena 1 / Apply Corridor Routes Collision And Onboarding Revision authors the current Greenwood scene. The older Apply First Arena Rebalance menu restores the earlier layout and should only be used deliberately. Adjust per-level tuning under Assets/RoadGunner/Levels/Greenwood/Arena1. The existing Level Editor supports moving and tuning all Arena 1 enemy types.
Tools / Road Gunner / Arena 1 / Apply Casual Combat UI And Music Polish updates the current scene's controls/feedback/map without relocating encounters. Run it again after changing terrain or roads to refresh the static minimap image.

Unity caches, local IDE state, logs, local test outputs and historical backup archives are excluded from Git. The requested release in WebBuild/ is tracked for publishing. Unity recreates its generated folders when the project is opened.
