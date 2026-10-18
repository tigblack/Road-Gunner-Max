using UnityEngine;

namespace RoadGunner
{
    public enum EnemyRole
    {
        Raider,
        ArmoredRaider,
        MutantMiniBoss,
        TruckArenaBoss,
        Buggy
    }

    [DisallowMultipleComponent]
    public sealed class EnemyIdentity : MonoBehaviour
    {
        public EnemyRole role;
        public string title = "Wasteland Raider";
    }
}
