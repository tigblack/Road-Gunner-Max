using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>A focused miniature of the actual authored road network, with the existing circular stencil.</summary>
    public static class CorridorMinimapStyler
    {
        static void Add(List<Vector3> points, Vector3 position) { points.Add(position); }

        static Bounds FocusBounds(LevelLayout layout, ArenaMinimap map, IReadOnlyList<Vector3> footprint)
        {
            if (footprint == null || footprint.Count < 3)
                throw new InvalidOperationException("Supply the actual corridor route/pocket/boundary footprint before baking its minimap.");
            var points = new List<Vector3>(footprint.Count + 32);
            for (int i = 0; i < footprint.Count; i++) Add(points, footprint[i]);
            var session = map.session;
            if (layout.playerSpawn) Add(points, layout.playerSpawn.position);
            else if (session) Add(points, session.startPosition);
            if (session && session.spawns)
            {
                if (session.spawns.authoredRaiders != null) foreach (var raider in session.spawns.authoredRaiders) if (raider) Add(points, raider.transform.position);
                if (session.spawns.authoredBuggies != null) foreach (var buggy in session.spawns.authoredBuggies) if (buggy) Add(points, buggy.transform.position);
            }
            if (session && session.zone1 && session.zone1.ExtraTargets != null)
                foreach (var target in session.zone1.ExtraTargets) if (target) Add(points, target.transform.position);
            if (map.upgrades != null) foreach (var station in map.upgrades) if (station) Add(points, station.transform.position);
            if (map.workshops != null) foreach (var workshop in map.workshops) if (workshop) Add(points, workshop.transform.position);
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
            Vector3 center = bounds.center;
            float radius = 1;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - center; delta.y = 0;
                radius = Mathf.Max(radius, delta.magnitude);
            }
            // The road footprint and all landmarks stay inside the circle, with room for the largest icon.
            float markerMargin = ArenaMinimap.MarkerSize(MinimapMarkerKind.Player) * .5f + 5;
            float availableFraction = Mathf.Max(.5f, 1 - markerMargin / map.CircleRadius);
            float diameter = radius * 2 / availableFraction;
            return new Bounds(new Vector3(center.x, layout.gameplayBounds.center.y, center.z),
                new Vector3(diameter, Mathf.Max(20, layout.gameplayBounds.size.y), diameter));
        }

        public static void Configure(LevelLayout layout, ArenaMinimap map, IReadOnlyList<Vector3> mapFootprint)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Style the authored corridor map outside Play Mode.");
            if (!layout || !map || !map.mapRect) throw new InvalidOperationException("The existing arena and minimap are required.");
            var viewBounds = FocusBounds(layout, map, mapFootprint);
            ArenaMinimapBaker.BakeAndConfigure(layout, map, viewBounds: viewBounds, minimumFeatureSize: 1.4f,
                minimumProjectedThickness: .8f);
            CasualMinimapInstaller.Configure(map);
            // FocusBounds already provides the circular fit; shrinking against the old broad ground would
            // make the new route look like a small floating diamond again.
            map.contentScale = 1;
            // Fixed arena north follows the authored 45-degree road axis, independent of the car.
            map.mapRect.localRotation = Quaternion.Euler(0, 0, 45);
            var image = map.backgroundImage.rectTransform;
            image.anchorMin = image.anchorMax = image.pivot = new Vector2(.5f, .5f);
            image.anchoredPosition = Vector2.zero; image.sizeDelta = map.mapRect.sizeDelta;
            var frame = map.mapRect.parent;
            SetColor(frame, "Casual map soft shadow", new Color(.13f, .17f, .20f, .22f));
            SetColor(frame, "Casual map warm rim", new Color(.43f, .48f, .51f));
            SetColor(frame, "Casual map cream surround", new Color(.82f, .86f, .88f));
            SetColor(frame, "Casual map rim highlight", new Color(.97f, .99f, 1, .62f));
            // The full square texture covers the viewport; the stencil naturally clips only peripheral
            // landscape, while the complete traversable route remains within its fitted circle.
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(image);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            Debug.Log("CORRIDOR_MINIMAP_READY texture=" + map.BackgroundTexture.width + "; focused world bounds=" + viewBounds +
                "; footprint points=" + mapFootprint.Count + "; ordinary targets=16px red dots; contentScale=1");
        }

        static void SetColor(Transform root, string name, Color color)
        {
            var child = root.Find(name);
            if (!child) return;
            var graphic = child.GetComponent<ArenaMinimapCircle>();
            if (!graphic) return;
            graphic.color = color; graphic.SetVerticesDirty(); EditorUtility.SetDirty(graphic);
        }
    }
}
