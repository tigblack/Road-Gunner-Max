using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Optional existing-Canvas boss bar; adds no controls or layout changes.</summary>
    [DisallowMultipleComponent]
    public sealed class Zone1BossHud : MonoBehaviour
    {
        public GameSession session;
        public Zone1EnemyRoster roster;
        [Tooltip("Boss bar container. It is hidden with CanvasGroup, so its controller stays active.")]
        public GameObject panel;
        public Text titleText, healthText, phaseText;
        public Image healthFill;
        [Min(1)] public float showDistance = 23;
        [Min(.03f)] public float refreshInterval = .1f;
        public CombatTarget Target { get; private set; }
        public bool IsVisible { get; private set; }
        float nextRefresh;
        int displayedHealth = -1, displayedMaximum = -1;
        string displayedTitle, displayedPhase;
        GameObject boundPanel;
        CanvasGroup panelGroup;

        void OnEnable() { nextRefresh = 0; Refresh(); }
        void OnDisable() { SetVisible(false); }
        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + Mathf.Max(.03f, refreshInterval); Refresh();
        }
        void SetVisible(bool visible)
        {
            IsVisible = visible;
            if (!panel) return;
            if (boundPanel != panel || !panelGroup)
            {
                boundPanel = panel; panelGroup = panel.GetComponent<CanvasGroup>();
                if (!panelGroup) panelGroup = panel.AddComponent<CanvasGroup>();
            }
            panelGroup.blocksRaycasts = false; panelGroup.interactable = false;
            panelGroup.alpha = visible ? 1 : 0;
            if (visible && !panel.activeSelf) panel.SetActive(true);
        }
        static float HealthOf(CombatTarget target) => target ? target.CurrentHealth : 0;
        static float MaximumOf(CombatTarget target) => target ? target.MaxHealth : 1;
        static string PhaseOf(CombatTarget target)
        {
            if (target is WastelandMutant mutant)
            {
                if (mutant.IsTelegraphing) return "HEAVY SMASH · DODGE";
                if (mutant.State == MutantState.SmashRecovery) return "RECOVERING";
                return "OPTIONAL MINI-BOSS";
            }
            if (target is TruckArenaBoss truck)
            {
                if (truck.State == TruckBossState.RamWindup) return "RAM · MOVE ASIDE";
                if (truck.State == TruckBossState.Ramming) return "RAMMING";
                if (truck.IsGunTelegraphing) return "GUN BURST · TAKE COVER";
                if (truck.IsFiringBurst) return "FIRING BURST";
                if (truck.State == TruckBossState.RamRecovery) return "RECOVERING";
                return "ARMORED TRUCK";
            }
            if (target is Raider raider && raider.arenaBoss)
            {
                if (raider.IsTelegraphing) return "HEAVY STRIKE · DODGE";
                if (raider.IsRecovering) return "RECOVERING";
                return string.Empty;
            }
            return string.Empty;
        }
        public void Refresh()
        {
            var source = roster ? roster : session ? session.zone1 : null;
            if (!session || !session.vehicle || !source || source.bosses == null || (session.State == SessionState.Defeated || session.State == SessionState.Completed))
            { Target = null; SetVisible(false); return; }
            CombatTarget closest = null; float nearest = showDistance * showDistance;
            for (int i = 0; i < source.bosses.Length; i++)
            {
                var candidate = source.bosses[i]; if (!candidate || !candidate.Alive) continue;
                float distance = AttackRange.SquaredDistance(session.vehicle.transform.position, candidate.transform.position);
                if (distance <= nearest) { nearest = distance; closest = candidate; }
            }
            if (!closest) { Target = null; SetVisible(false); return; }
            if (Target != closest)
            {
                Target = closest; var identity = closest.GetComponent<EnemyIdentity>();
                displayedTitle = identity && !string.IsNullOrEmpty(identity.title) ? identity.title : closest is WastelandMutant ? "Wasteland Mutant" : "Arena War Truck";
                if (titleText) titleText.text = displayedTitle;
                displayedHealth = displayedMaximum = -1; displayedPhase = null;
            }
            float health = Mathf.Max(0, HealthOf(closest)), maximum = Mathf.Max(1, MaximumOf(closest));
            if (healthFill) healthFill.fillAmount = Mathf.Clamp01(health / maximum);
            int integerHealth = Mathf.CeilToInt(health), integerMaximum = Mathf.CeilToInt(maximum);
            if (integerHealth != displayedHealth || integerMaximum != displayedMaximum)
            {
                displayedHealth = integerHealth; displayedMaximum = integerMaximum;
                if (healthText) healthText.text = integerHealth + " / " + integerMaximum;
            }
            string phase = session.Playing ? PhaseOf(closest) : "PAUSED";
            if (phase != displayedPhase)
            {
                displayedPhase = phase;
                if (phaseText)
                {
                    phaseText.text = phase;
                    phaseText.gameObject.SetActive(!string.IsNullOrEmpty(phase));
                }
            }
            SetVisible(true);
        }
    }
}
