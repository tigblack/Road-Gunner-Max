using UnityEngine;

namespace RoadGunner
{
    [CreateAssetMenu(menuName = "Road Gunner/Arena Exploration Tuning")]
    public sealed class ArenaExplorationTuning : ScriptableObject
    {
        [Header("Authored cache rewards; percentages use whole units (5 means 5%)")]
        [Min(0)] public float defaultDamagePercent = 5;
        [Min(0)] public float defaultRamPercent = 10;
        [Min(0)] public float defaultHealth = 5;
        [Min(0)] public float defaultRepair = 15;
        [Min(0)] public float defaultScrap = 3;
        [Header("Exploration interaction")]
        [Min(.1f)] public float collectionRadius = 3;
        [Min(.1f)] public float secretRevealRadius = 20;
        [Min(.02f)] public float proximityCheckInterval = .12f;
        public LayerMask obstructionMask = 1;
        [Header("Optional, explicitly assigned enemy weaknesses")]
        [Min(.01f)] public float buggyRamMultiplier = 1.25f;
        [Min(.01f)] public float warlordRecoveryBulletMultiplier = 1.2f;

        public float DefaultAmount(ArenaRewardKind reward)
        {
            switch (reward)
            {
                case ArenaRewardKind.Damage: return defaultDamagePercent;
                case ArenaRewardKind.Ram: return defaultRamPercent;
                case ArenaRewardKind.Health: return defaultHealth;
                case ArenaRewardKind.Repair: return defaultRepair;
                case ArenaRewardKind.Scrap: return defaultScrap;
                default: return 0;
            }
        }
    }
}
