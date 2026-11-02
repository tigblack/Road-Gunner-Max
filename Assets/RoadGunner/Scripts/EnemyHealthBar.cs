using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Prebuilt, pooled world UI reading the combat target's actual health. Never owns health.</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        public CombatTarget target;
        public Camera gameplayCamera;
        public Transform anchor;
        public Canvas worldCanvas;
        public Image healthFill;
        [Min(.1f)] public float height = 2.45f;
        [Header("Arcade presentation (optional)")]
        [Tooltip("White chunk that trails the real fill down after a hit. Decorative; the real fill updates instantly.")]
        public Image damageTrail;
        [Tooltip("Fades untouched bars so a group of full-health enemies is not a wall of red slabs.")]
        public CanvasGroup fadeGroup;
        [Range(0, 1)] public float fullHealthAlpha = .62f;
        public bool IsVisible => worldCanvas && worldCanvas.enabled && worldCanvas.gameObject.activeInHierarchy;
        public float DisplayedRatio => healthFill ? healthFill.fillAmount : 0;
        public int HealthRefreshCount { get; private set; }
        CombatTarget subscribedTarget;
        float trailHold;
        static Camera fallbackCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCameraCache() { fallbackCamera = null; }

        void OnEnable()
        {
            if (!target) target = GetComponent<CombatTarget>();
            Bind(); CacheCamera(); RefreshHealth();
        }
        void Bind()
        {
            if (subscribedTarget == target) return;
            if (subscribedTarget) subscribedTarget.HealthChanged -= RefreshHealth;
            subscribedTarget = target;
            if (subscribedTarget) subscribedTarget.HealthChanged += RefreshHealth;
        }
        void CacheCamera()
        {
            if (gameplayCamera) { if (worldCanvas) worldCanvas.worldCamera = gameplayCamera; return; }
            GameSession session = target is Raider raider ? raider.session : target is EnemyBuggy buggy ? buggy.session : null;
            if (session && session.input && session.input.view) gameplayCamera = session.input.view;
            if (!gameplayCamera)
            {
                // A future prefab without a serialized session shares one fallback lookup per scene.
                if (!fallbackCamera) fallbackCamera = Camera.main;
                gameplayCamera = fallbackCamera;
            }
            if (worldCanvas) worldCanvas.worldCamera = gameplayCamera;
        }
        public void RefreshHealth()
        {
            HealthRefreshCount++;
            float ratio = target ? Mathf.Clamp01(target.CurrentHealth / Mathf.Max(1, target.MaxHealth)) : 0;
            if (healthFill) healthFill.fillAmount = ratio;
            bool show = isActiveAndEnabled && target && target.Alive && !target.UsesBossHealthHud && gameplayCamera;
            if (worldCanvas) worldCanvas.enabled = show;
            if (damageTrail)
            {
                // Heals, respawns and deaths snap the trail; a hit leaves the old value to drain after a short hold.
                if (!show || ratio >= damageTrail.fillAmount) damageTrail.fillAmount = ratio;
                else trailHold = Time.unscaledTime + .22f;
            }
            if (fadeGroup) fadeGroup.alpha = ratio >= .999f ? fullHealthAlpha : 1;
            if (show) PositionAndFaceCamera();
        }
        void PositionAndFaceCamera()
        {
            if (!anchor || !target || !gameplayCamera) return;
            anchor.SetPositionAndRotation(target.transform.position + Vector3.up * height, gameplayCamera.transform.rotation);
        }
        void LateUpdate()
        {
            if (!IsVisible) return;
            PositionAndFaceCamera();
            if (damageTrail && healthFill)
            {
                float fill = healthFill.fillAmount, trail = damageTrail.fillAmount;
                if (trail > fill + .0005f && Time.unscaledTime >= trailHold)
                    damageTrail.fillAmount = Mathf.Max(fill, trail - Mathf.Min(.1f, Time.unscaledDeltaTime) * Mathf.Max(.7f, (trail - fill) * 4.5f));
            }
        }
        void OnDisable()
        {
            if (subscribedTarget) subscribedTarget.HealthChanged -= RefreshHealth;
            subscribedTarget = null;
            if (worldCanvas) worldCanvas.enabled = false;
        }
    }
}
