using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
namespace RoadGunner
{
    public sealed class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform ring, thumb;
        public float radius=105;
        public Vector2 Value { get; private set; }
        public bool IsHeld { get; private set; }
        int pointer, touchId;
        Vector2 home;
        void Awake() { home=ring.anchoredPosition; }
        public void OnPointerDown(PointerEventData e) {
            if(IsHeld || Time.timeScale==0) return;
            IsHeld=true; pointer=e.pointerId; touchId=e is ExtendedPointerEventData extended ? extended.touchId : 0;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,e.position,e.pressEventCamera,out Vector2 p);
            var bounds=((RectTransform)transform).rect;
            p.x=Mathf.Clamp(p.x,bounds.xMin+radius,bounds.xMax-radius);
            p.y=Mathf.Clamp(p.y,bounds.yMin+radius,bounds.yMax-radius);
            ring.localPosition=p;
            OnDrag(e);
        }
        public void OnDrag(PointerEventData e) {
            if(!IsHeld || e.pointerId!=pointer) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(ring,e.position,e.pressEventCamera,out Vector2 p);
            Value=Vector2.ClampMagnitude(p/radius,1); thumb.anchoredPosition=Value*radius;
        }
        public void OnPointerUp(PointerEventData e) { if(IsHeld && e.pointerId==pointer) ResetPointer(); }
        public void ResetPointer() { IsHeld=false; pointer=int.MinValue; touchId=0; Value=Vector2.zero; if(thumb) thumb.anchoredPosition=Vector2.zero; if(ring) ring.anchoredPosition=home; }
        void Update() {
            if(!IsHeld)return;
            if(touchId>0 && Touchscreen.current!=null) {
                foreach(var touch in Touchscreen.current.touches) if(touch.touchId.ReadValue()==touchId) {
                    var phase=touch.phase.ReadValue();
                    if(phase==UnityEngine.InputSystem.TouchPhase.Canceled||phase==UnityEngine.InputSystem.TouchPhase.Ended||phase==UnityEngine.InputSystem.TouchPhase.None)ResetPointer();
                    return;
                }
                ResetPointer();
            }
            else if(Mouse.current!=null&&!Mouse.current.leftButton.isPressed) ResetPointer();
        }
        void OnApplicationFocus(bool value) { if(!value) ResetPointer(); }
        void OnApplicationPause(bool value) { if(value) ResetPointer(); }
        void OnDisable() { ResetPointer(); }
    }
}
