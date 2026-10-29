using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Batch entry: -executeMethod RoadGunner.Editor.Arena1Verification.Run, without -quit.</summary>
    [InitializeOnLoad]
    public static class Arena1Verification
    {
        const string Pending = "RoadGunner.Arena1Verification.Pending";
        const string Started = "RoadGunner.Arena1Verification.Started";
        const string ExitCode = "RoadGunner.Arena1Verification.ExitCode";
        const string ErrorCount = "RoadGunner.Arena1Verification.ErrorCount";
        const string Batch = "RoadGunner.Arena1Verification.Batch";
        const string Timeout = "RoadGunner.Arena1Verification.Timeout";
        static Arena1PlayChecks checks;
        static bool finishing;
        static readonly List<string> errors = new List<string>();

        static Arena1Verification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Pending, false))
            {
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
                if (EditorApplication.isPlaying) EditorApplication.delayCall += StartChecks;
            }
        }

        [MenuItem("Tools/Road Gunner/Validate/Run Arena 1 Acceptance Checks")]
        public static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for the project to compile before Arena 1 verification.");
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Start Arena 1 verification from Edit Mode.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the currently open scene before running acceptance checks.");
            EditorSceneManager.OpenScene(scene.path);
            Directory.CreateDirectory(Path.GetDirectoryName(Arena1PlayChecks.ReportPath));
            File.WriteAllText(Arena1PlayChecks.ReportPath, "Preparing saved Arena 1 Anton feedback verification...\n");
            errors.Clear(); finishing = false;
            UnityEditor.SessionState.SetBool(Pending, true);
            UnityEditor.SessionState.SetBool(Batch, Application.isBatchMode);
            UnityEditor.SessionState.SetBool(Started, false);
            UnityEditor.SessionState.SetInt(ExitCode, 1);
            UnityEditor.SessionState.SetInt(ErrorCount, 0);
            UnityEditor.SessionState.SetString(Timeout, DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.runInBackground = true;
            PrototypeBuilder.SetPortraitGameView(540, 960);
            Debug.Log("Arena 1 verification opening saved scene " + scene.path + "; entering Play Mode.");
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!UnityEditor.SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartChecks;
            if (state == PlayModeStateChange.EnteredEditMode && UnityEditor.SessionState.GetBool(Started, false)) Finish();
        }

        static void StartChecks()
        {
            if (!UnityEditor.SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            var session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
            if (!session) { Fail("No GameSession in the first enabled build scene."); return; }
            checks = session.GetComponent<Arena1PlayChecks>();
            if (!checks) checks = session.gameObject.AddComponent<Arena1PlayChecks>();
            if (!UnityEditor.SessionState.GetBool(Started, false))
            {
                UnityEditor.SessionState.SetBool(Started, true); checks.Begin();
            }
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            UnityEditor.SessionState.SetInt(ErrorCount, UnityEditor.SessionState.GetInt(ErrorCount, 0) + 1);
            errors.Add(type + ": " + message + "\n" + stackTrace);
        }

        static void Tick()
        {
            if (!UnityEditor.SessionState.GetBool(Pending, false) || finishing) return;
            string deadline = UnityEditor.SessionState.GetString(Timeout, "0");
            if (long.TryParse(deadline, out long ticks) && DateTime.UtcNow.Ticks > ticks)
            { Fail("Arena 1 Play Mode checks exceeded the twelve minute timeout."); return; }
            // Disabled domain/scene reload or an editor startup exception can consume delayCall.
            // SessionState still guarantees a single Begin when both entry paths arrive.
            if (EditorApplication.isPlaying && !UnityEditor.SessionState.GetBool(Started, false))
            { StartChecks(); return; }
            if (!EditorApplication.isPlaying || !checks || !checks.Completed) return;
            int runtimeErrors = UnityEditor.SessionState.GetInt(ErrorCount, 0);
            int result = checks.Failures == 0 && runtimeErrors == 0 ? 0 : 1;
            UnityEditor.SessionState.SetInt(ExitCode, result);
            File.AppendAllText(Arena1PlayChecks.ReportPath, "\n" + (runtimeErrors == 0 ? "PASS" : "FAIL") +
                " runtime Console: errors/exceptions/asserts=" + runtimeErrors + "\n" + string.Join("\n", errors) + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }

        static void Fail(string message)
        {
            UnityEditor.SessionState.SetInt(ExitCode, 1);
            UnityEditor.SessionState.SetBool(Started, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Arena1PlayChecks.ReportPath));
            File.AppendAllText(Arena1PlayChecks.ReportPath, "\nFAIL verification runner: " + message + "\n");
            finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Finish();
        }

        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(ExitCode, 1);
            bool exit = UnityEditor.SessionState.GetBool(Batch, false);
            UnityEditor.SessionState.SetBool(Pending, false);
            Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick;
            finishing = false;
            Debug.Log("Arena 1 verification finished: exit=" + code + "; " + Arena1PlayChecks.ReportPath);
            if (exit) EditorApplication.Exit(code);
        }
    }
}
