using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Run with -batchmode -executeMethod RoadGunner.Editor.ReferenceArenaVerification.Run, without -quit/-nographics.</summary>
    [InitializeOnLoad]
    public static class ReferenceArenaVerification
    {
        const string Prefix = "RoadGunner.ReferenceArenaVerification.";
        const string DataPath = "Assets/RoadGunner/Levels/Greenwood/ReferenceArena/Reference layout.asset";
        static ReferenceArenaPlayChecks checks; static bool finishing; static readonly List<string> errors = new List<string>();
        static ReferenceArenaVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Prefix + "Pending", false))
            { Application.logMessageReceived += OnLog; EditorApplication.update += Tick; if (EditorApplication.isPlaying) EditorApplication.delayCall += StartChecks; }
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Reference Arena Acceptance Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Run reference checks from compiled Edit Mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before acceptance.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled); if (scene == null) throw new InvalidOperationException("No enabled gameplay scene.");
            if (!AssetDatabase.LoadAssetAtPath<ReferenceArenaLayoutData>(DataPath)) throw new InvalidOperationException("Install and save the reference layout before acceptance: " + DataPath);
            EditorSceneManager.OpenScene(scene.path);
            Directory.CreateDirectory(Path.GetDirectoryName(ReferenceArenaPlayChecks.ReportPath));
            File.WriteAllText(ReferenceArenaPlayChecks.ReportPath, "Preparing saved-world reference-arena checks...\n");
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
                var session = UnityEngine.Object.FindAnyObjectByType<GameSession>(); var level = UnityEngine.Object.FindAnyObjectByType<LevelLayout>();
                var data = AssetDatabase.LoadAssetAtPath<ReferenceArenaLayoutData>(DataPath);
                if (!session || !level || !data || !data.geometry) throw new InvalidOperationException("Saved reference scene/data binding missing.");
                var root = level.authoredContentRoot; var routes = root.Find("Arena exploration/Routes"); var gates = root.Find("Reference arena/Closed gates");
                var road = root.Find("Arena exploration/Physical connected roads and combat pockets");
                if (!routes || !gates || !road) throw new InvalidOperationException("Missing authored routes, closed gates or real connected road.");
                var specification = new ReferenceArenaPlayChecks.Specification {
                    WidthScale = data.widthScale, Junction = World(data.crossW), East = (World(Vector2.right) - World(Vector2.zero)).normalized,
                    North = (World(Vector2.up) - World(Vector2.zero)).normalized,
                    WorkshopForward = (World(Vector2.up) - World(Vector2.zero)).normalized,
                    Ground = level.ground.GetComponent<Collider>(), Road = road.GetComponent<MeshCollider>(),
                    Routes = data.geometry.routes.Select(r => {
                        var node = routes.Find(r.id); if (!node || r.nodesW == null || r.nodesW.Length < 2) throw new InvalidOperationException("Missing saved route " + r.id);
                        return new ReferenceArenaPlayChecks.RouteSpec { Id = r.id, Width = r.widthMeters,
                            Points = Enumerable.Range(0, node.childCount).Select(i => node.GetChild(i).position).ToArray(),
                            AuthoredStart = World(r.nodesW[0]), AuthoredEnd = World(r.nodesW[r.nodesW.Length - 1]) };
                    }).ToArray(),
                    Footprint = new[] { World(data.geometry.minW), World(new Vector2(data.geometry.minW.x, data.geometry.maxW.y)),
                        World(data.geometry.maxW), World(new Vector2(data.geometry.maxW.x, data.geometry.minW.y)) },
                    Landmarks = new[] {
                        new ReferenceArenaPlayChecks.LandmarkSpec { Name = "start", Position = session.startPosition },
                        new ReferenceArenaPlayChecks.LandmarkSpec { Name = "crossroads", Position = Ground(level, World(data.crossW)) },
                        new ReferenceArenaPlayChecks.LandmarkSpec { Name = "repair", Position = Ground(level, World(data.workshopW)) },
                        new ReferenceArenaPlayChecks.LandmarkSpec { Name = "northeast-camp", Position = Ground(level, World(data.northeastCampW - Vector2.up * 4)) },
                        new ReferenceArenaPlayChecks.LandmarkSpec { Name = "boss-northwest", Position = Ground(level, World(data.bossW - Vector2.up * 4)) }
                    }
                };
                string[] names = { "Gate West", "Gate East", "Gate North" };
                if (data.gatesW == null || data.gatesW.Length != names.Length) throw new InvalidOperationException("Expected three closed authored exits.");
                specification.Gates = names.Select((name, i) => {
                    var gate = gates.Find(name); var inside = gate ? gate.Find("Inside approach") : null;
                    if (!gate || !inside) throw new InvalidOperationException("Missing saved closed gate/approach " + name);
                    return new ReferenceArenaPlayChecks.GateSpec { Name = name, Root = gate, Position = gate.position, Expected = World(data.gatesW[i]),
                        Inside = inside.position, Colliders = gate.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray() };
                }).ToArray();
                checks = session.GetComponent<ReferenceArenaPlayChecks>(); if (!checks) checks = session.gameObject.AddComponent<ReferenceArenaPlayChecks>();
                UnityEditor.SessionState.SetBool(Prefix + "Started", true); checks.Begin(specification);
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
            File.AppendAllText(ReferenceArenaPlayChecks.ReportPath, "\n" + (count == 0 ? "PASS" : "FAIL") + " runtime Console: errors/exceptions/asserts=" + count + "\n" + string.Join("\n", errors) + "\n");
            finishing = true; EditorApplication.isPlaying = false;
        }
        static void Fail(string message)
        {
            UnityEditor.SessionState.SetBool(Prefix + "Started", true); UnityEditor.SessionState.SetInt(Prefix + "Exit", 1);
            File.AppendAllText(ReferenceArenaPlayChecks.ReportPath, "\nFAIL runner: " + message + "\n"); finishing = true;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Finish();
        }
        static void Finish()
        {
            int code = UnityEditor.SessionState.GetInt(Prefix + "Exit", 1); bool exit = UnityEditor.SessionState.GetBool(Prefix + "Batch", false);
            UnityEditor.SessionState.SetBool(Prefix + "Pending", false); Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; finishing = false;
            Debug.Log("Reference arena verification finished: exit=" + code + "; " + ReferenceArenaPlayChecks.ReportPath); if (exit) EditorApplication.Exit(code);
        }
    }
}
