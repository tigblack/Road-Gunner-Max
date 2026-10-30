using UnityEngine;
using UnityEngine.Events;

namespace RoadGunner
{
    /// <summary>A destructible reward or a nearby secret chest; each can pay only once per RunSerial.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaRewardCache : CombatTarget
    {
        public string id;
        public GameSession session;
        public ArenaExploration exploration;
        public ArenaRewardKind reward;
        [Min(0)] public float amount = 5;
        public bool proximityOnly, secret;
        [Min(.1f)] public float durability = 20;
        [Min(.1f)] public float radius = 3;
        [Min(0), Tooltip("Optional additional repair with the primary reward.")]
        public float repairAmount;
        public Vector3 aimOffset = new Vector3(0, .8f, 0);
        public TextMesh displayText;
        public GameObject availableVisual, consumedVisual;
        public UnityEvent onCollected = new UnityEvent();

        public bool Claimed => session && claimedRunSerial == session.RunSerial;
        public bool Revealed { get; private set; }
        public override bool Alive => gameObject.activeInHierarchy && !proximityOnly && !Claimed && currentHealth > 0;
        public override float CurrentHealth => currentHealth;
        public override float MaxHealth => Mathf.Max(.1f, durability);
        public override Vector3 AimPoint => transform.TransformPoint(aimOffset);
        public string PrimaryRewardLabel
        {
            get
            {
                string value = Mathf.Max(0, amount).ToString("0.#");
                string label;
                switch (reward)
                {
                    case ArenaRewardKind.Damage: label = "DAMAGE +" + value + "%"; break;
                    case ArenaRewardKind.Ram: label = "RAM +" + value + "%"; break;
                    case ArenaRewardKind.Health: label = "MAX HP +" + value; break;
                    case ArenaRewardKind.Repair: label = "REPAIR +" + value + " HP"; break;
                    case ArenaRewardKind.Scrap: label = "SCRAP +" + Mathf.Max(0, Mathf.RoundToInt(amount)); break;
                    default: return "";
                }
                return label;
            }
        }
        public string RewardLabel => PrimaryRewardLabel + (ArenaExploration.ValidAmount(repairAmount) ? "\nREPAIR +" + repairAmount.ToString("0.#") + " HP" : "");
        readonly RaycastHit[] sightHits = new RaycastHit[32];
        Collider[] colliders;
        bool[] colliderEnabled;
        int claimedRunSerial = int.MinValue, resetRunSerial = int.MinValue;
        float currentHealth, nextCheck, nextRam;
        bool claiming;

        void Awake() { Bind(); currentHealth = MaxHealth; Revealed = !secret; }
        void Bind()
        {
            if (!exploration && session) exploration = session.exploration;
            if (!exploration) exploration = GetComponentInParent<ArenaExploration>();
            if (!session && exploration) session = exploration.session;
            if (!session) session = GetComponentInParent<GameSession>();
        }
        void CaptureColliders()
        {
            if (colliders != null) return;
            colliders = GetComponentsInChildren<Collider>(true); colliderEnabled = new bool[colliders.Length];
            for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
        }
        public void ResetCache()
        {
            Bind(); CaptureColliders(); nextCheck = nextRam = 0;
            // A repeated installer/reset call must not turn the same run into an infinite reward source.
            if (!session || resetRunSerial != session.RunSerial)
            {
                resetRunSerial = session ? session.RunSerial : int.MinValue;
                currentHealth = MaxHealth; Revealed = !secret;
            }
            if (Claimed) currentHealth = 0;
            ApplyVisuals(); NotifyHealthChanged();
        }
        void ApplyVisuals()
        {
            CaptureColliders();
            if (availableVisual) availableVisual.SetActive(!Claimed);
            if (consumedVisual) consumedVisual.SetActive(Claimed);
            for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = !Claimed && colliderEnabled[i];
            if (displayText) { displayText.text = RewardLabel; displayText.gameObject.SetActive(!Claimed); }
        }
        void Update()
        {
            if (!exploration) Bind();
            if (exploration && !exploration.isActiveAndEnabled) return;
            if (Claimed || !session || !session.Playing || Time.time < nextCheck) return;
            nextCheck = Time.time + (exploration && exploration.tuning ? Mathf.Max(.02f, exploration.tuning.proximityCheckInterval) : .12f);
            if (secret && !Revealed && session.vehicle)
            {
                float revealRadius = exploration && exploration.tuning ? exploration.tuning.secretRevealRadius : 20;
                if (AttackRange.Contains(session.vehicle.transform.position, transform.position, revealRadius)) Reveal();
            }
            if (proximityOnly || currentHealth <= 0 && InCollectionRange()) TryCollect();
        }
        public void Reveal()
        {
            if (Revealed || !session || !session.Playing) return;
            Revealed = true;
            if (exploration) exploration.NotifyCacheChanged();
        }
        public bool InCollectionRange()
        {
            if (!session || !session.vehicle || radius <= 0) return false;
            if (!AttackRange.Contains(session.vehicle.transform.position, transform.position, radius)) return false;
            Vector3 from = session.vehicle.transform.position + Vector3.up * .7f;
            Vector3 delta = AimPoint - from;
            if (delta.sqrMagnitude < .001f) return true;
            int mask = exploration && exploration.tuning ? exploration.tuning.obstructionMask.value : 1;
            int count = Physics.RaycastNonAlloc(from, delta.normalized, sightHits, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
            if (count >= sightHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var hit = sightHits[i].collider;
                if (!hit || hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(session.vehicle.transform)) continue;
                return false;
            }
            return true;
        }
        public bool TryCollect()
        {
            Bind();
            if (claiming || Claimed || !gameObject.activeInHierarchy || !session || !session.Playing || !session.health || session.health.Current <= 0 || !exploration) return false;
            if (proximityOnly ? !InCollectionRange() : currentHealth > 0) return false;
            if (!ArenaExploration.ValidAmount(amount)) return false;
            int previousClaim = claimedRunSerial;
            float previousHealth = currentHealth;
            bool previouslyRevealed = Revealed;
            GameSession claimSession = session;
            int claimSerial = session.RunSerial;
            claiming = true; claimedRunSerial = claimSerial; currentHealth = 0; Revealed = true;
            // Mark before publishing stats/health events so callbacks cannot claim this cache twice.
            bool granted = exploration.GrantReward(this);
            // Restart can run synchronously inside a stats/health/score subscriber. Its reset is authoritative.
            if (!SameRun(claimSession, claimSerial)) { claiming = false; return false; }
            if (!granted)
            {
                claimedRunSerial = previousClaim; currentHealth = previousHealth; Revealed = previouslyRevealed; claiming = false; return false;
            }
            ApplyVisuals();
            if (!SameRun(claimSession, claimSerial)) { claiming = false; return false; }
            NotifyHealthChanged();
            if (!SameRun(claimSession, claimSerial)) { claiming = false; return false; }
            claiming = false;
            if (!session.Playing) return true;
            if (session.effects) session.effects.Pickup();
            onCollected?.Invoke(); return true;
        }
        bool SameRun(GameSession owner, int serial) => owner && session == owner && owner.RunSerial == serial;
        public override void Hit(float damage, Vector3 direction, bool ram = false)
        {
            if (!Alive || !session || !session.Playing || !ArenaExploration.ValidAmount(damage) || (ram && Time.time < nextRam)) return;
            if (ram) nextRam = Time.time + (session.turret && session.turret.tuning ? Mathf.Max(.1f, session.turret.tuning.ramCooldown) : .8f);
            currentHealth = Mathf.Max(0, currentHealth - damage); NotifyHealthChanged();
            if (currentHealth <= 0) TryCollect();
        }
    }
}
