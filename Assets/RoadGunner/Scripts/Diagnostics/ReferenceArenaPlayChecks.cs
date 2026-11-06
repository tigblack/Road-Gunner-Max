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
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in saved-world acceptance. Authoring data is read by the Editor runner, never by gameplay.</summary>
    public sealed class ReferenceArenaPlayChecks : MonoBehaviour
    {
        public static bool PreviewOnly => Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-rgReferencePreviewOnly", StringComparison.OrdinalIgnoreCase));
        public static string ReportPath => "Documentation/RoadGunner/ReferenceArena-2026-11-06/" + (PreviewOnly ? "Preview.txt" : "PlayMode.txt");
        public const string ScreenshotFolder = "Documentation/RoadGunner/ReferenceArena-2026-11-06/Screenshots";
        public const string ArtifactFolder = "outputs/ReferenceArena-2026-11-06";
        [Serializable] public sealed class RouteSpec
        { public string Id; public float Width; public Vector3[] Points; public Vector3 AuthoredStart, AuthoredEnd; }
        [Serializable] public sealed class GateSpec
        { public string Name; public Transform Root; public Collider[] Colliders; public Vector3 Position, Expected, Inside; }
        [Serializable] public sealed class LandmarkSpec
        { public string Name; public Vector3 Position; }
        public sealed class Specification
        {
            public RouteSpec[] Routes; public GateSpec[] Gates; public LandmarkSpec[] Landmarks;
            public Vector3 Junction, East, North, WorkshopForward; public Vector3[] Footprint;
            public float WidthScale; public Collider Ground; public MeshCollider Road;
        }
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        Specification spec; GameSession s; VehicleController car; LevelLayout layout;
        ReferenceArenaContactProbe contact;
        ArenaDecorationCrush crush; DecorationCrushChunk testedStone; int testedStoneIndex = -1;
        readonly List<string> report = new List<string>();
        readonly Dictionary<Behaviour, bool> componentStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Raider, bool> raiderAi = new Dictionary<Raider, bool>();
        readonly Dictionary<EnemyBuggy, bool> buggyAi = new Dictionary<EnemyBuggy, bool>();
        WastelandMutant monster; bool monsterAi;
        RigidbodyConstraints originalConstraints; bool originalInput, originalCanDrive, originalTestInput, originalBrake, originalDamage;
        Vector2 originalDirection; float originalTimeScale; int originalFps, originalVsync;
        bool running, captured, restored;
        struct Ledger { public int Kills, Scrap, Earned, Stored, Reward; public float HP, Max; }
        Ledger Snapshot() => new Ledger { Kills = s.Kills, Scrap = s.Scrap, Earned = s.ScrapEarned,
            Stored = s.pickups.StoredValue, Reward = s.exploration.RewardSerial, HP = s.health.Current, Max = s.health.Maximum };
        CombatTarget[] Roster() => s.spawns.authoredRaiders.Cast<CombatTarget>().Concat(s.spawns.authoredBuggies).Concat(s.zone1.ExtraTargets).ToArray();

        public void Begin(Specification value)
        {
            if (running) return; running = true; spec = value;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); Directory.CreateDirectory(ScreenshotFolder); Directory.CreateDirectory(ArtifactFolder);
            report.Add("Reference-shaped Arena1 — actual saved world, Unity " + Application.unityVersion);
            if (PreviewOnly) report.Add("PREVIEW ONLY: saved-scene assertions and native captures; physical route/stone/workshop/gate cases are not run. The independent full PlayMode report is preserved.");
            report.Add("The1.5 width ratio is our normalized vehicle adaptation, not measured reference metres. Setup ResetPose and administrative enemy despawn are disclosed fixtures. Every moving case uses production DriveInput and Rigidbody/WheelCollider physics; no moving velocity/rotation writes. Closed gates and roads are never substituted or moved.");
            Write(); StartCoroutine(Guarded());
        }
        IEnumerator Guarded()
        {
            yield return null; yield return null;
            var factories = PreviewOnly ? new Func<IEnumerator>[] { SavedScene, LandmarkCaptures } :
                new Func<IEnumerator>[] { SavedScene, LandmarkCaptures, Routes, SmallStone, JunctionDriving, WorkshopDriving, ClosedGates, Restart };
            foreach (var factory in factories)
            {
                var stack = new Stack<IEnumerator>(); stack.Push(factory()); bool failed = false;
                while (stack.Count > 0 && !failed)
                {
                    var part = stack.Peek(); bool more = false; object current = null;
                    try { more = part.MoveNext(); if (more) current = part.Current; }
                    catch (Exception e) { Check(factory.Method.Name + " exception", false, e.ToString()); failed = true; }
                    if (failed) break;
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested); else yield return current;
                }
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                if (!captured) break;
            }
            try { Restore(); } catch (Exception e) { Check("restore", false, e.ToString()); }
            report.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; no scene, mesh, tuning or asset writes.");
            running = false; Completed = true; Write();
        }
        IEnumerator SavedScene()
        {
            s = GetComponent<GameSession>(); layout = Object.FindAnyObjectByType<LevelLayout>();
            if (!s || !layout || spec == null || !spec.Ground || !spec.Road) throw new InvalidOperationException("Missing saved session, terrain, road or runner specification.");
            car = s.vehicle;
            originalConstraints = car.Body.constraints; originalInput = s.input.Enabled; originalCanDrive = car.CanDrive;
            originalTestInput = s.input.testInput; originalDirection = s.input.testDirection; originalBrake = s.input.testBrake;
            originalDamage = s.health.DamageEnabled; originalTimeScale = Time.timeScale; originalFps = Application.targetFrameRate; originalVsync = QualitySettings.vSyncCount;
            captured = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            var roster = Roster();
            foreach (var actor in roster.OfType<Raider>()) raiderAi[actor] = actor.AIEnabled;
            foreach (var actor in roster.OfType<EnemyBuggy>()) buggyAi[actor] = actor.AIEnabled;
            monster = roster.OfType<WastelandMutant>().Single(); monsterAi = monster.AIEnabled;
            Check("saved baseline keeps16 actors and15 eligible respawns", s.Playing && roster.Length == 16 && roster.Distinct().Count() == 16 && roster.All(t => t && t.Alive) &&
                roster.OfType<Raider>().Count(r => !r.arenaBoss) == 12 && roster.OfType<Raider>().Count(r => r.arenaBoss) == 1 &&
                roster.OfType<EnemyBuggy>().Count() == 2 && s.respawns && s.respawns.isActiveAndEnabled && s.respawns.RegisteredCount == 15 &&
                s.respawns.PendingCount == 0 && Mathf.Abs(s.respawns.respawnDelaySeconds - 30) < .001f,
                "Alive=" + roster.Count(t => t.Alive) + "; eligible=" + s.respawns.RegisteredCount + "; delay=" + s.respawns.respawnDelaySeconds);
            Check("geometry preserves HP combat rewards and progression objects", Mathf.Abs(s.health.Maximum - 100) < .001f && Mathf.Abs(s.health.Current - 100) < .001f &&
                Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 5.25f) < .001f && s.Kills == 0 && s.Scrap == 0 && s.ScrapEarned == 0 &&
                s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 && s.runStats.stations.Length == 2 && s.exploration.caches.Length == 8 &&
                s.exploration.SecretCount == 3 && s.exploration.zones.Length == 4 && s.minimap.workshops.Length > 0,
                "HP=" + s.health.Current + "/" + s.health.Maximum + "; bullet=" + s.PlayerDamage(s.turret.tuning.bulletDamage) + "; caches=" + s.exploration.caches.Length);
            Check("normalized crossroads width is increased by fifty percent", Mathf.Abs(spec.WidthScale - 1.5f) < .001f &&
                Width("main-south", 30) && Width("main-north", 30) && Width("cross-west", 27) && Width("cross-east", 27) && Width("workshop-drive-through", 18),
                "Normalized width scale=" + spec.WidthScale + "; main30m, cross27m, service18m; no claim about original reference metres");
            SaveDisable(s.turret); SaveDisable(s.spawns); SaveDisable(s.exploration); SaveDisable(car.GetComponent<HarpoonCollector>());
            foreach (var station in s.runStats.stations) SaveDisable(station);
            foreach (var workshop in s.minimap.workshops) SaveDisable(workshop);
            foreach (var actor in raiderAi.Keys) { actor.AIEnabled = false; if (actor.agent && actor.agent.isOnNavMesh) actor.agent.isStopped = true; }
            foreach (var actor in buggyAi.Keys) { actor.AIEnabled = false; SaveDisable(actor.gun); }
            monster.AIEnabled = false; s.health.DamageEnabled = false; car.Body.constraints = RigidbodyConstraints.FreezeAll;
            yield return new WaitForSecondsRealtime(.15f);
            bool connected = NavMesh.SamplePosition(s.startPosition, out var start, 3, NavMesh.AllAreas);
            foreach (var actor in roster) connected &= CompletePath(start.position, actor.transform.position);
            Check("every saved encounter has navigation from the new start", connected && !layout.navigationNeedsBake,
                "Actual saved16-actor positions; navigationNeedsBake=" + layout.navigationNeedsBake);
            var map = s.minimap; var image = map.backgroundImage; bool mapFit = map.hasBakedWorldBounds && map.BackgroundTexture && image && image.texture == map.BackgroundTexture;
            if (mapFit)
            {
                mapFit &= Vector2.Distance(image.rectTransform.rect.size, map.mapRect.rect.size * map.contentScale) < .02f;
                foreach (var p in spec.Footprint) mapFit &= map.IsInsideCircle(map.Project(p), 12);
                foreach (var actor in roster) mapFit &= map.TryGetMarker(actor.transform, out var p) && Vector2.Distance(p, map.Project(actor.transform.position)) < .02f;
                var backdrop = map.mapRect.Find("Warm sand behind arena");
                mapFit &= backdrop && backdrop.GetSiblingIndex() < image.transform.GetSiblingIndex();
            }
            Check("rebaked minimap contains the new footprint and exact live actors", mapFit,
                "Footprint corners fit circular view; real background above backdrop; all16 actor marker projections agree");
        }
        bool Width(string id, float expected) => spec.Routes.Any(r => r.Id == id && Mathf.Abs(r.Width - expected) < .001f);
        IEnumerator LandmarkCaptures()
        {
            foreach (var item in spec.Landmarks)
            {
                Place(item.Position, Quaternion.LookRotation(spec.North));
                yield return null; yield return null; Capture(item.Name);
            }
            CaptureOverview();
            // Clear only actors for the geometric driving fixtures; all actual saved scenery, caches, roads and gates remain.
            foreach (var r in s.spawns.pool) if (r) r.Despawn();
            foreach (var b in s.spawns.buggyPool) if (b) b.Despawn();
            s.zone1.DespawnAll(); s.health.DamageEnabled = true; Physics.SyncTransforms();
        }
        IEnumerator Routes()
        {
            bool startFound = NavMesh.SamplePosition(s.startPosition, out var start, 3, NavMesh.AllAreas);
            var csv = new List<string> { "id,width,samples,navigation,chassis_clear,cross_sections,first_obstruction" };
            foreach (var route in spec.Routes)
            {
                int nav = 0, clear = 0, covered = 0, sections = 0; string obstruction = "";
                var points = route.Points;
                for (int i = 0; i < points.Length; i++)
                {
                    if (NavMesh.SamplePosition(points[i], out _, 1, NavMesh.AllAreas)) nav++;
                    var direction = Flat(points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                    string hit = "no tangent";
                    if (direction.sqrMagnitude > .1f && CarClear(Ground(points[i]) + Vector3.up * .7f, Quaternion.LookRotation(direction), out hit)) clear++;
                    else if (obstruction == "") obstruction = direction.sqrMagnitude <= .1f ? "no tangent" : hit;
                    if (i % Mathf.Max(1, points.Length / 12) == 0 && direction.sqrMagnitude > .1f)
                    {
                        sections++; bool section = true; var side = Vector3.Cross(Vector3.up, direction);
                        foreach (float offset in new[] { -.42f, 0, .42f })
                            section &= spec.Road.Raycast(new Ray(points[i] + side * (route.Width * offset) + Vector3.up * 4, Vector3.down), out _, 12);
                        if (section) covered++;
                    }
                    if (i % 80 == 0) yield return null;
                }
                bool aligned = points.Length >= 2 && FlatDistance(points[0], route.AuthoredStart) < .15f && FlatDistance(points[points.Length - 1], route.AuthoredEnd) < .15f;
                bool connected = points.Length >= 2 && startFound && CompletePath(start.position, points[0]) && CompletePath(start.position, points[points.Length - 1]);
                Check("route " + route.Id + " has full saved-car clearance and navigation", aligned && connected && route.Width >= 8 && nav == points.Length && clear == points.Length && sections > 0 && covered == sections,
                    "width=" + route.Width + "; nav=" + nav + "/" + points.Length + "; car=" + clear + "; width sections=" + covered + "/" + sections + "; obstruction=" + obstruction);
                csv.Add(route.Id + "," + route.Width + "," + points.Length + "," + nav + "," + clear + "," + covered + "/" + sections + "," + obstruction);
            }
            File.WriteAllLines(ArtifactFolder + "/RouteClearance.csv", csv);
            bool recovery = s.recoveryPoints != null && s.recoveryPoints.Length > 0;
            foreach (var point in s.recoveryPoints ?? Array.Empty<Vector3>()) recovery &= CompletePath(start.position, point) && CarClear(point, s.startRotation, out _);
            Check("saved recovery sites have navigable full-car standing space", startFound && recovery, "Recovery points=" + (s.recoveryPoints == null ? 0 : s.recoveryPoints.Length));
        }
        IEnumerator SmallStone()
        {
            crush = Object.FindAnyObjectByType<ArenaDecorationCrush>();
            if (!crush || !crush.isActiveAndEnabled) { Check("a registered saved small stone has a real clear driving approach", false, "No enabled production scenery-crush manager"); yield break; }
            Vector3 start = default, end = default, direction = default; string id = "";
            var roadPoints = spec.Routes.SelectMany(r => r.Points).ToArray();
            foreach (var chunk in crush.chunks.Where(c => c && c.isActiveAndEnabled))
            {
                for (int i = 0; i < chunk.items.Length && !testedStone; i++)
                {
                    var item = chunk.items[i];
                    if (item == null || item.kind != DecorationCrushKind.Stone || !chunk.ValidItem(i) || chunk.IsBroken(i) || !item.probe ||
                        !item.probe.enabled || !item.probe.isTrigger || item.probe.gameObject.layer != 12) continue;
                    var bounds = chunk.GetWorldBounds(i);
                    if (bounds.size.y > .7f || Mathf.Max(bounds.size.x, bounds.size.z) > 1.5f) continue;
                    foreach (var point in roadPoints.OrderBy(p => Flat(p - bounds.center).sqrMagnitude).Take(32))
                    {
                        var toward = Flat(bounds.center - point).normalized; if (toward.sqrMagnitude < .1f) continue;
                        var a = bounds.center - toward * 9; var b = bounds.center + toward * 5;
                        bool clear = true;
                        for (int step = 0; step <= 28 && clear; step++)
                        {
                            var wanted = Vector3.Lerp(a, b, step / 28f);
                            if (!spec.Ground.Raycast(new Ray(wanted + Vector3.up * 100, Vector3.down), out var floor, 250)) { clear = false; break; }
                            var p = floor.point + Vector3.up * .7f;
                            clear = NavMesh.SamplePosition(p, out _, 1.5f, NavMesh.AllAreas) && CarClear(p, Quaternion.LookRotation(toward), out _);
                        }
                        if (!clear) continue;
                        testedStone = chunk; testedStoneIndex = i; start = Ground(a) + Vector3.up * .8f; end = b; direction = toward; id = item.id; break;
                    }
                }
                if (testedStone) break;
            }
            Check("a registered saved small stone has a real clear driving approach", testedStone,
                "Actual authored Stone=" + id + "; 9m approach and5m exit checked at0.5m spacing; no copied/replaced/moved prop or probe");
            if (!testedStone) yield break;
            var original = testedStone.OriginalMesh; var indices = Flatten(original); var pose = testedStone.source.transform.localToWorldMatrix;
            var probe = testedStone.items[testedStoneIndex].probe; int registered = crush.RegisteredCount; var registry = crush.chunks;
            yield return Settle(start, Quaternion.LookRotation(direction)); var ledger = Snapshot(); Capture("small-stone-before");
            var result = new DriveResult(); float brokeAt = -1; bool capturedDebris = false;
            yield return Drive(new[] { end }, result, 14, () => {
                if (testedStone.IsBroken(testedStoneIndex) && brokeAt < 0) brokeAt = Time.fixedTime;
                if (!capturedDebris && brokeAt >= 0 && Time.fixedTime - brokeAt >= .12f)
                { Capture("small-stone-debris"); capturedDebris = true; }
            });
            bool collapsed = testedStone.RuntimeMesh; bool changed = false;
            if (collapsed)
            {
                var after = Flatten(testedStone.RuntimeMesh); var item = testedStone.items[testedStoneIndex];
                for (int r = 0; r < item.rangeStarts.Length; r++)
                    for (int i = item.rangeStarts[r]; i < item.rangeStarts[r] + item.rangeCounts[r]; i += 3)
                    { collapsed &= after[i] == after[i + 1] && after[i] == after[i + 2]; changed |= after[i + 1] != indices[i + 1] || after[i + 2] != indices[i + 2]; }
            }
            Check("real input drive breaks the saved small stone without HP or reward changes", result.Reached && result.Travel > 10 && result.Grounded >= 10 &&
                testedStone.IsBroken(testedStoneIndex) && !probe.enabled && collapsed && changed && original != testedStone.RuntimeMesh && indices.SequenceEqual(Flatten(original)) &&
                testedStone.source.transform.localToWorldMatrix == pose && ReferenceEquals(crush.chunks, registry) && crush.RegisteredCount == registered && ledger.Equals(Snapshot()) && s.health.DamageEnabled,
                "ID=" + id + "; " + DrivingDetails(result) + "; broken=" + testedStone.IsBroken(testedStoneIndex) + "; probe=" + probe.enabled +
                "; original indices unchanged=" + indices.SequenceEqual(Flatten(original)) + "; ledger unchanged=" + ledger.Equals(Snapshot()));
        }
        static int[] Flatten(Mesh mesh) => Enumerable.Range(0, mesh.subMeshCount).SelectMany(i => mesh.GetIndices(i)).ToArray();
        sealed class DriveResult
        { public bool Reached; public int Grounded; public float Travel, Upright = 1, Peak; public int Contacts; }
        IEnumerator JunctionDriving()
        {
            var axes = new[] { spec.North, spec.East, -spec.North, -spec.East };
            var names = new[] { "south to north", "west to east", "north to south", "east to west" };
            for (int i = 0; i < axes.Length; i++)
            {
                var start = Ground(spec.Junction - axes[i] * 16) + Vector3.up * .8f;
                yield return Settle(start, Quaternion.LookRotation(axes[i])); var ledger = Snapshot(); var result = new DriveResult();
                yield return Drive(new[] { spec.Junction + axes[i] * 16 }, result, 14);
                Check("actual crossroads straight drive " + names[i], result.Reached && result.Travel > 26 && result.Grounded >= 10 && result.Upright > .85f && result.Contacts == 0 && ledger.Equals(Snapshot()),
                    DrivingDetails(result) + "; HP and reward ledger unchanged=" + ledger.Equals(Snapshot()));
            }
            var exits = new[] { spec.East, spec.North, -spec.East, -spec.North };
            for (int i = 0; i < axes.Length; i++)
            {
                yield return Settle(Ground(spec.Junction - axes[i] * 16) + Vector3.up * .8f, Quaternion.LookRotation(axes[i]));
                var result = new DriveResult(); var ledger = Snapshot();
                yield return Drive(new[] { spec.Junction - axes[i] * 3, spec.Junction + exits[i] * 16 }, result, 24);
                Check("actual crossroads controlled turn from arm " + i, result.Reached && result.Travel > 20 && result.Grounded >= 10 && result.Upright > .85f && result.Contacts == 0 && ledger.Equals(Snapshot()),
                    DrivingDetails(result) + "; alternating left/right turns use only live directional input");
            }
        }
        IEnumerator WorkshopDriving()
        {
            var workshop = s.minimap.workshops.First(); var forward = spec.WorkshopForward.normalized;
            var start = Ground(workshop.transform.position - forward * (workshop.healingRadius + 5)) + Vector3.up * .8f;
            var end = workshop.transform.position + forward * (workshop.healingRadius + 5);
            yield return Settle(start, Quaternion.LookRotation(forward));
            s.health.DamageEnabled = true; s.health.Damage(40, false); workshop.enabled = true;
            float before = s.health.Current; var ledger = Snapshot(); var result = new DriveResult(); bool repaired = false;
            yield return Drive(new[] { end }, result, 18, () => { repaired |= workshop.IsRepairing; });
            workshop.enabled = false;
            Check("actual vehicle enters repairs and exits the saved workshop", result.Reached && repaired && s.health.Current > before && result.Grounded >= 10 && result.Upright > .9f && result.Contacts == 0 &&
                s.Kills == ledger.Kills && s.ScrapEarned == ledger.Earned && s.Scrap == ledger.Scrap && s.exploration.RewardSerial == ledger.Reward,
                DrivingDetails(result) + "; repairing=" + repaired + "; HP=" + before + " -> " + s.health.Current);
            s.health.ResetHealth();
        }
        IEnumerator ClosedGates()
        {
            foreach (var gate in spec.Gates)
            {
                Vector3 outward = Flat(gate.Position - spec.Junction).normalized;
                var colliders = new HashSet<Collider>(gate.Colliders);
                Vector3 from = Ground(gate.Inside) + Vector3.up * 1.2f, to = Ground(gate.Position + outward * 3) + Vector3.up * 1.2f;
                bool line = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore).Any(h => colliders.Contains(h.collider));
                bool path = NavMesh.SamplePosition(s.startPosition, out var nav, 3, NavMesh.AllAreas) && CompletePath(nav.position, gate.Inside);
                Check(gate.Name + " is a real closed barrier with a reachable inside approach", gate.Root && gate.Root.gameObject.activeInHierarchy && gate.Colliders.Length > 0 &&
                    gate.Colliders.All(c => c && c.enabled && !c.isTrigger && c.gameObject.layer == 0) && FlatDistance(gate.Position, gate.Expected) < .1f && line && path,
                    "Saved colliders=" + gate.Colliders.Length + "; LOS blocked=" + line + "; inside path=" + path);
                yield return Settle(Ground(gate.Inside) + Vector3.up * .8f, Quaternion.LookRotation(outward));
                var ledger = Snapshot(); contact.ResetContacts(colliders); float began = Time.fixedTime; Vector3 initial = car.Body.position;
                float deadline = Time.realtimeSinceStartup + 24; Command(outward);
                while (Time.fixedTime - began < 9 && Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForFixedUpdate(); Command(outward);
                    if (contact.ExpectedContacts > 0 && Time.fixedTime - contact.FirstExpectedContact >= .65f) break;
                }
                float plane = Vector3.Dot(Flat(car.Body.position - gate.Position), outward); float travel = FlatDistance(initial, car.Body.position);
                Command(Vector3.zero, true);
                Check(gate.Name + " stops a real physical vehicle without passage or rewards", contact.ExpectedContacts > 0 && contact.ExpectedImpulse > .01f && travel > 3 &&
                    plane < .3f && Vector3.Dot(car.transform.up, Vector3.up) > .85f && ledger.Equals(Snapshot()),
                    "contacts=" + contact.ExpectedContacts + "; impulse=" + contact.ExpectedImpulse + "; travel=" + travel + "; signed gate plane=" + plane + "; car=" + car.Body.position);
                Capture("closed-" + gate.Name.ToLowerInvariant().Replace(" ", "-"));
            }
        }
        IEnumerator Restart()
        {
            int serial = s.RunSerial; s.Restart(); s.turret.enabled = false; car.Body.constraints = RigidbodyConstraints.FreezeAll;
            yield return null; yield return null;
            Check("new-layout Restart restores the original fresh run and spawn", s.RunSerial > serial && s.Playing && s.Kills == 0 && s.Scrap == 0 && s.ScrapEarned == 0 &&
                Mathf.Abs(s.health.Current - 100) < .001f && Mathf.Abs(s.health.Maximum - 100) < .001f && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                Roster().Length == 16 && Roster().All(t => t.Alive) && s.respawns.PendingCount == 0 && s.exploration.caches.All(c => !c.Claimed) &&
                FlatDistance(car.Body.position, s.startPosition) < .1f,
                "Serial=" + serial + " -> " + s.RunSerial + "; live=" + Roster().Count(t => t.Alive) + "; pending=" + s.respawns.PendingCount);
            Check("Restart restores broken scenery and the saved small-stone probe", crush && testedStone && testedStoneIndex >= 0 && crush.BrokenCount == 0 &&
                !testedStone.IsBroken(testedStoneIndex) && testedStone.items[testedStoneIndex].probe && testedStone.items[testedStoneIndex].probe.enabled &&
                crush.chunks.Where(c => c).All(c => Enumerable.Range(0, c.items.Length).All(i => !c.IsBroken(i))),
                "Production broken count=" + (crush ? crush.BrokenCount : -1) + "; same saved selected stone/probe rearmed");
        }
        IEnumerator Settle(Vector3 position, Quaternion rotation)
        {
            car.Body.constraints = originalConstraints; Place(position, rotation); s.input.Enabled = true; car.CanDrive = true; Command(Vector3.zero, true);
            float began = Time.fixedTime, watchdog = Time.realtimeSinceStartup + 6;
            while (Time.fixedTime - began < 1 && Time.realtimeSinceStartup < watchdog) yield return new WaitForFixedUpdate();
            if (car.GroundedWheels < 3 || car.Speed > .35f) throw new InvalidOperationException("Driving fixture did not settle on actual saved road: contacts=" + car.GroundedWheels + "; speed=" + car.Speed + "; position=" + car.Body.position);
            if (!contact) contact = car.gameObject.AddComponent<ReferenceArenaContactProbe>();
            contact.Ground = spec.Ground; contact.Road = spec.Road; contact.Player = car.transform; contact.ResetContacts(null);
        }
        IEnumerator Drive(Vector3[] waypoints, DriveResult result, float budget, Action sample = null)
        {
            int next = 0; float began = Time.fixedTime, watchdog = Time.realtimeSinceStartup + budget * 3; Vector3 previous = car.Body.position;
            while (Time.fixedTime - began < budget && Time.realtimeSinceStartup < watchdog)
            {
                Vector3 delta = Flat(waypoints[next] - car.Body.position);
                if (delta.magnitude < (next == waypoints.Length - 1 ? 2.5f : 4))
                { if (++next == waypoints.Length) { result.Reached = true; break; } delta = Flat(waypoints[next] - car.Body.position); }
                Command(delta.normalized); yield return new WaitForFixedUpdate();
                result.Travel += FlatDistance(previous, car.Body.position); previous = car.Body.position;
                result.Upright = Mathf.Min(result.Upright, Vector3.Dot(car.transform.up, Vector3.up));
                result.Peak = Mathf.Max(result.Peak, car.HorizontalSpeed); if (car.GroundedWheels >= 3) result.Grounded++;
                sample?.Invoke(); if (!s.Playing || result.Upright < .5f) break;
            }
            Command(Vector3.zero, true); result.Contacts = contact.UnexpectedContacts;
        }
        static string DrivingDetails(DriveResult r) => "reached=" + r.Reached + "; travel=" + r.Travel.ToString("F2") + "; grounded steps=" + r.Grounded + "; min up=" + r.Upright.ToString("F3") + "; peak=" + r.Peak.ToString("F2") + "; hard contacts=" + r.Contacts;
        void Place(Vector3 position, Quaternion rotation) { car.ResetPose(position, rotation); Physics.SyncTransforms(); s.follow.Snap(); }
        void Command(Vector3 direction, bool brake = false)
        {
            var right = Flat(s.input.view.transform.right).normalized; var north = Flat(s.input.view.transform.forward).normalized;
            s.input.testInput = true; s.input.testDirection = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, north)); s.input.testBrake = brake;
        }
        Vector3 Ground(Vector3 point)
        {
            if (!spec.Ground.Raycast(new Ray(point + Vector3.up * 100, Vector3.down), out var hit, 250)) throw new InvalidOperationException("No actual saved terrain below " + point);
            return hit.point;
        }
        bool CarClear(Vector3 root, Quaternion rotation, out string obstruction)
        {
            var boxes = car.GetComponentsInChildren<BoxCollider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.attachedRigidbody == car.Body).ToArray();
            if (boxes.Length == 0) throw new InvalidOperationException("The saved player has no active physical BoxCollider for the chassis clearance test.");
            foreach (var box in boxes)
            {
                var localCentre = car.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
                var half = Vector3.Scale(box.size * .5f, box.transform.lossyScale) + new Vector3(.15f, 0, .15f);
                var centre = root + rotation * localCentre; var orientation = rotation * Quaternion.Inverse(car.transform.rotation) * box.transform.rotation;
                foreach (var hit in Physics.OverlapBox(centre, half, orientation, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(car.transform) || hit == spec.Ground || hit.bounds.max.y <= centre.y - half.y + .04f) continue;
                    obstruction = hit.name; return false;
                }
            }
            obstruction = ""; return true;
        }
        static bool CompletePath(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(to, out var sample, 2, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath(); return NavMesh.CalculatePath(from, sample.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
        static Vector3 Flat(Vector3 p) => Vector3.ProjectOnPlane(p, Vector3.up);
        static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;
        void SaveDisable(Behaviour value) { if (value && !componentStates.ContainsKey(value)) { componentStates[value] = value.enabled; value.enabled = false; } }
        void Check(string name, bool pass, string detail) { Checks++; if (!pass) Failures++; string line = (pass ? "PASS " : "FAIL ") + name + ": " + detail; report.Add(line); Write(); Debug.Log("REFERENCE ARENA " + line); }
        void Write() => File.WriteAllLines(ReportPath, report);
        sealed class CanvasState { public Canvas Canvas; public RenderMode Mode; public Camera Camera; public float Plane; }
        void CaptureOverview()
        {
            var owner = new GameObject("Temporary reference acceptance overview"); owner.hideFlags = HideFlags.DontSave;
            var camera = owner.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.transform.rotation = Quaternion.Euler(90, 45, 0); camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.76f, .57f, .34f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            Vector3 centre = spec.Footprint.Aggregate(Vector3.zero, (sum, p) => sum + p) / spec.Footprint.Length;
            const int width = 1400, height = 1100; camera.aspect = (float)width / height;
            float halfX = 0, halfY = 0;
            foreach (var p in spec.Footprint)
            { halfX = Mathf.Max(halfX, Mathf.Abs(Vector3.Dot(p - centre, camera.transform.right))); halfY = Mathf.Max(halfY, Mathf.Abs(Vector3.Dot(p - centre, camera.transform.up))); }
            camera.orthographicSize = Mathf.Max(halfY, halfX / camera.aspect) * 1.08f;
            camera.transform.position = new Vector3(centre.x, spec.Ground.bounds.max.y + 450, centre.z);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Select(c => (canvas: c, enabled: c.enabled)).ToArray();
            var target = RenderTexture.GetTemporary(width, height, 24); var active = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            bool oldFog = RenderSettings.fog;
            try
            {
                foreach (var c in canvases) c.canvas.enabled = false;
                // The gameplay haze reaches full opacity at140m; the fitted diagnostic camera is450m above the floor.
                RenderSettings.fog = false;
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                string path = ScreenshotFolder + "/native-overview.png"; File.WriteAllBytes(path, image.EncodeToPNG());
                report.Add("CAPTURE " + path + "; actual saved terrain/scenery and live neutralized actors; temporary fitted top-down camera pitch90/yaw45; HUD and distance fog hidden only for this overview, then restored"); Write();
            }
            finally
            {
                foreach (var c in canvases) if (c.canvas) c.canvas.enabled = c.enabled;
                RenderSettings.fog = oldFog;
                RenderTexture.active = active; camera.targetTexture = null; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image); Object.DestroyImmediate(owner);
            }
        }
        void Capture(string name)
        {
            var camera = s.input.view; const int width = 540, height = 960; var target = RenderTexture.GetTemporary(width, height, 24);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { Canvas = c, Mode = c.renderMode, Camera = c.worldCamera, Plane = c.planeDistance }).ToArray();
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target;
                foreach (var c in canvases) { c.Canvas.renderMode = RenderMode.ScreenSpaceCamera; c.Canvas.worldCamera = camera; c.Canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1); }
                Canvas.ForceUpdateCanvases(); RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                string path = ScreenshotFolder + "/" + name + ".png"; File.WriteAllBytes(path, image.EncodeToPNG());
                report.Add("CAPTURE " + path + "; actual game camera + HUD; setup car=" + car.transform.position); Write();
            }
            finally
            {
                foreach (var c in canvases) if (c.Canvas) { c.Canvas.renderMode = c.Mode; c.Canvas.worldCamera = c.Camera; c.Canvas.planeDistance = c.Plane; }
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image);
            }
        }
        void Restore()
        {
            if (restored || !captured || !s) return; restored = true;
            Command(Vector3.zero, true); car.Body.constraints = originalConstraints;
            foreach (var item in componentStates) if (item.Key) item.Key.enabled = item.Value;
            s.Restart();
            foreach (var item in raiderAi) if (item.Key) item.Key.AIEnabled = item.Value;
            foreach (var item in buggyAi) if (item.Key) item.Key.AIEnabled = item.Value;
            if (monster) monster.AIEnabled = monsterAi;
            car.Body.constraints = originalConstraints; car.CanDrive = originalCanDrive; s.health.DamageEnabled = originalDamage;
            s.input.Clear(); s.input.Enabled = originalInput; s.input.testInput = originalTestInput; s.input.testDirection = originalDirection; s.input.testBrake = originalBrake;
            Time.timeScale = originalTimeScale; Application.targetFrameRate = originalFps; QualitySettings.vSyncCount = originalVsync;
            if (contact) Object.DestroyImmediate(contact);
        }
        void OnDestroy() { if (captured && !restored) Restore(); }
    }
    public sealed class ReferenceArenaContactProbe : MonoBehaviour
    {
        public Collider Ground, Road; public Transform Player; HashSet<Collider> expected;
        public int ExpectedContacts { get; private set; } public float ExpectedImpulse { get; private set; }
        public float FirstExpectedContact { get; private set; } public int UnexpectedContacts { get; private set; }
        public void ResetContacts(HashSet<Collider> value) { expected = value; ExpectedContacts = UnexpectedContacts = 0; ExpectedImpulse = 0; FirstExpectedContact = -1; }
        void OnCollisionEnter(Collision collision) { Record(collision); }
        void OnCollisionStay(Collision collision) { Record(collision); }
        void Record(Collision collision)
        {
            var other = collision.collider;
            if (expected != null && expected.Contains(other))
            { if (ExpectedContacts++ == 0) FirstExpectedContact = Time.fixedTime; ExpectedImpulse += collision.impulse.magnitude; return; }
            if (other == Ground || other == Road || other.transform.IsChildOf(Player)) return;
            // Wheel/ground contacts are expected, while authored upright solids must not be scraped through.
            if (collision.contacts.All(c => c.normal.y > .8f)) return;
            UnexpectedContacts++;
        }
    }
}
#endif
