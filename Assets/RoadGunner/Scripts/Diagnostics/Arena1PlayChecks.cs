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
    [DefaultExecutionOrder(10000)]
    public sealed partial class Arena1PlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/WheelPhysics-2026-10-30/Gameplay.txt";
        public const string CaptureFolder = "outputs/wheel-physics-regression";
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
        bool trackRange;
        AttackRangeIndicator trackedRange;
        int rangeTrackingFrames;
        float maximumRangeCentreError;

        void LateUpdate()
        {
            // Observe the production indicator after its own LateUpdate has followed physics.
            if (!trackRange || !trackedRange || !s || !s.vehicle) return;
            rangeTrackingFrames++;
            maximumRangeCentreError = Mathf.Max(maximumRangeCentreError,
                AttackRange.SquaredDistance(trackedRange.Centre, s.vehicle.transform.position));
        }

        public void Begin()
        {
            if (running) return;
            running = true; Completed = false; Failures = Checks = 0; report.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            Directory.CreateDirectory(CaptureFolder);
            report.Add("Arena 1 wheel-driven handling regression — actual Unity Play Mode; " + Application.unityVersion);
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
            var sections = new Func<IEnumerator>[] { SceneAndMap, EnemyHealthUi, CircularMapAndWhiteRange, ArenaMusicChecks, UpgradesAndRepair, CombatAndLoot, FastHarpoon, DrivingAndTouch, AntonFeedback, BossAndCompletion };
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
            Check("current speed increased by thirty percent", Mathf.Abs(s.vehicle.tuning.maxSpeed - 9f * 1.30f) < .05f &&
                s.vehicle.tuning.maxSpeed < 18 && s.vehicle.tuning.steeringRate >= 120 &&
                s.turret.tuning.playerDamageMultiplier >= .33f && s.turret.tuning.playerDamageMultiplier <= .4f,
                "Previous forward=9; current=" + s.vehicle.tuning.maxSpeed + "; multiplier=" + s.vehicle.tuning.maxSpeed / 9 +
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
            VerifyMapBackgroundAndStyles(icons);
            VerifyRangeVisualization();
            bool legendInitiallyHidden = s.minimap.legendPanel && !s.minimap.legendPanel.activeSelf;
            s.minimap.ToggleLegend();
            bool legendOpened = s.minimap.legendPanel && s.minimap.legendPanel.activeSelf;
            s.minimap.ToggleLegend();
            Check("map key opens and closes without covering default HUD", legendInitiallyHidden && legendOpened &&
                s.minimap.legendPanel && !s.minimap.legendPanel.activeSelf,
                "Legend starts hidden, opens on request and closes before the gameplay capture");
            Capture("initial-start");
        }

        static bool IsRed(Color color) => color.r >= .75f && color.g <= .35f && color.b <= .4f && color.a > .8f;

        static bool HasSkullFeatures(ArenaMinimapIcon icon)
        {
            var mesh = icon.canvasRenderer.GetMesh(); var points = mesh.vertices; var colors = mesh.colors;
            float x = icon.rectTransform.rect.width * .5f, y = icon.rectTransform.rect.height * .5f;
            bool leftEye = false, rightEye = false, nose = false;
            for (int i = 0; i < points.Length; i++)
            {
                if (colors[i].r > .15f || colors[i].g > .15f || colors[i].b > .15f) continue;
                Vector3 point = points[i];
                leftEye |= point.x < -x * .1f && point.x > -x * .6f && point.y > y * .05f && point.y < y * .5f;
                rightEye |= point.x > x * .1f && point.x < x * .6f && point.y > y * .05f && point.y < y * .5f;
                nose |= Mathf.Abs(point.x) < x * .2f && point.y < -y * .05f && point.y > -y * .4f;
            }
            return leftEye && rightEye && nose;
        }

        void VerifyMapBackgroundAndStyles(ArenaMinimapIcon[] icons)
        {
            var map = s.minimap; var texture = map.BackgroundTexture;
            int distinctColors = 0;
            if (texture)
            {
                // Read the actual GPU texture; the production asset need not be CPU-readable.
                var target = RenderTexture.GetTemporary(128, 128, 0);
                var previous = RenderTexture.active; var sampled = new Texture2D(128, 128, TextureFormat.RGB24, false);
                try
                {
                    Graphics.Blit(texture, target); RenderTexture.active = target;
                    sampled.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); sampled.Apply();
                    var colors = new HashSet<int>();
                    foreach (var pixel in sampled.GetPixels32()) colors.Add((pixel.r / 16 << 8) | (pixel.g / 16 << 4) | pixel.b / 16);
                    distinctColors = colors.Count;
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(sampled); }
            }
            Bounds bounds = map.WorldBounds;
            bool aligned = false;
            if (map.backgroundImage)
            {
                var imageRect = map.backgroundImage.rectTransform;
                Vector2 imageMin = map.mapRect.InverseTransformPoint(imageRect.TransformPoint(new Vector3(imageRect.rect.xMin, imageRect.rect.yMin, 0)));
                Vector2 imageMax = map.mapRect.InverseTransformPoint(imageRect.TransformPoint(new Vector3(imageRect.rect.xMax, imageRect.rect.yMax, 0)));
                aligned = Vector2.Distance(map.Project(bounds.min), imageMin) < .02f && Vector2.Distance(map.Project(bounds.max), imageMax) < .02f;
            }
            Check("minimap renders baked Arena terrain and roads", texture && map.backgroundImage && map.backgroundImage.texture == texture &&
                map.hasBakedWorldBounds && map.bakedEnvironmentMeshCount > 0 && map.bakedRoadMeshCount > 0 &&
                map.bakedTriangleCount > 0 && texture.width >= 256 && texture.width <= 1024 && texture.height >= 256 &&
                distinctColors >= 12 && aligned,
                "Texture=" + (texture ? texture.width + "x" + texture.height : "missing") + "; sampled palette=" + distinctColors +
                "; environment meshes=" + map.bakedEnvironmentMeshCount + "; roads=" + map.bakedRoadMeshCount +
                "; triangles=" + map.bakedTriangleCount + "; background/icon bounds aligned=" + aligned);
            var ordinary = icons.Where(i => i.kind == MinimapMarkerKind.Raider || i.kind == MinimapMarkerKind.Buggy).ToArray();
            var interactive = icons.Where(i => i.kind == MinimapMarkerKind.Attack || i.kind == MinimapMarkerKind.Health).ToArray();
            var workshops = icons.Where(i => i.kind == MinimapMarkerKind.Workshop).ToArray();
            var mini = icons.Single(i => i.kind == MinimapMarkerKind.MiniBoss);
            var final = icons.Single(i => i.kind == MinimapMarkerKind.Boss);
            var player = icons.Single(i => i.kind == MinimapMarkerKind.Player);
            int ordinaryVertices = ordinary.Min(i => i.canvasRenderer.GetMesh().vertexCount);
            bool skullGeometry = HasSkullFeatures(mini) && HasSkullFeatures(final);
            bool interactiveShape = interactive.All(i => i.canvasRenderer.GetMesh().vertexCount != ordinaryVertices);
            Check("normal enemies and distinct reward objects are red", ordinary.Length == 14 && ordinary.All(i => IsRed(i.color)) &&
                interactive.Length == 2 && interactive.All(i => IsRed(i.color)) && interactiveShape,
                "Red ordinary markers=" + ordinary.Count(i => IsRed(i.color)) + "; red interactive markers=" + interactive.Count(i => IsRed(i.color)) + "; distinct geometry=" + interactiveShape);
            bool greenWrenches = workshops.All(icon => icon.color.g >= .65f && icon.color.r < .4f && icon.color.b < .5f &&
                icon.canvasRenderer.GetMesh().colors.Count(c => c.r > .95f && c.g > .95f && c.b > .95f) > 48 &&
                icon.rectTransform.rect.width > ordinary.Max(i => i.rectTransform.rect.width));
            Check("workshop has a prominent green white-wrench marker", workshops.Length == map.workshops.Length && workshops.Length > 0 && greenWrenches,
                "Green repair markers=" + workshops.Length + "; white spanner geometry=" + greenWrenches);
            Check("orange mini skull and red boss skull are prominent", mini.color.r >= .9f && mini.color.g >= .3f && mini.color.g <= .75f &&
                mini.color.b < .25f && IsRed(final.color) && skullGeometry &&
                mini.rectTransform.rect.width > ordinary.Max(i => i.rectTransform.rect.width) &&
                final.rectTransform.rect.width > ordinary.Max(i => i.rectTransform.rect.width),
                "Mini color=" + mini.color + "; boss color=" + final.color + "; complex skull geometry=" + skullGeometry);
            Check("player marker stays unique and map stays north-up", !IsRed(player.color) && player.color.g >= .7f && player.color.b >= .7f &&
                player.rectTransform.rect.width >= final.rectTransform.rect.width && Mathf.Abs(Mathf.DeltaAngle(0, map.mapRect.localEulerAngles.z)) < .01f,
                "Player color=" + player.color + "; player width=" + player.rectTransform.rect.width + "; boss width=" + final.rectTransform.rect.width);
        }

        void VerifyRangeVisualization()
        {
            var range = s.vehicle.GetComponent<AttackRangeIndicator>();
            if (!range || !range.Mesh) { Check("visible range geometry", false, "Existing AttackRangeIndicator or mesh is missing"); return; }
            Color[] colors = range.Mesh.colors;
            Color materialTint = range.material && range.material.HasProperty("_BaseColor") ? range.material.GetColor("_BaseColor") : Color.clear;
            bool white = range.fillColor.r > .98f && range.fillColor.g > .98f && range.fillColor.b > .98f &&
                range.outlineColor.r > .98f && range.outlineColor.g > .98f && range.outlineColor.b > .98f &&
                materialTint.r > .98f && materialTint.g > .98f && materialTint.b > .98f;
            Check("white range retains actual ten metre radius", Mathf.Abs(s.turret.Range - 10) < .001f && Mathf.Abs(range.Radius - 10) < .001f &&
                range.Visible && range.outlineWidth >= .15f && Mathf.Abs(range.contrastWidth - .10f) < .005f && white &&
                Mathf.Abs(range.fillColor.a - .025f) < .002f && Mathf.Abs(range.outlineColor.a - .92f) < .002f &&
                range.contrastColor.r < .2f && range.contrastColor.g < .2f && range.contrastColor.b < .2f &&
                colors.Length > 128 && colors.Max(c => c.a) * materialTint.a >= .9f && Mathf.Abs(colors[0].a * materialTint.a - .025f) < .002f,
                "Weapon/ring=" + s.turret.Range + "/" + range.Radius + "m; white=" + white + "; outline=" + range.outlineWidth +
                "; contrast=" + range.contrastWidth + "; fill alpha=" + range.fillColor.a + "; outline alpha=" + range.outlineColor.a + "; material alpha=" + materialTint.a);
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
            Check("damage upgrade explains its actual run effect", attack.Consumed && s.runStats.AttackLevel == 2 &&
                Mathf.Abs(baseDamage - 5.25f) < .001f && Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 6.3f) < .001f &&
                s.hud.statsText.text.Contains("ATK 2") && s.hud.upgradeFeedbackText.gameObject.activeSelf &&
                s.hud.upgradeFeedbackText.text.Contains("DAMAGE +20%") && s.hud.upgradeFeedbackText.text.Contains("THIS RUN"),
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
            Check("health upgrade explains its actual run effect", Mathf.Abs(s.health.Maximum - previousMax - 20) < .001f &&
                s.hud.upgradeFeedbackText.gameObject.activeSelf && s.hud.upgradeFeedbackText.text.Contains("MAX HP +20") && s.hud.upgradeFeedbackText.text.Contains("CURRENT +20") &&
                s.hud.upgradeFeedbackText.text.Contains("THIS RUN"),
                "Actual max HP increase=" + (s.health.Maximum - previousMax) + "; toast=" + s.hud.upgradeFeedbackText.text);
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
            Check("defeat overlay shows real run effects and reset consequence", s.hud.overlayTitle.text == "WRECKED" &&
                s.hud.overlayDetails && s.hud.overlayDetails.text.Contains("6.3") && s.hud.overlayDetails.text.Contains("MAX HP 120") &&
                s.hud.restartHint && s.hud.restartHint.text.Contains("RESET") && !s.hud.resume.gameObject.activeSelf,
                "Defeat keeps upgraded damage/maximum in the result and offers a disclosed fresh run");
            Capture("anton-defeated");
            s.Restart(); s.turret.enabled = false; s.health.DamageEnabled = false;
            yield return new WaitForSecondsRealtime(.15f);
            Check("restart resets run and stations", s.Playing && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                s.runStats.stations.All(station => !station.Consumed) && Mathf.Approximately(s.health.Maximum, previousMax) &&
                Mathf.Approximately(s.health.Current, previousMax) && s.minimap.TryGetMarker(attack.transform, out _) &&
                Mathf.Approximately(s.PlayerDamage(s.turret.tuning.bulletDamage), baseDamage) && !s.hud.upgradeFeedbackText.gameObject.activeSelf,
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
                yield return null;
                if (!survivedHit && human.Health > 0 && human.Health < hp)
                { survivedHit = true; Capture("white-range-combat"); }
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

        IEnumerator FastHarpoon()
        {
            float launch = harpoon.harpoonLaunchSpeed, retract = harpoon.harpoonRetractSpeed;
            float search = harpoon.searchInterval, delay = harpoon.delayBetweenCollections, aim = harpoon.aimSpeed;
            Quaternion pivotPose = harpoon.pivot.localRotation;
            Check("harpoon has faster configurable cycle", launch >= 36 && retract >= 30 && search <= .05f && delay <= .08f && aim >= 960,
                "Launch18->" + launch + "; retract/pull12->" + retract + "; search.15->" + search + "; cooldown.3->" + delay + "; aim480->" + aim);
            float baseline = 0, current = 0; bool newLaunch = false, newRetract = false; int maxReservations = 0;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 0)
                    {
                        harpoon.harpoonLaunchSpeed = 18; harpoon.harpoonRetractSpeed = 12;
                        harpoon.searchInterval = .15f; harpoon.delayBetweenCollections = .3f; harpoon.aimSpeed = 480;
                    }
                    else
                    {
                        harpoon.harpoonLaunchSpeed = launch; harpoon.harpoonRetractSpeed = retract;
                        harpoon.searchInterval = search; harpoon.delayBetweenCollections = delay; harpoon.aimSpeed = aim;
                    }
                    Isolate();
                    harpoon.pivot.localRotation = pivotPose;
                    var right = testRotation * Vector3.right;
                    s.pickups.DropGear(testOrigin + right * 4.5f + Vector3.up, Vector3.zero, 1);
                    s.pickups.DropGear(testOrigin - right * 4.5f + Vector3.up, Vector3.zero, 1);
                    float began = Time.realtimeSinceStartup, deadline = began + 8;
                    while (s.Scrap < 2 && Time.realtimeSinceStartup < deadline)
                    {
                        if (pass == 1)
                        {
                            newLaunch |= harpoon.State == HarpoonState.Launching;
                            newRetract |= harpoon.State == HarpoonState.Retracting;
                            maxReservations = Mathf.Max(maxReservations, s.pickups.ReservedGearCount);
                        }
                        yield return null;
                    }
                    float elapsed = Time.realtimeSinceStartup - began;
                    if (pass == 0) baseline = elapsed; else current = elapsed;
                    Check(pass == 0 ? "previous harpoon baseline measured" : "faster harpoon collects consecutive gear exactly once",
                        s.Scrap == 2 && s.pickups.ActiveCount == 0 && harpoon.CollectionsCompleted == 2,
                        "Two identical 4.5m drops; elapsed=" + elapsed.ToString("F2") + "s; resources=" + s.Scrap + "; collections=" + harpoon.CollectionsCompleted);
                }
                Check("collection is faster while animation remains visible", current < baseline * .85f && current > PickupPool.GearFlightTime &&
                    newLaunch && newRetract && maxReservations == 1,
                    "Previous/current two-drop cycles=" + baseline.ToString("F2") + "/" + current.ToString("F2") + "s; improvement=" +
                    (baseline / Mathf.Max(.01f, current)).ToString("F2") + "x; visible launch/retract=" + newLaunch + "/" + newRetract + "; maximum reservations=" + maxReservations);
            }
            finally
            {
                harpoon.harpoonLaunchSpeed = launch; harpoon.harpoonRetractSpeed = retract;
                harpoon.searchInterval = search; harpoon.delayBetweenCollections = delay; harpoon.aimSpeed = aim;
                harpoon.pivot.localRotation = pivotPose;
            }
        }

        Vector2 ControlPoint(float x, float y)
        {
            var rect = (RectTransform)s.input.joystick.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector2(
                Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, x), Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, y))));
        }

        void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
        {
            InputSystem.QueueStateEvent(syntheticTouch, new TouchState { touchId = id, phase = phase, position = position });
        }

        Vector3 DrivingPad()
        {
            // The existing final arena is deliberately open. Test actual wheel movement there,
            // after isolating its enemies, rather than steering into the narrow starting road.
            Vector3[] centers = { boss.spawnPoint ? boss.spawnPoint.position : boss.transform.position,
                monster.spawnPoint ? monster.spawnPoint.position : monster.transform.position, testOrigin };
            foreach (var center in centers)
            {
                if (!NavMesh.SamplePosition(center, out var point, 3, NavMesh.AllAreas)) continue;
                bool clear = true;
                for (int i = 0; i < 16; i++)
                {
                    var direction = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                    if (Physics.Linecast(point.position + Vector3.up * 1.3f,
                        point.position + direction * 7 + Vector3.up * 1.3f, 1, QueryTriggerInteraction.Ignore)) { clear = false; break; }
                }
                if (clear) return Ground(point.position) + Vector3.up * .7f;
            }
            throw new InvalidOperationException("No clear authored arena pad available for directional driving checks.");
        }

        IEnumerator DrivingAndTouch()
        {
            Isolate(); s.vehicle.Body.constraints = carConstraints;
            yield return new WaitForSecondsRealtime(.7f);
            Vector3 start = s.vehicle.transform.position; float peak = 0;
            trackedRange = s.vehicle.GetComponent<AttackRangeIndicator>();
            rangeTrackingFrames = 0; maximumRangeCentreError = 0; trackRange = true;
            Drive(testRotation * Vector3.forward);
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end) { peak = Mathf.Max(peak, s.vehicle.Speed); yield return null; }
            trackRange = false;
            Check("increased speed in real vehicle physics", peak > 6 && peak <= s.vehicle.tuning.maxSpeed * 1.15f &&
                Vector3.Distance(start, s.vehicle.transform.position) > 5 && s.vehicle.GroundedWheels >= 3,
                "Peak=" + peak.ToString("F2") + "m/s; limit=" + s.vehicle.tuning.maxSpeed + "; travelled=" + Vector3.Distance(start, s.vehicle.transform.position).ToString("F1"));
            Check("visible range follows the moving player", trackedRange && trackedRange.Visible && Mathf.Abs(trackedRange.Radius - s.turret.Range) < .001f &&
                rangeTrackingFrames > 2 && maximumRangeCentreError < .01f,
                "Observed after production LateUpdate; frames=" + rangeTrackingFrames + "; maximum centre error squared=" + maximumRangeCentreError.ToString("F6") +
                "; actual weapon radius=" + s.turret.Range);
            s.input.testBrake = true; float began = Time.realtimeSinceStartup; end = began + 3;
            while (s.vehicle.Speed > .65f && Time.realtimeSinceStartup < end) yield return null;
            Check("brake remains predictable", s.vehicle.Speed <= .65f, "Stop time=" + (Time.realtimeSinceStartup - began).ToString("F2") + "s; speed=" + s.vehicle.Speed.ToString("F2"));
            Isolate(); s.input.testInput = false; Canvas.ForceUpdateCanvases();
            var joystick = s.input.joystick;
            Check("phone input surface exists", EventSystem.current && joystick && joystick.gameObject.activeInHierarchy,
                "Existing EventSystem and floating joystick are active");
            syntheticTouch = InputSystem.AddDevice<Touchscreen>();
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            Check("floating joystick hides before a touch", !joystick.IsHeld && !joystick.ring.gameObject.activeInHierarchy && joystick.Value == Vector2.zero,
                "Activation zone stays available while its ring/thumb are hidden");
            Vector2 point = ControlPoint(.04f, .08f);
            Touch(101, UnityEngine.InputSystem.TouchPhase.Began, point);
            yield return null; yield return null; yield return null;
            float originError = Vector2.Distance(point, RectTransformUtility.WorldToScreenPoint(null, joystick.ring.position));
            Check("TEST A origin is exactly the lower-left touch", joystick.IsHeld && joystick.ring.gameObject.activeInHierarchy &&
                originError < 1 && Vector2.Distance(point, joystick.OriginScreenPosition) < 1 && s.input.Magnitude == 0,
                "Touch=" + point + "; displayed origin error=" + originError.ToString("F3") + "px; centered magnitude=" + s.input.Magnitude);
            float screenRadius = Vector2.Distance(point, RectTransformUtility.WorldToScreenPoint(null, joystick.ring.TransformPoint(Vector3.right * joystick.radius)));
            Touch(101, UnityEngine.InputSystem.TouchPhase.Moved, point + Vector2.up * screenRadius * .06f);
            yield return null; yield return null;
            Check("joystick dead zone remains configurable", s.input.joystickDeadZone > 0 && s.input.joystickDeadZone <= .2f && s.input.Magnitude == 0,
                "Configured dead zone=" + s.input.joystickDeadZone + "; small touch delta generates no motion");
            Touch(101, UnityEngine.InputSystem.TouchPhase.Ended, point);
            yield return null; yield return null;

            // Hold a real UI touch along the clear authored starting road, with no test-input override.
            Isolate(); s.input.testInput = false; s.vehicle.Body.constraints = carConstraints;
            yield return new WaitForSecondsRealtime(.7f);
            point = ControlPoint(.2f, .25f);
            Touch(105, UnityEngine.InputSystem.TouchPhase.Began, point);
            yield return null; yield return null;
            screenRadius = Vector2.Distance(point, RectTransformUtility.WorldToScreenPoint(null, joystick.ring.TransformPoint(Vector3.right * joystick.radius)));
            Vector3 roadDirection = Vector3.ProjectOnPlane(testRotation * Vector3.forward, Vector3.up).normalized;
            Vector3 touchRight = Vector3.ProjectOnPlane(s.input.view.transform.right, Vector3.up).normalized;
            Vector3 touchForward = Vector3.ProjectOnPlane(s.input.view.transform.forward, Vector3.up).normalized;
            Vector2 roadDrag = new Vector2(Vector3.Dot(roadDirection, touchRight), Vector3.Dot(roadDirection, touchForward)).normalized;
            Touch(105, UnityEngine.InputSystem.TouchPhase.Moved, point + roadDrag * screenRadius * 1.15f);
            float touchPeak = 0, sustainedFast = 0;
            end = Time.realtimeSinceStartup + 12;
            while (Time.realtimeSinceStartup < end && sustainedFast < .4f)
            {
                yield return null;
                touchPeak = Mathf.Max(touchPeak, s.vehicle.ForwardSpeed);
                if (s.vehicle.ForwardSpeed > 9 && s.vehicle.GroundedWheels >= 3 && joystick.IsHeld && s.input.Magnitude > .98f && !s.input.Reverse)
                    sustainedFast += Time.unscaledDeltaTime;
                else sustainedFast = 0;
            }
            Check("sustained touch exceeds the previous nine metre speed", !s.input.testInput && joystick.IsHeld &&
                Mathf.Abs(s.vehicle.tuning.maxSpeed - 11.7f) < .05f && touchPeak > 9 && touchPeak <= s.vehicle.tuning.maxSpeed * 1.15f && sustainedFast >= .4f,
                "Production Touchscreen/UI peak=" + touchPeak.ToString("F2") + "m/s; configured=" + s.vehicle.tuning.maxSpeed +
                "; continuously above9 with grounded wheels=" + sustainedFast.ToString("F2") + "s");
            s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            Touch(105, UnityEngine.InputSystem.TouchPhase.Ended, point + roadDrag * screenRadius * 1.15f);
            yield return null; yield return null;

            Vector3 pad = DrivingPad();
            var directions = new[] { Vector2.up, Vector2.left, Vector2.down, new Vector2(1, -1).normalized };
            var names = new[] { "TEST B screen UP", "TEST C screen LEFT", "TEST D screen DOWN turns around", "TEST E screen DOWN-RIGHT diagonal" };
            for (int index = 0; index < directions.Length; index++)
            {
                Isolate(); s.input.testInput = false;
                Vector3 screenUp = Vector3.ProjectOnPlane(s.input.view.transform.forward, Vector3.up).normalized;
                PlaceCar(pad, Quaternion.LookRotation(screenUp)); s.vehicle.Body.constraints = carConstraints;
                yield return new WaitForSecondsRealtime(.5f);
                point = ControlPoint(index % 2 == 0 ? .2f : .75f, .35f);
                int touchId = 110 + index;
                Touch(touchId, UnityEngine.InputSystem.TouchPhase.Began, point);
                yield return null; yield return null;
                screenRadius = Vector2.Distance(point, RectTransformUtility.WorldToScreenPoint(null, joystick.ring.TransformPoint(Vector3.right * joystick.radius)));
                Vector2 direction = directions[index]; Quaternion before = s.vehicle.transform.rotation;
                Touch(touchId, UnityEngine.InputSystem.TouchPhase.Moved, point + direction * screenRadius);
                yield return null; yield return null;
                Vector3 cameraRight = Vector3.ProjectOnPlane(s.input.view.transform.right, Vector3.up).normalized;
                Vector3 expected = (cameraRight * direction.x + screenUp * direction.y).normalized;
                bool routed = joystick.IsHeld && s.input.UsesHeadingSteering && s.input.Magnitude > .9f &&
                    Vector3.Dot(s.input.WorldDirection, expected) > .999f;
                float firstTurn = Quaternion.Angle(before, s.vehicle.transform.rotation), headingError = 180;
                bool reversed = false; float minimumForward = 0; end = Time.realtimeSinceStartup + 7;
                do
                {
                    reversed |= s.input.Reverse; minimumForward = Mathf.Min(minimumForward, s.vehicle.ForwardSpeed);
                    headingError = Vector3.Angle(Vector3.ProjectOnPlane(s.vehicle.transform.forward, Vector3.up), expected);
                    if (headingError < 12 && s.vehicle.ForwardSpeed > 1.5f) break;
                    yield return null;
                } while (Time.realtimeSinceStartup < end);
                Vector3 alignedPosition = s.vehicle.transform.position;
                end = Time.realtimeSinceStartup + .45f;
                while (Time.realtimeSinceStartup < end) { reversed |= s.input.Reverse; yield return null; }
                Vector3 travel = Vector3.ProjectOnPlane(s.vehicle.transform.position - alignedPosition, Vector3.up);
                Vector3 visualTravel = s.input.view.WorldToViewportPoint(s.vehicle.transform.position) - s.input.view.WorldToViewportPoint(alignedPosition);
                Check(names[index] + " uses forward directional driving", routed && !reversed && minimumForward > -.4f &&
                    firstTurn < 25 && headingError < 12 && s.vehicle.ForwardSpeed > 1 &&
                    Vector3.Dot(travel, expected) > .35f && Vector2.Dot(((Vector2)visualTravel).normalized, direction) > .8f &&
                    Vector3.Dot(s.vehicle.transform.up, Vector3.up) > .85f,
                    "Routed=" + routed + "; reverse ever=" + reversed + "; first-frame turn=" + firstTurn.ToString("F1") +
                    "; heading error=" + headingError.ToString("F1") + "; forward speed=" + s.vehicle.ForwardSpeed.ToString("F2") +
                    "; travel along request=" + Vector3.Dot(travel, expected).ToString("F2") + "m; minimum signed speed=" + minimumForward.ToString("F2"));
                if (index == 2) Capture("joystick-down-forward");
                s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
                Vector2 outside = new Vector2(Screen.width * .95f, Screen.height * .85f);
                Touch(touchId, UnityEngine.InputSystem.TouchPhase.Moved, outside);
                yield return null; yield return null;
                bool capturedOutside = joystick.IsHeld;
                Touch(touchId, UnityEngine.InputSystem.TouchPhase.Ended, outside);
                yield return null; yield return null; yield return null;
                Check(index == 0 ? "TEST F release outside control area clears input" : names[index] + " release clears input",
                    capturedOutside && !joystick.IsHeld && s.input.Magnitude == 0 && joystick.Value == Vector2.zero &&
                    !joystick.ring.gameObject.activeInHierarchy && !s.input.Reverse,
                    "Pointer remained captured outside=" + capturedOutside + "; release hides ring and clears input");
            }

            Isolate(); s.input.testInput = false;
            point = ControlPoint(.78f, .65f);
            Touch(120, UnityEngine.InputSystem.TouchPhase.Began, point);
            yield return null; yield return null;
            Check("TEST G origin respawns at a different touch", joystick.IsHeld &&
                Vector2.Distance(joystick.OriginScreenPosition, point) < 1 &&
                Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, joystick.ring.position), point) < 1,
                "New touch/origin=" + point + "; fixed prior origin was not reused");
            Touch(120, UnityEngine.InputSystem.TouchPhase.Moved, point + Vector2.up * 80);
            yield return null; yield return null;
            bool dragged = joystick.IsHeld && s.input.Magnitude > .1f;
            Vector2 pause = RectTransformUtility.WorldToScreenPoint(null, s.hud.pause.transform.position);
            Touch(121, UnityEngine.InputSystem.TouchPhase.Began, pause);
            yield return null; yield return null;
            Touch(121, UnityEngine.InputSystem.TouchPhase.Ended, pause);
            yield return null; yield return null;
            Check("touch joystick and second-finger pause", dragged && s.State == SessionState.Paused && !joystick.IsHeld && s.input.Magnitude == 0,
                "Actual UI touch drag=" + dragged + "; second pointer paused=" + (s.State == SessionState.Paused));
            Touch(120, UnityEngine.InputSystem.TouchPhase.Ended, point + Vector2.up * 80);
            yield return null; s.SetPaused(false);
            Touch(122, UnityEngine.InputSystem.TouchPhase.Began, point);
            yield return null; yield return null;
            Touch(122, UnityEngine.InputSystem.TouchPhase.Moved, point + Vector2.up * 80);
            yield return null; yield return null;
            Touch(122, UnityEngine.InputSystem.TouchPhase.Canceled, point + Vector2.up * 80);
            yield return null; yield return null;
            Check("touch cancellation releases input", !joystick.IsHeld && joystick.Value == Vector2.zero && s.input.Magnitude == 0 && !joystick.ring.gameObject.activeInHierarchy,
                "Canceled production pointer resets thumb and drive input");
            InputSystem.RemoveDevice(syntheticTouch); syntheticTouch = null;
            if (Keyboard.current != null)
            {
                foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D })
                {
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
                    yield return null; yield return null;
                    Vector2 expected = key == Key.W ? Vector2.up : key == Key.A ? Vector2.left : key == Key.S ? Vector2.down : Vector2.right;
                    Check("desktop " + key + " preserves screen direction", !s.input.Reverse && s.input.Magnitude > .99f &&
                        Vector3.Dot(s.input.WorldDirection, s.input.ScreenToWorldDirection(expected)) > .999f,
                        "Direction=" + s.input.Direction + "; reverse=" + s.input.Reverse);
                }
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                yield return null; yield return null;
            }
            if (Mouse.current != null)
            {
                point = ControlPoint(.45f, .42f);
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point, buttons = 1 });
                yield return null; yield return null;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point + Vector2.right * 80, buttons = 1 });
                yield return null; yield return null;
                bool held = joystick.IsHeld && s.input.Direction.x > .99f && !s.input.Reverse;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point + Vector2.right * 80 });
                yield return null; yield return null;
                Check("desktop mouse can drag and release floating stick", held && !joystick.IsHeld && s.input.Magnitude == 0,
                    "Mouse drag routes through the existing UI and release clears input");
            }
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
            Check("victory result identifies the goal and actual settled score", s.hud.overlayKicker && s.hud.overlayKicker.text == "WARLORD DEFEATED" &&
                s.hud.overlayScore.text == "SCRAP SCORE  " + s.Scrap + "\nKILLS  " + s.Kills &&
                s.hud.resultBadge && s.hud.resultBadge.gameObject.activeSelf && !s.hud.resume.gameObject.activeSelf,
                "Victory names the defeated objective, celebrates it and reports only this run's actual score/kills");
            var completedGuidance = s.GetComponent<ArenaGuidance>();
            Check("completion hides onboarding and navigation prompts", completedGuidance && completedGuidance.CurrentHint == "" &&
                !completedGuidance.hintPanel.activeSelf && !completedGuidance.gestureRoot.activeSelf &&
                !completedGuidance.directionArrow.gameObject.activeSelf && !completedGuidance.directionLabel.gameObject.activeSelf,
                "Terminal result has no live driving/combat prompt");
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

        void Capture(string name, int width = 540, int height = 960)
        {
            var camera = s.input.view;
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
                Check("gameplay capture " + name, File.Exists(path) && new FileInfo(path).Length > 5000, path + " — actual gameplay camera and HUD, " + width + " x " + height);
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
