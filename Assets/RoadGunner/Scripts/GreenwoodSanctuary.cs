using UnityEngine;
namespace RoadGunner
{
    [DisallowMultipleComponent]
    public sealed class GreenwoodSanctuary : MonoBehaviour
    {
        public GameSession session;
        public float radius = 3.7f, healthPerSecond = 14;
        float elapsed;
        void Update()
        {
            if (!session || !session.Playing) return;
            Vector3 delta = session.vehicle.transform.position - transform.position;
            delta.y = 0;
            if (delta.sqrMagnitude > radius * radius) { elapsed = 0; return; }
            elapsed += Time.deltaTime;
            if (elapsed < .25f) return;
            session.health.Heal(healthPerSecond * elapsed);
            elapsed = 0;
        }
        void OnDisable() { elapsed = 0; }
    }
}
