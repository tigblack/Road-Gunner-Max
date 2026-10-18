#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
namespace RoadGunner
{
    public sealed class RangedDeathLootChecks:MonoBehaviour
    {
        public const string ReportPath="Documentation/RoadGunner/Ranged-Loot-2026-10-07/RangedDeathLootChecks.txt";
        GameSession s;EnemyBuggy buggy;AttackRangeIndicator ground;bool running;RigidbodyConstraints constraints;
        readonly List<string> results=new List<string>();
        public void Begin(){if(running)return;running=true;s=GetComponent<GameSession>();buggy=s.spawns.buggyPool[0];ground=s.vehicle.GetComponent<AttackRangeIndicator>();constraints=s.vehicle.Body.constraints;StartCoroutine(Run());}
        void Record(string name,bool pass,string detail){string line=(pass?"PASS ":"FAIL ")+name+": "+detail;results.Add(line);Debug.Log("RG TACTICS "+line);Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));File.WriteAllLines(ReportPath,results);}
        Vector3 Point(float x,float z)=>new Vector3(x,ground.GroundHeight(x,z),z);
        void Isolate(){s.vehicle.Body.constraints=constraints;s.Restart();s.spawns.enabled=false;s.turret.enabled=false;foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();s.vehicle.ResetPose(Point(0,0)+Vector3.up*.7f,Quaternion.Euler(0,45,0));s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;s.health.DamageEnabled=false;s.follow.Snap();Physics.SyncTransforms();}
        void Spawn(float x,float z,bool ai=true){var p=Point(x,z);buggy.Spawn(p,Quaternion.LookRotation(Vector3.ProjectOnPlane(s.vehicle.transform.position-p,Vector3.up)),0);buggy.AIEnabled=ai;buggy.gun.CanShoot=false;Physics.SyncTransforms();}
        Raider RaiderAt(float x,float z){var r=s.spawns.pool[0];NavMesh.SamplePosition(Point(x,z),out var hit,2,NavMesh.AllAreas);r.Spawn(hit.position,0,0);r.enabled=false;return r;}
        Transform ActivePickup(){foreach(var p in s.pickups.items)if(p.gameObject.activeSelf)return p;return null;}
        IEnumerator Run()
        {
            Isolate();bool views=s.pickups.gearVisuals.Length==60&&s.pickups.scrapVisuals.Length==60;foreach(var t in s.pickups.gearVisuals)views&=t&&t.GetComponent<MeshFilter>().sharedMesh.subMeshCount==2;
            Record("saved gear and explosion pools",views&&s.effects.explosions.Length==3&&s.effects.explosion,"gear views="+s.pickups.gearVisuals.Length+", explosion slots="+s.effects.explosions.Length);
            Spawn(-6,-12);float min=100;float began=Time.time;
            while(Time.time-began<6){min=Mathf.Min(min,Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position,s.vehicle.transform.position)));yield return new WaitForFixedUpdate();}
            float distance=Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position,s.vehicle.transform.position));
            Record("approach stops before player",distance>5.5f&&distance<8.5f&&min>5.5f&&buggy.Body.linearVelocity.magnitude<.8f&&buggy.State==BuggyState.Firing,"distance="+distance+", min="+min+", speed="+buggy.Body.linearVelocity.magnitude+", state="+buggy.State);
            buggy.gun.CanShoot=true;s.health.DamageEnabled=true;int shots=buggy.gun.ShotsFired;float hp=s.health.Current;yield return new WaitForSeconds(1.8f);
            Record("standing ranged gun damages player",buggy.gun.ShotsFired>shots&&s.health.Current<hp&&Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position,s.vehicle.transform.position))>5.5f,"shots="+(buggy.gun.ShotsFired-shots)+", hp="+s.health.Current);
            Vector3 towards=(s.vehicle.transform.position-buggy.transform.position);towards.y=0;towards.Normalize();var near=buggy.transform.position+towards*4.5f;
            s.vehicle.ResetPose(Point(near.x,near.z)+Vector3.up*.7f,Quaternion.Euler(0,45,0));Physics.SyncTransforms();began=Time.time;min=100;
            while(Time.time-began<3.5f){min=Mathf.Min(min,Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position,s.vehicle.transform.position)));yield return new WaitForFixedUpdate();}
            distance=Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position,s.vehicle.transform.position));
            Record("retreat restores firing gap",distance>6.5f&&distance<9&&min>4&&buggy.State==BuggyState.Firing,"distance="+distance+", min="+min+", state="+buggy.State);
            Isolate();s.health.DamageEnabled=true;Spawn(0,-4.7f,false);buggy.Body.rotation=Quaternion.identity;buggy.transform.rotation=Quaternion.identity;s.vehicle.ResetPose(Point(0,0)+Vector3.up*.7f,Quaternion.identity);
            var contact=buggy.gameObject.AddComponent<BuggyContactProbe>();buggy.Body.linearVelocity=Vector3.forward*9;Physics.SyncTransforms();yield return new WaitForSeconds(1);
            Record("actual car collision causes no enemy ram damage",contact.Contacts>0&&s.health.Current==100,"contact callbacks="+contact.Contacts+", hp="+s.health.Current);Destroy(contact);

            Isolate();Spawn(5,5,false);int explosions=s.effects.ExplosionsPlayed,kills=s.Kills;buggy.Hit(999,Vector3.forward);buggy.Hit(999,Vector3.forward);
            int particles=0;foreach(var e in s.effects.explosions)particles+=e.ParticleCount;
            Record("vehicle death explodes exactly once",s.effects.ExplosionsPlayed==explosions+1&&s.Kills==kills+1&&s.pickups.StoredValue==3&&particles>=80&&s.health.Current==100,"explosions="+(s.effects.ExplosionsPlayed-explosions)+", particles="+particles+", scrap="+s.pickups.StoredValue);
            yield return new WaitForSeconds(2.7f);bool cleared=true;foreach(var e in s.effects.explosions)cleared&=!e.IsPlaying&&e.ParticleCount==0;
            Record("explosion and wreck clean up",cleared&&!buggy.gameObject.activeSelf,"effects empty="+cleared+", wreck active="+buggy.gameObject.activeSelf);
            Isolate();explosions=s.effects.ExplosionsPlayed;for(int i=0;i<3;i++){var b=s.spawns.buggyPool[i];b.Spawn(Point(5+i*2,5),Quaternion.identity,i);b.AIEnabled=false;b.gun.CanShoot=false;b.Hit(999,Vector3.forward);}
            int live=0;particles=0;foreach(var e in s.effects.explosions){if(e.IsPlaying)live++;particles+=e.ParticleCount;}
            Record("three simultaneous bounded explosions",live==3&&s.effects.ExplosionsPlayed==explosions+3&&s.pickups.StoredValue==9&&particles<=396,"live="+live+", particles="+particles+", scrap="+s.pickups.StoredValue);

            Isolate();var raider=RaiderAt(6,6);kills=s.Kills;raider.Hit(999,Vector3.right);raider.Hit(999,Vector3.right);var gear=ActivePickup();Vector3 origin=gear.position;
            Record("raider ejects one visible metal gear",s.Kills==kills+1&&s.pickups.StoredValue==1&&s.pickups.ActiveCount==1&&s.pickups.FlyingGearCount==1&&gear.Find("Metal gear").gameObject.activeSelf&&!gear.Find("Scrap plates").gameObject.activeSelf,"kills="+(s.Kills-kills)+", value="+s.pickups.StoredValue+", flying="+s.pickups.FlyingGearCount);
            yield return new WaitForSeconds(.25f);float lift=gear.position.y-origin.y;
            Record("gear launch has a visible arc",lift>.2f&&AttackRange.SquaredDistance(gear.position,origin)>.05f,"rise="+lift+", horizontal travel="+Mathf.Sqrt(AttackRange.SquaredDistance(gear.position,origin)));
            yield return new WaitForSeconds(1);bool waiting=gear.gameObject.activeSelf&&s.pickups.FlyingGearCount==0&&s.pickups.StoredValue==1;
            Record("landed gear persists for collection",waiting&&gear.GetComponent<Collider>()==null&&gear.GetComponent<Rigidbody>()==null,"waiting="+waiting+", pickup objects="+s.pickups.ActiveCount);
            s.vehicle.ResetPose(Point(gear.position.x,gear.position.z)+Vector3.up*.7f,Quaternion.Euler(0,45,0));Physics.SyncTransforms();yield return new WaitForSeconds(.2f);int scrap=s.Scrap;yield return new WaitForSeconds(.25f);
            Record("driving onto gear awards resource once",scrap==1&&s.Scrap==1&&s.pickups.ActiveCount==0&&s.pickups.StoredValue==0,"scrap="+s.Scrap+", active="+s.pickups.ActiveCount);
            raider.enabled=true;
            Isolate();raider=RaiderAt(.5f,.5f);raider.Hit(999,Vector3.zero);yield return new WaitForSeconds(.2f);bool visibleFlight=s.pickups.FlyingGearCount==1&&s.Scrap==0;
            yield return new WaitForSeconds(.7f);
            Record("nearby death keeps flight visible before pickup",visibleFlight&&s.Scrap==1&&s.pickups.ActiveCount==0,"visible flight="+visibleFlight+", scrap="+s.Scrap);raider.enabled=true;
            Isolate();raider=RaiderAt(6,6);raider.Hit(999,Vector3.forward);yield return new WaitForSeconds(.2f);gear=ActivePickup();var paused=gear.position;s.SetPaused(true);yield return new WaitForSecondsRealtime(.25f);bool frozen=(gear.position-paused).sqrMagnitude<.000001f;s.SetPaused(false);yield return new WaitForSeconds(.8f);
            Record("pause freezes airborne gear and resumes landing",frozen&&s.pickups.FlyingGearCount==0&&s.pickups.ActiveCount==1,"frozen="+frozen+", landed="+(s.pickups.FlyingGearCount==0));raider.enabled=true;
            Isolate();for(int i=0;i<85;i++)s.pickups.DropGear(Point(30,30)+Vector3.up,Vector3.right,1);
            Record("gear cap preserves overflow reward",s.pickups.ActiveCount==60&&s.pickups.StoredValue==85,"objects="+s.pickups.ActiveCount+", stored="+s.pickups.StoredValue);
            s.vehicle.Body.constraints=constraints;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();yield return null;cleared=s.pickups.ActiveCount==0&&s.pickups.FlyingGearCount==0&&s.Scrap==0;foreach(var e in s.effects.explosions)cleared&=!e.IsPlaying&&e.ParticleCount==0;
            Record("restart clears flying loot and explosion pool",cleared,"cleared="+cleared+", radius="+s.turret.Range);
            Record("completed",true,"Normal gameplay restored. Actual Unity physics, projectile damage, particle bursts and gear collection.");running=false;
        }
    }
    public sealed class BuggyContactProbe:MonoBehaviour
    {
        public int Contacts {get;private set;}
        void OnCollisionEnter(Collision collision){if(collision.rigidbody&&collision.rigidbody.GetComponent<VehicleController>())Contacts++;}
        void OnCollisionStay(Collision collision){if(collision.rigidbody&&collision.rigidbody.GetComponent<VehicleController>())Contacts++;}
    }
}
#endif
