using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    public static class CorridorRevisionInstaller
    {
        public const string ReportFolder = "Documentation/RoadGunner/CorridorRevision-2026-10-25";
        public static void RefreshOnboardingLabelsBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            var scene = EditorSceneManager.OpenScene(Arena1Installer.ScenePath);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            var corridor = layout.GetComponent<ArenaCorridorLayout>();
            // Visual-only refresh: colliders and positions remain the same; navigation stays valid.
            UpgradeOnboardingInstaller.Configure(layout, corridor.startHealthPoints);
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Debug.Log("ROAD_GUNNER_ONBOARDING_VISUAL_REFRESHED");
        }
        [MenuItem("Tools/Road Gunner/Arena 1/Apply Corridor Routes Collision And Onboarding Revision")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            var scene = EditorSceneManager.OpenScene(Arena1Installer.ScenePath);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            if (!layout || !layout.session) throw new InvalidOperationException("Existing Arena 1 references missing.");
            string collisionReport = GameplayObstacleAuthoring.ConfigureLevel(layout);
            var corridor = CorridorArenaInstaller.Configure(layout);
            UpgradeOnboardingInstaller.Configure(layout, corridor.startHealthPoints);
            RoadGunnerLevelAuthoring.Wire(layout);
            // Include newly shootable station solids in the same existing navigation bake.
            RoadGunnerLevelAuthoring.Bake(layout);
            CasualPolishInstaller.ConfigureScene(layout);
            layout.authoringNotes = "25 October 2026 corridor revision: existing Arena 1 systems retained, winding north road with right workshop, left Boss Raider branch, north visibly closed future arena and far-right Monster pocket. Three shootable Health +1 stations reuse ArenaRunStats. Collider classification and focused circular map updated.";
            EditorUtility.SetDirty(layout); EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + "/Integration.txt", collisionReport + "\n" +
                "Existing scene/pools/enemy health/gun/harpoon/music/UI retained. Main road14m, branches16m, broad combat pockets.\n" +
                "Three independent Health +1 station targets; durability10.5, two base5.25 gun hits each; no enemy kill reward.\n" +
                "Speed limit11.7m/s retained; explicit planar no-input deceleration11.5m/s2, settle.08m/s; vertical physics unchanged.\n" +
                "Static512px focused actual-terrain/road map, circular stencil, soft grey rim, ordinary16px red dots.\n");
            Debug.Log("ROAD_GUNNER_CORRIDOR_REVISION_INTEGRATED " + scene.path + "\n" + collisionReport);
        }
    }
}
