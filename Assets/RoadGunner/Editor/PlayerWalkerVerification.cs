using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Run with -batchmode -executeMethod RoadGunner.Editor.PlayerWalkerVerification.Run, without -quit/-nographics.</summary>
    [InitializeOnLoad]
    public static class PlayerWalkerVerification
    {
        const string Prefix = "RoadGunner.PlayerWalkerVerification.";
        const string DataPath = "Assets/RoadGunner/Levels/Greenwood/ReferenceArena/Reference layout.asset";
        static PlayerWalkerPlayChecks checks; static bool finishing; static readonly List<string> errors = new List<string>();
        static PlayerWalkerVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Prefix + "Pending", false))
            { Application.logMessageReceived += OnLog; EditorApplication.update += Tick; if (EditorApplication.isPlaying) EditorApplication.delayCall += StartChecks; }
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Walker Visual Acceptance Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Run walker checks from compiled Edit Mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before acceptance.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled); if (scene == null) throw new InvalidOperationException("No enabled gameplay scene.");
            if (!AssetDatabase.LoadAssetAtPath<ReferenceArenaLayoutData>(DataPath)) throw new InvalidOperationException("Install and save the reference layout before acceptance: " + DataPath);
            EditorSceneManager.OpenScene(scene.path);
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerWalkerPlayChecks.ReportPath));
            File.WriteAllText(PlayerWalkerPlayChecks.ReportPath, "Preparing walker appearance and footprint checks...\n");
            finishing = false; errors.Clear(); UnityEditor.SessionState.SetBool(Prefix + "Pending", true); UnityEditor.SessionState.SetBool(Prefix + "Started", false);
            UnityEditor.SessionState.SetBool(Prefix + "Batch", Application.isBatchMode); UnityEditor.SessionState.SetInt(Prefix + "Errors", 0); UnityEditor.SessionState.SetInt(Prefix + "Exit", 1);
            UnityEditor.SessionState.SetString(Prefix + "Deadline", DateTime.UtcNow.AddMinutes(12).Ticks.ToString());
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog; EditorApplication.update -= Tick; EditorApplication.update += Tick;
            typeof(ShaderUtil).GetProperty("allowAsyncCompilation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, false);
            Application.runInBackground = true; PrototypeBuilder.SetPortraitGameView(540, 960); EditorApplication.isPlaying = true;
        }
        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartChecks;
            if (state == PlayModeStateChange.EnteredEditMode && UnityEditor.SessionState.GetBool(Prefix + "Started", false)) Finish();
        }
        static void StartChecks()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || UnityEditor.SessionState.GetBool(Prefix + "Started", false) || !EditorApplication.isPlaying) return;
            try
            {
                var session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
                if (!session) throw new InvalidOperationException("Saved gameplay session missing.");
                checks = session.GetComponent<PlayerWalkerPlayChecks>();
                if (!checks) checks = session.gameObject.AddComponent<PlayerWalkerPlayChecks>();
                UnityEditor.SessionState.SetBool(Prefix + "Started", true); checks.Begin();
            }
            catch (Exception e) { Fail(e.ToString()); }
        }
        static Vector3 World(Vector2 w) => ExplorationArenaInstaller.World(w);
        static Vector3 Ground(LevelLayout level, Vector3 p)
        {
            var terrain = level.ground.GetComponent<Collider>();
            if (!terrain.Raycast(new Ray(p + Vector3.up * 100, Vector3.down), out var hit, 250)) throw new InvalidOperationException("No saved terrain below capture position " + p);
            return hit.point + Vector3.up * .8f;
        }
        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            UnityEditor.SessionState.SetInt(Prefix + "Errors", UnityEditor.SessionState.GetInt(Prefix + "Errors", 0) + 1); errors.Add(type + ": " + message + "\n" + stack);
        }
        static void Tick()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || finishing) return;
            if (long.TryParse(UnityEditor.SessionState.GetString(Prefix + "Deadline", "0"), out long deadline) && DateTime.UtcNow.Ticks > deadline) { Fail("Twelve minute timeout."); return; }
            if (EditorApplication.isPlaying && !UnityEditor.SessionState.GetBool(Prefix + "Started", false)) { StartChecks(); return; }
            if (!EditorApplication.isPlaying || !checks || !checks.Completed) return;
            int count = UnityEditor.SessionState.GetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", checks.Failures == 0 && count == 0 ? 0 : 1);
            File.AppendAllText(PlayerWalkerPlayChecks.ReportPath, "\n" + (count == 0 ? "PASS" : "FAIL") + " runtime Console: errors/exceptions/asserts=" + count + "\n" + string.Join("\n", errors) + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }
        static void Fail(string message)
        {
            UnityEditor.SessionState.SetBool(Prefix + "Started", true); UnityEditor.SessionState.SetInt(Prefix + "Exit", 1);
            File.AppendAllText(PlayerWalkerPlayChecks.ReportPath, "\nFAIL runner: " + message + "\n"); finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Finish();
        }
        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(Prefix + "Exit", 1); bool exit = UnityEditor.SessionState.GetBool(Prefix + "Batch", false);
            UnityEditor.SessionState.SetBool(Prefix + "Pending", false); Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; finishing = false;
            Debug.Log("Walker verification finished: exit=" + code + "; " + PlayerWalkerPlayChecks.ReportPath); if (exit) EditorApplication.Exit(code);
        }
    }
}
