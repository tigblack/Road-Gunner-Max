using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Actual LevelLayout bounds projected into UI. Targets are cached only when the run resets.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMinimap : MonoBehaviour
    {
        public GameSession session;
        public LevelLayout layout;
        public RectTransform mapRect;
        [Tooltip("Optional map key, opened by the HUD button and hidden during ordinary combat.")]
        public GameObject legendPanel;
        [Min(1)] public float legendVisibleDuration = 8;
        public ArenaUpgradeStation[] upgrades = Array.Empty<ArenaUpgradeStation>();
        public RepairWorkshop[] workshops = Array.Empty<RepairWorkshop>();
        [Min(.03f)] public float refreshInterval = .08f;
        public int VisibleMarkerCount { get; private set; }
        public Bounds WorldBounds => layout ? layout.gameplayBounds : new Bounds(Vector3.zero, new Vector3(100, 20, 100));
        sealed class Marker
        {
            public Transform world;
            public CombatTarget enemy;
            public ArenaUpgradeStation station;
            public RectTransform icon;
            public MinimapMarkerKind kind;
            public bool visible;
        }
        readonly List<Marker> markers = new List<Marker>(32);
        Transform markerRoot;
        float nextRefresh, legendHideAt;

        void Start() { if (legendPanel) legendPanel.SetActive(false); if (!markerRoot) RefreshTargets(); }

        public void ToggleLegend()
        {
            if (!legendPanel) return;
            bool visible = !legendPanel.activeSelf;
            legendPanel.SetActive(visible);
            if (visible) legendHideAt = Time.unscaledTime + Mathf.Max(1, legendVisibleDuration);
        }

        public void RefreshTargets()
        {
            if (legendPanel) legendPanel.SetActive(false);
            if (!mapRect || !session) return;
            if (markerRoot) { markerRoot.gameObject.SetActive(false); Destroy(markerRoot.gameObject); }
            markers.Clear();
            var root = new GameObject("Cached minimap markers", typeof(RectTransform));
            root.transform.SetParent(mapRect, false); markerRoot = root.transform;
            var rect = (RectTransform)markerRoot; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (upgrades != null) for (int i = 0; i < upgrades.Length; i++)
                if (upgrades[i]) Add(upgrades[i].transform, upgrades[i].kind == ArenaUpgradeKind.Attack ? MinimapMarkerKind.Attack : MinimapMarkerKind.Health, null, upgrades[i]);
            if (workshops != null) for (int i = 0; i < workshops.Length; i++) if (workshops[i]) Add(workshops[i].transform, MinimapMarkerKind.Workshop);
            if (session.spawns)
            {
                var raiders = session.spawns.pool;
                if (raiders != null) for (int i = 0; i < raiders.Length; i++) if (raiders[i]) Add(raiders[i].transform, MinimapMarkerKind.Raider, raiders[i]);
                var buggies = session.spawns.buggyPool;
                if (buggies != null) for (int i = 0; i < buggies.Length; i++) if (buggies[i]) Add(buggies[i].transform, MinimapMarkerKind.Buggy, buggies[i]);
            }
            if (session.zone1 && session.zone1.ExtraTargets != null)
            {
                var bosses = session.zone1.ExtraTargets;
                for (int i = 0; i < bosses.Length; i++)
                {
                    var target = bosses[i]; if (!target) continue;
                    var identity = target.GetComponent<EnemyIdentity>();
                    bool miniBoss = target is WastelandMutant || identity && identity.role == EnemyRole.MutantMiniBoss;
                    Add(target.transform, miniBoss ? MinimapMarkerKind.MiniBoss : MinimapMarkerKind.Boss, target);
                }
            }
            if (session.vehicle) Add(session.vehicle.transform, MinimapMarkerKind.Player);
            nextRefresh = 0; RefreshMarkers();
        }

        void Add(Transform target, MinimapMarkerKind kind, CombatTarget enemy = null, ArenaUpgradeStation station = null)
        {
            for (int i = 0; i < markers.Count; i++) if (markers[i].world == target) return;
            var go = new GameObject(kind + " marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(ArenaMinimapIcon));
            go.transform.SetParent(markerRoot, false);
            var icon = go.GetComponent<ArenaMinimapIcon>(); icon.kind = kind; icon.raycastTarget = false;
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            float size = 7;
            switch (kind)
            {
                case MinimapMarkerKind.Player: size = 20; icon.color = Color.white; break;
                case MinimapMarkerKind.Boss: size = 19; icon.color = new Color(1, .22f, .28f); break;
                case MinimapMarkerKind.MiniBoss: size = 15; icon.color = new Color(.83f, .43f, 1); break;
                case MinimapMarkerKind.Buggy: size = 11; icon.color = new Color(1, .43f, .16f); break;
                case MinimapMarkerKind.Attack: size = 13; icon.color = new Color(1, .8f, .27f); break;
                case MinimapMarkerKind.Health: size = 13; icon.color = new Color(.35f, 1, .51f); break;
                case MinimapMarkerKind.Workshop: size = 13; icon.color = new Color(.29f, .88f, .91f); break;
                default: icon.color = new Color(1, .75f, .48f); break;
            }
            rect.sizeDelta = kind == MinimapMarkerKind.Buggy ? new Vector2(size * .8f, size * 1.25f) : new Vector2(size, size);
            markers.Add(new Marker { world = target, enemy = enemy, station = station, icon = rect, kind = kind, visible = true });
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + Mathf.Max(.03f, refreshInterval);
            if (legendPanel && legendPanel.activeSelf && (Time.unscaledTime >= legendHideAt || session && !session.Playing))
                legendPanel.SetActive(false);
            RefreshMarkers();
        }

        public Vector2 Project(Vector3 worldPosition)
        {
            Bounds bounds = WorldBounds;
            Rect rect = mapRect.rect;
            float x = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x);
            float y = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPosition.z);
            // Keep edge markers fully on the map while retaining the actual arena extent.
            return new Vector2(Mathf.Lerp(rect.xMin + 10, rect.xMax - 10, x), Mathf.Lerp(rect.yMin + 10, rect.yMax - 10, y));
        }

        public bool TryGetMarker(Transform target, out Vector2 position)
        {
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].world == target && markers[i].visible &&
                    (!markers[i].enemy || markers[i].enemy.Alive) && (!markers[i].station || !markers[i].station.Consumed))
                { position = markers[i].icon.anchoredPosition; return true; }
            position = default; return false;
        }

        void RefreshMarkers()
        {
            if (!mapRect) return;
            VisibleMarkerCount = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                bool visible = marker.world && marker.world.gameObject.activeInHierarchy &&
                    (!marker.enemy || marker.enemy.Alive) && (!marker.station || !marker.station.Consumed);
                if (visible != marker.visible) { marker.icon.gameObject.SetActive(visible); marker.visible = visible; }
                if (!visible) continue;
                VisibleMarkerCount++;
                marker.icon.anchoredPosition = Project(marker.world.position);
                if (marker.kind == MinimapMarkerKind.Player || marker.kind == MinimapMarkerKind.Buggy)
                    marker.icon.localRotation = Quaternion.Euler(0, 0, -marker.world.eulerAngles.y);
            }
        }
    }
}
