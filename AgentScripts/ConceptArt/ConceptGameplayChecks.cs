using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using RoadGunner;
using Object=UnityEngine.Object;

public static class ConceptGameplayChecks
{
    const string Report="/Users/arthuri/Documents/Codex/2026-10-09/macbook-app-store/outputs/concept-overhaul-2026-10-16/GameplayChecks.txt";
    static GameSession s;static LevelLayout level;static HarpoonCollector harpoon;
    static readonly Quaternion Axis=Quaternion.Euler(0,45,0);
    static readonly List<string> Lines=new List<string>();static int failures;
    static float began;
    static void Save(){Directory.CreateDirectory(Path.GetDirectoryName(Report));File.WriteAllText(Report,"Actual Unity Play Mode / Concept Wasteland\nFailures="+failures+"; elapsed="+(Time.realtimeSinceStartup-began).ToString("F1")+"s\n"+string.Join("\n",Lines));}
    static void Check(string name,bool pass,string detail)
    {if(!pass)failures++;Lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);Save();Debug.Log("CONCEPT CHECK "+Lines.Last());}
    static async Task Wait(float seconds)
    {float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await Task.Delay(25);}
    static async Task Until(Func<bool> predicate,float seconds,Action observe=null)
    {float end=Time.realtimeSinceStartup+seconds;while(!predicate()&&Time.realtimeSinceStartup<end){observe?.Invoke();await Task.Delay(25);}observe?.Invoke();}
    static Vector3 W(float x,float z,float y=.7f)=>Axis*new Vector3(x*2.5f,y,z*2.5f);
    static Vector3 Ground(Vector3 position)
    {if(!NavMesh.SamplePosition(position,out var hit,3,NavMesh.AllAreas))throw new InvalidOperationException("No existing NavMesh near "+position);return hit.position;}
    static void DriveToward(Vector3 delta,float throttle)
    {
        var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;
        var forward=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;
        delta=Vector3.ProjectOnPlane(delta,Vector3.up).normalized;
        s.input.testDirection=new Vector2(Vector3.Dot(delta,right),Vector3.Dot(delta,forward))*throttle;
        s.input.testBrake=false;
    }
    static void Isolate(Vector3 position,Quaternion rotation,bool freeze=false)
    {
        Time.timeScale=1;foreach(var r in s.spawns.pool)if(r)r.enabled=true;
        foreach(var b in s.spawns.buggyPool)if(b){b.enabled=true;b.AIEnabled=true;if(b.gun){b.gun.enabled=true;b.gun.CanShoot=true;}}
        s.vehicle.Body.constraints=RigidbodyConstraints.None;harpoon.enabled=true;s.Restart();
        s.spawns.enabled=false;s.turret.enabled=false;s.health.DamageEnabled=false;
        foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();
        s.input.testInput=true;s.input.testDirection=Vector2.zero;s.input.testBrake=true;
        s.vehicle.ResetPose(position,rotation);s.vehicle.Body.constraints=freeze?RigidbodyConstraints.FreezeAll:RigidbodyConstraints.None;
        s.follow.Snap();Physics.SyncTransforms();
    }
    static bool MaterialsOkay(GameObject root)
    {
        var renderers=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled).ToArray();
        return renderers.Length>0&&renderers.All(r=>r.sharedMaterials.Length>0&&r.sharedMaterials.All(m=>m&&m.shader&&!m.shader.name.Contains("Error"))&&r.GetComponent<MeshFilter>()&&r.GetComponent<MeshFilter>().sharedMesh);
    }
    static bool RaiderVisual(Raider r)=>r&&r.visual&&r.visual.name.Contains("Concept")&&r.torso&&r.leftLeg&&r.rightLeg&&r.leftArm&&r.rightArm&&MaterialsOkay(r.visual.gameObject);
    static bool BuggyVisual(EnemyBuggy b)=>b&&b.armor!=null&&b.armor.Length>0&&b.armor.All(r=>r)&&b.transform.Find("Replaceable buggy visuals/Concept armored scout body")&&b.wheelVisuals.Length==4&&b.wheelVisuals.All(w=>w&&MaterialsOkay(w.gameObject));
    static float WheelPositionError(VehicleController car)
    {float error=0;for(int i=0;i<4;i++){car.wheels[i].GetWorldPose(out var p,out _);error=Mathf.Max(error,Vector3.Distance(p,car.wheelVisuals[i].position));}return error;}
    static async Task DriveRoute(string name,Vector3 start,Quaternion rotation,Vector3[] route,float timeout)
    {
        Isolate(start,rotation);await Wait(.9f);
        Check(name+" initial suspension",s.vehicle.GroundedWheels==4,"grounded="+s.vehicle.GroundedWheels);
        int at=0,minGround=4;float maxSpeed=0,maxSteer=0,minUp=1,maxWheelError=0;bool dustObserved=false;
        Vector3 initial=s.vehicle.transform.position;float end=Time.realtimeSinceStartup+timeout;
        while(at<route.Length&&Time.realtimeSinceStartup<end)
        {
            var delta=Vector3.ProjectOnPlane(route[at]-s.vehicle.transform.position,Vector3.up);
            if(delta.magnitude<2.5f){at++;continue;}
            DriveToward(delta,Mathf.Lerp(.39f,.68f,Mathf.InverseLerp(3,13,delta.magnitude)));
            maxSpeed=Mathf.Max(maxSpeed,s.vehicle.Speed);maxSteer=Mathf.Max(maxSteer,Mathf.Abs(s.vehicle.wheels[0].steerAngle));
            minGround=Math.Min(minGround,s.vehicle.GroundedWheels);minUp=Mathf.Min(minUp,Vector3.Dot(s.vehicle.transform.up,Vector3.up));maxWheelError=Mathf.Max(maxWheelError,WheelPositionError(s.vehicle));
            if(s.vehicle.dust!=null)dustObserved|=s.vehicle.dust.Any(p=>p&&p.particleCount>0);
            await Task.Delay(25);
        }
        s.input.testDirection=Vector2.zero;s.input.testBrake=true;await Wait(.5f);
        float moved=Vector3.Distance(initial,s.vehicle.transform.position);
        Check(name+" actual input, steering and suspension",at==route.Length&&moved>15&&maxSpeed>2&&maxSteer>3&&minGround>=3&&minUp>.90f,
            "waypoints="+at+"/"+route.Length+", movement="+moved.ToString("F1")+"m, speed="+maxSpeed.ToString("F1")+", steer="+maxSteer.ToString("F1")+", min wheels="+minGround+", upright="+minUp.ToString("F3"));
        Check(name+" wheel visual alignment / dust",maxWheelError<.28f&&dustObserved,"max animated-center lag="+maxWheelError.ToString("F3")+"m, moving-wheel dust="+dustObserved);
    }

    public static async Task<string> Main()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Run ConceptGameplayChecks in Play Mode.");
        began=Time.realtimeSinceStartup;Lines.Clear();failures=0;level=Object.FindAnyObjectByType<LevelLayout>();s=level?level.session:null;
        if(!s)throw new InvalidOperationException("Active Road Gunner gameplay scene/session missing.");
        harpoon=s.vehicle.GetComponent<HarpoonCollector>();var workshop=Object.FindAnyObjectByType<RepairWorkshop>();
        if(!harpoon||!workshop)throw new InvalidOperationException("Existing harpoon or repair workshop missing.");
        GameObject temporaryOverlap=null,temporaryCollider=null;Application.runInBackground=true;Save();
        try
        {
            s.vehicle.Body.constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.turret.enabled=false;s.Restart();s.health.DamageEnabled=false;await Wait(1.0f);
            Check("player references and visual physics separation",s.vehicle.wheels.Length==4&&s.vehicle.wheelVisuals.Length==4&&s.vehicle.wheels.All(w=>w)&&s.vehicle.wheelVisuals.All(w=>w)&&s.turret.yaw&&s.turret.pitch&&s.turret.muzzle&&harpoon.pivot&&harpoon.spawnPoint&&harpoon.collectionPoint&&harpoon.projectile&&harpoon.cable&&s.vehicle.GetComponentsInChildren<Rigidbody>(true).Length==1&&s.vehicle.transform.Find("Replaceable visuals").GetComponentsInChildren<Collider>(true).Length==0,
                "4 collider/visual pairs; independent gun/harpoon; one Rigidbody; no visual colliders");
            Check("all saved and pooled art references",MaterialsOkay(s.vehicle.gameObject)&&s.spawns.pool.All(RaiderVisual)&&s.spawns.buggyPool.All(BuggyVisual)&&s.pickups.gearVisuals.All(v=>v&&MaterialsOkay(v.gameObject))&&s.pickups.scrapVisuals.All(v=>v&&MaterialsOkay(v.gameObject)),
                "raiders="+s.spawns.pool.Length+", buggy reserve="+s.spawns.buggyPool.Length+", pickups="+s.pickups.items.Length);
            Check("authored population and cap",s.spawns.authoredRaiders.All(r=>r.Alive&&r.agent.enabled&&r.agent.isOnNavMesh)&&s.spawns.ActiveCount<=s.spawns.tuning.activeCap,
                "authored="+s.spawns.authoredRaiders.Count(r=>r.Alive)+"/"+s.spawns.authoredRaiders.Length+", cap="+s.spawns.tuning.activeCap);

            await DriveRoute("expanded main route",W(0,-48),Axis,new[]{W(0,-38),W(2,-30),W(0,-24),W(0,-18)},28);
            var bay=workshop.transform.position;
            await DriveRoute("service bay entry, clearance and exit turn",bay+Axis*new Vector3(0,.7f,-14),Axis,
                new[]{bay+Axis*new Vector3(0,0,-3),bay+Axis*new Vector3(0,0,11),bay+Axis*new Vector3(-12,0,13)},24);

            // Exercise each authored silhouette through the existing NavMesh and procedural limb animator.
            Isolate(Ground(W(0,-40,0))+Vector3.up*.7f,Axis,true);
            var variants=s.spawns.pool.Where(RaiderVisual).GroupBy(r=>r.visual.name).Select(g=>g.First()).ToArray();
            Check("three distinct raider rigs",variants.Length>=3,"rigs="+string.Join(", ",variants.Select(r=>r.visual.name)));
            foreach(var r in variants.Take(3))
            {
                var point=Ground(s.vehicle.transform.position+Axis*new Vector3(0,-.7f,9));r.enabled=true;r.Spawn(point,-1,1);
                float distance=Vector3.Distance(r.transform.position,s.vehicle.transform.position),swing=0;bool onMesh=true;
                float end=Time.realtimeSinceStartup+3.4f;
                while(Time.realtimeSinceStartup<end){swing=Mathf.Max(swing,Quaternion.Angle(r.leftLeg.localRotation,Quaternion.identity));onMesh&=r.agent.enabled&&r.agent.isOnNavMesh;await Task.Delay(25);}
                float final=Vector3.Distance(r.transform.position,s.vehicle.transform.position);
                Check("raider navigation / limb animation "+r.visual.name,onMesh&&distance-final>1.2f&&swing>4&&RaiderVisual(r),"approach="+(distance-final).ToString("F2")+"m, leg swing="+swing.ToString("F1")+"deg");r.Despawn();
            }

            // The actual roof gun aims, raycasts, damages and kills. The resulting gear uses the real harpoon/HUD.
            Isolate(Ground(W(0,-40,0))+Vector3.up*.7f,Axis,true);var target=s.spawns.pool.First(r=>r.tuning.enemyHealth<100);
            target.Spawn(Ground(s.vehicle.transform.position+Axis*new Vector3(0,-.7f,6)),-1,0);target.enabled=false;
            int shots=s.turret.ShotsFired,kills=s.Kills,collections=harpoon.CollectionsCompleted;float health=target.Health,minAim=180;
            bool damaged=false;s.turret.enabled=true;
            await Until(()=>!target.Alive,7,()=>{damaged|=target.Health<health;minAim=Mathf.Min(minAim,Vector3.Angle(s.turret.muzzle.forward,target.AimPoint-s.turret.muzzle.position));});
            s.turret.enabled=false;
            Check("automatic roof gun aiming, damage and kill",!target.Alive&&damaged&&s.turret.ShotsFired>shots&&s.Kills==kills+1&&minAim<=s.turret.tuning.aimTolerance+1,
                "shots="+(s.turret.ShotsFired-shots)+", health="+target.Health+", aim error="+minAim.ToString("F2")+", kills="+(s.Kills-kills));
            Check("human death drops one real gear",s.pickups.MetalGearCount==1&&s.pickups.StoredValue==1,"gears="+s.pickups.MetalGearCount+", stored reward="+s.pickups.StoredValue);
            bool launch=false,retract=false;int reservations=0;float cableError=0;
            await Until(()=>s.Scrap>=1,8,()=>{launch|=harpoon.State==HarpoonState.Launching;retract|=harpoon.State==HarpoonState.Retracting;reservations=Math.Max(reservations,s.pickups.ReservedGearCount);if(harpoon.cable.enabled)cableError=Mathf.Max(cableError,Vector3.Distance(harpoon.cable.GetPosition(0),harpoon.spawnPoint.position),Vector3.Distance(harpoon.cable.GetPosition(1),harpoon.projectile.position));});
            Check("companion launches, retrieves and updates existing HUD",s.Scrap==1&&s.pickups.ActiveCount==0&&harpoon.CollectionsCompleted==collections+1&&launch&&retract&&reservations==1&&cableError<.03f&&s.hud.scrapText.text=="001",
                "scrap="+s.Scrap+", launched="+launch+", retracted="+retract+", cable error="+cableError.ToString("F4")+", HUD="+s.hud.scrapText.text);target.enabled=true;

            // Spawn a reserve buggy with its normal components; prove suspension, approach and hostile gun.
            Isolate(Ground(W(0,-40,0))+Vector3.up*.7f,Axis,true);var buggy=s.spawns.buggyPool[0];
            buggy.Spawn(Ground(s.vehicle.transform.position+Axis*new Vector3(0,-.7f,14)),Quaternion.LookRotation(-(Axis*Vector3.forward)),0);
            buggy.AIEnabled=false;buggy.gun.CanShoot=false;await Wait(1.25f);
            Check("reserve buggy spawn and suspension",buggy.Alive&&buggy.GroundedWheels==4&&BuggyVisual(buggy),"grounded="+buggy.GroundedWheels+", HP="+buggy.Health);
            float before=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);buggy.AIEnabled=true;await Wait(4);
            float after=Vector3.Distance(buggy.transform.position,s.vehicle.transform.position);
            Check("buggy physical AI approach",before-after>2&&Vector3.Dot(buggy.transform.up,Vector3.up)>.8f,"distance="+before.ToString("F1")+"→"+after.ToString("F1")+", state="+buggy.State);
            buggy.AIEnabled=false;buggy.gun.CanShoot=true;s.health.DamageEnabled=true;
            int enemyShots=buggy.gun.ShotsFired,hits=buggy.gun.PlayerHits;float hp=s.health.Current;
            await Until(()=>buggy.gun.PlayerHits>=hits+2,7);buggy.gun.CanShoot=false;
            Check("enemy mounted gun aims and damages player",buggy.gun.ShotsFired>enemyShots&&buggy.gun.PlayerHits>hits&&s.health.Current<hp&&s.health.Current>0,
                "shots="+(buggy.gun.ShotsFired-enemyShots)+", hits="+(buggy.gun.PlayerHits-hits)+", player HP="+s.health.Current);
            s.health.DamageEnabled=false;int oldKills=s.Kills,explosions=s.effects.ExplosionsPlayed;Vector3 wreck=buggy.transform.position;
            buggy.Hit(99999,Axis*Vector3.forward);buggy.Hit(99999,Axis*Vector3.forward);
            Check("buggy destruction and one reward",!buggy.Alive&&buggy.State==BuggyState.Wrecked&&s.Kills==oldKills+1&&s.pickups.StoredValue==buggy.tuning.scrapReward&&s.effects.ExplosionsPlayed==explosions+1,
                "kills="+(s.Kills-oldKills)+", stored="+s.pickups.StoredValue+", explosions="+(s.effects.ExplosionsPlayed-explosions));
            s.vehicle.ResetPose(wreck-Axis*Vector3.forward*4+Vector3.up*.1f,Axis);Physics.SyncTransforms();
            await Until(()=>s.Scrap>=buggy.tuning.scrapReward&&!buggy.gameObject.activeSelf,6);
            Check("buggy reward and wreck pool lifecycle",s.Scrap==buggy.tuning.scrapReward&&s.pickups.ActiveCount==0&&!buggy.gameObject.activeSelf,
                "scrap="+s.Scrap+", wreck returned="+!buggy.gameObject.activeSelf);

            // Existing repair mechanics: configured rate, effects, radius, pause, full health and death.
            Isolate(bay+Vector3.up*.7f,Axis,true);s.health.DamageEnabled=true;s.health.Damage(s.health.tuning.vehicleHealth*.65f,false);await Wait(.12f);
            float startHP=s.health.Current,startTime=Time.time;await Wait(1.8f);
            float actual=s.health.Current-startHP,expected=s.health.tuning.vehicleHealth*workshop.maximumHealthFractionPerSecond*(Time.time-startTime);
            Check("workshop configured automatic healing rate",Mathf.Abs(actual-expected)<1.2f&&workshop.IsRepairing,"healed="+actual.ToString("F3")+", expected="+expected.ToString("F3")+", radius="+workshop.healingRadius+", fraction/s="+workshop.maximumHealthFractionPerSecond);
            Check("repair feedback while recovering",workshop.activeRepairEffect&&workshop.activeRepairEffect.activeSelf&&workshop.repairingLabel&&workshop.repairingLabel.gameObject.activeSelf,"effect and existing repairing HUD active");
            temporaryCollider=new GameObject("Temporary concept verification colliders");temporaryCollider.transform.SetParent(s.vehicle.transform,false);temporaryCollider.AddComponent<BoxCollider>().isTrigger=true;temporaryCollider.AddComponent<SphereCollider>().isTrigger=true;
            temporaryOverlap=new GameObject("Temporary concept verification overlapping workshop");temporaryOverlap.transform.position=bay;var duplicate=temporaryOverlap.AddComponent<RepairWorkshop>();duplicate.session=s;duplicate.healingRadius=workshop.healingRadius;duplicate.maximumHealthFractionPerSecond=workshop.maximumHealthFractionPerSecond;
            startHP=s.health.Current;startTime=Time.time;await Wait(1.3f);actual=s.health.Current-startHP;expected=s.health.tuning.vehicleHealth*workshop.maximumHealthFractionPerSecond*(Time.time-startTime);
            Check("multiple colliders / zones do not multiply repair",Mathf.Abs(actual-expected)<1.2f,"healed="+actual.ToString("F3")+", single-rate expected="+expected.ToString("F3"));Object.Destroy(temporaryOverlap);Object.Destroy(temporaryCollider);await Wait(.1f);
            s.SetPaused(true);float paused=s.health.Current;await Wait(.3f);
            Check("pause stops healing and feedback",s.health.Current==paused&&!workshop.IsRepairing&&!workshop.activeRepairEffect.activeSelf&&!workshop.repairingLabel.gameObject.activeSelf,"HP="+paused);s.SetPaused(false);await Wait(.2f);
            Check("resume restarts repair",s.health.Current>paused&&workshop.IsRepairing,"HP="+s.health.Current);
            s.vehicle.ResetPose(bay+Axis*new Vector3(workshop.healingRadius+1,.7f,0),Axis);await Wait(.1f);float outside=s.health.Current;await Wait(.35f);
            Check("outside service radius stops repair",s.health.Current==outside&&!workshop.IsRepairing&&!workshop.activeRepairEffect.activeSelf&&!workshop.repairingLabel.gameObject.activeSelf,"HP="+outside);
            s.vehicle.ResetPose(bay+Vector3.up*.7f,Axis);s.health.ResetHealth();s.health.Damage(1,false);await Wait(.4f);
            Check("max HP clamps and stops active repair",s.health.Current==s.health.tuning.vehicleHealth&&!workshop.IsRepairing&&!workshop.activeRepairEffect.activeSelf&&!workshop.repairingLabel.gameObject.activeSelf,"HP="+s.health.Current);
            s.health.Damage(99999,false);await Wait(.3f);
            Check("damage, defeat and no workshop revival",s.health.Current==0&&s.State==RoadGunner.SessionState.Defeated&&!workshop.IsRepairing&&!workshop.activeRepairEffect.activeSelf,"HP="+s.health.Current+", state="+s.State);

            s.vehicle.Body.constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.turret.enabled=false;s.Restart();await Wait(.6f);
            Check("restart restores gameplay and updated pool visuals",s.Playing&&s.health.Current==s.health.tuning.vehicleHealth&&s.Scrap==0&&s.Kills==0&&s.pickups.ActiveCount==0&&s.pickups.ReservedGearCount==0&&harpoon.State==HarpoonState.Idle&&!harpoon.cable.enabled&&!harpoon.projectile.gameObject.activeSelf&&s.spawns.authoredRaiders.All(r=>r.Alive&&RaiderVisual(r))&&s.spawns.buggyPool.All(BuggyVisual)&&MaterialsOkay(s.vehicle.gameObject),
                "state="+s.State+", authored="+s.spawns.authoredRaiders.Count(r=>r.Alive)+", pickups="+s.pickups.ActiveCount+", harpoon="+harpoon.State);
        }
        catch(Exception e){Check("verification exception",false,e.ToString());}
        finally
        {
            if(temporaryOverlap)Object.Destroy(temporaryOverlap);if(temporaryCollider)Object.Destroy(temporaryCollider);
            Time.timeScale=1;s.vehicle.Body.constraints=RigidbodyConstraints.None;
            foreach(var r in s.spawns.pool)if(r)r.enabled=true;
            foreach(var b in s.spawns.buggyPool)if(b){b.enabled=true;b.AIEnabled=true;if(b.gun){b.gun.enabled=true;b.gun.CanShoot=true;}}
            harpoon.enabled=true;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();s.input.testInput=false;s.input.testDirection=Vector2.zero;s.input.testBrake=false;s.input.Enabled=true;s.vehicle.CanDrive=true;s.follow.Snap();
            Lines.Add("RESTORED normal input, AI/spawner/turret/harpoon enabled, no frozen constraints, health and pools reset.");Save();
        }
        return "Failures="+failures+"; elapsed="+(Time.realtimeSinceStartup-began).ToString("F1")+"s\n"+string.Join("\n",Lines);
    }
}
