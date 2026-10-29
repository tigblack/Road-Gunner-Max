using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Current arena polish; keeps all authored encounters and navigation in place.</summary>
    public static class CasualPolishInstaller
    {
        public const string ReportFolder = "Documentation/RoadGunner/CasualPolish-2026-10-25";
        [MenuItem("Tools/Road Gunner/Arena 1/Apply Casual Combat UI And Music Polish")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.OpenScene(Arena1Installer.ScenePath);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            if (!layout || !layout.session) throw new InvalidOperationException("Existing Greenwood arena references are missing.");
            // Shared preparation retains the previous harpoon, stats and real map bake.
            // Its final configure hook applies this current revision exactly once.
            MobileRevisionInstaller.ConfigureScene(layout);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + "/Integration.txt",
                "Current Greenwood arena retained; no encounter/navigation/layout reconstruction.\n" +
                "Player maximum speed9 ->11.7m/s (+30%); acceleration4 ->4.4m/s2; torque2700/brakes/steering/drag retained.\n" +
                "Existing10m weapon boundary now white (.92 edge/.025 fill) with neutral dark contrast, material alpha1.\n" +
                "Normal enemies use existing-health world UI; Monster/Boss retain the existing boss HUD.\n" +
                "Static512px terrain/roads retained, circular stencil mask, chunky frame, green repair wrench.\n" +
                "Original Arena music loop and one session music source are configured.\n");
            Debug.Log("ROAD_GUNNER_CASUAL_POLISH_INTEGRATED " + scene.path);
        }
        static void Dirty(Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        public static void ConfigureScene(LevelLayout layout)
        {
            var s = layout.session;
            s.vehicle.tuning.maxSpeed = 9f * 1.30f;
            s.vehicle.tuning.acceleration = 4.4f;
            Dirty(s.vehicle.tuning);
            var indicator = s.vehicle.GetComponent<AttackRangeIndicator>();
            if (!indicator || !indicator.material) throw new InvalidOperationException("Existing weapon range UI is missing.");
            indicator.fillColor = new Color(1, 1, 1, .025f);
            indicator.outlineColor = new Color(1, 1, 1, .92f);
            indicator.contrastColor = new Color(.09f, .08f, .06f, .5f);
            indicator.outlineWidth = .16f; indicator.contrastWidth = .10f;
            indicator.material.SetColor("_BaseColor", Color.white);
            indicator.material.renderQueue = 2995; Dirty(indicator.material); Dirty(indicator);
            CasualMinimapInstaller.Configure(s.minimap);
            EnemyHealthBarAuthoring.Configure(layout);
            ArenaMusicBuilder.Configure(s);
        }
    }
}
