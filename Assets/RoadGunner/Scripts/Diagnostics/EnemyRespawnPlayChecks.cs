#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadGunner
{
    /// <summary>Opt-in saved-roster death/respawn tests. Never calls QueueDeath or a spawn method to satisfy a case.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class EnemyRespawnPlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/EnemyRespawn-2026-11-05/PlayMode.txt";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        const float FixtureDelay = 2;
        readonly List<string> report = new List<string>();
        readonly Dictionary<Behaviour, bool> componentStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Raider, bool> humanAi = new Dictionary<Raider, bool>();
        readonly Dictionary<EnemyBuggy, bool> buggyAi = new Dictionary<EnemyBuggy, bool>();
        GameSession s;
        ArenaEnemyRespawns scheduler;
        CombatTarget[] roster;
        Raider human, boss;
        EnemyBuggy buggy;
        WastelandMutant monster;
        CombatTarget activeAiTarget;
        GameObject wall;
        RigidbodyConstraints constraints;
        float originalDelay, originalRetry, originalTimeScale;
        bool originalMonsterAi, originalDamageEnabled, originalCanDrive, originalInputEnabled;
        bool originalTestInput, originalTestBrake, running, restored, captured;
        Vector2 originalTestDirection;

        public void Begin()
        {
            if (running) return;
            running = true;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            report.Add("Enemy respawn — actual saved Arena 1 actors; Unity " + Application.unityVersion);
            report.Add("Fixture: delay temporarily2s instead of30s, AI/auto-gun isolated, real damage/death hooks, scaled Playing time, real map navigation and colliders. Player/one Raider are repositioned only for setup. This is not a natural playthrough.");
            Write(); StartCoroutine(Guarded());
        }

        IEnumerator Guarded()
        {
            yield return null; yield return null;
            foreach (var factory in new Func<IEnumerator>[] { Setup, RaiderCountdown, BuggyReset, MonsterReset, BlockedSites, TerminalStates })
            {
                var stack = new Stack<IEnumerator>(); stack.Push(factory()); bool failed = false;
                while (stack.Count > 0 && !failed)
                {
                    var part = stack.Peek(); bool more = false; object current = null;
                    try { more = part.MoveNext(); if (more) current = part.Current; }
                    catch (Exception error) { Check(factory.Method.Name + " exception", false, error.ToString()); failed = true; }
                    if (failed) break;
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested); else yield return current;
                }
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                if (!captured) break;
            }
            try { Restore(); } catch (Exception error) { Check("restore", false, error.ToString()); }
            report.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; original delay/settings restored, saved scene not written.");
            running = false; Completed = true; Write();
        }

        IEnumerator Setup()
        {
            s = GetComponent<GameSession>();
            if (!s || !s.respawns) throw new InvalidOperationException("Saved GameSession must have its production ArenaEnemyRespawns.");
            scheduler = s.respawns;
            roster = s.spawns.authoredRaiders.Cast<CombatTarget>().Concat(s.spawns.authoredBuggies).Concat(s.zone1.ExtraTargets).ToArray();
            human = s.spawns.authoredRaiders.Where(r => r && !r.arenaBoss)
                .OrderByDescending(r => FlatDistance(r.transform.position, s.startPosition)).First();
            buggy = s.spawns.authoredBuggies.First(); monster = s.zone1.ExtraTargets.OfType<WastelandMutant>().Single();
            boss = s.zone1.ExtraTargets.OfType<Raider>().Single(r => r.arenaBoss);
            originalDelay = scheduler.respawnDelaySeconds; originalRetry = scheduler.retryInterval;
            originalTimeScale = Time.timeScale; constraints = s.vehicle.Body.constraints;
            originalDamageEnabled = s.health.DamageEnabled; originalCanDrive = s.vehicle.CanDrive;
            originalInputEnabled = s.input.Enabled; originalTestInput = s.input.testInput;
            originalTestDirection = s.input.testDirection; originalTestBrake = s.input.testBrake;
            foreach (var r in roster.OfType<Raider>()) humanAi[r] = r.AIEnabled;
            foreach (var b in roster.OfType<EnemyBuggy>()) buggyAi[b] = b.AIEnabled;
            originalMonsterAi = monster.AIEnabled;
            captured = true;
            SaveDisable(s.turret); SaveDisable(s.exploration); SaveDisable(s.vehicle.GetComponent<HarpoonCollector>());
            foreach (var station in s.runStats.stations) SaveDisable(station);
            foreach (var workshop in s.minimap.workshops) SaveDisable(workshop);
            foreach (var b in roster.OfType<EnemyBuggy>()) SaveDisable(b.gun);
            Check("saved roster has15 eligible actors and a nonrespawning final boss", s.Playing && scheduler.isActiveAndEnabled &&
                scheduler.session == s && Mathf.Abs(originalDelay - 30) < .001f && roster.Length == 16 && roster.Distinct().Count() == 16 &&
                roster.All(t => t && t.Alive) && scheduler.RegisteredCount == 15 && scheduler.Entries.Count == 15 &&
                scheduler.Entries.Count(e => e.Target is Raider r && !r.arenaBoss) == 12 &&
                scheduler.Entries.Count(e => e.Target is EnemyBuggy) == 2 && scheduler.Entries.Count(e => e.Target is WastelandMutant) == 1 &&
                scheduler.Find(boss) == null && !s.spawns.repeatAuthoredEnemies && s.spawns.GroupCount == 0,
                "Roster=" + roster.Length + "; registered=" + scheduler.RegisteredCount + "; production delay=" + originalDelay + "; boss excluded=" + (scheduler.Find(boss) == null));
            scheduler.respawnDelaySeconds = FixtureDelay;
            FreshRun(); yield return null;
        }

        IEnumerator RaiderCountdown()
        {
            FreshRun(); Vector3 home = human.transform.position;
            Vector3 moved = ClearRaiderSite(human);
            if (!human.agent.Warp(moved)) throw new InvalidOperationException("Could not place Raider on the actual connected NavMesh.");
            Physics.SyncTransforms(); int kills = s.Kills, earned = s.ScrapEarned;
            Kill(human); var entry = scheduler.Find(human); var instance = human.GetEntityId();
            Vector3 death = human.transform.position;
            Kill(human);
            Check("normal lethal damage queues one death-site countdown and one reward", entry != null && entry.Pending && !human.Alive &&
                scheduler.PendingCount == 1 && entry.Target == human && FlatDistance(entry.DeathPosition, death) < .02f &&
                FlatDistance(entry.Site, death) < .02f && FlatDistance(death, home) > 1.5f && entry.Remaining > FixtureDelay - .1f &&
                s.Kills == kills + 1 && s.ScrapEarned == earned + 1 && Conserved(),
                "Pending=" + scheduler.PendingCount + "; death=" + death + "; home distance=" + FlatDistance(death, home) + "; remaining=" + entry.Remaining + "; kills=" + s.Kills);
            yield return new WaitForSecondsRealtime(.12f);
            var marker = entry.Marker; Canvas.ForceUpdateCanvases();
            int displayedSeconds = -1;
            if (marker && marker.SecondsLabel)
                int.TryParse(new string(marker.SecondsLabel.text.Where(char.IsDigit).ToArray()), out displayedSeconds);
            var backgroundMesh = marker && marker.BackgroundRenderer ? marker.BackgroundRenderer.GetComponent<MeshFilter>() : null;
            var progressMesh = marker && marker.ProgressRenderer ? marker.ProgressRenderer.GetComponent<MeshFilter>() : null;
            Check("death marker renders a circle, progress and noninteractive real seconds", marker && marker.Visible &&
                FlatDistance(marker.WorldSite, entry.Site) < .02f && marker.BackgroundRenderer.enabled && marker.ProgressRenderer.enabled &&
                backgroundMesh && backgroundMesh.sharedMesh && backgroundMesh.sharedMesh.vertexCount > 12 &&
                progressMesh && progressMesh.sharedMesh && progressMesh.sharedMesh.vertexCount >= 3 &&
                marker.SecondsLabel && marker.SecondsLabel.gameObject.activeInHierarchy && !marker.SecondsLabel.raycastTarget &&
                !marker.GetComponentInChildren<UnityEngine.UI.GraphicRaycaster>(true) && displayedSeconds == Mathf.CeilToInt(entry.Remaining) &&
                Mathf.Abs(marker.Progress - (1 - entry.Remaining / entry.Total)) < .02f,
                "Visible=" + (marker && marker.Visible) + "; seconds=" + displayedSeconds + "; remaining=" + entry.Remaining + "; progress=" + (marker ? marker.Progress : -1));
            float before = entry.Remaining, progressBefore = marker.Progress;
            yield return new WaitForSecondsRealtime(.3f);
            Check("countdown advances during real Playing frames without an immediate spawn", !human.Alive && entry.Pending &&
                before - entry.Remaining > .15f && before - entry.Remaining < .6f && marker.Progress > progressBefore && s.Kills == kills + 1,
                "Remaining " + before + " -> " + entry.Remaining + "; scaled game time=" + Time.time);
            yield return new WaitForSecondsRealtime(.5f); // Let the ordinary short corpse finish before the visual capture.
            CaptureMarker(entry.Site);
            s.SetPaused(true); before = entry.Remaining; progressBefore = marker.Progress;
            yield return new WaitForSecondsRealtime(.5f);
            Check("pause freezes countdown and circular progress", s.State == SessionState.Paused && !human.Alive && entry.Pending &&
                Mathf.Abs(entry.Remaining - before) < .001f && Mathf.Abs(marker.Progress - progressBefore) < .001f,
                "Paused remaining=" + entry.Remaining + "; progress=" + marker.Progress + "; unscaled wait0.5s");
            s.SetPaused(false); yield return Until(() => human.Alive, FixtureDelay + 3);
            Check("resume respawns the same full-health Raider at its death site", human.Alive && human.GetEntityId() == instance &&
                human.agent.enabled && human.agent.isOnNavMesh && human.hitbox.enabled && !human.RewardGranted &&
                Mathf.Abs(human.Health - human.MaximumHealth) < .001f && FlatDistance(human.transform.position, entry.Site) < .5f &&
                !entry.Pending && !marker.Visible && s.Kills == kills + 1 && s.ScrapEarned == earned + 1 && Conserved(),
                "Alive=" + human.Alive + "; position=" + human.transform.position + "; death site=" + entry.Site + "; HP=" + human.Health);
            Kill(human); Kill(human);
            Check("a respawned enemy can die again and grants exactly one new reward", !human.Alive && entry.Pending &&
                scheduler.PendingCount == 1 && s.Kills == kills + 2 && s.ScrapEarned == earned + 2 && Conserved(),
                "Kills=" + s.Kills + "; earned=" + s.ScrapEarned + "; resources=" + Resources());
            FreshRun(); yield return null;
            Check("Restart clears pending markers and restores the fresh16-actor arena", scheduler.PendingCount == 0 && VisibleMarkers() == 0 &&
                roster.All(t => t.Alive && Mathf.Abs(t.CurrentHealth - t.MaxHealth) < .001f) && s.Kills == 0 && s.Scrap == 0 &&
                s.ScrapEarned == 0 && s.pickups.StoredValue == 0 && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                Mathf.Abs(s.health.Current - s.health.Maximum) < .001f,
                "Alive=" + roster.Count(t => t.Alive) + "; pending=" + scheduler.PendingCount + "; visible markers=" + VisibleMarkers());
        }

        IEnumerator BuggyReset()
        {
            FreshRun(); var instance = buggy.GetEntityId(); int reward = buggy.tuning.scrapReward;
            buggy.gun.CanShoot = false; Kill(buggy); var entry = scheduler.Find(buggy);
            bool wreck = !buggy.Alive && buggy.State == BuggyState.Wrecked && buggy.Body.isKinematic &&
                buggy.bodyColliders.All(c => !c.enabled) && buggy.wheels.All(w => !w.enabled) && entry.Pending;
            yield return Until(() => buggy.Alive, FixtureDelay + 4);
            yield return new WaitForSecondsRealtime(.6f);
            Check("Buggy wreck returns as the same grounded full-health vehicle with a fresh gun", wreck && buggy.Alive &&
                buggy.GetEntityId() == instance && buggy.State != BuggyState.Wrecked && !buggy.Body.isKinematic &&
                buggy.bodyColliders.All(c => c.enabled) && buggy.wheels.All(w => w.enabled) && buggy.GroundedWheels >= 2 &&
                Mathf.Abs(buggy.Health - buggy.MaximumHealth) < .001f && FlatDistance(buggy.transform.position, entry.Site) < .85f &&
                buggy.gun.CanShoot && (!buggy.gun.flash || !buggy.gun.flash.gameObject.activeSelf) && !entry.Pending &&
                s.Kills == 1 && s.ScrapEarned == reward && Conserved(),
                "Wreck observed=" + wreck + "; alive=" + buggy.Alive + "; contacts=" + buggy.GroundedWheels + "; HP=" + buggy.Health + "; gun ready=" + buggy.gun.CanShoot);
        }

        IEnumerator MonsterReset()
        {
            FreshRun();
            Vector3 near = monster.transform.position + monster.transform.forward * 4.2f;
            if (!NavMesh.SamplePosition(near, out var sample, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No real player approach to Monster.");
            PlacePlayer(sample.position + Vector3.up * .65f, Quaternion.LookRotation(monster.transform.position - sample.position));
            activeAiTarget = monster; monster.AIEnabled = true;
            yield return Until(() => monster.IsTelegraphing, 8);
            bool warning = monster.IsTelegraphing && monster.telegraph && monster.telegraph.activeSelf;
            activeAiTarget = null; monster.AIEnabled = false; Kill(monster); var entry = scheduler.Find(monster);
            bool deadReset = monster.RewardGranted && !monster.IsTelegraphing && (!monster.telegraph || !monster.telegraph.activeSelf);
            PlacePlayer(s.startPosition, s.startRotation);
            yield return Until(() => monster.Alive, FixtureDelay + 4);
            Check("Monster respawn resets its earned reward and a real committed telegraph", warning && deadReset && monster.Alive &&
                !monster.RewardGranted && !monster.IsTelegraphing && (!monster.telegraph || !monster.telegraph.activeSelf) &&
                monster.SmashesStarted == 0 && monster.SmashesLanded == 0 && monster.agent.enabled && monster.agent.isOnNavMesh &&
                Mathf.Abs(monster.Health - monster.MaximumHealth) < .001f && FlatDistance(monster.transform.position, entry.Site) < .6f &&
                !entry.Pending && s.Kills == 1 && s.ScrapEarned == monster.scrapReward && Conserved(),
                "Real warning=" + warning + "; death cleared warning=" + deadReset + "; alive=" + monster.Alive + "; reward reset=" + !monster.RewardGranted);
        }

        IEnumerator BlockedSites()
        {
            FreshRun();
            if (!human.agent.Warp(ClearRaiderSite(human))) throw new InvalidOperationException("No clear Raider setup site for occupancy checks.");
            Physics.SyncTransforms(); Kill(human); var entry = scheduler.Find(human); Vector3 site = entry.Site;
            PlacePlayer(site + Vector3.up * .65f, s.startRotation);
            yield return new WaitForSecondsRealtime(FixtureDelay + .8f);
            Check("an occupied death site defers instead of spawning inside the actual player", !human.Alive && entry.Pending && entry.Blocked &&
                entry.Remaining <= .001f && FlatDistance(entry.Site, site) < .001f && entry.Marker.Visible && entry.Marker.Progress > .99f,
                "Blocked=" + entry.Blocked + "; pending=" + entry.Pending + "; remaining=" + entry.Remaining + "; player occupies=" + site);
            PlacePlayer(s.startPosition, s.startRotation);
            yield return Until(() => human.Alive, 3);
            Check("clearing the player permits a same-site navigation-safe retry", human.Alive && human.agent.isOnNavMesh && !entry.Pending &&
                FlatDistance(human.transform.position, site) < .5f && !OverlapsPlayer(human.hitbox) && s.Kills == 1 && s.ScrapEarned == 1 && Conserved(),
                "Alive=" + human.Alive + "; site distance=" + FlatDistance(human.transform.position, site) + "; no player penetration=" + !OverlapsPlayer(human.hitbox));
            Kill(human); entry = scheduler.Find(human); site = entry.Site;
            wall = new GameObject("Temporary respawn blocking wall") { hideFlags = HideFlags.DontSave };
            wall.transform.position = site + Vector3.up * 1.5f; var box = wall.AddComponent<BoxCollider>(); box.size = new Vector3(4, 3, 4);
            Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(FixtureDelay + .8f);
            bool deferred = !human.Alive && entry.Pending && entry.Blocked && entry.Remaining <= .001f && FlatDistance(entry.Site, site) < .001f;
            wall.SetActive(false); Physics.SyncTransforms(); yield return Until(() => human.Alive, 3);
            Check("a real solid blocks respawn until removed without moving the death site", deferred && human.Alive && !entry.Pending &&
                human.agent.isOnNavMesh && FlatDistance(human.transform.position, site) < .5f && s.Kills == 2 && s.ScrapEarned == 2 && Conserved(),
                "Solid deferred=" + deferred + "; respawn after removal=" + human.Alive + "; same-site distance=" + FlatDistance(human.transform.position, site));
        }

        IEnumerator TerminalStates()
        {
            FreshRun(); Kill(human); bool queued = scheduler.PendingCount == 1;
            s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum + 1, false);
            yield return new WaitForSecondsRealtime(FixtureDelay + .2f);
            Check("Defeated cancels pending jobs and markers without a future enemy", queued && s.State == SessionState.Defeated &&
                scheduler.PendingCount == 0 && VisibleMarkers() == 0 && !human.Alive && s.Kills == 1,
                "State=" + s.State + "; pending=" + scheduler.PendingCount + "; visible=" + VisibleMarkers());
            FreshRun(); Kill(human); queued = scheduler.PendingCount == 1; int kills = s.Kills, earned = s.ScrapEarned;
            Kill(boss); s.TogglePause(); yield return new WaitForSecondsRealtime(FixtureDelay + .2f);
            Check("final boss completes with Monster alive and permanently cancels this run's countdowns", queued &&
                s.State == SessionState.Completed && !boss.Alive && boss.RewardGranted && monster.Alive && scheduler.Find(boss) == null &&
                scheduler.PendingCount == 0 && VisibleMarkers() == 0 && !human.Alive && s.Kills == kills + 1 &&
                s.ScrapEarned == earned + boss.bossGearReward && s.pickups.StoredValue == 0 && s.Scrap == s.ScrapEarned,
                "State=" + s.State + "; Monster alive=" + monster.Alive + "; pending=" + scheduler.PendingCount + "; earned=" + s.ScrapEarned);
        }

        void FreshRun()
        {
            activeAiTarget = null; if (wall) wall.SetActive(false);
            s.Restart();
            foreach (var component in componentStates.Keys) if (component) component.enabled = false;
            s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            s.health.DamageEnabled = true; FreezeAi(); Physics.SyncTransforms();
        }
        void SaveDisable(Behaviour component)
        {
            if (!component || componentStates.ContainsKey(component)) return;
            componentStates.Add(component, component.enabled); component.enabled = false;
        }
        void LateUpdate() { if (running && captured) FreezeAi(); }
        void FreezeAi()
        {
            foreach (var r in humanAi.Keys) if (r) r.AIEnabled = r == activeAiTarget;
            foreach (var b in buggyAi.Keys) if (b) b.AIEnabled = b == activeAiTarget;
            if (monster) monster.AIEnabled = monster == activeAiTarget;
        }
        static void Kill(CombatTarget actor) { actor.Hit(actor.MaxHealth + 100, Vector3.forward); }
        int VisibleMarkers() => scheduler.Entries.Count(e => e.Marker && e.Marker.Visible);
        int Resources() => s.Scrap + s.pickups.StoredValue;
        bool Conserved() => Resources() == s.ScrapEarned;
        static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
        IEnumerator Until(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        }
        Vector3 ClearRaiderSite(Raider actor)
        {
            var path = new NavMeshPath();
            for (int i = 0; i < 24; i++)
            {
                Vector3 wanted = actor.transform.position + Quaternion.Euler(0, i * 137.5f, 0) * Vector3.forward * (3 + i % 3);
                if (!NavMesh.SamplePosition(wanted, out var hit, 1, NavMesh.AllAreas) || FlatDistance(hit.position, actor.transform.position) < 1.5f ||
                    FlatDistance(hit.position, s.vehicle.transform.position) < 8 || Physics.CheckSphere(hit.position + Vector3.up, .7f, 1, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.OverlapSphere(hit.position + Vector3.up * .9f, .8f, ~0, QueryTriggerInteraction.Collide)
                    .Any(c => c.GetComponentInParent<CombatTarget>() is CombatTarget other && other != actor && other.Alive)) continue;
                if (NavMesh.CalculatePath(actor.transform.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) return hit.position;
            }
            throw new InvalidOperationException("No nearby clear connected death site for the actual Raider.");
        }
        void PlacePlayer(Vector3 point, Quaternion rotation)
        {
            s.vehicle.ResetPose(point, rotation); s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            Physics.SyncTransforms(); s.follow.Snap();
        }
        bool OverlapsPlayer(Collider other)
        {
            var player = s.vehicle.GetComponent<BoxCollider>();
            return player && other && Physics.ComputePenetration(player, player.transform.position, player.transform.rotation,
                other, other.transform.position, other.transform.rotation, out _, out float distance) && distance > .01f;
        }
        void CaptureMarker(Vector3 site)
        {
            // Real world camera/marker rendering, temporarily centred on the distant test site.
            // Screen-space overlay HUD is intentionally not part of this UI-readability capture.
            var camera = s.input.view; var position = camera.transform.position;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; float aspect = camera.aspect;
            var target = RenderTexture.GetTemporary(540, 960, 24);
            var texture = new Texture2D(540, 960, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position += site - s.vehicle.transform.position;
                camera.aspect = 540f / 960; camera.targetTexture = target; Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); texture.Apply();
                Directory.CreateDirectory("outputs/EnemyRespawn-2026-11-05");
                File.WriteAllBytes("outputs/EnemyRespawn-2026-11-05/respawn-countdown.png", texture.EncodeToPNG());
            }
            finally
            {
                camera.transform.position = position; camera.aspect = aspect; camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target); Destroy(texture);
            }
        }
        void Check(string name, bool pass, string detail)
        {
            Checks++; if (!pass) Failures++;
            string line = (pass ? "PASS " : "FAIL ") + name + ": " + detail;
            report.Add(line); Debug.Log("RG RESPAWN " + line); Write();
        }
        void Write() => File.WriteAllLines(ReportPath, report);
        void Restore()
        {
            if (restored) return; restored = true; running = false;
            if (wall) { wall.SetActive(false); Destroy(wall); }
            if (!captured || !s) return;
            scheduler.respawnDelaySeconds = originalDelay; scheduler.retryInterval = originalRetry;
            s.vehicle.Body.constraints = constraints; s.Restart();
            foreach (var pair in componentStates) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in humanAi) if (pair.Key) pair.Key.AIEnabled = pair.Value;
            foreach (var pair in buggyAi) if (pair.Key) pair.Key.AIEnabled = pair.Value;
            monster.AIEnabled = originalMonsterAi; s.health.DamageEnabled = originalDamageEnabled;
            s.vehicle.CanDrive = originalCanDrive; s.input.Enabled = originalInputEnabled;
            s.input.testInput = originalTestInput; s.input.testDirection = originalTestDirection; s.input.testBrake = originalTestBrake;
            Time.timeScale = originalTimeScale;
        }
        void OnDestroy() { Restore(); }
    }
}
#endif
