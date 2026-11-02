using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Automatic service bay. Uses the existing health/session; never a trigger callback.</summary>
    [DisallowMultipleComponent]
    public sealed class RepairWorkshop : MonoBehaviour
    {
        public GameSession session;
        [Min(.1f)] public float healingRadius = 10f;
        [Range(0f, 1f), Tooltip("Fraction of the existing maximum health restored each second.")]
        public float maximumHealthFractionPerSecond = .1f;
        public Transform radiusMarking;
        public GameObject activeRepairEffect;
        public Text repairingLabel;
        public bool IsRepairing { get; private set; }
        static readonly List<RepairWorkshop> zones = new List<RepairWorkshop>();
        static readonly Dictionary<VehicleHealth, int> healedFrame = new Dictionary<VehicleHealth, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { zones.Clear(); healedFrame.Clear(); }
        void OnEnable() { if (!zones.Contains(this)) zones.Add(this); Feedback(false); }
        void OnDisable() { zones.Remove(this); Feedback(false); }
        public bool Contains(Vector3 position)
        {
            var delta = position - transform.position; delta.y = 0;
            return delta.sqrMagnitude <= healingRadius * healingRadius;
        }
        bool Eligible => session && session.Playing && session.vehicle && session.health &&
            session.health.Current > 0 && session.health.Current < session.health.Maximum &&
            Contains(session.vehicle.transform.position) && maximumHealthFractionPerSecond > 0;
        void LateUpdate()
        {
            if (radiusMarking) radiusMarking.localScale = new Vector3(healingRadius, 1, healingRadius);
            RepairWorkshop selected = null; float nearest = float.PositiveInfinity;
            if (Eligible && Time.deltaTime > 0)
                foreach (var zone in zones)
                {
                    if (!zone || !zone.isActiveAndEnabled || zone.session != session || !zone.Eligible) continue;
                    var offset = session.vehicle.transform.position - zone.transform.position; offset.y = 0;
                    float distance = offset.sqrMagnitude;
                    if (distance < nearest)
                    { selected = zone; nearest = distance; }
                }
            bool repairing = selected == this;
            if (repairing && (!healedFrame.TryGetValue(session.health, out int frame) || frame != Time.frameCount))
            {
                healedFrame[session.health] = Time.frameCount;
                session.health.Heal(session.health.Maximum * maximumHealthFractionPerSecond * Time.deltaTime);
            }
            Feedback(repairing && Eligible);
        }
        void Feedback(bool active)
        {
            IsRepairing = active;
            Present(active);
            if (activeRepairEffect && activeRepairEffect.activeSelf != active) activeRepairEffect.SetActive(active);
            if (repairingLabel && repairingLabel.gameObject.activeSelf != active) repairingLabel.gameObject.SetActive(active);
        }
        // Presentation cue only (sound, particles, HUD chip). Gaps shorter than the hold, such as a hit landing at full HP
        // inside the bay or a pause, keep one continuous cue instead of restarting it.
        bool presenting; float presentStopAt;
        void Present(bool active)
        {
            if (!session) return;
            if (active) { presentStopAt = float.PositiveInfinity; if (presenting) return; presenting = true; }
            else if (!presenting) return;
            else if (!isActiveAndEnabled || session.State == SessionState.Defeated || session.State == SessionState.Completed) presenting = false;
            else
            {
                if (float.IsPositiveInfinity(presentStopAt)) presentStopAt = Time.unscaledTime + .75f;
                if (session.State == SessionState.Paused || Time.unscaledTime < presentStopAt) return;
                presenting = false;
            }
            var fx = session.Feedback; if (fx) fx.RepairChanged(presenting, transform.position);
        }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(.85f,.65f,.25f,.7f); Gizmos.DrawWireSphere(transform.position, healingRadius); }
    }
}
