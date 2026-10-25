using System;
using System.IO;
using RoadGunner.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Updates the current saved arena without respawning or relocating encounters.</summary>
    public static class MobileRevisionInstaller
    {
        const string PlayerPrefab = "Assets/RoadGunner/Prefabs/Dust Jackal.prefab";
        public const string ReportFolder = "Documentation/RoadGunner/MobileRevision-2026-10-25";

        [MenuItem("Tools/Road Gunner/Arena 1/Apply Mobile Driving And Map Revision")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.OpenScene(Arena1Installer.ScenePath);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            if (!layout || !layout.session) throw new InvalidOperationException("Existing arena references are missing.");
            var prefab = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                ConfigureHarpoon(prefab.GetComponent<HarpoonCollector>());
                PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            ConfigureScene(layout);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + "/Integration.txt",
                "Current Greenwood scene revised in place; enemy positions, roster and progression retained.\n" +
                "Player max speed6 ->9 m/s; torque2400 ->2700; brakes and wheel/suspension settings retained.\n" +
                "Harpoon launch18 ->36 m/s, retract/pull12 ->30, search.15 ->.05s, cooldown.3 ->.08s, aim480 ->960deg/s.\n" +
                "10m actual attack range retained; cyan fill and bright boundary with dark contrasting stroke.\n" +
                "Static512px top-down world mesh/texture bake, north-up;394x394 logical UI map.\n" +
                "Enemy/upgrade colors and legend updated; reward icons are distinct diamonds; mini/boss skulls.\n");
            Debug.Log("ROAD_GUNNER_MOBILE_REVISION_INTEGRATED " + scene.path);
        }

        static void Dirty(Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        static void ConfigureHarpoon(HarpoonCollector value)
        {
            if (!value) throw new InvalidOperationException("Existing rear companion harpoon is missing.");
            value.harpoonLaunchSpeed = 36; value.harpoonRetractSpeed = 30;
            value.searchInterval = .05f; value.delayBetweenCollections = .08f; value.aimSpeed = 960;
            Dirty(value);
        }

        public static void ConfigureScene(LevelLayout layout)
        {
            var s = layout.session;
            s.vehicle.tuning.maxSpeed = 9; s.vehicle.tuning.totalDriveTorque = 2700;
            Dirty(s.vehicle.tuning);
            ConfigureHarpoon(s.vehicle.GetComponent<HarpoonCollector>());
            var indicator = s.vehicle.GetComponent<AttackRangeIndicator>();
            if (!indicator || !indicator.material || !indicator.ground || !indicator.turret)
                throw new InvalidOperationException("Existing weapon range indicator references are missing.");
            indicator.fillColor = new Color(.3f, .86f, 1, .07f);
            indicator.outlineColor = new Color(.65f, .97f, 1, .98f);
            indicator.contrastColor = new Color(.025f, .11f, .14f, .65f);
            indicator.outlineWidth = .16f; indicator.contrastWidth = .14f; Dirty(indicator);
            // Ground feedback follows tire tracks but stays behind the existing UGUI HUD/map.
            indicator.material.SetColor("_BaseColor", Color.white);
            indicator.material.renderQueue = 2995; Dirty(indicator.material);

            var map = s.minimap;
            if (!map || !map.mapRect) throw new InvalidOperationException("Existing minimap references are missing.");
            map.mapRect.sizeDelta = new Vector2(394, 394);
            var frame = (RectTransform)map.mapRect.parent;
            frame.sizeDelta = new Vector2(426, 454); frame.anchoredPosition = new Vector2(-249, -547);
            var title = frame.Find("Minimap title") as RectTransform;
            if (title) { title.anchoredPosition = new Vector2(-213, -24); title.sizeDelta = new Vector2(394, 35); }
            var stats = s.hud.statsText.transform.parent as RectTransform;
            if (stats) stats.offsetMax = new Vector2(-498, -320);
            foreach (var icon in map.legendPanel.GetComponentsInChildren<ArenaMinimapIcon>(true))
            {
                icon.color = ArenaMinimap.MarkerColor(icon.kind); Dirty(icon);
            }
            var feedback = s.hud.upgradeFeedbackText.transform as RectTransform;
            if (feedback) { feedback.offsetMin = new Vector2(35, -850); feedback.offsetMax = new Vector2(-35, -780); }
            ArenaMinimapBaker.BakeAndConfigure(layout, map);
            // The optional key may overlap the map on tall phones; keep every label readable.
            map.legendPanel.transform.SetAsLastSibling();
            Dirty(map); Dirty(frame); Dirty(map.mapRect); Dirty(s.hud);
            CasualPolishInstaller.ConfigureScene(layout);
        }
    }
}
