using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
namespace RoadGunner
{
    public sealed class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IDragHandler, IPointerUpHandler, IEndDragHandler, ICancelHandler
    {
        public RectTransform ring, thumb;
        [Min(1)] public float radius=105;
        public Vector2 Value { get; private set; }
        public bool IsHeld { get; private set; }
        public Vector2 OriginScreenPosition { get; private set; }
        int pointer=int.MinValue, touchId;
        Vector2 originLocal;
        Camera pointerCamera;
        InputDevice pointerDevice;
        bool touchPointer;
        void Awake() { ResetPointer(); }
        void OnEnable() { ResetPointer(); }
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold=false; }
        public void OnPointerDown(PointerEventData e) {
            if(IsHeld || !isActiveAndEnabled || Time.timeScale<=0 || e.button!=PointerEventData.InputButton.Left || !ring || !thumb)return;
            var parent=ring.parent as RectTransform;
            if(!parent || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,e.position,e.pressEventCamera,out originLocal))return;
            IsHeld=true;pointer=e.pointerId;pointerCamera=e.pressEventCamera;OriginScreenPosition=e.position;
            var extended=e as ExtendedPointerEventData;
            touchPointer=extended!=null&&extended.pointerType==UIPointerType.Touch;
            touchId=extended!=null?extended.touchId:0;pointerDevice=extended!=null?extended.device:null;
            // Never clamp the origin to an inset. Even a touch at the edge starts exactly under that finger.
            ring.localPosition=new Vector3(originLocal.x,originLocal.y,ring.localPosition.z);
            ring.gameObject.SetActive(true);thumb.anchoredPosition=Vector2.zero;Value=Vector2.zero;e.useDragThreshold=false;
        }
        public void OnDrag(PointerEventData e) {
            if(!IsHeld || e.pointerId!=pointer || !ring)return;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)ring.parent,e.position,pointerCamera,out Vector2 point))return;
            Value=Vector2.ClampMagnitude((point-originLocal)/Mathf.Max(1,radius),1);thumb.anchoredPosition=Value*radius;
        }
        public void OnPointerUp(PointerEventData e) { if(IsHeld && e.pointerId==pointer)ResetPointer(); }
        public void OnEndDrag(PointerEventData e) { if(IsHeld && e.pointerId==pointer)ResetPointer(); }
        public void OnCancel(BaseEventData e) { ResetPointer(); }
        public void ResetPointer() {
            IsHeld=false;pointer=int.MinValue;touchId=0;pointerDevice=null;touchPointer=false;pointerCamera=null;Value=Vector2.zero;
            if(thumb)thumb.anchoredPosition=Vector2.zero;
            if(ring)ring.gameObject.SetActive(false);
        }
        void Update() {
            if(!IsHeld)return;
            if(pointerDevice!=null&&!pointerDevice.added){ResetPointer();return;}
            if(touchPointer) {
                var touchscreen=pointerDevice as Touchscreen;
                if(touchscreen==null){ResetPointer();return;}
                foreach(var touch in touchscreen.touches)if(touch.touchId.ReadValue()==touchId) {
                    var phase=touch.phase.ReadValue();
                    if(!touch.press.isPressed||phase==UnityEngine.InputSystem.TouchPhase.Canceled||phase==UnityEngine.InputSystem.TouchPhase.Ended||phase==UnityEngine.InputSystem.TouchPhase.None)ResetPointer();
                    return;
                }
                ResetPointer();
            }
            else if(pointerDevice is Mouse mouse&&!mouse.leftButton.isPressed)ResetPointer();
            else if(pointerDevice is Pen pen&&!pen.tip.isPressed)ResetPointer();
        }
        void OnApplicationFocus(bool focused) { if(!focused)ResetPointer(); }
        void OnApplicationPause(bool paused) { if(paused)ResetPointer(); }
        void OnDisable() { ResetPointer(); }
    }
}
