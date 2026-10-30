using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reframes the existing baked map and cached markers; creates no rendering camera.</summary>
    public static class CasualMinimapInstaller
    {
        static RectTransform Child(Transform parent, string name, Vector2 size, Vector2 offset)
        {
            var existing = parent.Find(name);
            if (!existing)
            {
                existing = new GameObject(name, typeof(RectTransform)).transform;
                existing.SetParent(parent, false);
            }
            var rect = (RectTransform)existing;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size; rect.anchoredPosition = offset;
            return rect;
        }

        static ArenaMinimapCircle Disc(Transform parent, string name, float size, Vector2 offset, Color color, float inner = 0, float softness = 0)
        {
            var rect = Child(parent, name, Vector2.one * size, offset);
            var circle = rect.GetComponent<ArenaMinimapCircle>();
            if (!circle) circle = rect.gameObject.AddComponent<ArenaMinimapCircle>();
            circle.innerRadius = inner; circle.softness = softness; circle.color = color;
            circle.raycastTarget = false; circle.SetVerticesDirty();
            EditorUtility.SetDirty(circle); return circle;
        }

        static float RadiusOf(Vector3 position, Bounds bounds)
        {
            float x = (position.x - bounds.center.x) * 2 / bounds.size.x;
            float y = (position.z - bounds.center.z) * 2 / bounds.size.z;
            return Mathf.Sqrt(x * x + y * y);
        }

        static float FitArena(ArenaMinimap map)
        {
            Bounds bounds = map.WorldBounds;
            float largestRadius = 0;
            var ground = map.layout ? map.layout.ground : null;
            if (ground && ground.sharedMesh && ground.sharedMesh.isReadable)
            {
                var matrix = ground.transform.localToWorldMatrix;
                foreach (var point in ground.sharedMesh.vertices)
                    largestRadius = Mathf.Max(largestRadius, RadiusOf(matrix.MultiplyPoint3x4(point), bounds));
            }
            else largestRadius = Mathf.Sqrt(2); // Safe fallback for an unfamiliar rectangular arena.
            var session = map.session;
            if (session)
            {
                largestRadius = Mathf.Max(largestRadius, RadiusOf(session.startPosition, bounds));
                if (session.spawns)
                {
                    foreach (var raider in session.spawns.authoredRaiders) if (raider) largestRadius = Mathf.Max(largestRadius, RadiusOf(raider.transform.position, bounds));
                    foreach (var buggy in session.spawns.authoredBuggies) if (buggy) largestRadius = Mathf.Max(largestRadius, RadiusOf(buggy.transform.position, bounds));
                }
                if (session.zone1 && session.zone1.ExtraTargets != null)
                    foreach (var target in session.zone1.ExtraTargets) if (target) largestRadius = Mathf.Max(largestRadius, RadiusOf(target.transform.position, bounds));
            }
            if (map.upgrades != null) foreach (var station in map.upgrades) if (station) largestRadius = Mathf.Max(largestRadius, RadiusOf(station.transform.position, bounds));
            if (map.workshops != null) foreach (var workshop in map.workshops) if (workshop) largestRadius = Mathf.Max(largestRadius, RadiusOf(workshop.transform.position, bounds));
            // Include the complete actual ground and leave room for the largest directional marker.
            float markerMargin = ArenaMinimap.MarkerSize(MinimapMarkerKind.Player) * .5f + 4;
            float available = Mathf.Max(.1f, 1 - markerMargin / map.CircleRadius);
            return Mathf.Clamp(available / Mathf.Max(.01f, largestRadius), .1f, 1);
        }

        /// <summary>Refresh a new bake without moving/resizing the existing HUD frame or legend.</summary>
        public static void RefreshBakedContent(ArenaMinimap map)
        {
            if (!map || !map.mapRect || !map.backgroundImage || !map.BackgroundTexture)
                throw new InvalidOperationException("The existing baked map and rectangle are required.");
            var backdrop = map.mapRect.Find("Warm sand behind arena");
            if (backdrop) backdrop.SetAsFirstSibling();
            var imageRect = map.backgroundImage.rectTransform;
            imageRect.SetSiblingIndex(backdrop ? 1 : 0);
            bool circular = map.circularMask && map.circularMask.enabled;
            if (circular)
            {
                map.contentScale = FitArena(map);
                imageRect.anchorMin = imageRect.anchorMax = imageRect.pivot = new Vector2(.5f, .5f);
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.sizeDelta = map.mapRect.sizeDelta * map.contentScale;
            }
            else
            {
                map.contentScale = 1;
                imageRect.anchorMin = Vector2.zero; imageRect.anchorMax = Vector2.one;
                imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
            }
            imageRect.localScale = Vector3.one; imageRect.localRotation = Quaternion.identity;
            map.backgroundImage.uvRect = new Rect(0, 0, 1, 1);
            map.backgroundImage.color = Color.white; map.backgroundImage.enabled = true;
            map.backgroundImage.maskable = true; map.backgroundImage.raycastTarget = false;
            EditorUtility.SetDirty(imageRect); EditorUtility.SetDirty(map.backgroundImage); EditorUtility.SetDirty(map);
            if (backdrop) EditorUtility.SetDirty(backdrop);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log("MINIMAP_BAKED_CONTENT_REFRESHED textureSibling=" + imageRect.GetSiblingIndex() +
                "; backdropSibling=" + (backdrop ? backdrop.GetSiblingIndex() : -1) + "; contentScale=" + map.contentScale.ToString("F4"));
        }

        public static void Configure(ArenaMinimap map)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Configure the circular minimap outside Play Mode.");
            if (!map || !map.mapRect || !map.backgroundImage || !map.BackgroundTexture)
                throw new InvalidOperationException("Bake the existing arena map before applying its circular frame.");
            var frame = map.mapRect.parent as RectTransform;
            if (!frame) throw new InvalidOperationException("The existing minimap frame is missing.");
            frame.sizeDelta = new Vector2(426, 426); frame.anchoredPosition = new Vector2(-249, -533);
            var oldFrame = frame.GetComponent<Image>(); if (oldFrame) oldFrame.enabled = false;
            var oldTitle = frame.Find("Minimap title"); if (oldTitle) oldTitle.gameObject.SetActive(false);
            // Frame geometry is outside the stencil hierarchy; map pixels and every marker are inside it.
            Disc(frame, "Casual map soft shadow", 444, new Vector2(0, -10), new Color(.19f, .11f, .055f, .28f), 0, .12f).transform.SetAsFirstSibling();
            var outer = Disc(frame, "Casual map warm rim", 426, Vector2.zero, new Color(.52f, .34f, .16f)); outer.transform.SetSiblingIndex(1);
            var surround = Disc(frame, "Casual map cream surround", 416, Vector2.zero, new Color(1, .91f, .65f)); surround.transform.SetSiblingIndex(2);
            map.mapRect.anchorMin = map.mapRect.anchorMax = map.mapRect.pivot = new Vector2(.5f, .5f);
            map.mapRect.sizeDelta = new Vector2(394, 394); map.mapRect.anchoredPosition = Vector2.zero;
            var square = map.mapRect.GetComponent<Image>(); if (square) Object.DestroyImmediate(square);
            var maskGraphic = map.mapRect.GetComponent<ArenaMinimapCircle>();
            if (!maskGraphic) maskGraphic = map.mapRect.gameObject.AddComponent<ArenaMinimapCircle>();
            maskGraphic.innerRadius = maskGraphic.softness = 0; maskGraphic.color = Color.white;
            maskGraphic.raycastTarget = false; maskGraphic.SetVerticesDirty();
            var mask = map.mapRect.GetComponent<Mask>(); if (!mask) mask = map.mapRect.gameObject.AddComponent<Mask>();
            mask.enabled = true; mask.showMaskGraphic = false; map.circularMask = mask;
            var backdrop = Disc(map.mapRect, "Warm sand behind arena", 394, Vector2.zero, new Color(.96f, .79f, .51f));
            backdrop.transform.SetAsFirstSibling();
            RefreshBakedContent(map);
            map.mapRect.SetAsLastSibling();
            Disc(frame, "Casual map rim highlight", 416, new Vector2(0, 1), new Color(1, .98f, .86f, .50f), .977f).transform.SetAsLastSibling();
            if (map.legendPanel)
            {
                foreach (var icon in map.legendPanel.GetComponentsInChildren<ArenaMinimapIcon>(true))
                { icon.color = ArenaMinimap.MarkerColor(icon.kind); icon.SetVerticesDirty(); EditorUtility.SetDirty(icon); }
                map.legendPanel.transform.SetAsLastSibling();
            }
            EditorUtility.SetDirty(frame); EditorUtility.SetDirty(map.mapRect); EditorUtility.SetDirty(maskGraphic);
            EditorUtility.SetDirty(mask); EditorUtility.SetDirty(map.backgroundImage); EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log("CIRCULAR_MINIMAP_CONFIGURED contentScale=" + map.contentScale.ToString("F4") + "; static texture=" + map.BackgroundTexture.width + "; workshop=green white wrench");
        }
    }
}
