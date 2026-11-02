using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Idempotent VFX + game-feel pass (runs after every legacy installer): generates particle textures, materials and meshes,
    /// builds the pooled ArcadeVfx sink under the feedback hub (numbers mesh, telegraph zones, decals, station beacons, loot halos,
    /// speed streaks, confetti), and restyles CombatEffects tracers/impacts/muzzle flash, the three VehicleExplosion slots,
    /// the car's wheel dust and the tire-track material. Pool sizes checked by tests are kept.
    /// </summary>
    public static class ArcadeVfxInstaller
    {
        const string Root = "Assets/RoadGunner/Art/Arcade/Vfx";
        const string TextureFolder = Root + "/Textures", MaterialFolder = Root + "/Materials", MeshFolder = Root + "/Meshes";
        const string FontPath = "Assets/RoadGunner/UI/Arcade/Fonts/LilitaOne-Regular.ttf";
        const string VfxName = "Arcade VFX • pooled juice";
        const int ConfettiPieces = 56, TelegraphSlots = 2, ScorchSlots = 4, CrackSlots = 2, MinBeaconSlots = 4, HaloSlots = 12;
        const float TrackFadeSeconds = 3.5f;

        [MenuItem("Tools/Road Gunner/Arcade/Apply VFX + Game Feel")]
        public static void ApplyMenu() { Apply(ArcadeFeedbackSetup.OpenGameplayScene()); Debug.Log("ARCADE VFX applied; scene marked dirty (not saved)."); }

        public static void ApplyBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            Apply(session);
            ArcadeFeedbackSetup.Save();
            Debug.Log("ARCADE VFX OK");
        }

        public static void Apply(GameSession session)
        {
            var hub = ArcadeFeedbackSetup.Ensure(session);
            Folder(TextureFolder); Folder(MaterialFolder); Folder(MeshFolder);
            var kit = new Kit();
            var vfx = BuildSink(hub, session, kit);
            UpgradeCombatEffects(session, kit);
            UpgradeExplosions(session, kit);
            WheelDust(session, kit);
            TireTracksMaterial(session, kit);
            DeleteObsoleteAssets();
            EditorUtility.SetDirty(vfx);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        }

        // ---------- generated assets ----------

        sealed class Kit
        {
            public readonly Material glow, core, groundFlash, halo, spark, sparkle, ring, shock, zone, dangerEdge, alert, tracer, flame, impact, flashStar, muzzleStar, streak;
            public readonly Material puff, dustPuff, softDust, fire, star, plus, scorch, crack, chips, brass, column, iconBolt, iconHeart, text, track;
            public readonly Mesh quad, billboard, flameMesh, casing, chip;
            public readonly Sprite confetti;
            public readonly Font font;
            public Kit()
            {
                var add = Shader.Find("RoadGunner/Arcade Particle Additive");
                var alpha = Shader.Find("RoadGunner/Arcade Particle Alpha");
                var textShader = Shader.Find("RoadGunner/Arcade Particle Text");
                var trackShader = Shader.Find("RoadGunner/Arcade Particle Track");
                var lit = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
                if (!add || !alpha || !textShader || !trackShader) throw new InvalidOperationException("Arcade particle shaders are missing or failed to compile.");
                if (!lit) throw new InvalidOperationException("URP Particles/Simple Lit shader not found.");
                var glowTex = Png("Vfx Glow", 128, 128, GlowPixel);
                var sparkTex = Png("Vfx Spark", 64, 64, SparkPixel);
                var sparkleTex = Png("Vfx Sparkle Star", 128, 128, SparklePixel);
                var impactTex = Png("Vfx Impact Burst", 128, 128, ImpactPixel);
                var ringTex = Png("Vfx Ring", 256, 256, RingPixel);
                var shockTex = Png("Vfx Shock Ring", 256, 256, ShockPixel);
                var discTex = Png("Vfx Disc", 256, 256, DiscPixel);
                var alertTex = Png("Vfx Alert", 128, 128, AlertPixel);
                var tracerTex = Png("Vfx Tracer Hot", 128, 32, TracerPixel);
                var flameTex = Png("Vfx Muzzle Flame", 128, 64, FlamePixel);
                var muzzleTex = Png("Vfx Muzzle Star", 128, 128, MuzzleStarPixel);
                var dotTex = Png("Vfx Soft Dot", 32, 32, SoftDotPixel);
                var puffTex = Png("Vfx Toon Puff Sheet", 256, 256, (u, v) => PuffPixel(u, v, new Color(.8f, .71f, .82f)));
                var dustTex = Png("Vfx Dust Puff Sheet", 256, 256, (u, v) => PuffPixel(u, v, new Color(.83f, .69f, .6f)));
                var softTex = Png("Vfx Soft Dust", 64, 64, SoftDustPixel);
                var starTex = Png("Vfx Cartoon Star", 128, 128, StarPixel);
                var plusTex = Png("Vfx Repair Plus", 128, 128, PlusPixel);
                var scorchTex = Png("Vfx Scorch", 128, 128, ScorchPixel);
                var crackTex = Png("Vfx Ground Crack", 256, 256, CrackPixel);
                var columnTex = Png("Vfx Light Column", 32, 128, ColumnPixel);
                var boltTex = Png("Vfx Icon Bolt", 128, 128, BoltPixel);
                var heartTex = Png("Vfx Icon Heart", 128, 128, HeartPixel);
                var trackTex = Png("Vfx Tire Track", 64, 16, TrackPixel);
                var haloTex = Png("Vfx Loot Halo", 128, 128, HaloPixel);
                Png("Vfx Confetti", 32, 48, ConfettiPixel, true);
                confetti = AssetDatabase.LoadAssetAtPath<Sprite>(TextureFolder + "/Vfx Confetti.png");

                glow = Mat("Arcade Vfx Glow", add, glowTex, Lin(2.2f, 2.2f, 2.2f));
                core = Mat("Arcade Vfx Core Flash", add, glowTex, Lin(4.2f, 3.8f, 3.1f));
                groundFlash = Mat("Arcade Vfx Ground Flash", alpha, glowTex, Color.white, Plain);
                halo = Mat("Arcade Vfx Loot Halo", alpha, haloTex, Lin(1.3f, .95f, .28f, .75f), Plain);
                spark = Mat("Arcade Vfx Spark", alpha, sparkTex, Lin(2.4f, 2.4f, 2.4f), Plain);
                sparkle = Mat("Arcade Vfx Sparkle", alpha, sparkleTex, Lin(2.2f, 2.2f, 2.2f), Plain);
                ring = Mat("Arcade Vfx Ring", add, ringTex, Lin(2f, 2f, 2f));
                shock = Mat("Arcade Vfx Shock Ring", add, shockTex, Color.white);
                zone = Mat("Arcade Vfx Danger Zone", alpha, discTex, Color.white, Plain);
                dangerEdge = Mat("Arcade Vfx Danger Edge", alpha, shockTex, Color.white, Plain);
                alert = Mat("Arcade Vfx Alert", alpha, alertTex, Lin(1.08f, 1.08f, 1.08f), Plain);
                tracer = Mat("Arcade Vfx Tracer", alpha, tracerTex, Color.white, Plain);
                flame = Mat("Arcade Vfx Muzzle Flame", alpha, flameTex, Lin(2.6f, 1.3f, .25f), Plain);
                impact = Mat("Arcade Vfx Impact Star", alpha, impactTex, Lin(2.4f, 1.3f, .32f), Plain);
                flashStar = Mat("Arcade Vfx Explosion Star", add, null, Color.white);
                muzzleStar = Mat("Arcade Vfx Muzzle Star", alpha, muzzleTex, Lin(3f, 3f, 3f), Plain);
                streak = Mat("Arcade Vfx Speed Streak", alpha, dotTex, Lin(1.7f, 1.7f, 1.7f), Plain);
                puff = Mat("Arcade Vfx Toon Puff", alpha, puffTex, Color.white, m => Toon(m, 1, .05f, new Color(.86f, .8f, .86f), .06f));
                dustPuff = Mat("Arcade Vfx Dust Puff", alpha, dustTex, Color.white, m => Toon(m, 1, .05f, new Color(.86f, .76f, .68f), .05f));
                softDust = Mat("Arcade Vfx Soft Dust", alpha, softTex, Color.white, Plain);
                fire = Mat("Arcade Vfx Toon Fire", alpha, puffTex, Lin(2.3f, 2f, 1.7f), m => Toon(m, 1, .05f, new Color(.78f, .36f, .22f), .13f));
                star = Mat("Arcade Vfx Cartoon Star", alpha, starTex, Lin(1.2f, 1.2f, 1.2f), Plain);
                plus = Mat("Arcade Vfx Repair Plus", alpha, plusTex, Lin(1.15f, 1.15f, 1.15f), Plain);
                scorch = Mat("Arcade Vfx Scorch", alpha, scorchTex, Color.white, Plain);
                crack = Mat("Arcade Vfx Ground Crack", alpha, crackTex, Color.white, Plain);
                chips = Mat("Arcade Vfx Chips Lit", lit, null, Color.white, m => m.SetFloat("_Surface", 0));
                brass = Mat("Arcade Vfx Brass", alpha, null, Lin(1.5f, 1.05f, .3f), Plain);
                column = Mat("Arcade Vfx Light Column", alpha, columnTex, Color.white, Plain);
                iconBolt = Mat("Arcade Vfx Icon Bolt", alpha, boltTex, Lin(1.1f, 1.1f, 1.1f), Plain);
                iconHeart = Mat("Arcade Vfx Icon Heart", alpha, heartTex, Lin(1.1f, 1.1f, 1.1f), Plain);
                text = Mat("Arcade Vfx Numbers", textShader, null, Color.white);
                track = Mat("Arcade Vfx Tire Track", trackShader, trackTex, new Color(.82f, .66f, .5f, .5f), m =>
                {
                    m.SetFloat("_FadeCeil", .3f); m.SetFloat("_FadeFloor", .3f * (1 - TrackFadeSeconds / 14f)); m.SetFloat("_StencilBit", 128);
                });

                quad = SaveMesh("Arcade Vfx flat quad", FlatQuad);
                billboard = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                flameMesh = SaveMesh("Arcade Vfx muzzle flame", FlameMesh);
                casing = SaveMesh("Arcade Vfx shell casing", CasingMesh);
                chip = SaveMesh("Arcade Vfx chip shard", ChipMesh);
                font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        static Color Lin(float r, float g, float b, float a = 1) => new Color(r, g, b, a).gamma;
        static void Plain(Material m) => Toon(m, 0, .04f, Color.white, 0);
        static void Toon(Material m, float erode, float soft, Color rim, float rimWidth)
        {
            m.SetFloat("_Erode", erode); m.SetFloat("_EdgeSoftness", soft); m.SetColor("_RimColor", rim); m.SetFloat("_RimWidth", rimWidth);
            m.SetFloat("_SoftParticles", 0); m.DisableKeyword("_SOFTPARTICLES_ON");
        }

        static Material Mat(string name, Shader shader, Texture texture, Color color, Action<Material> extra = null)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            if (m.shader != shader) m.shader = shader;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            m.enableInstancing = false;
            extra?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Texture2D Png(string name, int width, int height, Func<float, float, Color> pixel, bool sprite = false)
        {
            string path = TextureFolder + "/" + name + ".png";
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = pixel((x + .5f) / width, (y + .5f) / height);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels); byte[] png = texture.EncodeToPNG(); Object.DestroyImmediate(texture);
            if (!File.Exists(path) || !Same(File.ReadAllBytes(path), png)) { File.WriteAllBytes(path, png); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Texture importer missing for " + path);
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply) { if (!Equals(current, wanted)) { apply(wanted); changed = true; } }
            Set(importer.textureType, sprite ? TextureImporterType.Sprite : TextureImporterType.Default, v => importer.textureType = v);
            if (sprite) Set(importer.spriteImportMode, SpriteImportMode.Single, v => importer.spriteImportMode = v);
            Set(importer.alphaIsTransparency, true, v => importer.alphaIsTransparency = v);
            Set(importer.sRGBTexture, true, v => importer.sRGBTexture = v);
            Set(importer.mipmapEnabled, !sprite, v => importer.mipmapEnabled = v);
            Set(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            Set(importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            Set(importer.textureCompression, TextureImporterCompression.Uncompressed, v => importer.textureCompression = v);
            Set(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v);
            Set(importer.maxTextureSize, 256, v => importer.maxTextureSize = v);
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static Mesh SaveMesh(string name, Action<Mesh> fill)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = !mesh;
            if (created) mesh = new Mesh();
            mesh.Clear(); mesh.name = name; fill(mesh); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void DeleteObsoleteAssets()
        {
            foreach (string path in new[]
            {
                MaterialFolder + "/Arcade Vfx Speed Trail.mat", MaterialFolder + "/Arcade Vfx Warning Ring.mat", MaterialFolder + "/Arcade Vfx Brass Lit.mat",
                TextureFolder + "/Vfx Tracer Streak.png", TextureFolder + "/Vfx Soft Line.png",
            })
                if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
        }

        // ---------- procedural pixels (u,v in 0..1, v up) ----------

        static float Sat(float x) => Mathf.Clamp01(x);
        static float Smooth(float a, float b, float x) { float t = Sat((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Vector2 Centered(float u, float v) => new Vector2(u * 2 - 1, v * 2 - 1);
        static Color White(float a) => new Color(1, 1, 1, Sat(a));
        static Color Hex(int rgb, float a = 1) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        static Color GlowPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude;
            return White(.82f * Mathf.Pow(Sat(1 - r), 2.4f) + .45f * Mathf.Exp(-(r / .15f) * (r / .15f)) * Sat(1 - r) * 1.1f);
        }

        // Two-tone convention: RGB 1 = white-hot core, ~.35 = saturated rim once multiplied by an HDR tint. Reads on pale sand and blooms.
        static Color Tone(float core, float alpha, float rim = .35f) { float k = Mathf.Lerp(rim, 1, Sat(core)); return new Color(k, k, k, Sat(alpha)); }

        static Color SparkPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude;
            return Tone(Smooth(.62f, .18f, r), Smooth(.86f, .7f, r));
        }

        static float Ray(float along, float across, float length, float width)
        {
            along = Mathf.Abs(along) / length; if (along >= 1) return 0;
            float w = width * Mathf.Pow(1 - along, .8f) + .01f;
            return Smooth(w, w * .55f, Mathf.Abs(across)) * Smooth(1, .85f, along);
        }

        static Color SparklePixel(float u, float v)
        {
            var p = Centered(u, v); float r = p.magnitude;
            var d = new Vector2(p.x + p.y, p.y - p.x) * .70710678f;
            float rays = Mathf.Max(Mathf.Max(Ray(p.x, p.y, .98f, .16f), Ray(p.y, p.x, .98f, .16f)), Mathf.Max(Ray(d.x, d.y, .55f, .12f), Ray(d.y, d.x, .55f, .12f)));
            float core = Smooth(.3f, .14f, r);
            return Tone(Mathf.Max(core, Smooth(.5f, .1f, r) * rays), Mathf.Max(rays, core));
        }

        // Fat 4-point muzzle star: long cross rays, short diagonal glints, white-hot heart.
        static Color MuzzleStarPixel(float u, float v)
        {
            var p = Centered(u, v); float r = p.magnitude;
            var d = new Vector2(p.x + p.y, p.y - p.x) * .70710678f;
            float rays = Mathf.Max(Mathf.Max(Ray(p.x, p.y, .98f, .24f), Ray(p.y, p.x, .98f, .24f)), Mathf.Max(Ray(d.x, d.y, .42f, .14f), Ray(d.y, d.x, .42f, .14f)));
            float core = Smooth(.34f, .16f, r);
            return Tone(Mathf.Max(core, Smooth(.6f, .12f, r) * rays), Mathf.Max(rays, core), .32f);
        }

        // Comic hit burst: eight alternating spikes, white heart, saturated rim.
        static Color ImpactPixel(float u, float v)
        {
            var p = Centered(u, v); float r = p.magnitude, a = Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2) * 8 + 8;
            float f = a - Mathf.Floor(a), spike = Mathf.Abs(f - .5f) * 2;
            float edge = Mathf.Lerp(.95f, .42f, Mathf.Pow(spike, .7f)) * ((int)Mathf.Floor(a) % 2 == 0 ? 1 : .78f);
            return Tone(Smooth(edge * .62f, edge * .42f, r), Smooth(edge + .02f, edge - .02f, r));
        }

        // Tracer: u along the shot (0 = muzzle, 1 = head), v across. White-hot core, saturated orange edge baked in, so a near-white HDR
        // tint gives a blooming white core with a gold/orange rim that still reads on pale sand.
        static Color TracerPixel(float u, float v)
        {
            float d = Mathf.Abs(v - .5f) * 2;
            float core = Smooth(.4f, .16f, d);
            float alpha = Smooth(.98f, .62f, d) * Smooth(0, .22f, u) * (1 - .25f * Smooth(.97f, 1, u));
            var edge = new Color(.42f, .19f, .035f);
            var c = Color.Lerp(edge, Color.white, core);
            c.a = Sat(alpha);
            return c;
        }

        static Color FlamePixel(float u, float v)
        {
            float w = .46f * Mathf.Pow(Smooth(0, .28f, u), .6f) * Mathf.Pow(1 - u, .7f) + .002f, d = Mathf.Abs(v - .5f);
            return Tone(Smooth(w * .55f, w * .25f, d) * (1 - u * .6f), Smooth(w, w * .8f, d) * (1 - Smooth(.86f, 1, u)), .4f);
        }

        static Color RingPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude, d = r - .84f;
            float band = Mathf.Exp(-(d / .045f) * (d / .045f)), halo = .3f * Mathf.Exp(-(d / .13f) * (d / .13f));
            float inner = r < .84f ? .1f * Smooth(.25f, .84f, r) : 0;
            return White((band + halo + inner) * Sat((1 - r) / .04f));
        }

        // Shockwave / danger edge: crisp bright band at r = .84 with a sharp outside and a soft inner trail.
        static Color ShockPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude, d = r - .84f;
            float band = d > 0 ? Mathf.Exp(-(d / .028f) * (d / .028f)) : Mathf.Exp(-(d / .075f) * (d / .075f));
            float halo = .28f * Mathf.Exp(-(d / .14f) * (d / .14f));
            return White((band + halo) * Sat((1 - r) / .03f));
        }

        // Filled danger disc (radius .94 of the quad), slightly stronger toward its edge so the zone reads as a solid shape.
        static Color DiscPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude;
            return White((.82f + .18f * Smooth(.55f, .94f, r)) * Smooth(.95f, .925f, r));
        }

        static Color LinePixel(float u, float v) { float d = v - .5f; return White(Mathf.Exp(-(d / .2f) * (d / .2f)) * Sat((.5f - Mathf.Abs(d)) / .1f)); }

        static Color SoftDotPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude;
            return White(Mathf.Exp(-(r / .48f) * (r / .48f)) * Sat((1 - r) / .12f));
        }

        // Soft cream dust disc: flat body, soft rim, faintly lighter on top. No outline.
        static Color SoftDustPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude;
            float a = Mathf.Pow(Smooth(1f, .45f, r), 1.3f);
            var c = Color.Lerp(new Color(.86f, .74f, .6f), Color.white, Smooth(.15f, .8f, v));
            c.a = a;
            return c;
        }

        // 2x2 sheet of cartoon cloud blobs: alpha = interior depth (drives toon erosion), RGB = two-tone shading lit from top-left.
        static readonly Vector3[][] PuffCircles =
        {
            new[] { new Vector3(0, -.08f, .52f), new Vector3(-.4f, -.2f, .34f), new Vector3(.4f, -.22f, .36f), new Vector3(-.13f, .3f, .36f), new Vector3(.24f, .26f, .32f) },
            new[] { new Vector3(0, 0, .58f), new Vector3(-.44f, -.12f, .33f), new Vector3(.42f, .1f, .35f), new Vector3(.06f, .42f, .3f) },
            new[] { new Vector3(-.1f, -.12f, .5f), new Vector3(.36f, -.16f, .4f), new Vector3(-.44f, .08f, .32f), new Vector3(.12f, .32f, .37f) },
            new[] { new Vector3(.04f, .04f, .5f), new Vector3(-.36f, .26f, .3f), new Vector3(.38f, .26f, .31f), new Vector3(-.32f, -.3f, .32f), new Vector3(.32f, -.32f, .3f) },
        };
        static Color PuffPixel(float u, float v, Color shade)
        {
            int tile = Mathf.Min(1, (int)(u * 2)) + 2 * Mathf.Min(1, (int)(v * 2));
            var p = new Vector2((u * 2 % 1) * 2 - 1, (v * 2 % 1) * 2 - 1) * .88f;
            var circles = PuffCircles[tile];
            float d = 9, best = -9; Vector2 normal = Vector2.zero;
            foreach (var c in circles)
            {
                var offset = p - new Vector2(c.x, c.y); float di = offset.magnitude - c.z;
                float h = Mathf.Max(.14f - Mathf.Abs(d - di), 0) / .14f; d = Mathf.Min(d, di) - h * h * .14f * .25f;
                if (-di > best) { best = -di; normal = offset / c.z; }
            }
            float facing = Vector2.Dot(normal, new Vector2(-.6f, .8f));
            float lit = Smooth(-.34f, -.2f, facing);
            var rgb = Color.Lerp(shade, Color.white, lit);
            rgb.a = Mathf.Pow(Sat(-d / .42f), .85f);
            return rgb;
        }

        static float Star5(Vector2 p, float r, float rf)
        {
            var k1 = new Vector2(.809016994f, -.587785252f); var k2 = new Vector2(-k1.x, k1.y);
            p.x = Mathf.Abs(p.x);
            p -= 2 * Mathf.Max(Vector2.Dot(k1, p), 0) * k1;
            p -= 2 * Mathf.Max(Vector2.Dot(k2, p), 0) * k2;
            p.x = Mathf.Abs(p.x); p.y -= r;
            var ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0, 1);
            float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0, r);
            return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
        }

        static Color OutlinedShape(float d, float outline, Color rim)
        {
            var c = d > -outline ? rim : Color.white;
            c.a = Smooth(.025f, -.025f, d);
            return c;
        }

        static Color StarPixel(float u, float v) { var p = Centered(u, v) * 1.12f; p.y += .06f; return OutlinedShape(Star5(p, .86f, .5f) - .07f, .13f, new Color(.74f, .42f, .26f)); }

        static float Cross(Vector2 p, Vector2 b)
        {
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)); if (p.y > p.x) p = new Vector2(p.y, p.x);
            var q = p - b; float k = Mathf.Max(q.y, q.x);
            var w = k > 0 ? q : new Vector2(b.y - p.x, -k);
            return Mathf.Sign(k) * Vector2.Max(w, Vector2.zero).magnitude;
        }

        // Repair plus: white face (tinted mint by the particle colour), thick dark-teal outline so it reads on the cream workshop floor.
        static Color PlusPixel(float u, float v) => OutlinedShape(Cross(Centered(u, v) * 1.08f, new Vector2(.7f, .22f)) - .1f, .17f, new Color(.08f, .33f, .3f));

        static float Hash(float x, float y) { float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f; return h - Mathf.Floor(h); }
        static float Noise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y), fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx), Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        }

        static Color ScorchPixel(float u, float v)
        {
            var p = Centered(u, v); float r = p.magnitude, angle = Mathf.Atan2(p.y, p.x);
            float edge = .68f + .12f * (Noise(Mathf.Cos(angle) * 2.2f + 3, Mathf.Sin(angle) * 2.2f + 7) - .5f) * 2 + .05f * Mathf.Sin(angle * 7);
            float a = (1 - Smooth(edge - .3f, edge, r)) * (.7f + .3f * Noise(u * 9, v * 9));
            return White(a * Sat((1 - r) / .05f));
        }

        // Ground crack: a dented centre with seven jagged radial cracks that taper out.
        static Color CrackPixel(float u, float v)
        {
            var p = Centered(u, v); float r = p.magnitude, angle = Mathf.Atan2(p.y, p.x);
            float a = 0;
            for (int k = 0; k < 7; k++)
            {
                float length = .62f + .3f * Hash(k, 3.1f), baseAngle = k * Mathf.PI * 2 / 7 + (Hash(k, 9.7f) - .5f) * .5f;
                if (r > length) continue;
                float wiggle = .2f * Mathf.Sin(r * 11 + k * 2.3f) + .1f * Mathf.Sin(r * 23 + k);
                float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, (baseAngle + wiggle * Sat(r * 3)) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float dist = Mathf.Abs(delta) * r, width = .065f * (1 - r / length) + .012f;
                a = Mathf.Max(a, Smooth(width, width * .45f, dist));
            }
            float dent = Smooth(.2f + .04f * Noise(angle * 2 + 5, 1), .1f, r) * .75f;
            return White(Mathf.Max(a, dent) * Sat((1 - r) / .05f));
        }

        // '!' alert badge: red disc with a dark outline, a white exclamation mark and a soft top highlight (colours baked in).
        static Color AlertPixel(float u, float v)
        {
            var p = Centered(u, v) * 1.06f; float r = p.magnitude;
            float disc = r - .82f;
            float bar = Capsule(p, new Vector2(0, .5f), new Vector2(0, -.08f), .15f);
            float dot = (p - new Vector2(0, -.4f)).magnitude - .14f;
            float glyph = Mathf.Min(bar, dot);
            var fill = Color.Lerp(Hex(0xE8301F), Hex(0xFF6A4A), Sat(p.y * .6f + .45f));
            var c = disc > -.13f ? Hex(0x3A1F14) : fill;
            if (glyph < .0f && disc < -.13f) c = Color.white;
            else if (glyph < .07f && disc < -.13f) c = Color.Lerp(Hex(0x3A1F14), c, Smooth(.05f, .07f, glyph));
            c.a = Smooth(.025f, -.025f, disc);
            return c;
        }

        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            var pa = p - a; var ba = b - a; float h = Sat(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        static readonly Vector2[] BoltShape =
        {
            new Vector2(-.12f, 1f), new Vector2(.53f, 1f), new Vector2(.13f, .25f), new Vector2(.53f, .25f),
            new Vector2(-.32f, -1f), new Vector2(-.07f, -.15f), new Vector2(-.52f, -.15f),
        };

        static float Polygon(Vector2 p, Vector2[] poly)
        {
            float d = float.MaxValue; bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], e = b - a, w = p - a;
                float h = Sat(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, (w - e * h).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside ? -d : d;
        }

        // Gold lightning bolt with a thick dark outline and a white glint stripe (attack upgrade beacon).
        static Color BoltPixel(float u, float v)
        {
            var p = Centered(u, v) / .84f;
            float d = Polygon(p, BoltShape) * .84f;
            var fill = Color.Lerp(Hex(0xFF9A1A), Hex(0xFFE45C), Sat(p.y * .5f + .55f));
            float glint = Polygon(p * 1.45f + new Vector2(.06f, -.12f), BoltShape);
            if (glint < -.03f && p.y > .3f) fill = Color.Lerp(fill, Color.white, .6f);
            var c = d > -.075f ? Hex(0x3A1F14) : fill;
            c.a = Smooth(.02f, -.02f, d);
            return c;
        }

        static float Heart(Vector2 p)
        {
            p.x = Mathf.Abs(p.x);
            if (p.y + p.x > 1) return (p - new Vector2(.25f, .75f)).magnitude - Mathf.Sqrt(2) / 4;
            float a = (p - new Vector2(0, 1)).sqrMagnitude, b = (p - .5f * Mathf.Max(p.x + p.y, 0) * Vector2.one).sqrMagnitude;
            return Mathf.Sqrt(Mathf.Min(a, b)) * Mathf.Sign(p.x - p.y);
        }

        // Mint heart with a dark-teal outline and a white highlight (health upgrade beacon).
        static Color HeartPixel(float u, float v)
        {
            var c0 = Centered(u, v);
            var q = new Vector2(c0.x / 1.45f, c0.y / 1.45f + .5f);
            float d = Heart(q) * 1.45f;
            var fill = Color.Lerp(Hex(0x2FBF86), Hex(0x8CF5C8), Sat(c0.y * .55f + .5f));
            float shine = (c0 - new Vector2(-.36f, .32f)).magnitude;
            fill = Color.Lerp(fill, Color.white, .7f * Smooth(.17f, .1f, shine));
            var c = d > -.09f ? Hex(0x0E4F48) : fill;
            c.a = Smooth(.02f, -.02f, d);
            return c;
        }

        // Hollow soft halo: glow peaks around the gear and stays clear in the middle so the gear itself is never washed out.
        static Color HaloPixel(float u, float v)
        {
            float r = Centered(u, v).magnitude, d = (r - .66f) / .2f;
            return White((Mathf.Exp(-d * d) + .05f) * Smooth(.38f, .55f, r) * Sat((1 - r) / .08f));
        }

        // Soft vertical light column: bright at its foot, fading up and to the sides.
        static Color ColumnPixel(float u, float v)
        {
            float side = Mathf.Pow(Sat(1 - Mathf.Abs(u - .5f) * 2), 1.6f);
            return White(side * Mathf.Pow(Sat(1 - v), 1.4f) * Smooth(0, .06f, v));
        }

        // Tire track profile: u across the 0.38 m stamp, constant along it (v) so overlapping stamps form one ribbon.
        static Color TrackPixel(float u, float v)
        {
            float edge = Smooth(0, .16f, u) * Smooth(1, .84f, u);
            float grooves = 1 - .3f * (Mathf.Exp(-Mathf.Pow((u - .34f) / .045f, 2)) + Mathf.Exp(-Mathf.Pow((u - .66f) / .045f, 2)));
            return White(edge * grooves);
        }

        static Color ConfettiPixel(float u, float v)
        {
            var p = new Vector2(Mathf.Abs(u * 2 - 1), Mathf.Abs(v * 2 - 1)); var q = p - new Vector2(.62f, .72f);
            float d = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - .28f;
            return White(Smooth(.06f, -.06f, d));
        }

        // ---------- procedural meshes ----------

        static void FlatQuad(Mesh m)
        {
            m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        }

        // Three crossed flame tongues along +Z (u along the barrel).
        static void FlameMesh(Mesh m)
        {
            var v = new Vector3[12]; var uv = new Vector2[12]; var t = new int[18]; var n = new Vector3[12]; var c = new Color[12];
            for (int i = 0; i < 3; i++)
            {
                var side = Quaternion.Euler(0, 0, i * 60) * Vector3.right * .5f; int b = i * 4;
                v[b] = -side; v[b + 1] = side; v[b + 2] = side + Vector3.forward; v[b + 3] = -side + Vector3.forward;
                uv[b] = new Vector2(0, 0); uv[b + 1] = new Vector2(0, 1); uv[b + 2] = new Vector2(1, 1); uv[b + 3] = new Vector2(1, 0);
                for (int k = 0; k < 4; k++) { n[b + k] = Quaternion.Euler(0, 0, i * 60) * Vector3.up; c[b + k] = Color.white; }
                int o = i * 6; t[o] = b; t[o + 1] = b + 1; t[o + 2] = b + 2; t[o + 3] = b; t[o + 4] = b + 2; t[o + 5] = b + 3;
            }
            m.vertices = v; m.uv = uv; m.normals = n; m.colors = c; m.triangles = t;
        }

        static void Prism(Mesh m, Vector2[] outline, float depth, Func<int, float> sideShade = null, float capShade = 1)
        {
            int count = outline.Length; var verts = new List<Vector3>(); var tris = new List<int>(); var colors = new List<Color>();
            void Face(Vector3 a, Vector3 b, Vector3 c, float shade)
            {
                int i = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                var col = new Color(shade, shade, shade, 1); colors.Add(col); colors.Add(col); colors.Add(col);
            }
            Vector3 top = Vector3.forward * depth * .5f, bottom = -top;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = outline[i], b = outline[(i + 1) % count];
                float side = sideShade != null ? sideShade(i) : 1;
                Face(top, (Vector3)a + top, (Vector3)b + top, capShade); Face(bottom, (Vector3)b + bottom, (Vector3)a + bottom, capShade);
                Face((Vector3)a + bottom, (Vector3)b + bottom, (Vector3)b + top, side); Face((Vector3)a + bottom, (Vector3)b + top, (Vector3)a + top, side);
            }
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.SetColors(colors);
            m.RecalculateNormals();
        }

        // Octagonal brass casing with baked facet shading (bright/dark stripes, dark open ends): reads as shiny metal with an unlit material.
        static readonly float[] CasingFacets = { 1f, .78f, .55f, .7f, .95f, .66f, .5f, .82f };
        static void CasingMesh(Mesh m)
        {
            var o = new Vector2[8]; for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; o[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .2f; }
            Prism(m, o, 1, i => CasingFacets[i], .42f);
        }

        static void ChipMesh(Mesh m)
        {
            Prism(m, new[] { new Vector2(-.5f, -.32f), new Vector2(.12f, -.5f), new Vector2(.5f, -.08f), new Vector2(.3f, .44f), new Vector2(-.36f, .34f) }, .4f);
        }

        // ---------- scene objects ----------

        static Transform Child(Transform parent, string name, bool rect = false)
        {
            var t = parent.Find(name);
            if (!t)
            {
                var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Arcade VFX");
                t = go.transform; t.SetParent(parent, false);
            }
            return t;
        }

        static void Remove(Transform parent, string name)
        {
            for (int guard = 0; guard < 8; guard++) { var t = parent.Find(name); if (!t) return; Object.DestroyImmediate(t.gameObject); }
        }

        static T Get<T>(Component c) where T : Component { var x = c.GetComponent<T>(); return x ? x : c.gameObject.AddComponent<T>(); }

        static void Prune(Transform parent, string prefix, int keep)
        {
            for (int i = keep; i < keep + 64; i++) { var t = parent.Find(prefix + i); if (t) Object.DestroyImmediate(t.gameObject); }
        }

        static ParticleSystem.MinMaxCurve Curve(params float[] timeValue)
        {
            var keys = new Keyframe[timeValue.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            return new ParticleSystem.MinMaxCurve(1, curve);
        }

        static ParticleSystem.MinMaxGradient Fade(Color end, params float[] timeAlpha) => Grad2(Color.white, end, timeAlpha);

        static ParticleSystem.MinMaxGradient Grad2(Color start, Color end, params float[] timeAlpha)
        {
            var alpha = new GradientAlphaKey[timeAlpha.Length / 2];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = new GradientAlphaKey(timeAlpha[i * 2 + 1], timeAlpha[i * 2]);
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) }, alpha);
            return new ParticleSystem.MinMaxGradient(g);
        }

        static ParticleSystem.MinMaxGradient Grad3(Color a, float tb, Color b, Color c, params float[] timeAlpha)
        {
            var alpha = new GradientAlphaKey[timeAlpha.Length / 2];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = new GradientAlphaKey(timeAlpha[i * 2 + 1], timeAlpha[i * 2]);
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, tb), new GradientColorKey(c, 1) }, alpha);
            return new ParticleSystem.MinMaxGradient(g);
        }

        static ParticleSystem.MinMaxCurve Range(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

        /// <summary>Find-or-create a world-space particle system and reset every module to a known state.</summary>
        static ParticleSystem System(Transform parent, string name, Material material, int max, int order)
        {
            var t = Child(parent, name);
            var ps = Get<ParticleSystem>(t);
            var main = ps.main;
            main.loop = true; main.duration = 1; main.playOnAwake = true; main.prewarm = false; main.startDelay = 0;
            main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.ringBufferMode = ParticleSystemRingBufferMode.Disabled; main.useUnscaledTime = false;
            main.gravityModifier = 0; main.startSize3D = false; main.startRotation3D = false; main.startRotation = 0; main.flipRotation = 0;
            main.startLifetime = .5f; main.startSpeed = 0; main.startSize = 1; main.startColor = Color.white; main.stopAction = ParticleSystemStopAction.None;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0; emission.rateOverDistance = 0; emission.burstCount = 0;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .05f; shape.radiusThickness = 1;
            shape.arc = 360; shape.angle = 25; shape.rotation = Vector3.zero; shape.position = Vector3.zero; shape.scale = Vector3.one;
            shape.alignToDirection = false; shape.randomDirectionAmount = 0; shape.sphericalDirectionAmount = 0; shape.randomPositionAmount = 0;
            var vel = ps.velocityOverLifetime; vel.enabled = false;
            var limit = ps.limitVelocityOverLifetime; limit.enabled = false;
            var inherit = ps.inheritVelocity; inherit.enabled = false;
            var force = ps.forceOverLifetime; force.enabled = false;
            var colorLife = ps.colorOverLifetime; colorLife.enabled = false;
            var colorSpeed = ps.colorBySpeed; colorSpeed.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = false;
            var sizeSpeed = ps.sizeBySpeed; sizeSpeed.enabled = false;
            var rotation = ps.rotationOverLifetime; rotation.enabled = false;
            var rotationSpeed = ps.rotationBySpeed; rotationSpeed.enabled = false;
            var external = ps.externalForces; external.enabled = false;
            var noise = ps.noise; noise.enabled = false;
            var collision = ps.collision; collision.enabled = false;
            var trigger = ps.trigger; trigger.enabled = false;
            var sub = ps.subEmitters; sub.enabled = false;
            var sheet = ps.textureSheetAnimation; sheet.enabled = false;
            var lights = ps.lights; lights.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var custom = ps.customData; custom.enabled = false;
            var r = Get<ParticleSystemRenderer>(t);
            r.sharedMaterial = material; r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
            r.sortingOrder = order; r.sortMode = ParticleSystemSortMode.None; r.minParticleSize = 0; r.maxParticleSize = 4; r.allowRoll = true;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.lengthScale = 1; r.velocityScale = 0; r.cameraVelocityScale = 0; r.mesh = null; r.enableGPUInstancing = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
            return ps;
        }

        static void Drag(ParticleSystem ps, float drag) { var l = ps.limitVelocityOverLifetime; l.enabled = true; l.limit = 200; l.dampen = 0; l.drag = drag; l.multiplyDragByParticleSize = false; l.multiplyDragByParticleVelocity = false; l.separateAxes = false; }
        static void Rise(ParticleSystem ps, float y) { var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = 0; v.y = y; v.z = 0; v.speedModifier = 1; }
        static void SizeLife(ParticleSystem ps, ParticleSystem.MinMaxCurve curve) { var s = ps.sizeOverLifetime; s.enabled = true; s.separateAxes = false; s.size = curve; }
        static void ColorLife(ParticleSystem ps, ParticleSystem.MinMaxGradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = g; }
        static void Spin(ParticleSystem ps, float a, float b) { var r = ps.rotationOverLifetime; r.enabled = true; r.separateAxes = false; r.z = Range(a * Mathf.Deg2Rad, b * Mathf.Deg2Rad); }
        static void Stretch(ParticleSystem ps, float length, float velocity)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = length; r.velocityScale = velocity;
        }
        static void Sheet(ParticleSystem ps)
        {
            var s = ps.textureSheetAnimation; s.enabled = true; s.mode = ParticleSystemAnimationMode.Grid; s.numTilesX = 2; s.numTilesY = 2;
            s.animation = ParticleSystemAnimationType.WholeSheet; s.timeMode = ParticleSystemAnimationTimeMode.Lifetime; s.frameOverTime = Range(0, .999f);
            s.startFrame = 0; s.cycleCount = 1;
        }
        static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, Vector3 rotation, float thickness = 1, float angle = 25)
        {
            var s = ps.shape; s.shapeType = type; s.radius = radius; s.rotation = rotation; s.radiusThickness = thickness; s.angle = angle;
        }
        static void MeshMode(ParticleSystem ps, Mesh mesh)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = mesh; r.alignment = ParticleSystemRenderSpace.World;
            var main = ps.main; main.startRotation3D = true;
            main.startRotationX = Range(0, Mathf.PI * 2); main.startRotationY = Range(0, Mathf.PI * 2); main.startRotationZ = Range(0, Mathf.PI * 2);
            var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true; spin.x = Range(-7, 7); spin.y = Range(-5, 5); spin.z = Range(-7, 7);
        }
        static readonly Vector3 Up = new Vector3(-90, 0, 0), Flat = new Vector3(90, 0, 0);

        static ArcadeVfx BuildSink(ArcadeFeedback hub, GameSession session, Kit kit)
        {
            var root = Child(hub.transform, VfxName);
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
            root.gameObject.SetActive(true);
            // Round-1 objects replaced by the combined numbers mesh, particle speed streaks and the new telegraph zones.
            Remove(root, "Damage numbers"); Remove(root, "Speed trail left"); Remove(root, "Speed trail right"); Remove(root, "Telegraph rings");
            var vfx = Get<ArcadeVfx>(root);
            var cool = new Color(1f, .62f, .35f);
            vfx.repairRate = 12; vfx.wreckSmokeRate = 6;
            vfx.explosionTrauma = .62f; vfx.slamTrauma = .56f; vfx.damageTrauma = .2f; vfx.ramTrauma = .2f; vfx.collisionTrauma = .42f; vfx.killTrauma = .12f;

            vfx.glow = System(root, "Glow bursts", kit.glow, 64, -1);
            SizeLife(vfx.glow, Curve(0, .55f, .2f, 1, 1, .8f)); ColorLife(vfx.glow, Fade(Color.white, 0, 1, .4f, .7f, 1, 0));

            vfx.sparks = System(root, "Spark streaks", kit.spark, 192, -2);
            Stretch(vfx.sparks, 1.6f, .06f);
            var sparkMain = vfx.sparks.main; sparkMain.gravityModifier = 1.4f;
            Drag(vfx.sparks, 2.2f); SizeLife(vfx.sparks, Curve(0, 1, 1, .25f)); ColorLife(vfx.sparks, Fade(cool, 0, 1, .7f, 1, 1, 0));

            vfx.sparkles = System(root, "Sparkle stars", kit.sparkle, 128, -1);
            Shape(vfx.sparkles, ParticleSystemShapeType.Cone, .2f, Up, 1, 32);
            var sparkleMain = vfx.sparkles.main; sparkleMain.gravityModifier = -.12f; sparkleMain.startRotation = Range(0, Mathf.PI * 2);
            Drag(vfx.sparkles, 2.6f); Spin(vfx.sparkles, -160, 160); SizeLife(vfx.sparkles, Curve(0, 0, .12f, 1.15f, .35f, 1, 1, 0)); ColorLife(vfx.sparkles, Fade(Color.white, 0, 1, .8f, 1, 1, 0));

            vfx.muzzleStars = System(root, "Muzzle stars", kit.muzzleStar, 16, 1);
            var muzzleMain = vfx.muzzleStars.main; muzzleMain.startRotation = Range(0, Mathf.PI * 2);

            vfx.puffs = System(root, "Toon smoke puffs", kit.puff, 96, -5);
            Remove(root, "Toon puffs");
            Shape(vfx.puffs, ParticleSystemShapeType.Circle, .35f, Flat, .3f);
            var puffMain = vfx.puffs.main; puffMain.startRotation = Range(-.2f, .2f);
            Drag(vfx.puffs, 4f); Rise(vfx.puffs, .6f); Sheet(vfx.puffs); SizeLife(vfx.puffs, Curve(0, .45f, .25f, 1, 1, 1.25f));
            ColorLife(vfx.puffs, Grad3(new Color(.32f, .3f, .34f), .2f, Color.white, Color.white, 0, 1, .3f, .95f, 1, 0));

            vfx.dustPuffs = System(root, "Sand dust puffs", kit.dustPuff, 160, -5);
            Shape(vfx.dustPuffs, ParticleSystemShapeType.Circle, .35f, Flat, .3f);
            var dustMain = vfx.dustPuffs.main; dustMain.startRotation = Range(-.25f, .25f);
            Drag(vfx.dustPuffs, 4.2f); Rise(vfx.dustPuffs, .45f); Sheet(vfx.dustPuffs); SizeLife(vfx.dustPuffs, Curve(0, .45f, .25f, 1, 1, 1.15f));
            ColorLife(vfx.dustPuffs, Fade(Color.white, 0, 1, .35f, .95f, 1, 0));

            vfx.firePuffs = System(root, "Toon fire puffs", kit.fire, 48, -4);
            Shape(vfx.firePuffs, ParticleSystemShapeType.Sphere, .5f, Vector3.zero);
            var fireMain = vfx.firePuffs.main; fireMain.startRotation = Range(-.2f, .2f);
            Drag(vfx.firePuffs, 2.5f); Rise(vfx.firePuffs, 1.3f); Sheet(vfx.firePuffs); SizeLife(vfx.firePuffs, Curve(0, .4f, .2f, 1, 1, 1.15f)); ColorLife(vfx.firePuffs, Fade(new Color(.85f, .45f, .32f), 0, 1, .3f, 1, 1, 0));

            vfx.stars = System(root, "Cartoon stars", kit.star, 64, -3);
            Shape(vfx.stars, ParticleSystemShapeType.Cone, .2f, Up, 1, 40);
            var starMain = vfx.stars.main; starMain.gravityModifier = 1.1f; starMain.startRotation = Range(0, Mathf.PI * 2);
            Drag(vfx.stars, 1f); Spin(vfx.stars, -320, 320); SizeLife(vfx.stars, Curve(0, 0, .12f, 1.2f, .28f, 1, 1, .7f)); ColorLife(vfx.stars, Fade(Color.white, 0, 1, .75f, 1, 1, 0));

            vfx.rings = System(root, "Ground rings", kit.ring, 24, -8);
            vfx.rings.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.World;
            var ringMain = vfx.rings.main; ringMain.startRotation3D = true; ringMain.startRotationX = Mathf.PI * .5f; ringMain.startRotationY = 0; ringMain.startRotationZ = 0;
            Shape(vfx.rings, ParticleSystemShapeType.Sphere, .01f, Vector3.zero);
            SizeLife(vfx.rings, Curve(0, .12f, .25f, .7f, 1, 1)); ColorLife(vfx.rings, Fade(Color.white, 0, 1, .5f, .6f, 1, 0));

            vfx.chips = System(root, "Debris chips", kit.chips, 64, -3);
            Shape(vfx.chips, ParticleSystemShapeType.Sphere, .2f, Vector3.zero);
            var chipMain = vfx.chips.main; chipMain.gravityModifier = 2.2f;
            MeshMode(vfx.chips, kit.chip); SizeLife(vfx.chips, Curve(0, 1, .75f, 1, 1, 0));

            vfx.casings = System(root, "Shell casings", kit.brass, 32, -3);
            var casingMain = vfx.casings.main; casingMain.gravityModifier = 2.4f; casingMain.startSize = .5f; casingMain.startLifetime = .7f;
            MeshMode(vfx.casings, kit.casing); SizeLife(vfx.casings, Curve(0, 1, .8f, 1, 1, 0));

            vfx.streaks = System(root, "Speed streaks", kit.streak, 48, -4);
            Stretch(vfx.streaks, 2f, .2f);
            ColorLife(vfx.streaks, Fade(Color.white, 0, 0, .25f, 1, .7f, .8f, 1, 0));
            vfx.streakSpeedFraction = .6f; vfx.streakRate = 22;

            Remove(root, "Loot gear halos");
            var halos = Child(root, "Loot halos");
            vfx.lootHalos = new Renderer[HaloSlots];
            for (int i = 0; i < HaloSlots; i++) vfx.lootHalos[i] = Pooled(halos, "Loot halo " + i, kit.billboard, kit.halo, -3);
            Prune(halos, "Loot halo ", HaloSlots);
            vfx.gearTint = new Color(1.5f, 1.12f, .42f, 1);

            vfx.repairPlus = System(root, "Repair plus signs", kit.plus, 48, -2);
            Shape(vfx.repairPlus, ParticleSystemShapeType.Circle, 1.9f, Flat, .12f);
            var plusMain = vfx.repairPlus.main; plusMain.startSize = Range(.85f, 1.15f); plusMain.startLifetime = Range(1.1f, 1.4f);
            plusMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.86f, 1f, .93f), new Color(.62f, .98f, .8f));
            Rise(vfx.repairPlus, 1.3f); SizeLife(vfx.repairPlus, Curve(0, 0, .15f, 1.15f, .3f, 1, 1, .75f)); ColorLife(vfx.repairPlus, Fade(Color.white, 0, 1, .65f, 1, 1, 0));

            vfx.wreckSmoke = System(root, "Wreck smoke", kit.puff, 32, -5);
            Shape(vfx.wreckSmoke, ParticleSystemShapeType.Circle, .5f, Flat, .5f);
            var wreckMain = vfx.wreckSmoke.main; wreckMain.startSpeed = Range(.3f, .8f); wreckMain.startSize = Range(1.1f, 1.6f); wreckMain.startLifetime = Range(1.6f, 2.2f);
            wreckMain.startColor = new ParticleSystem.MinMaxGradient(new Color(.95f, .91f, .85f), new Color(.85f, .79f, .76f)); wreckMain.startRotation = Range(-.2f, .2f);
            Drag(vfx.wreckSmoke, 1f); Rise(vfx.wreckSmoke, 1.4f); Sheet(vfx.wreckSmoke); SizeLife(vfx.wreckSmoke, Curve(0, .5f, .3f, 1, 1, 1.7f));
            ColorLife(vfx.wreckSmoke, Grad3(new Color(.3f, .28f, .32f), .25f, new Color(.75f, .72f, .74f), Color.white, 0, .2f, .15f, .95f, 1, 0));

            BuildNumbers(root, vfx, kit);
            BuildTelegraphs(root, vfx, kit);
            BuildDecals(root, vfx, kit);
            BuildBeacons(root, vfx, kit, session);
            BuildConfetti(root, vfx, kit);
            EditorUtility.SetDirty(vfx);
            return vfx;
        }

        static void BuildNumbers(Transform root, ArcadeVfx vfx, Kit kit)
        {
            var t = Child(root, "Damage numbers mesh");
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            Get<MeshFilter>(t).sharedMesh = null;
            var r = Get<MeshRenderer>(t);
            r.sharedMaterial = kit.text; r.sortingOrder = 22; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; r.allowOcclusionWhenDynamic = false;
            var numbers = Get<ArcadeVfxNumbers>(t);
            numbers.font = kit.font; numbers.fontSize = 96; numbers.capacity = 16;
            numbers.normalHeight = .82f; numbers.lethalHeight = 1.3f; numbers.ramHeight = 1.05f; numbers.healHeight = .9f;
            numbers.mergeWindow = .35f; numbers.normalLife = .78f; numbers.lethalLife = 1.05f; numbers.rise = 1.2f; numbers.outline = .1f; numbers.shadowDrop = .2f;
            vfx.numbers = numbers;
            EditorUtility.SetDirty(numbers); EditorUtility.SetDirty(r);
        }

        static MeshRenderer FlatRenderer(Transform t, Mesh mesh, Material material, int order)
        {
            Get<MeshFilter>(t).sharedMesh = mesh;
            var r = Get<MeshRenderer>(t);
            r.sharedMaterial = material; r.sortingOrder = order; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            foreach (var c in t.GetComponents<Collider>()) Object.DestroyImmediate(c);
            EditorUtility.SetDirty(r);
            return r;
        }

        static void BuildTelegraphs(Transform root, ArcadeVfx vfx, Kit kit)
        {
            var group = Child(root, "Telegraph zones");
            vfx.telegraphZones = new Renderer[TelegraphSlots]; vfx.telegraphProgress = new Renderer[TelegraphSlots];
            vfx.telegraphFronts = new Renderer[TelegraphSlots]; vfx.telegraphEdges = new Renderer[TelegraphSlots]; vfx.telegraphAlerts = new Transform[TelegraphSlots];
            for (int i = 0; i < TelegraphSlots; i++)
            {
                vfx.telegraphZones[i] = Pooled(group, "Zone fill " + i, kit.quad, kit.zone, 1);
                vfx.telegraphProgress[i] = Pooled(group, "Progress disc " + i, kit.quad, kit.zone, 2);
                vfx.telegraphFronts[i] = Pooled(group, "Progress front " + i, kit.quad, kit.shock, 3);
                vfx.telegraphEdges[i] = Pooled(group, "Pulsing edge " + i, kit.quad, kit.dangerEdge, 4);
                vfx.telegraphAlerts[i] = Pooled(group, "Alert badge " + i, kit.billboard, kit.alert, 24).transform;
            }
            Prune(group, "Zone fill ", TelegraphSlots); Prune(group, "Progress disc ", TelegraphSlots); Prune(group, "Progress front ", TelegraphSlots);
            Prune(group, "Pulsing edge ", TelegraphSlots); Prune(group, "Alert badge ", TelegraphSlots);
            vfx.ringTextureRadius = .84f; vfx.discTextureRadius = .94f;
        }

        static MeshRenderer Pooled(Transform group, string name, Mesh mesh, Material material, int order)
        {
            var t = Child(group, name);
            var r = FlatRenderer(t, mesh, material, order);
            t.gameObject.SetActive(false);
            return r;
        }

        static void BuildDecals(Transform root, ArcadeVfx vfx, Kit kit)
        {
            var group = Child(root, "Ground decals");
            vfx.scorches = new Renderer[ScorchSlots]; vfx.cracks = new Renderer[CrackSlots];
            for (int i = 0; i < ScorchSlots; i++) vfx.scorches[i] = Pooled(group, "Scorch " + i, kit.quad, kit.scorch, -9);
            for (int i = 0; i < CrackSlots; i++) vfx.cracks[i] = Pooled(group, "Crack " + i, kit.quad, kit.crack, -9);
            Prune(group, "Scorch ", ScorchSlots); Prune(group, "Crack ", CrackSlots);
        }

        static void BuildBeacons(Transform root, ArcadeVfx vfx, Kit kit, GameSession session)
        {
            int slots = Mathf.Max(MinBeaconSlots, session.runStats && session.runStats.stations != null ? session.runStats.stations.Length : 0);
            var group = Child(root, "Station beacons");
            vfx.beaconRings = new Renderer[slots]; vfx.beaconColumns = new Renderer[slots]; vfx.beaconIcons = new Renderer[slots];
            for (int i = 0; i < slots; i++)
            {
                vfx.beaconRings[i] = Pooled(group, "Beacon ring " + i, kit.quad, kit.shock, -7);
                vfx.beaconColumns[i] = Pooled(group, "Beacon column " + i, kit.billboard, kit.column, -6);
                vfx.beaconIcons[i] = Pooled(group, "Beacon icon " + i, kit.billboard, kit.iconBolt, 23);
            }
            Prune(group, "Beacon ring ", slots); Prune(group, "Beacon column ", slots); Prune(group, "Beacon icon ", slots);
            vfx.attackIconMaterial = kit.iconBolt; vfx.healthIconMaterial = kit.iconHeart;
        }

        static void BuildConfetti(Transform root, ArcadeVfx vfx, Kit kit)
        {
            var overlay = Child(root, "Victory confetti overlay", true);
            var canvas = Get<Canvas>(overlay); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 450; canvas.pixelPerfect = false;
            var scaler = Get<CanvasScaler>(overlay); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
            foreach (var raycaster in overlay.GetComponents<GraphicRaycaster>()) Object.DestroyImmediate(raycaster);
            var group = Get<CanvasGroup>(overlay); group.blocksRaycasts = false; group.interactable = false; group.ignoreParentGroups = true; group.alpha = 1;
            vfx.confettiRoot = overlay.gameObject; vfx.confettiGroup = group; vfx.confetti = new RectTransform[ConfettiPieces];
            for (int i = 0; i < ConfettiPieces; i++)
            {
                var piece = (RectTransform)Child(overlay, "Confetti " + i, true);
                piece.anchorMin = piece.anchorMax = piece.pivot = new Vector2(.5f, .5f); piece.sizeDelta = new Vector2(i % 3 == 0 ? 30 : 24, i % 3 == 0 ? 46 : 38);
                var image = Get<Image>(piece); image.sprite = kit.confetti; image.raycastTarget = false; image.maskable = false; image.color = Color.white;
                vfx.confetti[i] = piece;
                EditorUtility.SetDirty(image);
            }
            Prune(overlay, "Confetti ", ConfettiPieces);
            overlay.gameObject.SetActive(false);
        }

        // ---------- existing effects ----------

        static void UpgradeCombatEffects(GameSession session, Kit kit)
        {
            var e = session.effects;
            var camera = session.input ? session.input.view : null;
            e.view = camera ? camera.transform : session.follow ? session.follow.transform : null;
            // Near-white HDR tints: the baked white core blooms, the baked orange edge keeps the gold/orange rim readable on sand.
            e.playerTracer = Lin(2.6f, 2.4f, 2.1f); e.hostileTracer = Lin(2.8f, 1.15f, .75f);
            e.tracerTime = .075f; e.tracerChase = .65f; e.sparkTime = .1f; e.flashTime = .06f; e.flameFlash = true;
            if (e.tracers != null) foreach (var line in e.tracers)
            {
                if (!line) continue;
                line.sharedMaterial = kit.tracer; line.positionCount = 2; line.useWorldSpace = true;
                line.widthMultiplier = 1; line.widthCurve = new AnimationCurve(new Keyframe(0, .24f), new Keyframe(1, .38f));
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                line.colorGradient = g; line.numCapVertices = 0; line.numCornerVertices = 0; line.alignment = LineAlignment.View; line.textureMode = LineTextureMode.Stretch;
                line.sortingOrder = -2; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                EditorUtility.SetDirty(line);
            }
            var builtinQuad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (e.sparks != null) foreach (var spark in e.sparks)
            {
                if (!spark) continue;
                FlatRenderer(spark, builtinQuad, kit.impact, -1); spark.localScale = Vector3.one * 1.05f; spark.localRotation = Quaternion.identity;
            }
            if (e.flash)
            {
                FlatRenderer(e.flash, kit.flameMesh, kit.flame, -1); e.flash.localScale = new Vector3(.8f, .8f, 1.7f);
            }
            EditorUtility.SetDirty(e);
        }

        static void UpgradeExplosions(GameSession session, Kit kit)
        {
            var list = session.effects.explosions;
            if (list == null || list.Length != 3) throw new InvalidOperationException("CombatEffects must keep exactly three explosion slots.");
            Color charcoal = new Color(.23f, .21f, .25f), enemyRed = new Color(.91f, .28f, .23f);
            foreach (var e in list)
            {
                if (!e) throw new InvalidOperationException("Missing VehicleExplosion slot.");
                bool wasActive = e.gameObject.activeSelf;
                // Fireball: #FFC93C -> #FF7A1A toon puffs, ~1.3x the round-1 size, dark red-orange rim for contrast on sand.
                Restyle(e.fire, kit.fire, 20, -4, new Vector3(0, 1f, 0));
                Shape(e.fire, ParticleSystemShapeType.Sphere, .8f, Vector3.zero);
                var main = e.fire.main; main.startLifetime = Range(.55f, .95f); main.startSize = Range(1.85f, 3.1f); main.startSpeed = Range(1.8f, 4.5f); main.gravityModifier = -.1f;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .79f, .24f), new Color(1f, .48f, .1f)); main.startRotation = Range(-.25f, .25f);
                Drag(e.fire, 2.5f); Rise(e.fire, 1.4f); Sheet(e.fire); SizeLife(e.fire, Curve(0, .4f, .2f, 1, 1, 1.15f));
                ColorLife(e.fire, Grad3(Color.white, .42f, new Color(1f, .74f, .52f), new Color(.66f, .52f, .5f), 0, 1, .45f, .9f, .85f, .2f, 1, 0));

                // Smoke: warm light two-tone (#F3E7D9 lit / baked lilac shade ~#B9A2AC), charcoal only for the first ~0.25 s.
                Restyle(e.smoke, kit.puff, 22, -5, new Vector3(0, .9f, 0));
                Shape(e.smoke, ParticleSystemShapeType.Sphere, .7f, Vector3.zero);
                main = e.smoke.main; main.startLifetime = Range(1.05f, 1.7f); main.startSize = Range(1.4f, 2.3f); main.startSpeed = Range(.8f, 2.2f); main.gravityModifier = 0;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.95f, .91f, .85f), new Color(.9f, .85f, .8f)); main.startRotation = Range(-.25f, .25f);
                Drag(e.smoke, 1.2f); Rise(e.smoke, 1.6f); Sheet(e.smoke); SizeLife(e.smoke, Curve(0, .5f, .3f, 1, 1, 1.6f));
                ColorLife(e.smoke, Grad3(new Color(.3f, .28f, .32f), .16f, new Color(.95f, .93f, .94f), Color.white, 0, .5f, .12f, .95f, 1, 0));

                Restyle(e.dust, kit.dustPuff, 28, -6, new Vector3(0, .25f, 0));
                Shape(e.dust, ParticleSystemShapeType.Circle, .9f, Flat, .2f);
                main = e.dust.main; main.startLifetime = Range(.5f, .9f); main.startSize = Range(.85f, 1.5f); main.startSpeed = Range(3.5f, 6.5f); main.gravityModifier = 0;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.91f, .79f, .6f), new Color(.97f, .88f, .72f)); main.startRotation = Range(-.25f, .25f);
                Drag(e.dust, 3.5f); Rise(e.dust, .4f); Sheet(e.dust); SizeLife(e.dust, Curve(0, .5f, .3f, 1, 1, 1.2f)); ColorLife(e.dust, Fade(Color.white, 0, 1, .3f, .95f, 1, 0));

                Restyle(e.sparks, kit.spark, 32, -2, new Vector3(0, .9f, 0));
                Shape(e.sparks, ParticleSystemShapeType.Sphere, .5f, Vector3.zero);
                Stretch(e.sparks, 1.6f, .06f);
                main = e.sparks.main; main.startLifetime = Range(.35f, .8f); main.startSize = Range(.15f, .24f); main.startSpeed = Range(6f, 14f); main.gravityModifier = 1.6f;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .79f, .24f), new Color(1f, .55f, .15f));
                Drag(e.sparks, 1.2f); SizeLife(e.sparks, Curve(0, 1, 1, .2f)); ColorLife(e.sparks, Fade(new Color(1f, .55f, .25f), 0, 1, .7f, 1, 1, 0));

                // Chunky bouncing debris in team red and charcoal.
                Restyle(e.debris, kit.chips, 12, -3, new Vector3(0, .6f, 0));
                Shape(e.debris, ParticleSystemShapeType.Sphere, .6f, Vector3.zero);
                main = e.debris.main; main.startLifetime = Range(.8f, 1.3f); main.startSize = Range(.34f, .58f); main.startSpeed = Range(4.5f, 8.5f); main.gravityModifier = 1.9f;
                main.startColor = new ParticleSystem.MinMaxGradient(charcoal, enemyRed);
                MeshMode(e.debris, kit.chip); SizeLife(e.debris, Curve(0, 1, .8f, 1, 1, 0));

                // 1-2 frame white-yellow core flash at ~1.5x buggy size.
                e.core = System(e.transform, "Arcade HDR core glow", kit.core, 6, -1);
                ToBurst(e.core, new Vector3(0, 1.1f, 0));
                main = e.core.main; main.startLifetime = Range(.07f, .11f); main.startSize = Range(3.3f, 4f); main.startSpeed = 0;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .93f, .66f), new Color(1f, .8f, .42f));
                Shape(e.core, ParticleSystemShapeType.Sphere, .25f, Vector3.zero);
                SizeLife(e.core, Curve(0, .7f, .35f, 1, 1, .85f)); ColorLife(e.core, Fade(new Color(1f, .75f, .35f), 0, 1, .5f, .8f, 1, 0));

                e.fireCount = 16; e.smokeCount = 18; e.dustCount = 24; e.sparkCount = 28; e.debrisCount = 10; e.coreCount = 4;

                if (e.shockwave)
                {
                    // Additive ground shockwave: crisp band out to ~4 m radius in ~0.25 s (ring at .84 of the half-size -> scale 9.5).
                    FlatRenderer(e.shockwave, kit.quad, kit.shock, -8);
                    e.waveTime = .32f; e.waveFrom = .8f; e.waveTo = 9.5f; e.waveColor = Lin(3f, 2.5f, 1.7f, 1);
                }
                if (e.flash)
                {
                    var flashRenderer = e.flash.GetComponent<MeshRenderer>();
                    if (flashRenderer) { flashRenderer.sharedMaterial = kit.flashStar; flashRenderer.sortingOrder = -1; flashRenderer.shadowCastingMode = ShadowCastingMode.Off; EditorUtility.SetDirty(flashRenderer); }
                    e.flashTime = .16f; e.flashScale = 3.2f; e.flashColor = Lin(3.2f, 2f, .7f, 1);
                }
                // Alpha-blended HDR orange (additive orange would only bleach pale sand to yellow-white).
                var groundFlash = Child(e.transform, "Arcade ground flash"); FlatRenderer(groundFlash, kit.quad, kit.groundFlash, -7); groundFlash.gameObject.SetActive(false); e.groundFlash = groundFlash;
                e.groundFlashTime = .3f; e.groundFlashScale = 5.6f; e.groundFlashColor = Lin(2.2f, .42f, .05f, .85f);
                // The scorch decal now lives in the ArcadeVfx decal pool (fades over ~3.6 s, independent of the 2.4 s slot lifetime).
                Remove(e.transform, "Arcade scorch"); e.scorch = null;

                int total = e.fireCount + e.smokeCount + e.dustCount + e.sparkCount + e.debrisCount + e.coreCount;
                int capacity = e.fire.main.maxParticles + e.smoke.main.maxParticles + e.dust.main.maxParticles + e.sparks.main.maxParticles + e.debris.main.maxParticles + e.core.main.maxParticles;
                if (total < 80 || capacity > 132) throw new InvalidOperationException("Explosion budget broken: burst " + total + ", capacity " + capacity);
                if (e.gameObject.activeSelf != wasActive) e.gameObject.SetActive(wasActive);
                EditorUtility.SetDirty(e);
            }
        }

        /// <summary>Explosion systems: non-looping 2.4 s bursts driven by VehicleExplosion.PlayAt/Clear.</summary>
        static void ToBurst(ParticleSystem ps, Vector3 localPosition)
        {
            ps.transform.localPosition = localPosition; ps.transform.localRotation = Quaternion.identity; ps.transform.localScale = Vector3.one;
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = VehicleExplosion.Lifetime;
        }

        static void Restyle(ParticleSystem ps, Material material, int max, int order, Vector3 localPosition)
        {
            if (!ps) throw new InvalidOperationException("Explosion slot is missing a particle system.");
            var parent = ps.transform.parent; string name = ps.name;
            var rebuilt = System(parent, name, material, max, order);
            if (rebuilt != ps) throw new InvalidOperationException("Explosion particle system name is not unique: " + name);
            ToBurst(ps, localPosition);
        }

        // Soft cream discs (#FCE7B8, alpha ~.45, no outline) that grow .4 -> 1.3 m over ~.45 s; VehicleController only emits above ~2 m/s.
        static void WheelDust(GameSession session, Kit kit)
        {
            var car = session.vehicle;
            if (!car || car.dust == null) return;
            foreach (var ps in car.dust)
            {
                if (!ps) continue;
                var main = ps.main; main.startLifetime = Range(.4f, .5f); main.startSize = Range(1.15f, 1.4f); main.startSpeed = Range(.3f, .8f); main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .97f, .9f, .55f), new Color(1f, .95f, .86f, .48f));
                main.maxParticles = 40; main.gravityModifier = -.04f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startRotation = 0;
                var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .2f; shape.position = new Vector3(0, .25f, -.45f);
                Drag(ps, 2f); SizeLife(ps, Curve(0, .32f, .4f, .75f, 1, 1)); ColorLife(ps, Fade(Color.white, 0, .7f, .15f, 1, .6f, .7f, 1, 0));
                var sheet = ps.textureSheetAnimation; sheet.enabled = false;
                var noise = ps.noise; noise.enabled = false;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = kit.softDust; r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View; r.sortingOrder = -6;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = 4;
                EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
                PrefabUtility.RecordPrefabInstancePropertyModifications(ps); PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
        }

        // Continuous warm tire tracks: stencil-merged multiply ribbon that fades out over ~3.5 s (TireTracks keeps its mesh/pool logic).
        static void TireTracksMaterial(GameSession session, Kit kit)
        {
            var tracks = session.vehicle ? session.vehicle.GetComponentInChildren<TireTracks>(true) : null;
            if (!tracks) tracks = Object.FindAnyObjectByType<TireTracks>(FindObjectsInactive.Include);
            if (!tracks) { Debug.LogWarning("ARCADE VFX: no TireTracks component found; tire track material not assigned."); return; }
            if (tracks.material != kit.track) { tracks.material = kit.track; EditorUtility.SetDirty(tracks); PrefabUtility.RecordPrefabInstancePropertyModifications(tracks); }
        }
    }
}
