using UnityEngine;
namespace RoadGunner
{
    public sealed class VehicleHealth : MonoBehaviour
    {
        public CombatTuning tuning;
        public ArenaRunStats runStats;
        public float Maximum => Mathf.Max(1, tuning.vehicleHealth * (runStats ? runStats.HealthMultiplier : 1));
        public float Current { get; private set; }
        public bool DamageEnabled { get; set; }
        public event System.Action Changed, Died;
        public Renderer[] bodyPanels;
        float nextMelee,flashUntil;
        MaterialPropertyBlock block;
        void Awake() { block=new MaterialPropertyBlock(); }
        public void ResetHealth() { Current=Maximum; nextMelee=0; flashUntil=0; DamageEnabled=true; Changed?.Invoke(); }
        public void Heal(float amount) {
            if(Current<=0 || amount<=0)return;
            float next=Mathf.Min(Maximum,Current+amount);
            if(next<=Current)return;
            Current=next;Changed?.Invoke();
        }
        public void RefreshMaximum(float previousMaximum) {
            // A health level grants its additional capacity immediately, retaining existing damage.
            Current=Mathf.Clamp(Current+Mathf.Max(0,Maximum-previousMaximum),0,Maximum);
            Changed?.Invoke();
        }
        public bool Damage(float amount,bool sharedContact=true) {
            if(!DamageEnabled || Current<=0 || (sharedContact&&Time.time<nextMelee)) return false;
            if(sharedContact) nextMelee=Time.time+tuning.sharedMeleeCooldown;
            Current=Mathf.Max(0,Current-amount); flashUntil=Time.time+.12f; Changed?.Invoke();
            if(Current<=0) {DamageEnabled=false;Died?.Invoke();} return true;
        }
        void LateUpdate() {
            if(bodyPanels==null) return;
            foreach(var r in bodyPanels) { if(!r)continue;block.Clear();if(Time.time<flashUntil)block.SetColor("_BaseColor",new Color(1,.52f,.27f));r.SetPropertyBlock(block); }
        }
    }
}
