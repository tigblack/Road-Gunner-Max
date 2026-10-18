using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    // Prepares the working game for reference authoring. It deliberately retains the
    // existing footprint until the first-level video can actually be inspected.
    public static class Level01Builder
    {
        public const string ScenePath = PrototypeBuilder.Root + "/Scenes/Level01_Wasteland.unity";
        public const string AssetRoot = PrototypeBuilder.Root + "/Levels/Level01";

        [MenuItem("Tools/Road Gunner/Level 01/Prepare From Working Prototype")]
        public static void Prepare()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before preparing Level 01.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Level01_Wasteland already exists. Edit the saved level; preparation does not overwrite it.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene before preparing Level 01.");

            Directory.CreateDirectory(AssetRoot);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath, OpenSceneMode.Single);
            var session = Object.FindAnyObjectByType<GameSession>();
            if (!session) throw new InvalidOperationException("The working prototype has no GameSession.");
            var originalSurface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (!originalSurface) throw new InvalidOperationException("The working prototype has no navigation surface.");
            var arena = originalSurface.transform;
            originalSurface.RemoveData();
            Object.DestroyImmediate(originalSurface);

            var root = new GameObject("Level01 • existing map / reference pending").transform;
            var terrain = Section(root, "Terrain");
            var boundaries = Section(root, "Boundaries");
            var environment = Section(root, "Environment Props");
            var landmarks = Section(root, "Landmarks");
            var playerSpawn = Section(root, "Player Spawn");
            var groups = Section(root, "Enemy Groups • editable markers");
            var navigation = Section(root, "Navigation");
            var lighting = Section(root, "Lighting");
            var gameplay = Section(root, "Gameplay Systems");
            var presentation = Section(root, "Camera and Portrait UI");

            var children = new List<Transform>();
            foreach (Transform child in arena) children.Add(child);
            foreach (var child in children)
            {
                Transform parent = environment;
                if (child.GetComponent<MeshCollider>()) parent = terrain;
                else if (child.name.Contains("perimeter") || new Vector2(child.position.x, child.position.z).magnitude > 44) parent = boundaries;
                else if (child.name.Contains("barricade")) parent = landmarks;
                child.SetParent(parent, true);
            }
            Object.DestroyImmediate(arena.gameObject);
            foreach (var obj in scene.GetRootGameObjects())
            {
                if (obj == root.gameObject) continue;
                var t = obj.transform;
                if (obj.GetComponent<Light>() || obj.GetComponent<UnityEngine.Rendering.Volume>()) t.SetParent(lighting, true);
                else if (obj.GetComponent<Camera>() || obj.GetComponent<Canvas>() || obj.GetComponent<UnityEngine.EventSystems.EventSystem>()) t.SetParent(presentation, true);
                else t.SetParent(gameplay, true);
            }

            var layout = root.gameObject.AddComponent<LevelLayout>();
            layout.session = session;
            layout.playerSpawn = Section(playerSpawn, "Start • position and forward heading");
            layout.playerSpawn.SetPositionAndRotation(session.startPosition, session.startRotation);
            layout.recoveryPoints = new Transform[session.recoveryPoints.Length];
            var recovery = Section(playerSpawn, "Recovery markers");
            for (int i = 0; i < layout.recoveryPoints.Length; i++)
            {
                var marker = Section(recovery, "Recovery " + i.ToString("00"));
                marker.SetPositionAndRotation(session.recoveryPoints[i], session.startRotation);
                layout.recoveryPoints[i] = marker;
            }

            string markerPath = AssetRoot + "/EncounterSpawn.prefab";
            var markerTemplate = new GameObject("EncounterSpawn").AddComponent<EncounterSpawn>();
            markerTemplate.spawnPoints = new Transform[4];
            for (int j = 0; j < 4; j++)
            {
                var point = Section(markerTemplate.transform, "Raider spawn " + (j + 1));
                float angle = j * Mathf.PI / 2;
                point.localPosition = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2.3f;
                markerTemplate.spawnPoints[j] = point;
            }
            var markerPrefab = PrefabUtility.SaveAsPrefabAsset(markerTemplate.gameObject, markerPath);
            Object.DestroyImmediate(markerTemplate.gameObject);
            session.spawns.encounters = new EncounterSpawn[session.spawns.groupCenters.Length];
            for (int i = 0; i < session.spawns.encounters.Length; i++)
            {
                var obj = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab, groups);
                obj.name = "Encounter " + (i + 1).ToString("00") + " • original prototype position";
                obj.transform.SetPositionAndRotation(session.spawns.groupCenters[i], Quaternion.Euler(0, -i * Mathf.Rad2Deg, 0));
                var marker = obj.GetComponent<EncounterSpawn>();
                marker.enemyCount = session.spawns.tuning.groupSize;
                PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                session.spawns.encounters[i] = marker;
            }

            Physics.SyncTransforms();
            var surface = navigation.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1;
            surface.overrideVoxelSize = true;
            surface.voxelSize = .18f;
            surface.minRegionArea = 3;
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, AssetRoot + "/Level01 navigation.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            PrototypeBuilder.SetPortraitGameView(540, 960);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("ROAD GUNNER: Prepared Level01_Wasteland with the existing map. Reference layout and 2x footprint are pending inspected frames.");
        }

        static Transform Section(Transform parent, string name)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            return result;
        }
    }
}
