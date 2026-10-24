#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RoadGunner
{
    // Opt-in checks through real InputSystemUIInputModule pointer routing and wheel physics.
    public sealed class JoystickHeadingChecks : MonoBehaviour
    {
        GameSession session;
        Vector2 origin;
        float radius;
        Vector3 startPosition;
        bool running;
        readonly System.Text.StringBuilder results=new System.Text.StringBuilder();
        public void Begin(Vector3? testOrigin=null) { if(running)return;running=true;session=GetComponent<GameSession>();startPosition=testOrigin??session.startPosition;StartCoroutine(Run()); }
        void Record(string name,bool pass,string detail) {
            string line=(pass?"PASS ":"FAIL ")+name+": "+detail;
            results.AppendLine(line);Debug.Log("RG JOYSTICK "+line);
            File.WriteAllText("Documentation/RoadGunner/JoystickHeadingResults.txt",results.ToString());
        }
        void Keys(params Key[] keys) { InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys)); }
        void Pointer(Vector2 direction,bool held=true) {
            InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=origin+direction*radius,buttons=(ushort)(held?1:0)});
        }
        IEnumerator Frames() { yield return null;yield return null;yield return null; }
        IEnumerator Reset() {
            UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            Pointer(Vector2.zero,false);Keys();session.Restart();session.spawns.enabled=false;session.turret.enabled=false;
            foreach(var raider in session.spawns.pool)raider.Despawn();session.health.DamageEnabled=false;
            if(session.spawns.buggyPool!=null)foreach(var buggy in session.spawns.buggyPool)buggy.Despawn();
            session.vehicle.ResetPose(startPosition,Quaternion.Euler(0,45,0));session.follow.Snap();
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);
            var joy=session.input.joystick;
            origin=RectTransformUtility.WorldToScreenPoint(null,joy.ring.position);
            radius=Vector2.Distance(origin,RectTransformUtility.WorldToScreenPoint(null,joy.ring.TransformPoint(Vector3.right*joy.radius)));
            Pointer(Vector2.zero,false);yield return Frames();Pointer(Vector2.zero);yield return Frames();
        }
        IEnumerator Run() {
            Vector2[] directions={Vector2.up,new Vector2(1,1).normalized,Vector2.right,new Vector2(1,-1).normalized,Vector2.down,new Vector2(-1,-1).normalized,Vector2.left,new Vector2(-1,1).normalized};
            foreach(var direction in directions) {
                yield return Reset();Keys(Key.S);Pointer(direction);yield return Frames();
                Vector3 right=Vector3.ProjectOnPlane(session.input.view.transform.right,Vector3.up).normalized;
                Vector3 forward=Vector3.ProjectOnPlane(session.input.view.transform.forward,Vector3.up).normalized;
                Vector3 expected=(right*direction.x+forward*direction.y).normalized;
                bool reverse=Vector3.Dot(session.vehicle.transform.forward,expected)<-.5f;
                bool routed=session.input.joystick.IsHeld&&session.input.Reverse==reverse&&Vector3.Dot(session.input.WorldDirection,expected)>.999f&&session.input.Magnitude>.99f;
                float sign=reverse?-1:1;
                float start=Time.time,error=180;bool wrongGear=false;
                do {
                    yield return new WaitForFixedUpdate();
                    error=Vector3.Angle(Vector3.ProjectOnPlane(session.vehicle.transform.forward*sign,Vector3.up),expected);
                    wrongGear|=session.input.Reverse!=reverse||session.vehicle.ForwardSpeed*sign<-.5f;
                } while(Time.time-start<7&&(error>10||session.vehicle.ForwardSpeed*sign<1));
                Record("stick "+direction+" drives "+(reverse?"backward":"forward")+" toward screen direction",routed&&!wrongGear&&error<10&&session.vehicle.ForwardSpeed*sign>1,"heading error="+error.ToString("F1")+", forward speed="+session.vehicle.ForwardSpeed.ToString("F2")+", seconds="+(Time.time-start).ToString("F2")+", routed="+routed+", position="+session.vehicle.transform.position+", grounded="+session.vehicle.GroundedWheels);
            }
            yield return Reset();Pointer(Vector2.up);yield return new WaitForSeconds(1.4f);
            Quaternion before=session.vehicle.transform.rotation;Pointer(Vector2.down);yield return Frames();
            float initialTurn=Quaternion.Angle(before,session.vehicle.transform.rotation),initialSpeed=session.vehicle.ForwardSpeed;
            bool brakedFirst=session.vehicle.wheels[0].brakeTorque>0&&session.vehicle.wheels[0].motorTorque==0;
            float turnStarted=Time.time,turnError=180;
            do {
                yield return new WaitForFixedUpdate();
                turnError=Vector3.Angle(Vector3.ProjectOnPlane(-session.vehicle.transform.forward,Vector3.up),session.input.WorldDirection);
            } while(Time.time-turnStarted<5&&(turnError>=15||session.vehicle.ForwardSpeed>=-1));
            Record("rearward command brakes before reversing",initialTurn<10&&initialSpeed>0&&brakedFirst&&session.input.Reverse&&session.vehicle.ForwardSpeed<-1&&turnError<15,"initial turn="+initialTurn.ToString("F1")+", initial speed="+initialSpeed.ToString("F2")+", braked first="+brakedFirst+", final speed="+session.vehicle.ForwardSpeed.ToString("F2")+", final error="+turnError.ToString("F1"));
            yield return Reset();Pointer(Vector2.up*.1f);yield return Frames();Record("dead zone",session.input.Magnitude==0&&!session.input.Reverse,"magnitude="+session.input.Magnitude);
            Pointer(Vector2.up*.5f);yield return Frames();float half=session.input.Magnitude;
            Pointer(Vector2.down*.5f);yield return Frames();Record("analog magnitude symmetric in both gears",half>0&&half<1&&Mathf.Abs(half-session.input.Magnitude)<.001f&&session.input.Reverse,"up="+half+", down="+session.input.Magnitude);
            Pointer(Vector2.zero);Keys(Key.S);yield return Frames();Record("held centered stick overrides S",session.input.Magnitude==0&&!session.input.Reverse,"magnitude="+session.input.Magnitude);
            Pointer(Vector2.zero,false);Keys();yield return Frames();Record("release clears input",!session.input.joystick.IsHeld&&session.input.Magnitude==0&&!session.input.Reverse,"held="+session.input.joystick.IsHeld);
            Keys(Key.S);yield return new WaitForSeconds(2);Record("S still reverses after stick release",session.input.Reverse&&session.vehicle.ForwardSpeed<-1,"speed="+session.vehicle.ForwardSpeed.ToString("F2"));
            yield return Reset();Pointer(Vector2.zero,false);yield return Frames();
            var touch=InputSystem.AddDevice<Touchscreen>();Keys(Key.S);
            InputSystem.QueueStateEvent(touch,new TouchState{touchId=71,phase=UnityEngine.InputSystem.TouchPhase.Began,position=origin});yield return Frames();
            InputSystem.QueueStateEvent(touch,new TouchState{touchId=71,phase=UnityEngine.InputSystem.TouchPhase.Moved,position=origin+Vector2.down*radius});yield return Frames();
            Record("rearward touch engages reverse",session.input.joystick.IsHeld&&session.input.Reverse&&session.input.Direction.y<-.99f,"direction="+session.input.Direction+", reverse="+session.input.Reverse);
            Keys();InputSystem.QueueStateEvent(touch,new TouchState{touchId=71,phase=UnityEngine.InputSystem.TouchPhase.Canceled,position=origin+Vector2.down*radius});yield return Frames();
            Record("touch cancellation clears heading",!session.input.joystick.IsHeld&&session.input.Magnitude==0,"held="+session.input.joystick.IsHeld);InputSystem.RemoveDevice(touch);
            Keys();session.spawns.enabled=true;session.turret.enabled=true;session.Restart();running=false;Record("completed",true,"Normal session restored. Editor pointer simulation; physical device not tested.");
        }
        void OnDestroy() { if(Keyboard.current!=null)Keys();if(Mouse.current!=null)Pointer(Vector2.zero,false); }
    }
}
#endif
