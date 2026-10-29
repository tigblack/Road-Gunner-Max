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

        public string UpgradeLabel
        {
            get
            {
                var source = stats ? stats : session ? session.runStats : null;
                var combat = source ? source.tuning : null;
                if (kind == ArenaUpgradeKind.Attack)
                {
                    if (!combat) return "ATTACK\nUPGRADE";
                    float bonus = Mathf.Max(0, combat.attackLevelBonus);
                    int level = source.AttackLevel;
                    float currentMultiplier = 1 + Mathf.Max(0, level - 1) * bonus;
                    float percent = bonus / currentMultiplier * 100;
                    return "ATTACK\n+" + percent.ToString("0.#") + "%";
                }
                var health = session ? session.health : source && source.session ? source.session.health : null;
                if (!health || !health.tuning || !combat) return "HEALTH\nUPGRADE";
                float baseHealth = health.tuning.vehicleHealth;
                float healthBonus = Mathf.Max(0, combat.healthLevelBonus);
                int healthLevel = source.HealthLevel;
                float currentMaximum = Mathf.Max(1, baseHealth * (1 + Mathf.Max(0, healthLevel - 1) * healthBonus));
                float nextMaximum = Mathf.Max(1, baseHealth * (1 + healthLevel * healthBonus));
                return "HEALTH\n+" + Mathf.Max(0, nextMaximum - currentMaximum).ToString("0.#") + " HP";
            }
        }

        public void ResetStation()
        {
            Consumed = false; nextCheck = 0; highlightUntil = 0;
            if (availableVisual) availableVisual.SetActive(true);
            if (consumedVisual) consumedVisual.SetActive(false);
            if (highlight) highlight.SetActive(false);
            if (displayText)
            {
                if (!capturedColor) { labelColor = displayText.color; capturedColor = true; }
                displayText.gameObject.SetActive(true);
                displayText.color = labelColor;
                displayText.text = UpgradeLabel;
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
                displayText.gameObject.SetActive(false);
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
