using UnityEngine;
namespace RoadGunner
{
    public abstract class CombatTarget : MonoBehaviour
    {
        public abstract bool Alive { get; }
        public abstract float CurrentHealth { get; }
        public abstract float MaxHealth { get; }
        public virtual bool UsesBossHealthHud => false;
        public event System.Action HealthChanged;
        protected void NotifyHealthChanged() { HealthChanged?.Invoke(); }
        public abstract Vector3 AimPoint { get; }
        public abstract void Hit(float damage, Vector3 direction, bool ram = false);
    }
}
