using UnityEngine;

namespace RoadGunner
{
    /// <summary>Fits the authored portrait HUD inside the safe area without stretching its controls.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class ArenaHudLayout : MonoBehaviour
    {
        public RectTransform frame;
        public Vector2 referenceSize = new Vector2(1080, 1920);
        Vector2 lastSize;
        void OnEnable() { Fit(); }
        void LateUpdate()
        {
            var parent = (RectTransform)transform;
            if (parent.rect.size != lastSize) Fit();
        }
        public void Fit()
        {
            if (!frame) return;
            var parent = (RectTransform)transform;
            lastSize = parent.rect.size;
            if (lastSize.x <= 0 || lastSize.y <= 0) return;
            frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.one * .5f;
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = referenceSize;
            float scale = Mathf.Min(lastSize.x / Mathf.Max(1, referenceSize.x), lastSize.y / Mathf.Max(1, referenceSize.y));
            frame.localScale = Vector3.one * scale;
        }
    }
}
