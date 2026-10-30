using System;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Run-only levels, backed by the existing combat tuning and VehicleHealth.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaRunStats : MonoBehaviour
    {
        public GameSession session;
        public CombatTuning tuning;
        public ArenaUpgradeStation[] stations = Array.Empty<ArenaUpgradeStation>();
        public int AttackLevel { get; private set; } = 1;
        public int HealthLevel { get; private set; } = 1;
        public float BonusDamagePercent { get; private set; }
        public float BonusRamPercent { get; private set; }
        public float BonusMaxHP { get; private set; }
        public float HealthMultiplier => 1 + Mathf.Max(0, HealthLevel - 1) * (tuning ? Mathf.Max(0, tuning.healthLevelBonus) : .2f);
        public event Action Changed;
        public event Action<ArenaUpgradeKind> UpgradeCollected;

        public float Damage(float baseDamage, bool ram = false)
        {
            float damage = tuning ? tuning.ScalePlayerDamage(baseDamage, AttackLevel) : Mathf.Max(0, baseDamage);
            return damage * (1 + BonusDamagePercent / 100) * (ram ? 1 + BonusRamPercent / 100 : 1);
        }

        public void ResetRun()
        {
            if (!session) session = GetComponent<GameSession>();
            if (!tuning && session && session.turret) tuning = session.turret.tuning;
            if (session && session.health) session.health.runStats = this;
            AttackLevel = HealthLevel = 1;
            BonusDamagePercent = BonusRamPercent = BonusMaxHP = 0;
            if (stations != null) for (int i = 0; i < stations.Length; i++)
                if (stations[i]) stations[i].ResetStation();
            Changed?.Invoke();
        }

        public bool Upgrade(ArenaUpgradeKind kind)
        {
            if (!session || !session.Playing || !session.health || session.health.Current <= 0) return false;
            if (kind == ArenaUpgradeKind.Attack) AttackLevel++;
            else
            {
                float previousMaximum = session.health.Maximum;
                HealthLevel++;
                session.health.RefreshMaximum(previousMaximum);
            }
            Changed?.Invoke();
            UpgradeCollected?.Invoke(kind);
            return true;
        }

        public bool AddExplorationBonus(ArenaRewardKind kind, float amount)
        {
            if (!session || !session.Playing || !session.health || session.health.Current <= 0 || !ArenaExploration.ValidAmount(amount)) return false;
            switch (kind)
            {
                case ArenaRewardKind.Damage: BonusDamagePercent += amount; break;
                case ArenaRewardKind.Ram: BonusRamPercent += amount; break;
                case ArenaRewardKind.Health:
                    float previousMaximum = session.health.Maximum;
                    BonusMaxHP += amount;
                    session.health.RefreshMaximum(previousMaximum); break;
                default: return false;
            }
            Changed?.Invoke(); return true;
        }
    }
}
