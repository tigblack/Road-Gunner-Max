using UnityEngine;

namespace RoadGunner
{
    public enum EnemyRole
    {
        Raider,
        ArmoredRaider,
        MutantMiniBoss,
        TruckArenaBoss,
        Buggy,
        BossRaider
    }

    public enum EnemyRank { Normal, VehicleElite, MiniBoss, Boss }

    [DisallowMultipleComponent]
    public sealed class EnemyIdentity : MonoBehaviour
    {
        public EnemyRole role;
        public string title = "Wasteland Raider";
        public EnemyRank Rank => role == EnemyRole.Buggy ? EnemyRank.VehicleElite :
            role == EnemyRole.MutantMiniBoss ? EnemyRank.MiniBoss :
            role == EnemyRole.BossRaider || role == EnemyRole.TruckArenaBoss ? EnemyRank.Boss : EnemyRank.Normal;
    }
}
