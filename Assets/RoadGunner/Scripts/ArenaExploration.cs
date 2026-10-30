using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadGunner
{
    public enum ArenaRewardKind { Damage, Ram, Health, Repair, Scrap }

    [Serializable]
    public sealed class ArenaExplorationZone
    {
        public string id, displayName, hint;
        public Vector3 center;
        [Min(1)] public float radius = 20;
    }

    /// <summary>Authored exploration rewards and region descriptions, lasting only for this run.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaExploration : MonoBehaviour
    {
        public GameSession session;
        public ArenaExplorationTuning tuning;
        public ArenaRewardCache[] caches = Array.Empty<ArenaRewardCache>();
        public ArenaExplorationZone[] zones = Array.Empty<ArenaExplorationZone>();
        public string LastReward { get; private set; } = "";
        public int RewardSerial { get; private set; }
        public ArenaExplorationZone CurrentZone { get; private set; }
        public string CurrentRegionName => CurrentZone != null ? CurrentZone.displayName : "";
        public string CurrentRegionHint => CurrentZone == null ? "" : (CurrentZone.hint ?? "")
            .Replace("{buggyRamBonus}", ((tuning ? tuning.buggyRamMultiplier : 1) * 100 - 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{bossRecoveryBonus}", ((tuning ? tuning.warlordRecoveryBulletMultiplier : 1) * 100 - 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        public string CurrentRegionId => CurrentZone != null ? CurrentZone.id : "";
        public event Action Changed;

        public IEnumerable<CombatTarget> Targets
        {
            get
            {
                if (!isActiveAndEnabled || caches == null) yield break;
                for (int i = 0; i < caches.Length; i++)
                    if (FirstCache(i) && caches[i].Alive) yield return caches[i];
            }
        }

        public int SecretsFound
        {
            get
            {
                int count = 0;
                if (caches != null) for (int i = 0; i < caches.Length; i++)
                    if (FirstCache(i) && caches[i].secret && caches[i].Claimed) count++;
                return count;
            }
        }
        public int SecretCount
        {
            get
            {
                int count = 0;
                if (caches != null) for (int i = 0; i < caches.Length; i++)
                    if (FirstCache(i) && caches[i].secret) count++;
                return count;
            }
        }
        bool FirstCache(int index)
        {
            if (!caches[index]) return false;
            for (int i = 0; i < index; i++) if (caches[i] == caches[index]) return false;
            return true;
        }
        void BindSession()
        {
            if (!session) session = GetComponent<GameSession>();
            if (session && !session.exploration) session.exploration = this;
        }
        void Awake() { BindSession(); }
        public void ResetRun()
        {
            BindSession(); LastReward = ""; RewardSerial = 0; CurrentZone = null;
            if (caches != null) for (int i = 0; i < caches.Length; i++) if (FirstCache(i))
            {
                caches[i].session = session; caches[i].exploration = this; caches[i].ResetCache();
            }
            RefreshZone(); Changed?.Invoke();
        }
        void Update()
        {
            if (!session || !session.Playing) return;
            if (RefreshZone()) Changed?.Invoke();
        }
        bool RefreshZone()
        {
            ArenaExplorationZone next = null;
            float closest = float.PositiveInfinity;
            if (session && session.vehicle && zones != null) foreach (var zone in zones)
            {
                if (zone == null || zone.radius <= 0) continue;
                float distance = AttackRange.SquaredDistance(session.vehicle.transform.position, zone.center);
                if (distance <= zone.radius * zone.radius && distance < closest) { closest = distance; next = zone; }
            }
            if (next == CurrentZone) return false;
            CurrentZone = next; return true;
        }

        internal bool GrantReward(ArenaRewardCache cache)
        {
            if (!cache || cache.exploration != this || cache.session != session || !cache.Claimed || !session || !session.Playing || !session.health || session.health.Current <= 0) return false;
            float amount = cache.amount;
            if (!ValidAmount(amount)) return false;
            GameSession rewardSession = session;
            int rewardSerial = session.RunSerial;
            float repaired = 0;
            bool granted;
            switch (cache.reward)
            {
                case ArenaRewardKind.Damage:
                case ArenaRewardKind.Ram:
                case ArenaRewardKind.Health:
                    granted = session.runStats && session.runStats.AddExplorationBonus(cache.reward, amount); break;
                case ArenaRewardKind.Repair:
                    float previousHealth = session.health.Current;
                    session.health.Heal(amount);
                    if (!SameRun(rewardSession, rewardSerial)) return false;
                    repaired = session.health.Current - previousHealth;
                    granted = repaired > 0; break;
                case ArenaRewardKind.Scrap:
                    granted = session.GrantExplorationScrap(Mathf.RoundToInt(amount)); break;
                default: return false;
            }
            if (!SameRun(rewardSession, rewardSerial)) return false;
            if (!granted) return false;
            if (!session.Playing) return true;
            if (ValidAmount(cache.repairAmount))
            {
                float previousHealth = session.health.Current;
                session.health.Heal(cache.repairAmount);
                if (!SameRun(rewardSession, rewardSerial)) return false;
                repaired += session.health.Current - previousHealth;
            }
            if (!session.Playing) return true;
            LastReward = cache.reward == ArenaRewardKind.Repair ? "REPAIR +" + repaired.ToString("0.#") + " HP" : cache.PrimaryRewardLabel.Replace('\n', ' ');
            if (cache.reward == ArenaRewardKind.Damage || cache.reward == ArenaRewardKind.Ram || cache.reward == ArenaRewardKind.Health)
                LastReward += " · THIS RUN";
            if (cache.reward != ArenaRewardKind.Repair && repaired > 0) LastReward += " · REPAIR +" + repaired.ToString("0.#") + " HP";
            RewardSerial++; Changed?.Invoke(); return SameRun(rewardSession, rewardSerial);
        }
        bool SameRun(GameSession owner, int serial) => owner && session == owner && owner.RunSerial == serial;
        internal void NotifyCacheChanged() { Changed?.Invoke(); }
        internal static bool ValidAmount(float amount) => amount > 0 && !float.IsNaN(amount) && !float.IsInfinity(amount);
    }
}
