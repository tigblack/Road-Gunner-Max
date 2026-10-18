using UnityEngine;
namespace RoadGunner
{
    [CreateAssetMenu(menuName = "Road Gunner/Combat Tuning")]
    public sealed class CombatTuning : ScriptableObject
    {
        [Header("Vehicle and turret")] public float vehicleHealth=100, range=10, shotsPerSecond=5, bulletDamage=15, yawSpeed=300, aimTolerance=7;
        public float sharedMeleeCooldown=.35f, ramMinimumSpeed=6, ramCooldown=.8f;
        [Header("Raider")] public float enemyHealth=45, moveSpeed=3.5f, aggroRadius=18, meleeReach=1.8f, meleeDamage=8, attackCooldown=1;
        [Header("Population")] public int activeCap=30, groupSize=4, pickupCap=60;
        public float replenishDelay=10, spawnDistance=20, pickupRadius=2.5f;
    }
}
