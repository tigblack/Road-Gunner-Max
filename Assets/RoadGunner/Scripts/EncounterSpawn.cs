using UnityEngine;

namespace RoadGunner
{
    // Authoring only: these transforms never render or collide in a player build.
    public sealed class EncounterSpawn : MonoBehaviour
    {
        [Min(1)] public int enemyCount = 4;
        [Min(0)] public float fallbackRadius = 2.3f;
        public Transform[] spawnPoints;

        public Vector3 Position(int index)
        {
            if (spawnPoints != null && index < spawnPoints.Length && spawnPoints[index])
                return spawnPoints[index].position;
            float angle = index * Mathf.PI * 2 / Mathf.Max(1, enemyCount);
            return transform.TransformPoint(new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * fallbackRadius);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, .55f, .15f);
            Gizmos.DrawWireSphere(transform.position, .6f);
            for (int i = 0; i < enemyCount; i++)
            {
                Vector3 point = Position(i);
                Gizmos.DrawWireSphere(point + Vector3.up * .4f, .4f);
                Gizmos.DrawLine(transform.position, point);
            }
        }
    }
}
