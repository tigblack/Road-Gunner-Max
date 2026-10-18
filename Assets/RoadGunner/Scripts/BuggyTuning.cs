using UnityEngine;
namespace RoadGunner
{
    [CreateAssetMenu(menuName = "Road Gunner/Enemy Buggy Tuning")]
    public sealed class BuggyTuning : ScriptableObject
    {
        public float health = 105, aggroRadius = 26, maxSpeed = 11, driveTorque = 1450;
        [Header("Ranged driving")]
        public float preferredRange = 7.5f, retreatDistance = 5.5f, retreatSpeed = 4, approachDeceleration = 3.5f, brakeTorque = 1400;
        [Header("Roof gun — range is shared with the player")]
        public float shotsPerSecond = 2, bulletDamage = 5, yawSpeed = 240, aimTolerance = 6, gunWarmup = .8f;
        public float respawnDelay = 16, wreckLifetime = 2.5f;
        public int scrapReward = 3;
    }
}
