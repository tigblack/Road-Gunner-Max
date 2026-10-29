#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RoadGunner
{
    /// <summary>Opt-in physical regression checks. Fixtures exist only in Play Mode; no scene or tuning is saved.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class WheelPhysicsPlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/WheelPhysics-2026-10-30/PlayMode.txt";
        public const string ArtifactFolder = "outputs/wheel-physics-validation";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }

        [Serializable] public sealed class Trial
        {
            public string name;
            public float fixedStep, duration, startSpeed, endSpeed, distance, pathLength, yaw, peakSlip, maximumTilt, suspensionMotion;
        }
        [Serializable] sealed class Result
        {
            public string unityVersion, scope;
            public int checks, failures;
            public bool completed;
            public List<Trial> trials;
        }
        readonly List<string> report = new List<string>();
        readonly List<Trial> trials = new List<Trial>();
        readonly StringBuilder samples = new StringBuilder();
        readonly Vector3 floorOrigin = new Vector3(0, 800, 0);
        GameSession session;
        VehicleController car;
        Rigidbody body;
        GameObject fixture, wall, curb;
        WheelPhysicsCollisionProbe collisionProbe;
        Keyboard keyboard;
        RigidbodyConstraints originalConstraints;
        float originalStep;
        int originalFrameRate, originalVSync;
        bool originalSpawns, originalTurret, running, restored;
        string phase = "setup";

        public void Begin()
        {
            if (running) return;
            running = true; Completed = false;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            Directory.CreateDirectory(ArtifactFolder);
            report.Add("Wheel-based player physics — actual saved vehicle; Unity " + Application.unityVersion);
            report.Add("Temporary flat track, wall and one-wheel curb. ResetPose is setup only; motion uses the production DriveInput/WheelColliders.");
            report.Add("Impulse case explicitly applies a measured external force. No running-case velocity/rotation replacement. Timesteps are solver rates, not measured display FPS.");
            samples.AppendLine("phase,fixedTime,x,y,z,vx,vy,vz,yaw,tilt,frontSteer,rearSteer,motorTorque,brakeTorque,grounded,slip");
            WriteArtifacts(); StartCoroutine(RunGuarded());
        }

        void FixedUpdate()
        {
            if (!running || !body || car.wheels == null || car.wheels.Length != 4) return;
            Vector3 p = body.position, v = body.linearVelocity;
            samples.Append(phase).Append(',').Append(F(Time.fixedTime)).Append(',')
                .Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append(',')
                .Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(',')
                .Append(F(body.rotation.eulerAngles.y)).Append(',').Append(F(Tilt())).Append(',')
                .Append(F(car.wheels[0].steerAngle)).Append(',').Append(F(car.wheels[2].steerAngle)).Append(',')
                .Append(F(MotorTorque())).Append(',').Append(F(car.wheels[0].brakeTorque)).Append(',')
                .Append(car.GroundedWheels).Append(',').Append(F(car.LargestSlip)).AppendLine();
        }

        IEnumerator RunGuarded()
        {
            yield return null; yield return null;
            var sections = new Func<IEnumerator>[] { Setup, CoastAndSpace, SteeringArcs, NoPivotOrAirSteering, ExternalImpulse, WallAndSuspension, SolverRates };
            foreach (var factory in sections)
            {
                var stack = new Stack<IEnumerator>(); stack.Push(factory());
                bool failed = false;
                while (stack.Count > 0 && !failed)
                {
                    IEnumerator routine = stack.Peek(); bool more = false; object current = null;
                    try { more = routine.MoveNext(); if (more) current = routine.Current; }
                    catch (Exception error) { Check(factory.Method.Name + " exception", false, error.ToString()); failed = true; }
                    if (failed) break;
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested);
                    else yield return current;
                }
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                if (!car || !body) break;
            }
            try { Restore(); }
            catch (Exception error) { Check("restore", false, error.ToString()); }
            report.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; temporary physics fixtures removed; saved scene/tuning not modified.");
            running = false; Completed = true; WriteArtifacts();
        }

        IEnumerator Setup()
        {
            session = GetComponent<GameSession>();
            if (!session || !session.vehicle) throw new InvalidOperationException("Attach checks to the saved GameSession with its player vehicle.");
            car = session.vehicle; body = car.Body;
            originalStep = Time.fixedDeltaTime; originalFrameRate = Application.targetFrameRate; originalVSync = QualitySettings.vSyncCount;
            originalConstraints = body.constraints; originalSpawns = session.spawns.enabled; originalTurret = session.turret.enabled;
            session.Restart(); session.spawns.enabled = false; session.turret.enabled = false; session.health.DamageEnabled = false;
            foreach (var raider in session.spawns.pool) if (raider) raider.Despawn();
            foreach (var buggy in session.spawns.buggyPool) if (buggy) buggy.Despawn();
            if (session.zone1) session.zone1.DespawnAll();
            keyboard = InputSystem.AddDevice<Keyboard>();
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; Time.fixedDeltaTime = 1f / 60;
            fixture = new GameObject("Play Mode wheel-physics fixtures") { hideFlags = HideFlags.DontSave };
            MakeBox("Flat track", floorOrigin - Vector3.up, new Vector3(200, 2, 200));
            wall = MakeBox("Collision wall", floorOrigin + new Vector3(0, 2, -5), new Vector3(14, 4, 1)); wall.SetActive(false);
            // Left wheel only: creates a real staggered compression/roll response, not a camera animation.
            curb = MakeBox("One-wheel curb", floorOrigin + new Vector3(-.95f, .08f, -12), new Vector3(1.4f, .16f, 1)); curb.SetActive(false);
            collisionProbe = car.gameObject.AddComponent<WheelPhysicsCollisionProbe>();
            collisionProbe.hideFlags = HideFlags.DontSave; collisionProbe.Wall = wall.GetComponent<Collider>();
            Check("saved player uses four dynamic physical wheels", !body.isKinematic && originalConstraints == RigidbodyConstraints.None &&
                car.wheels.Length == 4 && Mathf.Abs(body.mass - car.tuning.mass) < .01f && body.mass > 100 &&
                body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic,
                "Mass=" + body.mass + "; constraints=" + originalConstraints + "; CCD=" + body.collisionDetectionMode);
            yield return ResetAt(-45);
            Check("authored suspension settles on four-wheel test ground", car.GroundedWheels >= 3 && car.Speed < .3f && Tilt() < 5 &&
                Array.TrueForAll(car.wheels, wheel => wheel.enabled && wheel.suspensionDistance > .05f && wheel.suspensionSpring.spring > 1000),
                "Contacts=" + car.GroundedWheels + "; speed=" + F(car.Speed) + "; tilt=" + F(Tilt()) + "; fixed step=" + F(Time.fixedDeltaTime));
        }

        IEnumerator CoastAndSpace()
        {
            var results = new Trial[2];
            for (int mode = 0; mode < 2; mode++)
            {
                yield return ResetAt(-45); phase = mode == 0 ? "release-coast" : "space-brake";
                yield return AccelerateTo(6);
                var result = new Trial { name = phase, fixedStep = Time.fixedDeltaTime, startSpeed = HorizontalSpeed() };
                Vector3 start = body.position;
                // Both modes release the direction. Space is queued through the real keyboard input path.
                car.input.testInput = false;
                InputSystem.QueueStateEvent(keyboard, mode == 0 ? new KeyboardState() : new KeyboardState(Key.Space));
                yield return null; yield return null;
                bool actualSpace = car.input.Brake;
                float began = Time.fixedTime;
                while (Time.fixedTime - began < 1.5f) yield return new WaitForFixedUpdate();
                result.duration = Time.fixedTime - began; result.endSpeed = HorizontalSpeed(); result.distance = FlatDistance(start, body.position);
                results[mode] = result; trials.Add(result);
                Check(mode == 0 ? "release preserves natural forward momentum" : "Space uses physical service brakes",
                    result.startSpeed >= 5.5f && (mode == 0 ? !actualSpace && result.endSpeed > 1.5f : actualSpace && result.endSpeed < result.startSpeed * .6f),
                    "Actual Space=" + actualSpace + "; speed=" + F(result.startSpeed) + " -> " + F(result.endSpeed) + "; distance=" + F(result.distance));
            }
            Check("release coasts farther than Space from comparable speed", Mathf.Abs(results[0].startSpeed - results[1].startSpeed) < .7f &&
                results[0].distance > results[1].distance * 1.2f && results[0].endSpeed > results[1].endSpeed + .75f,
                "Coast/brake travel=" + F(results[0].distance) + "/" + F(results[1].distance) + "m; end speeds=" + F(results[0].endSpeed) + "/" + F(results[1].endSpeed));
        }

        IEnumerator SteeringArcs()
        {
            foreach (float request in new[] { 90f, 180f })
            {
                yield return ResetAt(-35); phase = "arc-" + request;
                var start = body.position; var previous = start; var initial = body.rotation;
                Vector3 desired = Quaternion.AngleAxis(request, Vector3.up) * Vector3.forward;
                Command(desired); float began = Time.fixedTime, path = 0, yawBeforeHalfMetre = 0, frontSteer = 0, rearSteer = 0, minimumForward = 0;
                bool reversed = false; float error = 180;
                do
                {
                    yield return new WaitForFixedUpdate();
                    path += FlatDistance(previous, body.position); previous = body.position;
                    float yaw = Vector3.Angle(initial * Vector3.forward, body.rotation * Vector3.forward);
                    if (path < .5f) yawBeforeHalfMetre = Mathf.Max(yawBeforeHalfMetre, yaw);
                    frontSteer = Mathf.Max(frontSteer, Mathf.Abs(car.wheels[0].steerAngle)); rearSteer = Mathf.Max(rearSteer, Mathf.Abs(car.wheels[2].steerAngle));
                    minimumForward = Mathf.Min(minimumForward, car.ForwardSpeed); reversed |= car.input.Reverse;
                    error = Vector3.Angle(Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up), desired);
                } while (error > 20 && Time.fixedTime - began < 16);
                float turned = Vector3.Angle(initial * Vector3.forward, body.rotation * Vector3.forward);
                var result = new Trial { name = phase, fixedStep = Time.fixedDeltaTime, duration = Time.fixedTime - began,
                    endSpeed = HorizontalSpeed(), pathLength = path, distance = FlatDistance(start, body.position), yaw = turned };
                trials.Add(result);
                Check("directional " + request + " degree command follows a physical arc", error <= 20 && path > 1.5f &&
                    path / Mathf.Max(.01f, turned * Mathf.Deg2Rad) > .75f && yawBeforeHalfMetre < 20 && !reversed && minimumForward > -.5f && Tilt() < 25,
                    "Final heading error=" + F(error) + "; path=" + F(path) + "m; mean radius=" + F(path / Mathf.Max(.01f, turned * Mathf.Deg2Rad)) +
                    "m; yaw before0.5m=" + F(yawBeforeHalfMetre) + "; time=" + F(result.duration) + "s (no old pivot-time requirement)");
                Check("front wheel steering supplies the " + request + " degree turn", frontSteer > 5 && rearSteer < .25f,
                    "Peak front/rear wheel steer=" + F(frontSteer) + "/" + F(rearSteer) + "deg");
            }
        }

        IEnumerator NoPivotOrAirSteering()
        {
            yield return ResetAt(-35); phase = "translation-locked-heading";
            body.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
            Vector3 start = body.position; Quaternion initial = body.rotation;
            Command(Vector3.right); float began = Time.fixedTime;
            while (Time.fixedTime - began < 1) yield return new WaitForFixedUpdate();
            float pinnedYaw = Vector3.Angle(initial * Vector3.forward, body.rotation * Vector3.forward);
            Check("heading command cannot pivot a translation-locked chassis", pinnedYaw < 8 && FlatDistance(start, body.position) < .03f &&
                Mathf.Abs(car.wheels[0].steerAngle) > 5 && car.GroundedWheels >= 2,
                "Actual grounded steering with X/Z pinned; yaw=" + F(pinnedYaw) + "deg; travel=" + F(FlatDistance(start, body.position)));
            body.constraints = originalConstraints;
            phase = "airborne-setup";
            car.ResetPose(floorOrigin + new Vector3(0, 8, -35), Quaternion.identity); Physics.SyncTransforms(); session.follow.Snap();
            Command(Vector3.zero, true);
            // GetGroundHit describes the last simulation. First let PhysX refresh the wheel
            // contacts, then let the production FixedUpdate refresh its contact telemetry.
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check("airborne fixture has fresh zero-contact physics state", car.GroundedWheels == 0 &&
                Array.TrueForAll(car.wheels, wheel => !wheel.GetGroundHit(out _)),
                "After two setup physics steps: contacts=" + car.GroundedWheels);
            phase = "airborne-heading";
            Command(Vector3.right); initial = body.rotation; start = body.position; began = Time.fixedTime; int contacts = 0;
            while (Time.fixedTime - began < .45f) { yield return new WaitForFixedUpdate(); contacts = Mathf.Max(contacts, car.GroundedWheels); }
            float airYaw = Vector3.Angle(initial * Vector3.forward, body.rotation * Vector3.forward);
            Check("airborne input cannot steer chassis without tire contact", contacts == 0 && airYaw < 2 && HorizontalSpeed() < .2f &&
                start.y - body.position.y > .3f && body.linearVelocity.y < -.5f,
                "Contacts=" + contacts + "; yaw=" + F(airYaw) + "; horizontal speed=" + F(HorizontalSpeed()) + "; fall=" + F(start.y - body.position.y));
        }

        IEnumerator ExternalImpulse()
        {
            yield return ResetAt(-35); phase = "external-side-impulse"; Command(Vector3.zero);
            Vector3 start = body.position;
            body.AddForceAtPosition(Vector3.right * body.mass * 3, body.worldCenterOfMass + Vector3.forward, ForceMode.Impulse);
            float began = Time.fixedTime, firstSpeed = 0, peakSlip = 0, peakAngular = 0;
            while (Time.fixedTime - began < .35f)
            {
                yield return new WaitForFixedUpdate();
                if (Time.fixedTime - began < .07f) firstSpeed = Mathf.Max(firstSpeed, body.linearVelocity.x);
                peakSlip = Mathf.Max(peakSlip, car.LargestSlip); peakAngular = Mathf.Max(peakAngular, Mathf.Abs(body.angularVelocity.y));
            }
            Check("external impulse retains lateral and angular physical response", firstSpeed > 1.5f && FlatDistance(start, body.position) > .12f &&
                peakAngular > .03f && peakSlip > .005f && Finite(body.linearVelocity) && Tilt() < 35,
                "Applied3m/s lateral impulse via AddForceAtPosition; first speed=" + F(firstSpeed) + "; travel=" + F(FlatDistance(start, body.position)) +
                "; peak angular=" + F(peakAngular) + "; tire slip=" + F(peakSlip));
        }

        IEnumerator WallAndSuspension()
        {
            wall.SetActive(true); Physics.SyncTransforms();
            yield return ResetAt(-25); phase = "physical-wall"; collisionProbe.WallContacts = 0; collisionProbe.MaximumImpulse = 0;
            Command(Vector3.forward); float began = Time.fixedTime, peakSpeed = 0, furthest = body.position.z;
            while (Time.fixedTime - began < 8 && collisionProbe.WallContacts == 0)
            {
                yield return new WaitForFixedUpdate(); peakSpeed = Mathf.Max(peakSpeed, HorizontalSpeed()); furthest = Mathf.Max(furthest, body.position.z);
            }
            Command(Vector3.zero); began = Time.fixedTime;
            while (Time.fixedTime - began < .6f) { yield return new WaitForFixedUpdate(); furthest = Mathf.Max(furthest, body.position.z); }
            Check("wall receives a genuine collision impulse without tunnelling", collisionProbe.WallContacts > 0 && peakSpeed > 3 &&
                collisionProbe.MaximumImpulse > body.mass * .5f && HorizontalSpeed() < peakSpeed * .6f && furthest < wall.transform.position.z && Tilt() < 40,
                "Contacts=" + collisionProbe.WallContacts + "; impulse=" + F(collisionProbe.MaximumImpulse) + "Ns; approach/end speed=" + F(peakSpeed) +
                "/" + F(HorizontalSpeed()) + "; furthest chassisZ=" + F(furthest));
            wall.SetActive(false); curb.SetActive(true); Physics.SyncTransforms();
            yield return ResetAt(-20); phase = "one-wheel-curb";
            var baselines = new float[4];
            for (int i = 0; i < 4; i++) baselines[i] = SuspensionOffset(car.wheels[i]);
            Command(Vector3.forward * .55f); began = Time.fixedTime; float peakTilt = 0, suspensionMotion = 0;
            while (Time.fixedTime - began < 4.5f)
            {
                yield return new WaitForFixedUpdate(); peakTilt = Mathf.Max(peakTilt, Tilt());
                for (int i = 0; i < 4; i++) suspensionMotion = Mathf.Max(suspensionMotion, Mathf.Abs(SuspensionOffset(car.wheels[i]) - baselines[i]));
            }
            Command(Vector3.zero, true); began = Time.fixedTime;
            while (Time.fixedTime - began < 1.5f) yield return new WaitForFixedUpdate();
            trials.Add(new Trial { name = phase, fixedStep = Time.fixedDeltaTime, maximumTilt = peakTilt, suspensionMotion = suspensionMotion, endSpeed = HorizontalSpeed() });
            Check("uneven ground compresses suspension and permits pitch and roll", peakTilt > .35f && peakTilt < 35 && suspensionMotion > .025f &&
                suspensionMotion <= car.tuning.suspensionTravel + .12f && Tilt() < 8 && body.angularVelocity.magnitude < 1.5f && car.GroundedWheels >= 3,
                "Peak tilt=" + F(peakTilt) + "; wheel suspension motion=" + F(suspensionMotion) + "m; settled tilt=" + F(Tilt()) +
                "; settled angular speed=" + F(body.angularVelocity.magnitude));
            curb.SetActive(false);
        }

        IEnumerator SolverRates()
        {
            var rateTrials = new List<Trial>();
            foreach (int rate in new[] { 30, 60, 120 })
            {
                Time.fixedDeltaTime = 1f / rate; yield return ResetAt(-45); phase = "solver-" + rate;
                Vector3 start = body.position; Command(Vector3.forward); float began = Time.fixedTime, slip = 0;
                while (Time.fixedTime - began < 3) { yield return new WaitForFixedUpdate(); slip = Mathf.Max(slip, car.LargestSlip); }
                float speed = HorizontalSpeed(), distance = FlatDistance(start, body.position);
                Quaternion before = body.rotation; Command(Vector3.right); began = Time.fixedTime;
                while (Time.fixedTime - began < 2) yield return new WaitForFixedUpdate();
                float yaw = Vector3.Angle(before * Vector3.forward, body.rotation * Vector3.forward);
                var result = new Trial { name = phase, fixedStep = Time.fixedDeltaTime, startSpeed = speed, distance = distance, yaw = yaw, peakSlip = slip };
                rateTrials.Add(result); trials.Add(result);
                Check("wheel physics remains finite at solver " + rate + "Hz", speed > 2 && distance > 2 && yaw > 5 && yaw < 130 && Tilt() < 30 &&
                    Finite(body.position) && Finite(body.linearVelocity) && car.GroundedWheels >= 2,
                    "Three-second drive distance/speed=" + F(distance) + "m/" + F(speed) + "m/s; following two-second arc=" + F(yaw) + "deg; slip=" + F(slip));
            }
            var baseline = rateTrials[1];
            foreach (var result in new[] { rateTrials[0], rateTrials[2] })
                Check(result.name + " agrees with60Hz physical trajectory", Relative(result.distance, baseline.distance) < .25f &&
                    Relative(result.startSpeed, baseline.startSpeed) < .25f && Mathf.Abs(result.yaw - baseline.yaw) < 30,
                    "Distance deviation=" + F(Relative(result.distance, baseline.distance) * 100) + "%; speed deviation=" +
                    F(Relative(result.startSpeed, baseline.startSpeed) * 100) + "%; yaw difference=" + F(Mathf.Abs(result.yaw - baseline.yaw)) + "deg");
        }

        IEnumerator ResetAt(float z)
        {
            phase = "settling"; body.constraints = originalConstraints;
            car.input.testInput = true; car.input.testDirection = Vector2.zero; car.input.testBrake = true;
            car.input.Enabled = true; car.CanDrive = true;
            if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            car.ResetPose(floorOrigin + new Vector3(0, .8f, z), Quaternion.identity); Physics.SyncTransforms(); session.follow.Snap();
            float began = Time.fixedTime;
            while (Time.fixedTime - began < 1) yield return new WaitForFixedUpdate();
            car.input.testBrake = false;
        }

        IEnumerator AccelerateTo(float speed)
        {
            Command(Vector3.forward); float began = Time.fixedTime;
            while ((HorizontalSpeed() < speed || car.GroundedWheels < 3) && Time.fixedTime - began < 9) yield return new WaitForFixedUpdate();
        }

        void Command(Vector3 world, bool brake = false)
        {
            Vector3 right = Vector3.ProjectOnPlane(car.input.view.transform.right, Vector3.up).normalized;
            Vector3 up = Vector3.ProjectOnPlane(car.input.view.transform.forward, Vector3.up).normalized;
            car.input.testInput = true; car.input.testDirection = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, up));
            car.input.testBrake = brake;
        }

        GameObject MakeBox(string name, Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.SetParent(fixture.transform); box.transform.position = position; box.transform.localScale = size;
            box.hideFlags = HideFlags.DontSave; return box;
        }
        float Tilt() => Vector3.Angle(body.rotation * Vector3.up, Vector3.up);
        float HorizontalSpeed() => Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        float MotorTorque() { float total = 0; foreach (var wheel in car.wheels) total += wheel.motorTorque; return total; }
        static float SuspensionOffset(WheelCollider wheel) { wheel.GetWorldPose(out Vector3 p, out _); return wheel.transform.InverseTransformPoint(p).y; }
        static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        static float Relative(float a, float b) => Mathf.Abs(a - b) / Mathf.Max(.01f, Mathf.Abs(b));
        static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

        void Check(string name, bool pass, string detail)
        {
            Checks++; if (!pass) Failures++;
            string line = (pass ? "PASS " : "FAIL ") + name + ": " + detail;
            report.Add(line); Debug.Log("RG WHEEL PHYSICS " + line); WriteArtifacts();
        }
        void WriteArtifacts()
        {
            File.WriteAllLines(ReportPath, report);
            File.WriteAllText(ArtifactFolder + "/Samples.csv", samples.ToString());
            File.WriteAllText(ArtifactFolder + "/Metrics.json", JsonUtility.ToJson(new Result {
                unityVersion = Application.unityVersion, scope = "Actual player with runtime-only physical fixtures; solver-rate tests are not display-FPS measurements",
                checks = Checks, failures = Failures, completed = Completed, trials = trials }, true));
        }
        void Restore()
        {
            if (restored) return; restored = true;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (collisionProbe) { collisionProbe.enabled = false; Destroy(collisionProbe); }
            if (fixture) { fixture.SetActive(false); Destroy(fixture); }
            if (!session || !car) return;
            Time.timeScale = 1; Time.fixedDeltaTime = originalStep; Application.targetFrameRate = originalFrameRate; QualitySettings.vSyncCount = originalVSync;
            body.constraints = originalConstraints; car.input.testInput = false; car.input.testBrake = false; car.input.Clear();
            session.spawns.enabled = originalSpawns; session.turret.enabled = originalTurret; session.Restart();
        }
        void OnDestroy() { if (running) Restore(); }
    }

    public sealed class WheelPhysicsCollisionProbe : MonoBehaviour
    {
        public Collider Wall;
        public int WallContacts;
        public float MaximumImpulse;
        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider != Wall) return;
            WallContacts++; MaximumImpulse = Mathf.Max(MaximumImpulse, collision.impulse.magnitude);
        }
    }
}
#endif
