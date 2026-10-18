Road Gunner: joystick reverse fix, 10 October 2026

Problem
The held joystick always disabled Reverse. A rearward drag tried to turn the
nose around instead of backing out, leaving the car pinned against a palisade.

Behavior
The stick still specifies a camera-relative world direction. Pull into the
rear cone of the car (more than 120 degrees from its nose) to engage reverse.
Reverse remains selected until the direction crosses into the front half,
which prevents gear chatter during diagonal drags. Neutral/release clears it.
The reverse steering sign guides the tail toward the requested world heading,
including when the vehicle faces sideways on screen. Existing momentum is
braked before opposite motor torque, using the existing WheelCollider physics.
Keyboard S/down and A/D steering retain their previous behavior.

Implementation
DriveInput.cs: chassis-relative gear selection, source flag and latch reset.
VehicleController.cs: runtime chassis binding and reverse heading steering.
JoystickHeadingChecks.cs: updated checks for the new reverse behavior,
optional clear test origin and Game View focus at each pointer reset.
Runtime binding means existing scene and prefab references need no edits.
Asset hashes confirm only these three C# files changed under Assets/RoadGunner.
Scene, terrain, prefabs, art, tuning, combat and Recover are unchanged.

Validation
Actual Input System mouse events -> UI input module -> FloatingJoystick ->
DriveInput -> WheelCollider vehicle physics. A matching northeast camp fence
reproduction first stayed at screen-local (12.44,42.00), with Reverse false.
After the fix it backed to (18.35,42.00): 5.91 m clear, -5.19 m/s forward speed,
four grounded wheels. No Recover or position reset during the escape.

FinalChecks.txt records 31 meaningful checks with zero failures: fence escape,
eight screen directions, reversing at five chassis headings, diagonal reverse
steering, braking before reversal, dead zone, analog throttle, pointer release,
touch routing/cancellation, keyboard reverse steering, gear stability, pause,
resume and restart. Synthetic touch events use the real UI path in the editor;
a physical phone was not tested. Compilation and final console have no errors.

Early heading-suite attempts are retained for audit. The southern-gate start
blocked a backward diagonal, and the next road start blocked the forward left
turn at a watchtower. Some first-run pointer samples also were not routed.
The final heading suite runs at the open crossroads with Game View refocused
at each reset; all checks pass. The fence reproduction and chassis-state suite
are independent. No changes were made to production driving after the first
successful fence escape.

Restored state
All test input/device presses released, constraints None, testInput false,
spawns and turret enabled, timeScale 1 and a fresh normal session restored.
Play Mode then stopped; Level01_Greenwood remains the active saved scene.

Reproduce
Enter Play Mode in Level01_Greenwood, focus Game View, then use Unity Pipeline
run_script AgentScripts/JoystickReverseFenceCheck.cs with args ["After"].
RunJoystickHeadingChecks.cs and JoystickReverseStateChecks.cs run the other
checks. All helper scripts restore the normal session in finally blocks.

Backup
Documentation/RoadGunner/Backups/Before-Joystick-Reverse-2026-10-10.zip
