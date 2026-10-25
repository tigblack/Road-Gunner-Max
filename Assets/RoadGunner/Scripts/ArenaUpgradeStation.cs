using UnityEngine;
using UnityEngine.Events;

namespace RoadGunner
{
    public enum ArenaUpgradeKind { Attack, Health }

    /// <summary>One-use run upgrade, collected by proximity or by destroying its existing station.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaUpgradeStation : CombatTarget
    {
        public GameSession session;
        public ArenaRunStats stats;
        public ArenaUpgradeKind kind;
        [Min(.1f)] public float radius = 3.5f;
        public GameObject availableVisual, consumedVisual, highlight;
        public TextMesh displayText;
        [Tooltip("Optional existing sound/VFX hooks on successful collection.")]
        public UnityEvent onCollected = new UnityEvent();
        [Header("Optional destructible onboarding object")]
        public bool requiresDestruction;
        [Min(.1f)] public float durability = 10.5f;
        public Collider hitbox;
        public bool Consumed { get; private set; }
        float remainingDurability;
        public override bool Alive => requiresDestruction && !Consumed && remainingDurability > 0 && gameObject.activeInHierarchy;
        public override float CurrentHealth => remainingDurability;
        public override float MaxHealth => Mathf.Max(.1f, durability);
        public override Vector3 AimPoint => hitbox ? hitbox.bounds.center : transform.position + Vector3.up;
        float nextCheck, highlightUntil;
        Color labelColor;
        bool capturedColor;

        public void ResetStation()
        {
            Consumed = false; remainingDurability = MaxHealth; nextCheck = 0; highlightUntil = 0;
            if (hitbox) hitbox.enabled = requiresDestruction;
            if (availableVisual) availableVisual.SetActive(true);
            if (consumedVisual) consumedVisual.SetActive(false);
            if (highlight) highlight.SetActive(false);
            if (displayText)
            {
                if (!capturedColor) { labelColor = displayText.color; capturedColor = true; }
                displayText.color = labelColor;
                displayText.text = kind == ArenaUpgradeKind.Attack ? "ATTACK\n+1" : requiresDestruction ? "HEALTH +1" : "HEALTH\n+1";
            }
            NotifyHealthChanged();
        }

        void Update()
        {
            if (highlight && highlight.activeSelf && Time.time >= highlightUntil) highlight.SetActive(false);
            if (requiresDestruction || Consumed || !session || !session.Playing || !session.vehicle || Time.time < nextCheck) return;
            nextCheck = Time.time + .12f;
            Vector3 offset = session.vehicle.transform.position - transform.position; offset.y = 0;
            if (offset.sqrMagnitude <= radius * radius) TryCollect();
        }

        public bool TryCollect()
        {
            if (Consumed || !session || !session.Playing || (requiresDestruction && remainingDurability > 0)) return false;
            var source = stats ? stats : session.runStats;
            if (!source || !source.Upgrade(kind)) return false;
            Consumed = true;
            if (hitbox) hitbox.enabled = false;
            if (availableVisual) availableVisual.SetActive(false);
            if (consumedVisual) consumedVisual.SetActive(true);
            if (highlight) { highlightUntil = Time.time + .8f; highlight.SetActive(true); }
            if (displayText)
            {
                displayText.text = kind == ArenaUpgradeKind.Attack ? "ATTACK\nINSTALLED" : "HEALTH\nINSTALLED";
                displayText.color = new Color(.55f, .63f, .55f, 1);
            }
            NotifyHealthChanged();
            if (session.effects) session.effects.Pickup();
            onCollected?.Invoke();
            return true;
        }

        public override void Hit(float damage, Vector3 direction, bool ram = false)
        {
            if (!Alive || !session || !session.Playing || damage <= 0) return;
            remainingDurability = Mathf.Max(0, remainingDurability - damage);
            // Award through the same run-stat/VehicleHealth path, never an enemy kill or Metal Gear drop.
            if (remainingDurability <= 0) TryCollect();
            else NotifyHealthChanged();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = kind == ArenaUpgradeKind.Attack ? new Color(1, .64f, .18f) : new Color(.36f, .9f, .48f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
