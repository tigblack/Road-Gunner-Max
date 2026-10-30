using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Offline orthographic XZ rasterization of the actual static scene meshes. CPU-only so batch
    /// authoring works without a graphics device; the player ships one small texture and UI markers.
    /// </summary>
    public static class ArenaMinimapBaker
    {
        public const string DefaultAssetPath = "Assets/RoadGunner/Levels/Greenwood/Arena1/Arena top-down map.png";
        static readonly Vector3 LightDirection = new Vector3(-.35f, 1, -.25f).normalized;

        sealed class SampleTexture
        {
            public int width, height;
            public Color32[] pixels;
            public bool repeat;
            public Color Sample(Vector2 uv)
            {
                float u = repeat ? Mathf.Repeat(uv.x, 1) : Mathf.Clamp01(uv.x);
                float v = repeat ? Mathf.Repeat(uv.y, 1) : Mathf.Clamp01(uv.y);
                int x = Mathf.Clamp((int)(u * width), 0, width - 1), y = Mathf.Clamp((int)(v * height), 0, height - 1);
                return pixels[y * width + x];
            }
        }

        sealed class Surface
        {
            public Color tint;
            public SampleTexture texture;
            public Vector2 scale = Vector2.one, offset;
            public float worldScale;
        }

        sealed class MeshData
        {
            public Vector3[] vertices;
            public Vector2[] uv;
            public Color[] colors;
            public int[][] triangles;
        }

        sealed class Rasterizer
        {
            public readonly Color[] pixels;
            readonly float[] heights;
            readonly Bounds bounds;
            readonly int size;
            readonly Dictionary<Material, Surface> surfaces = new Dictionary<Material, Surface>();
            readonly Dictionary<Texture2D, SampleTexture> textures = new Dictionary<Texture2D, SampleTexture>();
            readonly Dictionary<Mesh, MeshData> meshes = new Dictionary<Mesh, MeshData>();
            public int meshCount, roadCount, triangleCount;

            public Rasterizer(Bounds worldBounds, int resolution)
            {
                bounds = worldBounds; size = resolution;
                pixels = new Color[size * size]; heights = new float[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                { pixels[i] = new Color(.96f, .79f, .51f, 1); heights[i] = float.NegativeInfinity; }
            }

            SampleTexture ReadTexture(Texture2D source)
            {
                if (!source) return null;
                if (textures.TryGetValue(source, out var cached)) return cached;
                SampleTexture result = null;
                string path = AssetDatabase.GetAssetPath(source);
                if (source.isReadable)
                    result = new SampleTexture { pixels = source.GetPixels32(), width = source.width, height = source.height };
                else if (File.Exists(path) && (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                {
                    // Decode the source file in memory, leaving the real texture importer untouched.
                    var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        if (ImageConversion.LoadImage(decoded, File.ReadAllBytes(path), false))
                            result = new SampleTexture { pixels = decoded.GetPixels32(), width = decoded.width, height = decoded.height };
                    }
                    finally { Object.DestroyImmediate(decoded); }
                }
                if (result != null) result.repeat = source.wrapMode == TextureWrapMode.Repeat;
                textures.Add(source, result); return result;
            }

            Surface ReadSurface(Material material)
            {
                if (!material) return new Surface { tint = new Color(.5f, .48f, .39f) };
                if (surfaces.TryGetValue(material, out var cached)) return cached;
                var surface = new Surface { tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white };
                string textureName = material.HasProperty("_BaseMap") ? "_BaseMap" : material.HasProperty("_MainTex") ? "_MainTex" : null;
                if (textureName != null)
                {
                    surface.texture = ReadTexture(material.GetTexture(textureName) as Texture2D);
                    surface.scale = material.GetTextureScale(textureName); surface.offset = material.GetTextureOffset(textureName);
                }
                if (material.shader && material.shader.name == "RoadGunner/Concept Desert")
                    surface.worldScale = Mathf.Max(.001f, material.GetFloat("_WorldScale"));
                surfaces.Add(material, surface); return surface;
            }

            MeshData ReadMesh(Mesh mesh)
            {
                if (meshes.TryGetValue(mesh, out var cached)) return cached;
                var data = new MeshData { vertices = mesh.vertices, uv = mesh.uv, colors = mesh.colors, triangles = new int[mesh.subMeshCount][] };
                for (int i = 0; i < data.triangles.Length; i++) data.triangles[i] = mesh.GetTriangles(i);
                meshes.Add(mesh, data); return data;
            }

            public void Draw(MeshFilter filter, MeshRenderer renderer)
            {
                var data = ReadMesh(filter.sharedMesh); var matrix = filter.transform.localToWorldMatrix;
                var world = new Vector3[data.vertices.Length];
                for (int i = 0; i < world.Length; i++) world[i] = matrix.MultiplyPoint3x4(data.vertices[i]);
                var materials = renderer.sharedMaterials;
                bool road = filter.sharedMesh.name.StartsWith("Road ", StringComparison.OrdinalIgnoreCase) ||
                    filter.sharedMesh.name.IndexOf("worn-track", StringComparison.OrdinalIgnoreCase) >= 0;
                bool drawn = false;
                for (int submesh = 0; submesh < data.triangles.Length; submesh++)
                {
                    var surface = ReadSurface(materials.Length > 0 ? materials[Mathf.Min(submesh, materials.Length - 1)] : null);
                    if (surface.tint.a < .45f) continue;
                    var triangles = data.triangles[submesh];
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        Vector3 p0 = world[a], p1 = world[b], p2 = world[c];
                        Vector2 q0 = Pixel(p0), q1 = Pixel(p1), q2 = Pixel(p2);
                        float area = Edge(q0, q1, q2);
                        if (Mathf.Abs(area) < .00001f) continue; // Vertical walls have no area in a true overhead view.
                        int xmin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(q0.x, Mathf.Min(q1.x, q2.x))));
                        int xmax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(q0.x, Mathf.Max(q1.x, q2.x))));
                        int ymin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(q0.y, Mathf.Min(q1.y, q2.y))));
                        int ymax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(q0.y, Mathf.Max(q1.y, q2.y))));
                        if (xmin > xmax || ymin > ymax) continue;
                        drawn = true; triangleCount++;
                        Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                        if (normal.y < 0) normal = -normal;
                        float shade = .72f + .28f * Mathf.Max(0, Vector3.Dot(normal, LightDirection));
                        for (int y = ymin; y <= ymax; y++) for (int x = xmin; x <= xmax; x++)
                        {
                            var pixel = new Vector2(x + .5f, y + .5f);
                            float w0 = Edge(q1, q2, pixel) / area, w1 = Edge(q2, q0, pixel) / area, w2 = 1 - w0 - w1;
                            if (w0 < -.0001f || w1 < -.0001f || w2 < -.0001f) continue;
                            float height = p0.y * w0 + p1.y * w1 + p2.y * w2;
                            int index = y * size + x;
                            if (height < heights[index] - .002f) continue;
                            Color color = surface.tint;
                            if (surface.texture != null)
                            {
                                Vector2 uv;
                                if (surface.worldScale > 0)
                                {
                                    var position = p0 * w0 + p1 * w1 + p2 * w2;
                                    uv = new Vector2(position.x / surface.worldScale + .5f, position.z / surface.worldScale + .5f);
                                }
                                else if (data.uv.Length == world.Length)
                                    uv = Vector2.Scale(data.uv[a] * w0 + data.uv[b] * w1 + data.uv[c] * w2, surface.scale) + surface.offset;
                                else uv = Vector2.zero;
                                color *= surface.texture.Sample(uv);
                            }
                            if (data.colors.Length == world.Length)
                                color *= data.colors[a] * w0 + data.colors[b] * w1 + data.colors[c] * w2;
                            if (color.a < .45f) continue;
                            // Existing road mesh footprints receive contrast for legibility at phone size.
                            if (road) color = Color.Lerp(color, new Color(.43f, .34f, .24f, 1), .32f);
                            color.r *= shade; color.g *= shade; color.b *= shade; color.a = 1;
                            pixels[index] = color; heights[index] = height;
                        }
                    }
                }
                if (drawn) { meshCount++; if (road) roadCount++; }
            }

            public void AddReliefEdges()
            {
                // One-pixel contact shading separates actual cliffs/buildings from navigable ground.
                for (int y = 1; y < size - 1; y++) for (int x = 1; x < size - 1; x++)
                {
                    int i = y * size + x; float height = heights[i];
                    if (float.IsNegativeInfinity(height)) continue;
                    float nearby = Mathf.Max(heights[i - 1], Mathf.Max(heights[i + 1], Mathf.Max(heights[i - size], heights[i + size])));
                    if (nearby - height > 1.3f)
                    { var color = pixels[i]; color.r *= .74f; color.g *= .74f; color.b *= .74f; pixels[i] = color; }
                }
            }

            Vector2 Pixel(Vector3 p) => new Vector2((p.x - bounds.min.x) / bounds.size.x * size, (p.z - bounds.min.z) / bounds.size.z * size);
            static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        static bool Include(MeshFilter filter, LevelLayout layout, Bounds bounds, out MeshRenderer renderer)
        {
            renderer = filter.GetComponent<MeshRenderer>();
            if (!filter.sharedMesh || !filter.sharedMesh.isReadable || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) return false;
            if (filter.GetComponentInParent<CombatTarget>() || filter.GetComponentInParent<VehicleController>() ||
                filter.GetComponentInParent<ArenaUpgradeStation>() || filter.GetComponentInParent<Canvas>() ||
                filter.GetComponentInParent<PickupPool>() || filter.GetComponentInParent<CombatEffects>()) return false;
            string meshName = filter.sharedMesh.name;
            if (meshName.IndexOf("radius", StringComparison.OrdinalIgnoreCase) >= 0 ||
                meshName.IndexOf("telegraph", StringComparison.OrdinalIgnoreCase) >= 0 ||
                meshName.IndexOf("shockwave", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (filter != layout.ground && Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) < .65f) return false;
            var box = renderer.bounds;
            return box.max.x >= bounds.min.x && box.min.x <= bounds.max.x && box.max.z >= bounds.min.z && box.min.z <= bounds.max.z;
        }

        public static Texture2D BakeAndConfigure(LevelLayout layout, ArenaMinimap map,
            string assetPath = DefaultAssetPath, int resolution = 512)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Bake the static map outside Play Mode.");
            if (!layout || !layout.ground || !map || !map.mapRect) throw new InvalidOperationException("Minimap bake requires the existing arena layout, ground and UI map rectangle.");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use an Assets/...png destination.", nameof(assetPath));
            Bounds bounds = layout.gameplayBounds;
            if (bounds.size.x <= 0 || bounds.size.z <= 0) throw new InvalidOperationException("Arena bounds must have positive X/Z extents.");
            resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(resolution), 128, 1024);
            var rasterizer = new Rasterizer(bounds, resolution);
            foreach (var root in layout.gameObject.scene.GetRootGameObjects())
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
                    if (Include(filter, layout, bounds, out var renderer)) rasterizer.Draw(filter, renderer);
            if (rasterizer.meshCount == 0) throw new InvalidOperationException("No readable static arena meshes were found.");
            rasterizer.AddReliefEdges();
            var image = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            try
            {
                image.SetPixels(rasterizer.pixels); image.Apply(false, false);
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                File.WriteAllBytes(assetPath, image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = resolution; importer.isReadable = false; importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            var layer = map.mapRect.Find("Baked arena terrain");
            if (!layer)
            {
                var go = new GameObject("Baked arena terrain", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(map.mapRect, false); layer = go.transform;
            }
            var rect = (RectTransform)layer; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var background = layer.GetComponent<RawImage>();
            if (!background) background = layer.gameObject.AddComponent<RawImage>();
            background.texture = texture; background.color = Color.white; background.raycastTarget = false;
            map.layout = layout; map.backgroundImage = background; map.bakedWorldBounds = bounds; map.hasBakedWorldBounds = true;
            map.bakedEnvironmentMeshCount = rasterizer.meshCount; map.bakedRoadMeshCount = rasterizer.roadCount; map.bakedTriangleCount = rasterizer.triangleCount;
            CasualMinimapInstaller.RefreshBakedContent(map);
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(background); EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            Debug.Log("MINIMAP_BAKED " + resolution + "x" + resolution + "; static meshes=" + rasterizer.meshCount +
                "; road meshes=" + rasterizer.roadCount + "; triangles=" + rasterizer.triangleCount + "; bounds=" + bounds + "; " + assetPath);
            return texture;
        }
    }
}
