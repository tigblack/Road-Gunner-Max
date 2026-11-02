using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Arcade HUD pass: chunky outlined font, procedural sprites and icons, and exactly two panel families —
    /// PRIMARY glossy (HP bar, pause, Resume/Restart CTAs) and SECONDARY translucent navy (chips, ATK/HP pills,
    /// objective, region, hint, toast, boss, map key, recover). Restyles bars, minimap frame + map plate, joystick,
    /// result screens, world labels and enemy bars, plus the HUD juice sink. Presentation only: every referenced
    /// object keeps its name, type, checked rect and exact text; new graphics never raycast. Idempotent.
    /// </summary>
    public static class ArcadeHudStyleInstaller
    {
        const string UiRoot = "Assets/RoadGunner/UI/Arcade";
        const string SpriteFolder = UiRoot + "/Sprites";
        public const string FontPath = UiRoot + "/Fonts/LilitaOne-Regular.ttf";

        // Text + stroke ink.
        static readonly Color Ink = Hex(0x1B2440), InkText = Hex(0x262D45), MutedText = Hex(0x4A5170);
        static readonly Color Cream = Hex(0xFFF7E8), White = Hex(0xFFFDF6), CaptionBlue = Hex(0xC9D5F5), SoftWhite = Hex(0xE6ECFB);
        // Secondary family: the card sprite face is ~0.8 grey, so this tint lands the face on ~#1A2650 navy.
        // UI blends in linear space: 0.95 alpha reads like ~80% (gamma) translucency over the sand.
        static readonly Color NavyTint = Hex(0x213064);
        const float SecondaryAlpha = .95f;
        static readonly Color MapNavy = Hex(0x24304A), RimNavy = Hex(0x1B2340), MapCream = Hex(0xFFF4DA);
        // Primary family.
        static readonly Color ButtonBlue = Hex(0x2F9BFF), ButtonGreen = Hex(0x54CF48), ButtonOrange = Hex(0xFF8A1F), ButtonRed = Hex(0xFF5848);
        static readonly Color HpGreen = Hex(0x62E24C), TrackDark = Hex(0x2B3253), Gold = Hex(0xFFC93C), Red = Hex(0xFF4A3D);
        static readonly Color MintText = Hex(0x7BF0BE), TeamRed = Hex(0xE8473B);
        // Result screens (titles pass 4.5:1 on the cream card).
        static readonly Color PausedBackdrop = Hex(0x241F36), VictoryBackdrop = Hex(0x3A2230), DefeatBackdrop = Hex(0x3A1418);
        static readonly Color VictoryTitle = Hex(0xB0480A), DefeatTitle = Hex(0xB3261E);

        static Font font;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static int created;
        static readonly List<string> notes = new List<string>();

        [MenuItem("Tools/Road Gunner/Arcade/Apply Arcade HUD Style")]
        public static void ApplyMenu() { Apply(ArcadeFeedbackSetup.OpenGameplayScene()); }

        public static void ApplyBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            Apply(session);
            ArcadeFeedbackSetup.Save();
            Debug.Log("ARCADE HUD OK " + string.Join("; ", notes));
        }

        public static void Apply(GameSession session)
        {
            if (!session || !session.hud) throw new InvalidOperationException("Arcade HUD needs the saved GameSession and HudView.");
            var hud = session.hud;
            var frame = hud.designFrame;
            if (!frame) throw new InvalidOperationException("Arcade HUD needs the fitted 'Portrait HUD frame' (run the Anton feedback HUD first).");
            var canvas = hud.GetComponentInParent<Canvas>();
            if (!canvas) throw new InvalidOperationException("HudView is not under a Canvas.");
            canvas = canvas.rootCanvas;
            ArcadeFeedbackSetup.Ensure(session);
            created = 0; notes.Clear();

            EnsureFont();
            EnsureSprites();
            foreach (var text in canvas.GetComponentsInChildren<Text>(true)) { text.font = font; text.fontStyle = FontStyle.Normal; Dirty(text); }

            var juice = new JuiceRefs();
            StyleHeader(hud, juice);
            StyleCounters(hud, juice);
            StyleGuidance(hud, juice);
            StyleRegion(session, frame, juice);
            StyleToast(hud, juice);
            StyleMap(session, frame);
            StyleControls(hud);
            StyleBoss(hud, juice);
            StyleOverlay(session, hud, juice);
            StyleJoystick(session);
            int bars = StyleEnemyBars(session);
            int labels = StyleWorldLabels(session);
            BuildJuice(session, hud, canvas, frame, juice);

            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            notes.Add("font=" + (font ? font.name : "none") + "; enemy bars=" + bars + "; world labels=" + labels + "; new objects=" + created);
        }

        // ---------- font + sprites ----------

        static void EnsureFont()
        {
            if (File.Exists(FontPath))
            {
                var importer = AssetImporter.GetAtPath(FontPath) as TrueTypeFontImporter;
                if (importer == null) { AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceUpdate); importer = AssetImporter.GetAtPath(FontPath) as TrueTypeFontImporter; }
                if (importer != null && (importer.fontTextureCase != FontTextureCase.Dynamic || !importer.includeFontData ||
                    importer.fontRenderingMode != FontRenderingMode.Smooth || importer.characterPadding != 2))
                {
                    importer.fontTextureCase = FontTextureCase.Dynamic; importer.includeFontData = true;
                    importer.fontRenderingMode = FontRenderingMode.Smooth; importer.characterPadding = 2;
                    importer.SaveAndReimport();
                }
                font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            }
            if (!font)
            {
                Debug.LogWarning("ARCADE HUD: " + FontPath + " missing; falling back to LegacyRuntime.");
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return;
            }
            const string probe = "·•AZaz09+/%:";
            var missing = probe.Where(c => !font.HasCharacter(c)).Select(c => "U+" + ((int)c).ToString("X4")).ToArray();
            notes.Add("glyphs U+00B7=" + font.HasCharacter('·') + " U+2022=" + font.HasCharacter('•') + (missing.Length > 0 ? " MISSING " + string.Join(",", missing) : " all present"));
            if (missing.Length > 0) Debug.LogWarning("ARCADE HUD font is missing " + string.Join(",", missing));
        }

        static Sprite S(string name) { if (name == null) return null; sprites.TryGetValue(name, out var s); return s; }

        static void EnsureSprites()
        {
            Directory.CreateDirectory(SpriteFolder);
            sprites.Clear();
            // name, design width/height, render scale, border (L,B,R,T in design units), mipmaps, pixel function.
            Make("panel", 128, 128, 2, new Vector4(44, 52, 44, 44), true, p => Art.Panel(p, new Vector2(64, 64), new Vector2(62.5f, 62.5f), 38, 5.5f, 8, .96f, .87f, .70f));
            Make("button", 128, 128, 2, new Vector4(40, 54, 40, 40), true, p => Art.Panel(p, new Vector2(64, 64), new Vector2(62.5f, 62.5f), 34, 5.5f, 13, .98f, .86f, .60f));
            Make("card", 128, 128, 2, new Vector4(34, 38, 34, 34), true, p => Art.Card(p, new Vector2(64, 64), new Vector2(62.5f, 62.5f), 26, 4, 4.5f));
            Make("pillcard", 128, 64, 2, new Vector4(32, 31, 32, 31), true, p => Art.Card(p, new Vector2(64, 32), new Vector2(63, 31), 31, 3.5f, 3.5f));
            Make("pill", 128, 64, 2, new Vector4(32, 31, 32, 31), true, p => Art.Panel(p, new Vector2(64, 32), new Vector2(63, 31), 31, 0, 0, 1, .93f, 1));
            Make("plate", 128, 64, 2, new Vector4(32, 31, 32, 31), true, p => Art.Panel(p, new Vector2(64, 32), new Vector2(63, 31), 31, 5, 0, 1, .9f, 1));
            Make("trough", 128, 64, 2, new Vector4(32, 31, 32, 31), true, Art.Trough);
            Make("inset", 128, 128, 2, new Vector4(34, 34, 34, 34), true, Art.Inset);
            Make("barfill", 16, 64, 2, Vector4.zero, false, p => Art.Gray(Art.BarValue(p.y / 64f)));
            Make("barcap", 192, 24, 2, Vector4.zero, true, p => Art.Gray(Art.BarValue(p.y / 24f), Art.Cov(Art.Box(p, new Vector2(96, 12), new Vector2(95, 11), 11))));
            Make("gloss", 128, 64, 2, new Vector4(30, 26, 30, 26), true, Art.Gloss);
            Make("circle", 128, 128, 2, Vector4.zero, true, p => Art.Gray(1, Art.Cov(Art.Circle(p, new Vector2(64, 64), 62))));
            Make("badge", 128, 128, 2, Vector4.zero, true, Art.Badge);
            Make("ring", 128, 128, 2, Vector4.zero, true, p => Art.Gray(1, Art.Cov(Mathf.Abs((p - new Vector2(64, 64)).magnitude - 56) - 6)));
            Make("joybase", 128, 128, 2, Vector4.zero, true, Art.JoyBase);
            Make("shadow", 128, 128, 1, new Vector4(60, 60, 60, 60), false, Art.SoftShadow);
            Make("vignette", 128, 128, 1, Vector4.zero, false, Art.Vignette);
            Make("rays", 256, 256, 1, Vector4.zero, true, Art.Rays);
            Make("ribbon", 320, 112, 2, new Vector4(96, 0, 96, 0), true, Art.Ribbon);
            foreach (var icon in Art.Icons()) Make("icon_" + icon.Key, 128, 128, 2, Vector4.zero, true, icon.Value);
            AssetDatabase.Refresh();
        }

        static void Make(string name, int width, int height, int scale, Vector4 border, bool mipmaps, Func<Vector2, Color> pixel)
        {
            Store(name, Art.Render(width, height, scale, pixel), scale, border, mipmaps);
        }

        static void Store(string name, byte[] png, int scale, Vector4 border, bool mipmaps)
        {
            string path = SpriteFolder + "/" + name + ".png";
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png))
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (importer == null) throw new InvalidOperationException("Could not import generated HUD sprite " + path);
            var pixelBorder = border * scale;
            bool changed = importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
                importer.spriteBorder != pixelBorder || !Mathf.Approximately(importer.spritePixelsPerUnit, 100 * scale) ||
                importer.mipmapEnabled != mipmaps || importer.filterMode != FilterMode.Bilinear || importer.wrapMode != TextureWrapMode.Clamp ||
                !importer.alphaIsTransparency || !importer.sRGBTexture || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.isReadable;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            changed |= settings.spriteMeshType != SpriteMeshType.FullRect || settings.spriteGenerateFallbackPhysicsShape;
            if (changed)
            {
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.spriteBorder = pixelBorder; importer.spritePixelsPerUnit = 100 * scale;
                importer.mipmapEnabled = mipmaps; importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true; importer.sRGBTexture = true; importer.isReadable = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite) throw new InvalidOperationException("Generated HUD sprite did not load: " + path);
            sprites[name] = sprite;
        }

        static float TextWidth(string text, int size)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            if (!font) return text.Length * size * .5f;
            font.RequestCharactersInTexture(text, size, FontStyle.Normal);
            float widest = 0, line = 0;
            foreach (char c in text)
            {
                if (c == '\n') { widest = Mathf.Max(widest, line); line = 0; continue; }
                if (font.GetCharacterInfo(c, out var info, size, FontStyle.Normal)) line += info.advance; else line += size * .5f;
            }
            return Mathf.Max(widest, line);
        }

        // ---------- shared UI helpers ----------

        static Color Hex(int rgb, float a = 1) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        static Color A(Color c, float a) { c.a = a; return c; }

        static void Dirty(Object value)
        {
            if (!value) return;
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        static RectTransform Child(Transform parent, string name)
        {
            var t = parent.Find(name) as RectTransform;
            if (t) return t;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            t = (RectTransform)go.transform; t.SetParent(parent, false); created++;
            return t;
        }

        static void Remove(Transform parent, string name)
        {
            if (!parent) return;
            var t = parent.Find(name);
            if (t) { Object.DestroyImmediate(t.gameObject); Dirty(parent.gameObject); }
        }

        static T Get<T>(Component owner) where T : Component
        {
            var c = owner.GetComponent<T>();
            return c ? c : owner.gameObject.AddComponent<T>();
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            if (!r) return;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity; Dirty(r);
        }

        // Centre given in the parent's top-left coordinates; pivot centred so juice can scale/rotate in place.
        static void PlaceCentered(RectTransform r, float cx, float cy, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(cx, -cy); r.sizeDelta = new Vector2(w, h);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity; Dirty(r);
        }

        static void Middle(RectTransform r, float w, float h, Vector2 offset = default)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = offset; r.sizeDelta = new Vector2(w, h);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity; Dirty(r);
        }

        static void Stretch(RectTransform r, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity; Dirty(r);
        }

        static void TopBand(RectTransform r, float inset, float top, float bottomFraction = .5f)
        {
            r.anchorMin = new Vector2(0, bottomFraction); r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = new Vector2(inset, 0); r.offsetMax = new Vector2(-inset, -top);
            r.localScale = Vector3.one; r.localRotation = Quaternion.identity; Dirty(r);
        }

        static Image Img(Component owner, string sprite, Color color, Image.Type type = Image.Type.Sliced, float ppu = 1)
        {
            if (!owner) return null;
            var image = Get<Image>(owner);
            image.sprite = S(sprite); image.type = type; image.color = color; image.raycastTarget = false;
            image.pixelsPerUnitMultiplier = ppu; image.preserveAspect = false; image.fillCenter = true;
            if (!image.enabled) image.enabled = true;
            Dirty(image); return image;
        }

        static Image Icon(RectTransform r, string icon)
        {
            var image = Img(r, "icon_" + icon, Color.white, Image.Type.Simple);
            image.preserveAspect = true; Dirty(image); return image;
        }

        static Image Filled(Component owner, string sprite, Color color)
        {
            var image = Img(owner, sprite, color, Image.Type.Simple);
            image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left; Dirty(image); return image;
        }

        static void DropShadow(Graphic g, float y = -7, float alpha = .32f)
        {
            if (!g) return;
            var shadow = Get<Shadow>(g);
            // Linear-space UI blending makes thin dark alphas almost vanish over bright sand: boost them.
            shadow.effectColor = A(Ink, Mathf.Clamp01(alpha * 1.6f)); shadow.effectDistance = new Vector2(0, y); shadow.useGraphicAlpha = true;
            Dirty(shadow);
        }

        static void NoShadow(Component owner)
        {
            var shadow = owner ? owner.GetComponent<Shadow>() : null;
            if (shadow && shadow.GetType() == typeof(Shadow)) Object.DestroyImmediate(shadow, true);
        }

        /// <summary>The single SECONDARY panel look: translucent navy card with a dark rim and bottom lip.</summary>
        static Image Secondary(Component owner, float ppu = 1.6f, float alpha = SecondaryAlpha)
        {
            if (!owner) return null;
            Remove(owner.transform, "Arcade gloss");
            var image = Img(owner, "card", A(NavyTint, alpha), Image.Type.Sliced, ppu);
            DropShadow(image, -5, .26f);
            return image;
        }

        static Image Gloss(Transform parent, float inset, float top, float alpha, float ppu = 1.6f)
        {
            var r = Child(parent, "Arcade gloss");
            TopBand(r, inset, top);
            r.SetAsFirstSibling();
            return Img(r, "gloss", A(Color.white, alpha), Image.Type.Sliced, ppu);
        }

        static Text Txt(Text t, int size, Color color, TextAnchor align, float stroke = 0, int min = 0)
        {
            if (!t) return null;
            t.font = font; t.fontStyle = FontStyle.Normal; t.fontSize = size; t.color = color; t.alignment = align;
            t.raycastTarget = false; t.supportRichText = true;
            // Lilita's line height is taller than Arial's: Truncate would silently drop a line that no longer fits,
            // so fixed labels overflow and long labels shrink with best fit instead.
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = min > 0 ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = min > 0;
            if (min > 0) { t.resizeTextMinSize = min; t.resizeTextMaxSize = size; }
            var fx = t.GetComponent<ArcadeHudStroke>();
            if (stroke > 0)
            {
                if (!fx) fx = t.gameObject.AddComponent<ArcadeHudStroke>();
                fx.directions = 8;
                fx.Configure(Ink, stroke, A(Ink, .92f), new Vector2(0, -Mathf.Max(2.5f, stroke * 1.15f)));
                Dirty(fx);
            }
            else if (fx) Object.DestroyImmediate(fx, true);
            Dirty(t); return t;
        }

        // Order helpers that are stable when the installer runs again.
        static void Before(Transform a, Transform b)
        {
            if (a && b && a.parent == b.parent && a.GetSiblingIndex() > b.GetSiblingIndex()) a.SetSiblingIndex(b.GetSiblingIndex());
        }

        static void Hide(Transform t) { if (t && t.gameObject.activeSelf) { t.gameObject.SetActive(false); Dirty(t.gameObject); } }

        /// <summary>
        /// Button keeps its rect, an invisible raycast Image and its listeners; a centred face child carries the look:
        /// glossy lipped (primary) or the translucent navy card (secondary).
        /// </summary>
        static RectTransform StyleButton(Button button, Color color, int labelSize, float ppu = 1, string icon = null, float labelLeft = 12, float labelRight = 12, bool secondary = false)
        {
            if (!button) return null;
            var rt = (RectTransform)button.transform;
            var hit = button.GetComponent<Image>();
            if (hit) { hit.sprite = null; hit.type = Image.Type.Simple; hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true; Dirty(hit); }
            var face = Child(rt, "Arcade face");
            Stretch(face); face.SetAsFirstSibling();
            Image faceImage;
            float lip;
            if (secondary)
            {
                faceImage = Secondary(face, ppu);
                lip = 4.5f / ppu;
            }
            else
            {
                faceImage = Img(face, "button", color, Image.Type.Sliced, ppu);
                DropShadow(faceImage, -7, .34f);
                lip = 13 / ppu;
                Gloss(face, 9 / ppu + 4, 7 / ppu + 2, .38f, Mathf.Max(1.2f, ppu * 1.3f));
            }
            var label = rt.Find("Label") ?? face.Find("Label");
            if (label && label.parent != face) label.SetParent(face, false);
            var iconRt = face.Find("Arcade icon") as RectTransform;
            if (!string.IsNullOrEmpty(icon))
            {
                iconRt = Child(face, "Arcade icon");
                float size = Mathf.Min(rt.rect.height - lip - 26, 72);
                if (label && label.gameObject.activeSelf) PlaceCentered(iconRt, labelLeft * .5f + 6, (rt.rect.height - lip) * .5f + 1, size, size);
                else Middle(iconRt, size, size, new Vector2(0, lip * .5f));
                Icon(iconRt, icon);
                iconRt.SetSiblingIndex(secondary ? 0 : 1);
            }
            else if (iconRt) Object.DestroyImmediate(iconRt.gameObject);
            if (label)
            {
                var text = label.GetComponent<Text>();
                Place((RectTransform)label, labelLeft, 4, rt.rect.width - labelLeft - labelRight, rt.rect.height - lip - 6);
                Txt(text, labelSize, White, TextAnchor.MiddleCenter, Mathf.Clamp(labelSize * .1f, 2.5f, 4.5f), Mathf.RoundToInt(labelSize * .65f));
                label.SetAsLastSibling();
            }
            button.targetGraphic = faceImage;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1, 1, 1, 1); colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.80f, .82f, .86f, 1); colors.disabledColor = new Color(.78f, .78f, .8f, .75f);
            colors.colorMultiplier = 1; colors.fadeDuration = .06f;
            button.colors = colors;
            var press = Get<ArcadeHudButton>(button); press.face = face; press.pressedScale = .92f;
            Dirty(button); Dirty(press);
            return face;
        }

        sealed class JuiceRefs
        {
            public Image hpTrail, bossTrail, toastGlow, toastIcon, stateIcon, gestureHand, regionAccent, defeatBadge;
            public RectTransform heart, coinTarget, killsIcon, sealRays, repairIcon, statsAttack, statsHealth, hintPill, regionCard;
            public RectTransform showcaseRays, showcaseIcon, objectiveCard;
            public Graphic gestureThumb;
            public CanvasGroup regionGroup;
            public GameObject repairChip, victoryShowcase, defeatTips;
            public Text showcaseCount;
            public Image[] backdrops = new Image[0];
            public readonly List<CanvasGroup> rows = new List<CanvasGroup>();
            public readonly List<int> rowOrder = new List<int>();
            public void Row(Component owner, int order)
            {
                if (!owner) return;
                var group = Get<CanvasGroup>(owner); group.blocksRaycasts = false; group.interactable = false; group.alpha = 1; Dirty(group);
                rows.Add(group); rowOrder.Add(order);
            }
        }

        // ---------- HUD sections ----------

        static void StyleHeader(HudView h, JuiceRefs juice)
        {
            var header = h.healthText ? h.healthText.transform.parent as RectTransform : null;
            var track = h.healthFill ? h.healthFill.transform.parent as RectTransform : null;
            if (!header || !track) { notes.Add("header missing"); return; }
            var plate = header.GetComponent<Image>(); if (plate) { plate.enabled = false; Dirty(plate); }
            Hide(header.Find("Wordmark")); Hide(header.Find("Sector"));

            // PRIMARY: glossy HP bar.
            var frameRt = Child(header, "Arcade HP frame");
            Place(frameRt, 52, 10, 824, 88);
            DropShadow(Img(frameRt, "pill", Ink, Image.Type.Sliced, 64f / 88f), -6, .30f);
            Place(track, 60, 18, 808, 72);
            Img(track, "trough", TrackDark, Image.Type.Sliced, 64f / 72f);
            var mask = Get<Mask>(track); mask.showMaskGraphic = true; Dirty(mask);
            var trail = Child(track, "HP trail"); Stretch(trail); trail.SetAsFirstSibling();
            juice.hpTrail = Filled(trail, "barfill", Hex(0xFFF1B5)); juice.hpTrail.fillAmount = h.healthFill.fillAmount;
            Stretch(h.healthFill.rectTransform);
            Filled(h.healthFill, "barfill", HpGreen);
            h.healthFill.transform.SetSiblingIndex(1);
            var gloss = Child(track, "HP gloss"); TopBand(gloss, 18, 6, .45f); gloss.SetAsLastSibling();
            Img(gloss, "gloss", A(Color.white, .5f), Image.Type.Sliced, 1.6f);

            var heart = Child(header, "Arcade heart icon");
            PlaceCentered(heart, 56, 54, 118, 118); Icon(heart, "heart");
            juice.heart = heart;
            Place(h.healthText.rectTransform, 116, 17, 744, 72);
            Txt(h.healthText, 40, White, TextAnchor.MiddleCenter, 4);

            // Visible repair state, toggled by the juice sink from RepairChanged (the legacy label is hidden). SECONDARY chip.
            var chip = Child(header, "Arcade repair chip");
            PlaceCentered(chip, 316, 112, 300, 52);
            Secondary(chip, 64f / 52f);
            var wrench = Child(chip, "Arcade repair icon"); PlaceCentered(wrench, 30, 26, 64, 64); Icon(wrench, "wrench");
            var repairLabel = Get<Text>(Child(chip, "Arcade repair label"));
            Place(repairLabel.rectTransform, 60, 2, 232, 46); repairLabel.text = "REPAIRING";
            Txt(repairLabel, 28, MintText, TextAnchor.MiddleCenter, 3);
            chip.gameObject.SetActive(false);
            juice.repairChip = chip.gameObject; juice.repairIcon = wrench;

            foreach (var t in new Transform[] { frameRt, track, heart, h.healthText.transform, chip, h.pause ? h.pause.transform : null })
                if (t) t.SetAsLastSibling();
            var pauseFace = StyleButton(h.pause, ButtonBlue, 42, 1.0f, "pause");
            var pauseLabel = pauseFace ? pauseFace.Find("Label") : null;
            Hide(pauseLabel);
            var pauseIcon = pauseFace ? pauseFace.Find("Arcade icon") as RectTransform : null;
            if (pauseIcon) Middle(pauseIcon, 62, 62, new Vector2(0, 6));
        }

        static void StyleChip(Image chip, string icon, Text label, Text value, out RectTransform iconRt)
        {
            iconRt = null;
            if (!chip) return;
            Secondary(chip, 1.7f);
            iconRt = Child(chip.transform, "Arcade chip icon");
            PlaceCentered(iconRt, 22, 39, 96, 96); Icon(iconRt, icon);
            iconRt.SetSiblingIndex(0);
            if (label) { Place(label.rectTransform, 76, 3, 194, 30); Txt(label, 25, CaptionBlue, TextAnchor.MiddleLeft, 2.5f); }
            if (value) { Place(value.rectTransform, 76, 27, 194, 48); Txt(value, 46, White, TextAnchor.MiddleLeft, 4); }
        }

        static void StyleCounters(HudView h, JuiceRefs juice)
        {
            StyleChip(h.scrapText ? h.scrapText.transform.parent.GetComponent<Image>() : null, "gear", h.scoreLabel, h.scrapText, out juice.coinTarget);
            StyleChip(h.killsText ? h.killsText.transform.parent.GetComponent<Image>() : null, "skull", h.killsLabel, h.killsText, out juice.killsIcon);

            // ATK / HP: the single checked label is split by a mesh effect onto two compact pills in the same rect.
            var stats = h.statsPanel ? h.statsPanel.transform as RectTransform : null;
            if (stats)
            {
                Remove(stats, "Arcade gloss");
                var panelImage = stats.GetComponent<Image>();
                if (panelImage) { NoShadow(panelImage); panelImage.enabled = false; Dirty(panelImage); }
                var atk = Child(stats, "Arcade ATK pill"); Place(atk, 0, 3, 236, 56);
                Img(atk, "pillcard", A(NavyTint, SecondaryAlpha), Image.Type.Sliced, 64f / 56f); DropShadow(atk.GetComponent<Image>(), -5, .26f);
                var hp = Child(stats, "Arcade HP pill"); Place(hp, 286, 3, 236, 56);
                Img(hp, "pillcard", A(NavyTint, SecondaryAlpha), Image.Type.Sliced, 64f / 56f); DropShadow(hp.GetComponent<Image>(), -5, .26f);
                var bolt = Child(stats, "Arcade stats icon"); PlaceCentered(bolt, 16, 31, 72, 72); Icon(bolt, "bolt");
                var heart = Child(stats, "Arcade HP icon"); PlaceCentered(heart, 302, 31, 64, 64); Icon(heart, "heart");
                atk.SetSiblingIndex(0); hp.SetSiblingIndex(1); bolt.SetSiblingIndex(2); heart.SetSiblingIndex(3);
                juice.statsAttack = atk; juice.statsHealth = hp;
            }
            if (h.statsText)
            {
                Place(h.statsText.rectTransform, 0, 0, 560, 62);
                var pills = h.statsText.GetComponent<ArcadeHudStatPills>();
                var stroke = h.statsText.GetComponent<ArcadeHudStroke>();
                // The split must run before the stroke so the outline copies follow the moved glyphs.
                if (!pills) { if (stroke) Object.DestroyImmediate(stroke, true); pills = h.statsText.gameObject.AddComponent<ArcadeHudStatPills>(); }
                else if (stroke)
                {
                    var effects = h.statsText.GetComponents<BaseMeshEffect>().ToList();
                    if (effects.IndexOf(stroke) < effects.IndexOf(pills)) Object.DestroyImmediate(stroke, true);
                }
                pills.groupCentres = new[] { 144f, 286f + 144f }; pills.valueColor = Gold; pills.tintValues = true; Dirty(pills);
                Txt(h.statsText, 32, White, TextAnchor.MiddleCenter, 3.5f);
                h.statsText.transform.SetAsLastSibling();
            }
            if (h.scoreGainText)
            {
                // "+N SCRAP" pops in the scrap chip's caption slot (the juice hides the caption meanwhile), right where
                // the flying gears land, without covering any other panel. Drawn above the chips, below the overlay.
                Place(h.scoreGainText.rectTransform, 104, 148, 204, 46);
                Txt(h.scoreGainText, 36, Gold, TextAnchor.MiddleLeft, 4);
                if (h.overlay && h.overlay.transform.parent == h.scoreGainText.transform.parent) Before(h.scoreGainText.transform, h.overlay.transform);
            }
        }

        static void StyleGuidance(HudView h, JuiceRefs juice)
        {
            var g = h.guidance;
            if (!g) { notes.Add("guidance missing"); return; }
            if (g.objectivePanel)
            {
                // The checked panel rect stays; a child card carries the look so the juice can hug the content
                // (title only -> short card) instead of leaving an empty navy block under the title.
                var own = g.objectivePanel.GetComponent<Image>();
                if (own) { NoShadow(own); own.raycastTarget = false; own.enabled = false; Dirty(own); }
                var card = Child(g.objectivePanel, "Arcade objective card");
                card.anchorMin = new Vector2(0, 1); card.anchorMax = new Vector2(1, 1); card.pivot = new Vector2(.5f, 1);
                card.anchoredPosition = Vector2.zero; card.sizeDelta = new Vector2(0, g.objectivePanel.rect.height);
                card.localScale = Vector3.one; card.localRotation = Quaternion.identity; Dirty(card);
                card.SetAsFirstSibling();
                Secondary(card, 1.5f);
                juice.objectiveCard = card;
                Hide(g.objectivePanel.Find("Objective accent"));
                var badge = Child(g.objectivePanel, "Arcade objective badge");
                PlaceCentered(badge, 514, 44, 76, 76); Icon(badge, "target");
            }
            Txt(g.objectiveText, 38, Gold, TextAnchor.MiddleLeft, 3.5f);
            Txt(g.objectiveDetailText, 29, SoftWhite, TextAnchor.MiddleLeft, 0, 22);
            if (g.directionArrow) { var arrow = g.directionArrow.GetComponent<Graphic>(); if (arrow) { arrow.color = Hex(0x3FD0FF); arrow.raycastTarget = false; Dirty(arrow); } }
            Txt(g.directionLabel, 30, MintText, TextAnchor.MiddleLeft, 2.5f);

            if (g.hintPanel)
            {
                var hint = (RectTransform)g.hintPanel.transform;
                // The hint is a SECONDARY translucent pill sized to its text by the juice (rect, name and text unchanged).
                var own = hint.GetComponent<Image>();
                if (own) { NoShadow(own); own.raycastTarget = false; own.enabled = false; Dirty(own); }
                var pill = Child(hint, "Arcade hint pill");
                pill.anchorMin = pill.anchorMax = new Vector2(0, .5f); pill.pivot = new Vector2(0, .5f);
                pill.anchoredPosition = Vector2.zero; pill.localScale = Vector3.one; pill.localRotation = Quaternion.identity;
                float width = 208 + Mathf.Min(TextWidth("Your gun fires automatically", 34), 784) + 56;
                pill.sizeDelta = new Vector2(Mathf.Clamp(width, 560, 1016), 160); Dirty(pill);
                pill.SetAsFirstSibling();
                var pillImage = Img(pill, "pillcard", A(NavyTint, .93f), Image.Type.Sliced, 64f / 160f);
                DropShadow(pillImage, -6, .26f);
                juice.hintPill = pill;

                var badge = hint.Find("Tip badge") as RectTransform;
                var disc = badge ? badge.GetComponent<ArenaMinimapCircle>() : null;
                if (disc)
                {
                    disc.color = Gold; badge.sizeDelta = new Vector2(104, 104); disc.SetVerticesDirty(); Dirty(disc);
                    var ring = Child(hint, "Arcade tip ring");
                    ring.anchorMin = badge.anchorMin; ring.anchorMax = badge.anchorMax; ring.pivot = badge.pivot;
                    ring.anchoredPosition = badge.anchoredPosition; ring.sizeDelta = new Vector2(118, 118); ring.localScale = Vector3.one;
                    var circle = Get<ArenaMinimapCircle>(ring); circle.color = Hex(0x0E1428); circle.innerRadius = .86f; circle.softness = 0; circle.raycastTarget = false;
                    circle.SetVerticesDirty(); Dirty(circle);
                    Before(ring, badge);
                }
                var tip = hint.Find("Tip badge label");
                if (tip) Txt(tip.GetComponent<Text>(), 30, RimNavy, TextAnchor.MiddleCenter);
                SetDisc(hint.Find("First drive gesture/Gesture pad"), Hex(0x2A3866), 0, 136);
                SetDisc(hint.Find("First drive gesture/Gesture ring"), A(White, .92f), .84f, 116);
                var thumb = hint.Find("First drive gesture/Gesture thumb") as RectTransform;
                SetDisc(thumb, White, 0, 48);
                if (thumb)
                {
                    var hand = Child(thumb, "Arcade gesture hand");
                    hand.anchorMin = hand.anchorMax = new Vector2(.5f, .5f); hand.pivot = new Vector2(46f / 128f, 114f / 128f);
                    hand.anchoredPosition = new Vector2(2, -4); hand.sizeDelta = new Vector2(104, 104);
                    hand.localScale = Vector3.one; hand.localRotation = Quaternion.Euler(0, 0, 14); Dirty(hand);
                    juice.gestureHand = Icon(hand, "hand");
                    juice.gestureThumb = thumb.GetComponent<Graphic>();
                }
                foreach (var graphic in hint.GetComponentsInChildren<Graphic>(true)) if (graphic.raycastTarget) { graphic.raycastTarget = false; Dirty(graphic); }
            }
            Txt(g.hintText, 34, White, TextAnchor.MiddleLeft, 0, 24);
        }

        static void SetDisc(Transform t, Color color, float inner, float size)
        {
            var disc = t ? t.GetComponent<ArenaMinimapCircle>() : null;
            if (!disc) return;
            disc.color = color; disc.innerRadius = inner; disc.raycastTarget = false;
            if (size > 0) ((RectTransform)t).sizeDelta = new Vector2(size, size);
            disc.SetVerticesDirty(); Dirty(disc); Dirty(t);
        }

        static void StyleRegion(GameSession session, RectTransform frame, JuiceRefs juice)
        {
            var region = frame.Find("Exploration region HUD") as RectTransform;
            var view = session.GetComponent<ArenaExplorationHud>();
            if (!region || !view) return;
            Secondary(region, 1.6f);
            var accent = Child(region, "Arcade region accent"); Place(accent, 9, 14, 9, 88);
            juice.regionAccent = Img(accent, "pill", Gold, Image.Type.Sliced, 64f / 9f);
            var icon = Child(region, "Arcade region icon"); PlaceCentered(icon, 56, 58, 76, 76); Icon(icon, "pin");
            accent.SetSiblingIndex(0); icon.SetSiblingIndex(1);
            if (view.regionName) { Place(view.regionName.rectTransform, 100, 6, 450, 40); Txt(view.regionName, 31, Gold, TextAnchor.MiddleLeft, 3, 22); }
            if (view.regionHint) { Place(view.regionHint.rectTransform, 100, 45, 450, 36); Txt(view.regionHint, 27, SoftWhite, TextAnchor.MiddleLeft, 0, 19); }
            if (view.progress) { Place(view.progress.rectTransform, 100, 81, 450, 30); Txt(view.progress, 23, MintText, TextAnchor.MiddleLeft, 0, 17); }
            var group = Get<CanvasGroup>(region); group.blocksRaycasts = false; group.interactable = false; group.alpha = 1; Dirty(group);
            juice.regionCard = region; juice.regionGroup = group;
        }

        static void StyleToast(HudView h, JuiceRefs juice)
        {
            if (!h.upgradeFeedbackPanel) return;
            var toast = (RectTransform)h.upgradeFeedbackPanel.transform;
            var plate = toast.GetComponent<Image>(); if (plate) { plate.enabled = false; Dirty(plate); }
            Remove(toast, "Arcade toast glow");   // older installs put the rays behind the card
            var card = Child(toast, "Arcade toast card"); Stretch(card); card.SetSiblingIndex(0);
            Secondary(card, 1.4f, .97f);
            // Shine: rays centred on the toast icon, clipped to the card face so they never spill behind other panels.
            var shine = Child(toast, "Arcade toast shine"); Stretch(shine, 5, 5, 5, 9); shine.SetSiblingIndex(1);
            var clip = Get<RectMask2D>(shine); Dirty(clip);
            var glow = Child(shine, "Arcade toast glow"); PlaceCentered(glow, 61, 57, 330, 330);
            juice.toastGlow = Img(glow, "rays", A(Hex(0xFFC93C), 0), Image.Type.Simple);
            var icon = Child(toast, "Arcade toast icon"); PlaceCentered(icon, 66, 62, 108, 108); icon.SetSiblingIndex(2);
            juice.toastIcon = Icon(icon, "bolt");
            if (h.upgradeFeedbackText)
            {
                Place(h.upgradeFeedbackText.rectTransform, 122, 8, 424, 108);
                Txt(h.upgradeFeedbackText, 36, Gold, TextAnchor.MiddleCenter, 4, 22);
                h.upgradeFeedbackText.transform.SetAsLastSibling();
            }
        }

        static void StyleMap(GameSession session, RectTransform frame)
        {
            var map = session.minimap;
            var mapFrame = map && map.mapRect ? map.mapRect.parent as RectTransform : null;
            if (!mapFrame) { notes.Add("minimap frame missing"); return; }
            // Thin bezel: navy rim + cream line; the frame discs sit outside mapRect (no Graphics added to the markers).
            SetMapDisc(mapFrame, "Casual map soft shadow", 430, new Vector2(0, -9), A(Ink, .32f), 0, .1f);
            SetMapDisc(mapFrame, "Casual map warm rim", 416, Vector2.zero, RimNavy, 0, 0);
            SetMapDisc(mapFrame, "Casual map cream surround", 404, Vector2.zero, MapCream, 0, 0);
            SetMapDisc(mapFrame, "Casual map rim highlight", 397, Vector2.zero, A(Ink, .5f), .972f, 0);
            // The checked backdrop keeps its name and sibling slot; only its colour changes to a deliberate deep navy.
            var backdrop = map.mapRect.Find("Warm sand behind arena");
            var backdropDisc = backdrop ? backdrop.GetComponent<ArenaMinimapCircle>() : null;
            if (backdropDisc) { backdropDisc.color = MapNavy; backdropDisc.SetVerticesDirty(); Dirty(backdropDisc); }
            else if (backdrop && backdrop.GetComponent<Graphic>() is Graphic g) { g.color = MapNavy; Dirty(g); }
            StyleMapPlate(map);
            var root = frame.Find("Anton feedback HUD");
            var key = root ? root.Find("Map key") : null;
            StyleButton(key ? key.GetComponent<Button>() : null, NavyTint, 30, 1.45f, null, 12, 12, true);
            var legend = map.legendPanel ? map.legendPanel.transform as RectTransform : null;
            if (!legend) return;
            DropShadow(Img(legend, "panel", Cream, Image.Type.Sliced, .95f), -10, .34f);
            foreach (var text in legend.GetComponentsInChildren<Text>(true))
            {
                if (text.transform.parent != legend) continue;
                if (text.name == "Heading") Txt(text, 46, Hex(0x2456C9), TextAnchor.MiddleLeft);
                else if (text.name == "Key rule") Txt(text, 27, MutedText, TextAnchor.MiddleCenter, 0, 18);
                else Txt(text, text.fontSize, InkText, TextAnchor.MiddleLeft, 0, Mathf.Max(16, text.fontSize - 8));
            }
            var close = legend.Find("Close map key");
            StyleButton(close ? close.GetComponent<Button>() : null, ButtonRed, 40, 1.2f);
        }

        static void SetMapDisc(RectTransform parent, string name, float size, Vector2 offset, Color color, float inner, float softness)
        {
            var t = parent.Find(name) as RectTransform;
            var disc = t ? t.GetComponent<ArenaMinimapCircle>() : null;
            if (!disc) return;
            t.sizeDelta = new Vector2(size, size); t.anchoredPosition = offset;
            disc.color = color; disc.innerRadius = inner; disc.softness = softness; disc.raycastTarget = false;
            disc.SetVerticesDirty(); Dirty(disc); Dirty(t);
        }

        /// <summary>
        /// A plain Image (not a marker icon) laid exactly over the baked terrain: deep navy where the bake only holds
        /// its default sand fill, a cream rim along the arena outline and a soft inner shadow. Generated from the bake.
        /// </summary>
        static void StyleMapPlate(ArenaMinimap map)
        {
            var terrain = map.backgroundImage;
            if (!terrain || !map.mapRect) return;
            var sprite = BuildMapPlate(map);
            var existing = map.mapRect.Find("Arcade map plate");
            if (!sprite) { if (existing) Object.DestroyImmediate(existing.gameObject); return; }
            var plate = Child(map.mapRect, "Arcade map plate");
            var source = terrain.rectTransform;
            plate.anchorMin = source.anchorMin; plate.anchorMax = source.anchorMax; plate.pivot = source.pivot;
            plate.anchoredPosition = source.anchoredPosition; plate.sizeDelta = source.sizeDelta;
            plate.localScale = Vector3.one; plate.localRotation = Quaternion.identity; Dirty(plate);
            var image = Img(plate, null, Color.white, Image.Type.Simple);
            image.sprite = sprite; image.maskable = true; image.preserveAspect = false; Dirty(image);
            plate.SetSiblingIndex(source.GetSiblingIndex() + 1);
        }

        static Sprite BuildMapPlate(ArenaMinimap map)
        {
            var texture = map.BackgroundTexture;
            string path = texture ? AssetDatabase.GetAssetPath(texture) : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { notes.Add("map plate skipped (bake source missing)"); return null; }
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(path))) { notes.Add("map plate skipped (bake unreadable)"); return null; }
                int w = decoded.width, h = decoded.height, n = w * h;
                var px = decoded.GetPixels32();
                // The baker fills uncovered texels with one uniform sand colour (later colour grading keeps it uniform):
                // read it from the four corners and flood from the border through that colour only.
                var fill = px[0];
                foreach (int corner in new[] { w - 1, (h - 1) * w, n - 1 })
                    if (Mathf.Abs(px[corner].r - fill.r) > 6 || Mathf.Abs(px[corner].g - fill.g) > 6 || Mathf.Abs(px[corner].b - fill.b) > 6)
                    { notes.Add("map plate skipped (corners differ)"); return null; }
                var outside = new bool[n];
                var queue = new int[n];
                int head = 0, tail = 0;
                bool Match(int i) => Mathf.Abs(px[i].r - fill.r) <= 9 && Mathf.Abs(px[i].g - fill.g) <= 9 && Mathf.Abs(px[i].b - fill.b) <= 9;
                void Seed(int i) { if (!outside[i] && Match(i)) { outside[i] = true; queue[tail++] = i; } }
                for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
                for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
                while (head < tail)
                {
                    int i = queue[head++], x = i % w, y = i / w;
                    if (x > 0) Seed(i - 1);
                    if (x < w - 1) Seed(i + 1);
                    if (y > 0) Seed(i - w);
                    if (y < h - 1) Seed(i + w);
                }
                float fraction = tail / (float)n;
                if (fraction < .02f || fraction > .7f) { notes.Add("map plate skipped (outside fraction " + fraction.ToString("F2") + ")"); return null; }
                var dIn = Chamfer(outside, w, h, true);    // inside texels: distance to the nearest outside texel
                var dOut = Chamfer(outside, w, h, false);  // outside texels: distance to the nearest inside texel
                var colors = new Color32[n];
                Color navy = MapNavy, cream = MapCream, shade = Hex(0x141B30);
                const float rimHalf = 3.6f, shadowWidth = 15;
                for (int i = 0; i < n; i++)
                {
                    float s = outside[i] ? -(dOut[i] - .5f) : dIn[i] - .5f;   // signed: + inside the arena
                    var col = new Color(navy.r, navy.g, navy.b, Mathf.Clamp01(.5f - s));
                    float rim = Mathf.Clamp01(rimHalf + .5f - Mathf.Abs(s - .3f));
                    col = Over(col, cream, rim);
                    float inner = s > rimHalf ? .42f * Mathf.Pow(1 - Mathf.Clamp01((s - rimHalf) / shadowWidth), 1.6f) * Mathf.Clamp01(s - rimHalf + .5f) : 0;
                    col = Over(col, shade, inner);
                    colors[i] = col;
                }
                var output = new Texture2D(w, h, TextureFormat.RGBA32, false);
                try
                {
                    output.SetPixels32(colors); output.Apply(false);
                    Store("minimap_plate", output.EncodeToPNG(), 1, Vector4.zero, false);
                }
                finally { Object.DestroyImmediate(output); }
                notes.Add("map plate outside=" + (fraction * 100).ToString("F0") + "%");
                return S("minimap_plate");
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        static Color Over(Color dst, Color src, float a)
        {
            float outA = a + dst.a * (1 - a);
            if (outA <= 0) return new Color(0, 0, 0, 0);
            var c = (src * a + dst * dst.a * (1 - a)) / outA; c.a = outA; return c;
        }

        /// <summary>Two-pass 3-4 chamfer distance (texels) from texels whose mask differs from <paramref name="inside"/>.</summary>
        static float[] Chamfer(bool[] outside, int w, int h, bool inside)
        {
            const float Far = 1e6f, D1 = 1, D2 = 1.41421f;
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = (outside[i] != inside) ? Far : 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (v == 0) continue;
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + D1);
                    if (y > 0) { v = Mathf.Min(v, d[i - w] + D1); if (x > 0) v = Mathf.Min(v, d[i - w - 1] + D2); if (x < w - 1) v = Mathf.Min(v, d[i - w + 1] + D2); }
                    d[i] = v;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (v == 0) continue;
                    if (x < w - 1) v = Mathf.Min(v, d[i + 1] + D1);
                    if (y < h - 1) { v = Mathf.Min(v, d[i + w] + D1); if (x < w - 1) v = Mathf.Min(v, d[i + w + 1] + D2); if (x > 0) v = Mathf.Min(v, d[i + w - 1] + D2); }
                    d[i] = v;
                }
            return d;
        }

        static void StyleControls(HudView h)
        {
            Txt(h.speedText, 40, White, TextAnchor.MiddleRight, 4);
            // Recover is an emergency action: SECONDARY navy, white icon. Orange stays reserved for primary CTAs.
            StyleButton(h.recover, NavyTint, 28, 1.3f, "recover", 92, 10, true);
            Txt(h.debugText, 25, White, TextAnchor.MiddleLeft, 3);
        }

        static void StyleBoss(HudView h, JuiceRefs juice)
        {
            var boss = h.bossHud;
            if (!boss || !boss.panel) return;
            var panel = (RectTransform)boss.panel.transform;
            Secondary(panel, 1.4f, .96f);
            Txt(boss.titleText, 30, Gold, TextAnchor.MiddleLeft, 3.5f, 20);
            if (boss.healthFill)
            {
                var track = boss.healthFill.transform.parent as RectTransform;
                if (track && track != panel)
                {
                    var barFrame = Child(panel, "Arcade boss bar frame");
                    Place(barFrame, 12, 51, 536, 46);
                    Img(barFrame, "pill", Hex(0x0E1226), Image.Type.Sliced, 64f / 46f);
                    Before(barFrame, track);
                    Place(track, 18, 57, 524, 34);
                    Img(track, "trough", Hex(0x3E2238), Image.Type.Sliced, 64f / 34f);
                    var mask = Get<Mask>(track); mask.showMaskGraphic = true; Dirty(mask);
                    var trail = Child(track, "Boss HP trail"); Stretch(trail); trail.SetAsFirstSibling();
                    juice.bossTrail = Filled(trail, "barfill", Hex(0xFFE59E)); juice.bossTrail.fillAmount = boss.healthFill.fillAmount;
                    Stretch(boss.healthFill.rectTransform);
                    Filled(boss.healthFill, "barfill", Red);
                    boss.healthFill.transform.SetSiblingIndex(1);
                    var gloss = Child(track, "Boss HP gloss"); TopBand(gloss, 12, 4, .42f); gloss.SetAsLastSibling();
                    Img(gloss, "gloss", A(Color.white, .45f), Image.Type.Sliced, 2.2f);
                }
            }
            if (boss.healthText) { Place(boss.healthText.rectTransform, 380, 99, 162, 44); Txt(boss.healthText, 27, White, TextAnchor.MiddleRight, 3); }
            if (boss.phaseText) { Place(boss.phaseText.rectTransform, 18, 99, 362, 44); Txt(boss.phaseText, 26, Hex(0xFFB03B), TextAnchor.MiddleLeft, 3, 16); }
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) { graphic.raycastTarget = false; Dirty(graphic); }
        }

        static void StyleOverlay(GameSession session, HudView h, JuiceRefs juice)
        {
            if (!h.overlayBackdrop || !h.overlayCard) { notes.Add("overlay missing"); return; }
            // Warm dark backdrop (juice retints it per state; alpha stays >= .8 for the contrast check).
            var backdropColor = A(PausedBackdrop, .93f);
            var backdrop = h.overlayBackdrop;
            backdrop.sprite = null; backdrop.type = Image.Type.Simple; backdrop.color = backdropColor; backdrop.raycastTarget = true; Dirty(backdrop);
            var tints = new List<Image> { backdrop };
            // Extend the dim beyond the fitted frame so tall phones do not show bright bands around the card.
            var bands = new[] { ("top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(-4000, 0), new Vector2(4000, 4000)),
                ("bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(-4000, -4000), new Vector2(4000, 0)),
                ("left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-4000, 0), new Vector2(0, 0)),
                ("right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(4000, 0)) };
            for (int i = 0; i < bands.Length; i++)
            {
                var band = Child(backdrop.transform, "Arcade backdrop band " + bands[i].Item1);
                band.anchorMin = bands[i].Item2; band.anchorMax = bands[i].Item3; band.pivot = new Vector2(.5f, .5f);
                band.offsetMin = bands[i].Item4; band.offsetMax = bands[i].Item5; band.localScale = Vector3.one;
                var image = Get<Image>(band); image.sprite = null; image.color = backdropColor; image.raycastTarget = false; Dirty(image); Dirty(band);
                band.SetSiblingIndex(i);
                tints.Add(image);
            }
            juice.backdrops = tints.ToArray();
            var cardRt = h.overlayCard.rectTransform;
            var cardShadow = Child(backdrop.transform, "Arcade card shadow");
            Vector2 cardPos = cardRt.anchoredPosition, cardSize = cardRt.sizeDelta;
            float cardW = cardSize.x;
            Place(cardShadow, cardPos.x - 40, -cardPos.y - 14, cardSize.x + 80, cardSize.y + 84);
            Img(cardShadow, "shadow", A(Hex(0x120A10), .62f), Image.Type.Sliced, .7f);
            Before(cardShadow, cardRt);

            Img(h.overlayCard, "panel", Cream, Image.Type.Sliced, .72f);
            if (h.overlayAccent)
            {
                Place(h.overlayAccent.rectTransform, -46, -40, cardSize.x + 92, 134);
                Img(h.overlayAccent, "ribbon", h.overlayAccent.color, Image.Type.Sliced, 112f / 134f);
                h.overlayAccent.transform.SetAsFirstSibling();
            }
            if (h.overlayKicker)
            {
                Place(h.overlayKicker.rectTransform, 80, -26, cardSize.x - 160, 94);
                Txt(h.overlayKicker, 38, White, TextAnchor.MiddleCenter, 4, 24);
                h.overlayKicker.transform.SetSiblingIndex(1);
            }
            if (h.resultBadge)
            {
                // Same rect, centred pivot: the juice bounces the medal around its centre instead of its corner.
                var r = h.resultBadge;
                var oldPivot = r.pivot; var newPivot = new Vector2(.5f, .5f);
                if (oldPivot != newPivot)
                {
                    r.anchoredPosition += Vector2.Scale(newPivot - oldPivot, r.rect.size);
                    r.pivot = newPivot; Dirty(r);
                }
                var rays = Child(r, "Arcade seal rays"); Middle(rays, 600, 600); rays.SetSiblingIndex(0);
                Img(rays, "rays", A(Hex(0xFFD45A), .92f), Image.Type.Simple);
                var disc = Child(r, "Arcade seal disc"); Middle(disc, 204, 204); disc.SetSiblingIndex(1);
                Img(disc, "badge", Gold, Image.Type.Simple);
                SetDisc(r.Find("Victory ring"), A(Color.white, .85f), .88f, 168);
                var mark = r.Find("Victory mark");
                if (mark) { Middle((RectTransform)mark, 180, 70, new Vector2(0, 4)); Txt(mark.GetComponent<Text>(), 44, White, TextAnchor.MiddleCenter, 4.5f); }
                juice.sealRays = rays;
            }
            // Pause / defeat use the slot the victory seal uses: pause icon, or a big red skull badge on defeat.
            var defeatBadge = Child(cardRt, "Arcade defeat badge");
            PlaceCentered(defeatBadge, cardW * .5f, 168, 200, 200);
            juice.defeatBadge = Img(defeatBadge, "badge", TeamRed, Image.Type.Simple);
            juice.defeatBadge.enabled = false;
            var stateIcon = Child(cardRt, "Arcade state icon");
            PlaceCentered(stateIcon, cardW * .5f, 166, 136, 136);
            juice.stateIcon = Icon(stateIcon, "pause"); juice.stateIcon.enabled = false;
            if (h.resultBadge) Before(stateIcon, h.resultBadge);
            Before(defeatBadge, stateIcon);
            Txt(h.overlayTitle, 88, InkText, TextAnchor.MiddleCenter, 0, 56);
            Txt(h.overlayDescription, 32, MutedText, TextAnchor.MiddleCenter, 0, 22);
            if (h.overlayScore)
            {
                var plate = Child(cardRt, "Arcade score plate");
                Place(plate, 60, 530, cardSize.x - 120, 156);
                Img(plate, "panel", Hex(0xF6E0B2), Image.Type.Sliced, 1.15f);
                Before(plate, h.overlayScore.transform);
                var gear = Child(plate, "Arcade plate gear"); PlaceCentered(gear, 74, 72, 96, 96); Icon(gear, "gear");
                var skull = Child(plate, "Arcade plate skull"); PlaceCentered(skull, cardSize.x - 120 - 74, 72, 96, 96); Icon(skull, "skull");
                Txt(h.overlayScore, 46, InkText, TextAnchor.MiddleCenter, 0, 30);
                juice.Row(plate, 0); juice.Row(h.overlayScore, 0);
            }
            if (h.overlayDetails)
            {
                var strip = Child(cardRt, "Arcade stats strip");
                Place(strip, 56, 694, cardSize.x - 112, 136);
                Img(strip, "inset", Hex(0xF3DFB6), Image.Type.Sliced, 1.1f);
                Before(strip, h.overlayDetails.transform);
                Txt(h.overlayDetails, 28, MutedText, TextAnchor.MiddleCenter, 0, 21);
                juice.Row(strip, 1); juice.Row(h.overlayDetails, 1);
            }
            Txt(h.restartHint, 27, InkText, TextAnchor.MiddleCenter, 0, 18);
            var controls = cardRt.Find("Player controls");
            if (controls) Txt(controls.GetComponent<Text>(), 24, MutedText, TextAnchor.MiddleCenter, 0, 16);
            BuildShowcase(cardRt, cardW, juice);
            BuildDefeatTips(cardRt, cardW, juice);
            if (juice.victoryShowcase) juice.Row(juice.victoryShowcase.transform, 2);
            if (juice.defeatTips) juice.Row(juice.defeatTips.transform, 2);
            StyleButton(h.resume, ButtonGreen, 46, 1.0f);
            StyleButton(h.restart, ButtonOrange, 44, 1.0f);
            StyleMusic(session);
        }

        /// <summary>Victory only: fills the gap above Restart with a reward moment (rays, gear, decorative count-up copy).</summary>
        static void BuildShowcase(RectTransform card, float cardW, JuiceRefs juice)
        {
            var root = Child(card, "Arcade victory showcase");
            float w = cardW - 120;
            Place(root, 60, 832, w, 280);
            var rays = Child(root, "Arcade showcase rays"); PlaceCentered(rays, w * .5f, 106, 440, 440);
            Img(rays, "rays", A(Hex(0xFFA526), .9f), Image.Type.Simple);
            var gear = Child(root, "Arcade showcase gear"); PlaceCentered(gear, w * .5f, 104, 168, 168); Icon(gear, "gear");
            var pill = Child(root, "Arcade showcase pill"); PlaceCentered(pill, w * .5f, 224, 260, 78);
            Img(pill, "pillcard", A(NavyTint, 1), Image.Type.Sliced, 64f / 78f);
            var count = Get<Text>(Child(pill, "Arcade showcase count"));
            Stretch(count.rectTransform, 10, 2, 10, 8); count.text = "+0";
            Txt(count, 50, Gold, TextAnchor.MiddleCenter, 4.5f);
            rays.SetSiblingIndex(0); gear.SetSiblingIndex(1); pill.SetSiblingIndex(2);
            // Low in the card's draw order: the rays glow behind the stats strip and the Restart button, never over text.
            root.SetSiblingIndex(Mathf.Min(2, card.childCount - 1));
            root.gameObject.SetActive(false);
            juice.victoryShowcase = root.gameObject; juice.showcaseRays = rays; juice.showcaseIcon = gear; juice.showcaseCount = count;
        }

        /// <summary>Defeat only: two SECONDARY tiles echoing the description (upgrade stations, repair workshop).</summary>
        static void BuildDefeatTips(RectTransform card, float cardW, JuiceRefs juice)
        {
            var root = Child(card, "Arcade defeat tips");
            float w = cardW - 120, tileW = (w - 40) * .5f;
            Place(root, 60, 864, w, 220);
            string[] icons = { "bolt", "wrench" };
            string[] captions = { "UPGRADE\nSTATIONS", "REPAIR\nWORKSHOP" };
            for (int i = 0; i < 2; i++)
            {
                var tile = Child(root, "Arcade tip tile " + i); Place(tile, i * (tileW + 40), 10, tileW, 196);
                Img(tile, "card", A(NavyTint, 1), Image.Type.Sliced, 1.2f);
                var icon = Child(tile, "Arcade tip icon"); PlaceCentered(icon, 84, 96, 116, 116); Icon(icon, icons[i]);
                var caption = Get<Text>(Child(tile, "Arcade tip caption"));
                Place(caption.rectTransform, 148, 18, tileW - 160, 160); caption.text = captions[i];
                Txt(caption, 34, White, TextAnchor.MiddleLeft, 3, 24);
            }
            root.gameObject.SetActive(false);
            juice.defeatTips = root.gameObject;
        }

        static void StyleMusic(GameSession session)
        {
            var music = session.GetComponent<ArenaMusic>();
            if (!music || !music.volumeControl || !music.volumeSlider) return;
            foreach (var text in music.volumeControl.GetComponentsInChildren<Text>(true)) Txt(text, 30, InkText, text.alignment);
            var slider = music.volumeSlider;
            var track = slider.transform.Find("Track") as RectTransform;
            if (track) { Place(track, -8, 18, 448, 28); Img(track, "trough", Hex(0xD8BC88), Image.Type.Sliced, 64f / 28f); }
            if (slider.fillRect)
            {
                var fillArea = slider.fillRect.parent as RectTransform;
                // Fill stays <= 20 reference units tall (checked); the Slider drives its x anchors.
                if (fillArea && fillArea != slider.transform) Place(fillArea, 0, 23, 432, 18);
                slider.fillRect.offsetMin = slider.fillRect.offsetMax = Vector2.zero; Dirty(slider.fillRect);
                Img(slider.fillRect, "pill", ButtonGreen, Image.Type.Sliced, 64f / 18f);
            }
            if (slider.handleRect)
            {
                slider.handleRect.sizeDelta = new Vector2(52, 52); Dirty(slider.handleRect);
                var handle = Img(slider.handleRect, "badge", Color.white, Image.Type.Simple);
                handle.preserveAspect = true;
                DropShadow(handle, -4, .3f);
            }
            Dirty(slider);
        }

        static void StyleJoystick(GameSession session)
        {
            var joystick = session.input ? session.input.joystick : null;
            if (!joystick || !joystick.ring) return;
            Img(joystick.ring, "joybase", Color.white, Image.Type.Simple);
            // The activation zone is the frame's first sibling (raycast fallback), so cards drew over the held stick.
            // A nested sorting canvas lifts only the ring visuals; it has no raycaster and its graphics never raycast.
            var ringCanvas = Get<Canvas>(joystick.ring);
            ringCanvas.overrideSorting = true; ringCanvas.sortingOrder = 5; Dirty(ringCanvas);
            var inner = joystick.ring.Find("Joystick inner");
            if (inner) Img(inner, "circle", A(Color.white, .10f), Image.Type.Simple);
            if (joystick.thumb)
            {
                joystick.thumb.sizeDelta = new Vector2(112, 112); Dirty(joystick.thumb);
                DropShadow(Img(joystick.thumb, "badge", Color.white, Image.Type.Simple), -7, .35f);
            }
        }

        static int StyleEnemyBars(GameSession session)
        {
            int count = 0;
            foreach (var bar in Object.FindObjectsByType<EnemyHealthBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!bar || !bar.worldCanvas || bar.gameObject.scene != session.gameObject.scene) continue;
                var rect = (RectTransform)bar.worldCanvas.transform;
                // Checked: 1.7-3 m wide and >= .2 m tall at the authored .01 canvas scale.
                rect.sizeDelta = bar.target is EnemyBuggy ? new Vector2(222, 26) : new Vector2(178, 24); Dirty(rect);
                var background = rect.Find("HealthBarBackground");
                var backgroundImage = background ? background.GetComponent<Image>() : null;
                if (backgroundImage)
                {
                    Stretch(backgroundImage.rectTransform);
                    Img(backgroundImage, "pill", Hex(0x2B2233), Image.Type.Sliced, 64f / rect.sizeDelta.y);
                    DropShadow(backgroundImage, -3, .3f);
                }
                var track = background ? background.Find("HealthBarTrack") as RectTransform : null;
                var trackImage = track ? track.GetComponent<Image>() : null;
                if (trackImage) { Stretch(track, 3, 3, 3, 3); Img(trackImage, "trough", Hex(0x3A3540), Image.Type.Sliced, 64f / (rect.sizeDelta.y - 6)); }
                if (bar.healthFill)
                {
                    float fill = bar.healthFill.fillAmount;
                    var fillParent = bar.healthFill.transform.parent;
                    var trail = Child(fillParent, "Arcade damage trail"); Stretch(trail, 1, 1, 1, 1);
                    var trailImage = Filled(trail, "barcap", A(Color.white, .96f)); trailImage.fillAmount = fill;
                    Before(trail, bar.healthFill.transform);   // stable on re-runs: the trail always draws under the fill
                    Stretch(bar.healthFill.rectTransform, 1, 1, 1, 1);
                    Filled(bar.healthFill, "barcap", TeamRed);
                    bar.healthFill.fillAmount = fill; Dirty(bar.healthFill);
                    var shine = Child(fillParent, "Arcade bar shine"); TopBand(shine, 5, 2, .55f); shine.SetAsLastSibling();
                    Img(shine, "gloss", A(Color.white, .22f), Image.Type.Sliced, 3f);
                    bar.damageTrail = trailImage;
                }
                var group = Get<CanvasGroup>(bar.worldCanvas); group.blocksRaycasts = false; group.interactable = false; Dirty(group);
                bar.fadeGroup = group; bar.fullHealthAlpha = .9f;
                Dirty(bar);
                count++;
            }
            return count;
        }

        const string LegacyShadow = "Arcade label shadow";
        const string OutlinePrefix = "Arcade label outline ";
        const string PlateName = "Arcade label plate";

        static bool OwnedByArcadePass(Transform t)
        {
            for (var p = t; p; p = p.parent) if (p.name.StartsWith("Arcade ", StringComparison.Ordinal)) return true;
            return false;
        }

        struct LabelStyle { public Color text, outline, plate; public bool hasOutline, hasPlate; public float outlineEm; }

        static int StyleWorldLabels(GameSession session)
        {
            if (!font) return 0;
            var stationLabels = new HashSet<TextMesh>();
            if (session.runStats && session.runStats.stations != null)
                foreach (var station in session.runStats.stations)
                    if (station && station.displayText) { stationLabels.Add(station.displayText); station.displayText.characterSize = .11f; Dirty(station.displayText); }
            var plateSprite = S("plate");
            int count = 0;
            foreach (var label in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!label || label.gameObject.scene != session.gameObject.scene) continue;
                if (label.name == LegacyShadow || label.name.StartsWith(OutlinePrefix, StringComparison.Ordinal)) continue;
                if (OwnedByArcadePass(label.transform))
                {
                    // Other arcade passes (e.g. VFX damage numbers) style their own text; undo anything an older run added.
                    var stray = label.transform.Find(LegacyShadow);
                    if (stray) { Object.DestroyImmediate(stray.gameObject); EditorUtility.SetDirty(label.gameObject); }
                    continue;
                }
                var style = new LabelStyle { text = White, outline = Hex(0x2A1A10), plate = A(RimNavy, .93f), hasOutline = true, hasPlate = true, outlineEm = .075f };
                if (stationLabels.Contains(label)) style = new LabelStyle { text = RimNavy, plate = Gold, hasOutline = false, hasPlate = true };
                else if (label.name == "Repair hub title") style = new LabelStyle { text = White, outline = Hex(0x0E4F48), hasOutline = true, hasPlate = false, outlineEm = .085f };
                else if (label.name == "Repair direction")
                {
                    style = new LabelStyle { text = White, outline = Hex(0x0E4F48), plate = A(Hex(0x2FBF8A), .96f), hasOutline = true, hasPlate = true, outlineEm = .075f };
                    // Pull the entrance label in over the lane edge, just inside the entrance, so it stays on screen and
                    // clear of the HUD cards at the gameplay zoom (signs sit at z = +-11 of the hub; the label moves 3 m in).
                    var sign = label.transform.parent;
                    float inward = sign && sign.localPosition.z > 0 ? -3f : sign && sign.localPosition.z < 0 ? 3f : 0;
                    label.transform.localPosition = new Vector3(2.6f, 2.3f, inward); Dirty(label.transform);
                }

                label.font = font; label.fontStyle = FontStyle.Normal; label.color = style.text; Dirty(label);
                var renderer = label.GetComponent<MeshRenderer>();
                if (renderer && font.material) { renderer.sharedMaterial = font.material; Dirty(renderer); }
                // Keep world labels at their authored order (0): a raised order draws them over the camera-space HUD in captures.
                if (renderer && renderer.sortingOrder != 0) { renderer.sortingOrder = 0; Dirty(renderer); }
                var legacy = label.transform.Find(LegacyShadow);
                if (legacy) Object.DestroyImmediate(legacy.gameObject);

                float em = label.characterSize * Mathf.Max(1, label.fontSize > 0 ? label.fontSize : font.fontSize) * .1f;
                var outlines = new List<TextMesh>();
                for (int i = 0; i < 8; i++)
                {
                    var existing = label.transform.Find(OutlinePrefix + i);
                    if (!style.hasOutline) { if (existing) Object.DestroyImmediate(existing.gameObject); continue; }
                    if (!existing)
                    {
                        var go = new GameObject(OutlinePrefix + i, typeof(MeshRenderer), typeof(TextMesh));
                        go.layer = label.gameObject.layer; existing = go.transform; existing.SetParent(label.transform, false); created++;
                    }
                    var copy = existing.GetComponent<TextMesh>();
                    copy.font = font; copy.fontStyle = FontStyle.Normal; copy.fontSize = label.fontSize; copy.characterSize = label.characterSize;
                    copy.anchor = label.anchor; copy.alignment = label.alignment; copy.lineSpacing = label.lineSpacing; copy.tabSize = label.tabSize;
                    copy.richText = label.richText; copy.offsetZ = label.offsetZ; copy.text = label.text; copy.color = style.outline;
                    float angle = i * Mathf.PI / 4;
                    float radius = style.outlineEm * em * (i % 2 == 1 ? .92f : 1);
                    // Slightly heavier below the glyphs for a chunky drop.
                    existing.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius - (Mathf.Sin(angle) < -.1f ? radius * .45f : 0), 0);
                    existing.localRotation = Quaternion.identity; existing.localScale = Vector3.one;
                    var copyRenderer = existing.GetComponent<MeshRenderer>();
                    copyRenderer.sharedMaterial = font.material;
                    copyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; copyRenderer.receiveShadows = false;
                    if (renderer) { copyRenderer.sortingLayerID = renderer.sortingLayerID; copyRenderer.sortingOrder = renderer.sortingOrder - 1; }
                    var stale = existing.GetComponent<ArcadeHudWorldLabel>(); if (stale) Object.DestroyImmediate(stale);
                    Dirty(copy); Dirty(copyRenderer); Dirty(existing);
                    outlines.Add(copy);
                }
                var plateTransform = label.transform.Find(PlateName);
                SpriteRenderer plate = null;
                if (style.hasPlate && plateSprite)
                {
                    if (!plateTransform)
                    {
                        var go = new GameObject(PlateName, typeof(SpriteRenderer));
                        go.layer = label.gameObject.layer; plateTransform = go.transform; plateTransform.SetParent(label.transform, false); created++;
                    }
                    plate = plateTransform.GetComponent<SpriteRenderer>();
                    plate.sprite = plateSprite; plate.drawMode = SpriteDrawMode.Sliced; plate.color = style.plate;
                    plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; plate.receiveShadows = false;
                    if (renderer) { plate.sortingLayerID = renderer.sortingLayerID; plate.sortingOrder = renderer.sortingOrder - 2; }
                    plateTransform.localRotation = Quaternion.identity;
                    Dirty(plate); Dirty(plateTransform);
                }
                else if (plateTransform) Object.DestroyImmediate(plateTransform.gameObject);
                var styler = Get<ArcadeHudWorldLabel>(label);
                styler.source = null; styler.outlines = outlines.ToArray(); styler.plate = plate;
                styler.platePadding = new Vector2(.42f, .22f); styler.plateDepth = .02f;
                Dirty(styler);
                count++;
            }
            return count;
        }

        static void BuildJuice(GameSession session, HudView hud, Canvas canvas, RectTransform frame, JuiceRefs refs)
        {
            var layer = Child(frame, "Arcade HUD juice");
            Stretch(layer);
            layer.SetAsLastSibling();
            var region = frame.Find("Exploration region HUD");
            if (region) region.SetAsLastSibling();
            var juice = Get<ArcadeHudJuice>(layer);
            juice.session = session; juice.hud = hud; juice.canvas = canvas; juice.flyLayer = layer;
            var coins = new Image[12];
            for (int i = 0; i < coins.Length; i++)
            {
                var r = Child(layer, "Arcade fly gear " + i); Middle(r, 72, 72);
                coins[i] = Icon(r, "gear"); r.gameObject.SetActive(false);
            }
            var stars = new Image[10];
            for (int i = 0; i < stars.Length; i++)
            {
                var r = Child(layer, "Arcade burst star " + i); Middle(r, 66, 66);
                stars[i] = Icon(r, "star"); r.gameObject.SetActive(false);
            }
            juice.coins = coins; juice.stars = stars;
            juice.gearSprite = S("icon_gear"); juice.starSprite = S("icon_star");
            juice.attackIcon = S("icon_bolt"); juice.healthIcon = S("icon_heart");
            juice.coinTarget = refs.coinTarget; juice.heart = refs.heart; juice.killsIcon = refs.killsIcon;
            juice.sealRays = refs.sealRays; juice.repairChip = refs.repairChip; juice.repairIcon = refs.repairIcon;
            juice.hpTrail = refs.hpTrail; juice.bossTrail = refs.bossTrail; juice.toastGlow = refs.toastGlow; juice.toastIcon = refs.toastIcon;
            juice.stateIcon = refs.stateIcon; juice.pausedIcon = S("icon_pause"); juice.defeatIcon = S("icon_skull");
            juice.damageColor = new Color(.95f, .05f, .12f, 1); juice.healColor = new Color(.30f, .94f, .62f, 1);
            juice.phaseColor = Hex(0xFFB03B); juice.phaseFlashColor = Hex(0xFFF58C);
            juice.statsAttack = refs.statsAttack; juice.statsHealth = refs.statsHealth;
            juice.objectiveCard = refs.objectiveCard;
            juice.hintPill = refs.hintPill; juice.gestureHand = refs.gestureHand; juice.gestureThumb = refs.gestureThumb;
            juice.hintTextLeft = 208; juice.hintTextMax = 784; juice.hintPillPadding = 56; juice.hintPillMin = 560; juice.hintPillMax = 1016;
            juice.regionCard = refs.regionCard; juice.regionGroup = refs.regionGroup; juice.regionAccent = refs.regionAccent;
            juice.backdrops = refs.backdrops;
            juice.pausedBackdrop = PausedBackdrop; juice.victoryBackdrop = VictoryBackdrop; juice.defeatBackdrop = DefeatBackdrop;
            juice.pausedTitle = InkText; juice.victoryTitle = VictoryTitle; juice.defeatTitle = DefeatTitle;
            juice.defeatBadge = refs.defeatBadge;
            juice.victoryShowcase = refs.victoryShowcase; juice.defeatTips = refs.defeatTips;
            juice.showcaseRays = refs.showcaseRays; juice.showcaseIcon = refs.showcaseIcon; juice.showcaseCount = refs.showcaseCount;
            juice.resultRows = refs.rows.ToArray(); juice.resultRowOrder = refs.rowOrder.ToArray();

            // Full-screen edge flash below every HUD element (also covers tall-phone bands outside the frame).
            var vignette = Child(canvas.transform, "Arcade screen vignette");
            Stretch(vignette); vignette.SetAsFirstSibling();
            var image = Img(vignette, "vignette", A(Color.red, 0), Image.Type.Simple);
            image.enabled = false;
            juice.vignette = image;
            Dirty(juice);
            ArcadeFeedbackSetup.Register(session, juice);
        }

        // ---------- procedural art (design units, rendered supersampled) ----------

        static class Art
        {
            static readonly Color InkColor = Hex(0x1B2440);
            static float aa = 2;

            public static byte[] Render(int width, int height, int scale, Func<Vector2, Color> pixel)
            {
                aa = scale;
                int w = width * scale, h = height * scale;
                var colors = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        colors[y * w + x] = pixel(new Vector2((x + .5f) / scale, (y + .5f) / scale));
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.SetPixels32(colors); texture.Apply(false);
                byte[] png = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);
                return png;
            }

            public static float Cov(float d) => Mathf.Clamp01(.5f - d * aa);
            public static Color Gray(float v, float a = 1) => new Color(v, v, v, a);
            public static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
            public static float Box(Vector2 p, Vector2 c, Vector2 half, float r)
            {
                var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
                return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
            }
            public static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
            {
                Vector2 pa = p - a, ba = b - a;
                float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
                return (pa - ba * h).magnitude - r;
            }
            public static float Polygon(Vector2 p, Vector2[] v)
            {
                float d = Vector2.Dot(p - v[0], p - v[0]), s = 1;
                for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
                {
                    Vector2 e = v[j] - v[i], w = p - v[i];
                    Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                    d = Mathf.Min(d, Vector2.Dot(b, b));
                    bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                }
                return s * Mathf.Sqrt(d);
            }
            public static float SMin(float a, float b, float k)
            {
                float h = Mathf.Clamp01(.5f + .5f * (b - a) / k);
                return Mathf.Lerp(b, a, h) - k * h * (1 - h);
            }
            static float Heart(Vector2 p)
            {
                p.x = Mathf.Abs(p.x);
                if (p.y + p.x > 1) return (p - new Vector2(.25f, .75f)).magnitude - Mathf.Sqrt(2) / 4;
                float m = .5f * Mathf.Max(p.x + p.y, 0);
                return Mathf.Sqrt(Mathf.Min((p - new Vector2(0, 1)).sqrMagnitude, (p - new Vector2(m, m)).sqrMagnitude)) * Mathf.Sign(p.x - p.y);
            }
            static Vector2 Rotate(Vector2 v, float radians)
            {
                float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
                return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
            }
            static Color Over(Color dst, Color src, float a)
            {
                float outA = a + dst.a * (1 - a);
                if (outA <= 0) return new Color(0, 0, 0, 0);
                var c = (src * a + dst * dst.a * (1 - a)) / outA; c.a = outA; return c;
            }

            /// <summary>Tintable rounded panel: dark outline, white face with soft gradient and highlight line, darker bottom lip.</summary>
            public static Color Panel(Vector2 p, Vector2 c, Vector2 half, float radius, float outline, float lip, float top, float bottom, float lipValue)
            {
                float d = Box(p, c, half, radius);
                float aOut = Cov(d);
                if (aOut <= 0) return new Color(0, 0, 0, 0);
                float aIn = outline > 0 ? Cov(d + outline) : aOut;
                var faceHalf = new Vector2(half.x - outline, half.y - outline - lip * .5f);
                var faceCentre = c + new Vector2(0, lip * .5f);
                float faceRadius = Mathf.Max(2, radius - outline);
                float aFace = lip > 0 ? Cov(Box(p, faceCentre, faceHalf, faceRadius)) : 1;
                float t = Mathf.InverseLerp(faceCentre.y - faceHalf.y, faceCentre.y + faceHalf.y, p.y);
                float v = Mathf.Lerp(bottom, top, t);
                if (outline > 0)
                {
                    float band = aFace * (1 - Cov(Box(p + new Vector2(0, 4.5f), faceCentre, faceHalf, faceRadius)));
                    v = Mathf.Lerp(v, 1, band * .9f);
                }
                v = Mathf.Lerp(lipValue, v, aFace);
                var col = Color.Lerp(InkColor, Gray(v), aIn);
                col.a = aOut;
                return col;
            }

            /// <summary>
            /// SECONDARY card (grey, multiplied by the navy tint): darker rim, slightly darker bottom lip, ~0.8 face with a
            /// gentle vertical gradient and a 1-unit lighter line under the top rim.
            /// </summary>
            public static Color Card(Vector2 p, Vector2 c, Vector2 half, float radius, float border, float lip)
            {
                float d = Box(p, c, half, radius);
                float aOut = Cov(d);
                if (aOut <= 0) return new Color(0, 0, 0, 0);
                var faceHalf = new Vector2(half.x - border, half.y - border - lip * .5f);
                var faceCentre = c + new Vector2(0, lip * .5f);
                float faceRadius = Mathf.Max(2, radius - border);
                float aFace = Cov(Box(p, faceCentre, faceHalf, faceRadius));
                float t = Mathf.InverseLerp(faceCentre.y - faceHalf.y, faceCentre.y + faceHalf.y, p.y);
                float v = Mathf.Lerp(.74f, .86f, t);
                float line = aFace * (1 - Cov(Box(p + new Vector2(0, 2.2f), faceCentre, faceHalf, faceRadius)));
                v = Mathf.Lerp(v, 1, line * .85f);
                float rim = Mathf.Lerp(.30f, .42f, Mathf.InverseLerp(c.y - half.y, c.y + half.y, p.y));
                return Gray(Mathf.Lerp(rim, v, aFace), aOut);
            }

            public static Color Trough(Vector2 p)
            {
                var c = new Vector2(64, 32); var half = new Vector2(63, 31);
                float a = Cov(Box(p, c, half, 31));
                if (a <= 0) return new Color(0, 0, 0, 0);
                // Inner shadow under the top edge makes the trough read as recessed.
                float shade = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-9, 1, Box(p + new Vector2(0, 8), c, half, 31)));
                float lit = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6, 1, Box(p - new Vector2(0, 5), c, half, 31)));
                float v = Mathf.Lerp(.9f, .5f, shade);
                v = Mathf.Min(1, v + .1f * lit * (1 - shade));
                return Gray(v, a);
            }

            /// <summary>Recessed flat plate: soft inner shadow under the top edge, faint light lip at the bottom.</summary>
            public static Color Inset(Vector2 p)
            {
                var c = new Vector2(64, 64); var half = new Vector2(62.5f, 62.5f);
                float d = Box(p, c, half, 26);
                float a = Cov(d);
                if (a <= 0) return new Color(0, 0, 0, 0);
                float shade = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(-Box(p + new Vector2(0, 5), c, half, 26) / 7f));
                float lit = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(-Box(p - new Vector2(0, 3), c, half, 26) / 3f));
                float edge = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(-d / 2.2f));
                float v = .96f - .2f * shade * (1 - lit * .5f) + .04f * lit - .12f * edge;
                return Gray(Mathf.Clamp01(v), a);
            }

            public static float BarValue(float t)
            {
                if (t < .38f) return Mathf.Lerp(.66f, .90f, t / .38f);
                if (t < .74f) return Mathf.Lerp(.90f, 1f, (t - .38f) / .36f);
                return Mathf.Lerp(1f, .91f, (t - .74f) / .26f);
            }

            public static Color Gloss(Vector2 p)
            {
                float cover = Cov(Box(p, new Vector2(64, 32), new Vector2(63, 31), 26));
                float t = p.y / 64f;
                return new Color(1, 1, 1, cover * Mathf.Lerp(.04f, .78f, Mathf.Pow(t, 1.3f)));
            }

            public static Color Badge(Vector2 p)
            {
                var c = new Vector2(64, 64);
                float d = Circle(p, c, 61);
                float aOut = Cov(d);
                if (aOut <= 0) return new Color(0, 0, 0, 0);
                float aIn = Cov(d + 6);
                var faceCentre = c + new Vector2(0, 4);
                float aFace = Cov(Circle(p, faceCentre, 51));
                float t = Mathf.InverseLerp(faceCentre.y - 51, faceCentre.y + 51, p.y);
                float v = Mathf.Lerp(.84f, 1, t);
                float band = aFace * (1 - Cov(Circle(p + new Vector2(0, 5), faceCentre, 51)));
                v = Mathf.Lerp(v, 1, band);
                v = Mathf.Lerp(.66f, v, aFace);
                var col = Color.Lerp(InkColor, Gray(v), aIn); col.a = aOut; return col;
            }

            public static Color JoyBase(Vector2 p)
            {
                float r = (p - new Vector2(64, 64)).magnitude;
                var col = new Color(InkColor.r, InkColor.g, InkColor.b, 0);
                col = Over(col, A(InkColor, 1), .30f * Cov(r - 54));
                col = Over(col, InkColor, Cov(Mathf.Abs(r - 53.5f) - 2));
                col = Over(col, Color.white, Cov(Mathf.Abs(r - 58.5f) - 4));
                col = Over(col, InkColor, Cov(Mathf.Abs(r - 62.5f) - 1.5f));
                return col;
            }

            public static Color SoftShadow(Vector2 p)
            {
                float d = Box(p, new Vector2(64, 64), new Vector2(32, 32), 22);
                return new Color(1, 1, 1, .8f * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-24, 26, d))));
            }

            public static Color Vignette(Vector2 p)
            {
                float inside = -Box(p, new Vector2(64, 64), new Vector2(63.5f, 63.5f), 34);
                float a = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(inside / 46f));
                return new Color(1, 1, 1, Mathf.Pow(a, 1.7f));
            }

            public static Color Rays(Vector2 p)
            {
                var q = p - new Vector2(128, 128);
                float r = q.magnitude / 128f;
                if (r >= 1) return new Color(1, 1, 1, 0);
                float theta = Mathf.Atan2(q.y, q.x);
                float rays = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .85f, .5f + .5f * Mathf.Cos(theta * 14)));
                float radial = Mathf.Pow(1 - r, 1.25f);
                float core = Mathf.Exp(-r * r * 16);
                return new Color(1, 1, 1, Mathf.Clamp01(radial * (.22f + .78f * rays) * .9f + core * .55f));
            }

            public static Color Ribbon(Vector2 p)
            {
                var col = new Color(0, 0, 0, 0);
                // Tails hang lower behind the band and carry a V notch.
                for (int side = 0; side < 2; side++)
                {
                    var q = side == 0 ? p : new Vector2(320 - p.x, p.y);
                    float dt = Polygon(q, new[] { new Vector2(6, 12), new Vector2(70, 12), new Vector2(70, 80), new Vector2(6, 80), new Vector2(28, 46) }) - 2;
                    float aOut = Cov(dt), aIn = Cov(dt + 5);
                    if (aOut > 0)
                    {
                        float v = Mathf.Lerp(.56f, .68f, Mathf.InverseLerp(12, 80, q.y));
                        var tail = Color.Lerp(InkColor, Gray(v), aIn);
                        col = Over(col, tail, aOut);
                    }
                    float fold = Polygon(q, new[] { new Vector2(44, 12), new Vector2(70, 12), new Vector2(70, 24) });
                    if (Cov(fold) > 0) col = Over(col, Color.Lerp(InkColor, Gray(.4f), .5f), Cov(fold) * Cov(dt));
                }
                var band = Panel(p, new Vector2(160, 62), new Vector2(118, 42), 12, 5.5f, 8, 1, .86f, .66f);
                col = Over(col, new Color(band.r, band.g, band.b, 1), band.a);
                return col;
            }

            // ---------- icons ----------

            static Color Icon(Vector2 p, Func<Vector2, float> silhouette, Func<Vector2, Color> fill, Func<Vector2, float> detail = null,
                Func<Vector2, float> spot = null, float outline = 7)
            {
                float d = silhouette(p);
                float aOut = Cov(d - outline);
                float aShadow = Cov(silhouette(p + new Vector2(0, 6)) - outline) * .32f;
                if (aOut <= 0 && aShadow <= 0) return new Color(0, 0, 0, 0);
                Color col = InkColor;
                float aIn = Cov(d);
                if (aIn > 0)
                {
                    Color f = fill(p);
                    float up = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-3, 2, silhouette(p + new Vector2(0, 8))));
                    float down = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-3, 2, silhouette(p - new Vector2(0, 8))));
                    f = Color.Lerp(f, Color.white, up * .4f);
                    f = Color.Lerp(f, new Color(f.r * .76f, f.g * .76f, f.b * .82f, 1), down * .5f);
                    if (detail != null) f = Color.Lerp(f, InkColor, Cov(detail(p)));
                    if (spot != null) f = Color.Lerp(f, Color.white, Mathf.Clamp01(spot(p)));
                    col = Color.Lerp(InkColor, f, aIn);
                }
                float a = aOut + aShadow * (1 - aOut);
                var rgb = (new Vector3(col.r, col.g, col.b) * aOut + new Vector3(InkColor.r, InkColor.g, InkColor.b) * aShadow * (1 - aOut)) / a;
                return new Color(rgb.x, rgb.y, rgb.z, a);
            }

            static Func<Vector2, Color> Vertical(Color top, Color bottom, float y0 = 14, float y1 = 114) =>
                p => Color.Lerp(bottom, top, Mathf.InverseLerp(y0, y1, p.y));

            static Func<Vector2, float> Ellipse(Vector2 c, Vector2 r, float angle, float strength) => p =>
            {
                var q = Rotate(p - c, -angle * Mathf.Deg2Rad);
                float k = new Vector2(q.x / r.x, q.y / r.y).magnitude;
                return strength * Mathf.Clamp01((1 - k) * Mathf.Min(r.x, r.y) * aa * .5f);
            };

            public static IEnumerable<KeyValuePair<string, Func<Vector2, Color>>> Icons()
            {
                var c = new Vector2(64, 64);

                Func<Vector2, float> heart = p => Heart((p - new Vector2(64, 17)) / 86f) * 86f;
                yield return Pair("heart", p => Icon(p, heart, Vertical(Hex(0xFF7180), Hex(0xE3243B)), null, Ellipse(new Vector2(42, 90), new Vector2(13, 8), 35, .9f)));

                Func<Vector2, float> gear = p =>
                {
                    var q = p - c;
                    float seg = Mathf.PI * 2 / 8, angle = Mathf.Round(Mathf.Atan2(q.y, q.x) / seg) * seg;
                    var local = Rotate(q, -angle);
                    return SMin(Circle(p, c, 39), Box(local, new Vector2(44, 0), new Vector2(9, 9.5f), 3.5f), 4);
                };
                yield return Pair("gear", p => Icon(p, gear, q =>
                {
                    var col = Color.Lerp(Hex(0xF0961A), Hex(0xFFDA4F), Mathf.InverseLerp(14, 114, q.y));
                    float r = (q - c).magnitude;
                    return r > 21 && r < 27 ? col * new Color(.86f, .8f, .7f, 1) : col;
                }, q => Circle(q, c, 12), Ellipse(new Vector2(46, 90), new Vector2(11, 6), 30, .75f)));

                Func<Vector2, float> skull = p => SMin(Circle(p, new Vector2(64, 74), 40), Box(p, new Vector2(64, 37), new Vector2(25, 17), 8), 8);
                yield return Pair("skull", p => Icon(p, skull, Vertical(Hex(0xFFFDF5), Hex(0xDCD1BD)), q => Mathf.Min(
                    Mathf.Min(Circle(q, new Vector2(48.5f, 70), 11.5f), Circle(q, new Vector2(79.5f, 70), 11.5f)),
                    Mathf.Min(Polygon(q, new[] { new Vector2(64, 57), new Vector2(57.5f, 46), new Vector2(70.5f, 46) }) - 1,
                        Mathf.Min(Segment(q, new Vector2(56, 22), new Vector2(56, 33), 2.2f), Mathf.Min(Segment(q, new Vector2(64, 22), new Vector2(64, 33), 2.2f), Segment(q, new Vector2(72, 22), new Vector2(72, 33), 2.2f))))),
                    Ellipse(new Vector2(48, 98), new Vector2(12, 6), 20, .6f)));

                var boltPoints = new[] { new Vector2(66, 116), new Vector2(91, 116), new Vector2(73, 77), new Vector2(97, 77), new Vector2(47, 13), new Vector2(57, 58), new Vector2(31, 58) };
                Func<Vector2, float> bolt = p => Polygon(p, boltPoints) - 3;
                yield return Pair("bolt", p => Icon(p, bolt, Vertical(Hex(0xFFE95E), Hex(0xFF9A1A), 13, 116)));

                Func<Vector2, float> wrench = p =>
                {
                    var dir = new Vector2(.7071f, .7071f);
                    float head = Circle(p, new Vector2(84, 84), 25);
                    var q = Rotate(p - (new Vector2(84, 84) + dir * 14), -Mathf.PI / 4);
                    float notch = Box(q, Vector2.zero, new Vector2(17, 8.5f), 2);
                    float body = SMin(Segment(p, new Vector2(36, 36), new Vector2(80, 80), 11), head, 4);
                    body = Mathf.Max(body, -notch);
                    return SMin(body, Circle(p, new Vector2(33, 33), 16), 3);
                };
                yield return Pair("wrench", p => Icon(p, wrench, Vertical(Hex(0xF6F8FC), Hex(0xA9B5C8)), q => Circle(q, new Vector2(33, 33), 6)));

                Func<Vector2, float> pause = p => Mathf.Min(Box(p, new Vector2(45, 64), new Vector2(14, 41), 7), Box(p, new Vector2(83, 64), new Vector2(14, 41), 7));
                yield return Pair("pause", p => Icon(p, pause, Vertical(Color.white, Hex(0xD7E0EE))));

                Func<Vector2, float> note = p => SMin(SMin(Circle(p, new Vector2(46, 36), 20), Segment(p, new Vector2(62, 38), new Vector2(62, 106), 6.5f), 3),
                    Mathf.Min(Segment(p, new Vector2(62, 104), new Vector2(94, 88), 7.5f), Segment(p, new Vector2(94, 88), new Vector2(92, 68), 6.5f)), 3);
                yield return Pair("note", p => Icon(p, note, Vertical(Color.white, Hex(0xD7E0EE))));

                Func<Vector2, float> target = p => Circle(p, c, 52);
                yield return Pair("target", p => Icon(p, target, q =>
                {
                    float r = (q - c).magnitude;
                    var red = Color.Lerp(Hex(0xE02E2E), Hex(0xFF6252), Mathf.InverseLerp(14, 114, q.y));
                    var white = Color.Lerp(Hex(0xE9E2DA), Hex(0xFFFFFF), Mathf.InverseLerp(14, 114, q.y));
                    float band = r < 14 ? 0 : r < 28 ? 1 : r < 42 ? 0 : 1;
                    return band < .5f ? red : white;
                }, q => Mathf.Min(Mathf.Abs((q - c).magnitude - 14) - .9f, Mathf.Min(Mathf.Abs((q - c).magnitude - 28) - .9f, Mathf.Abs((q - c).magnitude - 42) - .9f)),
                    Ellipse(new Vector2(46, 92), new Vector2(10, 6), 35, .5f)));

                Func<Vector2, float> pin = p => SMin(Circle(p, new Vector2(64, 84), 34), Polygon(p, new[] { new Vector2(35, 72), new Vector2(93, 72), new Vector2(64, 17) }) - 2, 9);
                yield return Pair("pin", p => Icon(p, pin, q => Circle(q, new Vector2(64, 86), 13.5f) < 0 ? Color.white : Color.Lerp(Hex(0x1E5ED6), Hex(0x4D97FF), Mathf.InverseLerp(17, 118, q.y)),
                    q => Mathf.Abs(Circle(q, new Vector2(64, 86), 13.5f)) - 2.2f));

                var starPoints = new Vector2[10];
                for (int i = 0; i < 10; i++)
                {
                    float angle = Mathf.PI / 2 + i * Mathf.PI / 5, radius = i % 2 == 0 ? 53 : 23;
                    starPoints[i] = new Vector2(64, 62) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
                Func<Vector2, float> star = p => Polygon(p, starPoints) - 4;
                yield return Pair("star", p => Icon(p, star, Vertical(Hex(0xFFE65A), Hex(0xFFA91C), 10, 118), null, Ellipse(new Vector2(52, 84), new Vector2(8, 5), 30, .7f)));

                Func<Vector2, float> recover = p =>
                {
                    var q = p - c;
                    float theta = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg; if (theta < 0) theta += 360;
                    const float R = 35, half = 10;
                    var e1 = c + new Vector2(Mathf.Cos(50 * Mathf.Deg2Rad), Mathf.Sin(50 * Mathf.Deg2Rad)) * R;
                    var dir2 = new Vector2(Mathf.Cos(330 * Mathf.Deg2Rad), Mathf.Sin(330 * Mathf.Deg2Rad));
                    var e2 = c + dir2 * R;
                    float arc = theta >= 50 && theta <= 330 ? Mathf.Abs(q.magnitude - R) - half : Mathf.Min((p - e1).magnitude - half, (p - e2).magnitude - half);
                    var tangent = new Vector2(-dir2.y, dir2.x);
                    float head = Polygon(p, new[] { c + dir2 * (R - 22), c + dir2 * (R + 22), e2 + tangent * 26 }) - 2;
                    return Mathf.Min(arc, head);
                };
                yield return Pair("recover", p => Icon(p, recover, Vertical(Color.white, Hex(0xD7E0EE))));

                // Pointing hand for the drag tutorial: index finger up (tip at design (46,114)), blue cuff.
                Func<Vector2, float> handBody = p =>
                {
                    float finger = Segment(p, new Vector2(46, 70), new Vector2(46, 104), 10.5f);
                    float palm = Box(p, new Vector2(61, 46), new Vector2(25, 21), 11);
                    float knuckles = Mathf.Min(Segment(p, new Vector2(63, 64), new Vector2(64, 56), 8.5f),
                        Mathf.Min(Segment(p, new Vector2(77, 62), new Vector2(78, 54), 8.5f), Segment(p, new Vector2(89, 56), new Vector2(88, 48), 7.5f)));
                    float thumb = Segment(p, new Vector2(38, 46), new Vector2(26, 60), 8.5f);
                    return SMin(SMin(finger, palm, 6), SMin(knuckles, thumb, 5), 5);
                };
                Func<Vector2, float> cuff = p => Box(p, new Vector2(63, 20), new Vector2(21, 8), 4);
                Func<Vector2, float> hand = p => Mathf.Min(handBody(p), cuff(p));
                yield return Pair("hand", p => Icon(p, hand, q => cuff(q) < handBody(q) && cuff(q) < 0 ? Color.Lerp(Hex(0x1F5BD6), Hex(0x4C8DFF), Mathf.InverseLerp(12, 28, q.y))
                    : Color.Lerp(Hex(0xE4E9F2), Color.white, Mathf.InverseLerp(26, 110, q.y)),
                    q => Mathf.Min(Mathf.Min(Segment(q, new Vector2(70.5f, 50), new Vector2(70.5f, 60), 1.1f), Segment(q, new Vector2(83.5f, 48), new Vector2(83.5f, 57), 1.1f)),
                        Mathf.Abs(cuff(q)) < 1.2f && handBody(q) > 0 ? -1 : 10),
                    Ellipse(new Vector2(42, 96), new Vector2(4, 7), 0, .7f), 6.5f));
            }

            static KeyValuePair<string, Func<Vector2, Color>> Pair(string name, Func<Vector2, Color> f) => new KeyValuePair<string, Func<Vector2, Color>>(name, f);
        }
    }

    /// <summary>
    /// Play-mode probe for HUD states the review shots never stage (driven state with region card and compact
    /// objective, damage flash + trail, low-health pulse, flying gears, repair chip, map key + held joystick).
    /// -executeMethod RoadGunner.Editor.ArcadeHudJuiceProbe.Run [-rgCaptureLabel name] [-rgCaptureSize WxH]
    /// (no -quit). Writes PNGs to outputs/visual-capture/&lt;label&gt;/ and never saves the scene.
    /// </summary>
    [InitializeOnLoad]
    public static class ArcadeHudJuiceProbe
    {
        const string Key = "RoadGunner.ArcadeHudProbe.";
        static GameSession session;
        static double start, mark;
        static int step, index;

        static ArcadeHudJuiceProbe()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (UnityEditor.SessionState.GetBool(Key + "Pending", false)) EditorApplication.update += Tick;
            else if (UnityEditor.SessionState.GetInt(Key + "Exit", -1) >= 0 && !EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += Exit;
        }

        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the HUD juice probe from Edit Mode.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(item => item.enabled);
            if (scene == null) throw new InvalidOperationException("No enabled gameplay scene in Build Settings.");
            EditorSceneManager.OpenScene(scene.path);
            string folder = VisualCaptureShots.OutputRoot + "/" + Argument("-rgCaptureLabel", "hud-juice");
            Directory.CreateDirectory(folder);
            VisualCaptureRunner.ParseSize(Argument("-rgCaptureSize", "720x1280"), out int w, out int h);
            UnityEditor.SessionState.SetBool(Key + "Pending", true); UnityEditor.SessionState.SetBool(Key + "Batch", Application.isBatchMode);
            UnityEditor.SessionState.SetString(Key + "Folder", folder); UnityEditor.SessionState.SetInt(Key + "W", w); UnityEditor.SessionState.SetInt(Key + "H", h);
            UnityEditor.SessionState.SetInt(Key + "Exit", -1);
            UnityEditor.SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddMinutes(4).Ticks.ToString());
            File.WriteAllText(folder + "/PROBE.txt", "HUD juice probe " + w + "x" + h + "\n");
            PrototypeBuilder.SetPortraitGameView(w, h);
            Application.runInBackground = true;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        static void Log(string line) => File.AppendAllText(UnityEditor.SessionState.GetString(Key + "Folder", "outputs/visual-capture/hud-juice") + "/PROBE.txt", line + "\n");

        static void Tick()
        {
            if (!UnityEditor.SessionState.GetBool(Key + "Pending", false)) { EditorApplication.update -= Tick; return; }
            if (long.TryParse(UnityEditor.SessionState.GetString(Key + "Deadline", "0"), out long ticks) && DateTime.UtcNow.Ticks > ticks) { Log("FAIL timeout"); Finish(1); return; }
            if (!EditorApplication.isPlaying) return;
            if (!session)
            {
                session = Object.FindAnyObjectByType<GameSession>();
                if (!session) return;
                int mobile = Array.IndexOf(QualitySettings.names, "Mobile");
                if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);
                start = EditorApplication.timeSinceStartup; step = 0; index = 0;
            }
            double t = EditorApplication.timeSinceStartup - start;
            var s = session;
            try
            {
                if (step == 0 && t > .6)
                {
                    s.Restart(); s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
                    foreach (var r in s.spawns.pool) if (r) r.Despawn();
                    foreach (var b in s.spawns.buggyPool) if (b) b.Despawn();
                    if (s.zone1) s.zone1.DespawnAll();
                    s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
                    step = 1;
                }
                else if (step == 1 && t > 1.5)
                {
                    // Normal onboarding transition (same as the exploration suite): a short drive input sets HasDriven.
                    s.input.testBrake = false; s.input.testDirection = Vector2.up;
                    mark = t; step = 2;
                }
                else if (step == 2 && t > mark + .12) { s.input.testDirection = Vector2.zero; s.input.testBrake = true; mark = t; step = 3; }
                else if (step == 3 && t > mark + .9) { Capture("driven-region"); step = 4; }
                else if (step == 4)
                {
                    s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum * .78f, false); s.health.DamageEnabled = false;
                    mark = t; step = 5;
                }
                else if (step == 5 && t > mark + .1) { Capture("damage-flash"); step = 6; }
                else if (step == 6 && t > mark + 1.25) { Capture("low-health"); step = 7; }
                else if (step == 7)
                {
                    var fx = s.Feedback;
                    Vector3 car = s.vehicle.transform.position;
                    s.AddScrap(5);
                    if (fx) { fx.ScrapCollected(car + s.vehicle.transform.right * 2.5f, 5); fx.RepairChanged(true, car); }
                    mark = t; step = 8;
                }
                else if (step == 8 && t > mark + .22) { Capture("coins-repair"); step = 9; }
                else if (step == 9)
                {
                    var fx = s.Feedback; if (fx) fx.RepairChanged(false, s.vehicle.transform.position);
                    if (s.minimap && s.minimap.legendPanel && !s.minimap.legendPanel.activeSelf) s.minimap.ToggleLegend();
                    var joystick = s.input.joystick;
                    if (joystick && joystick.ring && joystick.thumb)
                    {
                        // Show the stick as a held touch would, without injecting input.
                        var zone = (RectTransform)joystick.transform;
                        joystick.ring.position = zone.TransformPoint(zone.rect.center + new Vector2(-40, -60));
                        joystick.ring.gameObject.SetActive(true); joystick.thumb.anchoredPosition = new Vector2(52, 74);
                    }
                    mark = t; step = 10;
                }
                else if (step == 10 && t > mark + .2) { Capture("map-key-joystick"); step = 11; }
                else if (step == 11)
                {
                    if (s.minimap && s.minimap.legendPanel && s.minimap.legendPanel.activeSelf) s.minimap.ToggleLegend();
                    if (s.input.joystick) s.input.joystick.ResetPointer();
                    // World labels: park next to a supply cache (reward label on its navy plate).
                    var cache = s.exploration ? s.exploration.caches.FirstOrDefault(c => c && !c.secret && c.displayText && c.displayText.gameObject.activeInHierarchy) : null;
                    if (cache) Park(cache.transform.position, 6.5f);
                    mark = t; step = 12;
                }
                else if (step == 12 && t > mark + .35) { Capture("world-label-cache"); step = 13; }
                else if (step == 13)
                {
                    var region = Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(l => l.name == "Region name");
                    if (region) Park(region.transform.position, 6.5f);
                    mark = t; step = 14;
                }
                else if (step == 14 && t > mark + .35) { Capture("world-label-region"); step = 15; }
                else if (step == 15)
                {
                    s.input.testInput = false; s.input.Clear(); s.spawns.enabled = true; s.turret.enabled = true; s.Restart();
                    Log("RESULT shots=" + index); Finish(0);
                }
            }
            catch (Exception error) { Log("FAIL " + error); Finish(1); }
        }

        static void Park(Vector3 target, float distance)
        {
            var s = session;
            var view = s.input.view;
            Vector3 back = view ? Vector3.ProjectOnPlane(-view.transform.forward, Vector3.up).normalized : Vector3.back;
            Vector3 point = target + back * distance;
            if (Physics.Raycast(point + Vector3.up * 100, Vector3.down, out var hit, 250, 1, QueryTriggerInteraction.Ignore)) point = hit.point;
            s.vehicle.ResetPose(point + Vector3.up * .7f, s.startRotation); Physics.SyncTransforms(); s.follow.Snap();
        }

        static void Capture(string name)
        {
            var s = session; var camera = s.input.view;
            int w = UnityEditor.SessionState.GetInt(Key + "W", 720), h = UnityEditor.SessionState.GetInt(Key + "H", 1280);
            string file = UnityEditor.SessionState.GetString(Key + "Folder", "") + "/" + (++index).ToString("00") + "-" + name + ".png";
            var msaa = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = true });
            var resolved = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0) { sRGB = true });
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; float previousAspect = camera.aspect;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            try
            {
                camera.aspect = (float)w / h; camera.targetTexture = msaa;
                foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = camera; c.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1); }
                Canvas.ForceUpdateCanvases();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = msaa });
                Graphics.Blit(msaa, resolved);
                RenderTexture.active = resolved; texture.ReadPixels(new Rect(0, 0, w, h), 0, 0); texture.Apply();
                File.WriteAllBytes(file, texture.EncodeToPNG());
                Log("SHOT " + file + " hp=" + s.health.Current + "/" + s.health.Maximum + " driven=" + (s.hud.guidance ? s.hud.guidance.HasDriven : false));
            }
            finally
            {
                foreach (var c in canvases) if (c) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
                camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(msaa); RenderTexture.ReleaseTemporary(resolved); Object.Destroy(texture); Canvas.ForceUpdateCanvases();
            }
        }

        static void Finish(int code)
        {
            UnityEditor.SessionState.SetBool(Key + "Pending", false); UnityEditor.SessionState.SetInt(Key + "Exit", code);
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Exit();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && UnityEditor.SessionState.GetInt(Key + "Exit", -1) >= 0) EditorApplication.delayCall += Exit;
        }

        static void Exit()
        {
            int code = UnityEditor.SessionState.GetInt(Key + "Exit", -1);
            if (code < 0) return;
            UnityEditor.SessionState.SetInt(Key + "Exit", -1);
            Debug.Log("ARCADE HUD juice probe finished: exit=" + code);
            if (UnityEditor.SessionState.GetBool(Key + "Batch", false)) EditorApplication.Exit(code);
        }
    }
}
