#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace RoadGunner
{
    public sealed class ReverseChecks : MonoBehaviour
    {
        GameSession s;
        readonly System.Text.StringBuilder results=new System.Text.StringBuilder();
        public void Begin(){s=GetComponent<GameSession>();StartCoroutine(Run());}
        void Record(string name,bool pass,string detail){string text=(pass?"PASS ":"FAIL ")+name+": "+detail;results.AppendLine(text);File.WriteAllText("Documentation/RoadGunner/ReverseResults.txt",results.ToString());Debug.Log("RG REVERSE "+text);}
        void Keys(params Key[] keys){InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys));}
        IEnumerator Reset(float yaw=45){Keys();s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();if(s.spawns.buggyPool!=null)foreach(var b in s.spawns.buggyPool)b.Despawn();s.health.DamageEnabled=false;s.vehicle.ResetPose(s.startPosition,Quaternion.Euler(0,yaw,0));Physics.SyncTransforms();yield return new WaitForSeconds(.7f);}
        IEnumerator Run(){
            yield return Reset(135);Vector3 start=s.vehicle.transform.position,back=-s.vehicle.transform.forward;Quaternion heading=s.vehicle.transform.rotation;Keys(Key.S);yield return new WaitForSeconds(2);
            Record("S reverses in chassis direction",s.vehicle.ForwardSpeed<-1&&Vector3.Dot(s.vehicle.transform.position-start,back)>2&&Quaternion.Angle(heading,s.vehicle.transform.rotation)<8,"speed="+s.vehicle.ForwardSpeed.ToString("F2")+", backward travel="+Vector3.Dot(s.vehicle.transform.position-start,back).ToString("F2"));
            Keys();float coast=s.vehicle.ForwardSpeed;yield return new WaitForSeconds(.6f);Record("reverse release coasts",s.vehicle.ForwardSpeed<-.3f&&s.vehicle.ForwardSpeed>coast,"before="+coast.ToString("F2")+", after="+s.vehicle.ForwardSpeed.ToString("F2"));
            Keys(Key.Space);yield return new WaitForSeconds(1.5f);Record("service brake stops reverse",s.vehicle.Speed<.5f,"speed="+s.vehicle.Speed.ToString("F2"));
            yield return Reset();Keys(Key.W);yield return new WaitForSeconds(1.7f);float forward=s.vehicle.ForwardSpeed;Keys(Key.S);yield return null;yield return new WaitForFixedUpdate();float first=s.vehicle.ForwardSpeed;yield return new WaitForSeconds(3);
            Record("forward to reverse brakes before backing",forward>5&&first>0&&s.vehicle.ForwardSpeed<-1,"before="+forward.ToString("F2")+", first step="+first.ToString("F2")+", after="+s.vehicle.ForwardSpeed.ToString("F2"));
            Keys(Key.W);yield return null;yield return new WaitForFixedUpdate();first=s.vehicle.ForwardSpeed;yield return new WaitForSeconds(2.7f);Record("reverse to forward brakes before accelerating",first<0&&s.vehicle.ForwardSpeed>2,"first step="+first.ToString("F2")+", after="+s.vehicle.ForwardSpeed.ToString("F2"));
            yield return Reset();Keys(Key.DownArrow);yield return new WaitForSeconds(1.5f);Record("down arrow reverses",s.vehicle.ForwardSpeed<-1,"speed="+s.vehicle.ForwardSpeed.ToString("F2"));
            yield return Reset();var mouse=Mouse.current;Vector2 p=RectTransformUtility.WorldToScreenPoint(null,s.input.joystick.ring.position);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=1});yield return null;yield return null;
            Keys(Key.S);InputSystem.QueueStateEvent(mouse,new MouseState{position=p+Vector2.down*80,buttons=1});yield return new WaitForSeconds(.8f);
            Record("downward joystick selects forward gear and overrides S",s.input.joystick.IsHeld&&!s.input.Reverse&&s.vehicle.ForwardSpeed>1,"held="+s.input.joystick.IsHeld+", speed="+s.vehicle.ForwardSpeed.ToString("F2"));
            heading=s.vehicle.transform.rotation;InputSystem.QueueStateEvent(mouse,new MouseState{position=p+new Vector2(60,-80),buttons=1});yield return new WaitForSeconds(1.2f);
            Record("lower diagonal joystick steers forward",!s.input.Reverse&&s.vehicle.ForwardSpeed>.5f&&Quaternion.Angle(heading,s.vehicle.transform.rotation)>8,"heading delta="+Quaternion.Angle(heading,s.vehicle.transform.rotation).ToString("F1"));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});Keys();yield return null;yield return null;Record("joystick release clears reverse",!s.input.Reverse&&!s.input.joystick.IsHeld,"reverse="+s.input.Reverse);
            yield return Reset();Keys(Key.S);yield return new WaitForSeconds(4);Record("reverse speed capped",s.vehicle.ForwardSpeed<-4&&s.vehicle.ForwardSpeed>-s.vehicle.tuning.maxReverseSpeed-.7f,"speed="+s.vehicle.ForwardSpeed.ToString("F2")+", limit="+s.vehicle.tuning.maxReverseSpeed);
            s.SetPaused(true);Record("pause clears reverse input",!s.input.Reverse&&s.input.Magnitude==0,"reverse="+s.input.Reverse);Keys();s.spawns.enabled=true;s.turret.enabled=true;s.Restart();Record("restart restores normal input",!s.input.Reverse&&s.input.Enabled&&Time.timeScale==1,"state="+s.State);
        }
        void OnDestroy(){if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());}
    }
}
#endif
