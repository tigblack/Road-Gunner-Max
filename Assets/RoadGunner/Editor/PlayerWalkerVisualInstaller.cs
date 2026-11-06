using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Player-only walker presentation; no world, physics, combat or prefab-asset installer is invoked.</summary>
    public static class PlayerWalkerVisualInstaller
    {
        public const string AssetRoot = "Assets/RoadGunner/Art/PlayerWalker";
        public const string ReportFolder = "Documentation/RoadGunner/PlayerWalkerInstant-2026-11-06";
        public const string ScenePath = PlayerTrackVisualInstaller.ScenePath;
        static readonly string[] BodyNames = { "Walker torso", "Walker pelvis", "Walker right arm" };
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static Quaternion Q(float x, float y, float z) => Quaternion.Euler(x, y, z);
        [Serializable] sealed class Row { public string id, type, json; }
        [Serializable] sealed class Snapshot { public Row[] rows; }
        [Serializable] sealed class Pose { public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] sealed class HealthSettings { public string tuning, stats; public bool enabled; }
        [Serializable] sealed class ImprintSettings { public string vehicle; public bool enabled; }
        [Serializable] sealed class Report
        {
            public string scene, scope; public bool protectedSettingsUnchanged;
            public int clearedLegacyMeshReferences, walkerRenderers, physicalColliders, wheels, geometryRevision, generatedVertices, generatedTriangles;
            public Vector3 visualGroundOrigin; public string[] generatedMeshes;
            public Snapshot before, after;
        }

        [MenuItem("Tools/Road Gunner/Player/Install Walker Visuals Only")]
        public static void InstallBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install walker presentation from compiled Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene before walker authoring.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameSession>(true)).Single();
            Apply(session);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("PLAYER_WALKER_INSTALL_OK " + ScenePath + "; player presentation only; physics/gameplay guard passed.");
        }

        public static PlayerTrackVisuals Apply(GameSession session)
        {
            if (!session || !session.vehicle || !session.health || !session.turret)
                throw new InvalidOperationException("The existing saved player, health and roof gun are required.");
            var car = session.vehicle; var gun = session.turret; var harpoon = car.GetComponent<HarpoonCollector>();
            var visuals = car.transform.Find("Replaceable visuals");
            var roof = visuals ? visuals.Find("RoofMachineGun") : null;
            var rear = visuals ? visuals.Find("RearCompanionArea") : null;
            var mount = rear ? rear.Find("HarpoonMount") : null;
            if (!visuals || !roof || !rear || !mount || !gun.yaw || !gun.pitch || !gun.muzzle ||
                !harpoon || !harpoon.pivot || !harpoon.spawnPoint || !harpoon.collectionPoint || !harpoon.projectile)
                throw new InvalidOperationException("Retain the saved weapon mounts, aiming pivots, muzzles and harpoon references.");
            if (visuals.GetComponentsInChildren<Collider>(true).Length != 0 || visuals.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("Cannot move the visual origin: it contains a physical component.");
            if ((visuals.localScale - Vector3.one).sqrMagnitude > .00001f || Quaternion.Angle(visuals.localRotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException("The existing replaceable visual root must have unit scale and identity local rotation.");
            var level = sceneObjects<LevelLayout>(car.gameObject.scene).Single(l => l.session == session);
            var terrain = level.ground ? level.ground.GetComponent<Collider>() : null;
            if (!terrain || !terrain.Raycast(new Ray(car.transform.position + Vector3.up * 100, Vector3.down), out var floor, 250))
                throw new InvalidOperationException("No actual saved terrain below the player for its ground-space walker frame.");

            var before = ReadProtected(session, visuals);
            var colliderIds = car.GetComponentsInChildren<Collider>(true).Select(Id).OrderBy(s => s).ToArray();
            var wheelIds = car.wheels.Select(Id).ToArray();
            // This narrow helper regenerates the existing biped leg preview, and retains all four ordinary wheel anchors.
            // Call it before clearing their obsolete visible meshes; the renderer anchors remain for subsequent installs.
            var legs = PlayerTrackVisualInstaller.Apply(session);
            EnsureFolder(AssetRoot); var material = WalkerMaterial();
            var oldMeshes = visuals.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && !OwnedWalker(f.transform, visuals, harpoon)).ToArray();
            foreach (var filter in visuals.GetComponentsInChildren<MeshFilter>(true))
            {
                filter.sharedMesh = null; Dirty(filter);
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer) { renderer.enabled = false; renderer.sharedMaterials = Array.Empty<Material>(); Dirty(renderer); }
            }
            foreach (var renderer in visuals.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { renderer.sharedMesh = null; renderer.sharedMaterials = Array.Empty<Material>(); renderer.enabled = false; Dirty(renderer); }
            // Removing mesh/material references (rather than hiding a car beneath the walker) preserves prefab anchors.
            // Their empty disabled renderer components also keep PlayerTrackVisualInstaller idempotent.
            visuals.localPosition = car.transform.InverseTransformPoint(floor.point); Dirty(visuals);
            rear.localPosition = Vector3.zero; rear.localRotation = Quaternion.identity; Dirty(rear);
            roof.localPosition = V(.56f, 1.965f, -.12f); Dirty(roof);
            mount.localPosition = V(-.84f, 1.92f, .14f); mount.localRotation = Quaternion.identity; Dirty(mount);
            harpoon.pivot.localRotation = Q(0, -4, 0); Dirty(harpoon.pivot);

            var meshes = new List<string>(); var body = new List<Renderer>();
            MeshPart(Child(visuals, BodyNames[0]), Torso(), "Walker torso", material, meshes, body);
            MeshPart(Child(visuals, BodyNames[1]), Pelvis(), "Walker pelvis", material, meshes, body);
            MeshPart(Child(visuals, BodyNames[2]), RightArm(), "Walker right arm", material, meshes, body);
            MeshPart(Child(roof, "Walker cannon support"), CannonSupport(), "Walker cannon support", material, meshes, body);
            // Shape the barrels in the actual existing pitch/pivot spaces, ending at their real firing origins.
            Vector3 muzzle = gun.pitch.InverseTransformPoint(gun.muzzle.position);
            Vector3 barrelForward = gun.pitch.InverseTransformDirection(gun.muzzle.forward).normalized;
            MeshPart(Child(gun.pitch, "Walker shoulder cannon"), Cannon(muzzle, barrelForward), "Walker shoulder cannon", material, meshes, body);
            Vector3 launch = harpoon.pivot.InverseTransformPoint(harpoon.spawnPoint.position);
            Vector3 launchForward = harpoon.pivot.InverseTransformDirection(harpoon.spawnPoint.forward).normalized;
            MeshPart(Child(harpoon.pivot, "Walker harpoon arm"), HarpoonArm(launch, launchForward), "Walker harpoon arm", material, meshes, body);
            MeshPart(Child(harpoon.projectile, "Walker grapnel"), Grapnel(), "Walker grapnel", material, meshes, new List<Renderer>());
            body.Add(legs.leftMesh.GetComponent<Renderer>()); body.Add(legs.rightMesh.GetComponent<Renderer>());
            session.health.bodyPanels = body.Where(r => r).Distinct().ToArray(); Dirty(session.health);
            var tracks = car.GetComponent<TireTracks>();
            if (tracks) { tracks.material = PlayerWalkerFootprintMaterial.Ensure(); Dirty(tracks); }
            legs.ResetMotion(); Dirty(legs);

            if (!colliderIds.SequenceEqual(car.GetComponentsInChildren<Collider>(true).Select(Id).OrderBy(s => s)) || !wheelIds.SequenceEqual(car.wheels.Select(Id)))
                throw new InvalidOperationException("Walker authoring changed an original physical contact.");
            if (visuals.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh && !OwnedWalker(f.transform, visuals, harpoon)))
                throw new InvalidOperationException("An obsolete player car/crew/wheel mesh reference remains.");
            var after = ReadProtected(session, visuals);
            bool unchanged = JsonUtility.ToJson(before) == JsonUtility.ToJson(after);
            var report = new Report {
                scene = ScenePath, scope = "Player-only visual replacement. Existing physical contacts, Rigidbody, controls, health/combat configuration, weapon component references, world, navigation and UI are unchanged. Visual-only origin/mount positions, damage-flash renderer bindings and player footprint material are deliberately updated; shared historical car assets/prefabs are untouched.",
                protectedSettingsUnchanged = unchanged, clearedLegacyMeshReferences = oldMeshes.Length, walkerRenderers = body.Count,
                physicalColliders = colliderIds.Length, wheels = wheelIds.Length, visualGroundOrigin = visuals.localPosition,
                geometryRevision = 2, generatedVertices = meshes.Sum(p => AssetDatabase.LoadAssetAtPath<Mesh>(p).vertexCount),
                generatedTriangles = meshes.Sum(p => checked((int)AssetDatabase.LoadAssetAtPath<Mesh>(p).GetIndexCount(0)) / 3),
                generatedMeshes = meshes.ToArray(), before = before, after = after
            };
            Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/Authoring.json", JsonUtility.ToJson(report, true));
            if (!unchanged)
            {
                var a = before.rows.ToDictionary(r => r.id, r => r.json); var b = after.rows.ToDictionary(r => r.id, r => r.json);
                var changed = a.Keys.Union(b.Keys).Where(id => !a.TryGetValue(id, out var x) || !b.TryGetValue(id, out var y) || x != y);
                throw new InvalidOperationException("Walker authoring changed protected settings: " + string.Join(", ", changed.Take(12)));
            }
            EditorSceneManager.MarkSceneDirty(car.gameObject.scene);
            Debug.Log("PLAYER_WALKER_VISUALS_OK cleared legacy meshes=" + oldMeshes.Length + "; combined upper meshes=" + meshes.Count +
                "; retained wheels=" + wheelIds.Length + "; retained colliders=" + colliderIds.Length + "; protected rows=" + before.rows.Length + "; actual ground-frame=" + visuals.localPosition);
            return legs;
        }

        static bool OwnedWalker(Transform t, Transform visual, HarpoonCollector harpoon) =>
            BodyNames.Any(n => { var root = visual.Find(n); return root && t.IsChildOf(root); }) || t.name.StartsWith("Walker ", StringComparison.Ordinal) ||
            AncestorNamed(t, "Walker harpoon arm") || AncestorNamed(t, "Walker shoulder cannon") || AncestorNamed(t, "Walker cannon support") || AncestorNamed(t, "Walker grapnel");
        static bool AncestorNamed(Transform t, string name) { for (; t; t = t.parent) if (t.name == name) return true; return false; }
        static string Id(Object value) => value ? value.GetEntityId().ToString() : "null";
        static IEnumerable<T> sceneObjects<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static Snapshot ReadProtected(GameSession session, Transform visual)
        {
            var rows = new List<Row>(); var trackRoot = session.vehicle.transform.Find(PlayerTrackVisualInstaller.RootName);
            var objects = sceneObjects<Component>(session.gameObject.scene).Cast<Object>().ToList();
            objects.AddRange(AssetDatabase.GetDependencies(ScenePath, true).Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                !p.StartsWith(AssetRoot + "/", StringComparison.Ordinal) && !p.StartsWith(PlayerTrackVisualInstaller.AssetRoot + "/", StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>).Where(a => a).Cast<Object>());
            foreach (var value in objects.Where(o => o).Distinct())
            {
                string json;
                if (value is PlayerTrackVisuals) continue;
                if (value == session.health) json = JsonUtility.ToJson(new HealthSettings { tuning = Id(session.health.tuning), stats = Id(session.health.runStats), enabled = session.health.enabled });
                else if (value is TireTracks imprints && imprints.vehicle == session.vehicle)
                    json = JsonUtility.ToJson(new ImprintSettings { vehicle = Id(imprints.vehicle), enabled = imprints.enabled });
                else if (value is Transform transform)
                {
                    if (transform.IsChildOf(visual) || trackRoot && transform.IsChildOf(trackRoot)) continue;
                    json = JsonUtility.ToJson(new Pose { position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale });
                }
                else if (value is Renderer renderer && (renderer.transform.IsChildOf(visual) || trackRoot && renderer.transform.IsChildOf(trackRoot))) continue;
                else if (value is MeshFilter filter && (filter.transform.IsChildOf(visual) || trackRoot && filter.transform.IsChildOf(trackRoot))) continue;
                else if (value is MonoBehaviour || value is Collider || value is Rigidbody || value is NavMeshAgent || value is Renderer || value is MeshFilter || value is ScriptableObject || value is AudioSource || value is ParticleSystem)
                    json = EditorJsonUtility.ToJson(value);
                else continue;
                rows.Add(new Row { id = Id(value), type = value.GetType().FullName, json = json });
            }
            var harpoon = session.vehicle.GetComponent<HarpoonCollector>();
            foreach (var point in new[] { session.turret.muzzle, harpoon.spawnPoint, harpoon.collectionPoint })
                rows.Add(new Row { id = "weapon-point/" + Id(point), type = "Protected muzzle/collection local pose",
                    json = JsonUtility.ToJson(new Pose { position = point.localPosition, rotation = point.localRotation, scale = point.localScale }) });
            return new Snapshot { rows = rows.OrderBy(r => r.id, StringComparer.Ordinal).ToArray() };
        }

        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name); if (!child) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            child.localPosition = Vector3.zero; child.localRotation = Quaternion.identity; child.localScale = Vector3.one; child.gameObject.layer = parent.gameObject.layer;
            Dirty(child);
            return child;
        }
        static void MeshPart(Transform parent, Geometry geometry, string name, Material material, List<string> paths, List<Renderer> renderers)
        {
            string path = AssetRoot + "/" + name + ".asset"; var generated = geometry.Mesh(name);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) { EditorUtility.CopySerialized(generated, saved); Object.DestroyImmediate(generated); EditorUtility.SetDirty(saved); }
            else { saved = generated; AssetDatabase.CreateAsset(saved, path); }
            var filter = parent.GetComponent<MeshFilter>(); if (!filter) filter = parent.gameObject.AddComponent<MeshFilter>();
            var renderer = parent.GetComponent<MeshRenderer>(); if (!renderer) renderer = parent.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = saved; renderer.sharedMaterial = material; renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true; renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            Dirty(filter); Dirty(renderer); paths.Add(path); renderers.Add(renderer);
        }
        static void Dirty(Object value) { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        static Material WalkerMaterial()
        {
            var shader = Shader.Find("RoadGunner/Arcade Toon"); if (!shader) throw new InvalidOperationException("Saved Arcade Toon shader missing.");
            var colors = new[] { "253336", "647D7E", "D9DDCE", "267D7A", "DFAF4D", "EEE2BC", "173C42", "80E1CE" };
            var palette = Palette("Walker palette", false, colors.Select(Hex).ToArray());
            float[] metal = { .5f, .7f, .18f, .36f, .65f, .2f, .5f, .15f }, smooth = { .2f, .38f, .32f, .32f, .44f, .28f, .5f, .52f };
            var response = Palette("Walker surface response", true, Enumerable.Range(0, 8).Select(i => new Color(metal[i], 0, 0, smooth[i])).ToArray());
            string path = AssetRoot + "/Walker Arcade Toon.mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader) { name = "Walker Arcade Toon" }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.enableInstancing = true; material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", palette); material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetTexture("_MetallicGlossMap", response); material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1);
            material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION");
            material.SetFloat("_WreckAmount", 0); EditorUtility.SetDirty(material); return material;
        }
        static Texture2D Palette(string name, bool linear, Color[] colors)
        {
            string path = AssetRoot + "/" + name + ".asset"; var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture) { texture = new Texture2D(8, 1, TextureFormat.RGBA32, false, linear) { name = name }; AssetDatabase.CreateAsset(texture, path); }
            texture.SetPixels(colors); texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; texture.Apply(false, false); EditorUtility.SetDirty(texture); return texture;
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }

        static Geometry Torso()
        {
            var g = new Geometry();
            g.Box(V(0, 1.79f, -.02f), V(1.40f, .67f, .78f), 3, .12f, Q(-6, 0, 0));
            g.Box(V(0, 1.93f, .34f), V(1.18f, .30f, .18f), 2, .065f, Q(-14, 0, 0));
            g.Box(V(0, 1.61f, .35f), V(.72f, .18f, .19f), 0, .04f, Q(15, 0, 0));
            g.Box(V(0, 1.79f, .455f), V(.13f, .42f, .055f), 4, .018f);
            g.Box(V(0, 1.82f, .49f), V(.085f, .15f, .035f), 7, .012f);
            // Separate plates reveal the dark underlying chassis at their seams instead of adding painted noise.
            for (int s = -1; s <= 1; s += 2)
            {
                g.Box(V(s * .32f, 1.96f, .443f), V(.47f, .23f, .065f), 0, .035f, Q(-14, 0, s * -6));
                g.Box(V(s * .32f, 1.965f, .482f), V(.40f, .185f, .035f), 2, .025f, Q(-14, 0, s * -6));
                g.Box(V(s * .32f, 1.875f, .467f), V(.31f, .027f, .035f), 4, .005f, Q(-14, 0, 0));
                g.Box(V(s * .47f, 1.67f, .386f), V(.27f, .19f, .07f), 0, .025f);
                for (int i = 0; i < 3; i++) g.Box(V(s * .47f, 1.615f + i * .052f, .426f), V(.205f, .017f, .026f), 1, .004f, Q(-15, 0, 0));
                foreach (float y in new[] { 1.915f, 2.015f }) g.Bolt(V(s * .49f, y, .494f), Vector3.forward, .024f);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                g.Box(V(s * .65f, 1.94f, .02f), V(.35f, .34f, .68f), 2, .075f, Q(0, 0, s * 12));
                g.Box(V(s * .69f, 1.93f, .38f), V(.19f, .11f, .055f), 4, .015f);
                g.Cylinder(V(s * .76f, 1.91f, .02f), Vector3.right, .20f, .17f, 0);
                g.Cylinder(V(s * .85f, 1.91f, .02f), Vector3.right, .135f, .06f, 4);
                g.Box(V(s * .65f, 2.108f, .015f), V(.29f, .045f, .49f), 0, .022f, Q(0, 0, s * 12));
                g.Box(V(s * .65f, 2.132f, .015f), V(.235f, .026f, .41f), 3, .014f, Q(0, 0, s * 12));
                for (int i = 0; i < 3; i++) g.Box(V(s * .65f, 2.151f, -.11f + i * .095f), V(.14f, .012f, .025f), 1, .003f);
                foreach (float z in new[] { -.23f, .25f }) g.Bolt(V(s * .65f, 2.15f, z), Vector3.up, .027f);
                g.Tube(V(s * .865f, 1.91f, .02f), Vector3.right, .145f, .105f, .055f, 1, 12);
                g.Box(V(s * .42f, 1.83f, -.46f), V(.37f, .56f, .30f), 0, .055f);
                g.Cylinder(V(s * .42f, 1.84f, -.64f), Vector3.forward, .135f, .14f, 1);
                g.Cylinder(V(s * .42f, 1.84f, -.72f), Vector3.forward, .09f, .025f, 6);
                for (int i = 0; i < 4; i++) g.Box(V(s * .39f, 1.66f + i * .095f, -.637f), V(.28f, .028f, .035f), 1, .005f);
                g.Box(V(s * .42f, 2.12f, -.46f), V(.32f, .055f, .23f), 3, .018f);
                g.Bolt(V(s * .42f, 2.155f, -.46f), Vector3.up, .03f);
                g.Hose(new[] { V(s * .22f, 1.54f, -.24f), V(s * .63f, 1.60f, -.32f), V(s * .66f, 1.82f, -.27f) }, .033f, 0);
                g.Piston(V(s * .52f, 1.56f, .03f), V(s * .62f, 1.89f, .04f), .045f);
            }
            g.Box(V(0, 1.91f, -.465f), V(.29f, .41f, .105f), 3, .04f);
            for (int i = 0; i < 4; i++) g.Box(V(0, 1.79f + i * .075f, -.523f), V(.18f, .024f, .02f), 0, .004f);
            g.Cylinder(V(0, 2.12f, .04f), Vector3.up, .17f, .17f, 0);
            for (int i = 0; i < 3; i++) g.Cylinder(V(0, 2.07f + i * .046f, .04f), Vector3.up, .18f, .017f, 1);
            g.Box(V(0, 2.32f, .075f), V(.57f, .43f, .55f), 2, .09f, Q(-5, 0, 0));
            g.Box(V(0, 2.34f, .345f), V(.45f, .145f, .065f), 6, .035f, Q(-5, 0, 0));
            g.Box(V(0, 2.35f, .385f), V(.34f, .043f, .024f), 7, .008f);
            g.Box(V(0, 2.429f, .30f), V(.49f, .045f, .17f), 0, .018f, Q(-9, 0, 0));
            g.Box(V(0, 2.464f, .19f), V(.43f, .035f, .27f), 2, .02f, Q(-8, 0, 0));
            g.Cylinder(V(.19f, 2.335f, .391f), Vector3.forward, .041f, .037f, 1, 10);
            g.Cylinder(V(.19f, 2.335f, .413f), Vector3.forward, .025f, .008f, 7, 10);
            for (int s = -1; s <= 1; s += 2)
            {
                g.Cylinder(V(s * .285f, 2.32f, .035f), Vector3.right, .085f, .065f, 0, 12);
                g.Cylinder(V(s * .323f, 2.32f, .035f), Vector3.right, .055f, .018f, 4, 10);
                g.Bolt(V(s * .20f, 2.48f, .06f), Vector3.up, .022f);
            }
            g.Box(V(0, 2.18f, .32f), V(.30f, .095f, .10f), 0, .02f);
            g.Box(V(0, 2.52f, -.03f), V(.12f, .08f, .26f), 4, .018f);
            g.Box(V(-.23f, 2.58f, -.10f), V(.035f, .27f, .045f), 1, .008f, Q(0, 0, 6));
            g.Cylinder(V(-.23f, 2.46f, -.10f), Vector3.up, .055f, .08f, 0, 8);
            return g;
        }
        static Geometry Pelvis()
        {
            var g = new Geometry(); g.Cylinder(V(0, 1.39f, 0), Vector3.up, .38f, .21f, 0);
            g.Box(V(0, 1.29f, .015f), V(1.18f, .26f, .53f), 3, .07f);
            g.Box(V(0, 1.29f, .30f), V(.36f, .23f, .12f), 4, .035f);
            for (int s = -1; s <= 1; s += 2)
            {
                g.Cylinder(V(s * .63f, 1.38f, 0), Vector3.right, .18f, .20f, 1);
                g.Box(V(s * .44f, 1.18f, .24f), V(.29f, .36f, .19f), 2, .05f, Q(-8, 0, s * 11));
                g.Box(V(s * .44f, 1.21f, .349f), V(.18f, .19f, .025f), 3, .018f, Q(-8, 0, s * 11));
                g.Bolt(V(s * .45f, 1.27f, .371f), Vector3.forward, .025f);
                g.Piston(V(s * .28f, 1.35f, -.23f), V(s * .34f, 1.56f, -.20f), .037f);
            }
            g.Box(V(0, 1.29f, .37f), V(.23f, .13f, .04f), 0, .02f);
            g.Box(V(0, 1.29f, .396f), V(.11f, .045f, .014f), 1, .006f);
            return g;
        }
        static Geometry RightArm()
        {
            var g = new Geometry();
            g.Cylinder(V(.90f, 1.90f, 0), Vector3.right, .18f, .21f, 0);
            g.Beam(V(.92f, 1.86f, 0), V(1.02f, 1.53f, .055f), .23f, 1);
            g.Box(V(1.01f, 1.68f, .06f), V(.31f, .42f, .34f), 3, .06f, Q(-8, 0, 8));
            g.Cylinder(V(1.02f, 1.48f, .10f), Vector3.right, .13f, .30f, 4);
            g.Beam(V(1.02f, 1.48f, .10f), V(1.0f, 1.18f, .34f), .22f, 0);
            g.Box(V(1.0f, 1.29f, .25f), V(.34f, .39f, .36f), 2, .065f, Q(22, 0, 0));
            g.Box(V(1.0f, 1.11f, .38f), V(.27f, .20f, .29f), 0, .035f);
            g.Piston(V(1.115f, 1.74f, -.08f), V(1.15f, 1.34f, .10f), .044f);
            g.Hose(new[] { V(.94f, 1.77f, -.16f), V(.92f, 1.51f, -.13f), V(.94f, 1.31f, .03f) }, .028f, 0);
            g.Tube(V(1.18f, 1.48f, .10f), Vector3.right, .115f, .065f, .035f, 1, 12);
            foreach (float y in new[] { 1.24f, 1.36f }) g.Bolt(V(1.0f, y, .453f), Vector3.forward, .025f);
            g.Box(V(1.15f, 1.30f, .25f), V(.035f, .22f, .21f), 3, .014f, Q(22, 0, 0));
            for (int i = 0; i < 3; i++) g.Box(V(1.171f, 1.25f + i * .055f, .25f), V(.012f, .016f, .15f), 0, .003f);
            for (int i = 0; i < 3; i++) g.Box(V(.91f + i * .085f, 1.08f, .52f), V(.055f, .10f, .075f), 1, .01f);
            return g;
        }
        static Geometry CannonSupport()
        {
            var g = new Geometry(); g.Box(V(0, -.015f, 0), V(.48f, .16f, .52f), 0, .04f);
            g.Cylinder(V(0, .12f, 0), Vector3.up, .20f, .18f, 4);
            g.Tube(V(0, .165f, 0), Vector3.up, .215f, .17f, .035f, 1, 14);
            for (int i = 0; i < 4; i++)
            { float a = i * Mathf.PI * .5f; g.Bolt(V(Mathf.Cos(a) * .19f, .079f, Mathf.Sin(a) * .19f), Vector3.up, .027f); }
            return g;
        }
        static Geometry Cannon(Vector3 muzzle, Vector3 forward)
        {
            var g = new Geometry(); Vector3 axis = forward.sqrMagnitude > .5f ? forward : Vector3.forward;
            g.Box(V(0, 0, .12f), V(.49f, .35f, .70f), 3, .075f);
            g.Box(V(0, .19f, .10f), V(.30f, .07f, .50f), 2, .02f);
            g.Box(V(.28f, -.025f, .03f), V(.14f, .26f, .42f), 4, .025f);
            g.Box(V(-.25f, .015f, .12f), V(.025f, .22f, .48f), 0, .012f);
            for (int i = 0; i < 4; i++) g.Box(V(-.269f, .015f, -.025f + i * .09f), V(.018f, .15f, .026f), 1, .005f, Q(-12, 0, 0));
            g.Box(V(0, .232f, .10f), V(.23f, .025f, .36f), 0, .014f);
            foreach (float z in new[] { -.02f, .22f }) g.Bolt(V(0, .251f, z), Vector3.up, .025f);
            for (int i = 0; i < 4; i++) g.Box(V(.36f, -.04f, -.10f + i * .08f), V(.025f, .13f, .045f), 1, .007f);
            Vector3 rear = muzzle - axis * Mathf.Min(1.10f, Mathf.Max(.55f, muzzle.magnitude - .20f));
            g.Cylinder((rear + muzzle) * .5f, axis, .115f, Vector3.Distance(rear, muzzle) - .08f, 0);
            for (int i = 0; i < 5; i++) g.Cylinder(Vector3.Lerp(rear, muzzle, .12f + i * .15f), axis, .15f, .075f, i % 2 == 0 ? 1 : 3);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized, up = Vector3.Cross(axis, right).normalized;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3; Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                g.Beam(rear + radial * .145f + axis * .08f, muzzle + radial * .145f - axis * .31f, .035f, 1);
            }
            g.Tube(muzzle - axis * .09f, axis, .19f, .12f, .18f, 2, 16);
            g.Cylinder(muzzle - axis * .135f, axis, .122f, .012f, 0, 16);
            g.Tube(muzzle - axis * .18f, axis, .16f, .12f, .03f, 4, 14);
            return g;
        }
        static Geometry HarpoonArm(Vector3 launch, Vector3 forward)
        {
            var g = new Geometry();
            g.Cylinder(V(0, -.01f, 0), Vector3.right, .19f, .26f, 4);
            g.Beam(V(0, -.06f, .02f), V(-.04f, -.35f, .22f), .24f, 1);
            g.Box(V(-.04f, -.22f, .16f), V(.34f, .38f, .35f), 3, .06f, Q(-20, 0, 0));
            g.Cylinder(V(-.04f, -.34f, .25f), Vector3.right, .13f, .34f, 0);
            g.Beam(V(-.04f, -.34f, .25f), V(0, -.11f, .72f), .23f, 1);
            g.Box(V(0, -.045f, .70f), V(.42f, .33f, .59f), 2, .065f, Q(-6, 0, 0));
            g.Box(V(-.23f, -.08f, .68f), V(.10f, .24f, .42f), 4, .02f);
            g.Piston(V(.15f, -.27f, .29f), V(.18f, -.12f, .73f), .037f);
            g.Hose(new[] { V(-.17f, -.14f, .22f), V(-.24f, -.28f, .37f), V(-.20f, -.16f, .65f) }, .027f, 0);
            g.Tube(V(-.30f, -.08f, .68f), Vector3.right, .115f, .071f, .09f, 1, 12);
            g.Cylinder(V(-.315f, -.08f, .68f), Vector3.right, .06f, .025f, 0, 10);
            g.Box(V(0, .136f, .69f), V(.28f, .035f, .43f), 0, .018f);
            for (int i = 0; i < 4; i++) g.Box(V(0, .16f, .55f + i * .085f), V(.22f, .018f, .025f), 1, .004f);
            foreach (float z in new[] { .48f, .88f }) g.Bolt(V(.14f, .14f, z), Vector3.up, .025f);
            var axis = forward.sqrMagnitude > .5f ? forward : Vector3.forward;
            g.Cylinder(launch - axis * .30f, axis, .12f, .55f, 0);
            g.Tube(launch - axis * .08f, axis, .20f, .125f, .16f, 3, 16);
            g.Cylinder(launch - axis * .13f, axis, .127f, .01f, 0, 14);
            g.Tube(launch - axis * .19f, axis, .16f, .12f, .045f, 1, 12);
            for (int s = -1; s <= 1; s += 2)
                g.Box(launch - axis * .14f + Vector3.right * (s * .20f), V(.075f, .10f, .32f), 4, .018f, Q(0, s * 10, 0));
            return g;
        }
        static Geometry Grapnel()
        {
            var g = new Geometry(); g.Cylinder(Vector3.zero, Vector3.forward, .075f, .32f, 1);
            g.Cylinder(V(0, 0, .15f), Vector3.forward, .10f, .11f, 4);
            g.Tube(V(0, 0, -.11f), Vector3.forward, .095f, .06f, .035f, 4, 8);
            for (int s = -1; s <= 1; s += 2) g.Beam(V(s * .05f, 0, .11f), V(s * .18f, 0, -.08f), .065f, 2);
            return g;
        }

        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>(); readonly List<int> triangles = new List<int>(); readonly List<Vector2> uv = new List<Vector2>();
            void Face(Vector3[] points, Vector3 normal, int color)
            {
                points = (Vector3[])points.Clone(); // Cylinder cap winding must not reorder the side-ring vertices.
                if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0) Array.Reverse(points);
                int first = vertices.Count; vertices.AddRange(points);
                for (int i = 0; i < points.Length; i++) uv.Add(new Vector2((color + .5f) / 8, .5f));
                for (int i = 1; i < points.Length - 1; i++) { triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1); }
            }
            public void Box(Vector3 centre, Vector3 size, int color, float bevel = .035f, Quaternion? orientation = null)
            {
                Quaternion rotation = orientation ?? Quaternion.identity; Vector3 h = size * .5f;
                float b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * .75f); var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                void F(Vector3[] points, Vector3 normal) => Face(points.Select(p => centre + rotation * p).ToArray(), rotation * normal, color);
                for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = axes[axis] * sign, u = axes[(axis + 1) % 3], v = axes[(axis + 2) % 3], at = n * h[axis];
                    float hu = h[(axis + 1) % 3] - b, hv = h[(axis + 2) % 3] - b;
                    F(new[] { at - u * hu - v * hv, at + u * hu - v * hv, at + u * hu + v * hv, at - u * hu + v * hv }, n);
                }
                for (int edge = 0; edge < 3; edge++) for (int s = -1; s <= 1; s += 2) for (int t = -1; t <= 1; t += 2)
                {
                    int a = (edge + 1) % 3, c = (edge + 2) % 3; Vector3 e = axes[edge] * (h[edge] - b);
                    Vector3 pa = axes[a] * (s * h[a]) + axes[c] * (t * (h[c] - b)), pc = axes[a] * (s * (h[a] - b)) + axes[c] * (t * h[c]);
                    F(new[] { pa - e, pa + e, pc + e, pc - e }, axes[a] * s + axes[c] * t);
                }
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    F(new[] { V(x * h.x, y * (h.y - b), z * (h.z - b)), V(x * (h.x - b), y * h.y, z * (h.z - b)), V(x * (h.x - b), y * (h.y - b), z * h.z) }, V(x, y, z));
            }
            public void Beam(Vector3 a, Vector3 b, float width, int color) => Box((a + b) * .5f, V(width, Vector3.Distance(a, b), width), color, .02f, Quaternion.FromToRotation(Vector3.up, b - a));
            public void Cylinder(Vector3 centre, Vector3 axis, float radius, float length, int color, int sides = 10)
            {
                var rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized); var top = new Vector3[sides]; var bottom = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides; Vector3 p = V(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                    top[i] = centre + rotation * (p + Vector3.up * (length * .5f)); bottom[i] = centre + rotation * (p - Vector3.up * (length * .5f));
                }
                Face(top, axis, color); Face(bottom, -axis, color);
                for (int i = 0; i < sides; i++)
                { int next = (i + 1) % sides; Face(new[] { bottom[i], bottom[next], top[next], top[i] }, (top[i] + top[next] + bottom[i] + bottom[next]) * .25f - centre, color); }
            }
            public void Bolt(Vector3 centre, Vector3 normal, float radius)
            {
                Cylinder(centre, normal, radius, radius * .42f, 1, 6);
                Cylinder(centre + normal.normalized * (radius * .23f), normal, radius * .38f, .003f, 0, 6);
            }
            public void Piston(Vector3 a, Vector3 b, float radius)
            {
                Vector3 axis = (b - a).normalized; float length = Vector3.Distance(a, b);
                Cylinder(Vector3.Lerp(a, b, .30f), axis, radius, length * .60f, 0, 8);
                Cylinder(Vector3.Lerp(a, b, .72f), axis, radius * .50f, length * .56f, 1, 8);
                Cylinder(Vector3.Lerp(a, b, .58f), axis, radius * 1.12f, .035f, 4, 10);
                Cylinder(a, axis, radius * 1.20f, .045f, 1, 8);
                Cylinder(b, axis, radius * .86f, .035f, 0, 8);
            }
            public void Hose(Vector3[] points, float radius, int color)
            {
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 axis = points[i] - points[i - 1];
                    Cylinder((points[i] + points[i - 1]) * .5f, axis, radius, axis.magnitude + radius, color, 6);
                    Cylinder(points[i - 1], axis, radius * 1.20f, radius * .9f, 1, 8);
                }
            }
            public void Tube(Vector3 centre, Vector3 axis, float outerRadius, float innerRadius, float length, int color, int sides)
            {
                axis.Normalize(); var rotation = Quaternion.FromToRotation(Vector3.up, axis);
                Vector3 Point(int index, float radius, float height)
                {
                    float angle = index * Mathf.PI * 2 / sides;
                    return centre + rotation * V(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
                }
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides; float h = length * .5f;
                    Vector3 ot = Point(i, outerRadius, h), on = Point(next, outerRadius, h), ob = Point(i, outerRadius, -h), obn = Point(next, outerRadius, -h);
                    Vector3 it = Point(i, innerRadius, h), inn = Point(next, innerRadius, h), ib = Point(i, innerRadius, -h), ibn = Point(next, innerRadius, -h);
                    Vector3 radial = Vector3.ProjectOnPlane((ot + on) * .5f - centre, axis).normalized;
                    Face(new[] { ot, on, inn, it }, axis, color);
                    Face(new[] { ob, obn, ibn, ib }, -axis, color);
                    Face(new[] { ob, obn, on, ot }, radial, color);
                    Face(new[] { ib, ibn, inn, it }, -radial, 0);
                }
            }
            public Mesh Mesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
