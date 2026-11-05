using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Wire timed enemy returns without regenerating the arena, art or navigation.</summary>
    public static class EnemyRespawnInstaller
    {
        [MenuItem("Tools/Road Gunner/Enemies/Install Death-site Respawn Timers")]
        public static void InstallBatch()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install enemy timers from compiled Edit Mode.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before installing enemy timers.");
            var scene = EditorSceneManager.OpenScene(DecorationCollisionInstaller.ScenePath);
            var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameSession>(true)).Single();
            var manager = session.GetComponent<ArenaEnemyRespawns>();
            if (!manager) manager = session.gameObject.AddComponent<ArenaEnemyRespawns>();
            manager.session = session;
            manager.respawnDelaySeconds = 30;
            manager.view = session.spawns.view;
            var range = session.vehicle.GetComponent<AttackRangeIndicator>();
            manager.ringMaterial = range ? range.material : null;
            manager.ground = range ? range.ground : null;
            manager.labelFont = session.hud.statsText.font;
            manager.enabled = true;
            session.respawns = manager;
            if (!manager.view || !manager.ringMaterial || !manager.labelFont || !manager.ground)
                throw new InvalidOperationException("Respawn markers require the saved camera, ground, ring material and HUD font.");
            EditorUtility.SetDirty(manager); EditorUtility.SetDirty(session);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("ENEMY_RESPAWN_INSTALL_OK: death-site timers wired; delay=" + manager.respawnDelaySeconds +
                "s; visual assets and navigation retained.");
        }
    }
}
