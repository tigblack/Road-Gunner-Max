#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Opt-in contact observer; only exists on the temporary verification car.</summary>
    public sealed class CorridorContactProbe : MonoBehaviour
    {
        public Transform target;
        public bool Hit { get; private set; }
        public int Contacts { get; private set; }
        public float FirstImpactSpeed { get; private set; }
        public float OpposingNormal { get; private set; }
        Vector3 approach;
        public void ResetTarget(Transform value, Vector3 direction)
        { target = value; approach = direction.normalized; Hit = false; Contacts = 0; FirstImpactSpeed = OpposingNormal = 0; }
        void OnCollisionEnter(Collision collision) { Observe(collision); }
        void OnCollisionStay(Collision collision) { Observe(collision); }
        void Observe(Collision collision)
        {
            if (!target || !(collision.collider.transform == target || collision.collider.transform.IsChildOf(target))) return;
            if (!Hit) FirstImpactSpeed = collision.relativeVelocity.magnitude;
            Hit = true; Contacts += collision.contactCount;
            for (int i = 0; i < collision.contactCount; i++)
                OpposingNormal = Mathf.Max(OpposingNormal, Vector3.Dot(collision.GetContact(i).normal, -approach));
        }
    }

    public sealed partial class Arena1PlayChecks
    {
        IEnumerator CorridorPhysicsChecks()
        {
            Isolate();
            var body = s.vehicle.Body;
            var main = body.GetComponents<Collider>().Where(c => !(c is WheelCollider) && c.enabled && !c.isTrigger).ToArray();
            Check("player chassis retains physical collision", !body.isKinematic && body.detectCollisions &&
                body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic && main.Length >= 1 &&
                main.All(c => c.attachedRigidbody == body) && !Physics.GetIgnoreLayerCollision(body.gameObject.layer, 0),
                "Root solid colliders=" + main.Length + "; layer=" + body.gameObject.layer +
                "; Default collision enabled=" + !Physics.GetIgnoreLayerCollision(body.gameObject.layer, 0) + "; CCD=" + body.collisionDetectionMode);
            Check("release braking is independently tunable", s.vehicle.tuning.coastDeceleration >= 10 &&
                s.vehicle.tuning.coastDeceleration <= 14 && s.vehicle.tuning.coastStopSpeed <= .15f &&
                body.linearDamping < .2f && Mathf.Abs(s.vehicle.tuning.maxSpeed - 11.7f) < .05f,
                "Coast=" + s.vehicle.tuning.coastDeceleration + "m/s²; settle=" + s.vehicle.tuning.coastStopSpeed +
                "m/s; global damping=" + body.linearDamping + "; forward limit=" + s.vehicle.tuning.maxSpeed);

            // An isolated temporary flat pad removes terrain slopes and unrelated roadside objects from
            // timing/contact measurements. Chassis, suspension, wheels and controller remain production components.
            var fixture = new GameObject("Temporary corridor physics verification");
            fixture.transform.position = new Vector3(0, 150, 0);
            var floor = fixture.AddComponent<BoxCollider>(); floor.size = new Vector3(160, .5f, 300);
            Vector3 top = fixture.transform.position + Vector3.up * .25f;
            var probe = body.gameObject.AddComponent<CorridorContactProbe>();
            CorridorContactProbe buggyProbe = null;
            bool savedBuggyAI = buggy.AIEnabled, savedBuggyGun = buggy.gun && buggy.gun.CanShoot;
            RigidbodyConstraints savedBuggyConstraints = buggy.Body.constraints;
            GameObject obstacle = null;
            try
            {
                // A/B/C: drive the production wheels to actual cruise, then release. Do not seed linear
                // velocity for stopping: a stationary tyre RPM would manufacture excessive initial slip.
                float[] speeds = { 3, 10.5f, s.vehicle.tuning.maxSpeed };
                string[] labels = { "A low", "B normal", "C maximum" };
                float normalEntry = 0;
                for (int index = 0; index < speeds.Length; index++)
                {
                    Isolate(); body.constraints = carConstraints;
                    PlaceCar(top + Vector3.back * 100 + Vector3.up * .7f, Quaternion.identity);
                    s.input.testBrake = false;
                    yield return new WaitForSecondsRealtime(.7f);
                    // Low speed uses the real input curve. Normal/max use full drive; release normal
                    // while passing cruise, then measure max at the controller's natural full-input plateau.
                    Drive(Vector3.forward, index == 0 ? RawDriveMagnitude(speeds[index]) : 1);
                    yield return null;
                    float drivingAt = Time.time, lastSampleAt = drivingAt, lastSampleSpeed = 0;
                    bool plateau = false;
                    while (Time.time - drivingAt < 12)
                    {
                        yield return new WaitForFixedUpdate();
                        float current = PlanarSpeed(body), drivingTime = Time.time - drivingAt;
                        if (Time.time - lastSampleAt >= 1)
                        { plateau = Mathf.Abs(current - lastSampleSpeed) < .1f; lastSampleAt = Time.time; lastSampleSpeed = current; }
                        if (index == 0 && drivingTime >= 3 && current >= 2.8f) break;
                        if (index == 1 && drivingTime >= 3 && current >= 10.2f) break;
                        if (index == 2 && drivingTime >= 8 && plateau) break;
                    }
                    float entry = PlanarSpeed(body), drivenFor = Time.time - drivingAt;
                    if (index == 1) normalEntry = entry;
                    bool entered = index == 0 ? entry >= 2.8f : index == 1 ? entry >= 10.2f : entry >= 10.3f && entry + .05f >= normalEntry && drivenFor >= 8;
                    Check("real driven release entry " + labels[index], entered && s.vehicle.GroundedWheels >= 3,
                        "Target cruise=" + speeds[index].ToString("F2") + "m/s; commanded ceiling=" + s.vehicle.tuning.maxSpeed +
                        "; actual driven entry=" + entry.ToString("F3") + "; wheel-driving=" + drivenFor.ToString("F2") +
                        "s; full input=" + (index > 0) + "; plateau=" + plateau + "; no linear-velocity boost");
                    s.input.testDirection = Vector2.zero; s.input.testBrake = false;
                    yield return null;
                    float began = Time.time, distance = 0, previous = PlanarSpeed(body), maximumRise = 0;
                    Vector3 start = body.position;
                    while (PlanarSpeed(body) > .2f && Time.time - began < 1.6f)
                    {
                        yield return new WaitForFixedUpdate();
                        float speed = PlanarSpeed(body);
                        maximumRise = Mathf.Max(maximumRise, speed - previous); previous = speed;
                        distance = Vector3.ProjectOnPlane(body.position - start, Vector3.up).magnitude;
                    }
                    float elapsed = Time.time - began;
                    bool timing = index == 0 ? elapsed > .05f && elapsed < .55f : elapsed >= .75f && elapsed <= 1.2f;
                    Check("release stop " + labels[index], timing && PlanarSpeed(body) <= .2f && maximumRise < .12f &&
                        s.vehicle.wheels.All(w => Mathf.Abs(w.motorTorque) < .001f),
                        "Actual driven entry=" + entry.ToString("F2") + "m/s; stop=" + elapsed.ToString("F3") +
                        "s; distance=" + distance.ToString("F2") + "m; residual=" + PlanarSpeed(body).ToString("F3") +
                        "; maximum speed rise=" + maximumRise.ToString("F3") + "; wheel motor=0 after release");
                }

                // Literal D: build wheel speed, command a real turn, and release while the chassis
                // is visibly changing heading. Keep the seeded sideways-momentum stress below as well.
                Isolate(); body.constraints = carConstraints;
                PlaceCar(top + Vector3.back * 70 + Vector3.up * .7f, Quaternion.identity); s.input.testBrake = false;
                Drive(Vector3.forward);
                yield return new WaitForSecondsRealtime(3);
                float turningCruise = PlanarSpeed(body);
                Vector3 beforeTurn = Vector3.ProjectOnPlane(s.vehicle.transform.forward, Vector3.up).normalized;
                Drive(Quaternion.Euler(0, 90, 0) * beforeTurn);
                yield return new WaitForSecondsRealtime(.35f);
                float actualYaw = Mathf.Abs(Vector3.SignedAngle(beforeTurn,
                    Vector3.ProjectOnPlane(s.vehicle.transform.forward, Vector3.up), Vector3.up));
                float turnReleaseEntry = PlanarSpeed(body);
                Drive(Vector3.zero, 0); yield return null;
                float naturalTurnAt = Time.time;
                yield return new WaitForFixedUpdate();
                float firstReleasedStep = PlanarSpeed(body);
                while (PlanarSpeed(body) > .2f && Time.time - naturalTurnAt < 1.4f) yield return new WaitForFixedUpdate();
                float naturalTurnStop = Time.time - naturalTurnAt;
                Check("release stop D while actually turning", turningCruise > 3 && actualYaw > 10 &&
                    turnReleaseEntry > 1 && firstReleasedStep > .2f && firstReleasedStep > turnReleaseEntry * .4f &&
                    naturalTurnStop > .05f && naturalTurnStop <= 1.2f && PlanarSpeed(body) <= .2f &&
                    Mathf.Abs(Vector3.Dot(body.linearVelocity, s.vehicle.transform.right)) <= .2f && !s.input.Reverse,
                    "Real3s cruise=" + turningCruise.ToString("F2") + "m/s; actual yaw during.35s turn=" + actualYaw.ToString("F1") +
                    "deg; release entry=" + turnReleaseEntry.ToString("F2") + "; first released step=" + firstReleasedStep.ToString("F2") +
                    "; stop=" + naturalTurnStop.ToString("F3") + "s; final planar=" + PlanarSpeed(body).ToString("F3") +
                    "; no reverse=" + !s.input.Reverse + "; no velocity seed during cruise/turn/release");

                // D: release after a turn must remove sideways drift as well as forward momentum.
                Isolate(); body.constraints = carConstraints;
                PlaceCar(top + Vector3.up * .7f, Quaternion.Euler(0, 35, 0)); s.input.testBrake = false;
                yield return new WaitForSecondsRealtime(.7f);
                Drive(s.vehicle.transform.forward);
                yield return null;
                yield return new WaitForFixedUpdate();
                s.input.testDirection = Vector2.zero; s.input.testBrake = false;
                yield return null;
                body.linearVelocity = s.vehicle.transform.forward * 8 + s.vehicle.transform.right * 5 + Vector3.up * body.linearVelocity.y;
                float turningStart = Time.time;
                while (PlanarSpeed(body) > .2f && Time.time - turningStart < 1.4f) yield return new WaitForFixedUpdate();
                Check("release stop D includes turn drift", PlanarSpeed(body) <= .2f && Time.time - turningStart <= 1.2f &&
                    Mathf.Abs(Vector3.Dot(body.linearVelocity, s.vehicle.transform.right)) < .2f,
                    "8m/s forward +5m/s sideways; stop=" + (Time.time - turningStart).ToString("F3") +
                    "s; residual planar=" + PlanarSpeed(body).ToString("F3"));

                // Literal E: real wheel cruise, actual release, and renewed input before coming to rest.
                Isolate(); body.constraints = carConstraints;
                PlaceCar(top + Vector3.back * 70 + Vector3.up * .7f, Quaternion.identity); s.input.testBrake = false;
                Drive(Vector3.forward);
                yield return new WaitForSecondsRealtime(3.5f);
                float cruiseBeforeRelease = PlanarSpeed(body);
                Drive(Vector3.zero, 0);
                yield return new WaitForSecondsRealtime(.35f);
                float resumeSpeed = PlanarSpeed(body); Drive(Vector3.forward);
                yield return null;
                yield return new WaitForFixedUpdate();
                bool motorResumed = s.vehicle.wheels.Any(w => w.motorTorque > 1);
                yield return new WaitForSecondsRealtime(.8f);
                Check("release stop E resumes during actual cruise deceleration", cruiseBeforeRelease > 3 && resumeSpeed > .2f &&
                    cruiseBeforeRelease > resumeSpeed + .5f && motorResumed && PlanarSpeed(body) > resumeSpeed + .3f &&
                    !s.input.Reverse && Vector3.Dot(s.vehicle.transform.forward, body.linearVelocity.normalized) > .9f,
                    "Real3.5s cruise=" + cruiseBeforeRelease.ToString("F2") + "m/s; after.35s release=" + resumeSpeed.ToString("F2") +
                    "; after.8s resumed drive=" + PlanarSpeed(body).ToString("F2") + "m/s; motor resumed=" + motorResumed +
                    "; nose-first=" + !s.input.Reverse + "; no velocity seed during cruise/release/resume");

                Isolate(); body.constraints = carConstraints;
                PlaceCar(top + Vector3.up * 8, Quaternion.identity); s.input.testBrake = false;
                yield return null;
                yield return new WaitForFixedUpdate();
                body.linearVelocity = new Vector3(4, -2, 3);
                yield return new WaitForFixedUpdate();
                Check("release braking preserves falling velocity", body.linearVelocity.y < -1.9f &&
                    PlanarSpeed(body) < 5 && PlanarSpeed(body) > 4,
                    "Airborne velocity=" + body.linearVelocity + "; vertical gravity was not flattened by planar braking");

                var corridor = layout.GetComponent<ArenaCorridorLayout>();
                var band = corridor ? corridor.blockers.OfType<BoxCollider>().Where(c => c && c.enabled && !c.isTrigger &&
                    Mathf.Min(c.size.x, c.size.z) >= 4 && Mathf.Max(c.size.x, c.size.z) <= 24)
                    .OrderBy(c => c.size.x * c.size.z).FirstOrDefault() : null;
                var barrier = CollisionSource("Rusted corrugated barricade");
                if (!barrier) barrier = CollisionSource("Corrugated fence");
                GameObject[] sources = { CollisionSource("Boulder"), CollisionSource("Abandoned pickup"), barrier,
                    CollisionSource("Weathered concrete"), band ? band.gameObject : null };
                string[] kinds = { "rock", "wreck", "thin corrugated barricade", "concrete wall", "corridor arena boundary" };
                float[] impactSpeeds = { 3, 10.5f, s.vehicle.tuning.maxSpeed, s.vehicle.tuning.maxSpeed };
                for (int type = 0; type < sources.Length; type++)
                {
                    var source = sources[type];
                    var prefab = source ? PrefabUtility.GetCorrespondingObjectFromSource(source) : null;
                    Check("actual obstacle fixture " + kinds[type], source,
                        source ? "Actual saved scene source " + SceneHierarchyPath(source.transform) +
                            (prefab ? "; asset=" + AssetDatabase.GetAssetPath(prefab) : "; edit-mode prefab metadata unavailable in Play") :
                        "Missing configured " + kinds[type] + " with an enabled nontrigger collider");
                    if (!source) continue;
                    for (int index = 0; index < impactSpeeds.Length; index++)
                    {
                        Isolate(); body.constraints = carConstraints;
                        obstacle = CloneCollisionFixture(source, fixture.transform, top);
                        var colliders = obstacle.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
                        var bounds = SolidBounds(obstacle);
                        Vector3 heading = Quaternion.Euler(0, index == 3 ? 35 : 0, 0) * Vector3.forward;
                        // Aim the approach line at the actual front face. This retains the prefab's real
                        // dimensions, including the finite narrow rock, rather than making it an endless wall.
                        PlaceCar(new Vector3(bounds.center.x - heading.x / heading.z * 5.2f,
                            top.y + .7f, bounds.min.z - 5.2f), Quaternion.LookRotation(heading));
                        s.input.testBrake = false;
                        yield return new WaitForSecondsRealtime(.5f);
                        Drive(heading, RawDriveMagnitude(impactSpeeds[index])); yield return null; yield return new WaitForFixedUpdate();
                        body.linearVelocity = heading * impactSpeeds[index] + Vector3.up * body.linearVelocity.y;
                        probe.ResetTarget(obstacle.transform, heading);
                        float observation = type >= 2 && index != 3 ? 1 : .25f;
                        float deadline = Time.time + 2.6f, contactAt = -1, farthest = body.position.z;
                        while (Time.time < deadline && (contactAt < 0 || Time.time - contactAt < observation))
                        {
                            yield return new WaitForFixedUpdate();
                            farthest = Mathf.Max(farthest, body.position.z);
                            if (probe.Hit && contactAt < 0) contactAt = Time.time;
                        }
                        Check("solid impact " + kinds[type] + " / " + (index == 3 ? "diagonal maximum" : new[] { "low", "normal", "maximum" }[index]),
                            probe.Hit && probe.Contacts > 0 && probe.OpposingNormal > .3f &&
                            probe.FirstImpactSpeed >= impactSpeeds[index] * (index == 0 ? .8f : .9f) && farthest < bounds.min.z + .4f &&
                            colliders.All(c => c.gameObject.layer == 0 && !c.isTrigger),
                            "Seeded entry=" + impactSpeeds[index].ToString("F2") + "m/s; actual impact=" + probe.FirstImpactSpeed.ToString("F2") +
                            "; contacts=" + probe.Contacts + "; opposing normal=" + probe.OpposingNormal.ToString("F2") +
                            "; farthest center=" + farthest.ToString("F2") + "; front=" + bounds.min.z.ToString("F2") +
                            "; heading=" + (index == 3 ? 35 : 0) + "deg; physical contact observed for " + observation + "s");
                        Destroy(obstacle); obstacle = null;
                        yield return null;
                    }
                }

                // The north gate is checked in its saved world position, not only as a cloned test fixture.
                Isolate(); body.constraints = carConstraints;
                var gateBox = corridor ? corridor.gateColliders.OfType<BoxCollider>().Where(c => c && c.enabled && !c.isTrigger)
                    .OrderByDescending(c => c.size.x * c.size.y).FirstOrDefault() : null;
                Check("actual north gate collision fixture", gateBox && corridor.lockedGate && gateBox.transform.IsChildOf(corridor.lockedGate),
                    gateBox ? "Saved closed gate central plate: " + gateBox.name : "Missing saved closed gate plate");
                if (gateBox)
                {
                    Vector3 normal = Vector3.ProjectOnPlane(gateBox.transform.forward, Vector3.up).normalized;
                    Vector3 front = gateBox.transform.TransformPoint(gateBox.center - Vector3.forward * gateBox.size.z * .5f);
                    PlaceCar(Ground(front - normal * 5.2f) + Vector3.up * .7f, Quaternion.LookRotation(normal));
                    s.input.testBrake = false;
                    yield return new WaitForSecondsRealtime(.5f);
                    Drive(normal); yield return null; yield return new WaitForFixedUpdate();
                    body.linearVelocity = normal * s.vehicle.tuning.maxSpeed + Vector3.up * body.linearVelocity.y;
                    probe.ResetTarget(corridor.lockedGate, normal);
                    float deadline = Time.time + 2.6f, contactAt = -1, penetration = Vector3.Dot(body.position - front, normal);
                    while (Time.time < deadline && (contactAt < 0 || Time.time - contactAt < 1))
                    {
                        yield return new WaitForFixedUpdate();
                        penetration = Mathf.Max(penetration, Vector3.Dot(body.position - front, normal));
                        if (probe.Hit && contactAt < 0) contactAt = Time.time;
                    }
                    Check("saved north gate blocks maximum-speed approach", probe.Hit && probe.Contacts > 0 && probe.OpposingNormal > .7f &&
                        probe.FirstImpactSpeed > s.vehicle.tuning.maxSpeed * .9f && penetration < .4f &&
                        corridor.lockedGate.gameObject.activeInHierarchy && gateBox.enabled,
                        "Actual impact=" + probe.FirstImpactSpeed.ToString("F2") + "m/s; contacts=" + probe.Contacts +
                        "; furthest center vs front plane=" + penetration.ToString("F2") + "m; gate remains solid/closed after1s pushing");
                    Capture("closed-north-gate-physics");
                }

                // Exercise the existing Buggy chassis too. AI is disabled, but its dynamic body,
                // WheelColliders and real obstacle contacts remain enabled; no post-launch teleporting.
                Isolate();
                Check("Buggy obstacle fixture", barrier, "Configured scene corrugated barricade is used by the existing Buggy body");
                if (barrier)
                {
                    obstacle = CloneCollisionFixture(barrier, fixture.transform, top);
                    var bounds = SolidBounds(obstacle);
                    buggy.Spawn(new Vector3(bounds.center.x, top.y, bounds.min.z - 2.5f), Quaternion.identity, buggy.SpawnSlot);
                    buggy.AIEnabled = false; if (buggy.gun) buggy.gun.CanShoot = false;
                    buggy.Body.constraints = savedBuggyConstraints;
                    Physics.SyncTransforms();
                    yield return new WaitForSecondsRealtime(.5f);
                    buggyProbe = buggy.gameObject.AddComponent<CorridorContactProbe>();
                    buggyProbe.ResetTarget(obstacle.transform, Vector3.forward);
                    buggy.Body.linearVelocity = Vector3.forward * buggy.tuning.maxSpeed + Vector3.up * buggy.Body.linearVelocity.y;
                    float deadline = Time.time + 1.2f, contactAt = -1, farthest = buggy.Body.position.z;
                    while (Time.time < deadline && (contactAt < 0 || Time.time - contactAt < .3f))
                    {
                        // AI-off applies a parking brake. A small known acceleration offsets that brake
                        // during the short seeded approach, while collision resolution stays entirely physical.
                        if (!buggyProbe.Hit) buggy.Body.AddForce(Vector3.forward * 10, ForceMode.Acceleration);
                        yield return new WaitForFixedUpdate();
                        farthest = Mathf.Max(farthest, buggy.Body.position.z);
                        if (buggyProbe.Hit && contactAt < 0) contactAt = Time.time;
                    }
                    Check("existing Buggy maximum-speed body hits solid barricade", buggy.Alive && !buggy.AIEnabled &&
                        !buggy.Body.isKinematic && buggy.Body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic &&
                        buggyProbe.Hit && buggyProbe.Contacts > 0 && buggyProbe.OpposingNormal > .7f &&
                        buggyProbe.FirstImpactSpeed >= buggy.tuning.maxSpeed * .8f && farthest < bounds.min.z + .3f &&
                        (!buggy.gun || !buggy.gun.CanShoot),
                        "Buggy limit=" + buggy.tuning.maxSpeed + "m/s; actual impact=" + buggyProbe.FirstImpactSpeed.ToString("F2") +
                        "; contacts=" + buggyProbe.Contacts + "; center/front=" + farthest.ToString("F2") + "/" + bounds.min.z.ToString("F2") +
                        "; dynamic body retained; AI/gun off");
                }
            }
            finally
            {
                if (obstacle) Destroy(obstacle);
                if (probe) Destroy(probe);
                if (buggyProbe) Destroy(buggyProbe);
                Destroy(fixture);
                Isolate();
                buggy.AIEnabled = savedBuggyAI; if (buggy.gun) buggy.gun.CanShoot = savedBuggyGun;
                if (buggy.Body) buggy.Body.constraints = savedBuggyConstraints;
            }
        }

        static float PlanarSpeed(Rigidbody body) => new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
        float RawDriveMagnitude(float speed)
        {
            float fraction = Mathf.Clamp01(speed / s.vehicle.tuning.maxSpeed);
            return s.input.joystickDeadZone + (1 - s.input.joystickDeadZone) * Mathf.Pow(fraction, 1 / s.input.inputResponseExponent);
        }
        static string SceneHierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
        static Bounds SolidBounds(GameObject obstacle)
        {
            var colliders = obstacle.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            var bounds = colliders[0].bounds; foreach (var collider in colliders) bounds.Encapsulate(collider.bounds);
            return bounds;
        }
        static GameObject CloneCollisionFixture(GameObject source, Transform fixture, Vector3 top)
        {
            var clone = Instantiate(source, fixture); clone.name = "Verification " + source.name; clone.SetActive(true);
            clone.transform.SetPositionAndRotation(top + Vector3.forward * 14, Quaternion.identity);
            clone.transform.localScale = source.transform.lossyScale;
            Physics.SyncTransforms();
            clone.transform.position += Vector3.up * (top.y - SolidBounds(clone).min.y + .01f);
            Physics.SyncTransforms(); return clone;
        }
        GameObject CollisionSource(string kind)
        {
            // Play-mode scene copies may have no editable prefab linkage. Identify the preserved source
            // root by its exact authored name (or Unity's numbered duplicate suffix), not an arbitrary child.
            return layout.gameObject.scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => IsCollisionSourceName(t.name, kind) && !t.GetComponentInParent<CombatTarget>() &&
                    !t.GetComponentInParent<VehicleController>() && t.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                .Select(t => t.gameObject)
                .FirstOrDefault(g => g.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger));
        }
        static bool IsCollisionSourceName(string value, string kind)
        {
            if (value == kind) return true;
            string prefix = kind + " (";
            return value.StartsWith(prefix, System.StringComparison.Ordinal) && value.EndsWith(")", System.StringComparison.Ordinal) &&
                int.TryParse(value.Substring(prefix.Length, value.Length - prefix.Length - 1), out _);
        }
    }
}
#endif
