#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in acceptance checks against the saved Arena 1, using real gameplay components.</summary>
    public sealed class Arena1PlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/Arena1-2026-10-24/PlayMode.txt";
        public const string CaptureFolder = "outputs/arena1-validation";
        public bool Completed { get; private set; }
        public int Failures { get; private set; }
        public int Checks { get; private set; }
        readonly List<string> report = new List<string>();
        GameSession s;
        LevelLayout layout;
        HarpoonCollector harpoon;
        Raider human, boss;
        EnemyBuggy buggy;
        WastelandMutant monster;
        Touchscreen syntheticTouch;
        Vector3 testOrigin;
        Quaternion testRotation;
        RigidbodyConstraints carConstraints;
        int humanShots;
        bool running;

        public void Begin()
        {
            if (running) return;
            running = true; Completed = false; Failures = Checks = 0; report.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            Directory.CreateDirectory(CaptureFolder);
            report.Add("Arena 1 acceptance — actual Unity Play Mode; " + Application.unityVersion);
            report.Add("Synthetic touch uses the production InputSystem UI route; physical phone/browser performance is not measured here.");
            File.WriteAllLines(ReportPath, report);
            StartCoroutine(RunGuarded());
        }

        void Check(string name, bool passed, string detail)
        {
            Checks++; if (!passed) Failures++;
            string line = (passed ? "PASS " : "FAIL ") + name + ": " + detail;
            report.Add(line); File.WriteAllLines(ReportPath, report); Debug.Log("RG ARENA1 " + line);
        }

        IEnumerator RunGuarded()
        {
            // Let the real GameSession.Start initialize its run and HUD first.
            yield return null; yield return null;
            var sections = new Func<IEnumerator>[] { SceneAndMap, UpgradesAndRepair, CombatAndLoot, DrivingAndTouch, BossAndCompletion };
            foreach (var factory in sections)
            {
                IEnumerator section = factory();
                for (;;)
                {
                    bool more; object current = null;
                    try { more = section.MoveNext(); if (more) current = section.Current; }
                    catch (Exception error) { Check(factory.Method.Name + " exception", false, error.ToString()); break; }
                    if (!more) break;
                    yield return current;
                }
                (section as IDisposable)?.Dispose();
            }
            try { Restore(); }
            catch (Exception error) { Check("restore", false, error.ToString()); }
            report.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; saved scene was not modified.");
            File.WriteAllLines(ReportPath, report); Completed = true; running = false;
        }

        IEnumerator SceneAndMap()
        {
            s = GetComponent<GameSession>();
            if (!s) throw new InvalidOperationException("Attach checks to the actual GameSession.");
            layout = Object.FindAnyObjectByType<LevelLayout>();
            harpoon = s.vehicle.GetComponent<HarpoonCollector>();
            carConstraints = s.vehicle.Body.constraints;
            testOrigin = s.startPosition; testRotation = s.startRotation;
            human = s.spawns.authoredRaiders.First(r => r && !r.arenaBoss);
            buggy = s.spawns.authoredBuggies.First(b => b);
            monster = s.zone1.ExtraTargets.OfType<WastelandMutant>().Single();
            boss = s.zone1.ExtraTargets.OfType<Raider>().Single(r => r.arenaBoss);
            s.Restart(); s.turret.enabled = false; s.health.DamageEnabled = false;
            yield return new WaitForSecondsRealtime(.35f);
            Check("saved gameplay references", layout && layout.session == s && s.runStats && s.minimap && s.hud.statsText &&
                s.hud.upgradeFeedbackText && harpoon && harpoon.session == s && harpoon.pickups == s.pickups,
                "LevelLayout, stats, HUD, minimap and rear harpoon are wired on the gameplay scene");
            int raiders = s.spawns.authoredRaiders.Count(r => r && !r.arenaBoss);
            int buggies = s.spawns.authoredBuggies.Count(b => b);
            Check("fallback roster and ranks", raiders >= 10 && raiders <= 14 && buggies >= 2 && buggies <= 3 &&
                s.zone1.ExtraTargets.Length == 2 && !s.zone1.ExtraTargets.OfType<TruckArenaBoss>().Any() &&
                human.GetComponent<EnemyIdentity>().Rank == EnemyRank.Normal && buggy.GetComponent<EnemyIdentity>().Rank == EnemyRank.VehicleElite &&
                monster.GetComponent<EnemyIdentity>().Rank == EnemyRank.MiniBoss && boss.GetComponent<EnemyIdentity>().Rank == EnemyRank.Boss,
                "Authored Raiders=" + raiders + ", Buggies=" + buggies + ", Monster=1, Boss Raider=1; temporary fallback, not verified The War data");
            Check("finite authored encounters", !s.spawns.repeatAuthoredEnemies && s.spawns.GroupCount == 0 &&
                s.spawns.authoredRaiders.All(r => r.Alive && r.agent.isOnNavMesh) && s.spawns.authoredBuggies.All(b => b.Alive),
                "All authored enemies spawned; legacy random encounter respawn is disabled");
            Check("tactical tuning", Mathf.Abs(s.vehicle.tuning.maxSpeed - 18f / 3) < .05f &&
                Mathf.Abs(s.vehicle.tuning.maxReverseSpeed - 7f / 3) < .05f && s.vehicle.tuning.steeringRate >= 120 &&
                s.turret.tuning.playerDamageMultiplier >= .33f && s.turret.tuning.playerDamageMultiplier <= .4f,
                "Forward=" + s.vehicle.tuning.maxSpeed + ", reverse=" + s.vehicle.tuning.maxReverseSpeed +
                ", steering=" + s.vehicle.tuning.steeringRate + ", outgoing multiplier=" + s.turret.tuning.playerDamageMultiplier);
            Check("base run stats and HUD", s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                s.hud.statsText.text.Contains("ATK 1") && s.hud.statsText.text.Contains("HP 1") &&
                Mathf.Approximately(s.health.Maximum, s.health.tuning.vehicleHealth), s.hud.statsText.text);
            var path = new NavMeshPath();
            bool reachable = NavMesh.SamplePosition(s.startPosition, out var start, 4, NavMesh.AllAreas);
            foreach (var target in s.spawns.authoredRaiders.Cast<CombatTarget>().Concat(s.spawns.authoredBuggies).Concat(s.zone1.ExtraTargets))
                reachable &= NavMesh.CalculatePath(start.position, target.transform.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check("encounters connected to start", reachable, "Navigation paths reach ordinary encounters, both vehicles, Monster and Boss Raider");
            yield return new WaitForSecondsRealtime(.12f);
            bool playerMarker = s.minimap.TryGetMarker(s.vehicle.transform, out var playerPosition);
            int expected = 1 + raiders + buggies + 2 + s.runStats.stations.Length + s.minimap.workshops.Length;
            var lo = s.minimap.Project(s.minimap.WorldBounds.min);
            var hi = s.minimap.Project(s.minimap.WorldBounds.max);
            Check("minimap uses actual arena and live targets", s.minimap.layout == layout && playerMarker &&
                Vector2.Distance(playerPosition, s.minimap.Project(s.vehicle.transform.position)) < .02f &&
                hi.x > lo.x && hi.y > lo.y && s.minimap.VisibleMarkerCount == expected && s.minimap.mapRect.rect.width >= 150,
                "Visible markers=" + s.minimap.VisibleMarkerCount + "/" + expected + "; map UI=" + s.minimap.mapRect.rect.size + "; bounds=" + s.minimap.WorldBounds);
            Canvas.ForceUpdateCanvases();
            var icons = s.minimap.mapRect.GetComponentsInChildren<ArenaMinimapIcon>();
            int renderedIcons = 0, vertices = 0;
            foreach (var icon in icons)
            {
                var renderer = icon.GetComponent<CanvasRenderer>();
                var mesh = renderer ? renderer.GetMesh() : null;
                if (mesh && mesh.vertexCount > 0) { renderedIcons++; vertices += mesh.vertexCount; }
            }
            Check("minimap icons produce rendered geometry", renderedIcons == expected && vertices > expected * 3,
                "Visible CanvasRenderer meshes=" + renderedIcons + "/" + expected + "; vertices=" + vertices);
            bool legendInitiallyHidden = s.minimap.legendPanel && !s.minimap.legendPanel.activeSelf;
            s.minimap.ToggleLegend();
            bool legendOpened = s.minimap.legendPanel && s.minimap.legendPanel.activeSelf;
            s.minimap.ToggleLegend();
            Check("map key opens and closes without covering default HUD", legendInitiallyHidden && legendOpened &&
                s.minimap.legendPanel && !s.minimap.legendPanel.activeSelf,
                "Legend starts hidden, opens on request and closes before the gameplay capture");
            Capture("initial-start");
        }

        void Isolate()
        {
            Time.timeScale = 1;
            s.Restart(); s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
            foreach (var r in s.spawns.pool) if (r) r.Despawn();
            foreach (var b in s.spawns.buggyPool) if (b) b.Despawn();
            s.zone1.DespawnAll();
            s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            PlaceCar(testOrigin, testRotation); Physics.SyncTransforms();
        }

        void PlaceCar(Vector3 point, Quaternion rotation)
        {
            s.vehicle.ResetPose(point, rotation); Physics.SyncTransforms(); s.follow.Snap();
        }

        Vector3 Ground(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 100, Vector3.down, out var hit, 250, 1, QueryTriggerInteraction.Ignore)) return hit.point;
            throw new InvalidOperationException("No terrain beneath test point " + point);
        }

        IEnumerator UpgradesAndRepair()
        {
            Isolate();
            var attack = s.runStats.stations.First(station => station.kind == ArenaUpgradeKind.Attack);
            var health = s.runStats.stations.First(station => station.kind == ArenaUpgradeKind.Health);
            float baseDamage = s.PlayerDamage(s.turret.tuning.bulletDamage);
            PlaceCar(attack.transform.position + Vector3.up * .7f + Vector3.right * (attack.radius + 2), testRotation);
            yield return new WaitForSecondsRealtime(.25f);
            Check("upgrade proximity boundary", !attack.Consumed && s.runStats.AttackLevel == 1, "Standing outside the attack station radius grants no upgrade");
            PlaceCar(attack.transform.position + Vector3.up * .7f, testRotation);
            yield return new WaitForSecondsRealtime(.25f);
            Check("ATTACK +1 changes combat and HUD", attack.Consumed && s.runStats.AttackLevel == 2 &&
                s.PlayerDamage(s.turret.tuning.bulletDamage) > baseDamage && s.hud.statsText.text.Contains("ATK 2") &&
                s.hud.upgradeFeedbackText.gameObject.activeSelf && s.hud.upgradeFeedbackText.text == "ATTACK +1",
                "Damage " + baseDamage + " -> " + s.PlayerDamage(s.turret.tuning.bulletDamage) + "; HUD=" + s.hud.statsText.text);
            Check("consumed upgrade leaves minimap", !s.minimap.TryGetMarker(attack.transform, out _), "ATTACK station marker disappears after collection");
            Capture("attack-upgrade");
            yield return new WaitForSecondsRealtime(.4f);
            Check("upgrade only applies once", !attack.TryCollect() && s.runStats.AttackLevel == 2, "Remaining in radius and retrying do not increase attack again");
            float previousMax = s.health.Maximum;
            s.health.DamageEnabled = true; s.health.Damage(previousMax * .4f, false); s.health.DamageEnabled = false;
            float damagedHP = s.health.Current;
            PlaceCar(health.transform.position + Vector3.up * .7f, testRotation);
            yield return new WaitForSecondsRealtime(.25f);
            Check("HEALTH +1 uses existing health", health.Consumed && s.runStats.HealthLevel == 2 && s.health.Maximum > previousMax &&
                Mathf.Abs(s.health.Current - (damagedHP + s.health.Maximum - previousMax)) < .1f &&
                s.hud.statsText.text.Contains("HP 2") && Mathf.Abs(s.hud.healthFill.fillAmount - s.health.Current / s.health.Maximum) < .001f,
                "Maximum=" + previousMax + " -> " + s.health.Maximum + "; current=" + s.health.Current + "; HUD=" + s.hud.statsText.text);
            var workshop = s.minimap.workshops.First(w => w && w.session == s);
            PlaceCar(workshop.transform.position + Vector3.up * .7f, testRotation);
            float before = s.health.Current, began = Time.time;
            yield return new WaitForSecondsRealtime(.8f);
            float healed = s.health.Current - before, expected = s.health.Maximum * workshop.maximumHealthFractionPerSecond * (Time.time - began);
            Check("workshop heals upgraded health", healed > 0 && Mathf.Abs(healed - expected) < 2 && workshop.IsRepairing,
                "Healed=" + healed.ToString("F2") + "; expected=" + expected.ToString("F2") + "; current maximum=" + s.health.Maximum);
            s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum + 1, false);
            Check("death retains levels until restart", s.State == SessionState.Defeated && s.runStats.AttackLevel == 2 && s.runStats.HealthLevel == 2,
                "Defeat uses the original session flow");
            s.Restart(); s.turret.enabled = false; s.health.DamageEnabled = false;
            yield return new WaitForSecondsRealtime(.15f);
            Check("restart resets run and stations", s.Playing && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                s.runStats.stations.All(station => !station.Consumed) && Mathf.Approximately(s.health.Maximum, previousMax) &&
                Mathf.Approximately(s.health.Current, previousMax) && s.minimap.TryGetMarker(attack.transform, out _),
                "Base health/stats, station availability and map icons restored");
        }

        bool SpawnInGunRange(CombatTarget target)
        {
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 22.5f, 0) * (testRotation * Vector3.forward);
                if (!NavMesh.SamplePosition(testOrigin + direction * 5, out var hit, 1, NavMesh.AllAreas)) continue;
                // Gear ejects a further 1.3 m. Stay inside the unchanged 8 m collection radius.
                if (AttackRange.SquaredDistance(testOrigin, hit.position) > 36) continue;
                PlaceCar(testOrigin, Quaternion.LookRotation(direction));
                if (target is Raider raider)
                {
                    raider.Spawn(hit.position, -1, 0); raider.AIEnabled = false;
                }
                else if (target is EnemyBuggy vehicle)
                {
                    vehicle.Spawn(hit.position, Quaternion.LookRotation(-direction), -1); vehicle.AIEnabled = false;
                    vehicle.gun.CanShoot = false; vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
                }
                Physics.SyncTransforms();
                if (s.turret.CanAttack(target)) return true;
            }
            return false;
        }

        IEnumerator CombatAndLoot()
        {
            Isolate();
            Vector3 meleePoint = testOrigin + (testRotation * Vector3.right) * 2.5f;
            if (!NavMesh.SamplePosition(meleePoint, out var meleeSite, 1, NavMesh.AllAreas))
                throw new InvalidOperationException("No navigable melee test point beside the authored player start.");
            human.Spawn(meleeSite.position, -1, 0); s.health.DamageEnabled = true;
            float initialHP = s.health.Current, meleeDeadline = Time.realtimeSinceStartup + 3;
            while (Mathf.Approximately(s.health.Current, initialHP) && Time.realtimeSinceStartup < meleeDeadline) yield return null;
            Check("ordinary Raider still attacks vehicle", s.health.Current < initialHP &&
                Mathf.Abs(initialHP - s.health.Current - human.tuning.meleeDamage) < .01f,
                "Actual melee damage=" + (initialHP - s.health.Current) + "; configured=" + human.tuning.meleeDamage);
            Isolate();
            if (!SpawnInGunRange(human)) throw new InvalidOperationException("No reachable clear test target within turret range of the authored start.");
            yield return new WaitForSecondsRealtime(.15f);
            Check("live Raider minimap marker", s.minimap.TryGetMarker(human.transform, out var marker) &&
                Vector2.Distance(marker, s.minimap.Project(human.transform.position)) < .02f, "Marker is projected from the actual spawned Raider");
            s.turret.enabled = true; int shots = s.turret.ShotsFired; float hp = human.Health;
            float end = Time.realtimeSinceStartup + 10; bool survivedHit = false, sawGear = false, sawLaunch = false, sawRetract = false;
            while (human.Alive && Time.realtimeSinceStartup < end)
            {
                survivedHit |= human.Health > 0 && human.Health < hp;
                yield return null;
            }
            humanShots = s.turret.ShotsFired - shots; s.turret.enabled = false;
            int earned = s.ScrapEarned, kills = s.Kills; human.Hit(hp * 10, Vector3.forward);
            Check("roof gun needs multiple Raider hits", !human.Alive && survivedHit && humanShots >= 2 &&
                s.Kills == 1 && kills == s.Kills && earned == s.ScrapEarned && earned == 1,
                "Actual shots=" + humanShots + "; initial HP=" + hp + "; player hit=" + s.PlayerDamage(s.turret.tuning.bulletDamage));
            end = Time.realtimeSinceStartup + 10;
            while (s.Scrap < 1 && Time.realtimeSinceStartup < end)
            {
                sawGear |= s.pickups.MetalGearCount > 0; sawLaunch |= harpoon.State == HarpoonState.Launching;
                sawRetract |= harpoon.State == HarpoonState.Retracting; yield return null;
            }
            yield return new WaitForSecondsRealtime(.12f);
            Check("human Metal Gear and rear harpoon", sawGear && sawLaunch && sawRetract && s.Scrap == 1 &&
                s.pickups.ActiveCount == 0 && harpoon.CollectionsCompleted == 1 && s.hud.scrapText.text == "001",
                "Metal Gear visible=" + sawGear + "; launch=" + sawLaunch + "; retract=" + sawRetract + "; scrap=" + s.Scrap);
            Check("dead Raider leaves minimap", !s.minimap.TryGetMarker(human.transform, out _), "Death removes the enemy marker while the corpse can remain");

            Isolate();
            if (!SpawnInGunRange(buggy)) throw new InvalidOperationException("No clear Buggy target in the authored start area.");
            yield return new WaitForSecondsRealtime(.15f);
            bool originalMarker = s.minimap.TryGetMarker(buggy.transform, out var oldMarker);
            Vector3 moved = buggy.Body.position + Vector3.right * 2;
            buggy.Body.position = moved; buggy.transform.position = moved; Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.15f);
            Check("moving vehicle map marker follows", originalMarker && s.minimap.TryGetMarker(buggy.transform, out var newMarker) &&
                Vector2.Distance(newMarker, oldMarker) > .01f && Vector2.Distance(newMarker, s.minimap.Project(moved)) < .02f,
                "Cached marker follows position changes without rediscovering targets");
            if (!SpawnInGunRange(buggy)) throw new InvalidOperationException("Could not restore the Buggy shooting position.");
            s.health.DamageEnabled = true; buggy.gun.CanShoot = true;
            float playerHP = s.health.Current; int hostileShots = buggy.gun.ShotsFired;
            yield return new WaitForSecondsRealtime(1.8f);
            Check("enemy roof gun still damages player", buggy.gun.ShotsFired > hostileShots && s.health.Current < playerHP,
                "Hostile shots=" + (buggy.gun.ShotsFired - hostileShots) + "; player HP=" + playerHP + " -> " + s.health.Current);
            buggy.gun.CanShoot = false; s.health.DamageEnabled = false;
            shots = s.turret.ShotsFired; int explosions = s.effects.ExplosionsPlayed;
            s.turret.enabled = true; end = Time.realtimeSinceStartup + 14;
            while (buggy.Alive && Time.realtimeSinceStartup < end) yield return null;
            int vehicleShots = s.turret.ShotsFired - shots; s.turret.enabled = false;
            earned = s.ScrapEarned; buggy.Hit(buggy.tuning.health * 10, Vector3.forward);
            Check("Buggy tougher than Raider and rewards once", !buggy.Alive && vehicleShots > humanShots && s.Kills == 1 &&
                s.ScrapEarned == earned && earned == buggy.tuning.scrapReward && s.effects.ExplosionsPlayed == explosions + 1,
                "Raider shots=" + humanShots + "; Buggy shots=" + vehicleShots + "; reward=" + earned);
            sawGear = sawLaunch = sawRetract = false; end = Time.realtimeSinceStartup + 10;
            while (s.Scrap < buggy.tuning.scrapReward && Time.realtimeSinceStartup < end)
            {
                sawGear |= s.pickups.MetalGearCount > 0; sawLaunch |= harpoon.State == HarpoonState.Launching;
                sawRetract |= harpoon.State == HarpoonState.Retracting; yield return null;
            }
            yield return new WaitForSecondsRealtime(.12f);
            Check("vehicle Metal Gear and rear harpoon", sawGear && sawLaunch && sawRetract &&
                s.Scrap == buggy.tuning.scrapReward && s.pickups.ActiveCount == 0 && harpoon.CollectionsCompleted == 1,
                "Metal Gear=" + sawGear + "; launch=" + sawLaunch + "; retract=" + sawRetract + "; scrap=" + s.Scrap);
            Check("dead Buggy leaves minimap", !s.minimap.TryGetMarker(buggy.transform, out _), "Vehicle destruction hides its map marker");
        }

        void Drive(Vector3 worldDirection, float magnitude = 1)
        {
            var right = Vector3.ProjectOnPlane(s.input.view.transform.right, Vector3.up).normalized;
            var forward = Vector3.ProjectOnPlane(s.input.view.transform.forward, Vector3.up).normalized;
            s.input.testInput = true; s.input.testBrake = false;
            s.input.testDirection = new Vector2(Vector3.Dot(worldDirection, right), Vector3.Dot(worldDirection, forward)) * magnitude;
        }

        IEnumerator DrivingAndTouch()
        {
            Isolate(); s.vehicle.Body.constraints = carConstraints;
            yield return new WaitForSecondsRealtime(.7f);
            Vector3 start = s.vehicle.transform.position; float peak = 0;
            Drive(testRotation * Vector3.forward);
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end) { peak = Mathf.Max(peak, s.vehicle.Speed); yield return null; }
            Check("tactical speed in real vehicle physics", peak > s.vehicle.tuning.maxSpeed * .45f && peak <= s.vehicle.tuning.maxSpeed * 1.15f &&
                Vector3.Distance(start, s.vehicle.transform.position) > 5 && s.vehicle.GroundedWheels >= 3,
                "Peak=" + peak.ToString("F2") + "m/s; limit=" + s.vehicle.tuning.maxSpeed + "; travelled=" + Vector3.Distance(start, s.vehicle.transform.position).ToString("F1"));
            s.input.testBrake = true; float began = Time.realtimeSinceStartup; end = began + 3;
            while (s.vehicle.Speed > .65f && Time.realtimeSinceStartup < end) yield return null;
            Check("brake remains predictable", s.vehicle.Speed <= .65f, "Stop time=" + (Time.realtimeSinceStartup - began).ToString("F2") + "s; speed=" + s.vehicle.Speed.ToString("F2"));
            Isolate(); s.vehicle.Body.constraints = carConstraints;
            yield return new WaitForSecondsRealtime(.6f);
            Quaternion heading = s.vehicle.transform.rotation;
            Drive(Quaternion.Euler(0, 45, 0) * (testRotation * Vector3.forward), .8f);
            yield return new WaitForSecondsRealtime(2);
            Check("steering remains responsive", Quaternion.Angle(heading, s.vehicle.transform.rotation) > 8 && s.vehicle.Speed > .5f,
                "Heading changed=" + Quaternion.Angle(heading, s.vehicle.transform.rotation).ToString("F1") + " degrees; steering rate=" + s.vehicle.tuning.steeringRate);

            Isolate(); s.input.testInput = false; Canvas.ForceUpdateCanvases();
            var joystick = s.input.joystick;
            Check("phone input surface exists", EventSystem.current && joystick && joystick.gameObject.activeInHierarchy,
                "Existing EventSystem and floating joystick are active");
            syntheticTouch = InputSystem.AddDevice<Touchscreen>();
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, joystick.ring.position);
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 101, phase = UnityEngine.InputSystem.TouchPhase.Began, position = point });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 101, phase = UnityEngine.InputSystem.TouchPhase.Moved, position = point + Vector2.up * 100 });
            yield return null; yield return null;
            bool dragged = joystick.IsHeld && s.input.Magnitude > .1f && s.input.UsesHeadingSteering;
            Vector2 pause = RectTransformUtility.WorldToScreenPoint(null, s.hud.pause.transform.position);
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 102, phase = UnityEngine.InputSystem.TouchPhase.Began, position = pause });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 102, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = pause });
            yield return null; yield return null;
            Check("touch joystick and second-finger pause", dragged && s.State == SessionState.Paused && !joystick.IsHeld && s.input.Magnitude == 0,
                "Actual UI touch drag=" + dragged + "; second pointer paused=" + (s.State == SessionState.Paused));
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 101, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = point + Vector2.up * 100 });
            yield return null; s.SetPaused(false);
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 103, phase = UnityEngine.InputSystem.TouchPhase.Began, position = point });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 103, phase = UnityEngine.InputSystem.TouchPhase.Moved, position = point + Vector2.up * 80 });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = 103, phase = UnityEngine.InputSystem.TouchPhase.Canceled, position = point + Vector2.up * 80 });
            yield return null; yield return null;
            Check("touch cancellation releases input", !joystick.IsHeld && joystick.Value == Vector2.zero && s.input.Magnitude == 0,
                "Canceled production pointer resets thumb and drive input");
            InputSystem.RemoveDevice(syntheticTouch); syntheticTouch = null;
        }

        IEnumerator BossAndCompletion()
        {
            Isolate(); monster.ResetEncounter(); monster.AIEnabled = false; boss.ResetEncounter(); boss.AIEnabled = false;
            Check("boss health hierarchy", monster.MaximumHealth > buggy.tuning.health && boss.MaximumHealth > monster.MaximumHealth,
                "Raider=" + human.MaximumHealth + "; Buggy=" + buggy.tuning.health + "; Monster=" + monster.MaximumHealth + "; Boss Raider=" + boss.MaximumHealth);
            yield return new WaitForSecondsRealtime(.15f);
            Check("both boss map markers", s.minimap.TryGetMarker(monster.transform, out _) && s.minimap.TryGetMarker(boss.transform, out _),
                "Mini-boss and final boss are both present on the minimap");
            float monsterHP = monster.Health; monster.Hit(s.PlayerDamage(s.turret.tuning.bulletDamage), Vector3.forward);
            Check("mini-boss survives ordinary hit", monster.Alive && monster.Health < monsterHP && s.Playing, "Monster HP=" + monster.Health);
            monster.Hit(monster.MaximumHealth + 1, Vector3.forward); int earned = s.ScrapEarned;
            monster.Hit(monster.MaximumHealth + 1, Vector3.forward);
            Check("Monster death does not finish arena", !monster.Alive && monster.RewardGranted && s.Playing && s.Kills == 1 && s.ScrapEarned == earned,
                "State=" + s.State + "; reward=" + earned + "; kills=" + s.Kills);
            Vector3 attackPosition = Ground(boss.transform.position + boss.transform.forward * 4.6f) + Vector3.up * .7f;
            Quaternion attackHeading = Quaternion.LookRotation(-boss.transform.forward);
            PlaceCar(attackPosition, attackHeading); s.health.DamageEnabled = true; boss.AIEnabled = true;
            float playerHP = s.health.Current, deadline = Time.realtimeSinceStartup + 3;
            while (!boss.IsTelegraphing && Time.realtimeSinceStartup < deadline) yield return null;
            bool warned = boss.IsTelegraphing && boss.bossWarning && boss.bossWarning.gameObject.activeSelf;
            Check("Boss Raider warns before heavy strike", warned && Mathf.Approximately(s.health.Current, playerHP),
                "Visible warning=" + warned + "; HP before strike=" + s.health.Current);
            boss.Hit(s.PlayerDamage(s.turret.tuning.bulletDamage), Vector3.forward);
            Check("gun hit does not cancel boss warning", warned && boss.IsTelegraphing, "Committed windup remains active after a player bullet");
            PlaceCar(attackPosition + boss.transform.right * 7, attackHeading);
            yield return new WaitForSecondsRealtime(boss.bossWindup + .08f);
            Check("dodging locked boss strike avoids damage", warned && Mathf.Approximately(s.health.Current, playerHP),
                "Player moved outside the warned circle; HP=" + s.health.Current);
            boss.ResetEncounter(); boss.AIEnabled = true;
            PlaceCar(attackPosition, attackHeading);
            deadline = Time.realtimeSinceStartup + 3;
            while (!boss.IsTelegraphing && Time.realtimeSinceStartup < deadline) yield return null;
            bool secondWarning = boss.IsTelegraphing;
            yield return new WaitForSecondsRealtime(boss.bossWindup + .08f);
            Check("remaining in boss strike takes damage once", secondWarning &&
                Mathf.Abs(playerHP - s.health.Current - boss.tuning.meleeDamage) < .01f && boss.IsRecovering,
                "Actual heavy damage=" + (playerHP - s.health.Current) + "; configured=" + boss.tuning.meleeDamage + "; recovering=" + boss.IsRecovering);
            boss.AIEnabled = false; s.health.DamageEnabled = false;
            var bossPoint = boss.transform.position + boss.transform.forward * 8;
            if (NavMesh.SamplePosition(bossPoint, out var sample, 4, NavMesh.AllAreas)) bossPoint = sample.position;
            PlaceCar(Ground(bossPoint) + Vector3.up * .7f, Quaternion.LookRotation(boss.transform.position - bossPoint));
            yield return new WaitForSecondsRealtime(.25f);
            var bossHUD = Object.FindAnyObjectByType<Zone1BossHud>();
            Check("existing boss health bar identifies Raider", bossHUD && bossHUD.Target == boss && bossHUD.IsVisible &&
                bossHUD.healthText.text.Contains(Mathf.CeilToInt(boss.MaximumHealth).ToString()),
                bossHUD ? "Title=" + bossHUD.titleText.text + "; HP=" + bossHUD.healthText.text : "No existing boss HUD");
            Capture("boss-arena");
            float bossHP = boss.Health; boss.Hit(s.PlayerDamage(s.turret.tuning.bulletDamage), Vector3.forward);
            Check("Boss Raider survives ordinary hit", boss.Alive && boss.Health < bossHP && s.Playing, "Boss HP=" + boss.Health);
            // Keep both collected resources and outstanding gear in the completion result.
            s.AddScrap(2); int collected = s.Scrap, remaining = s.pickups.StoredValue, kills = s.Kills;
            boss.Hit(boss.MaximumHealth + 1, Vector3.forward); boss.Hit(boss.MaximumHealth + 1, Vector3.forward);
            yield return new WaitForSecondsRealtime(.15f);
            Check("only final boss completes and preserves rewards", s.State == SessionState.Completed && boss.RewardGranted &&
                s.Kills == kills + 1 && s.Scrap == collected + remaining + boss.bossGearReward && s.pickups.StoredValue == 0 &&
                !s.vehicle.CanDrive && !s.input.Enabled && Time.timeScale == 0 && s.hud.overlay.activeSelf && s.hud.overlayTitle.text == "ARENA CLEAR",
                "State=" + s.State + "; resources=" + s.Scrap + "; expected=" + (collected + remaining + boss.bossGearReward) + "; overlay=" + s.hud.overlayTitle.text);
            Check("final boss marker removed", !s.minimap.TryGetMarker(boss.transform, out _), "Completed boss is absent from the map");
            Capture("arena-complete");
            s.TogglePause();
            Check("completed result stays completed", s.State == SessionState.Completed && Time.timeScale == 0, "Pause cannot resume a finished arena");
            s.Restart(); yield return new WaitForSecondsRealtime(.15f);
            Check("restart restores the full arena", s.Playing && s.Scrap == 0 && s.Kills == 0 && monster.Alive && boss.Alive &&
                s.spawns.authoredRaiders.All(r => r.Alive) && s.spawns.authoredBuggies.All(b => b.Alive) && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1,
                "Ordinary encounters, both bosses, stats and run resources reset");
        }

        sealed class CanvasState
        {
            public Canvas canvas; public RenderMode mode; public Camera camera; public float plane;
        }

        void Capture(string name)
        {
            var camera = s.input.view; const int width = 540, height = 960;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; float previousAspect = camera.aspect;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, plane = c.planeDistance }).ToArray();
            string path = CaptureFolder + "/" + name + ".png";
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target;
                foreach (var state in canvases)
                {
                    state.canvas.renderMode = RenderMode.ScreenSpaceCamera; state.canvas.worldCamera = camera;
                    state.canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1);
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Check("portrait capture " + name, File.Exists(path) && new FileInfo(path).Length > 5000, path + " — actual gameplay camera and HUD, 540 x 960");
            }
            finally
            {
                foreach (var state in canvases) if (state.canvas)
                {
                    state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.plane;
                }
                camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target); Destroy(texture); Canvas.ForceUpdateCanvases();
            }
        }

        void Restore()
        {
            if (syntheticTouch != null) { InputSystem.RemoveDevice(syntheticTouch); syntheticTouch = null; }
            if (!s) return;
            Time.timeScale = 1;
            foreach (var r in s.spawns.pool) if (r) { r.enabled = true; r.AIEnabled = true; }
            foreach (var b in s.spawns.buggyPool) if (b)
            { b.enabled = true; b.AIEnabled = true; if (b.Body) b.Body.constraints = RigidbodyConstraints.None; }
            if (monster) { monster.enabled = true; monster.AIEnabled = true; }
            if (boss) { boss.enabled = true; boss.AIEnabled = true; }
            s.vehicle.Body.constraints = carConstraints; s.spawns.enabled = true; s.turret.enabled = true; s.Restart();
        }

        void OnDestroy() { if (syntheticTouch != null) InputSystem.RemoveDevice(syntheticTouch); }
    }
}
#endif
