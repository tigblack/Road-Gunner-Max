using UnityEngine;
using UnityEngine.InputSystem;
namespace RoadGunner
{
    public sealed class DriveInput : MonoBehaviour
    {
        public FloatingJoystick joystick;
        public Camera view;
        [Range(0,.5f)] public float joystickDeadZone=.12f;
        [Min(.1f)] public float inputResponseExponent=1.25f;
        public Vector2 Direction { get; private set; }
        public Vector3 WorldDirection { get; private set; }
        public float Magnitude { get; private set; }
        public bool Brake { get; private set; }
        // Kept for existing HUD/diagnostic callers. Directional controls always drive nose first.
        public bool Reverse { get; private set; }
        public bool UsesHeadingSteering { get; private set; }
        public bool Enabled { get; set; } = true;
        public event System.Action PausePressed, RecoverPressed, DebugPressed;
        // Deterministic editor harness feeds the same input/physics path; never used by normal play.
        [System.NonSerialized] public bool testInput;
        [System.NonSerialized] public Vector2 testDirection;
        [System.NonSerialized] public bool testBrake;

        void Awake() { if(!view)view=Camera.main; }
        public Vector3 ScreenToWorldDirection(Vector2 screenDirection)
        {
            if(screenDirection.sqrMagnitude<.0001f)return Vector3.zero;
            // The projected camera axes match what the player sees, independently of its vertical tilt.
            Vector3 right=view?Vector3.ProjectOnPlane(view.transform.right,Vector3.up).normalized:Vector3.right;
            Vector3 forward=view?Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized:Vector3.forward;
            return (right*screenDirection.x+forward*screenDirection.y).normalized;
        }
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
            float m=raw.magnitude,deadZone=Mathf.Clamp(joystickDeadZone,0,.5f);
            Magnitude=m<=deadZone?0:Mathf.Pow(Mathf.InverseLerp(deadZone,1,m),Mathf.Max(.1f,inputResponseExponent));
            Direction=Magnitude>0?raw.normalized:Vector2.zero;
            WorldDirection=ScreenToWorldDirection(Direction);
            UsesHeadingSteering=Magnitude>0;
            Reverse=false;
            Brake=testInput?testBrake:k!=null&&k.spaceKey.isPressed;
        }
        public void Clear() { Direction=Vector2.zero; WorldDirection=Vector3.zero; Magnitude=0; Brake=false; Reverse=false; UsesHeadingSteering=false; if(joystick) joystick.ResetPointer(); }
        void OnApplicationFocus(bool focused) { if(!focused) Clear(); }
        void OnApplicationPause(bool paused) { if(paused) Clear(); }
        void OnDisable() { Clear(); }
    }
}
