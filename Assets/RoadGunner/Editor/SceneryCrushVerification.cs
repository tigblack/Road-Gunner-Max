using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Run with -executeMethod RoadGunner.Editor.SceneryCrushVerification.Run, without -quit.</summary>
    [InitializeOnLoad]
    public static class SceneryCrushVerification
    {
        const string Prefix = "RoadGunner.SceneryCrushVerification.";
        static SceneryCrushPlayChecks checks;
        static bool finishing;
        static readonly List<string> errors = new List<string>();

        static SceneryCrushVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Prefix + "Pending", false))
            {
                Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
                if (EditorApplication.isPlaying) EditorApplication.delayCall += StartChecks;
            }
        }

        [MenuItem("Tools/Road Gunner/Validate/Run Scenery Crush Acceptance Checks")]
        public static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                throw new InvalidOperationException("Run scenery crush checks from compiled Edit Mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before running scenery crush checks.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            EditorSceneManager.OpenScene(scene.path);
            Directory.CreateDirectory(Path.GetDirectoryName(SceneryCrushPlayChecks.ReportPath));
            File.WriteAllText(SceneryCrushPlayChecks.ReportPath, "Preparing saved-scene scenery crush verification...\n");
            errors.Clear(); finishing = false;
            UnityEditor.SessionState.SetBool(Prefix + "Pending", true); UnityEditor.SessionState.SetBool(Prefix + "Started", false);
            UnityEditor.SessionState.SetBool(Prefix + "Batch", Application.isBatchMode);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetString(Prefix + "Deadline", DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.runInBackground = true;
            PrototypeBuilder.SetPortraitGameView(540, 960);
            Debug.Log("Scenery crush verification entering saved scene " + scene.path);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartChecks;
            if (state == PlayModeStateChange.EnteredEditMode && UnityEditor.SessionState.GetBool(Prefix + "Started", false)) Finish();
        }
        static void StartChecks()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || !EditorApplication.isPlaying || UnityEditor.SessionState.GetBool(Prefix + "Started", false)) return;
            var session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
            if (!session) { Fail("No GameSession in the first enabled build scene."); return; }
            checks = session.GetComponent<SceneryCrushPlayChecks>();
            if (!checks) checks = session.gameObject.AddComponent<SceneryCrushPlayChecks>();
            UnityEditor.SessionState.SetBool(Prefix + "Started", true); checks.Begin();
        }
        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            UnityEditor.SessionState.SetInt(Prefix + "Errors", UnityEditor.SessionState.GetInt(Prefix + "Errors", 0) + 1);
            errors.Add(type + ": " + message + "\n" + stack);
        }
        static void Tick()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || finishing) return;
            if (long.TryParse(UnityEditor.SessionState.GetString(Prefix + "Deadline", "0"), out long ticks) && DateTime.UtcNow.Ticks > ticks)
            { Fail("Scenery crush checks exceeded the twelve minute timeout."); return; }
            if (EditorApplication.isPlaying && !UnityEditor.SessionState.GetBool(Prefix + "Started", false)) { StartChecks(); return; }
            if (!EditorApplication.isPlaying || !checks || !checks.Completed) return;
            int count = UnityEditor.SessionState.GetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", checks.Failures == 0 && count == 0 ? 0 : 1);
            File.AppendAllText(SceneryCrushPlayChecks.ReportPath, "\n" + (count == 0 ? "PASS" : "FAIL") +
                " runtime Console: errors/exceptions/asserts=" + count + "\n" + string.Join("\n", errors) + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }
        static void Fail(string message)
        {
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetBool(Prefix + "Started", true);
            File.AppendAllText(SceneryCrushPlayChecks.ReportPath, "\nFAIL verification runner: " + message + "\n");
            finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Finish();
        }
        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(Prefix + "Exit", 1); bool exit = UnityEditor.SessionState.GetBool(Prefix + "Batch", false);
            UnityEditor.SessionState.SetBool(Prefix + "Pending", false);
            Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; finishing = false;
            Debug.Log("Scenery crush verification finished: exit=" + code + "; " + SceneryCrushPlayChecks.ReportPath);
            if (exit) EditorApplication.Exit(code);
        }
    }
}
