using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reuses the current Health +1 station and run stats for three shootable start objects.</summary>
    public static class UpgradeOnboardingInstaller
    {
        public static void Configure(LevelLayout layout, Vector3[] points)
        {
            var s = layout.session; var stats = s.runStats;
            if (!stats || points == null || points.Length != 3) throw new InvalidOperationException("Three authored health sites and the existing ArenaRunStats are required.");
            var health = stats.stations.Where(station => station && station.kind == ArenaUpgradeKind.Health).ToList();
            if (health.Count == 0) throw new InvalidOperationException("The existing Health +1 station must be retained as the source.");
            while (health.Count < 3)
            {
                var copy = Object.Instantiate(health[0].gameObject, health[0].transform.parent);
                health.Add(copy.GetComponent<ArenaUpgradeStation>());
            }
            for (int i = 0; i < 3; i++)
            {
                var station = health[i]; station.name = "START HEALTH +1 · Destructible " + (i + 1);
                station.transform.SetPositionAndRotation(Ground(layout, points[i]), s.startRotation);
                station.session = s; station.stats = stats; station.kind = ArenaUpgradeKind.Health;
                station.requiresDestruction = true; station.durability = 10.5f;
                var collider = station.GetComponent<BoxCollider>(); if (!collider) collider = station.gameObject.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, 1.05f, 0); collider.size = new Vector3(1.9f, 1.9f, 1.4f);
                collider.enabled = true; collider.isTrigger = false; station.hitbox = collider;
                station.gameObject.layer = 0;
                foreach (var t in station.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                if (station.displayText)
                {
                    station.displayText.text = "HEALTH +1";
                    var labelPosition = station.displayText.transform.localPosition; labelPosition.y = 3.4f;
                    station.displayText.transform.localPosition = labelPosition;
                    station.displayText.color = new Color(.06f, .18f, .15f);
                    EditorUtility.SetDirty(station.displayText); EditorUtility.SetDirty(station.displayText.transform);
                }
                EditorUtility.SetDirty(station); EditorUtility.SetDirty(collider); EditorUtility.SetDirty(station.transform);
            }
            stats.stations = stats.stations.Where(station => station && station.kind != ArenaUpgradeKind.Health).Concat(health).Distinct().ToArray();
            s.minimap.upgrades = stats.stations;
            EditorUtility.SetDirty(stats); EditorUtility.SetDirty(s.minimap);
        }
        static Vector3 Ground(LevelLayout layout, Vector3 desired)
        {
            var collider = layout.ground.GetComponent<Collider>();
            if (collider && collider.Raycast(new Ray(desired + Vector3.up * 150, Vector3.down), out var hit, 300)) return hit.point;
            throw new InvalidOperationException("No actual ground beneath onboarding station " + desired);
        }
    }
}
