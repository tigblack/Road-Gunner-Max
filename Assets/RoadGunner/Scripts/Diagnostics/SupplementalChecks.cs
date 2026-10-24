#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace RoadGunner
{
    public sealed class SupplementalChecks : MonoBehaviour
    {
        GameSession s;
        System.Text.StringBuilder log=new System.Text.StringBuilder();
        void Record(string name,bool ok,string observation){string line=(ok?"PASS ":"FAIL ")+name+": "+observation;log.AppendLine(line);Debug.Log("RG CHECK "+line);File.WriteAllText("Documentation/RoadGunner/SupplementalResults.txt",log.ToString());}
        public void Begin(){s=GetComponent<GameSession>();StartCoroutine(Run());}
        IEnumerator Run(){
            s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();yield return new WaitForSeconds(.7f);
            if(s.spawns.buggyPool!=null)foreach(var b in s.spawns.buggyPool)b.Despawn();
            var enemy=s.spawns.pool[0];Vector3 point=s.vehicle.transform.TransformPoint(new Vector3(2,0,0));NavMesh.SamplePosition(point,out NavMeshHit hit,3,NavMesh.AllAreas);enemy.Spawn(hit.position,0,0);
            int events=0;float minimumGap=100,last=-10;System.Action changed=()=>{events++;minimumGap=Mathf.Min(minimumGap,Time.time-last);last=Time.time;};s.health.Changed+=changed;yield return new WaitForSeconds(3.5f);s.health.Changed-=changed;
            Record("melee attack events",events>=2&&events<=4&&minimumGap>.90f,"events in 3.5s="+events+", minimum gap="+minimumGap.ToString("F2")+"s, health="+s.health.Current);
            foreach(var r in s.spawns.pool)r.Despawn();s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();
            if(s.spawns.buggyPool!=null)foreach(var b in s.spawns.buggyPool)b.Despawn();
            // A synthetic touchscreen validates distinct UI pointers, not real-device behavior.
            var screen=InputSystem.AddDevice<Touchscreen>();var joy=s.input.joystick;Vector2 p=RectTransformUtility.WorldToScreenPoint(null,joy.ring.position),pause=RectTransformUtility.WorldToScreenPoint(null,s.hud.pause.transform.position);
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=21,phase=UnityEngine.InputSystem.TouchPhase.Began,position=p});yield return null;yield return null;
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=21,phase=UnityEngine.InputSystem.TouchPhase.Moved,position=p+Vector2.up*70});yield return null;yield return null;bool drag=joy.IsHeld&&s.input.Magnitude>0;
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=22,phase=UnityEngine.InputSystem.TouchPhase.Began,position=pause});yield return null;yield return null;
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=22,phase=UnityEngine.InputSystem.TouchPhase.Ended,position=pause});yield return null;yield return null;bool paused=s.State==SessionState.Paused&&!joy.IsHeld;
            Record("synthetic second-finger Pause",drag&&paused,"first finger drove="+drag+", second finger paused and released input="+paused+"; physical device still unverified");
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=21,phase=UnityEngine.InputSystem.TouchPhase.Ended,position=p+Vector2.up*70});yield return null;s.SetPaused(false);
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=23,phase=UnityEngine.InputSystem.TouchPhase.Began,position=p});yield return null;yield return null;
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=23,phase=UnityEngine.InputSystem.TouchPhase.Moved,position=p+Vector2.up*60});yield return null;yield return null;
            InputSystem.QueueStateEvent(screen,new TouchState{touchId=23,phase=UnityEngine.InputSystem.TouchPhase.Canceled,position=p+Vector2.up*60});yield return null;yield return null;Record("touch cancellation",!joy.IsHeld&&joy.Value==Vector2.zero,"released="+!joy.IsHeld);
            InputSystem.RemoveDevice(screen);s.spawns.enabled=true;s.turret.enabled=true;s.Restart();
            int alive=0,reachable=0;var path=new NavMeshPath();NavMesh.SamplePosition(s.startPosition,out NavMeshHit start,2,NavMesh.AllAreas);foreach(var r in s.spawns.pool)if(r.Alive){alive++;if(NavMesh.CalculatePath(r.transform.position,start.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)reachable++;}
            Record("initial groups on reachable ground",alive==24&&reachable==alive,"alive="+alive+", paths to spawn="+reachable);s.Restart();
        }
    }
}
#endif
