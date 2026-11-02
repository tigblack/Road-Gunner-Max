using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Arcade palette atlases (same 8x4 cell layout and key order as ConceptEnemies.Palette) and the
    /// URP/Lit → RoadGunner/Arcade Toon conversion of every opaque material the gameplay scene renders.
    /// The world/player atlas lives on the shared palette material; enemies (team red) and the Ashmaw mutant get
    /// their own atlas + material with the same UV layout, assigned to their prefabs and scene instances, so the
    /// player's crew keeps its colours.
    /// </summary>
    public static class ArcadeVisualPalette
    {
        public const string ToonShaderName = "RoadGunner/Arcade Toon";
        const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string AtlasPath = ArcadeVisualInstaller.Root + "/Textures/Arcade palette.png";
        public const string ResponsePath = ArcadeVisualInstaller.Root + "/Textures/Arcade palette response.png";
        public const string EmissionPath = ArcadeVisualInstaller.Root + "/Textures/Arcade palette emission.png";
        public const string EnemyAtlasPath = ArcadeVisualInstaller.Root + "/Textures/Arcade enemy palette.png";
        public const string EnemyEmissionPath = ArcadeVisualInstaller.Root + "/Textures/Arcade enemy palette emission.png";
        public const string MutantAtlasPath = ArcadeVisualInstaller.Root + "/Textures/Arcade mutant palette.png";
        public const string MutantEmissionPath = ArcadeVisualInstaller.Root + "/Textures/Arcade mutant palette emission.png";
        public const string EnemyMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade enemy palette.mat";
        public const string MutantMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade mutant palette.mat";
        const string Swatches = "Assets/RoadGunner/Art/ConceptWasteland/Materials";
        public const string SharedPalettePath = Swatches + "/Enemy shared palette.mat";
        const float PaletteEmission = 1.6f; // material colours are gamma-space: 1.6 → ~2.8 linear, gold lights bloom

        struct Swatch
        {
            public string key, hex;
            public float metallic, smoothness, glow;
            public Color Color => ArcadeVisualInstaller.Hex(hex);
        }
        static Swatch S(string key, string hex, float metallic = 0, float smoothness = .1f, float glow = 0) =>
            new Swatch { key = key, hex = hex, metallic = metallic, smoothness = smoothness, glow = glow };

        // Sunny Wasteland Arcade: turquoise hero, sandstone rocks, fresh cacti, lifted navy steel. Hex values are authored
        // slightly off the art targets so the mild post grade (contrast +4, saturation +2) lands on them
        // (e.g. Teal 2AB9B1 renders ~#1EB8B0 with the red channel intact).
        static readonly Swatch[] Palette =
        {
            S("Sand", "EDBB6E"), S("SandLight", "F7D594"), S("SandShade", "D9984F"), S("Track", "E7B57A"),
            S("Rock", "E8A47E"), S("RockLight", "F3BA90"), S("RockShade", "CF8A69"),
            S("Teal", "2AB9B1", .12f, .45f), S("TealLight", "62DBCF", .1f, .45f), S("TealDark", "1B958F", .12f, .38f),
            S("Ivory", "FFF4DA", .05f, .32f), S("IvoryShade", "EAD8B5", .05f, .26f),
            S("Rust", "C8643A", .1f, .16f), S("RustDark", "8E4630", .1f, .14f),
            S("Steel", "3F4D65", .5f, .42f), S("SteelLight", "93A1B6", .45f, .46f),
            S("Rubber", "3D3843", 0, .12f), S("RubberEdge", "564F5E", 0, .12f),
            S("Cloth", "4E4359"), S("Scarf", "F2A23A", 0, .18f), S("Skin", "E1A37D", 0, .14f),
            S("Glass", "8FD3E0", .1f, .65f), S("Wood", "B97B45"), S("WoodLight", "D69C5F"),
            S("Mint", "4EE0A4", 0, .3f, .22f), S("Glow", "FFC93C", 0, .3f, 1f),
            S("Red", "E54C40", .08f, .32f), S("Yellow", "FCC946", .05f, .3f),
            S("Cactus", "69C04A", 0, .16f), S("DryLeaves", "B4B464"),
            S("TeamRed", "E54C40", .08f, .32f), S("AshStone", "A898BC"),
        };

        // Team red over >= 40% of the raider/buggy silhouette (Cloth = jacket + trousers, Rust/Red = buggy paint),
        // lifted violet charcoal for helmets, plates and cages, warm skin, a yellow accent (scarf / goggles).
        static readonly Dictionary<string, string> EnemyOverrides = new Dictionary<string, string>
        {
            { "Cloth", "E24A3F" }, { "Scarf", "FFC93C" }, { "Steel", "4D475A" }, { "SteelLight", "7B7389" },
            { "Rust", "BC3B31" }, { "RustDark", "5A3A40" }, { "Red", "E24A3F" }, { "TeamRed", "E24A3F" },
            { "Skin", "E1A37D" }, { "Rubber", "3D3843" }, { "IvoryShade", "F2DDB0" },
        };

        // Ashmaw: moss-green monster flesh with light planes, bone knots, team-red waist cloth, charcoal guard.
        static readonly Dictionary<string, string> MutantOverrides = new Dictionary<string, string>
        {
            { "Cloth", "6E9A45" }, { "Skin", "A9C66A" }, { "Scarf", "E24A3F" }, { "Steel", "4D475A" },
            { "SteelLight", "7B7389" }, { "RustDark", "5A3A40" }, { "IvoryShade", "FFF4DA" }, { "Rubber", "3D3843" },
        };

        // Scene materials that are not palette swatches but define the look of big shapes.
        static readonly (string path, string hex)[] Overrides =
        {
            ("Assets/RoadGunner/Art/ConceptWasteland/Materials/RockShade.mat", "E39C76"),   // 115 boulders + ledges
            ("Assets/RoadGunner/Art/ConceptWasteland/Materials/RockLight.mat", "F0B48A"),
            ("Assets/RoadGunner/Levels/WastelandRestored/Materials/Rock.mat", "E8A47E"),
            ("Assets/RoadGunner/Levels/WastelandRestored/Materials/RockLight.mat", "F3BA90"),
            ("Assets/RoadGunner/Levels/Greenwood/Exploration/Ash ridge dark shale.mat", "9E8FB0"),
        };

        public static Color ColorOf(string key)
        {
            foreach (var swatch in Palette) if (swatch.key == key) return swatch.Color;
            throw new ArgumentException("Unknown arcade palette key " + key);
        }

        public static int CellOf(string key)
        {
            for (int i = 0; i < Palette.Length; i++) if (Palette[i].key == key) return i;
            throw new ArgumentException("Unknown arcade palette key " + key);
        }

        /// <summary>UV of the centre of a palette cell (works for every atlas: same layout).</summary>
        public static Vector2 CellUv(int cell) => new Vector2((cell % 8 + .5f) / 8f, (cell / 8 + .5f) / 4f);

        public struct Atlases { public Texture2D color, response, emission, enemyColor, enemyEmission, mutantColor, mutantEmission; }

        /// <summary>Writes the world, enemy and mutant colour/emission atlases plus the shared response atlas.</summary>
        public static Atlases WriteAtlases()
        {
            var result = new Atlases();
            result.color = WriteColorAtlas(AtlasPath, null, false);
            result.emission = WriteColorAtlas(EmissionPath, null, true);
            result.enemyColor = WriteColorAtlas(EnemyAtlasPath, EnemyOverrides, false);
            result.enemyEmission = WriteColorAtlas(EnemyEmissionPath, EnemyOverrides, true);
            result.mutantColor = WriteColorAtlas(MutantAtlasPath, MutantOverrides, false);
            result.mutantEmission = WriteColorAtlas(MutantEmissionPath, MutantOverrides, true);
            const int cell = 8, width = 64, height = 32;
            var response = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                for (int index = 0; index < 32; index++)
                {
                    var swatch = Palette[Mathf.Min(index, Palette.Length - 1)];
                    var r = new Color(swatch.metallic, 0, 0, swatch.smoothness);
                    for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++) response.SetPixel((index % 8) * cell + x, (index / 8) * cell + y, r);
                }
                response.Apply();
                File.WriteAllBytes(ResponsePath, response.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(response); }
            result.response = ImportAtlas(ResponsePath, false);
            return result;
        }

        static Texture2D WriteColorAtlas(string path, Dictionary<string, string> overrides, bool emission)
        {
            const int cell = 8, width = 64, height = 32;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                for (int index = 0; index < 32; index++)
                {
                    var swatch = Palette[Mathf.Min(index, Palette.Length - 1)];
                    Color c = overrides != null && overrides.TryGetValue(swatch.key, out string hex) ? ArcadeVisualInstaller.Hex(hex) : swatch.Color;
                    if (emission) c = swatch.glow > 0 ? new Color(c.r * swatch.glow, c.g * swatch.glow, c.b * swatch.glow, 1) : Color.black;
                    for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++) texture.SetPixel((index % 8) * cell + x, (index / 8) * cell + y, c);
                }
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            return ImportAtlas(path, true);
        }

        static Texture2D ImportAtlas(string path, bool srgb)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer) throw new InvalidOperationException("Atlas import failed: " + path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = srgb; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false; importer.maxTextureSize = 64; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Every material asset the scene (and the prefabs it uses) depends on.</summary>
        public static List<Material> SceneMaterials(Scene scene)
        {
            var result = new HashSet<Material>();
            if (!string.IsNullOrEmpty(scene.path))
                foreach (string path in AssetDatabase.GetDependencies(scene.path, true))
                    if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    { var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) result.Add(m); }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in renderer.sharedMaterials)
                        if (m && AssetDatabase.Contains(m)) result.Add(m);
            return result.OrderBy(AssetDatabase.GetAssetPath).ToList();
        }

        static bool IsOpaqueLit(Material m)
        {
            if (!m || !m.shader || m.shader.name != LitShaderName) return false;
            if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f) return false;
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f) return false;
            return m.renderQueue < 2450;
        }

        /// <summary>Converts opaque Lit materials and re-applies the toon look to ones already converted.</summary>
        public static int ConvertScene(Scene scene, Shader toon, List<string> log)
        {
            int converted = 0, refreshed = 0, kept = 0;
            foreach (var m in SceneMaterials(scene))
            {
                if (m.shader == toon) { ApplyToonLook(m); refreshed++; continue; }
                if (!IsOpaqueLit(m)) { if (m.shader && m.shader.name == LitShaderName) kept++; continue; }
                ToToon(m, toon); converted++;
            }
            log.Add("Toon materials: converted " + converted + " opaque URP/Lit, refreshed " + refreshed + ", kept " + kept + " transparent/cutout Lit untouched.");
            return converted;
        }

        public static void ToToon(Material m, Shader toon)
        {
            Color baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            var baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            Vector2 scale = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = m.HasProperty("_BaseMap") ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0;
            float smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : .2f;
            var gloss = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            bool emissive = m.IsKeywordEnabled("_EMISSION");
            Color emission = emissive && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            var emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
            bool instancing = m.enableInstancing;

            m.shader = toon;
            m.SetColor("_BaseColor", baseColor);
            m.SetTexture("_BaseMap", baseMap); m.SetTextureScale("_BaseMap", scale); m.SetTextureOffset("_BaseMap", offset);
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_MetallicGlossMap", gloss);
            m.SetColor("_EmissionColor", emission); m.SetTexture("_EmissionMap", emissionMap);
            m.enableInstancing = instancing;
            m.renderQueue = -1;
            ApplyToonLook(m);
        }

        /// <summary>Shared toon parameters + keywords. Safe to call repeatedly.</summary>
        public static void ApplyToonLook(Material m)
        {
            m.SetColor("_ShadeColor", ArcadeVisualInstaller.ShadeColor);
            m.SetFloat("_RampThreshold", .45f); m.SetFloat("_RampSmoothness", .02f);
            m.SetFloat("_ShadowThreshold", .5f); m.SetFloat("_ShadowSmoothness", .04f);
            m.SetColor("_RimColor", new Color(1f, .88f, .68f, .28f)); m.SetFloat("_RimPower", 3f);
            m.SetColor("_SpecColor", new Color(1f, .98f, .92f, 1f)); m.SetFloat("_SpecSize", .38f);
            if (m.HasProperty("_WreckAmount") && AssetDatabase.GetAssetPath(m) != EnemyMaterialPath) m.SetFloat("_WreckAmount", 0);
            m.shaderKeywords = Array.Empty<string>();
            bool emissive = m.GetColor("_EmissionColor").maxColorComponent > .001f;
            if (emissive) m.EnableKeyword("_EMISSION");
            if (m.GetTexture("_MetallicGlossMap")) m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
        }

        static Material EnsureToon(string path, Shader toon)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) return null;
            if (m.shader != toon) { if (!IsOpaqueLit(m)) return null; ToToon(m, toon); }
            return m;
        }

        static void ConfigureAtlasMaterial(Material m, Texture2D color, Texture2D response, Texture2D emission)
        {
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", color); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetTexture("_MetallicGlossMap", response); m.SetFloat("_Metallic", 1); m.SetFloat("_Smoothness", 1);
            m.SetTexture("_EmissionMap", emission);
            m.SetColor("_EmissionColor", new Color(PaletteEmission, PaletteEmission, PaletteEmission, 1));
            m.enableInstancing = true;
            ApplyToonLook(m);
        }

        static Material EnsureAtlasMaterial(string path, Shader toon, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(toon) { name = name }; AssetDatabase.CreateAsset(m, path); }
            if (m.shader != toon) m.shader = toon;
            return m;
        }

        /// <summary>Atlas on the shared palette material + arcade colours on the swatch and rock materials; enemy/mutant atlas materials.</summary>
        public static (Material world, Material enemy, Material mutant) ApplyPalette(Shader toon, Atlases atlases, List<string> log)
        {
            var shared = EnsureToon(SharedPalettePath, toon);
            if (!shared) throw new InvalidOperationException("Missing shared palette material " + SharedPalettePath);
            ConfigureAtlasMaterial(shared, atlases.color, atlases.response, atlases.emission);

            var enemy = EnsureAtlasMaterial(EnemyMaterialPath, toon, "Arcade enemy palette");
            ConfigureAtlasMaterial(enemy, atlases.enemyColor, atlases.response, atlases.enemyEmission);
            // Husk lifted to readable charcoal-violet; ember HDR (gamma values) keeps G < 1 after clipping so specks read orange.
            enemy.SetFloat("_WreckAmount", 1); enemy.SetColor("_WreckColor", ArcadeVisualInstaller.Hex("4D4656"));
            enemy.SetColor("_WreckEmber", new Color(1.25f, .5f, .1f, 1)); EditorUtility.SetDirty(enemy);
            var mutant = EnsureAtlasMaterial(MutantMaterialPath, toon, "Arcade mutant palette");
            ConfigureAtlasMaterial(mutant, atlases.mutantColor, atlases.response, atlases.mutantEmission);

            int swatches = 0;
            foreach (var swatch in Palette)
            {
                var m = EnsureToon(Swatches + "/" + swatch.key + ".mat", toon);
                if (!m) continue;
                m.SetColor("_BaseColor", swatch.Color); m.SetTexture("_BaseMap", null);
                m.SetFloat("_Metallic", swatch.metallic); m.SetFloat("_Smoothness", swatch.smoothness);
                m.SetTexture("_MetallicGlossMap", null); m.SetTexture("_EmissionMap", null);
                var c = swatch.Color * (swatch.glow * PaletteEmission);
                m.SetColor("_EmissionColor", swatch.glow > 0 ? new Color(c.r, c.g, c.b, 1) : Color.black);
                ApplyToonLook(m); swatches++;
            }
            int overrides = 0;
            foreach (var (path, hex) in Overrides)
            {
                var m = EnsureToon(path, toon);
                if (!m) continue;
                m.SetColor("_BaseColor", ArcadeVisualInstaller.Hex(hex)); m.SetTexture("_BaseMap", null);
                m.SetFloat("_Metallic", 0); m.SetFloat("_Smoothness", .1f);
                ApplyToonLook(m); overrides++;
            }
            log.Add("Arcade palette: world atlas on shared palette; enemy + mutant atlas materials; " + swatches + " swatch + " + overrides + " rock/shale materials recoloured.");
            return (shared, enemy, mutant);
        }

        // ---------- enemy palette assignment ----------

        // Actor roots inside a prefab or scene: only renderers under these components are re-skinned (never props around them).
        static IEnumerable<(GameObject root, bool mutant)> ActorRoots(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Raider>(true)) yield return (c.gameObject, false);
            foreach (var c in go.GetComponentsInChildren<EnemyBuggy>(true)) yield return (c.gameObject, false);
            foreach (var c in go.GetComponentsInChildren<TruckArenaBoss>(true)) yield return (c.gameObject, false);
            foreach (var c in go.GetComponentsInChildren<WastelandMutant>(true)) yield return (c.gameObject, true);
        }

        /// <summary>Swaps world/actor palette slots under root to target. Returns the number of renderers changed.</summary>
        static int Swap(GameObject root, Material target, Material[] palettes, bool recordInstance)
        {
            int changed = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
                var materials = renderer.sharedMaterials; bool dirty = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] && materials[i] != target && palettes.Contains(materials[i])) { materials[i] = target; dirty = true; }
                if (!dirty) continue;
                renderer.sharedMaterials = materials; changed++;
                EditorUtility.SetDirty(renderer);
                if (recordInstance && PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return changed;
        }

        /// <summary>Enemy prefabs (raider rigs, raiders, enforcer, veteran/boss, buggy, truck) get the enemy atlas, Ashmaw the mutant atlas;
        /// then the scene instances and pools. The player vehicle is never touched.</summary>
        public static void AssignActorPalettes(Scene scene, Material world, Material enemy, Material mutant, List<string> log)
        {
            var palettes = new[] { world, enemy, mutant };
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RoadGunner" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
            var models = prefabPaths.Where(p => Path.GetFileName(p).StartsWith("Raider_") && p.EndsWith("_Model.prefab")).OrderBy(p => p).ToList();
            var actors = new List<string>();
            foreach (string path in prefabPaths.Except(models).OrderBy(p => p))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset || asset.GetComponentInChildren<VehicleController>(true)) continue;
                if (ActorRoots(asset).Any()) actors.Add(path);
            }
            bool Needs(GameObject root, Material target) => root.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m && m != target && palettes.Contains(m)));
            int prefabs = 0, prefabRenderers = 0;
            foreach (string path in models.Concat(actors))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset) continue;
                bool isModel = models.Contains(path);
                bool needs = isModel ? Needs(asset, enemy) : ActorRoots(asset).Any(a => Needs(a.root, a.mutant ? mutant : enemy));
                if (!needs) continue;
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int changed = isModel ? Swap(contents, enemy, palettes, false) : ActorRoots(contents).ToList().Sum(a => Swap(a.root, a.mutant ? mutant : enemy, palettes, false));
                    if (changed > 0) { PrefabUtility.SaveAsPrefabAsset(contents, path); prefabs++; prefabRenderers += changed; }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            int sceneRenderers = 0, sceneActors = 0;
            var roots = new HashSet<GameObject>();
            foreach (var c in RoadGunnerLevelAuthoring.InScene<Raider>()) if (c) roots.Add(c.gameObject);
            foreach (var c in RoadGunnerLevelAuthoring.InScene<EnemyBuggy>()) if (c) roots.Add(c.gameObject);
            foreach (var c in RoadGunnerLevelAuthoring.InScene<TruckArenaBoss>()) if (c) roots.Add(c.gameObject);
            var mutants = new HashSet<GameObject>();
            foreach (var c in RoadGunnerLevelAuthoring.InScene<WastelandMutant>()) if (c) mutants.Add(c.gameObject);
            foreach (var go in roots) { if (go.GetComponentInParent<VehicleController>()) continue; sceneRenderers += Swap(go, enemy, palettes, true); sceneActors++; }
            foreach (var go in mutants) { sceneRenderers += Swap(go, mutant, palettes, true); sceneActors++; }
            log.Add("Actor palettes: " + prefabs + " prefabs re-skinned (" + prefabRenderers + " renderers; " + models.Count + " raider rigs + " + actors.Count + " actor prefabs scanned); scene " +
                sceneActors + " actors, " + sceneRenderers + " renderer overrides. Player crew keeps the world atlas.");
        }
    }
}
