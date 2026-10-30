using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    public static class ExplorationHudInstaller
    {
        public static void Install(GameSession session)
        {
            if (!session || !session.hud || !session.hud.designFrame) throw new System.InvalidOperationException("Exploration HUD requires the saved portrait HUD.");
            var hud = session.hud;
            var frame = hud.designFrame;
            var old = frame.Find("Exploration region HUD");
            if (old) Object.DestroyImmediate(old.gameObject);
            var card = Rect("Exploration region HUD", frame, 32, 488, 560, 116);
            card.gameObject.AddComponent<Image>().color = new Color(.045f, .085f, .08f, .9f);
            var view = session.GetComponent<ArenaExplorationHud>();
            if (!view) view = session.gameObject.AddComponent<ArenaExplorationHud>();
            view.session = session; view.regionPanel = card.gameObject;
            var font = hud.statsText ? hud.statsText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view.regionName = Label(card, font, "Region", "WASTELAND", 16, 4, 528, 36, 28, new Color(1, .76f, .32f));
            view.regionHint = Label(card, font, "Counterplay hint", "Explore side roads for supplies", 16, 41, 528, 32, 25, new Color(.96f, .94f, .86f));
            view.progress = Label(card, font, "Exploration progress", "SECRETS 0 / 3", 16, 79, 528, 30, 23, new Color(.50f, .87f, .76f));
            foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            card.gameObject.SetActive(false);
            if (hud.overlayDetails) hud.overlayDetails.fontSize = 27;
            if (hud.overlay) hud.overlay.transform.SetAsLastSibling();
            if (session.minimap && session.minimap.legendPanel)
            {
                var legend = (RectTransform)session.minimap.legendPanel.transform;
                legend.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 716);
                var keyRule = legend.Find("Key rule");
                if (keyRule) ((RectTransform)keyRule).anchoredPosition = new Vector2(28, -626);
                LegendEntry(legend, font, "SUPPLY CACHE · SHOOT", MinimapMarkerKind.Cache, 34);
                LegendEntry(legend, font, "SECRET · NEARBY", MinimapMarkerKind.Secret, 490);
            }
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(hud);
        }

        static void LegendEntry(Transform parent, Font font, string title, MinimapMarkerKind kind, float x)
        {
            var old = parent.Find(title + " symbol"); if (old) Object.DestroyImmediate(old.gameObject);
            old = parent.Find(title + " label"); if (old) Object.DestroyImmediate(old.gameObject);
            var rect = Rect(title + " symbol", parent, x, 528, 42, 42);
            var icon = rect.gameObject.AddComponent<ArenaMinimapIcon>();
            icon.kind = kind; icon.color = ArenaMinimap.MarkerColor(kind); icon.raycastTarget = false;
            Label(parent, font, title + " label", title, x + 60, 516, 368, 72, 26, new Color(.96f, .94f, .86f));
        }
        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        static Text Label(Transform parent, Font font, string name, string text, float x, float y, float width, float height, int size, Color color)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = size; label.color = color;
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
    }
}
