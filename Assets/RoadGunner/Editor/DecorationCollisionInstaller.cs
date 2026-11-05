using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Shoulder = RoadGunner.ConceptArt.Editor.ConceptDressing;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Adds collision semantics to the saved art. Original visual meshes, materials and poses are
    /// authoritative. No full visual/world/shoulder installer is called. A verified catalog permits
    /// repeat installation after the new colliders have changed the original placement-query scene.
    /// </summary>
    public static class DecorationCollisionInstaller
    {
        public const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";
        public const string OwnedRoot = "Scenery crush collisions";
        public const string ReportFolder = "Documentation/RoadGunner/SceneryCrush-2026-11-03";
        public const string CatalogPath = ReportFolder + "/Authoring.json";
        public const string AssetFolder = "Assets/RoadGunner/Levels/Greenwood/SceneryCrush";
        public const int ProbeLayer = 12;
        const string ProbeLayerName = "RG_SceneryCrush";
        const int Schema = 1;
        const float VertexTolerance = .0001f, MatchTolerance = .001f;

        [Serializable] public sealed class SourceRecord
        {
            public string id, hierarchy, meshGuid, geometryHash, materials, pose;
            public int vertices, indices;
        }
        [Serializable] public sealed class ItemRecord
        {
            public string id, category, sourceId, ownerId, handling;
            public DecorationCrushKind kind;
            public int[] starts, counts;
            public Bounds localBounds;
        }
        [Serializable] public sealed class SolidRecord { public string id, signature; }
        [Serializable] public sealed class CountRecord { public string category; public int soft, hard, surface, composite; }
        [Serializable] public sealed class StoneBoundsRecord
        {
            public string id, sourceId;
            public Vector3 worldCenter, exactWorldSize, previousTransformedBoxSize;
        }
        [Serializable] public sealed class RelocationRecord
        {
            public string id, sourceId, ownerId, oldPose, newPose, reason;
            public Vector3 oldPosition, newPosition, delta;
        }
        [Serializable] public sealed class Catalog
        {
            public int schema = Schema, arcadePrimaryItems, arcadeChunks, arcadeVertices, shoulderInstances, shoulderMembers;
            public string sceneGuid, verifiedAtUtc, verification;
            public SourceRecord[] sources;
            public ItemRecord[] items;
            public string[] replacedSoftColliders;
            public SolidRecord[] protectedSolids;
            public CountRecord[] counts;
            public int mutableChunks, softProbes, hardColliders, reusedHardColliders, routeGuards;
            public int clearanceRepairVersion, carClearanceRoutes, carClearanceSamples;
            public int smallStoneVersion, smallStoneItems;
            public int smallStoneBoulders, smallStonePebbleClusters;
            public string smallStoneBoundsMethod;
            public StoneBoundsRecord[] smallStoneBoundsCorrections;
            public RelocationRecord[] relocations;
        }
        sealed class Context
        {
            public GameSession session;
            public LevelLayout level;
            public Scene scene;
            public readonly Dictionary<string, MeshFilter> filters = new Dictionary<string, MeshFilter>();
            public readonly Dictionary<string, int[]> indices = new Dictionary<string, int[]>();
            public readonly List<SourceRecord> sources = new List<SourceRecord>();
            public readonly List<ItemRecord> items = new List<ItemRecord>();
            public readonly Dictionary<string, List<TemplateMember>> templates = new Dictionary<string, List<TemplateMember>>();
        }
        sealed class TemplateMember { public string kind; public int[] offsets; }
        sealed class Triangle
        {
            public int offset;
            public Vector3 a, b, c;
            public bool used;
        }
        sealed class PathGuard { public string name; public Vector3 start, end; }

        [MenuItem("Tools/Road Gunner/Scenery/Install Vehicle Crush Collisions Only")]
        public static void InstallBatch()
        {
            RequireEditMode();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before scenery installation.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var context = ContextFor(scene);
            ValidateLayer(false);
            Catalog catalog;
            bool repeat = File.Exists(CatalogPath) && context.level.decorationRoot.Find(OwnedRoot);
            if (repeat)
            {
                catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath));
                if (catalog == null || catalog.schema != Schema || catalog.sceneGuid != AssetDatabase.AssetPathToGUID(ScenePath))
                    throw new InvalidOperationException("Existing scenery catalog is incompatible; refuse to regenerate the saved art.");
                BindSources(context, catalog);
                VerifyCatalog(context, catalog, true);
            }
            else catalog = BuildVerifiedCatalog(context);

            ApplySmallStonePolicy(context, catalog);
            // Every source and every replayed triangle has been checked before any scene/asset mutation.
            var paths = CapturePathGuards(context);
            ApplyCatalog(context, catalog);
            Physics.SyncTransforms();
            ValidateWorkshopClearance(context);
            ValidateCarRouteClearance(context, catalog, true);
            BakeExistingNavigation(context, paths);
            catalog.routeGuards = paths.Count;
            VerifyCatalog(context, catalog, true);
            ValidateInstalled(context, catalog);
            catalog.verifiedAtUtc = DateTime.UtcNow.ToString("O");
            catalog.verification = "PASS: original visual geometry/materials/poses unchanged; all item triples/ranges validated. " +
                (repeat ? "Repeat used the previously replay-verified catalog because collision semantics now differ from the original placement scene." :
                "All Arcade replay positions/normals/UV/indices matched the saved chunks; all Concept template triangles matched original members.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(CatalogPath, JsonUtility.ToJson(catalog, true));
            File.WriteAllText(ReportFolder + "/Authoring.txt", Summary(catalog, repeat));
            Debug.Log("SCENERY_CRUSH_INSTALL_OK\n" + Summary(catalog, repeat));
        }

        /// <summary>Read-only saved-catalog/scene diagnostic. No authoring, asset saving or navigation bake.</summary>
        public static void DumpManifestBatch()
        {
            RequireEditMode();
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Read-only manifest requires a saved scene.");
            var scene = SceneManager.GetActiveScene().path == ScenePath ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(ScenePath);
            var context = ContextFor(scene);
            if (!File.Exists(CatalogPath)) throw new InvalidOperationException("No installed scenery catalog.");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath));
            BindSources(context, catalog); VerifyCatalog(context, catalog, true); ValidateInstalled(context, catalog);
            Debug.Log("SCENERY_CRUSH_MANIFEST_OK\n" + Summary(catalog, true));
        }

        /// <summary>
        /// Approved migration for formerly nonblocking shoulder compositions whose new exact
        /// stone colliders entered the service/Warlord centreline sweep. Moves the complete original
        /// prefab instances and their probes, without deriving or altering any visual/collision mesh.
        /// The unchanged Exploration VehicleClear geometry must pass before anything is saved.
        /// </summary>
        public static void RepairRouteClearanceBatch()
        {
            RequireEditMode();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before the narrow clearance migration.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var context = ContextFor(scene);
            if (!File.Exists(CatalogPath)) throw new InvalidOperationException("Install and verify the scenery catalog before this migration.");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath));
            if (catalog.schema != Schema) throw new InvalidOperationException("Unsupported scenery catalog schema.");
            BindSources(context, catalog); VerifyCatalog(context, catalog, true); ValidateInstalled(context, catalog);
            if (catalog.clearanceRepairVersion >= 1)
            {
                ValidateCarRouteClearance(context, catalog, true);
                Debug.Log("SCENERY_CRUSH_CLEARANCE_ALREADY_REPAIRED\n" + Summary(catalog, true));
                return;
            }
            ValidateCarRouteClearance(context, catalog, false);
            var guards = CapturePathGuards(context);
            var moved = new List<RelocationRecord>();
            try
            {
                MoveShoulder("d26299ad621b05e1dc73", "Main route shoulder · 12", new Vector3(-38.793617f, .012f, -55.444828f), new Vector3(-1.18f, 0, -2.76f));
                MoveShoulder("88c9216d8eb31fd764b4", "Main route shoulder · 27", new Vector3(44.53004f, .012f, 56.85391f), new Vector3(2.92f, 0, -.69f));
                MoveShoulder("30a2efe5dfdb3320a1a5", "Main route shoulder · 22", new Vector3(13.465868f, .012f, 1.671326f), new Vector3(2.99f, 0, -.20f));
                Physics.SyncTransforms();
                // Exact current saved player boxes and original margins; no exclusion for the offending rocks.
                ValidateCarRouteClearance(context, catalog, true);
                ValidateWorkshopClearance(context);
                VerifyCatalog(context, catalog, true); ValidateInstalled(context, catalog);
                BakeExistingNavigation(context, guards);
                catalog.routeGuards = guards.Count;
                catalog.clearanceRepairVersion = 1; catalog.relocations = moved.ToArray();
                catalog.verifiedAtUtc = DateTime.UtcNow.ToString("O");
                catalog.verification += " Approved clearance migration: only three whole shoulder poses changed; all visual/collision-local geometry and materials preserved; exact original car-box centreline checks pass.";
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                File.WriteAllText(CatalogPath, JsonUtility.ToJson(catalog, true));
                File.WriteAllText(ReportFolder + "/Authoring.txt", Summary(catalog, true));
                Debug.Log("SCENERY_CRUSH_CLEARANCE_REPAIR_OK\n" + Summary(catalog, true));
            }
            catch
            {
                // A failed candidate never changes the saved scene, meshes, catalog or original pose provenance.
                foreach (var move in moved)
                {
                    var owner = Resolve<GameObject>(move.ownerId);
                    if (owner) { owner.transform.position = move.oldPosition; Record(owner.transform); }
                    var source = catalog.sources.Single(s => s.id == move.sourceId); source.pose = move.oldPose;
                    SynchronizeProxies(context, catalog, move.sourceId);
                }
                Physics.SyncTransforms(); throw;
            }

            void MoveShoulder(string hardHash, string expectedName, Vector3 expectedOld, Vector3 delta)
            {
                var item = catalog.items.Single(i => ShortHash(i.id) == hardHash && i.category == "Boulder" && i.handling == "hard");
                var filter = context.filters[item.sourceId];
                var owner = PrefabUtility.GetOutermostPrefabInstanceRoot(filter.gameObject);
                if (!owner || owner.name != expectedName || Gameplay(owner.transform) || (owner.transform.position - expectedOld).sqrMagnitude > .0004f)
                    throw new InvalidOperationException("Approved relocation source/old pose differs: " + expectedName);
                var source = catalog.sources.Single(s => s.id == item.sourceId);
                var move = new RelocationRecord { id = "clearance-20261103/" + hardHash, sourceId = source.id, ownerId = Id(owner),
                    oldPose = source.pose, oldPosition = owner.transform.position, delta = delta,
                    reason = "New exact Boulder collision entered the unchanged Exploration full-car centreline sweep; move whole composition to the shoulder, keeping the rock hard." };
                moved.Add(move);
                owner.transform.position += delta; Record(owner.transform);
                move.newPosition = owner.transform.position; move.newPose = source.pose = Pose(filter.transform);
                SynchronizeProxies(context, catalog, source.id);
            }
        }

        static void SynchronizeProxies(Context context, Catalog catalog, string sourceId)
        {
            var root = context.level.decorationRoot.Find(OwnedRoot);
            var filter = context.filters[sourceId];
            foreach (var record in catalog.items.Where(i => i.sourceId == sourceId && (i.handling == "hard" || i.handling == "soft")))
            {
                string folder = record.handling == "hard" ? "Hard exact collision (Default)" : "Soft query probes (layer 12)";
                string name = (record.handling == "hard" ? "Hard " : "Soft ") + ShortHash(record.id);
                var proxy = root.Find(folder + "/" + name);
                if (!proxy) throw new InvalidOperationException("Missing owned proxy during relocation: " + name);
                SetPose(proxy, filter.transform); EditorUtility.SetDirty(proxy);
            }
        }

        static void ValidateCarRouteClearance(Context context, Catalog catalog, bool requireClear)
        {
            var data = ArcadeVisualGround.LoadLayout();
            var root = context.level.authoredContentRoot.Find("Arena exploration/Routes");
            if (!root) throw new InvalidOperationException("Missing saved sampled route markers.");
            var terrain = context.level.ground.GetComponent<Collider>();
            var failures = new List<string>(); int samples = 0;
            foreach (var route in data.routes)
            {
                var node = root.Find(route.id);
                if (!node || node.childCount < 2) throw new InvalidOperationException("Missing original sampled route " + route.id);
                var points = Enumerable.Range(0, node.childCount).Select(i => node.GetChild(i).position).ToArray();
                int clear = 0; string first = "";
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)], Vector3.up).normalized;
                    if (direction.sqrMagnitude < .1f) throw new InvalidOperationException("No saved route tangent " + route.id + "/" + i);
                    if (!terrain.Raycast(new Ray(points[i] + Vector3.up * 100, Vector3.down), out var ground, 250)) throw new InvalidOperationException("No original ground below route " + route.id);
                    if (VehicleClearAt(context, ground.point + Vector3.up * .7f, Quaternion.LookRotation(direction), out string obstacle)) clear++;
                    else
                    {
                        string blocked = "sample=" + i + "; position=" + ground.point.ToString("F4") + "; obstacles=" + obstacle;
                        Debug.Log("SCENERY_CAR_BLOCKED " + route.id + "; " + blocked);
                        if (first == "") first = blocked;
                    }
                }
                samples += points.Length;
                Debug.Log("SCENERY_CAR_ROUTE " + route.id + "=" + clear + "/" + points.Length + (first == "" ? "" : "; " + first));
                if (clear != points.Length) failures.Add(route.id + " " + clear + "/" + points.Length + " (" + first + ")");
            }
            if (requireClear && failures.Count != 0) throw new InvalidOperationException("Exact unchanged car route clearance failed; no scene save: " + string.Join("; ", failures));
            if (failures.Count == 0) { catalog.carClearanceRoutes = data.routes.Length; catalog.carClearanceSamples = samples; }
        }

        // Kept equivalent to ExplorationPlayChecks.VehicleClear, including vertical ground rejection.
        static bool VehicleClearAt(Context context, Vector3 root, Quaternion rotation, out string obstacle)
        {
            var car = context.session.vehicle;
            var obstructions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chassis in car.GetComponents<BoxCollider>())
            {
                Vector3 half = Vector3.Scale(chassis.size * .5f, car.transform.lossyScale); half.x += .2f; half.z += .15f;
                Vector3 centre = root + rotation * Vector3.Scale(chassis.center, car.transform.lossyScale);
                foreach (var hit in Physics.OverlapBox(centre, half, rotation, 1, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(car.transform) || hit.transform == context.level.ground.transform || hit.GetComponentInParent<CombatTarget>()) continue;
                    if (hit.bounds.max.y <= centre.y - half.y + .04f) continue;
                    obstructions.Add(Hierarchy(hit.transform));
                }
            }
            obstacle = string.Join(" | ", obstructions.OrderBy(s => s, StringComparer.Ordinal));
            return obstructions.Count == 0;
        }

        static void RequireEditMode()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Run scenery authoring from compiled Edit Mode.");
        }
        static Context ContextFor(Scene scene)
        {
            var sessions = InScene<GameSession>(scene).ToArray();
            var levels = InScene<LevelLayout>(scene).Where(l => l.decorationRoot && l.ground).ToArray();
            if (sessions.Length != 1 || levels.Length != 1) throw new InvalidOperationException("Expected one saved player session and one editable level.");
            return new Context { session = sessions[0], level = levels[0], scene = scene };
        }
        static IEnumerable<T> InScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static string Id(Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static T Resolve<T>(string id) where T : Object => GlobalObjectId.TryParse(id, out var parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as T : null;
        static string Hierarchy(Transform t) => t.parent ? Hierarchy(t.parent) + "/" + t.name : t.name;
        static bool Gameplay(Transform t) =>
            t.GetComponentInParent<CombatTarget>() || t.GetComponentInChildren<CombatTarget>(true) ||
            t.GetComponentInParent<VehicleController>() || t.GetComponentInChildren<VehicleController>(true) ||
            t.GetComponentInParent<EnemyBuggy>() || t.GetComponentInChildren<EnemyBuggy>(true) ||
            t.GetComponentInParent<RepairWorkshop>() || t.GetComponentInChildren<RepairWorkshop>(true) ||
            t.GetComponentInParent<ArenaUpgradeStation>() || t.GetComponentInChildren<ArenaUpgradeStation>(true) ||
            t.GetComponentInParent<PickupPool>() || t.GetComponentInChildren<PickupPool>(true);

        static Catalog BuildVerifiedCatalog(Context context)
        {
            var catalog = new Catalog { sceneGuid = AssetDatabase.AssetPathToGUID(ScenePath) };
            AddArcade(context, catalog);
            AddShoulders(context, catalog);
            AddStandalone(context);
            catalog.sources = context.sources.OrderBy(s => s.id, StringComparer.Ordinal).ToArray();
            catalog.items = context.items.OrderBy(s => s.id, StringComparer.Ordinal).ToArray();
            if (catalog.items.Select(i => i.id).Distinct().Count() != catalog.items.Length) throw new InvalidOperationException("Duplicate scenery item ID.");
            var replaced = new HashSet<Collider>();
            foreach (var record in catalog.items.Where(i => i.handling == "soft" && !string.IsNullOrEmpty(i.ownerId)))
            {
                var owner = Resolve<GameObject>(record.ownerId);
                if (!owner || Gameplay(owner.transform)) throw new InvalidOperationException("Invalid soft scenery owner " + record.id);
                foreach (var collider in owner.GetComponentsInChildren<Collider>(true)) if (collider.enabled && !collider.isTrigger) replaced.Add(collider);
            }
            catalog.replacedSoftColliders = replaced.Select(Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            catalog.protectedSolids = InScene<Collider>(context.scene).Where(c => c.enabled && !c.isTrigger && !replaced.Contains(c) && !Gameplay(c.transform))
                .Select(c => new SolidRecord { id = Id(c), signature = ColliderSignature(c) }).ToArray();
            catalog.counts = catalog.items.GroupBy(i => i.category).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new CountRecord {
                category = g.Key, soft = g.Count(i => i.handling == "soft"), hard = g.Count(i => i.handling == "hard"),
                surface = g.Count(i => i.handling == "surface"), composite = g.Count(i => i.handling == "composite") }).ToArray();
            VerifyCatalog(context, catalog, false);
            return catalog;
        }

        static SourceRecord AddSource(Context context, MeshFilter filter)
        {
            if (!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable || Gameplay(filter.transform))
                throw new InvalidOperationException("Scenery source must be an original readable, non-gameplay mesh.");
            string id = Id(filter);
            if (context.filters.ContainsKey(id)) return context.sources.Single(s => s.id == id);
            var mesh = filter.sharedMesh;
            int[] indices = Flatten(mesh);
            var record = new SourceRecord { id = id, hierarchy = Hierarchy(filter.transform), meshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh)),
                geometryHash = MeshHash(mesh), materials = Materials(filter), pose = Pose(filter.transform), vertices = mesh.vertexCount, indices = indices.Length };
            context.filters.Add(id, filter); context.indices.Add(id, indices); context.sources.Add(record);
            return record;
        }

        static void AddArcade(Context context, Catalog catalog)
        {
            var root = context.level.decorationRoot.Find(ArcadeVisualDressing.RootName);
            if (!root) throw new InvalidOperationException("Missing original Arcade dressing.");
            var replay = ArcadeVisualDressing.ReplayRecords(context.session);
            var saved = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToDictionary(f => f.sharedMesh.name, StringComparer.Ordinal);
            if (saved.Count != replay.chunks.Length) throw new InvalidOperationException("Arcade replay chunk count differs from the saved art.");
            catalog.arcadePrimaryItems = replay.primaryItems; catalog.arcadeChunks = replay.chunks.Length;
            foreach (var chunk in replay.chunks)
            {
                string name = "Arcade dressing " + chunk.key.x + "_" + chunk.key.y;
                if (!saved.TryGetValue(name, out var filter)) throw new InvalidOperationException("Replay chunk missing: " + name);
                var source = AddSource(context, filter); var mesh = filter.sharedMesh;
                var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var indices = context.indices[source.id];
                if (v.Length != chunk.vertices.Length || n.Length != chunk.normals.Length || uv.Length != chunk.uvs.Length || !indices.SequenceEqual(chunk.triangles))
                    throw new InvalidOperationException("Arcade replay topology mismatch in " + name + ": saved=" + v.Length + ", replay=" + chunk.vertices.Length);
                for (int i = 0; i < v.Length; i++)
                    if ((v[i] - filter.transform.InverseTransformPoint(chunk.vertices[i])).sqrMagnitude > VertexTolerance * VertexTolerance ||
                        (n[i] - filter.transform.worldToLocalMatrix.MultiplyVector(chunk.normals[i]).normalized).sqrMagnitude > VertexTolerance * VertexTolerance ||
                        (uv[i] - chunk.uvs[i]).sqrMagnitude > 1e-10f)
                        throw new InvalidOperationException("Arcade replay vertex/normal/UV mismatch " + name + " vertex " + i);
                catalog.arcadeVertices += v.Length;
                foreach (var item in chunk.items)
                {
                    var record = Record(context, source.id, item.id, item.category, new[] { item.start }, new[] { item.count });
                    if (item.category == "Barrels") record.handling = "composite"; // each barrel has its own nested range
                    context.items.Add(record);
                }
            }
            if (catalog.arcadePrimaryItems != 1067 || catalog.arcadeChunks != 29 || catalog.arcadeVertices <= 0)
                throw new InvalidOperationException("Saved Arcade revision differs from the inspected 1067/29 baseline; review before authoring.");
        }

        static void AddShoulders(Context context, Catalog catalog)
        {
            var root = context.level.decorationRoot.Find("Concept route-shoulder composition");
            if (!root) throw new InvalidOperationException("Missing saved mixed Concept shoulder compositions.");
            foreach (Transform instance in root)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("Shoulder composition ", StringComparison.Ordinal) || !int.TryParse(name.Substring("Shoulder composition ".Length), out int type))
                    throw new InvalidOperationException("Unrecognized saved shoulder template: " + Hierarchy(instance));
                var filters = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled).ToArray();
                if (filters.Length != 1) throw new InvalidOperationException("Expected one original baked shoulder renderer: " + Hierarchy(instance));
                var filter = filters[0]; var source = AddSource(context, filter);
                string key = source.meshGuid + "/" + type;
                if (!context.templates.TryGetValue(key, out var members))
                {
                    members = MatchShoulderTemplate(filter, instance, type); context.templates.Add(key, members);
                }
                for (int i = 0; i < members.Count; i++)
                {
                    Compress(members[i].offsets, out int[] starts, out int[] counts);
                    context.items.Add(Record(context, source.id, "shoulder/" + Id(instance.gameObject) + "/" + i + "-" + members[i].kind, members[i].kind, starts, counts));
                    catalog.shoulderMembers++;
                }
                catalog.shoulderInstances++;
            }
            if (catalog.shoulderInstances != 79 || catalog.shoulderMembers != 354)
                throw new InvalidOperationException("Saved mixed shoulder revision differs from the inspected 79 instances/354 members.");
        }

        static List<TemplateMember> MatchShoulderTemplate(MeshFilter target, Transform root, int type)
        {
            var vertices = target.sharedMesh.vertices; var indices = Flatten(target.sharedMesh);
            var triangles = new List<Triangle>(); var byKey = new Dictionary<string, List<Triangle>>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var t = new Triangle { offset = i, a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]] };
                triangles.Add(t); string key = TriangleKey(t.a, t.b, t.c);
                if (!byKey.TryGetValue(key, out var list)) byKey.Add(key, list = new List<Triangle>());
                list.Add(t);
            }
            var result = new List<TemplateMember>();
            foreach (var member in Shoulder.ReplayMembers(type))
            {
                var source = Shoulder.ReplaySource(member.kind);
                var offsets = new List<int>();
                var memberMatrix = target.transform.worldToLocalMatrix * root.localToWorldMatrix *
                    Matrix4x4.TRS(member.position, Quaternion.Euler(0, member.yaw, 0), Vector3.one * member.scale);
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()))
                {
                    var matrix = memberMatrix * source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    var v = filter.sharedMesh.vertices; var ix = Flatten(filter.sharedMesh);
                    for (int i = 0; i < ix.Length; i += 3)
                    {
                        Vector3 a = matrix.MultiplyPoint3x4(v[ix[i]]), b = matrix.MultiplyPoint3x4(v[ix[i + 1]]), c = matrix.MultiplyPoint3x4(v[ix[i + 2]]);
                        string key = TriangleKey(a, b, c);
                        byKey.TryGetValue(key, out var candidates);
                        var found = candidates?.FirstOrDefault(t => !t.used && SameTriangle(t, a, b, c));
                        // Float CombineMeshes rounding may put a corner on the other side of a hash cell.
                        if (found == null) found = triangles.FirstOrDefault(t => !t.used && SameTriangle(t, a, b, c));
                        if (found == null) throw new InvalidOperationException("Shoulder triangle provenance mismatch: type=" + type + ", member=" + member.kind +
                            ", source=" + filter.sharedMesh.name + ", triangle=" + i / 3 + ", center=" + ((a + b + c) / 3).ToString("F5"));
                        found.used = true; offsets.Add(found.offset);
                    }
                }
                if (offsets.Count == 0) throw new InvalidOperationException("Empty shoulder member " + member.kind);
                result.Add(new TemplateMember { kind = member.kind, offsets = offsets.ToArray() });
            }
            int missing = triangles.Count(t => !t.used);
            if (missing != 0) throw new InvalidOperationException("Shoulder template has " + missing + " triangles without an original member owner (type " + type + ").");
            return result;
        }
        static bool SameTriangle(Triangle t, Vector3 a, Vector3 b, Vector3 c) =>
            Near(t.a, a, b, c) && Near(t.b, a, b, c) && Near(t.c, a, b, c) &&
            Near(a, t.a, t.b, t.c) && Near(b, t.a, t.b, t.c) && Near(c, t.a, t.b, t.c);
        static bool Near(Vector3 p, Vector3 a, Vector3 b, Vector3 c) => Mathf.Min((p - a).sqrMagnitude, (p - b).sqrMagnitude, (p - c).sqrMagnitude) <= MatchTolerance * MatchTolerance;
        static string TriangleKey(Vector3 a, Vector3 b, Vector3 c)
        {
            string Point(Vector3 p) => Mathf.RoundToInt(p.x * 1000) + "," + Mathf.RoundToInt(p.y * 1000) + "," + Mathf.RoundToInt(p.z * 1000);
            var keys = new[] { Point(a), Point(b), Point(c) }; Array.Sort(keys, StringComparer.Ordinal); return string.Join("/", keys);
        }

        static void AddStandalone(Context context)
        {
            var roots = InScene<Transform>(context.scene).Select(t => PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject)).Where(g => g).Distinct().ToArray();
            foreach (var root in roots)
            {
                if (!root.activeInHierarchy || Gameplay(root.transform)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root), kind = Path.GetFileNameWithoutExtension(path);
                bool concept = path.StartsWith("Assets/RoadGunner/Art/ConceptWasteland/Prefabs/", StringComparison.Ordinal) &&
                    new[] { "Cactus", "Wooden supply crate", "Boulder" }.Contains(kind);
                bool legacy = path.StartsWith("Assets/RoadGunner/Levels/WastelandRestored/Prefabs/", StringComparison.Ordinal) &&
                    new[] { "Dry scrub", "Tire and scrap pile", "Tire stack", "Fuel drums", "Metal storage crate" }.Contains(kind);
                bool oldCactus = path == "Assets/RoadGunner/Levels/Greenwood/Prefabs/Cactus.prefab";
                if (!concept && !legacy && !oldCactus) continue;
                // Never sweep arbitrary names such as a reward crate, player ammunition or a workshop barrel.
                var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled && f.gameObject.activeInHierarchy).ToArray();
                if (filters.Length != 1) throw new InvalidOperationException("Expected one current standalone scenery mesh; found " + filters.Length + " at " + Hierarchy(root.transform));
                var filter = filters[0]; var source = AddSource(context, filter);
                string hierarchy = Hierarchy(root.transform);
                string family = hierarchy.Contains("Arena exploration") ? "exploration" : hierarchy.Contains("Concept roadside accents") ? "concept-roadside" : "legacy";
                var record = Record(context, source.id, "standalone/" + family + "/" + Id(root) + "/" + kind, kind, new[] { 0 }, new[] { source.indices });
                record.ownerId = Id(root); context.items.Add(record);
            }
        }

        static ItemRecord Record(Context context, string sourceId, string id, string category, int[] starts, int[] counts)
        {
            var filter = context.filters[sourceId]; var v = filter.sharedMesh.vertices; var ix = context.indices[sourceId];
            bool first = true; Bounds bounds = default;
            for (int r = 0; r < starts.Length; r++)
                for (int i = starts[r]; i < starts[r] + counts[r]; i++)
                { if (first) { bounds = new Bounds(v[ix[i]], Vector3.zero); first = false; } else bounds.Encapsulate(v[ix[i]]); }
            var record = new ItemRecord { id = id, category = category, sourceId = sourceId, starts = starts, counts = counts, localBounds = bounds, handling = "soft", kind = DecorationCrushKind.Metal };
            switch (category)
            {
                case "Tuft": case "Dry scrub": case "Dune": case "Drift": record.handling = "surface"; break;
                case "Pebbles": record.kind = DecorationCrushKind.Stone; break;
                case "Spire": case "Hull": case "Girder": record.handling = "hard"; break;
                case "Boulder":
                    Bounds world = ClassificationWorldBounds(context, record);
                    bool small = IsSmallBoulder(world);
                    record.handling = small ? "soft" : "hard";
                    if (small) record.kind = DecorationCrushKind.Stone;
                    break;
                case "Cactus": case "DeadTree": record.kind = DecorationCrushKind.Vegetation; break;
                case "Wooden supply crate": record.kind = DecorationCrushKind.Wood; break;
                case "Tyre": case "TyreStack": case "Tire stack": record.kind = DecorationCrushKind.Rubber; break;
                case "Bones": record.kind = DecorationCrushKind.Bone; break;
            }
            return record;
        }

        static bool IsSmallBoulder(Bounds world) => world.size.y <= .6f && Mathf.Max(world.size.x, world.size.z) <= 1f;

        // Rotating a local AABB encloses empty corners and can make an .8m stone appear >1m.
        // Classify the actual preserved mesh triangles; localBounds still serves probes and FX.
        static Bounds ClassificationWorldBounds(Context context, ItemRecord item)
        {
            var filter = context.filters[item.sourceId];
            var vertices = filter.sharedMesh.vertices; var indices = context.indices[item.sourceId];
            var matrix = filter.transform.localToWorldMatrix;
            if (item.starts == null || item.counts == null || item.starts.Length != item.counts.Length)
                throw new InvalidOperationException("Invalid stone classification ranges: " + item.id);
            bool first = true; Bounds result = default;
            for (int range = 0; range < item.starts.Length; range++)
            {
                int start = item.starts[range], count = item.counts[range];
                if (start < 0 || start % 3 != 0 || count <= 0 || count % 3 != 0 || (long)start + count > indices.Length)
                    throw new InvalidOperationException("Out-of-range stone classification triangles: " + item.id);
                for (int i = start; i < start + count; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Empty stone classification geometry: " + item.id);
            return result;
        }

        static void ApplySmallStonePolicy(Context context, Catalog catalog)
        {
            // Migrate the verified triangle ranges, never regenerate the saved dressing or move its art.
            // The explicit category/size whitelist cannot turn cliffs, buildings or large boulders into props.
            var stones = catalog.items.Where(item => item.category == "Pebbles" ||
                item.category == "Boulder" && IsSmallBoulder(ClassificationWorldBounds(context, item))).ToArray();
            if (stones.Length == 0) throw new InvalidOperationException("No verified small stone sources in the saved arena.");
            foreach (var item in stones)
            {
                var owner = string.IsNullOrEmpty(item.ownerId) ? null : Resolve<GameObject>(item.ownerId);
                if (owner && owner.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
                    throw new InvalidOperationException("Small stone migration would replace an original protected collider: " + item.id);
                item.handling = "soft"; item.kind = DecorationCrushKind.Stone;
            }
            catalog.smallStoneVersion = 2; catalog.smallStoneItems = stones.Length;
            catalog.smallStoneBoulders = stones.Count(item => item.category == "Boulder");
            catalog.smallStonePebbleClusters = stones.Count(item => item.category == "Pebbles");
            catalog.smallStoneBoundsMethod = "Exact selected triangle vertices transformed by the verified source localToWorld matrix; ranges/source IDs unchanged; height<=0.6m and max(world X,Z)<=1m.";
            // Persist the old-box false positives on every repeat, so the migration remains
            // reviewable after the records already have soft/Stone handling.
            catalog.smallStoneBoundsCorrections = stones.Where(item => item.category == "Boulder" &&
                !IsSmallBoulder(TransformBounds(context.filters[item.sourceId].transform.localToWorldMatrix, item.localBounds)))
                .OrderBy(item => item.id, StringComparer.Ordinal).Select(item => {
                    var exact = ClassificationWorldBounds(context, item);
                    return new StoneBoundsRecord { id = item.id, sourceId = item.sourceId, worldCenter = exact.center,
                        exactWorldSize = exact.size, previousTransformedBoxSize = TransformBounds(context.filters[item.sourceId].transform.localToWorldMatrix, item.localBounds).size };
                }).ToArray();
            catalog.counts = catalog.items.GroupBy(i => i.category).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new CountRecord {
                category = g.Key, soft = g.Count(i => i.handling == "soft"), hard = g.Count(i => i.handling == "hard"),
                surface = g.Count(i => i.handling == "surface"), composite = g.Count(i => i.handling == "composite") }).ToArray();
        }

        static void BindSources(Context context, Catalog catalog)
        {
            foreach (var source in catalog.sources)
            {
                var filter = Resolve<MeshFilter>(source.id);
                if (!filter) throw new InvalidOperationException("Catalog source no longer exists: " + source.hierarchy);
                context.filters.Add(source.id, filter); context.indices.Add(source.id, Flatten(filter.sharedMesh));
            }
        }
        static void VerifyCatalog(Context context, Catalog catalog, bool ignoreReplaced)
        {
            foreach (var source in catalog.sources)
            {
                var f = context.filters[source.id];
                if (!f || !f.sharedMesh || Gameplay(f.transform) || MeshHash(f.sharedMesh) != source.geometryHash || Materials(f) != source.materials || Pose(f.transform) != source.pose)
                    throw new InvalidOperationException("Original scenery geometry/material/pose changed: " + source.hierarchy);
            }
            var used = new Dictionary<string, HashSet<int>>();
            foreach (var item in catalog.items)
            {
                if (!context.indices.TryGetValue(item.sourceId, out var indices) || item.starts == null || item.counts == null || item.starts.Length == 0 || item.starts.Length != item.counts.Length)
                    throw new InvalidOperationException("Invalid item source/ranges: " + item.id);
                if (!used.TryGetValue(item.sourceId, out var occupied)) used.Add(item.sourceId, occupied = new HashSet<int>());
                for (int r = 0; r < item.starts.Length; r++)
                {
                    int start = item.starts[r], count = item.counts[r];
                    if (start < 0 || start % 3 != 0 || count <= 0 || count % 3 != 0 || (long)start + count > indices.Length)
                        throw new InvalidOperationException("Out-of-range triangle triples: " + item.id);
                    if (item.handling == "composite") continue;
                    for (int i = start; i < start + count; i += 3)
                        if (!occupied.Add(i)) throw new InvalidOperationException("Two scenery items own the same triangle: " + item.id);
                }
            }
            foreach (var solid in catalog.protectedSolids)
            {
                var collider = Resolve<Collider>(solid.id);
                if (!collider || ColliderSignature(collider) != solid.signature) throw new InvalidOperationException("Existing solid changed: " + solid.id);
            }
            if (!ignoreReplaced)
                foreach (string id in catalog.replacedSoftColliders) if (!Resolve<Collider>(id)) throw new InvalidOperationException("Missing original soft collider " + id);
        }

        static void ApplyCatalog(Context context, Catalog catalog)
        {
            ValidateLayer(true); EnsureFolder(AssetFolder);
            var root = Child(context.level.decorationRoot, OwnedRoot);
            var softRoot = Child(root, "Soft query probes (layer 12)");
            var hardRoot = Child(root, "Hard exact collision (Default)");
            catalog.reusedHardColliders = 0;
            var wantedSoft = new HashSet<string>(); var wantedHard = new HashSet<string>();
            var chunks = new List<DecorationCrushChunk>();
            foreach (var group in catalog.items.Where(i => i.handling == "soft").GroupBy(i => i.sourceId).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var filter = context.filters[group.Key];
                var chunk = filter.GetComponent<DecorationCrushChunk>(); if (!chunk) chunk = Undo.AddComponent<DecorationCrushChunk>(filter.gameObject);
                chunk.source = filter;
                var items = new List<DecorationCrushChunk.Item>();
                foreach (var record in group.OrderBy(i => i.id, StringComparer.Ordinal))
                {
                    string name = "Soft " + ShortHash(record.id); wantedSoft.Add(name);
                    var probe = Child(softRoot, name); SetPose(probe, filter.transform);
                    probe.gameObject.layer = ProbeLayer;
                    var box = probe.GetComponent<BoxCollider>(); if (!box) box = Undo.AddComponent<BoxCollider>(probe.gameObject);
                    box.isTrigger = true; box.enabled = true; box.center = record.localBounds.center;
                    Vector3 size = record.localBounds.size;
                    box.size = new Vector3(Mathf.Max(.06f, size.x), Mathf.Max(.08f, size.y), Mathf.Max(.06f, size.z));
                    items.Add(new DecorationCrushChunk.Item { id = record.id, kind = record.kind, probe = box,
                        rangeStarts = record.starts, rangeCounts = record.counts, localBounds = record.localBounds });
                    EditorUtility.SetDirty(box);
                }
                chunk.items = items.ToArray(); chunks.Add(chunk); EditorUtility.SetDirty(chunk);
                var flags = GameObjectUtility.GetStaticEditorFlags(filter.gameObject);
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
            }
            foreach (var record in catalog.items.Where(i => i.handling == "hard"))
            {
                var filter = context.filters[record.sourceId];
                // Regional boulders can already have a real solid collider; keep it instead of adding a duplicate.
                var owner = string.IsNullOrEmpty(record.ownerId) ? null : Resolve<GameObject>(record.ownerId);
                if (owner && owner.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger)) { catalog.reusedHardColliders++; continue; }
                string name = "Hard " + ShortHash(record.id); wantedHard.Add(name);
                var proxy = Child(hardRoot, name); SetPose(proxy, filter.transform); proxy.gameObject.layer = 0;
                var collider = proxy.GetComponent<MeshCollider>(); if (!collider) collider = Undo.AddComponent<MeshCollider>(proxy.gameObject);
                collider.sharedMesh = HardMesh(context, record); collider.convex = false; collider.isTrigger = false; collider.enabled = true;
                GameObjectUtility.SetStaticEditorFlags(proxy.gameObject, StaticEditorFlags.OccludeeStatic);
                EditorUtility.SetDirty(collider);
            }
            foreach (string id in catalog.replacedSoftColliders)
            {
                var collider = Resolve<Collider>(id);
                if (!collider || Gameplay(collider.transform)) throw new InvalidOperationException("Refuse to replace a gameplay collider.");
                collider.enabled = false; Record(collider);
            }
            RemoveStale(softRoot, wantedSoft); RemoveStale(hardRoot, wantedHard);
            var manager = root.GetComponent<ArenaDecorationCrush>(); if (!manager) manager = Undo.AddComponent<ArenaDecorationCrush>(root.gameObject);
            manager.session = context.session; manager.chunks = chunks.ToArray(); manager.probeMask = 1 << ProbeLayer;
            manager.minimumImpactSpeed = .25f; manager.debrisMaterial = DebrisMaterial();
            EditorUtility.SetDirty(manager);
            foreach (var turret in InScene<TurretController>(context.scene)) if ((turret.hitMask.value & (1 << ProbeLayer)) != 0) { turret.hitMask &= ~(1 << ProbeLayer); Record(turret); }
            foreach (var gun in InScene<BuggyGun>(context.scene)) if ((gun.hitMask.value & (1 << ProbeLayer)) != 0) { gun.hitMask &= ~(1 << ProbeLayer); Record(gun); }
            catalog.mutableChunks = chunks.Count; catalog.softProbes = wantedSoft.Count; catalog.hardColliders = wantedHard.Count;
        }

        static Mesh HardMesh(Context context, ItemRecord record)
        {
            var source = context.filters[record.sourceId].sharedMesh;
            string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) + "/" + string.Join(",", record.starts) + "/" + string.Join(",", record.counts);
            string path = AssetFolder + "/Hard " + ShortHash(key) + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (saved) return saved;
            var original = source.vertices; var indices = context.indices[record.sourceId];
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var map = new Dictionary<int, int>();
            for (int r = 0; r < record.starts.Length; r++) for (int i = record.starts[r]; i < record.starts[r] + record.counts[r]; i++)
            {
                int index = indices[i];
                if (!map.TryGetValue(index, out int local)) { local = vertices.Count; map.Add(index, local); vertices.Add(original[index]); }
                triangles.Add(local);
            }
            var mesh = new Mesh { name = "Scenery hard collision " + ShortHash(key), indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
        static Material DebrisMaterial()
        {
            string path = AssetFolder + "/Scenery debris white.mat";
            var saved = AssetDatabase.LoadAssetAtPath<Material>(path); if (saved) return saved;
            var shader = Shader.Find("RoadGunner/Arcade Toon");
            if (!shader) throw new InvalidOperationException("Missing saved Arcade Toon shader.");
            saved = new Material(shader) { name = "Scenery debris white", enableInstancing = true };
            saved.SetColor("_BaseColor", Color.white); saved.SetFloat("_Metallic", .12f); saved.SetFloat("_Smoothness", .18f);
            AssetDatabase.CreateAsset(saved, path); return saved;
        }

        static void ValidateInstalled(Context context, Catalog catalog)
        {
            var root = context.level.decorationRoot.Find(OwnedRoot);
            var manager = root ? root.GetComponent<ArenaDecorationCrush>() : null;
            if (!manager || manager.session != context.session || manager.chunks == null) throw new InvalidOperationException("Missing scenery runtime wiring.");
            var expected = new HashSet<string>(catalog.items.Where(i => i.handling == "soft").Select(i => i.id));
            foreach (var chunk in manager.chunks)
            {
                if (!chunk || !chunk.source || Gameplay(chunk.transform) || (GameObjectUtility.GetStaticEditorFlags(chunk.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                    throw new InvalidOperationException("Mutable scenery must retain its original source and be excluded from static batching.");
                foreach (var item in chunk.items)
                    if (!expected.Remove(item.id) || !item.probe || !item.probe.enabled || !item.probe.isTrigger || item.probe.gameObject.layer != ProbeLayer)
                        throw new InvalidOperationException("Invalid/duplicate scenery query probe " + item.id);
            }
            if (expected.Count != 0) throw new InvalidOperationException("Missing scenery probes: " + string.Join(", ", expected.Take(10)));
            var hard = root.Find("Hard exact collision (Default)").GetComponentsInChildren<MeshCollider>(true);
            if (hard.Any(c => !c.enabled || c.isTrigger || c.gameObject.layer != 0 || !c.sharedMesh || c.sharedMesh.vertexCount == 0))
                throw new InvalidOperationException("Invalid exact hard scenery collision.");
            if (hard.Length != catalog.hardColliders) throw new InvalidOperationException("Hard scenery collision count differs from the catalog.");
        }

        static List<PathGuard> CapturePathGuards(Context context)
        {
            var data = ArcadeVisualGround.LoadLayout(); var result = new List<PathGuard>();
            foreach (var route in data.routes)
            {
                Vector3 start = ExplorationArenaInstaller.World(route.nodesW[0]), end = ExplorationArenaInstaller.World(route.nodesW[route.nodesW.Length - 1]);
                if (!CompletePath(start, end)) throw new InvalidOperationException("Baseline NavMesh path is incomplete: " + route.id);
                result.Add(new PathGuard { name = route.id, start = start, end = end });
            }
            return result;
        }
        static bool CompletePath(Vector3 start, Vector3 end)
        {
            return NavMesh.SamplePosition(start, out var a, 8, NavMesh.AllAreas) && NavMesh.SamplePosition(end, out var b, 8, NavMesh.AllAreas) &&
                PathComplete(a.position, b.position);
        }
        static bool PathComplete(Vector3 a, Vector3 b)
        { var path = new NavMeshPath(); return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete; }

        static void BakeExistingNavigation(Context context, List<PathGuard> guards)
        {
            var surfaces = InScene<NavMeshSurface>(context.scene).ToArray();
            if (surfaces.Length != 1) throw new InvalidOperationException("Expected one existing navigation surface; refuse to author new navigation configuration.");
            var surface = surfaces[0]; var old = surface.navMeshData;
            if (!old || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(old)) || surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders || (surface.layerMask.value & 1) == 0)
                throw new InvalidOperationException("Existing saved collider-based navigation data/configuration is required.");
            surface.RemoveData(); surface.BuildNavMesh(); var fresh = surface.navMeshData;
            try
            {
                if (!fresh) throw new InvalidOperationException("Targeted navigation bake returned no data.");
                var failed = guards.Where(g => !CompletePath(g.start, g.end)).Select(g => g.name).ToArray();
                if (failed.Length != 0) throw new InvalidOperationException("New hard scenery blocks existing complete routes: " + string.Join(", ", failed));
                EditorUtility.CopySerialized(fresh, old); surface.RemoveData(); surface.navMeshData = old; surface.AddData();
                EditorUtility.SetDirty(old); EditorUtility.SetDirty(surface);
                context.level.navigationNeedsBake = false; EditorUtility.SetDirty(context.level);
            }
            catch
            { surface.RemoveData(); surface.navMeshData = old; surface.AddData(); throw; }
            finally { if (fresh && fresh != old) Object.DestroyImmediate(fresh); }
        }

        static void ValidateWorkshopClearance(Context context)
        {
            var own = context.level.decorationRoot.Find(OwnedRoot).Find("Hard exact collision (Default)");
            var hard = new HashSet<Collider>(own.GetComponentsInChildren<Collider>(true));
            foreach (var workshop in InScene<RepairWorkshop>(context.scene))
            {
                Vector3 center = workshop.transform.position + Vector3.up * .75f;
                foreach (var collider in Physics.OverlapBox(center, new Vector3(1.45f, .6f, workshop.healingRadius + 2), workshop.transform.rotation, 1, QueryTriggerInteraction.Ignore))
                    if (hard.Contains(collider)) throw new InvalidOperationException("New hard scenery enters the real workshop driving opening: " + collider.name);
            }
        }

        static void ValidateLayer(bool apply)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").First();
            var serialized = new SerializedObject(asset); var layers = serialized.FindProperty("layers"); var field = layers.GetArrayElementAtIndex(ProbeLayer);
            if (!string.IsNullOrEmpty(field.stringValue) && field.stringValue != ProbeLayerName) throw new InvalidOperationException("Layer 12 is already owned by " + field.stringValue);
            if (apply && field.stringValue != ProbeLayerName) { field.stringValue = ProbeLayerName; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        }
        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name); if (child) return child;
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static void SetPose(Transform target, Transform source)
        { target.SetPositionAndRotation(source.position, source.rotation); target.localScale = Vector3.Scale(source.lossyScale, new Vector3(1 / target.parent.lossyScale.x, 1 / target.parent.lossyScale.y, 1 / target.parent.lossyScale.z)); }
        static void RemoveStale(Transform root, HashSet<string> expected)
        { foreach (Transform child in root.Cast<Transform>().ToArray()) if (!expected.Contains(child.name)) { if (Gameplay(child)) throw new InvalidOperationException("Gameplay object under owned scenery proxy root."); Object.DestroyImmediate(child.gameObject); } }
        static void Record(Object value)
        { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
        static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1)); }
        static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
            return result;
        }
        static int[] Flatten(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable) throw new InvalidOperationException("Scenery mesh must remain readable.");
            var indices = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            { if (mesh.GetTopology(s) != MeshTopology.Triangles) throw new InvalidOperationException("Scenery must use triangle topology."); indices.AddRange(mesh.GetIndices(s)); }
            return indices.ToArray();
        }
        static void Compress(int[] offsets, out int[] starts, out int[] counts)
        {
            var ordered = offsets.Distinct().OrderBy(i => i).ToArray(); var s = new List<int>(); var c = new List<int>();
            foreach (int offset in ordered)
            { if (s.Count > 0 && s[s.Count - 1] + c[c.Count - 1] == offset) c[c.Count - 1] += 3; else { s.Add(offset); c.Add(3); } }
            starts = s.ToArray(); counts = c.ToArray();
        }
        static string Pose(Transform transform)
        {
            var matrix = transform.localToWorldMatrix;
            return string.Join(",", Enumerable.Range(0, 16).Select(i => matrix[i].ToString("R", CultureInfo.InvariantCulture)));
        }
        static string Materials(MeshFilter filter) => string.Join("/", filter.GetComponent<MeshRenderer>().sharedMaterials.Select(m => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))));
        static string ColliderSignature(Collider collider)
        {
            string data = collider.GetType().Name + "/" + collider.enabled + "/" + collider.isTrigger + "/" + collider.gameObject.layer + "/" + Pose(collider.transform);
            if (collider is BoxCollider box) data += "/" + box.center.ToString("F6") + box.size.ToString("F6");
            if (collider is CapsuleCollider capsule) data += "/" + capsule.center.ToString("F6") + capsule.radius.ToString("R") + "/" + capsule.height.ToString("R") + "/" + capsule.direction;
            if (collider is SphereCollider sphere) data += "/" + sphere.center.ToString("F6") + sphere.radius.ToString("R");
            if (collider is MeshCollider mesh) data += "/" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh.sharedMesh)) + "/" + mesh.convex;
            return data;
        }
        static string MeshHash(Mesh mesh)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(mesh.vertexCount);
                foreach (var v in mesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                foreach (var n in mesh.normals) { writer.Write(n.x); writer.Write(n.y); writer.Write(n.z); }
                foreach (var uv in mesh.uv) { writer.Write(uv.x); writer.Write(uv.y); }
                writer.Write(mesh.subMeshCount);
                for (int s = 0; s < mesh.subMeshCount; s++) { writer.Write((int)mesh.GetTopology(s)); foreach (int index in mesh.GetIndices(s)) writer.Write(index); }
                writer.Flush(); return Hash(stream.ToArray());
            }
        }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static string ShortHash(string text) => Hash(Encoding.UTF8.GetBytes(text)).Substring(0, 20);
        static string Summary(Catalog c, bool repeat)
        {
            var text = new StringBuilder("Saved-scene vehicle scenery collision authoring\n");
            text.AppendLine("Verified catalog schema=" + c.schema + "; repeat=" + repeat + "; scene=" + ScenePath);
            text.AppendLine("Arcade original primary items=" + c.arcadePrimaryItems + "; chunks=" + c.arcadeChunks + "; unchanged vertices=" + c.arcadeVertices);
            text.AppendLine("Concept mixed instances=" + c.shoulderInstances + "; recovered members=" + c.shoulderMembers);
            text.AppendLine("Mutable original sources=" + c.mutableChunks + "; soft query probes=" + c.softProbes + "; compact exact hard colliders=" + c.hardColliders + "; reused original hard colliders=" + c.reusedHardColliders);
            text.AppendLine("Small-stone policy version=" + c.smallStoneVersion + "; crushable stones/pebble clusters=" + c.smallStoneItems + "; Boulder limits: height <=0.6m and horizontal extent <=1m; larger rocks remain hard.");
            if (c.smallStoneVersion >= 2)
            {
                text.AppendLine("Exact world-triangle classification: Boulder=" + c.smallStoneBoulders + "; pebble clusters=" + c.smallStonePebbleClusters + "; rotated local-box false positives corrected=" + (c.smallStoneBoundsCorrections?.Length ?? 0));
                text.AppendLine(c.smallStoneBoundsMethod);
                if (c.smallStoneBoundsCorrections != null) foreach (var correction in c.smallStoneBoundsCorrections)
                    text.AppendLine("Exact stone bounds " + correction.id + ": world centre=" + correction.worldCenter.ToString("F5") +
                        "; actual size=" + correction.exactWorldSize.ToString("F5") + "; previous box size=" + correction.previousTransformedBoxSize.ToString("F5") + "; source=" + correction.sourceId);
            }
            text.AppendLine("Replaced standalone soft solids=" + (c.replacedSoftColliders?.Length ?? 0) + "; existing protected solids=" + (c.protectedSolids?.Length ?? 0) + "; complete route guards=" + c.routeGuards);
            text.AppendLine("Exact unchanged car-box route guard: routes=" + c.carClearanceRoutes + "; samples=" + c.carClearanceSamples + "; clearance repair version=" + c.clearanceRepairVersion);
            if (c.relocations != null) foreach (var move in c.relocations)
                text.AppendLine("Approved pose-only relocation " + move.id + ": " + move.oldPosition.ToString("F5") + " -> " + move.newPosition.ToString("F5") + "; delta=" + move.delta.ToString("F3") + "; source=" + move.sourceId);
            foreach (var count in c.counts) text.AppendLine(count.category + ": soft=" + count.soft + ", hard=" + count.hard + ", surface=" + count.surface + ", composite=" + count.composite);
            text.AppendLine(c.verification ?? "All original triangle provenance verified before mutation.");
            text.AppendLine("Only query probes on RG_SceneryCrush/12 can break; large hard scenery is Default/0. Gameplay targets, upgrades, workshop, crew and actors are excluded.");
            text.AppendLine("Original visual meshes/materials and generation recipes retained; only recorded whole-instance clearance poses changed. One existing-config collider navigation bake; no full visual/world/minimap installer.");
            return text.ToString();
        }
    }
}
