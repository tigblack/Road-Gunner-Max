using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadGunner
{
    public sealed class DriveInput : MonoBehaviour
    {
        public FloatingJoystick joystick;
        public Camera view;
        public Vector2 Direction { get; private set; }
        public Vector3 WorldDirection { get; private set; }
        public float Magnitude { get; private set; }
        public bool Brake { get; private set; }
        public bool Reverse { get; private set; }
        public bool UsesHeadingSteering { get; private set; }
        public bool Enabled { get; set; } = true;
        public event System.Action PausePressed, RecoverPressed, DebugPressed;
        Transform vehicleHeading;
        bool joystickReverse;
        // The controller supplies its heading at runtime, including in existing scenes/prefabs.
        internal void BindVehicle(Transform heading) { vehicleHeading=heading; joystickReverse=false; }
        // Deterministic editor harness feeds the same input/physics path; never used by normal play.
        [System.NonSerialized] public bool testInput;
        [System.NonSerialized] public Vector2 testDirection;
        [System.NonSerialized] public bool testBrake;
        void Update()
        {
            var k=Keyboard.current;
            if(k != null) {
                if(k.escapeKey.wasPressedThisFrame) PausePressed?.Invoke();
                if(k.rKey.wasPressedThisFrame && Enabled) RecoverPressed?.Invoke();
                if(k.f3Key.wasPressedThisFrame) DebugPressed?.Invoke();
            }
            if(!Enabled) { Clear(); return; }
            Vector2 raw=Vector2.zero;
            bool joystickHeld=joystick != null && joystick.IsHeld;
            if(joystickHeld) raw=joystick.Value;
            else if(k!=null) raw=new Vector2((k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0), (k.wKey.isPressed||k.upArrowKey.isPressed?1:0)-(k.sKey.isPressed||k.downArrowKey.isPressed?1:0));
            if(testInput) raw=testDirection;
            raw=Vector2.ClampMagnitude(raw,1);
            float m=raw.magnitude;
            Magnitude=m<=.12f?0:Mathf.Pow(Mathf.InverseLerp(.12f,1,m),1.25f);
            Direction=m>.12f?raw.normalized:Vector2.zero;
            UsesHeadingSteering=joystickHeld || testInput;
            Vector3 right=Vector3.ProjectOnPlane(view.transform.right,Vector3.up).normalized;
            Vector3 forward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
            WorldDirection=(right*Direction.x+forward*Direction.y).normalized;
            if(UsesHeadingSteering && Magnitude>0 && vehicleHeading) {
                float alignment=Vector3.Dot(Vector3.ProjectOnPlane(vehicleHeading.forward,Vector3.up).normalized,WorldDirection);
                // Pull toward the tail to back out of obstacles. Enter beyond 120 degrees;
                // remain in reverse until the stick crosses 90 degrees to avoid gear chatter.
                joystickReverse=alignment<(joystickReverse?0f:-.5f);
                Reverse=joystickReverse;
            } else {
                joystickReverse=false;
                Reverse=!UsesHeadingSteering && Magnitude>0 && raw.y<-.12f;
            }
            Brake=testInput?testBrake:k!=null&&k.spaceKey.isPressed;
        }
        public void Clear() { Direction=Vector2.zero; WorldDirection=Vector3.zero; Magnitude=0; Brake=false; Reverse=false; UsesHeadingSteering=false; joystickReverse=false; if(joystick) joystick.ResetPointer(); }
        void OnApplicationFocus(bool focused) { if(!focused) Clear(); }
        void OnApplicationPause(bool paused) { if(paused) Clear(); }
        void OnDisable() { Clear(); }
    }
}
