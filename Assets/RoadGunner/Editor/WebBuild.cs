using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoadGunner.EditorTools
{
    public static class WebBuild
    {
        public const string Output = "WebBuild";
        public const string ArenaScene = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";

        [MenuItem("Tools/Road Gunner/Web/Configure Mobile Web")]
        public static void Configure()
        {
            // App UI is an editor-tool dependency (the project already marks its settings editorOnly).
            // Its mobile EnhancedTouch initializer must not run in this UGUI game player.
            const string appUiEditorOnly = "APP_UI_EDITOR_ONLY";
            string webDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            if (Array.IndexOf(webDefines.Split(';'), appUiEditorOnly) < 0)
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,
                    string.IsNullOrEmpty(webDefines) ? appUiEditorOnly : webDefines + ";" + appUiEditorOnly);
            PlayerSettings.companyName = "Road Gunner";
            PlayerSettings.productName = "Road Gunner";
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.template = "PROJECT:RoadGunner";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.WebGL.maximumMemorySize = 512;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ArenaScene, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Road Gunner/Web/Build Mobile Web")]
        public static void Build()
        {
            Configure();
            Directory.CreateDirectory(Output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ArenaScene },
                locationPathName = Output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Road Gunner Web build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Output, ".nojekyll"), "");
            File.WriteAllText(Path.Combine(Output, "build-info.json"), JsonUtility.ToJson(new BuildInfo
            {
                unityVersion = Application.unityVersion,
                scene = ArenaScene,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                compression = "Gzip with JavaScript decompression fallback",
                bytes = report.summary.totalSize
            }, true));
            Debug.Log("ROAD_GUNNER_WEB_BUILD_OK " + report.summary.totalSize + " bytes at " + Path.GetFullPath(Output));
        }

        [Serializable]
        sealed class BuildInfo
        {
            public string unityVersion, scene, builtAtUtc, compression;
            public ulong bytes;
        }
    }
}
