#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace RoadGunner
{
    // Opt-in play-mode instrumentation. Never attached to the saved scene or included in a player.
    public sealed class PrototypeChecks : MonoBehaviour
    {
        [System.Serializable] public class Entry { public string check;public bool passed;public string observation; }
        [System.Serializable] public class Results { public string environment;public List<Entry> checks=new List<Entry>(); }
        Results results=new Results(); GameSession s; bool running;
        public void Begin(bool endurance=false) {if(running)return;running=true;enduranceRun=endurance;results=new Results();s=GetComponent<GameSession>();results.environment=SystemInfo.operatingSystem+" / "+SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName+" / Unity "+Application.unityVersion;StartCoroutine(endurance?Endurance():Checks());}
        void Record(string name,bool pass,string value){results.checks.Add(new Entry{check=name,passed=pass,observation=value});Debug.Log("RG CHECK "+(pass?"PASS ":"FAIL ")+name+": "+value);Directory.CreateDirectory("Documentation/RoadGunner");File.WriteAllText("Documentation/RoadGunner/"+(enduranceRun?"EnduranceResults.json":"PlayModeResults.json"),JsonUtility.ToJson(results,true));}
        bool enduranceRun;
        void Isolate(){s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();if(s.spawns.buggyPool!=null)foreach(var b in s.spawns.buggyPool)b.Despawn();s.health.DamageEnabled=false;}
        void Position(float x,float z,float yaw=0){float y=2;if(Physics.Raycast(new Vector3(x,20,z),Vector3.down,out RaycastHit h,40,1))y=h.point.y+.70f;s.vehicle.ResetPose(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));s.input.testInput=true;s.input.testDirection=Vector2.zero;s.input.testBrake=false;s.follow.Snap();Physics.SyncTransforms();}
        IEnumerator Settle(){yield return new WaitForSeconds(.8f);}
        IEnumerator Accelerate(float speed){s.input.testDirection=new Vector2(-.707107f,.707107f);float start=Time.time;while(s.vehicle.ForwardSpeed<speed&&Time.time-start<6)yield return new WaitForFixedUpdate();}
        IEnumerator Checks(){
            Isolate();Position(-6,-16);yield return Settle();
            Record("static suspension",s.vehicle.GroundedWheels==4&&Vector3.Dot(s.vehicle.transform.up,Vector3.up)>.98f,"grounded="+s.vehicle.GroundedWheels+", position="+s.vehicle.transform.position);
            float t=Time.time;yield return Accelerate(12);float accel=Time.time-t;Record("0–12 m/s acceleration",accel>=2.5f&&accel<=3.5f,"seconds="+accel.ToString("F2")+", speed="+s.vehicle.Speed.ToString("F2"));
            s.input.testBrake=true;t=Time.time;while(s.vehicle.Speed>.5f&&Time.time-t<4)yield return new WaitForFixedUpdate();float braking=Time.time-t;Record("12–0.5 m/s service brake",braking>=1.4f&&braking<=2.1f,"seconds="+braking.ToString("F2"));
            Position(-6,-16);yield return Settle();yield return Accelerate(10);Vector3 coastStart=s.vehicle.transform.position;float initial=s.vehicle.Speed;s.input.testDirection=Vector2.zero;yield return new WaitForSeconds(2);float coastDistance=Vector3.Distance(coastStart,s.vehicle.transform.position);Record("coasting after release",coastDistance>8&&s.vehicle.Speed>1&&s.vehicle.Speed<initial,"2-second distance="+coastDistance.ToString("F2")+", remaining speed="+s.vehicle.Speed.ToString("F2"));
            Position(-5,-15);yield return Settle();yield return Accelerate(7);Vector3 turnStart=s.vehicle.transform.position;Quaternion before=s.vehicle.transform.rotation;s.input.testDirection=new Vector2(.707107f,.707107f);yield return new WaitForFixedUpdate();float snap=Quaternion.Angle(before,s.vehicle.transform.rotation);yield return new WaitForSeconds(2);float ninety=Quaternion.Angle(before,s.vehicle.transform.rotation);Record("90-degree command makes an arc",snap<6&&ninety>25&&Vector3.Distance(turnStart,s.vehicle.transform.position)>3,"first-step rotation="+snap.ToString("F2")+", after 2s="+ninety.ToString("F1")+", travel="+Vector3.Distance(turnStart,s.vehicle.transform.position).ToString("F1"));
            Position(-5,-15);yield return Settle();yield return Accelerate(7);before=s.vehicle.transform.rotation;s.input.testDirection=new Vector2(.707107f,-.707107f);yield return null;yield return new WaitForFixedUpdate();float forward=s.vehicle.ForwardSpeed;float firstTurn=Quaternion.Angle(before,s.vehicle.transform.rotation);float turnStarted=Time.time;float headingError=180;do{yield return new WaitForFixedUpdate();headingError=Vector3.Angle(Vector3.ProjectOnPlane(s.vehicle.transform.forward,Vector3.up),s.input.WorldDirection);}while(headingError>15&&Time.time-turnStarted<6);Record("opposite stick command makes a forward U-turn",forward>0&&firstTurn<6&&!s.input.Reverse&&s.vehicle.ForwardSpeed>.5f&&headingError<20,"first-step forward speed="+forward.ToString("F2")+", final speed="+s.vehicle.ForwardSpeed.ToString("F2")+", heading error="+headingError.ToString("F1"));
            Position(-13,-8);yield return Settle();s.input.testDirection=new Vector2(-.707107f,.707107f)*.28f;float maxRoll=0,maxDiff=0,maxSlip=0;t=Time.time;while(Time.time-t<5){float a=s.vehicle.transform.eulerAngles.z;if(a>180)a-=360;maxRoll=Mathf.Max(maxRoll,Mathf.Abs(a));float y0=s.vehicle.transform.InverseTransformPoint(s.vehicle.wheelVisuals[0].position).y,y1=s.vehicle.transform.InverseTransformPoint(s.vehicle.wheelVisuals[1].position).y;maxDiff=Mathf.Max(maxDiff,Mathf.Abs(y0-y1));yield return new WaitForFixedUpdate();}Record("uneven lane suspension",maxDiff>.005f&&maxRoll<25,"max left/right travel difference="+maxDiff.ToString("F3")+" m, roll="+maxRoll.ToString("F1"));
            Position(-6,-16);yield return Settle();yield return Accelerate(12);s.input.testDirection=new Vector2(.707107f,.707107f);t=Time.time;while(Time.time-t<2.5f){maxSlip=Mathf.Max(maxSlip,s.vehicle.LargestSlip);yield return new WaitForFixedUpdate();}Record("hard turn tire slip",maxSlip>.1f&&Vector3.Dot(s.vehicle.transform.up,Vector3.up)>.7f,"peak lateral slip="+maxSlip.ToString("F3"));
            // Input System key state is injected into the actual production input router.
            Position(0,0,45);yield return Settle();s.input.testInput=false;var keyboard=Keyboard.current;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return null;yield return null;float wdot=Vector3.Dot(s.input.WorldDirection,Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized);float wm=s.input.Magnitude;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.D));yield return null;yield return null;float dm=s.input.Magnitude;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;Record("W is screen-up; diagonal normalized",wdot>.999f&&Mathf.Abs(dm-wm)<.001f&&wm>.99f,"W dot screen-up="+wdot+", W magnitude="+wm+", diagonal="+dm);
            // Queue actual pointer device events through InputSystemUIInputModule.
            var mouse=Mouse.current;var joy=s.input.joystick;Vector2 p=RectTransformUtility.WorldToScreenPoint(null,joy.ring.position);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=1});yield return null;yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p+Vector2.up*80,buttons=1});yield return null;yield return null;bool held=joy.IsHeld&&s.input.Magnitude>.1f;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p+Vector2.up*80});yield return null;yield return null;Record("mouse joystick drag and release",held&&!joy.IsHeld&&joy.Value==Vector2.zero,"drag produced input="+held+", released="+!joy.IsHeld);
            s.SetPaused(true);Vector3 pauseP=s.vehicle.transform.position;int shots=s.turret.ShotsFired;yield return new WaitForSecondsRealtime(.3f);bool frozen=Time.timeScale==0&&Vector3.Distance(pauseP,s.vehicle.transform.position)<.01f&&!joy.IsHeld;s.SetPaused(false);Record("pause freezes physics and clears input",frozen&&Time.timeScale==1,"frozen="+frozen+", shots unchanged="+(shots==s.turret.ShotsFired));
            s.health.DamageEnabled=true;s.health.Damage(24,false);float hp=s.health.Current;s.AddScrap(3);s.vehicle.ResetPose(new Vector3(0,1,0),Quaternion.Euler(0,45,160));s.Recover();yield return new WaitForSeconds(.6f);Record("recovery preserves score and health",s.health.Current==hp&&s.Scrap==3&&Vector3.Dot(s.vehicle.transform.up,Vector3.up)>.98f,"health="+s.health.Current+", scrap="+s.Scrap+", grounded="+s.vehicle.GroundedWheels);
            s.spawns.enabled=true;s.turret.enabled=true;s.Restart();yield return null;
            var enemy=s.spawns.pool[0];int kills=s.Kills;enemy.Hit(999,Vector3.forward);enemy.Hit(999,Vector3.forward);Record("death reward exactly once",s.Kills==kills+1&&s.pickups.StoredValue==1&&!enemy.Alive,"kills delta="+(s.Kills-kills)+", pickup value="+s.pickups.StoredValue);
            s.pickups.ResetPool();for(int i=0;i<85;i++)s.pickups.Drop(new Vector3(30,2,30),1);Record("pickup cap preserves excess reward",s.pickups.ActiveCount==60&&s.pickups.StoredValue==85,"objects="+s.pickups.ActiveCount+", total value="+s.pickups.StoredValue);
            bool restartOK=true;for(int i=0;i<3;i++){s.health.Damage(999,false);restartOK&=s.State==SessionState.Defeated&&Time.timeScale==0;s.Restart();yield return null;restartOK&=s.State==SessionState.Playing&&s.health.Current==100&&s.Kills==0&&s.Scrap==0&&s.pickups.ActiveCount==0&&Time.timeScale==1&&Object.FindObjectsByType<GameSession>().Length==1;}
            Record("defeat and three restarts",restartOK,"state="+s.State+", hp="+s.health.Current+", sessions="+Object.FindObjectsByType<GameSession>().Length+", enemies="+s.spawns.ActiveCount);
            Isolate();Position(0,0,45);yield return Settle();s.turret.enabled=true;
            enemy=s.spawns.pool[0];NavMesh.SamplePosition(new Vector3(7,0,7),out NavMeshHit nav,3,NavMesh.AllAreas);enemy.Spawn(nav.position,0,0);enemy.enabled=false;
            Vector3 ray=s.turret.muzzle.position;Vector3 target=enemy.AimPoint;var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary occlusion test";wall.transform.position=(ray+target)*.5f;wall.transform.localScale=new Vector3(3,4,1);wall.transform.rotation=Quaternion.LookRotation(target-ray);Physics.SyncTransforms();float original=enemy.Health;yield return new WaitForSeconds(1);bool blocked=enemy.Health==original;Destroy(wall);yield return new WaitForSeconds(2);bool hit=enemy.Health<original;Record("occluder blocks actual turret hits",blocked&&hit,"protected behind obstacle="+blocked+", damaged after removal="+hit);enemy.enabled=true;
            s.spawns.enabled=true;s.turret.enabled=true;s.Restart();Record("checks completed",true,"Results saved; normal session restored.");running=false;
        }
        IEnumerator Endurance(){
            enduranceRun=true;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();s.health.DamageEnabled=false;s.input.testInput=true;
            Vector3[] route={new Vector3(17,0,8),new Vector3(27,0,30),new Vector3(-15,0,34),new Vector3(-33,0,4),new Vector3(-24,0,-30),new Vector3(27,0,-33),new Vector3(33,0,0)};
            int waypoint=0,maxEnemies=0,maxPickups=0,frames=0;float totalFrame=0,worstFrame=0;bool cap=true,rewards=true;float start=Time.time,nextSample=start+30,nextRecover=start+25;
            while(Time.time-start<300){
                Vector3 delta=route[waypoint]-s.vehicle.transform.position;delta.y=0;if(delta.magnitude<5)waypoint=(waypoint+1)%route.Length;
                Vector3 right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized,forward=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;
                s.input.testDirection=new Vector2(Vector3.Dot(delta.normalized,right),Vector3.Dot(delta.normalized,forward))*.70f;
                if(Time.time>nextRecover){if(s.vehicle.Speed<1)s.Recover();nextRecover=Time.time+25;}
                maxEnemies=Mathf.Max(maxEnemies,s.spawns.ActiveCount);maxPickups=Mathf.Max(maxPickups,s.pickups.ActiveCount);cap&=s.spawns.ActiveCount<=30&&s.pickups.ActiveCount<=60;rewards&=s.ScrapEarned==s.Scrap+s.pickups.StoredValue;
                frames++;totalFrame+=Time.unscaledDeltaTime;worstFrame=Mathf.Max(worstFrame,Time.unscaledDeltaTime);
                if(Time.time>=nextSample){Record("endurance "+Mathf.RoundToInt(Time.time-start)+"s",cap&&rewards,"kills="+s.Kills+", scrap="+s.Scrap+", uncollected="+s.pickups.StoredValue+", active="+s.spawns.ActiveCount+", position="+s.vehicle.transform.position);nextSample+=30;}
                yield return null;
            }
            Record("five-minute pooled combat session",cap&&rewards,"max enemies="+maxEnemies+", max pickups="+maxPickups+", kills="+s.Kills+", mean frame="+(totalFrame/frames*1000).ToString("F1")+"ms, worst="+(worstFrame*1000).ToString("F1")+"ms. Editor instrumentation; invulnerable vehicle, automated steering. No mobile performance claim.");s.Restart();running=false;
        }
    }
}
#endif
