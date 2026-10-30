using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Actual LevelLayout bounds projected into UI. Targets are cached only when the run resets.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMinimap : MonoBehaviour
    {
        public GameSession session;
        public LevelLayout layout;
        public RectTransform mapRect;
        [Header("Circular casual viewport")]
        public Mask circularMask;
        [Range(.1f, 1)] public float contentScale = 1;
        public float CircleRadius => mapRect ? Mathf.Min(mapRect.rect.width, mapRect.rect.height) * .5f : 0;
        [Header("Offline top-down arena bake")]
        public RawImage backgroundImage;
        public bool hasBakedWorldBounds;
        public Bounds bakedWorldBounds;
        [HideInInspector] public int bakedEnvironmentMeshCount, bakedRoadMeshCount, bakedTriangleCount;
        public Texture2D BackgroundTexture => backgroundImage ? backgroundImage.texture as Texture2D : null;
        [Tooltip("Optional map key, opened by the HUD button and hidden during ordinary combat.")]
        public GameObject legendPanel;
        [Min(1)] public float legendVisibleDuration = 8;
        public ArenaUpgradeStation[] upgrades = Array.Empty<ArenaUpgradeStation>();
        public RepairWorkshop[] workshops = Array.Empty<RepairWorkshop>();
        [Min(.03f)] public float refreshInterval = .08f;
        public int VisibleMarkerCount { get; private set; }
        public Bounds WorldBounds => hasBakedWorldBounds ? bakedWorldBounds : layout ? layout.gameplayBounds : new Bounds(Vector3.zero, new Vector3(100, 20, 100));
        sealed class Marker
        {
            public Transform world;
            public CombatTarget enemy;
            public ArenaUpgradeStation station;
            public ArenaRewardCache cache;
            public RectTransform icon;
            public MinimapMarkerKind kind;
            public bool visible, projected;
        }
        readonly List<Marker> markers = new List<Marker>(32);
        Transform markerRoot;
        float nextRefresh, legendHideAt;
        Vector2 lastMapSize;

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
            if (session.exploration && session.exploration.caches != null)
                foreach (var cache in session.exploration.caches)
                    if (cache) Add(cache.transform, cache.secret ? MinimapMarkerKind.Secret : MinimapMarkerKind.Cache, cache: cache);
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
                    Add(target.transform, KindOf(target), target);
                }
            }
            if (session.vehicle) Add(session.vehicle.transform, MinimapMarkerKind.Player);
            nextRefresh = 0; RefreshMarkers();
        }

        void Add(Transform target, MinimapMarkerKind kind, CombatTarget enemy = null, ArenaUpgradeStation station = null, ArenaRewardCache cache = null)
        {
            for (int i = 0; i < markers.Count; i++) if (markers[i].world == target) return;
            var go = new GameObject(kind + " marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(ArenaMinimapIcon));
            go.transform.SetParent(markerRoot, false);
            var icon = go.GetComponent<ArenaMinimapIcon>(); icon.kind = kind; icon.raycastTarget = false; icon.maskable = true;
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            float size = MarkerSize(kind); icon.color = MarkerColor(kind);
            rect.sizeDelta = kind == MinimapMarkerKind.Buggy ? new Vector2(size * .8f, size * 1.2f) : new Vector2(size, size);
            markers.Add(new Marker { world = target, enemy = enemy, station = station, cache = cache, icon = rect, kind = kind, visible = true });
        }

        public static MinimapMarkerKind KindOf(CombatTarget target)
        {
            var identity = target ? target.GetComponent<EnemyIdentity>() : null;
            if (identity)
            {
                if (identity.Rank == EnemyRank.Boss) return MinimapMarkerKind.Boss;
                if (identity.Rank == EnemyRank.MiniBoss) return MinimapMarkerKind.MiniBoss;
                if (identity.Rank == EnemyRank.VehicleElite) return MinimapMarkerKind.Buggy;
                return MinimapMarkerKind.Raider;
            }
            if (target is WastelandMutant) return MinimapMarkerKind.MiniBoss;
            if (target is TruckArenaBoss || target is Raider raider && raider.arenaBoss) return MinimapMarkerKind.Boss;
            return target is EnemyBuggy ? MinimapMarkerKind.Buggy : MinimapMarkerKind.Raider;
        }

        public static Color MarkerColor(MinimapMarkerKind kind)
        {
            if (kind == MinimapMarkerKind.Player) return Color.white;
            if (kind == MinimapMarkerKind.Workshop) return new Color(.12f, .82f, .32f);
            if (kind == MinimapMarkerKind.MiniBoss) return new Color(1, .48f, .07f);
            if (kind == MinimapMarkerKind.Cache) return new Color(1, .76f, .22f);
            if (kind == MinimapMarkerKind.Secret) return new Color(.35f, .94f, 1);
            return new Color(1, .13f, .19f);
        }

        public static float MarkerSize(MinimapMarkerKind kind)
        {
            switch (kind)
            {
                case MinimapMarkerKind.Player: return 32;
                case MinimapMarkerKind.Boss: return 27;
                case MinimapMarkerKind.MiniBoss: return 24;
                case MinimapMarkerKind.Attack:
                case MinimapMarkerKind.Health: return 17;
                case MinimapMarkerKind.Workshop: return 28;
                case MinimapMarkerKind.Buggy: return 12;
                case MinimapMarkerKind.Cache: return 19;
                case MinimapMarkerKind.Secret: return 22;
                default: return 11;
            }
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
            // The background and icons share the same fitted content rectangle inside the mask.
            return rect.center + new Vector2((x - .5f) * rect.width, (y - .5f) * rect.height) * contentScale;
        }

        public bool IsInsideCircle(Vector2 position, float inset = 0)
        {
            if (!mapRect) return false;
            float radius = Mathf.Max(0, CircleRadius - inset);
            return (position - mapRect.rect.center).sqrMagnitude <= radius * radius + .001f;
        }

        public bool TryGetMarker(Transform target, out Vector2 position)
        {
            for (int i = 0; i < markers.Count; i++)
                if (markers[i].world == target && markers[i].visible &&
                    (!markers[i].enemy || markers[i].enemy.Alive) && (!markers[i].station || !markers[i].station.Consumed) &&
                    (!markers[i].cache || IsCacheVisible(markers[i].cache)))
                { position = markers[i].icon.anchoredPosition; return true; }
            position = default; return false;
        }

        public static bool IsCacheVisible(ArenaRewardCache cache) => cache && cache.gameObject.activeInHierarchy &&
            !cache.Claimed && (!cache.secret || cache.Revealed);

        void RefreshMarkers()
        {
            if (!mapRect) return;
            VisibleMarkerCount = 0;
            Vector2 mapSize = mapRect.rect.size;
            bool resized = mapSize != lastMapSize; lastMapSize = mapSize;
            for (int i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                bool visible = marker.world && marker.world.gameObject.activeInHierarchy &&
                    (!marker.enemy || marker.enemy.Alive) && (!marker.station || !marker.station.Consumed) &&
                    (!marker.cache || IsCacheVisible(marker.cache));
                if (visible != marker.visible) { marker.icon.gameObject.SetActive(visible); marker.visible = visible; }
                if (!visible) continue;
                VisibleMarkerCount++;
                if (marker.enemy || marker.kind == MinimapMarkerKind.Player || !marker.projected || resized)
                { marker.icon.anchoredPosition = Project(marker.world.position); marker.projected = true; }
                if (marker.kind == MinimapMarkerKind.Player || marker.kind == MinimapMarkerKind.Buggy)
                    marker.icon.localRotation = Quaternion.Euler(0, 0, -marker.world.eulerAngles.y);
            }
        }
    }
}
