using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Adds reusable world bars to existing combat roots without touching health, placement or pools.</summary>
    public static class EnemyHealthBarAuthoring
    {
        const string AnchorName = "HealthBarAnchor";
        static void Dirty(Object value)
        {
            if (!value) return;
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        public static string Configure(LevelLayout layout)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Configure health bars in Edit Mode.");
            if (!layout || !layout.session) throw new InvalidOperationException("An existing Road Gunner level is required.");
            int prefabs = 0;
            var assets = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RoadGunner" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<CombatTarget>()).ToArray();
            foreach (var path in assets)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var target = root.GetComponent<CombatTarget>();
                    if (target.UsesBossHealthHud) RemoveBar(target);
                    else { ConfigureTarget(target); prefabs++; }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            var camera = layout.session.input ? layout.session.input.view : null;
            int ordinary = 0, bosses = 0;
            foreach (var target in RoadGunnerLevelAuthoring.InScene<CombatTarget>())
            {
                if (target.UsesBossHealthHud) { RemoveBar(target); bosses++; }
                else { ConfigureTarget(target, camera); ordinary++; }
            }
            EditorUtility.SetDirty(layout);
            return $"Enemy health UI: {ordinary} scene/pool targets, {prefabs} reusable normal-enemy prefabs; {bosses} bosses retain the existing dedicated boss HUD. HP values and placements unchanged.";
        }
        static void RemoveBar(CombatTarget target)
        {
            // A boss variant can inherit its ordinary donor's bar. Disable that inherited hierarchy
            // through prefab overrides; destroying inherited children is not allowed on scene instances.
            foreach (var bar in target.GetComponentsInChildren<EnemyHealthBar>(true))
            {
                bar.enabled = false;
                if (bar.worldCanvas) { bar.worldCanvas.enabled = false; Dirty(bar.worldCanvas); }
                if (bar.anchor) { bar.anchor.gameObject.SetActive(false); Dirty(bar.anchor.gameObject); }
                Dirty(bar);
            }
        }
        public static EnemyHealthBar ConfigureTarget(CombatTarget target, Camera camera = null)
        {
            if (!target || target.UsesBossHealthHud) return null;
            var bar = target.GetComponent<EnemyHealthBar>();
            if (!bar) bar = target.gameObject.AddComponent<EnemyHealthBar>();
            bar.enabled = true; bar.target = target; bar.gameplayCamera = camera; bar.height = target is EnemyBuggy ? 2.9f : 2.45f;
            var anchor = bar.anchor ? bar.anchor : target.transform.Find(AnchorName);
            if (!anchor) { anchor = new GameObject(AnchorName).transform; anchor.SetParent(target.transform, false); }
            bar.anchor = anchor; anchor.gameObject.SetActive(true);
            anchor.localPosition = Vector3.up * bar.height;
            anchor.rotation = camera ? camera.transform.rotation : Quaternion.identity;
            var scale = target.transform.lossyScale;
            anchor.localScale = new Vector3(1 / Mathf.Max(.01f, Mathf.Abs(scale.x)), 1 / Mathf.Max(.01f, Mathf.Abs(scale.y)), 1 / Mathf.Max(.01f, Mathf.Abs(scale.z)));
            var canvas = bar.worldCanvas;
            if (!canvas)
            {
                var existing = anchor.Find("WorldSpaceCanvas");
                canvas = existing ? existing.GetComponent<Canvas>() : null;
                if (!canvas)
                {
                    var go = new GameObject("WorldSpaceCanvas", typeof(RectTransform), typeof(Canvas));
                    go.transform.SetParent(anchor, false); canvas = go.GetComponent<Canvas>();
                }
            }
            bar.worldCanvas = canvas;
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            canvas.overrideSorting = true; canvas.sortingOrder = 20;
            canvas.enabled = true;
            var rect = (RectTransform)canvas.transform;
            rect.localPosition = Vector3.zero; rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one * .01f;
            rect.sizeDelta = new Vector2(target is EnemyBuggy ? 260 : 210, 28);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/RoadGunner/Art/ConceptWasteland/Textures/Neutral HUD panel.png");
            if (!sprite) sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var frame = ImageNode("HealthBarBackground", rect, 0, sprite, new Color(.91f, .87f, .75f, .96f));
            var shadow = frame.GetComponent<Shadow>();
            if (!shadow) shadow = frame.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.015f, .025f, .025f, .65f); shadow.effectDistance = new Vector2(2, -3);
            var track = ImageNode("HealthBarTrack", frame.rectTransform, 2, sprite, new Color(.08f, .105f, .105f, .96f));
            var fill = ImageNode("HealthBarFill", track.rectTransform, 2, sprite, new Color(1, .28f, .20f));
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 1;
            bar.healthFill = fill;
            foreach (var child in anchor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = target.gameObject.layer;
            Dirty(bar); Dirty(anchor); Dirty(canvas); Dirty(rect); Dirty(frame); Dirty(shadow); Dirty(track); Dirty(fill);
            return bar;
        }
        static Image ImageNode(string name, Transform parent, float padding, Sprite sprite, Color color)
        {
            var existing = parent.Find(name);
            var image = existing ? existing.GetComponent<Image>() : null;
            if (!image)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false); image = go.GetComponent<Image>();
            }
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding; rect.offsetMax = Vector2.one * -padding;
            rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
            image.sprite = sprite; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
            return image;
        }
    }
}
