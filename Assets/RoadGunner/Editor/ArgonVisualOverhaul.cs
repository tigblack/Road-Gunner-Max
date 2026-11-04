using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadGunner.ConceptArt.Editor;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Coordinated Argon visual overhaul runner.
    /// Rebuilds Player Car, Tracked Assemblies, Raiders (Scavenger, Enforcer, Veteran/Warlord Boss),
    /// Rust Vulture Buggies, Pickups, Ashmaw Mutant, Iron Hauler Boss Truck, Environment Props,
    /// Shoulder Templates, Regional Arcade Dressing, and Landmark Sites in-place without disturbing
    /// scene hierarchy, prefab fileIDs, shadowDistance, or route clearance, then refreshes Scenery Crush.
    /// </summary>
    public static class ArgonVisualOverhaul
    {
        const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";

        [MenuItem("Tools/Road Gunner/Art/Apply Argon Creative Visual Overhaul")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Run ArgonVisualOverhaul from compiled Edit Mode.");

            var scene = EditorSceneManager.OpenScene(ScenePath);
            var session = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameSession>(true)).Single();
            var level = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LevelLayout>(true)).Single(l => l.decorationRoot && l.ground);

            // 1. Cleanly undo previous Scenery Crush installation so collider queries match the baseline placement scene.
            UndoPreviousSceneryCrush(scene, level);

            // 2. Rebuild all Concept meshes in-place via temporary clones so existing prefab & scene MeshFilter references stay intact.
            ArtKit.Ensure();
            RebuildActorMeshesInPlace(session);
            RebuildEnvironmentMeshesInPlace(level);

            // 3. Rebuild player tracked assemblies in-place on the scene player vehicle.
            PlayerTrackVisualInstaller.Apply(session);

            // 4. Refresh Arcade palette atlases, regional dressing, landmark sites, toon materials, and actor palettes.
            var log = new List<string>();
            var toon = Shader.Find(ArcadeVisualPalette.ToonShaderName);
            if (!toon) throw new InvalidOperationException("Missing shader: " + ArcadeVisualPalette.ToonShaderName);
            var atlases = ArcadeVisualPalette.WriteAtlases();
            var (worldMat, enemyMat, mutantMat) = ArcadeVisualPalette.ApplyPalette(toon, atlases, log);
            ArcadeVisualDressing.Apply(session, log);
            ArcadeVisualSites.Apply(session, toon, log);
            ArcadeVisualPalette.ConvertScene(scene, toon, log);
            ArcadeVisualPalette.AssignActorPalettes(scene, worldMat, enemyMat, mutantMat, log);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // 5. Re-install and verify Scenery Crush collisions, route clearances, and navigation.
            DecorationCollisionInstaller.InstallBatch();

            Debug.Log("ARGON_VISUAL_OVERHAUL_OK\n" + string.Join("\n", log));
        }

        static void UndoPreviousSceneryCrush(Scene scene, LevelLayout level)
        {
            if (File.Exists(DecorationCollisionInstaller.CatalogPath))
            {
                var catalog = JsonUtility.FromJson<DecorationCollisionInstaller.Catalog>(File.ReadAllText(DecorationCollisionInstaller.CatalogPath));
                if (catalog != null && catalog.replacedSoftColliders != null)
                {
                    foreach (string id in catalog.replacedSoftColliders)
                    {
                        if (GlobalObjectId.TryParse(id, out var parsed) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) is Collider col && col)
                        {
                            col.enabled = true;
                            EditorUtility.SetDirty(col);
                            if (PrefabUtility.IsPartOfPrefabInstance(col))
                                PrefabUtility.RecordPrefabInstancePropertyModifications(col);
                        }
                    }
                }
                File.Delete(DecorationCollisionInstaller.CatalogPath);
            }

            foreach (var chunk in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DecorationCrushChunk>(true)).ToArray())
            {
                var go = chunk.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.BatchingStatic);
                Object.DestroyImmediate(chunk);
                EditorUtility.SetDirty(go);
            }

            var owned = level.decorationRoot.Find(DecorationCollisionInstaller.OwnedRoot);
            if (owned) Object.DestroyImmediate(owned.gameObject);

            if (AssetDatabase.IsValidFolder(DecorationCollisionInstaller.AssetFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { DecorationCollisionInstaller.AssetFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path).StartsWith("Hard ", StringComparison.Ordinal))
                        AssetDatabase.DeleteAsset(path);
                }
            }

            Physics.SyncTransforms();
        }

        static void RebuildActorMeshesInPlace(GameSession session)
        {
            // Player vehicle + crew + roof twin autocannon + rear ballista harpoon
            var tempPlayer = Object.Instantiate(session.vehicle.gameObject);
            try { ConceptVehicle.Apply(tempPlayer); }
            finally { Object.DestroyImmediate(tempPlayer); }

            // Raiders: Scavenger (0), Enforcer (1), Veteran / Warlord Boss (2)
            var raiderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Prefabs/Wasteland Raider.prefab");
            for (int variant = 0; variant < 3; variant++)
            {
                var tempRaider = Object.Instantiate(raiderPrefab);
                try { ConceptEnemies.ApplyRaider(tempRaider.GetComponent<Raider>(), variant); }
                finally { Object.DestroyImmediate(tempRaider); }
            }

            // Rust Vulture Enemy Buggy
            var buggyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Prefabs/Rust Vulture Enemy Buggy.prefab");
            var tempBuggy = Object.Instantiate(buggyPrefab);
            try { ConceptEnemies.ApplyBuggy(tempBuggy.GetComponent<EnemyBuggy>()); }
            finally { Object.DestroyImmediate(tempBuggy); }

            // Scrap Pickups
            var pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Prefabs/Scrap pickup.prefab");
            var tempPickup = Object.Instantiate(pickupPrefab);
            try { ConceptEnemies.ApplyPickup(tempPickup); }
            finally { Object.DestroyImmediate(tempPickup); }

            // Ashmaw Mutant Mini-Boss
            var tempMutant = new GameObject("TempAshmaw");
            try { Zone1MutantArt.Apply(tempMutant); }
            finally { Object.DestroyImmediate(tempMutant); }

            // Iron Hauler Boss Truck
            var tempTruck = new GameObject("TempIronHauler");
            try { Zone1TruckArt.Apply(tempTruck); }
            finally { Object.DestroyImmediate(tempTruck); }
        }

        static void RebuildEnvironmentMeshesInPlace(LevelLayout level)
        {
            string folder = "Assets/RoadGunner/Levels/WastelandRestored/Prefabs";
            foreach (string path in Directory.GetFiles(folder, "*.prefab"))
            {
                string kind = Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) continue;
                var temp = Object.Instantiate(prefab);
                try { ConceptEnvironment.ApplyProp(temp, kind); }
                finally { Object.DestroyImmediate(temp); }
            }

            var cliffPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Levels/Greenwood/Prefabs/Cliff.prefab");
            if (cliffPrefab)
            {
                var tempCliff = Object.Instantiate(cliffPrefab);
                try { ConceptEnvironment.ApplyProp(tempCliff, "Cliff"); }
                finally { Object.DestroyImmediate(tempCliff); }
            }

            foreach (string kind in new[] { "Cactus", "Wooden supply crate", "Boulder" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtKit.Root + "/Prefabs/" + kind + ".prefab");
                var temp = prefab ? Object.Instantiate(prefab) : new GameObject(kind);
                try { ConceptEnvironment.ApplyProp(temp, kind); }
                finally { Object.DestroyImmediate(temp); }
            }

            var workshop = Object.FindAnyObjectByType<RepairWorkshop>();
            if (workshop)
            {
                var tempWorkshop = Object.Instantiate(workshop.gameObject);
                try { ConceptEnvironment.ApplyWorkshop(tempWorkshop.GetComponent<RepairWorkshop>()); }
                finally { Object.DestroyImmediate(tempWorkshop); }
            }

            ConceptDressing.RebuildTemplatesOnly();
        }
    }
}
