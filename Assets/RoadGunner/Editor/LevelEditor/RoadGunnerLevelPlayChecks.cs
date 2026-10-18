using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace RoadGunner.Editor
{
    public sealed class RoadGunnerLevelPlayChecks
    {
        public bool Completed{get;private set;}public int Failures{get;private set;}
        GameSession s;LevelLayout l;List<string> lines=new List<string>();
        public static RoadGunnerLevelPlayChecks Active {get;private set;}
        IEnumerator routine;float waitUntil;
        public void Begin(GameSession session){s=session;l=Object.FindAnyObjectByType<LevelLayout>();Application.runInBackground=true;Active=this;routine=Run();EditorApplication.update+=Tick;}
        void Tick(){if(!EditorApplication.isPlaying){EditorApplication.update-=Tick;(routine as IDisposable)?.Dispose();return;}if(Time.time<waitUntil)return;
            try{if(!routine.MoveNext()){EditorApplication.update-=Tick;return;}waitUntil=Time.time+(routine.Current is float seconds?seconds:.015f);}
            catch(Exception ex){Check("runner",false,ex.ToString());Completed=true;EditorApplication.update-=Tick;s.vehicle.Body.constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();}}

        void Check(string name,bool pass,string detail){if(!pass)Failures++;var line=(pass?"PASS ":"FAIL ")+name+": "+detail;lines.Add(line);File.WriteAllLines(RoadGunnerLevelEditorChecks.Folder+"/PlayChecks.txt",lines);Debug.Log("RG LEVEL EDITOR "+line);}
        Vector3 G(float x,float z)=>RoadGunnerLevelEditorChecks.Ground(l,x,z);
        void Isolate(){s.Restart();s.health.DamageEnabled=false;s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;Physics.SyncTransforms();}
        IEnumerator Reward(int value){float end=Time.time+7;while(Time.time<end&&s.Scrap<value)yield return null;}
        IEnumerator Run()
        {
            yield return null;s.Restart();s.turret.enabled=false;s.health.DamageEnabled=false;
            var human=s.spawns.authoredRaiders[0];var buggy=s.spawns.authoredBuggies[0];
            Check("10 actual authored spawns and overrides",s.spawns.authoredRaiders.All(r=>r.Alive)&&s.spawns.authoredBuggies.All(b=>b.Alive)&&human.Health==60&&human.agent.speed==2&&buggy.Health==160&&buggy.tuning.maxSpeed==8,"Human health="+human.Health+", speed="+human.agent.speed+"; vehicle health="+buggy.Health+", speed="+buggy.tuning.maxSpeed);
            yield return (float)(.5f);Check("10 player start and grounded physics",Vector2.Distance(new Vector2(s.vehicle.transform.position.x,s.vehicle.transform.position.z),new Vector2(l.playerSpawn.position.x,l.playerSpawn.position.z))<.1f&&s.vehicle.GroundedWheels==4,"Spawn="+s.vehicle.transform.position+", wheels="+s.vehicle.GroundedWheels);
            yield return (float)(.6f);Check("10 enemies use existing AI",human.agent.velocity.magnitude>.1f&&s.spawns.authoredBuggies.Any(b=>b.State!=BuggyState.Idle),"Human velocity="+human.agent.velocity.magnitude+", vehicle="+buggy.State);
            Isolate();human.Spawn(G(-2.5f,0),-1,0);human.transform.rotation=Quaternion.Euler(0,90,0);s.health.DamageEnabled=true;float health=s.health.Current;float timeout=Time.time+2;while(Time.time<timeout&&s.health.Current==health)yield return null;
            Check("4 human override damage used",Mathf.Abs((health-s.health.Current)-12)<.01f,"Actual melee damage="+(health-s.health.Current));
            Isolate();buggy.Spawn(G(0,6),Quaternion.Euler(0,180,0),-1);buggy.AIEnabled=false;buggy.Body.constraints=RigidbodyConstraints.FreezeAll;s.health.DamageEnabled=true;health=s.health.Current;int shots=buggy.gun.ShotsFired;timeout=Time.time+4;while(Time.time<timeout&&s.health.Current==health)yield return null;
            Check("4 vehicle override gun used",health-s.health.Current==7&&buggy.gun.ShotsFired>shots&&buggy.tuning.shotsPerSecond==3,"Actual bullet damage="+(health-s.health.Current)+", shots="+(buggy.gun.ShotsFired-shots)+", configured rate="+buggy.tuning.shotsPerSecond);buggy.Body.constraints=RigidbodyConstraints.None;
            Isolate();human.Spawn(G(0,5),-1,0);human.enabled=false;Physics.SyncTransforms();s.turret.enabled=true;shots=s.turret.ShotsFired;timeout=Time.time+4;while(Time.time<timeout&&human.Alive)yield return null;
            Check("10 existing roof shooting",!human.Alive&&s.turret.ShotsFired-shots==4,"60-health override killed by four existing 15-damage shots");s.turret.enabled=false;var reward=Reward(1);while(reward.MoveNext())yield return reward.Current;
            Check("10 human Metal Gear / harpoon / HUD",s.Scrap==1&&s.pickups.ActiveCount==0&&s.hud.scrapText.text=="001","Existing reward="+s.Scrap);
            Isolate();buggy.Spawn(G(5,0),Quaternion.identity,-1);buggy.AIEnabled=false;buggy.gun.CanShoot=false;buggy.Hit(999,Vector3.right);reward=Reward(5);while(reward.MoveNext())yield return reward.Current;
            Check("10 vehicle override reward / harpoon",s.Scrap==5&&s.pickups.ActiveCount==0&&s.hud.scrapText.text=="005","Existing reward path with overridden amount="+s.Scrap);
            Isolate();s.vehicle.Body.constraints=RigidbodyConstraints.None;var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;var forward=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;s.input.testInput=true;s.input.testDirection=new Vector2(Vector3.Dot(Vector3.forward,right),Vector3.Dot(Vector3.forward,forward));
            yield return (float)(5);s.input.testDirection=Vector2.zero;s.input.testBrake=true;yield return (float)(.5f);
            Check("10 solid obstacle blocks actual car",s.vehicle.transform.position.z>6&&s.vehicle.transform.position.z<14,"Actual driven position="+s.vehicle.transform.position);
            Isolate();s.vehicle.Body.constraints=RigidbodyConstraints.None;s.vehicle.ResetPose(G(17,20)+Vector3.up*.70f,Quaternion.identity);yield return (float)(1.5f);
            Check("10 edited hill collision and navigation",s.vehicle.GroundedWheels>=3&&Mathf.Abs(s.vehicle.transform.position.y-G(17,20).y)<1.5f&&NavMesh.SamplePosition(G(17,20),out var hit,2,NavMesh.AllAreas),"Hill="+G(17,20).y+", wheels="+s.vehicle.GroundedWheels+", car Y="+s.vehicle.transform.position.y);
            Check("completed",Failures==0,"Actual Unity Play Mode workflow verified; no diagnostic component saved in scenes");Completed=true;
            human.enabled=true;s.vehicle.Body.constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();
        }
    }
}
