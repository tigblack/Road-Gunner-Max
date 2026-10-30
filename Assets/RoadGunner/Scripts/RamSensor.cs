using UnityEngine;
namespace RoadGunner
{
    public sealed class RamSensor : MonoBehaviour
    {
        public GameSession session;
        void OnTriggerStay(Collider other) {
            if(!session.Playing||session.vehicle.ForwardSpeed<session.turret.tuning.ramMinimumSpeed)return;
            var enemy=other.GetComponentInParent<CombatTarget>();if(enemy)enemy.Hit(session.PlayerDamage(Mathf.Clamp(session.vehicle.ForwardSpeed*3,18,48),true),session.vehicle.transform.forward,true);
        }
    }
}
