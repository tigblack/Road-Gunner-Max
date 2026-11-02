using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>
    /// Springy press feedback for a chunky HUD button. Only the decorative face child scales (around its
    /// centre); the Button's own rect, raycast surface and click routing stay exactly where they were.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArcadeHudButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public RectTransform face;
        [Range(.8f, 1)] public float pressedScale = .92f;
        Selectable selectable;
        bool held, inside;
        float scale = 1, velocity;

        void Awake() { selectable = GetComponent<Selectable>(); }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || (selectable && !selectable.IsInteractable())) return;
            held = inside = true;
        }
        public void OnPointerUp(PointerEventData e) { held = false; }
        public void OnPointerEnter(PointerEventData e) { inside = true; }
        public void OnPointerExit(PointerEventData e) { inside = false; }

        void Update()
        {
            if (!face) return;
            float target = held && inside ? pressedScale : 1;
            if (!held && Mathf.Abs(scale - 1) < .0005f && Mathf.Abs(velocity) < .001f)
            {
                if (face.localScale != Vector3.one) face.localScale = Vector3.one;
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            velocity += (target - scale) * 900 * dt;
            velocity *= Mathf.Exp(-26 * dt);
            scale += velocity * dt;
            face.localScale = new Vector3(scale, scale, 1);
        }

        void OnDisable()
        {
            held = inside = false; scale = 1; velocity = 0;
            if (face) face.localScale = Vector3.one;
        }
    }
}
