using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Run with -executeMethod RoadGunner.Editor.StoneContactVerification.Run, without -quit.</summary>
    [InitializeOnLoad]
    public static class StoneContactVerification
    {
        const string Prefix = "RoadGunner.StoneContactVerification.";
        static StoneContactPlayChecks checks;
        static bool finishing;
        static readonly List<string> errors = new List<string>();

        static StoneContactVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Prefix + "Pending", false))
            {
                Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
                if (EditorApplication.isPlaying) EditorApplication.delayCall += StartChecks;
            }
        }

        [MenuItem("Tools/Road Gunner/Validate/Run Saved World Stone Contact Checks")]
        public static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                throw new InvalidOperationException("Run saved-world stone contact checks from compiled Edit Mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before running saved-world stone contact checks.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            EditorSceneManager.OpenScene(scene.path);
            Directory.CreateDirectory(Path.GetDirectoryName(StoneContactPlayChecks.ReportPath));
            File.WriteAllText(StoneContactPlayChecks.ReportPath, "Preparing saved-scene saved-world stone contact verification...\n");
            errors.Clear(); finishing = false;
            UnityEditor.SessionState.SetBool(Prefix + "Pending", true); UnityEditor.SessionState.SetBool(Prefix + "Started", false);
            UnityEditor.SessionState.SetBool(Prefix + "Batch", Application.isBatchMode);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetString(Prefix + "Deadline", DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.runInBackground = true;
            PrototypeBuilder.SetPortraitGameView(540, 960);
            Debug.Log("Saved-world stone contact verification entering saved scene " + scene.path);
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
            checks = session.GetComponent<StoneContactPlayChecks>();
            if (!checks) checks = session.gameObject.AddComponent<StoneContactPlayChecks>();
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
            { Fail("Saved-world stone contact checks exceeded the twelve minute timeout."); return; }
            if (EditorApplication.isPlaying && !UnityEditor.SessionState.GetBool(Prefix + "Started", false)) { StartChecks(); return; }
            if (!EditorApplication.isPlaying || !checks || !checks.Completed) return;
            int count = UnityEditor.SessionState.GetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", checks.Failures == 0 && count == 0 ? 0 : 1);
            File.AppendAllText(StoneContactPlayChecks.ReportPath, "\n" + (count == 0 ? "PASS" : "FAIL") +
                " runtime Console: errors/exceptions/asserts=" + count + "\n" + string.Join("\n", errors) + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }
        static void Fail(string message)
        {
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetBool(Prefix + "Started", true);
            File.AppendAllText(StoneContactPlayChecks.ReportPath, "\nFAIL verification runner: " + message + "\n");
            finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Finish();
        }
        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(Prefix + "Exit", 1); bool exit = UnityEditor.SessionState.GetBool(Prefix + "Batch", false);
            UnityEditor.SessionState.SetBool(Prefix + "Pending", false);
            Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; finishing = false;
            Debug.Log("Saved-world stone contact verification finished: exit=" + code + "; " + StoneContactPlayChecks.ReportPath);
            if (exit) EditorApplication.Exit(code);
        }
    }
}
