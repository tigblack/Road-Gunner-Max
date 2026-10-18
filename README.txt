ROAD GUNNER

Portrait mobile arcade action game set in a stylized wasteland.
Unity Editor: 6000.6.4f1
Render pipeline: Universal Render Pipeline 17.6.0

Open this repository folder with Unity Hub using the version above. Let Unity import Assets and restore Packages, then open:
Assets/RoadGunner/Scenes/Level01_Greenwood.unity

Press Play. Drive with the on-screen joystick or WASD / arrow keys; Space brakes. Weapons fire automatically at nearby enemies.

The active first zone contains ordinary raiders, two Rust Vulture buggies, the Ashmaw mutant miniboss, and the Iron Hauler arena truck boss. Scene assets, shared prefabs, materials, meshes and gameplay scripts are included.

Assets/            Game assets, scenes, editor tools and runtime code
Packages/          Dependency manifest and lockfile
ProjectSettings/   Unity project settings
AgentScripts/      Asset authoring and gameplay verification helpers
Documentation/     Art direction, implementation notes and validation reports

Latest validation:
Documentation/RoadGunner/Zone1-Enemies-2026-10-17/Delivery.txt
43 Play Mode checks passed in the Unity Editor. Mobile device profiling and a mobile build are not part of that validation.

Unity caches, local IDE state, logs, build outputs and historical backup archives are excluded from Git. Unity recreates its generated folders when the project is opened.
