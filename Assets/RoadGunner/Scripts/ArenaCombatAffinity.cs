using UnityEngine;

namespace RoadGunner
{
    public enum ArenaAffinityProfile { Custom, Buggy, Warlord }

    /// <summary>Optional counterplay on explicitly authored enemies; absent components leave damage unchanged.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCombatAffinity : MonoBehaviour
    {
        public GameSession session;
        public ArenaAffinityProfile profile;
        [Min(.01f)] public float bulletMultiplier = 1;
        [Min(.01f)] public float ramMultiplier = 1;
        [Min(.01f)] public float recoveryBulletMultiplier = 1;
        CombatTarget target;

        public float DamageMultiplier(bool ram)
        {
            if (!target) target = GetComponent<CombatTarget>();
            if (!session)
            {
                if (target is Raider raider) session = raider.session;
                else if (target is EnemyBuggy buggy) session = buggy.session;
                else if (target is WastelandMutant mutant) session = mutant.session;
            }
            var tuning = session && session.exploration ? session.exploration.tuning : null;
            float multiplier = ram ? ramMultiplier : bulletMultiplier;
            bool recovering = target is Raider boss && boss.IsRecovering ||
                              target is WastelandMutant heavy && heavy.State == MutantState.SmashRecovery;
            if (!ram && recovering) multiplier *= recoveryBulletMultiplier;
            if (tuning && profile == ArenaAffinityProfile.Buggy && ram) multiplier *= tuning.buggyRamMultiplier;
            if (tuning && profile == ArenaAffinityProfile.Warlord && !ram && recovering) multiplier *= tuning.warlordRecoveryBulletMultiplier;
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier) ? 1 : Mathf.Max(.01f, multiplier);
        }
        public float ScaleDamage(float damage, bool ram) => Mathf.Max(0, damage) * DamageMultiplier(ram);
        public static float Apply(CombatTarget target, float damage, bool ram)
        {
            if (!target || !target.TryGetComponent<ArenaCombatAffinity>(out var affinity) || !affinity.enabled) return damage;
            return affinity.ScaleDamage(damage, ram);
        }
    }
}
