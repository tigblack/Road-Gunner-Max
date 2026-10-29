using UnityEngine;
using UnityEngine.Events;

namespace RoadGunner
{
    public enum ArenaUpgradeKind { Attack, Health }

    /// <summary>One-use, touch-friendly proximity station; no trigger or interaction button required.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaUpgradeStation : MonoBehaviour
    {
        public GameSession session;
        public ArenaRunStats stats;
        public ArenaUpgradeKind kind;
        [Min(.1f)] public float radius = 3.5f;
        public GameObject availableVisual, consumedVisual, highlight;
        public TextMesh displayText;
        [Tooltip("Optional existing sound/VFX hooks on successful collection.")]
        public UnityEvent onCollected = new UnityEvent();
        public bool Consumed { get; private set; }
        float nextCheck, highlightUntil;
        Color labelColor;
        bool capturedColor;

        public void ResetStation()
        {
            Consumed = false; nextCheck = 0; highlightUntil = 0;
            if (availableVisual) availableVisual.SetActive(true);
            if (consumedVisual) consumedVisual.SetActive(false);
            if (highlight) highlight.SetActive(false);
            if (displayText)
            {
                if (!capturedColor) { labelColor = displayText.color; capturedColor = true; }
                displayText.color = labelColor;
                displayText.text = kind == ArenaUpgradeKind.Attack ? "ATTACK\n+1" : "HEALTH\n+1";
            }
        }

        void Update()
        {
            if (highlight && highlight.activeSelf && Time.time >= highlightUntil) highlight.SetActive(false);
            if (Consumed || !session || !session.Playing || !session.vehicle || Time.time < nextCheck) return;
            nextCheck = Time.time + .12f;
            Vector3 offset = session.vehicle.transform.position - transform.position; offset.y = 0;
            if (offset.sqrMagnitude <= radius * radius) TryCollect();
        }

        public bool TryCollect()
        {
            if (Consumed || !session || !session.Playing) return false;
            var source = stats ? stats : session.runStats;
            if (!source || !source.Upgrade(kind)) return false;
            Consumed = true;
            if (availableVisual) availableVisual.SetActive(false);
            if (consumedVisual) consumedVisual.SetActive(true);
            if (highlight) { highlightUntil = Time.time + .8f; highlight.SetActive(true); }
            if (displayText)
            {
                displayText.text = kind == ArenaUpgradeKind.Attack ? "ATTACK\nINSTALLED" : "HEALTH\nINSTALLED";
                displayText.color = new Color(.55f, .63f, .55f, 1);
            }
            if (session.effects) session.effects.Pickup();
            onCollected?.Invoke();
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == ArenaUpgradeKind.Attack ? new Color(1, .64f, .18f) : new Color(.36f, .9f, .48f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
