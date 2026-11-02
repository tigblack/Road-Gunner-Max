using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// RENDER stream of the arcade overhaul ("Sunny Wasteland Arcade"): Arcade Toon materials + world/enemy/mutant
    /// palette atlases (enemy prefabs re-skinned team red, player crew untouched), sun/fill/ambient/fog tuned so lit
    /// ground lands on its albedo, a mild unclipped post grade, violet full-screen ink outline on both URP renderers
    /// (SSAO off), single-cascade crisp shadows, workshop/station restyle, arcade ground texture + minimap rebake and
    /// collider-free dressing. Idempotent; run after all legacy installers.
    /// </summary>
    public static class ArcadeVisualInstaller
    {
        public const string Root = "Assets/RoadGunner/Art/Arcade";
        public const string ProfilePath = Root + "/Settings/Arcade Sunny Grade.asset";
        public const string OutlineMaterialPath = Root + "/Materials/Arcade Outline.mat";
        public const string OutlineFeatureName = "Arcade ink outline";
        public const string VolumeName = "Scrap Rush • warm cartoon grading";
        static readonly string[] RendererPaths = { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" };

        // Shared look (gamma-space colours, as typed in the inspector). Linear light budget for flat lit ground:
        // sun 0.63 * FFF1D6 + sky ambient 93A1BA + fill ~= 0.95 neutral, so lit sand renders at its albedo (no clipping).
        // In shadow the sun is multiplied by ShadeColor: overall ~(0.60, 0.51, 0.80) of lit -> shadowed sand ~#C19273,
        // lilac stone ~#A090B3 after the shaders' saturation cap. Warm-violet, never black or purple slabs.
        public static readonly Color ShadeColor = Hex("B080CC");
        public static readonly Color Haze = Hex("F4DDB6");
        static readonly Color SunColor = Hex("FFF1D6");
        static readonly Vector3 SunEuler = new Vector3(60, -20, 0);
        const float SunIntensity = .63f;
        static readonly string[] PipelinePaths = { "Assets/Settings/Mobile_RPAsset.asset", "Assets/Settings/PC_RPAsset.asset" };

        static readonly List<string> log = new List<string>();
        public static IReadOnlyList<string> LastLog => log;

        public static Color Hex(string hex, float alpha = 1)
        {
            if (!ColorUtility.TryParseHtmlString("#" + hex, out var c)) throw new ArgumentException("Bad colour " + hex);
            c.a = alpha; return c;
        }

        [MenuItem("Tools/Road Gunner/Arcade/Apply Arcade Visuals (Render)")]
        public static void ApplyMenu() { ApplyBatch(); }

        public static void ApplyBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            Apply(session);
            ArcadeFeedbackSetup.Save();
            Debug.Log("ARCADE VISUAL OK\n" + string.Join("\n", log));
        }

        public static void Apply(GameSession session)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before applying arcade visuals.");
            if (!session) throw new InvalidOperationException("ArcadeVisualInstaller needs the GameSession.");
            ArcadeFeedbackSetup.Ensure(session);
            log.Clear();
            EnsureFolders();
            var toon = RequireShader(ArcadeVisualPalette.ToonShaderName);
            RequireShader("RoadGunner/Arcade Outline");
            RequireShader("RoadGunner/Concept Desert");
            var scene = session.gameObject.scene;

            var atlases = ArcadeVisualPalette.WriteAtlases();
            ArcadeVisualPalette.ConvertScene(scene, toon, log);
            var palettes = ArcadeVisualPalette.ApplyPalette(toon, atlases, log);
            ArcadeVisualPalette.AssignActorPalettes(scene, palettes.world, palettes.enemy, palettes.mutant, log);
            ConfigureLighting(session);
            ConfigureShadows();
            ConfigurePost();
            ConfigureRenderers(OutlineMaterial());
            ArcadeVisualSites.Apply(session, toon, log);
            ArcadeVisualGround.Apply(session, log);
            ArcadeVisualDressing.Apply(session, log);
            ArcadeVisualGround.BakeMinimap(session, log);
            log.Add(ProbeMagenta(session));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static Shader RequireShader(string name)
        {
            var shader = Shader.Find(name);
            if (!shader) throw new InvalidOperationException("Shader '" + name + "' is missing.");
            if (ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader '" + name + "' has errors: " +
                    string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message + " @" + m.line)));
            return shader;
        }

        static void EnsureFolders()
        {
            foreach (string folder in new[] { "Materials", "Textures", "Settings", "Meshes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder))
                {
                    if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/RoadGunner/Art", "Arcade");
                    AssetDatabase.CreateFolder(Root, folder);
                }
        }

        static void Record(Object value)
        {
            if (!value) return;
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        // ---------- lighting ----------

        static void ConfigureLighting(GameSession session)
        {
            var directional = RoadGunnerLevelAuthoring.InScene<Light>().Where(l => l && l.type == LightType.Directional).ToArray();
            var sun = RenderSettings.sun && directional.Contains(RenderSettings.sun) ? RenderSettings.sun
                : directional.FirstOrDefault(l => l.name.IndexOf("Sun", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? directional.OrderByDescending(l => l.intensity).FirstOrDefault();
            if (!sun) throw new InvalidOperationException("The gameplay scene has no directional sun light.");
            sun.color = SunColor; sun.intensity = SunIntensity; sun.transform.rotation = Quaternion.Euler(SunEuler);
            // Full-strength shadow map; the shaders step it crisply and tint shadowed areas with ShadeColor. A higher
            // sun (60 deg) keeps cliff/wall shadows short so off-screen geometry does not throw big slabs into view.
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 1; sun.shadowBias = .4f; sun.shadowNormalBias = .4f;
            var sunData = sun.GetUniversalAdditionalLightData();
            if (sunData) { sunData.usePipelineSettings = false; Record(sunData); }
            RenderSettings.sun = sun; Record(sun); Record(sun.transform);
            foreach (var fill in directional.Where(l => l != sun))
            {
                fill.color = Hex("C7D8F2"); fill.intensity = .1f; fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(22, 145, 0); Record(fill); Record(fill.transform);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("93A1BA");
            RenderSettings.ambientEquatorColor = Hex("8A93A6");
            RenderSettings.ambientGroundColor = Hex("7D7480");
            RenderSettings.ambientIntensity = 1;
            // Ortho camera at 35 m: ground spans ~22 m (bottom) to ~48 m (top) view depth -> gentle haze toward the top.
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Haze;
            RenderSettings.fogStartDistance = 40; RenderSettings.fogEndDistance = 140;
            var camera = session.input && session.input.view ? session.input.view : Camera.main;
            if (camera) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Haze; Record(camera); }
            DynamicGI.UpdateEnvironment();
            log.Add("Lighting: sun '" + sun.name + "' " + SunIntensity + " @" + SunEuler + ", " + (directional.Length - 1) + " fill; trilight ambient; linear haze 40-140 m; camera clear = haze.");
        }

        // ---------- shadows (both pipeline assets) ----------

        // The ortho follow camera sees ~22-48 m of view depth, so the 2-cascade split (first cascade 0-15 m) wasted
        // half the shadow map; one cascade over 60 m gives crisp, stable toon shadows on WebGL/mobile and PC.
        static void ConfigureShadows()
        {
            int configured = 0;
            foreach (string path in PipelinePaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!asset) { log.Add("Pipeline asset missing: " + path); continue; }
                // URP fades by straight-line distance to the camera; the farthest visible ground is ~52 m away, so the
                // fade must start beyond it (60 m with a 10% border starts at 54 m) or a soft edge shows at the top.
                asset.shadowDistance = 60; asset.shadowCascadeCount = 1; asset.cascadeBorder = .1f;
                asset.shadowDepthBias = .4f; asset.shadowNormalBias = .4f;
                EditorUtility.SetDirty(asset); configured++;
            }
            log.Add("Shadows: " + configured + " pipeline assets -> distance 60 m (fade from 54 m), 1 cascade, bias 0.4/0.4; sun bias 0.4/0.4.");
        }

        // ---------- post ----------

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                component = profile.Add<T>(false);
                component.name = typeof(T).Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true; EditorUtility.SetDirty(component);
            return component;
        }

        static void ConfigurePost()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = "Arcade Sunny Grade"; AssetDatabase.CreateAsset(profile, ProfilePath); }
            profile.components.RemoveAll(c => !c);

            Override<Tonemapping>(profile).mode.Override(TonemappingMode.None); // required: magenta probe and LDR palette
            var bloom = Override<Bloom>(profile);
            // Sand (max channel ~0.87 linear) never reaches the 1.1 threshold: only HDR VFX, emissive lights and flashes glow.
            bloom.threshold.Override(1.1f); bloom.intensity.Override(.45f); bloom.scatter.Override(.6f);
            bloom.tint.Override(new Color(1f, .95f, .86f)); bloom.clamp.Override(32f); bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Half); bloom.dirtIntensity.Override(0);
            // Mild grade: the palette is authored at its targets, the grade only adds a little punch. Linear-space
            // saturation above ~+3 crushes the turquoise red channel (round 1: #00BBA9), so colour lives in the albedo.
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(0f); color.contrast.Override(4f); color.saturation.Override(2f);
            color.hueShift.Override(0); color.colorFilter.Override(Color.white);
            var vignette = Override<Vignette>(profile);
            vignette.color.Override(Hex("5E4660")); vignette.center.Override(new Vector2(.5f, .5f));
            vignette.intensity.Override(.16f); vignette.smoothness.Override(.45f); vignette.rounded.Override(false);
            var split = Override<ShadowsMidtonesHighlights>(profile);
            split.shadows.Override(new Vector4(1f, .985f, 1.02f, 0)); split.midtones.Override(new Vector4(1, 1, 1, 0));
            split.highlights.Override(new Vector4(.98f, .98f, .975f, 0)); // highlight gain < 1: cream stripes/ring stay below clip
            var balance = Override<WhiteBalance>(profile);
            balance.temperature.Override(0f); balance.tint.Override(0);
            EditorUtility.SetDirty(profile);

            var volumes = RoadGunnerLevelAuthoring.InScene<Volume>().Where(v => v).ToArray();
            var volume = volumes.FirstOrDefault(v => v.name == VolumeName) ?? volumes.FirstOrDefault(v => v.isGlobal);
            if (!volume) volume = new GameObject(VolumeName).AddComponent<Volume>();
            volume.sharedProfile = profile; volume.isGlobal = true; volume.priority = 10; volume.weight = 1; volume.enabled = true;
            Record(volume);
            log.Add("Post: '" + volume.name + "' -> " + ProfilePath + " (Tonemapping None, bloom 1.1/0.45/0.6, sat +2, contrast +4, highlight gain 0.98, violet vignette 0.16).");
        }

        // ---------- outline + renderer features ----------

        static Material OutlineMaterial()
        {
            var shader = RequireShader("RoadGunner/Arcade Outline");
            var m = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (!m) { m = new Material(shader) { name = "Arcade Outline" }; AssetDatabase.CreateAsset(m, OutlineMaterialPath); }
            m.shader = shader;
            m.SetColor("_OutlineColor", Hex("2B2238", .9f));
            // px at a 1080 px short side: ~2.2 px at 720p, scales with resolution; flat ground never trips the depth test.
            m.SetFloat("_Thickness", 3.3f); m.SetFloat("_DepthThreshold", .28f); m.SetFloat("_DepthSoftness", .3f);
            m.SetVector("_FadeDistance", new Vector4(40, 110, .5f, 0));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ConfigureRenderers(Material outline)
        {
            foreach (string path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!data) { log.Add("Renderer missing: " + path); continue; }
                data.rendererFeatures.RemoveAll(f => !f);
                // SSAO's soft gradients fight the two-band cel ramp and cost a depth/normal pass + blur on mobile;
                // the ink outline and coloured stepped shadows now carry contact definition.
                int ssaoOff = 0;
                foreach (var feature in data.rendererFeatures)
                    if (feature is ScreenSpaceAmbientOcclusion && feature.isActive) { feature.SetActive(false); EditorUtility.SetDirty(feature); ssaoOff++; }
                var ink = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == OutlineFeatureName);
                if (!ink)
                {
                    ink = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    ink.name = OutlineFeatureName; ink.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(ink, data);
                    data.rendererFeatures.Add(ink);
                }
                ink.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
                ink.fetchColorBuffer = false;                        // hardware blend over the target, no colour copy
                ink.requirements = ScriptableRenderPassInput.Depth;  // makes URP produce _CameraDepthTexture (copy or prepass)
                ink.passMaterial = outline; ink.passIndex = 0; ink.bindDepthStencilAttachment = false;
                ink.SetActive(true);
                EditorUtility.SetDirty(ink); data.SetDirty(); EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                SyncFeatureMap(data);
                log.Add("Renderer " + System.IO.Path.GetFileNameWithoutExtension(path) + ": ink outline (BeforeRenderingTransparents, Depth) on; SSAO disabled x" + ssaoOff + ".");
            }
        }

        // The renderer keeps a parallel list of sub-asset local IDs used to repair missing references.
        static void SyncFeatureMap(ScriptableRendererData data)
        {
            var serialized = new SerializedObject(data);
            var list = serialized.FindProperty("m_RendererFeatures");
            var map = serialized.FindProperty("m_RendererFeatureMap");
            if (list == null || map == null) return;
            map.arraySize = list.arraySize;
            for (int i = 0; i < list.arraySize; i++)
            {
                var feature = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id))
                    map.GetArrayElementAtIndex(i).longValue = id;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        // ---------- self-check: the suites' magenta stencil probe must survive bloom + grading ----------

        static string ProbeMagenta(GameSession session)
        {
            var camera = session.input && session.input.view ? session.input.view : Camera.main;
            var map = session.minimap;
            if (!camera || !map || !map.mapRect) return "Magenta probe skipped (no camera/minimap).";
            var probe = new GameObject("Temporary arcade magenta probe", typeof(RectTransform), typeof(CanvasRenderer));
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var modes = canvases.Select(c => (c.renderMode, c.worldCamera, c.planeDistance)).ToArray();
            const int width = 540, height = 960;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
            try
            {
                var rect = probe.GetComponent<RectTransform>(); rect.SetParent(map.mapRect, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = Vector2.one * 24; rect.anchoredPosition = Vector2.zero;
                var raw = probe.AddComponent<RawImage>(); raw.texture = Texture2D.whiteTexture; raw.color = Color.magenta; raw.raycastTarget = false;
                camera.aspect = (float)width / height; camera.targetTexture = target;
                foreach (var canvas in canvases)
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1); }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                var pixels = readback.GetPixels32();
                int magenta = pixels.Count(c => c.r > 215 && c.g < 40 && c.b > 215);
                int minGreen = 255;
                foreach (var c in pixels) if (c.r > 180 && c.b > 180 && c.g < minGreen) minGreen = c.g;
                return "Magenta probe (edit-mode render through post): " + magenta + " px (suite needs >=4), darkest magenta green=" + minGreen + ".";
            }
            catch (Exception error) { return "Magenta probe failed to render: " + error.Message; }
            finally
            {
                for (int i = 0; i < canvases.Length; i++)
                    if (canvases[i]) { canvases[i].renderMode = modes[i].renderMode; canvases[i].worldCamera = modes[i].worldCamera; canvases[i].planeDistance = modes[i].planeDistance; }
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; camera.ResetAspect(); RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(probe);
            }
        }

        // ---------- shader compile validation for the shipped graphics APIs ----------

        /// <summary>Batch entry: compiles key variants of the arcade shaders for WebGL2 (GLES3) and Metal and logs errors.</summary>
        public static void ValidateShadersBatch()
        {
            var forwardSets = new[]
            {
                new string[0],
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_LOW", "_ADDITIONAL_LIGHTS", "FOG_LINEAR", "INSTANCING_ON" },
                new[] { "_MAIN_LIGHT_SHADOWS", "_SHADOWS_SOFT", "_ADDITIONAL_LIGHTS_VERTEX", "_LIGHT_LAYERS", "_EMISSION", "_METALLICSPECGLOSSMAP" },
                new[] { "_MAIN_LIGHT_SHADOWS_SCREEN", "_CLUSTER_LIGHT_LOOP", "_LIGHT_LAYERS", "_SHADOWS_SOFT_HIGH" },
            };
            int failures = 0, compiled = 0;
            foreach (string name in new[] { ArcadeVisualPalette.ToonShaderName, "RoadGunner/Concept Desert", "RoadGunner/Arcade Outline" })
            {
                var shader = Shader.Find(name);
                if (!shader) { Debug.LogError("ARCADE SHADER missing " + name); failures++; continue; }
                var data = ShaderUtil.GetShaderData(shader);
                for (int s = 0; s < data.SerializedSubshaderCount; s++)
                {
                    var subshader = data.GetSerializedSubshader(s);
                    for (int p = 0; p < subshader.PassCount; p++)
                    {
                        var pass = subshader.GetPass(p);
                        var sets = pass.Name == "ForwardLit" ? forwardSets : new[] { new string[0], new[] { "INSTANCING_ON" } };
                        foreach (var platform in new[] { (UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL), (UnityEditor.Rendering.ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX) })
                            foreach (var keywords in sets)
                                foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                                {
                                    var info = pass.CompileVariant(stage, keywords, platform.Item1, platform.Item2);
                                    compiled++;
                                    if (info.Success) continue;
                                    failures++;
                                    Debug.LogWarning("ARCADE SHADER FAIL " + name + "/" + pass.Name + " " + platform.Item1 + " " + stage + " [" + string.Join(",", keywords) + "]: " +
                                        string.Join(" | ", info.Messages.Select(m => m.message + " (" + m.file + ":" + m.line + ")")));
                                }
                    }
                }
            }
            Debug.Log("ARCADE SHADER VALIDATION compiled=" + compiled + " failures=" + failures);
            if (failures > 0) throw new InvalidOperationException("Arcade shader validation failed: " + failures);
        }
    }
}
