using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Batch screenshot pass of the saved arena for visual review. Run without -quit:
    /// -executeMethod RoadGunner.Editor.VisualCaptureRunner.Run [-rgCaptureLabel name] [-rgCaptureSize 720x1280] [-rgCaptureShots a,b]
    /// PNGs and CAPTURES.txt are written to outputs/visual-capture/&lt;label&gt;/. The scene is never saved.
    /// </summary>
    [InitializeOnLoad]
    public static class VisualCaptureRunner
    {
        const string Prefix = "RoadGunner.VisualCapture.";
        static VisualCaptureShots shots;
        static bool finishing;

        static VisualCaptureRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Prefix + "Pending", false))
            {
                Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
                if (EditorApplication.isPlaying) EditorApplication.delayCall += StartShots;
            }
        }

        [MenuItem("Tools/Road Gunner/Validate/Capture Visual Review Shots")]
        public static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
                throw new InvalidOperationException("Run visual capture from compiled Edit Mode.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            string label = Argument("-rgCaptureLabel", "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            string size = Argument("-rgCaptureSize", "720x1280");
            string filter = Argument("-rgCaptureShots", "");
            EditorSceneManager.OpenScene(scene.path);
            string folder = VisualCaptureShots.OutputRoot + "/" + label;
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/CAPTURES.txt", "Preparing visual capture " + label + "\n");
            UnityEditor.SessionState.SetBool(Prefix + "Pending", true); UnityEditor.SessionState.SetBool(Prefix + "Started", false);
            UnityEditor.SessionState.SetBool(Prefix + "Batch", Application.isBatchMode);
            UnityEditor.SessionState.SetString(Prefix + "Folder", folder);
            UnityEditor.SessionState.SetString(Prefix + "Size", size);
            UnityEditor.SessionState.SetString(Prefix + "Filter", filter);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetString(Prefix + "Deadline", DateTime.UtcNow.AddMinutes(8).Ticks.ToString());
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.runInBackground = true;
            // New shaders must be compiled before a frame is captured, otherwise they render as placeholders.
            typeof(ShaderUtil).GetProperty("allowAsyncCompilation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, false);
            ParseSize(size, out int w, out int h);
            PrototypeBuilder.SetPortraitGameView(w, h);
            Debug.Log("Visual capture entering " + scene.path + " -> " + folder);
            EditorApplication.isPlaying = true;
        }

        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        public static void ParseSize(string size, out int width, out int height)
        {
            width = 720; height = 1280;
            var parts = (size ?? "").Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 64 && h > 64) { width = w; height = h; }
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartShots;
            if (state == PlayModeStateChange.EnteredEditMode && UnityEditor.SessionState.GetBool(Prefix + "Started", false)) Finish();
        }

        static void StartShots()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || !EditorApplication.isPlaying || UnityEditor.SessionState.GetBool(Prefix + "Started", false)) return;
            var session = UnityEngine.Object.FindAnyObjectByType<GameSession>();
            if (!session) { Fail("No GameSession in the first enabled build scene."); return; }
            shots = session.GetComponent<VisualCaptureShots>();
            if (!shots) shots = session.gameObject.AddComponent<VisualCaptureShots>();
            ParseSize(UnityEditor.SessionState.GetString(Prefix + "Size", "720x1280"), out int w, out int h);
            UnityEditor.SessionState.SetBool(Prefix + "Started", true);
            shots.Begin(UnityEditor.SessionState.GetString(Prefix + "Folder", VisualCaptureShots.OutputRoot + "/unnamed"), w, h,
                UnityEditor.SessionState.GetString(Prefix + "Filter", ""));
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            UnityEditor.SessionState.SetInt(Prefix + "Errors", UnityEditor.SessionState.GetInt(Prefix + "Errors", 0) + 1);
            string folder = UnityEditor.SessionState.GetString(Prefix + "Folder", "");
            if (folder.Length > 0) File.AppendAllText(folder + "/CAPTURES.txt", "CONSOLE " + type + ": " + message + "\n" + stack + "\n");
        }

        static void Tick()
        {
            if (!UnityEditor.SessionState.GetBool(Prefix + "Pending", false) || finishing) return;
            if (long.TryParse(UnityEditor.SessionState.GetString(Prefix + "Deadline", "0"), out long ticks) && DateTime.UtcNow.Ticks > ticks)
            { Fail("Visual capture exceeded the eight minute timeout."); return; }
            if (EditorApplication.isPlaying && !UnityEditor.SessionState.GetBool(Prefix + "Started", false)) { StartShots(); return; }
            if (!EditorApplication.isPlaying || !shots || !shots.Completed) return;
            int errors = UnityEditor.SessionState.GetInt(Prefix + "Errors", 0);
            UnityEditor.SessionState.SetInt(Prefix + "Exit", shots.Failures == 0 && errors == 0 ? 0 : 1);
            string folder = UnityEditor.SessionState.GetString(Prefix + "Folder", "");
            File.AppendAllText(folder + "/CAPTURES.txt", "\nRESULT shots=" + shots.Captured + " failures=" + shots.Failures + " console errors=" + errors + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }

        static void Fail(string message)
        {
            UnityEditor.SessionState.SetInt(Prefix + "Exit", 1); UnityEditor.SessionState.SetBool(Prefix + "Started", true);
            string folder = UnityEditor.SessionState.GetString(Prefix + "Folder", "");
            if (folder.Length > 0) File.AppendAllText(folder + "/CAPTURES.txt", "\nFAIL capture runner: " + message + "\n");
            finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Finish();
        }

        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(Prefix + "Exit", 1); bool exit = UnityEditor.SessionState.GetBool(Prefix + "Batch", false);
            UnityEditor.SessionState.SetBool(Prefix + "Pending", false);
            Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; finishing = false;
            Debug.Log("Visual capture finished: exit=" + code + "; " + UnityEditor.SessionState.GetString(Prefix + "Folder", ""));
            if (exit) EditorApplication.Exit(code);
        }
    }
}
