ROAD GUNNER

Portrait mobile arcade action game set in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

Open this repository folder with Unity Hub using the version above. Let Unity import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. Drive with the on-screen joystick or WASD / arrow keys; Space brakes. Weapons fire automatically at nearby enemies.

Play the published Web version:
https://tigblack.github.io/Road-Gunner-Max/
Tap PLAY, then use the on-screen joystick. The site is published from gh-pages through GitHub's built-in Pages deployment. See Documentation/RoadGunner/Arena1-2026-10-24/Publication.txt for verification and browser/device testing limits.

Arena 1 contains 12 basic Raiders, two Rust Vulture Buggies, the Ashmaw Monster miniboss, and a final Boss Raider. Drive speed is one third of the previous value; outgoing damage is multiplied by 0.35. One-use ATTACK +1 and HEALTH +1 stations, run stats, a live minimap and an Arena Clear result are integrated with the existing Metal Gear / companion harpoon loop. Exact The War reference data was unavailable; the first-arena layout is an explicitly documented fallback.

Assets/            Game assets, scenes, editor tools and runtime code
Packages/          Dependency manifest and lockfile
ProjectSettings/   Unity project settings
AgentScripts/      Asset authoring and gameplay verification helpers
Documentation/     Art direction, implementation notes and validation reports

Latest validation:
Documentation/RoadGunner/Arena1-2026-10-24/Delivery.txt
51 actual Play Mode checks passed in Unity 6000.6.4f1, with zero Console errors/exceptions. See PlayMode.txt and Installation.txt in the same folder. Physical mobile-device profiling is not part of that validation.

Web publishing:
Use Tools / Road Gunner / Web / Build Mobile Web, or run RoadGunner.EditorTools.WebBuild.Build with -buildTarget WebGL. Output: WebBuild/. Gzip with JavaScript decompression fallback supports static GitHub Pages hosting. Publication.txt records the final build and live URL.

Arena authoring:
Tools / Road Gunner / Arena 1 / Apply First Arena Rebalance updates the existing Greenwood scene. Adjust per-level tuning under Assets/RoadGunner/Levels/Greenwood/Arena1. The existing Level Editor supports moving and tuning all Arena 1 enemy types.

Unity caches, local IDE state, logs, build outputs and historical backup archives are excluded from Git. Unity recreates its generated folders when the project is opened.
