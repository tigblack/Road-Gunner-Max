using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Arcade ground: regenerates the 512 m world texture (same path/GUID) from the exploration layout with the
    /// same route sampling and distance rasterisation as ExplorationArenaInstaller.BuildWorldSurface, writes the
    /// tiling sand-detail texture, configures both Concept Desert materials and rebakes the minimap.
    /// The world texture holds region colours (nearest-centre regions like ArenaExploration, noise-warped 6-10 m
    /// feathered borders, no ink), toon-stepped macro patches and trampled pocket floors; the packed roads are drawn by
    /// the shader from a separate uncompressed 512 px road map of signed distances, encoded 0.5 + 0.5 * saturate((d + 6) / 14):
    /// R to the packed-road union (roads + pockets + laybys), G to the travelled lanes. A road-painted copy of the world
    /// texture is swapped in only while the minimap is baked, which is then given a little more saturation/contrast.
    /// </summary>
    public static class ArcadeVisualGround
    {
        public const string WorldTexturePath = ExplorationArenaInstaller.AssetRoot + "/Exploration world surface.png";
        public const string GroundMaterialPath = ExplorationArenaInstaller.AssetRoot + "/Exploration continuous ground.mat";
        public const string LegacyGroundMaterialPath = "Assets/RoadGunner/Art/ConceptWasteland/Materials/Continuous desert surface.mat";
        public const string DetailPath = ArcadeVisualInstaller.Root + "/Textures/Arcade sand detail.png";
        public const string RoadMapPath = ArcadeVisualInstaller.Root + "/Textures/Arcade road distance.png";
        public const string MinimapSourcePath = ArcadeVisualInstaller.Root + "/Textures/Arcade minimap ground source.png";
        const int Size = 1024, RoadMapSize = 512;
        const float Span = 512;

        public sealed class Lane { public ExplorationArenaLayoutData.Route route; public Vector2[] points; }

        public static ExplorationArenaLayoutData LoadLayout()
        {
            var data = AssetDatabase.LoadAssetAtPath<ExplorationArenaLayoutData>(ExplorationArenaInstaller.LayoutAssetPath);
            if (!data || data.routes == null || data.regions == null)
                throw new InvalidOperationException("Exploration layout with routes and regions is required: " + ExplorationArenaInstaller.LayoutAssetPath);
            return data;
        }

        public static Lane[] Lanes(ExplorationArenaLayoutData data) => data.routes
            .Where(r => r != null && r.nodesW != null && r.nodesW.Length >= 2)
            .Select(r => new Lane { route = r, points = Sample(r.nodesW) }).ToArray();

        public static void Apply(GameSession session, List<string> log)
        {
            var layout = RoadGunnerLevelAuthoring.Current;
            if (!layout || !layout.ground) throw new InvalidOperationException("LevelLayout with ground is required for the arcade ground.");
            var desert = Shader.Find("RoadGunner/Concept Desert");
            if (!desert) throw new InvalidOperationException("Shader RoadGunner/Concept Desert is missing.");
            var data = LoadLayout();
            var detail = WriteDetailTexture();
            var world = WriteWorldSurface(data, log, out var roadMap);
            int configured = 0;
            foreach (string path in new[] { GroundMaterialPath, LegacyGroundMaterialPath })
                if (ConfigureGround(path, desert, world, roadMap, detail)) configured++;
            log.Add("Arcade ground: " + WorldTexturePath + " regenerated (" + Size + "px) + road distance " + RoadMapPath + "; detail " + DetailPath + "; " + configured + " desert materials.");
        }

        public static void BakeMinimap(GameSession session, List<string> log)
        {
            var layout = RoadGunnerLevelAuthoring.Current;
            if (!session || !session.minimap || !layout) { log.Add("Minimap rebake skipped (no minimap/layout)."); return; }
            // The shader paints the roads, the world texture does not: bake from the road-painted copy, without the
            // small collider-free dressing (clean map plate), then restore everything.
            var materials = new[] { GroundMaterialPath, LegacyGroundMaterialPath }.Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m).ToArray();
            var originals = materials.Select(m => m.GetTexture("_BaseMap")).ToArray();
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(MinimapSourcePath);
            var dressing = layout.decorationRoot ? layout.decorationRoot.Find(ArcadeVisualDressing.RootName) : null;
            bool dressingActive = dressing && dressing.gameObject.activeSelf;
            Texture2D texture;
            try
            {
                if (source) foreach (var m in materials) m.SetTexture("_BaseMap", source);
                if (dressing) dressing.gameObject.SetActive(false);
                texture = ArenaMinimapBaker.BakeAndConfigure(layout, session.minimap);
            }
            finally
            {
                for (int i = 0; i < materials.Length; i++) { materials[i].SetTexture("_BaseMap", originals[i]); EditorUtility.SetDirty(materials[i]); }
                if (dressing) dressing.gameObject.SetActive(dressingActive);
            }
            string path = AssetDatabase.GetAssetPath(texture);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (File.Exists(path) && ImageConversion.LoadImage(decoded, File.ReadAllBytes(path), false))
                {
                    // A touch more saturation and contrast so the regions read on the phone-sized map plate.
                    var pixels = decoded.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var c = pixels[i];
                        float l = c.r * .299f + c.g * .587f + c.b * .114f;
                        c.r = l + (c.r - l) * 1.22f; c.g = l + (c.g - l) * 1.22f; c.b = l + (c.b - l) * 1.22f;
                        c.r = (c.r - .55f) * 1.1f + .55f; c.g = (c.g - .55f) * 1.1f + .55f; c.b = (c.b - .55f) * 1.1f + .55f;
                        pixels[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1);
                    }
                    var boosted = new Texture2D(decoded.width, decoded.height, TextureFormat.RGB24, false);
                    try { boosted.SetPixels(pixels); boosted.Apply(false, false); File.WriteAllBytes(path, boosted.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(boosted); }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                    var colors = new HashSet<int>(); var lums = new List<float>();
                    int step = Mathf.Max(1, decoded.width / 128);
                    for (int y = decoded.height / 10; y < decoded.height * 9 / 10; y += step) for (int x = decoded.width / 10; x < decoded.width * 9 / 10; x += step)
                    {
                        var c = (Color32)pixels[y * decoded.width + x];
                        colors.Add((c.r / 16 << 8) | (c.g / 16 << 4) | c.b / 16);
                        lums.Add(((Color)c).grayscale);
                    }
                    lums.Sort();
                    log.Add("Minimap rebaked (road-painted source, no dressing, +22% sat, +10% contrast): " + colors.Count + " distinct 4-bit colours (need >=12); inner luminance p5 " +
                        lums[lums.Count / 20].ToString("F2") + " / p50 " + lums[lums.Count / 2].ToString("F2") + " / p95 " + lums[lums.Count * 19 / 20].ToString("F2") + ".");
                }
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        static bool ConfigureGround(string path, Shader desert, Texture2D world, Texture2D roadMap, Texture2D detail)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) return false;
            if (m.shader != desert) m.shader = desert;
            m.SetTexture("_BaseMap", world); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetTexture("_AccentMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AccentPath)); m.SetVector("_ToneLevels", new Vector4(.315f, .47f, .665f, 1));
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_WorldScale", Span);
            m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 6f);
            m.SetFloat("_GrainStrength", .05f); m.SetFloat("_RippleStrength", .065f);
            m.SetFloat("_PebbleStrength", .3f); m.SetFloat("_MacroStrength", .035f); m.SetFloat("_MottleStrength", .045f);
            m.SetColor("_PebbleColor", new Color(.74f, .6f, .52f, 1));
            m.SetTexture("_RoadMap", roadMap); m.SetFloat("_RutStrength", .06f);
            // Colours below are gamma-space inspector values; the shoulder tint multiplies in linear (~0.85, 0.77, 0.70).
            m.SetColor("_RoadColor", ArcadeVisualInstaller.Hex("FDEABF")); m.SetFloat("_RoadTint", .8f); m.SetFloat("_PocketRoad", .2f);
            m.SetFloat("_RoadSoftness", .2f); m.SetFloat("_RoadWarp", .3f);
            m.SetColor("_RoadEdgeColor", new Color(.93f, .89f, .85f, 1)); m.SetFloat("_RoadEdgeWidth", .75f); m.SetFloat("_RoadDetail", .35f);
            m.SetColor("_ShadeColor", ArcadeVisualInstaller.ShadeColor);
            m.SetFloat("_ShadowThreshold", .5f); m.SetFloat("_ShadowSmoothness", .03f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return true;
        }

        // ---------- world surface ----------

        sealed class RegionLook
        {
            public Color main, pocket, accent;
            public float accentLevel = 2; // >= 1: no accent patches
        }

        // Main tones per region (the shader steps the A tone field into deep/mid/main/light patches) and crisp accents:
        // Salvage Yard sage, Rustworks rust stains, Ash Ridge deeper lilac. Dust Flats relies on tones + dressing.
        static readonly Dictionary<string, RegionLook> Looks = new Dictionary<string, RegionLook>
        {
            { "dust-flats", new RegionLook { main = H("EFC17A"), pocket = H("F0C983") } },
            { "salvage-yard", new RegionLook { main = H("D3C181"), pocket = H("DACA90"), accent = H("AEB879"), accentLevel = .58f } },
            { "rustworks", new RegionLook { main = H("E99C6C"), pocket = H("EDAA7E"), accent = H("CD6A40"), accentLevel = .71f } },
            { "ash-ridge", new RegionLook { main = H("BBA9C2"), pocket = H("C8BACC"), accent = H("9E8FB0"), accentLevel = .6f } },
        };
        public const string AccentPath = ArcadeVisualInstaller.Root + "/Textures/Arcade ground accents.png";

        static float Smooth(float edge, float width, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(edge - width, edge + width, x));

        // C# mirror of the Concept Desert tone/accent steps (soft edges) for the minimap source.
        static Color Toned(Color c, float tone, Color accent, float field)
        {
            var l = c.linear; float luma = l.r * .2126f + l.g * .7152f + l.b * .0722f;
            var lum = new Color(luma, luma, luma);
            var deep = Color.LerpUnclamped(lum, l, 1.25f) * .86f;
            var light = Color.LerpUnclamped(lum, l, .85f) * 1.08f + new Color(.02f, .02f, .02f);
            var t = Color.Lerp(l * .965f, l, Smooth(.47f, .02f, tone));
            t = Color.Lerp(t, light, Smooth(.665f, .02f, tone));
            t = Color.Lerp(t, deep, 1 - Smooth(.315f, .02f, tone));
            t = Color.Lerp(t, accent.linear, Smooth(.5f, .02f, field));
            t = Color.Lerp(t, accent.linear * .88f, Smooth(.62f, .02f, field));
            var g = new Color(Mathf.Clamp01(t.r), Mathf.Clamp01(t.g), Mathf.Clamp01(t.b)).gamma; g.a = 1;
            return g;
        }

        public static Texture2D WriteWorldSurface(ExplorationArenaLayoutData data, List<string> log, out Texture2D roadMap)
        {
            var lanes = Lanes(data);
            var roads = RasterSurfaceDistance(lanes, data, false);
            var union = RasterSurfaceDistance(lanes, data, true);
            var regions = data.regions.Where(r => r != null).ToArray();
            var looks = regions.Select(r => Looks.TryGetValue(r.id, out var look) ? look : new RegionLook
            { main = r.sandColor, pocket = Color.Lerp(r.sandColor, Color.white, .1f) }).ToArray();
            Color packedRoad = H("FDEABF"), border = Looks["dust-flats"].main;
            var pixels = new Color32[Size * Size];
            var accents = new Color32[Size * Size];
            var minimap = new Color32[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                float wx = (x + .5f) / Size * Span - Span * .5f, wz = (y + .5f) / Size * Span - Span * .5f;
                // Two-octave domain warp: region borders wander organically (~90 m swings + ~22 m wiggles).
                float warpX = (Mathf.PerlinNoise(wx * .011f + 31, wz * .011f + 7) - .5f) * 28 + (Mathf.PerlinNoise(wx * .045f + 3, wz * .045f + 71) - .5f) * 8;
                float warpZ = (Mathf.PerlinNoise(wx * .011f + 93, wz * .011f + 57) - .5f) * 28 + (Mathf.PerlinNoise(wx * .045f + 47, wz * .045f + 13) - .5f) * 8;
                var w = ExplorationArenaInstaller.ToW(new Vector3(wx + warpX, 0, wz + warpZ));
                float tone = Mathf.PerlinNoise(wx / 15f + 3, wz / 15f + 17) * .62f + Mathf.PerlinNoise(wx / 6.5f + 40, wz / 6.5f + 11) * .38f;
                float q = Mathf.PerlinNoise(wx / 7.5f + 71, wz / 7.5f + 29) * .72f + Mathf.PerlinNoise(wx / 3.2f + 5, wz / 3.2f + 63) * .28f;

                // Nearest-centre regions (ArenaExploration picks the closest zone centre), feathered across the bisector.
                int a = 0, b = 0; float da = float.PositiveInfinity, db = float.PositiveInfinity;
                for (int r = 0; r < regions.Length; r++)
                {
                    float d = Vector2.Distance(w, regions[r].centerW) * 2.5f;
                    if (d < da) { db = da; b = a; da = d; a = r; }
                    else if (d < db) { db = d; b = r; }
                }
                float Field(RegionLook look) => look.accentLevel < 1 ? Mathf.Max(0, q - look.accentLevel + .5f) : 0;
                Color color = looks[a].main, pocketColor = looks[a].pocket, accent = looks[a].accentLevel < 1 ? looks[a].accent : looks[a].main;
                float field = Field(looks[a]);
                if (regions.Length > 1 && b != a)
                {
                    float separation = Mathf.Max(1, Vector2.Distance(regions[a].centerW, regions[b].centerW) * 2.5f);
                    float toBorder = (db * db - da * da) / (2 * separation);
                    float feather = 3f + 2f * Mathf.PerlinNoise(wx * .05f + 9, wz * .05f + 90);
                    float s = Mathf.Clamp01(toBorder / feather);
                    s = Mathf.Clamp01(s + (Mathf.PerlinNoise(wx * .42f + 5, wz * .42f + 8) - .5f) * .55f * (1 - s));
                    float t = .5f + .5f * Mathf.SmoothStep(0, 1, s);
                    color = Color.Lerp(looks[b].main, color, t);
                    pocketColor = Color.Lerp(looks[b].pocket, pocketColor, t);
                    var accentB = looks[b].accentLevel < 1 ? looks[b].accent : looks[b].main;
                    float fieldB = Field(looks[b]);
                    // Keep the accent colour of whichever side owns patches, so border patches never turn muddy.
                    accent = field <= 0 ? accentB : fieldB <= 0 ? accent : Color.Lerp(accentB, accent, t);
                    field = Mathf.Lerp(fieldB, field, t);
                }

                // Trampled combat pockets / laybys: lighter, quieter floor (flattened tones, fewer accents), soft noisy rim.
                float du = union[index], dr = roads[index];
                float rim = (Mathf.PerlinNoise(wx * .35f + 17, wz * .35f + 3) - .5f) * 1.1f;
                float onPocket = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.7f, .4f, du + rim));
                if (onPocket > 0)
                {
                    color = Color.Lerp(color, Color.Lerp(pocketColor, color, .15f), onPocket);
                    tone = Mathf.Lerp(tone, .56f + (tone - .5f) * .55f, onPocket * .85f);
                    field = Mathf.Max(0, field - .22f * onPocket);
                }

                // Fade to plain sand near the texture edge: the clamped border never smears region stripes.
                float edge = Mathf.Clamp01(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4, 20, Span * .5f - Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)))));
                color = Color.Lerp(border, color, edge); tone = Mathf.Lerp(.56f, tone, edge); field *= edge;
                pixels[index] = new Color32(Byte(color.r), Byte(color.g), Byte(color.b), Byte(tone));
                accents[index] = new Color32(Byte(accent.r), Byte(accent.g), Byte(accent.b), Byte(field));

                var surface = Toned(color, tone, accent, field);
                float onLane = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.5f, .5f, dr));
                var painted = Color.Lerp(surface, Color.Lerp(packedRoad, surface, .3f), onLane); painted.a = 1;
                minimap[index] = (Color32)painted;
            }
            var unionSmall = RasterSurfaceDistance(lanes, data, true, RoadMapSize);
            var lanesSmall = RasterSurfaceDistance(lanes, data, false, RoadMapSize);
            var pocketsSmall = RasterSurfaceDistance(Array.Empty<Lane>(), data, true, RoadMapSize);
            var distance = new Color32[RoadMapSize * RoadMapSize];
            byte Encode(float d) => Byte(.5f + .5f * Mathf.Clamp01((d + 6) / 14));
            for (int i = 0; i < distance.Length; i++)
                distance[i] = new Color32(Encode(unionSmall[i]), Encode(lanesSmall[i]), Encode(pocketsSmall[i]), 255);
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var opaque = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var roadTexture = new Texture2D(RoadMapSize, RoadMapSize, TextureFormat.RGB24, false, true);
            try
            {
                texture.SetPixels32(pixels); texture.Apply(false, false); File.WriteAllBytes(WorldTexturePath, texture.EncodeToPNG());
                texture.SetPixels32(accents); texture.Apply(false, false); File.WriteAllBytes(AccentPath, texture.EncodeToPNG());
                opaque.SetPixels32(minimap); opaque.Apply(false, false); File.WriteAllBytes(MinimapSourcePath, opaque.EncodeToPNG());
                roadTexture.SetPixels32(distance); roadTexture.Apply(false, false); File.WriteAllBytes(RoadMapPath, roadTexture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(opaque); Object.DestroyImmediate(roadTexture); }
            AssetDatabase.ImportAsset(RoadMapPath, ImportAssetOptions.ForceSynchronousImport);
            var roadImporter = (TextureImporter)AssetImporter.GetAtPath(RoadMapPath);
            if (!roadImporter) throw new InvalidOperationException("Road distance import failed.");
            roadImporter.textureType = TextureImporterType.Default;
            roadImporter.sRGBTexture = false; roadImporter.mipmapEnabled = true; roadImporter.wrapMode = TextureWrapMode.Clamp;
            roadImporter.filterMode = FilterMode.Bilinear; roadImporter.anisoLevel = 1; roadImporter.alphaSource = TextureImporterAlphaSource.None;
            roadImporter.textureCompression = TextureImporterCompression.Uncompressed; roadImporter.maxTextureSize = RoadMapSize;
            roadImporter.isReadable = false; roadImporter.SaveAndReimport();
            roadMap = AssetDatabase.LoadAssetAtPath<Texture2D>(RoadMapPath);
            foreach (string path in new[] { WorldTexturePath, AccentPath, MinimapSourcePath })
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (!importer) throw new InvalidOperationException("World surface import failed: " + path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 1;
                // A holds the smooth tone / accent fields (linear data in an sRGB texture: alpha is never gamma-converted).
                importer.alphaSource = path == MinimapSourcePath ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.maxTextureSize = Size;
                importer.isReadable = false; importer.SaveAndReimport();
            }
            log.Add("World surface: " + lanes.Length + " routes, " + (data.pockets?.Length ?? 0) + " pockets, " + regions.Length + " feathered regions; tone field in A, accents " + AccentPath + "; minimap source " + MinimapSourcePath + ".");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(WorldTexturePath);
        }

        // Same Catmull-Rom sampling as ExplorationArenaInstaller.Sample (W space, ~2 m spacing).
        public static Vector2[] Sample(Vector2[] nodes)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < nodes.Length - 1; i++)
            {
                var a = nodes[Mathf.Max(0, i - 1)]; var b = nodes[i]; var c = nodes[i + 1]; var d = nodes[Mathf.Min(nodes.Length - 1, i + 2)];
                int count = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(b, c) * 2.5f / 2));
                for (int k = 0; k < count; k++)
                {
                    float t = k / (float)count, t2 = t * t, t3 = t2 * t;
                    result.Add(.5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3));
                }
            }
            result.Add(nodes[nodes.Length - 1]); return result.ToArray();
        }

        // Same bounded segment rasterisation as ExplorationArenaInstaller.RasterSurfaceDistance (metres, world XZ).
        public static float[] RasterSurfaceDistance(Lane[] lanes, ExplorationArenaLayoutData data, bool includePockets, int size = Size, float span = Span)
        {
            var result = Enumerable.Repeat(float.PositiveInfinity, size * size).ToArray();
            void Stroke(Vector2 a, Vector2 b, float radius)
            {
                float extent = radius + 4;
                int minX = Mathf.Clamp(Mathf.FloorToInt(((Mathf.Min(a.x, b.x) - extent) / span + .5f) * size), 0, size - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(((Mathf.Max(a.x, b.x) + extent) / span + .5f) * size), 0, size - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(((Mathf.Min(a.y, b.y) - extent) / span + .5f) * size), 0, size - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(((Mathf.Max(a.y, b.y) + extent) / span + .5f) * size), 0, size - 1);
                var delta = b - a;
                for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2((x + .5f) / size * span - span * .5f, (y + .5f) / size * span - span * .5f);
                    float t = delta.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude);
                    int index = y * size + x; result[index] = Mathf.Min(result[index], Vector2.Distance(p, a + t * delta) - radius);
                }
            }
            foreach (var lane in lanes) for (int i = 1; i < lane.points.Length; i++)
            {
                var a = ExplorationArenaInstaller.World(lane.points[i - 1]); var b = ExplorationArenaInstaller.World(lane.points[i]);
                Stroke(new Vector2(a.x, a.z), new Vector2(b.x, b.z), lane.route.widthMeters * .5f);
            }
            if (includePockets)
            {
                foreach (var pocket in data.pockets ?? Array.Empty<ExplorationArenaLayoutData.Pocket>())
                { if (pocket == null) continue; var p = ExplorationArenaInstaller.World(pocket.centerW); Stroke(new Vector2(p.x, p.z), new Vector2(p.x, p.z), pocket.radiusMeters); }
                foreach (var layby in data.recoveryLaybysW ?? Array.Empty<Vector2>())
                { var p = ExplorationArenaInstaller.World(layby); Stroke(new Vector2(p.x, p.z), new Vector2(p.x, p.z), 6); }
            }
            return result;
        }

        /// <summary>Signed distance (m) to the packed-road union at a world XZ point, sampled from a raster.</summary>
        public static float SampleRaster(float[] raster, Vector3 world, int size = Size, float span = Span)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((world.x / span + .5f) * size), 0, size - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((world.z / span + .5f) * size), 0, size - 1);
            return raster[y * size + x];
        }

        // ---------- tiling sand detail ----------

        public static Texture2D WriteDetailTexture()
        {
            const int n = 256;
            var rng = new System.Random(9127);
            var pebbles = Enumerable.Repeat(.5f, n * n).ToArray();
            void Pebble(float value, float minRadius, float maxRadius)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float rx = minRadius + (float)rng.NextDouble() * (maxRadius - minRadius), ry = rx * (.65f + .35f * (float)rng.NextDouble());
                float angle = (float)rng.NextDouble() * Mathf.PI, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                int reach = Mathf.CeilToInt(rx) + 2;
                for (int dy = -reach; dy <= reach; dy++) for (int dx = -reach; dx <= reach; dx++)
                {
                    int px = ((Mathf.FloorToInt(cx) + dx) % n + n) % n, py = ((Mathf.FloorToInt(cy) + dy) % n + n) % n;
                    float ox = Mathf.Floor(cx) + dx + .5f - cx, oy = Mathf.Floor(cy) + dy + .5f - cy;
                    float u = (ox * cos + oy * sin) / rx, v = (-ox * sin + oy * cos) / ry;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float mask = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, 1f + 1f / rx, d));
                    int i = py * n + px; pebbles[i] = Mathf.Lerp(pebbles[i], value, mask);
                }
            }
            for (int i = 0; i < 20; i++) Pebble(1, 2.2f, 4.6f);
            for (int i = 0; i < 16; i++) Pebble(0, 1.5f, 3f);

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float grain = .5f + (TileNoise(u, v, 40, 1) - .5f) * .75f + (TileNoise(u, v, 90, 2) - .5f) * .5f;
                float warp = TileNoise(u, v, 3, 3), presence = TileNoise(u, v, 5, 4);
                float phase = v * 12 + (warp - .5f) * 1.6f + .3f * Mathf.Sin(2 * Mathf.PI * 2 * u);
                float s = Mathf.Sin(2 * Mathf.PI * phase);
                float crest = s > 0 ? Mathf.Pow(s, 6) : -.35f * Mathf.Pow(-s, 2);
                float ripple = .5f + .5f * crest * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .7f, presence));
                float macro = (TileNoise(u, v, 4, 5) * .65f + TileNoise(u, v, 9, 6) * .35f - .5f) * 1.6f + .5f;
                pixels[y * n + x] = new Color32(Byte(grain), Byte(ripple), Byte(pebbles[y * n + x]), Byte(macro));
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            try { texture.SetPixels32(pixels); texture.Apply(false, false); File.WriteAllBytes(DetailPath, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(DetailPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(DetailPath);
            if (!importer) throw new InvalidOperationException("Detail texture import failed.");
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 2;
            importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = n;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath);
        }

        static byte Byte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255);
        static Color H(string hex) => ArcadeVisualInstaller.Hex(hex);
        static float Gauss(float x, float sigma) => Mathf.Exp(-(x / sigma) * (x / sigma));
        static float Fbm(float x, float y) => Mathf.PerlinNoise(x, y) * .6f + Mathf.PerlinNoise(x * 2.03f + 17, y * 2.03f + 9) * .3f + Mathf.PerlinNoise(x * 4.1f + 3, y * 4.1f + 41) * .1f;

        // Periodic value noise on a period x period lattice: tiles seamlessly over the unit square.
        static float TileNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int seed)
        {
            x = (x % period + period) % period; y = (y % period + period) % period;
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
