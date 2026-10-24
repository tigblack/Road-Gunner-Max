#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
namespace RoadGunner
{
    public sealed class EnemyBuggyChecks : MonoBehaviour
    {
        GameSession s;EnemyBuggy buggy;bool running;
        readonly List<string> results=new List<string>();
        public void Begin(){if(running)return;running=true;s=GetComponent<GameSession>();buggy=s.spawns.buggyPool[0];StartCoroutine(Run());}
        void Record(string name,bool pass,string observation){string line=(pass?"PASS ":"FAIL ")+name+": "+observation;results.Add(line);Debug.Log("RG BUGGY "+line);Directory.CreateDirectory("Documentation/RoadGunner/Buggy-2026-10-07");File.WriteAllLines("Documentation/RoadGunner/Buggy-2026-10-07/EnemyBuggyChecks.txt",results);}
        Vector3 Ground(float x,float z){NavMesh.SamplePosition(new Vector3(x,1,z),out var hit,4,NavMesh.AllAreas);return hit.position;}
        void Isolate(float x=0,float z=0,float yaw=45){s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();s.health.DamageEnabled=false;s.vehicle.ResetPose(Ground(x,z)+Vector3.up*.7f,Quaternion.Euler(0,yaw,0));s.follow.Snap();Physics.SyncTransforms();}
        void Spawn(float x,float z,bool ai=true){Vector3 point=Ground(x,z);Vector3 to=s.vehicle.transform.position-point;to.y=0;buggy.Spawn(point,Quaternion.LookRotation(to),0);buggy.AIEnabled=ai;if(buggy.gun)buggy.gun.CanShoot=false;Physics.SyncTransforms();}
        IEnumerator Run()
        {
            s.Restart();s.turret.enabled=false;int initial=0;foreach(var b in s.spawns.buggyPool)if(b.Alive)initial++;
            Record("saved pool and shared population cap",initial==3&&s.spawns.ActiveCount==27&&s.spawns.ActiveCount<=s.spawns.tuning.activeCap,"buggies="+initial+", total="+s.spawns.ActiveCount+", cap="+s.spawns.tuning.activeCap);
            Isolate();Spawn(-6,-10,false);yield return new WaitForSeconds(1);
            Record("four wheel suspension",buggy.GroundedWheels==4&&Vector3.Dot(buggy.transform.up,Vector3.up)>.95f,"grounded="+buggy.GroundedWheels+", body="+buggy.transform.position);
            float before=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);buggy.AIEnabled=true;
            yield return new WaitForSeconds(4);
            float after=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);
            Record("physical approach stops at firing distance",before-after>3&&after>5.5f&&after<9&&Vector3.Dot(buggy.transform.up,Vector3.up)>.7f,"distance "+before.ToString("F1")+" -> "+after.ToString("F1")+", state="+buggy.State);
            Isolate();s.health.DamageEnabled=true;Spawn(-6,-10);
            yield return new WaitForSeconds(6);
            Record("holds distance without collision attacks",s.health.Current==100&&Vector3.Distance(buggy.transform.position,s.vehicle.transform.position)>5.5f&&buggy.State==BuggyState.Firing,"state="+buggy.State+", player health="+s.health.Current);
            Isolate(2,10,90);Spawn(-22,7);
            float start=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);
            yield return new WaitForSeconds(12);
            float end=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);
            Record("navigation around existing rock",end<10&&start-end>12&&Vector3.Dot(buggy.transform.up,Vector3.up)>.7f,"distance "+start.ToString("F1")+" -> "+end.ToString("F1")+", position="+buggy.transform.position);
            Isolate();Spawn(7,7,false);yield return new WaitForSeconds(.8f);s.turret.enabled=true;
            float original=buggy.Health;Vector3 ray=s.turret.muzzle.position,target=buggy.AimPoint;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Temporary buggy occlusion check";wall.transform.position=(ray+target)*.5f;wall.transform.localScale=new Vector3(3,4,1);wall.transform.rotation=Quaternion.LookRotation(target-ray);Physics.SyncTransforms();
            yield return new WaitForSeconds(1);bool blocked=buggy.Health==original;Destroy(wall);yield return new WaitForSeconds(2);
            Record("automatic turret and obstruction",blocked&&buggy.Health<original&&s.turret.ShotsFired>0,"protected="+blocked+", health="+buggy.Health+", shots="+s.turret.ShotsFired);
            Isolate();Spawn(5,5,false);int kills=s.Kills;buggy.Hit(999,Vector3.forward);buggy.Hit(999,Vector3.forward);
            Record("one death reward and wreck",s.Kills==kills+1&&s.pickups.StoredValue==3&&!buggy.Alive&&buggy.State==BuggyState.Wrecked,"kill delta="+(s.Kills-kills)+", scrap value="+s.pickups.StoredValue);
            yield return new WaitForSeconds(buggy.tuning.wreckLifetime+.2f);
            Record("wreck returns to bounded pool",!buggy.gameObject.activeSelf,"active="+buggy.gameObject.activeSelf);
            Isolate(0,0,0);Spawn(0,8,false);s.input.testInput=true;s.input.testDirection=new Vector2(-.707107f,.707107f)*.8f;float hp=buggy.Health;
            yield return new WaitForSeconds(3);
            Record("player front ram damages buggy",buggy.Health<hp,"buggy health "+hp+" -> "+buggy.Health+", player speed="+s.vehicle.ForwardSpeed.ToString("F1"));
            s.Restart();foreach(var r in s.spawns.pool)r.enabled=false;foreach(var b in s.spawns.buggyPool)b.AIEnabled=false;s.spawns.enabled=true;s.turret.enabled=false;
            float delay=buggy.tuning.respawnDelay;buggy.tuning.respawnDelay=.8f;yield return new WaitForSeconds(.7f);
            foreach(var b in s.spawns.buggyPool)if(b.SpawnSlot==0||b.SpawnSlot==1)b.Hit(999,Vector3.forward);
            yield return new WaitForSeconds(3.8f);
            int near=0,far=0;foreach(var b in s.spawns.buggyPool)if(b.Alive){if(b.SpawnSlot==0)near++;if(b.SpawnSlot==1)far++;}
            Record("offscreen respawn and near-player guard",near==0&&far==1&&s.spawns.ActiveCount<=30,"near slot="+near+", distant slot="+far+", total="+s.spawns.ActiveCount);
            buggy.tuning.respawnDelay=delay;foreach(var r in s.spawns.pool)r.enabled=true;
            s.Restart();s.spawns.enabled=true;s.turret.enabled=true;s.SetPaused(true);Vector3 paused=s.spawns.buggyPool[0].transform.position;
            yield return new WaitForSecondsRealtime(.25f);bool frozen=Vector3.Distance(paused,s.spawns.buggyPool[0].transform.position)<.001f;s.SetPaused(false);s.Restart();
            int alive=0;bool reset=true;foreach(var b in s.spawns.buggyPool)if(b.Alive){alive++;reset&=b.Health==b.tuning.health&&b.gun.CanShoot;}
            Record("pause and complete restart",frozen&&alive==3&&reset&&s.Kills==0&&s.Scrap==0,"frozen="+frozen+", alive="+alive+", reset="+reset);
            Record("completed",true,"Normal gameplay restored. Actual Unity physics and combat; no mobile-device performance claim.");running=false;
        }
    }
}
#endif
