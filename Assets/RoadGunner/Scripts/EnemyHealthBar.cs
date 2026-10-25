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
        public bool IsVisible => worldCanvas && worldCanvas.enabled && worldCanvas.gameObject.activeInHierarchy;
        public float DisplayedRatio => healthFill ? healthFill.fillAmount : 0;
        public int HealthRefreshCount { get; private set; }
        CombatTarget subscribedTarget;
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
            if (healthFill) healthFill.fillAmount = target ? Mathf.Clamp01(target.CurrentHealth / Mathf.Max(1, target.MaxHealth)) : 0;
            bool show = isActiveAndEnabled && target && target.Alive && !target.UsesBossHealthHud && gameplayCamera;
            if (worldCanvas) worldCanvas.enabled = show;
            if (show) PositionAndFaceCamera();
        }
        void PositionAndFaceCamera()
        {
            if (!anchor || !target || !gameplayCamera) return;
            anchor.SetPositionAndRotation(target.transform.position + Vector3.up * height, gameplayCamera.transform.rotation);
        }
        void LateUpdate()
        {
            if (IsVisible) PositionAndFaceCamera();
        }
        void OnDisable()
        {
            if (subscribedTarget) subscribedTarget.HealthChanged -= RefreshHealth;
            subscribedTarget = null;
            if (worldCanvas) worldCanvas.enabled = false;
        }
    }
}
