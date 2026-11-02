using UnityEngine;
namespace RoadGunner
{
    public sealed class VehicleHealth : MonoBehaviour
    {
        public CombatTuning tuning;
        public ArenaRunStats runStats;
        public float Maximum => Mathf.Max(1, (tuning ? tuning.vehicleHealth : 100) * (runStats ? runStats.HealthMultiplier : 1) + (runStats ? runStats.BonusMaxHP : 0));
        public float Current { get; private set; }
        public bool DamageEnabled { get; set; }
        public event System.Action Changed, Died;
        /// <summary>Actual HP removed or restored; presentation only.</summary>
        public event System.Action<float> Damaged, Healed;
        public Renderer[] bodyPanels;
        float nextMelee,flashUntil;
        // Short reddish-warm, hue-keeping damage flash on the body panels; see ArcadeVfxFlash.
        readonly ArcadeVfxFlash flashFx=new ArcadeVfxFlash();
        public void ResetHealth() { Current=Maximum; nextMelee=0; flashUntil=0; if(flashFx.Bound){flashFx.Clear();flashFx.ResetCooldown();} DamageEnabled=true; Changed?.Invoke(); }
        public void Heal(float amount) {
            if(Current<=0 || amount<=0)return;
            float next=Mathf.Min(Maximum,Current+amount);
            if(next<=Current)return;
            float healed=next-Current;Current=next;Changed?.Invoke();Healed?.Invoke(healed);
        }
        public void RefreshMaximum(float previousMaximum) {
            // A health level grants its additional capacity immediately, retaining existing damage.
            Current=Mathf.Clamp(Current+Mathf.Max(0,Maximum-previousMaximum),0,Maximum);
            Changed?.Invoke();
        }
        public bool Damage(float amount,bool sharedContact=true) {
            if(!DamageEnabled || Current<=0 || (sharedContact&&Time.time<nextMelee)) return false;
            if(sharedContact) nextMelee=Time.time+tuning.sharedMeleeCooldown;
            float before=Current;Current=Mathf.Max(0,Current-amount); Flash(); Changed?.Invoke();
            Damaged?.Invoke(before-Current);
            if(Current<=0) {DamageEnabled=false;Died?.Invoke();} return true;
        }
        void Flash() {
            if(bodyPanels==null) return;
            if(!flashFx.Bound) flashFx.Bind(bodyPanels);
            if(flashFx.Trigger(ArcadeVfxFlash.PlayerWarm,.45f,.07f,.12f)) flashUntil=Time.time+.1f;
        }
        void LateUpdate() {
            if(flashUntil>0 && !flashFx.Tick()) flashUntil=0;
        }
    }
}
