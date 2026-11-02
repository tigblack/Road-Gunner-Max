using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Creates and wires the presentation hub used by the arcade audio, VFX and HUD passes.</summary>
    public static class ArcadeFeedbackSetup
    {
        public static ArcadeFeedback Ensure(GameSession session)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before applying the arcade passes.");
            if (!session) throw new InvalidOperationException("No GameSession for the arcade feedback hub.");
            if (!session.effects) throw new InvalidOperationException("GameSession has no CombatEffects to host the arcade feedback hub.");
            var hub = session.effects.GetComponent<ArcadeFeedback>();
            if (!hub) hub = Undo.AddComponent<ArcadeFeedback>(session.effects.gameObject);
            hub.session = session;
            hub.sinks.RemoveAll(s => !s);
            session.effects.feedback = hub;
            EditorUtility.SetDirty(hub); EditorUtility.SetDirty(session.effects);
            return hub;
        }

        /// <summary>Registers a sink that does not live under the hub (for example HUD juice on the canvas).</summary>
        public static void Register(GameSession session, ArcadeFeedbackSink sink)
        {
            var hub = Ensure(session);
            if (sink && !hub.sinks.Contains(sink)) hub.sinks.Add(sink);
            EditorUtility.SetDirty(hub);
        }

        public static GameSession OpenGameplayScene()
        {
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before applying the arcade passes.");
            if (EditorSceneManager.GetActiveScene().path != scene.path)
            {
                var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scene.path);
                if (loaded.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(loaded);
                else
                {
                    // Interactive use must never silently discard unsaved work in the open scenes.
                    if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        throw new OperationCanceledException("Arcade pass cancelled: the open scenes were not saved.");
                    EditorSceneManager.OpenScene(scene.path);
                }
            }
            var session = UnityEngine.Object.FindAnyObjectByType<GameSession>(FindObjectsInactive.Include);
            if (!session) throw new InvalidOperationException("No GameSession in " + scene.path);
            return session;
        }

        public static void Save()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>Runs every arcade presentation pass in order: Visual, VFX, HUD, Audio. Apply after all legacy installers.</summary>
    public static class ArcadeOverhaulInstaller
    {
        static readonly string[] Passes = { "ArcadeVisualInstaller", "ArcadeVfxInstaller", "ArcadeHudStyleInstaller", "ArcadeAudioInstaller" };

        [MenuItem("Tools/Road Gunner/Arcade/Apply Full Arcade Overhaul")]
        public static void ApplyMenu() { ApplyBatch(); }

        public static void ApplyBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            ArcadeFeedbackSetup.Ensure(session);
            foreach (string name in Passes)
            {
                var type = typeof(ArcadeOverhaulInstaller).Assembly.GetType("RoadGunner.Editor." + name);
                var apply = type?.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameSession) }, null);
                if (apply == null) { Debug.LogWarning("ARCADE overhaul: pass " + name + " not present yet; skipped."); continue; }
                try { apply.Invoke(null, new object[] { session }); }
                catch (TargetInvocationException error) { throw new InvalidOperationException("ARCADE overhaul pass " + name + " failed", error.InnerException); }
                Debug.Log("ARCADE overhaul: applied " + name);
            }
            ArcadeFeedbackSetup.Save();
            Debug.Log("ARCADE OVERHAUL OK");
        }
    }
}
