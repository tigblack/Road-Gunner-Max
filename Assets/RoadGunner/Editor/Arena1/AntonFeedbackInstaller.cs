using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reauthors presentation only. Existing world, tuning, input and run systems remain authoritative.</summary>
    public static class AntonFeedbackInstaller
    {
        public const string ReportFolder = "Documentation/RoadGunner/AntonFeedback-2026-10-29";
        static readonly Color Ink = new Color(.055f, .085f, .085f, .97f);
        static readonly Color CardInk = new Color(.075f, .115f, .12f, 1);
        static readonly Color Cream = new Color(.96f, .94f, .86f, 1);
        static readonly Color Muted = new Color(.73f, .82f, .78f, 1);
        static readonly Color Gold = new Color(1, .73f, .30f, 1);
        static readonly Color Mint = new Color(.47f, .85f, .68f, 1);
        static readonly Color Teal = new Color(.14f, .34f, .33f, 1);
        static Font font;

        [MenuItem("Tools/Road Gunner/Arena 1/Apply Anton Feedback Improvements")]
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring the HUD.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene before applying HUD authoring.");
            var scene = EditorSceneManager.OpenScene(Arena1Installer.ScenePath);
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            if (!layout || !layout.session || !layout.session.hud) throw new InvalidOperationException("Existing Arena 1 session/HUD missing.");
            ConfigureScene(layout);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(ReportFolder + "/Implementation.txt",
                "AntonSkill feedback applied to the restored open Unity arena.\n" +
                "Presentation only: goal/tutorial/score/upgrade feedback, compact boss HUD and readable Pause/Defeat/Clear.\n" +
                "World geometry, navigation, roster, speed/inertia, HP/damage/rewards and free terminals/repair retained.\n" +
                "HUD is authored at1080x1920 and fitted into the actual safe area without stretching.\n" +
                "RunSerial resets presentation once per Restart. Pause/Resume preserves tutorial progress.\n" +
                "WebGL must be rebuilt after saving this scene.\n");
            Debug.Log("ROAD_GUNNER_ANTON_FEEDBACK_APPLIED " + scene.path);
        }

        static void Dirty(Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = anchor; r.pivot = pivot;
            r.anchoredPosition = position; r.sizeDelta = size; return r;
        }
        static RectTransform Top(string name, Transform parent, float x, float y, float width, float height) =>
            Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height));
        static void PositionTop(RectTransform r, float x, float y, float width, float height)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); r.localScale = Vector3.one;
        }
        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            r.localScale = Vector3.one;
        }
        static Image Panel(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var r = Top(name, parent, x, y, width, height); var image = r.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        static Text Label(string name, Transform parent, string text, float x, float y, float width, float height, int size,
            Color color, TextAnchor alignment = TextAnchor.MiddleLeft, bool bold = false)
        {
            var r = Top(name, parent, x, y, width, height);
            var label = r.gameObject.AddComponent<Text>(); label.font = font; label.text = text; label.fontSize = size;
            label.color = color; label.alignment = alignment; label.raycastTarget = false; label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            return label;
        }
        static Button Button(string name, Transform parent, string text, float x, float y, float width, float height, Color color, int size = 32)
        {
            var image = Panel(name, parent, x, y, width, height, color); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors; colors.highlightedColor = new Color(.85f, 1, .92f); colors.pressedColor = new Color(.65f, .78f, .73f);
            button.colors = colors;
            Label("Label", image.transform, text, 12, 6, width - 24, height - 12, size, Cream, TextAnchor.MiddleCenter, true);
            return button;
        }
        static ArenaMinimapCircle Disc(string name, Transform parent, float size, Vector2 position, Color color, float inner = 0)
        {
            var r = Rect(name, parent, Vector2.one * .5f, Vector2.one * .5f, position, Vector2.one * size);
            var graphic = r.gameObject.AddComponent<ArenaMinimapCircle>(); graphic.color = color; graphic.innerRadius = inner;
            graphic.raycastTarget = false; return graphic;
        }
        static void Hide(GameObject go) { if (go) go.SetActive(false); }

        public static void ConfigureScene(LevelLayout layout)
        {
            var s = layout.session; var h = s.hud; var safe = (RectTransform)h.transform;
            font = h.healthText ? h.healthText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var map = s.minimap;
            if (!map || !map.mapRect || !map.BackgroundTexture) throw new InvalidOperationException("Keep the saved, baked Arena1 minimap.");
            var boss = Object.FindAnyObjectByType<Zone1BossHud>();
            if (!boss || !boss.panel) throw new InvalidOperationException("Existing boss HUD is required.");
            var music = s.GetComponent<ArenaMusic>();
            if (!music || !music.volumeControl) throw new InvalidOperationException("Existing single music source/control is required.");
            var frameTransform = safe.Find("Portrait HUD frame");
            var frame = frameTransform as RectTransform;
            if (!frame) frame = Rect("Portrait HUD frame", safe, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(1080, 1920));
            foreach (var child in safe.Cast<Transform>().ToArray()) if (child != frame) child.SetParent(frame, false);
            // Preserve independent, authored map/boss/music objects across repeated applications.
            boss.panel.transform.SetParent(frame, false);
            music.volumeControl.transform.SetParent(frame, false);
            var oldRevision = frame.Find("Anton feedback HUD"); if (oldRevision) Object.DestroyImmediate(oldRevision.gameObject);
            Hide(h.healthText ? h.healthText.transform.parent.gameObject : null);
            Hide(h.scrapText ? h.scrapText.transform.parent.parent.gameObject : null);
            Hide(h.recover ? h.recover.gameObject : null); Hide(h.speedText ? h.speedText.gameObject : null);
            Hide(h.overlay); Hide(h.debugText ? h.debugText.gameObject : null);
            var legacyFireHint = frame.Find("Automatic fire hint"); if (legacyFireHint) Hide(legacyFireHint.gameObject);
            var joyHint = s.input.joystick.transform.Find("Joystick hint"); if (joyHint) Hide(joyHint.gameObject);
            var mapRoot = map.transform;
            var mapFrame = (RectTransform)map.mapRect.parent;
            if (map.legendPanel) Object.DestroyImmediate(map.legendPanel);
            foreach (var child in mapRoot.Cast<Transform>().ToArray())
                if (child != mapFrame) Object.DestroyImmediate(child.gameObject);
            map.legendPanel = null;
            mapFrame.anchoredPosition = new Vector2(-249, -382);
            var root = Rect("Anton feedback HUD", frame, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); Stretch(root);
            h.designFrame = frame;
            var fit = safe.GetComponent<ArenaHudLayout>(); if (!fit) fit = safe.gameObject.AddComponent<ArenaHudLayout>();
            fit.frame = frame; fit.referenceSize = new Vector2(1080, 1920); fit.Fit();

            BuildHeader(root, h);
            BuildGuidance(root, s, h);
            BuildBoss(frame, boss, h);
            var key = Button("Map key", root, "MAP KEY", 716, 608, 292, 74, Teal, 28);
            BuildLegend(map, root);
            UnityEventTools.AddPersistentListener(key.onClick, map.ToggleLegend);
            h.recover = Button("Recover car", root, "RECOVER CAR", 756, 1736, 292, 116, Teal, 31);
            h.speedText = Label("Speed", root, "00 KM/H", 736, 1674, 312, 54, 38, Cream, TextAnchor.MiddleRight);
            var toast = Panel("Upgrade feedback card", root, 32, 612, 560, 128, Ink);
            h.upgradeFeedbackPanel = toast.gameObject;
            h.upgradeFeedbackText = Label("Real upgrade benefit", toast.transform, "", 20, 10, 520, 108, 33, Gold, TextAnchor.MiddleCenter, true);
            toast.gameObject.SetActive(false);
            h.debugText = Label("Debug overlay", root, "", 32, 756, 1016, 150, 25, Cream); Hide(h.debugText.gameObject);
            BuildOverlay(root, h, music);
            h.overlay.transform.SetAsLastSibling();
            foreach (var station in s.runStats.stations)
            {
                if (!station || !station.displayText) continue;
                station.displayText.text = station.UpgradeLabel; station.displayText.gameObject.SetActive(true);
                station.displayText.characterSize = .08f;
                Dirty(station); Dirty(station.displayText);
            }
            Dirty(s); Dirty(h); Dirty(map); Dirty(boss); Dirty(fit); Dirty(s.GetComponent<ArenaGuidance>()); Dirty(music);
        }

        static void BuildHeader(Transform root, HudView h)
        {
            var header = Panel("Compact instruments", root, 32, 24, 1016, 108, Ink);
            Label("Wordmark", header.transform, "ROAD GUNNER", 24, 7, 500, 45, 36, Cream, TextAnchor.MiddleLeft, true);
            Label("Sector", header.transform, "ARENA 1", 26, 49, 160, 28, 22, Muted);
            h.healthText = Label("Actual player HP", header.transform, "100 / 100", 618, 13, 248, 45, 32, Cream, TextAnchor.MiddleRight, true);
            var track = Panel("Health track", header.transform, 24, 84, 840, 12, new Color(.19f, .26f, .25f));
            var fill = Panel("Health fill", track.transform, 0, 0, 840, 12, Mint);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            // A built-in white UI sprite gives the filled image a real geometry source.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); h.healthFill = fill;
            h.pause = Button("Pause", header.transform, "II", 892, 0, 108, 108, Teal, 42);
            var counters = Top("Run score and kills", root, 32, 156, 560, 78); h.counterPanel = counters.gameObject;
            var score = Panel("Scrap score", counters, 0, 0, 274, 78, Ink);
            h.scoreLabel = Label("Score label", score.transform, "SCRAP SCORE", 14, 8, 154, 62, 24, Muted, TextAnchor.MiddleLeft, true);
            h.scrapText = Label("Scrap value", score.transform, "000", 170, 8, 90, 62, 38, Cream, TextAnchor.MiddleRight, true);
            var kills = Panel("Kills", counters, 286, 0, 274, 78, Ink);
            h.killsLabel = Label("Kills label", kills.transform, "KILLS", 14, 8, 118, 62, 26, Muted, TextAnchor.MiddleLeft, true);
            h.killsText = Label("Kills value", kills.transform, "000", 142, 8, 118, 62, 38, Cream, TextAnchor.MiddleRight, true);
            var stats = Panel("Run levels", root, 32, 246, 560, 62, Ink); h.statsPanel = stats.gameObject;
            h.statsText = Label("ATK and HP levels", stats.transform, "ATK 1    HP 1", 18, 4, 524, 54, 33, Cream, TextAnchor.MiddleCenter, true);
            h.scoreGainText = Label("Collected score feedback", root, "", 32, 750, 560, 44, 31, Gold, TextAnchor.MiddleCenter, true);
            h.scoreGainText.gameObject.SetActive(false);
        }

        static void BuildGuidance(Transform root, GameSession s, HudView h)
        {
            var g = s.GetComponent<ArenaGuidance>(); if (!g) g = s.gameObject.AddComponent<ArenaGuidance>();
            g.session = s; h.guidance = g;
            var objective = Panel("Arena objective", root, 32, 326, 560, 194, Ink);
            g.objectivePanel = objective.rectTransform;
            var accent = Panel("Objective accent", objective.transform, 0, 0, 5, 194, Gold).rectTransform;
            accent.anchorMin = Vector2.zero; accent.anchorMax = Vector2.up;
            accent.pivot = new Vector2(0, .5f); accent.sizeDelta = new Vector2(5, 0); accent.anchoredPosition = Vector2.zero;
            g.objectiveText = Label("Main objective", objective.transform, "DEFEAT WARLORD", 22, 16, 516, 52, 36, Gold, TextAnchor.MiddleLeft, true);
            g.objectiveDetailText = Label("Objective rules", objective.transform,
                "Defeat Warlord to clear Arena 1 · Monster optional", 22, 76, 516, 100, 29, Cream);
            var direction = Top("Direction to current goal", objective.transform, 20, 192, 520, 52);
            g.directionRow = direction;
            g.directionGroup = direction.gameObject.AddComponent<CanvasGroup>();
            g.directionGroup.blocksRaycasts = g.directionGroup.interactable = false;
            var arrow = Top("Goal bearing", direction, 3, 2, 46, 46);
            var icon = arrow.gameObject.AddComponent<ArenaMinimapIcon>(); icon.kind = MinimapMarkerKind.Player; icon.color = Cream; icon.raycastTarget = false;
            g.directionArrow = arrow;
            g.directionLabel = Label("Goal distance", direction, "", 66, 0, 450, 52, 30, Mint, TextAnchor.MiddleLeft, true);
            var hint = Panel("Contextual guidance", root, 32, 1466, 1016, 190, Ink); g.hintPanel = hint.gameObject;
            Disc("Tip badge", hint.transform, 108, new Vector2(-400, 0), new Color(.17f, .27f, .25f));
            Label("Tip badge label", hint.transform, "TIP", 52, 60, 112, 64, 30, Mint, TextAnchor.MiddleCenter, true);
            var gesture = Top("First drive gesture", hint.transform, 32, 24, 156, 142); g.gestureRoot = gesture.gameObject;
            Disc("Gesture pad", gesture, 132, Vector2.zero, new Color(.18f, .26f, .25f));
            Disc("Gesture ring", gesture, 112, Vector2.zero, Gold, .94f);
            var dot = Disc("Gesture thumb", gesture, 42, new Vector2(-6, -18), Cream);
            g.gestureDot = dot.rectTransform;
            g.hintText = Label("Contextual instruction", hint.transform,
                "DRAG TO DRIVE\nYour gun fires automatically", 208, 16, 784, 158, 34, Cream, TextAnchor.MiddleLeft, true);
        }

        static void BuildBoss(Transform frame, Zone1BossHud boss, HudView h)
        {
            var panel = (RectTransform)boss.panel.transform; panel.SetParent(frame, false); PositionTop(panel, 32, 155, 560, 150);
            var background = panel.GetComponent<Image>(); if (!background) background = panel.gameObject.AddComponent<Image>();
            background.color = Ink; background.raycastTarget = false;
            PositionTop(boss.titleText.rectTransform, 18, 6, 524, 46); boss.titleText.fontSize = 28; boss.titleText.color = Gold;
            boss.titleText.horizontalOverflow = HorizontalWrapMode.Wrap; boss.titleText.verticalOverflow = VerticalWrapMode.Truncate;
            PositionTop(boss.healthText.rectTransform, 394, 94, 148, 44); boss.healthText.fontSize = 26; boss.healthText.color = Cream;
            var track = (RectTransform)boss.healthFill.transform.parent; PositionTop(track, 18, 62, 524, 14);
            Stretch(boss.healthFill.rectTransform);
            if (boss.phaseText)
            {
                PositionTop(boss.phaseText.rectTransform, 18, 94, 366, 44); boss.phaseText.fontSize = 28;
                boss.phaseText.color = Gold; boss.phaseText.raycastTarget = false;
            }
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            h.bossHud = boss; h.bossPanel = panel;
        }

        static void BuildLegend(ArenaMinimap map, Transform frame)
        {
            var panel = Panel("Readable map key", frame, 72, 708, 936, 620, Ink); map.legendPanel = panel.gameObject;
            Label("Heading", panel.transform, "MAP KEY", 28, 18, 720, 64, 42, Gold, TextAnchor.MiddleLeft, true);
            var close = Button("Close map key", panel.transform, "X", 820, 16, 88, 88, Teal, 34);
            UnityEventTools.AddPersistentListener(close.onClick, map.ToggleLegend);
            var kinds = new[] { MinimapMarkerKind.Player, MinimapMarkerKind.Raider, MinimapMarkerKind.Buggy, MinimapMarkerKind.MiniBoss,
                MinimapMarkerKind.Boss, MinimapMarkerKind.Attack, MinimapMarkerKind.Health, MinimapMarkerKind.Workshop };
            var names = new[] { "YOU", "RAIDER", "BUGGY", "MONSTER · OPTIONAL", "WARLORD · FINAL", "ATTACK UPGRADE", "HEALTH UPGRADE", "FREE REPAIR" };
            for (int i = 0; i < kinds.Length; i++)
            {
                int col = i % 2, row = i / 2; float x = 34 + col * 456, y = 132 + row * 96;
                var rect = Top(names[i] + " symbol", panel.transform, x, y + 12, 42, 42);
                var icon = rect.gameObject.AddComponent<ArenaMinimapIcon>(); icon.kind = kinds[i]; icon.color = ArenaMinimap.MarkerColor(kinds[i]); icon.raycastTarget = false;
                Label(names[i] + " text", panel.transform, names[i], x + 60, y, 368, 72, 28, Cream, TextAnchor.MiddleLeft, true);
            }
            Label("Key rule", panel.transform, "WARLORD ENDS THE RUN · COLLECT GEAR FOR SCRAP SCORE", 28, 530, 880, 64, 27, Muted, TextAnchor.MiddleCenter);
            panel.gameObject.SetActive(false);
        }

        static void BuildOverlay(Transform root, HudView h, ArenaMusic music)
        {
            var backdrop = Panel("Pause and result backdrop", root, 0, 0, 1080, 1920, new Color(.025f, .045f, .045f, .94f));
            backdrop.raycastTarget = true; h.overlay = backdrop.gameObject; h.overlayBackdrop = backdrop;
            var card = Panel("Readable result card", backdrop.transform, 80, 240, 920, 1440, CardInk); h.overlayCard = card;
            h.overlayAccent = Panel("State accent", card.transform, 0, 0, 920, 8, Mint);
            h.overlayKicker = Label("Result kicker", card.transform, "ROAD GUNNER / ARENA 1", 40, 48, 840, 56, 30, Mint, TextAnchor.MiddleCenter, true);
            var badge = Top("Victory seal", card.transform, 404, 112, 112, 112); h.resultBadge = badge;
            Disc("Victory ring", badge, 112, Vector2.zero, Gold, .90f);
            Label("Victory mark", badge, "CLEAR", 8, 25, 96, 62, 27, Gold, TextAnchor.MiddleCenter, true); Hide(badge.gameObject);
            h.overlayTitle = Label("Result title", card.transform, "PAUSED", 30, 250, 860, 112, 76, Cream, TextAnchor.MiddleCenter, true);
            h.overlayDescription = Label("Result explanation", card.transform, "", 50, 390, 820, 120, 32, Cream, TextAnchor.MiddleCenter);
            h.overlayScore = Label("Actual run results", card.transform, "", 50, 544, 820, 128, 46, Cream, TextAnchor.MiddleCenter, true);
            h.overlayDetails = Label("Real current stats", card.transform, "", 50, 708, 820, 108, 30, Muted, TextAnchor.MiddleCenter);
            var control = (RectTransform)music.volumeControl.transform; control.SetParent(card.transform, false);
            PositionTop(control, 80, 858, 760, 88);
            foreach (var label in control.GetComponentsInChildren<Text>(true)) { label.color = Cream; label.fontSize = 29; }
            foreach (var graphic in control.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.name == "Music label" || graphic.name == "Music value") continue;
                if (graphic.name == "Track") graphic.color = new Color(.28f, .36f, .34f);
                if (graphic.name == "Music handle" || graphic.name == "Music fill") graphic.color = Mint;
            }
            // Slider.UpdateVisuals drives both axes of its child anchors on enable.
            // Constrain the parent areas so the fill stays thin and the handle keeps its size.
            var slider = music.volumeSlider;
            PositionTop((RectTransform)slider.transform, 184, 12, 432, 64);
            var track = (RectTransform)slider.transform.Find("Track");
            if (track) PositionTop(track, 0, 28, 432, 8);
            var fillArea = (RectTransform)slider.fillRect.parent;
            PositionTop(fillArea, 0, 28, 432, 8);
            Stretch(slider.fillRect);
            var handleArea = (RectTransform)slider.handleRect.parent;
            PositionTop(handleArea, 0, 32, 432, 0);
            slider.handleRect.anchorMin = Vector2.zero; slider.handleRect.anchorMax = Vector2.up;
            slider.handleRect.pivot = Vector2.one * .5f; slider.handleRect.sizeDelta = new Vector2(24, 44);
            slider.handleRect.anchoredPosition = Vector2.zero;
            slider.SetValueWithoutNotify(music.musicVolume);
            h.resume = Button("Resume", card.transform, "RESUME", 80, 984, 760, 112, Teal, 35);
            h.restart = Button("Restart", card.transform, "RESTART RUN", 80, 1128, 760, 112, new Color(.25f, .29f, .27f), 35);
            h.restartHint = Label("Restart consequences", card.transform, "NEW RUN: SCORE, KILLS AND UPGRADES RESET", 60, 1260, 800, 72, 28, Cream, TextAnchor.MiddleCenter, true);
            Label("Player controls", card.transform, "WASD / ARROWS · SPACE: BRAKE · ESC: PAUSE\nR: RECOVER TO A CLEAR SPOT", 40, 1350, 840, 74, 25, Muted, TextAnchor.MiddleCenter);
            backdrop.gameObject.SetActive(false);
        }
    }
}
