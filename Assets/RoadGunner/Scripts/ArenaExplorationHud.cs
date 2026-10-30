using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Small, non-interactive region card reading the authored exploration state.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaExplorationHud : MonoBehaviour
    {
        public GameSession session;
        public GameObject regionPanel;
        public Text regionName, regionHint, progress;
        float nextRefresh;
        GameSession subscribedSession;

        void OnEnable()
        {
            if (!session) session = GetComponent<GameSession>();
            subscribedSession = session;
            if (subscribedSession) subscribedSession.Changed += Refresh;
        }
        void OnDisable()
        {
            if (subscribedSession) subscribedSession.Changed -= Refresh;
            subscribedSession = null;
            if (regionPanel) regionPanel.SetActive(false);
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .12f;
            Refresh();
        }

        public void Refresh()
        {
            var exploration = session ? session.exploration : null;
            var guidance = session && session.hud ? session.hud.guidance : null;
            bool visible = exploration && exploration.isActiveAndEnabled && session.Playing && (!guidance || guidance.HasDriven);
            if (regionPanel && regionPanel.activeSelf != visible) regionPanel.SetActive(visible);
            if (!visible) return;
            Set(regionName, string.IsNullOrEmpty(exploration.CurrentRegionName) ? "WASTELAND CROSSROADS" : exploration.CurrentRegionName.ToUpperInvariant());
            Set(regionHint, string.IsNullOrEmpty(exploration.CurrentRegionHint) ? "Explore side roads for supplies" : exploration.CurrentRegionHint);
            int remaining = 0;
            if (exploration.caches != null) foreach (var cache in exploration.caches)
                if (cache && !cache.secret && !cache.Claimed) remaining++;
            Set(progress, "SECRETS " + exploration.SecretsFound + " / " + exploration.SecretCount +
                "   ·   SUPPLY CACHES " + remaining);
        }

        static void Set(Text text, string value) { if (text && text.text != value) text.text = value; }
    }
}
