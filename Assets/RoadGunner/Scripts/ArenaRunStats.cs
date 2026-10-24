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
        public float HealthMultiplier => 1 + Mathf.Max(0, HealthLevel - 1) * (tuning ? Mathf.Max(0, tuning.healthLevelBonus) : .2f);
        public event Action Changed;
        public event Action<ArenaUpgradeKind> UpgradeCollected;

        public float Damage(float baseDamage) => tuning ? tuning.ScalePlayerDamage(baseDamage, AttackLevel) : baseDamage;

        public void ResetRun()
        {
            if (!session) session = GetComponent<GameSession>();
            if (!tuning && session && session.turret) tuning = session.turret.tuning;
            if (session && session.health) session.health.runStats = this;
            AttackLevel = HealthLevel = 1;
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
    }
}
