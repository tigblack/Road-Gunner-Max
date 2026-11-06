#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RoadGunner
{
    /// <summary>Production DriveInput acceptance on the saved chassis. Setup poses are administrative, movement is not.</summary>
    public sealed class PlayerWalkerInstantPlayChecks : MonoBehaviour
    {
        public const string Folder = "Documentation/RoadGunner/PlayerWalkerInstant-2026-11-06";
        public const string ReportPath = Folder + "/InstantMovement.txt";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        GameSession s; VehicleController car; Keyboard keyboard, originalKeyboard;
        readonly List<string> lines = new List<string>();
        readonly Dictionary<Behaviour, bool> states = new Dictionary<Behaviour, bool>();
        bool saved, restored, originalDamage; float originalTime; int originalFps, originalVsync;
        InputSettings focusSettings;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorInput;
        InputSettings.BackgroundBehavior originalBackground;
        BoxCollider[] chassis; Collider[] gateSolids; Transform gate;
        static readonly Vector2[] Directions = { Vector2.up, new Vector2(1,1).normalized, Vector2.right, new Vector2(1,-1).normalized, Vector2.down, new Vector2(-1,-1).normalized, Vector2.left, new Vector2(-1,1).normalized };
        static readonly string[] DirectionNames = { "up", "up-right", "right", "down-right", "down", "down-left", "left", "up-left" };
        static readonly Key[][] DirectionKeys = { new[]{Key.W}, new[]{Key.W,Key.D}, new[]{Key.D}, new[]{Key.S,Key.D}, new[]{Key.S}, new[]{Key.S,Key.A}, new[]{Key.A}, new[]{Key.W,Key.A} };
        public void Begin() { Directory.CreateDirectory(Folder); StartCoroutine(Guarded()); }
        IEnumerator Guarded()
        {
            lines.Add("Actual saved walker instantaneous movement; Unity " + Application.unityVersion);
            lines.Add("Synthetic InputSystem Keyboard events -> normal DriveInput.Update -> normal VehicleController.FixedUpdate -> actual Rigidbody. No testInput override, manual InputSystem.Update, physics simulation, driving transform or velocity injection. ResetPose is used only between fixtures; enemies are administratively despawned. This is focused regression, not natural playthrough.");
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                var current = stack.Peek(); bool more = false; object value = null;
                try { more = current.MoveNext(); if (more) value = current.Current; }
                catch (Exception e) { Check("fixture exception", false, e.ToString()); break; }
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (value is IEnumerator child) stack.Push(child); else yield return value;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            Restore(); lines.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; no saved scene/assets modified.");
            File.WriteAllLines(ReportPath, lines); Completed = true;
        }
        IEnumerator Run()
        {
            yield return null; yield return null;
            s = GetComponent<GameSession>(); if (!s || !s.vehicle || !s.input) throw new InvalidOperationException("Saved player/session/input missing.");
            car = s.vehicle; originalTime = Time.timeScale; originalFps = Application.targetFrameRate; originalVsync = QualitySettings.vSyncCount; originalDamage = s.health.DamageEnabled; saved = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; s.Restart();
            // Batch Editor focus must not route real queued device events into the Editor's state buffer.
            focusSettings = InputSystem.settings; originalEditorInput = focusSettings.editorInputBehaviorInPlayMode; originalBackground = focusSettings.backgroundBehavior;
            focusSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            focusSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            originalKeyboard = Keyboard.current; keyboard = InputSystem.AddDevice<Keyboard>("Walker acceptance keyboard"); keyboard.MakeCurrent();
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            Disable(s.spawns); Disable(s.turret); Disable(s.respawns); Disable(s.exploration); Disable(car.GetComponent<HarpoonCollector>());
            foreach (var w in s.minimap.workshops) Disable(w);
            QuietRoster(); s.health.DamageEnabled = false; s.input.testInput = false; s.input.Clear();
            chassis = car.GetComponentsInChildren<BoxCollider>(true).Where(b => b.enabled && !b.isTrigger && b.attachedRigidbody == car.Body).ToArray();
            Check("saved player uses opt-in dynamic walker", car.tuning.walkerDrive && !car.Body.isKinematic && chassis.Length > 0 && car.wheels.Length == 4 && car.wheels.All(w => w && w.enabled), "walkerDrive=" + car.tuning.walkerDrive + "; chassis boxes=" + chassis.Length + "; max=" + car.tuning.maxSpeed + "; dt=" + Time.fixedDeltaTime + "; damping=" + car.Body.linearDamping);
            yield return ResetGround(s.startPosition, s.startRotation);
            Check("original suspension reaches real ground", car.WalkerGrounded && car.GroundedWheels >= 2, Motion());
            for (int i = 0; i < Directions.Length; i++)
            {
                yield return ResetGround(s.startPosition, s.startRotation);
                yield return Keys(DirectionKeys[i]);
                Vector3 requested = s.input.ScreenToWorldDirection(Directions[i]);
                bool production = !s.input.testInput && s.input.Magnitude > .99f && Vector3.Dot(s.input.WorldDirection, requested) > .999f;
                yield return new WaitForFixedUpdate();
                Vector3 actual = Flat(car.Body.linearVelocity);
                Check("first Fixed start " + DirectionNames[i], production && car.WalkerGrounded && !car.WalkerBlocked && Vector3.Dot(actual, requested) >= car.tuning.maxSpeed * .9f && Vector3.Dot(Flat(car.Body.rotation * Vector3.forward).normalized, requested) > .98f,
                    "production=" + production + "; along=" + Vector3.Dot(actual, requested) + "; " + Motion());
                yield return Keys(); yield return new WaitForFixedUpdate();
                Check("first Fixed release " + DirectionNames[i], car.HorizontalSpeed <= .1f && s.input.Magnitude == 0 && car.WalkerRequestedVelocity.sqrMagnitude < .001f, Motion());
            }
            yield return ResetGround(s.startPosition, s.startRotation);
            yield return Keys(Key.W); yield return new WaitForFixedUpdate();
            Vector3 forward = s.input.ScreenToWorldDirection(Vector2.up);
            yield return Keys(Key.S); yield return new WaitForFixedUpdate();
            Check("abrupt reversal has no acceleration ramp or turn arc", car.WalkerGrounded && Vector3.Dot(Flat(car.Body.linearVelocity), -forward) >= car.tuning.maxSpeed * .9f && Vector3.Dot(Flat(car.Body.rotation * Vector3.forward).normalized, -forward) > .98f, Motion());
            yield return Keys(Key.S, Key.Space); yield return new WaitForFixedUpdate();
            Check("Space brake stops within first Fixed", s.input.Brake && car.HorizontalSpeed <= .1f, Motion());
            yield return Keys(Key.W); yield return new WaitForFixedUpdate();
            bool movingBeforePause = car.HorizontalSpeed >= car.tuning.maxSpeed * .9f;
            s.SetPaused(true); Vector3 pausedPosition = car.Body.position;
            Check("pause clears actual planar speed synchronously", movingBeforePause && s.State == SessionState.Paused && car.HorizontalSpeed <= .1f, Motion());
            yield return Keys(); yield return new WaitForSecondsRealtime(.15f);
            Check("paused body does not drift", Vector3.Distance(car.Body.position, pausedPosition) < .001f && car.HorizontalSpeed <= .1f, "distance=" + Vector3.Distance(car.Body.position, pausedPosition));
            s.SetPaused(false); yield return null; yield return new WaitForFixedUpdate();
            Check("resume with released input remains stopped", s.Playing && car.HorizontalSpeed <= .1f && s.input.Magnitude == 0, Motion());
            yield return Keys(Key.W); yield return new WaitForFixedUpdate();
            Check("fresh input after resume is immediate", car.WalkerGrounded && Vector3.Dot(Flat(car.Body.linearVelocity), forward) >= car.tuning.maxSpeed * .9f, Motion());
            s.CompleteArena();
            Check("completed state stops synchronously", s.State == SessionState.Completed && !s.input.Enabled && !car.CanDrive && car.HorizontalSpeed <= .1f, Motion());
            yield return Keys(Key.D); yield return new WaitForSecondsRealtime(.1f);
            Check("terminal input cannot restart movement", car.HorizontalSpeed <= .1f && s.input.Magnitude == 0 && !car.CanDrive, Motion());
            yield return Keys(); s.Restart(); QuietRoster(); s.health.DamageEnabled = false;
            yield return ResetGround(s.startPosition, s.startRotation);
            Check("restart restores enabled idle movement without ledger changes", s.Playing && car.CanDrive && s.input.Enabled && car.HorizontalSpeed <= .1f && s.Kills == 0 && s.Scrap == 0 && s.health.Current == s.health.Maximum, Motion());
            // Elevated setup, without changing an active body's velocity. Input must not propel or face an unsupported body.
            yield return Keys(); car.ResetPose(s.startPosition + Vector3.up * 12, s.startRotation); Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Quaternion airborneRotation = car.Body.rotation; float initialY = car.Body.position.y;
            yield return Keys(Key.D); yield return new WaitForFixedUpdate();
            bool airInput = s.input.Magnitude > .99f && car.WalkerRequestedVelocity.magnitude > car.tuning.maxSpeed * .9f;
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Check("airborne input preserves falling motion without propulsion or yaw", airInput && !car.WalkerGrounded && car.GroundedWheels == 0 && car.HorizontalSpeed <= .1f && car.Body.linearVelocity.y < -.1f && car.Body.position.y < initialY && Quaternion.Angle(airborneRotation, car.Body.rotation) < .5f, Motion());
            float falling = car.Body.linearVelocity.y; yield return Keys(Key.Space); yield return new WaitForFixedUpdate();
            Check("airborne brake does not cancel gravity", !car.WalkerGrounded && car.Body.linearVelocity.y < falling && car.HorizontalSpeed <= .1f, "beforeY=" + falling + "; " + Motion());
            yield return ResetGround(s.startPosition, s.startRotation);
            var level = FindAnyObjectByType<LevelLayout>(); gate = level && level.authoredContentRoot ? level.authoredContentRoot.Find("Reference arena/Closed gates/Gate North") : null;
            if (!gate) throw new InvalidOperationException("Saved Reference arena/Closed gates/Gate North missing.");
            gateSolids = gate.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
            var approach = gate.Find("Inside approach"); if (!approach || gateSolids.Length == 0) throw new InvalidOperationException("Saved north gate approach/real solid colliders missing.");
            Vector3 outward = Flat(gate.forward).normalized, tangent = Vector3.Cross(Vector3.up, outward);
            yield return ResetGround(GroundPose(approach.position), Quaternion.LookRotation(outward));
            int blocked = 0; float worstPlane = float.NegativeInfinity, penetration = 0; Vector3 hitStart = car.Body.position;
            yield return Keys(KeysForWorld(outward));
            for (int i = 0; i < Mathf.CeilToInt(3.2f / Time.fixedDeltaTime); i++)
            { yield return new WaitForFixedUpdate(); if (car.WalkerBlocked) blocked++; worstPlane = Mathf.Max(worstPlane, Vector3.Dot(car.Body.position - gate.position, outward)); penetration = Mathf.Max(penetration, GatePenetration()); }
            Check("real saved gate prevents full-speed tunneling", blocked > 0 && Vector3.Distance(hitStart, car.Body.position) > 3 && worstPlane < -.2f && penetration <= .045f,
                "blocked Fixed=" + blocked + "; travel=" + Vector3.Distance(hitStart, car.Body.position) + "; max signed plane=" + worstPlane + "; exact compound penetration=" + penetration + "; " + Motion());
            yield return Keys(KeysForWorld(-outward)); yield return new WaitForFixedUpdate();
            Check("wall contact permits immediate escape", car.WalkerGrounded && Vector3.Dot(Flat(car.Body.linearVelocity), -outward) >= car.tuning.maxSpeed * .8f, Motion());
            Vector3 slideStart = gate.position - outward * 6 - tangent * 6;
            yield return ResetGround(GroundPose(slideStart), Quaternion.LookRotation((outward + tangent).normalized));
            Vector3 beforeSlide = car.Body.position; blocked = 0; worstPlane = float.NegativeInfinity; penetration = 0;
            yield return Keys(KeysForWorld((outward + tangent).normalized));
            for (int i = 0; i < Mathf.CeilToInt(1.5f / Time.fixedDeltaTime); i++)
            { yield return new WaitForFixedUpdate(); if (car.WalkerBlocked) blocked++; worstPlane = Mathf.Max(worstPlane, Vector3.Dot(car.Body.position - gate.position, outward)); penetration = Mathf.Max(penetration, GatePenetration()); }
            Check("diagonal input slides along real gate without bypass", blocked > 0 && Vector3.Dot(car.Body.position - beforeSlide, tangent) > 3 && worstPlane < -.2f && penetration <= .045f,
                "blocked Fixed=" + blocked + "; tangent travel=" + Vector3.Dot(car.Body.position - beforeSlide, tangent) + "; max signed plane=" + worstPlane + "; exact compound penetration=" + penetration + "; " + Motion());
            yield return Keys(); yield return new WaitForFixedUpdate();
            Check("movement fixtures do not award combat or upgrades", s.Kills == 0 && s.Scrap == 0 && s.health.Current == s.health.Maximum && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1, "kills=" + s.Kills + "; scrap=" + s.Scrap + "; HP=" + s.health.Current + "; ATK=" + s.runStats.AttackLevel + "; HP level=" + s.runStats.HealthLevel);
        }
        // Queue events only; the normal InputSystem dynamic update and DriveInput.Update consume them.
        IEnumerator Keys(params Key[] keys) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); yield return null; }
        IEnumerator ResetGround(Vector3 position, Quaternion rotation)
        {
            yield return Keys(); s.input.Clear(); car.ResetPose(position, rotation); Physics.SyncTransforms();
            // ResetPose can leave the previous WheelCollider contact cached for the first solver steps.
            // Require actual settling time and a sustained supported state before testing first-input latency.
            float started = Time.fixedTime, until = Time.realtimeSinceStartup + 2.5f;
            int supportedSteps = 0;
            do
            {
                yield return new WaitForFixedUpdate();
                bool supported = car.WalkerGrounded && car.GroundedWheels >= 2 && Mathf.Abs(car.Body.linearVelocity.y) < .15f;
                supportedSteps = supported ? supportedSteps + 1 : 0;
            }
            while ((Time.fixedTime - started < .6f || supportedSteps < 8) && Time.realtimeSinceStartup < until);
            if (Time.fixedTime - started < .6f || supportedSteps < 8)
                throw new InvalidOperationException("Ground fixture failed to settle after ResetPose: supported Fixed=" + supportedSteps + "; solver seconds=" + (Time.fixedTime - started) + "; " + Motion());
        }
        Vector3 GroundPose(Vector3 position)
        {
            var hits = Physics.RaycastAll(position + Vector3.up * 15, Vector3.down, 40, 1, QueryTriggerInteraction.Ignore);
            var hit = hits.Where(h => h.collider.attachedRigidbody != car.Body && h.normal.y > .7f).OrderByDescending(h => h.point.y).FirstOrDefault();
            if (!hit.collider) throw new InvalidOperationException("No actual ground below gate setup " + position);
            return hit.point + Vector3.up * .8f;
        }
        Key[] KeysForWorld(Vector3 direction)
        {
            int best = 0; float score = -2;
            for (int i = 0; i < Directions.Length; i++) { float dot = Vector3.Dot(s.input.ScreenToWorldDirection(Directions[i]), direction); if (dot > score) { score = dot; best = i; } }
            if (score < .995f) throw new InvalidOperationException("Saved gate is not aligned with eight-direction production input: " + score);
            return DirectionKeys[best];
        }
        float GatePenetration()
        {
            float depth = 0;
            foreach (var box in chassis)
            {
                Quaternion localFrame = Quaternion.Inverse(car.transform.rotation);
                Vector3 origin = car.Body.position + car.Body.rotation * (localFrame * (box.transform.position - car.transform.position));
                Quaternion rotation = car.Body.rotation * localFrame * box.transform.rotation;
                foreach (var solid in gateSolids)
                    if (Physics.ComputePenetration(box, origin, rotation, solid, solid.transform.position, solid.transform.rotation, out var normal, out float distance) && Vector3.Dot(normal, Vector3.up) < .6f) depth = Mathf.Max(depth, distance);
            }
            return depth;
        }
        void QuietRoster() { foreach (var r in s.spawns.pool) if (r) r.Despawn(); foreach (var b in s.spawns.buggyPool) if (b) b.Despawn(); if (s.zone1) s.zone1.DespawnAll(); }
        static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);
        string Motion() => "grounded=" + car.GroundedWheels + "; walkerGrounded=" + car.WalkerGrounded + "; blocked=" + car.WalkerBlocked + "; input=" + s.input.WorldDirection.ToString("F3") + "; requested=" + car.WalkerRequestedVelocity.ToString("F3") + "; resolved=" + car.WalkerResolvedVelocity.ToString("F3") + "; actual=" + car.Body.linearVelocity.ToString("F3");
        void Disable(Behaviour b) { if (b && !states.ContainsKey(b)) { states[b] = b.enabled; b.enabled = false; } }
        void Check(string title, bool pass, string detail) { Checks++; if (!pass) Failures++; lines.Add((pass ? "PASS " : "FAIL ") + title + ": " + detail); File.WriteAllLines(ReportPath, lines); Debug.Log("WALKER INSTANT " + lines[lines.Count - 1]); }
        void Restore()
        {
            if (restored) return; restored = true;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
            if (focusSettings) { focusSettings.backgroundBehavior = originalBackground; focusSettings.editorInputBehaviorInPlayMode = originalEditorInput; focusSettings = null; }
            if (!saved) return;
            Time.timeScale = 1;
            foreach (var state in states) if (state.Key) state.Key.enabled = state.Value;
            if (s) { s.input.testInput = false; s.input.testDirection = Vector2.zero; s.input.testBrake = false; s.input.Clear(); s.health.DamageEnabled = originalDamage; s.Restart(); }
            Time.timeScale = originalTime; Application.targetFrameRate = originalFps; QualitySettings.vSyncCount = originalVsync;
        }
        void OnDestroy() { Restore(); }
    }
}
#endif
