using UnityEngine;
namespace RoadGunner
{
    // The ground circle and both weapons use the same centre-to-centre distance.
    public static class AttackRange
    {
        public static float SquaredDistance(Vector3 from, Vector3 to)
        {
            float x = to.x - from.x, z = to.z - from.z;
            return x * x + z * z;
        }
        public static bool Contains(Vector3 from, Vector3 to, float radius)
        {
            return radius > 0 && SquaredDistance(from, to) <= radius * radius;
        }
    }
}
