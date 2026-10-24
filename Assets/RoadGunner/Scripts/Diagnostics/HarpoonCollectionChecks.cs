#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace RoadGunner
{
    public sealed class HarpoonCollectionChecks : MonoBehaviour
    {
        public const string ReportFolder="Documentation/RoadGunner/Hero-Harpoon-2026-10-08";
        public bool Running { get; private set; }
        public bool Completed { get; private set; }
        public int Failures { get; private set; }
        GameSession s;HarpoonCollector h;AttackRangeIndicator ground;RigidbodyConstraints originalConstraints;
        float launchSpeed,retractSpeed;
        readonly List<string> results=new List<string>();
        bool sawLaunch,sawRetract;int maxReservations;
        public void Begin()
        {
            if(Running)return;s=GetComponent<GameSession>();h=s.vehicle.GetComponent<HarpoonCollector>();
            ground=s.vehicle.GetComponent<AttackRangeIndicator>();originalConstraints=s.vehicle.Body.constraints;
            launchSpeed=h.harpoonLaunchSpeed;retractSpeed=h.harpoonRetractSpeed;Running=true;Completed=false;Failures=0;results.Clear();Application.runInBackground=true;
            StartCoroutine(Run());
        }
        void Record(string name,bool pass,string detail)
        {
            if(!pass)Failures++;string line=(pass?"PASS ":"FAIL ")+name+": "+detail;results.Add(line);Debug.Log("RG HARPOON "+line);
            Directory.CreateDirectory(ReportFolder);File.WriteAllLines(ReportFolder+"/Checks.txt",results);
            File.WriteAllLines(ReportFolder+"/Checks-"+s.gameObject.scene.name+".txt",results);
        }
        Vector3 Ground(float x,float z)=>new Vector3(x,ground.GroundHeight(x,z),z);
        void Isolate()
        {
            foreach(var r in s.spawns.pool)r.enabled=true;
            s.vehicle.Body.constraints=originalConstraints;h.enabled=true;h.harpoonLaunchSpeed=launchSpeed;h.harpoonRetractSpeed=retractSpeed;
            s.Restart();s.spawns.enabled=false;s.turret.enabled=false;
            foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();
            s.vehicle.ResetPose(Ground(0,0)+Vector3.up*.65f,Quaternion.Euler(0,165,0));
            s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;s.health.DamageEnabled=false;s.follow.Snap();Physics.SyncTransforms();
            sawLaunch=false;sawRetract=false;maxReservations=0;
        }
        void Observe()
        {sawLaunch|=h.State==HarpoonState.Launching;sawRetract|=h.State==HarpoonState.Retracting;maxReservations=Mathf.Max(maxReservations,s.pickups.ReservedGearCount);}
        IEnumerator Reward(int value,float timeout=6,string capture=null)
        {
            float until=Time.time+timeout;bool taken=false;
            while(Time.time<until&&s.Scrap<value)
            {
                Observe();if(!taken&&capture!=null&&h.State==HarpoonState.Retracting){Capture(capture,false);Capture(capture+"-close",true);taken=true;}
                yield return null;
            }
        }
        IEnumerator Phase(HarpoonState state,float timeout=4)
        {float until=Time.time+timeout;while(Time.time<until&&h.State!=state){Observe();yield return null;}}
        Raider Human(float x,float z)
        {
            if(!NavMesh.SamplePosition(Ground(x,z),out var point,2,NavMesh.AllAreas))throw new System.InvalidOperationException("No navigation ground for human check.");
            var r=s.spawns.pool[0];r.Spawn(point.position,0,0);r.enabled=false;Physics.SyncTransforms();return r;
        }
        void Capture(string name,bool close)
        {
            var camera=s.follow.GetComponent<Camera>();int width=close?1200:720,height=close?1200:1280;var rt=RenderTexture.GetTemporary(width,height,24);
            var previous=camera.targetTexture;float size=camera.orthographicSize;var active=RenderTexture.active;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                if(close)camera.orthographicSize=3.6f;camera.targetTexture=rt;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();Directory.CreateDirectory(ReportFolder);File.WriteAllBytes(ReportFolder+"/"+name+".png",texture.EncodeToPNG());
            }
            finally{camera.targetTexture=previous;camera.orthographicSize=size;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
        }
        void Drive(Vector3 direction,float magnitude=1)
        {
            var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;
            var forward=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;
            s.input.testInput=true;s.input.testBrake=false;s.input.testDirection=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward))*magnitude;
        }
        IEnumerator Run()
        {
            try
            {
                Isolate();yield return new WaitForEndOfFrame();
                Record("saved references and hierarchy",h.session==s&&h.pickups==s.pickups&&h.pivot&&h.spawnPoint&&h.collectionPoint&&h.projectile&&h.cable
                    &&s.vehicle.transform.Find("Replaceable visuals/RoofMachineGun/GunPivot")&&s.pickups.gearVisuals.Length==60,
                    "one existing session/pickup pool; roof gun and independent bed harpoon wired");
                Capture("vehicle-gameplay",false);Capture("vehicle-close",true);
                var raider=Human(5,1);int kills=s.Kills;raider.Hit(999,Vector3.right);raider.Hit(999,Vector3.right);
                Record("A human drops one Metal Gear",s.pickups.MetalGearCount==1&&s.pickups.StoredValue==1&&s.Kills==kills+1,"gear="+s.pickups.MetalGearCount+", value="+s.pickups.StoredValue);
                yield return Reward(1,6,"human-retrieval");
                Record("A harpoon collects through existing HUD",s.Scrap==1&&s.pickups.ActiveCount==0&&h.CollectionsCompleted==1&&sawLaunch&&sawRetract&&s.hud.scrapText.text=="001",
                    "Scrap="+s.Scrap+", completed="+h.CollectionsCompleted+", launch="+sawLaunch+", retract="+sawRetract+", HUD="+s.hud.scrapText.text);
                Isolate();var buggy=s.spawns.buggyPool[0];buggy.Spawn(Ground(5,1),Quaternion.identity,0);buggy.AIEnabled=false;buggy.gun.CanShoot=false;Physics.SyncTransforms();
                int explosions=s.effects.ExplosionsPlayed;buggy.Hit(999,Vector3.right);buggy.Hit(999,Vector3.right);
                Record("B vehicle drops gear and retains explosion",s.pickups.MetalGearCount==1&&s.pickups.StoredValue==buggy.tuning.scrapReward&&s.effects.ExplosionsPlayed==explosions+1&&s.Kills==1,
                    "gear="+s.pickups.MetalGearCount+", value="+s.pickups.StoredValue+", explosion delta="+(s.effects.ExplosionsPlayed-explosions));
                yield return Reward(3,6,"vehicle-retrieval");
                Record("B vehicle reward collected once",s.Scrap==3&&s.pickups.ActiveCount==0&&h.CollectionsCompleted==1&&s.hud.scrapText.text=="003","Scrap="+s.Scrap+", HUD="+s.hud.scrapText.text);
                Isolate();for(int i=0;i<3;i++)s.pickups.DropGear(Ground(4+i,1)+Vector3.up,Vector3.right,i+1);
                yield return Reward(6,9,"sequential-retrieval");
                Record("C multiple drops collected sequentially",s.Scrap==6&&h.CollectionsCompleted==3&&s.pickups.ActiveCount==0&&maxReservations==1,
                    "Scrap="+s.Scrap+", collections="+h.CollectionsCompleted+", max reservations="+maxReservations);
                Isolate();s.pickups.DropGear(Ground(13,0)+Vector3.up,Vector3.right);yield return new WaitForSeconds(1.5f);
                Record("D outside-radius gear is ignored",s.Scrap==0&&h.State==HarpoonState.Idle&&s.pickups.ActiveCount==1&&s.pickups.ReservedGearCount==0,"radius="+h.collectionRadius+", target="+h.Target);
                s.vehicle.Body.constraints=originalConstraints;s.vehicle.ResetPose(Ground(0,0)+Vector3.up*.65f,Quaternion.Euler(0,90,0));Drive(Vector3.right);
                yield return Reward(1,9);s.input.testDirection=Vector2.zero;s.input.testBrake=true;
                Record("D driving into range enables collection",s.Scrap==1&&h.CollectionsCompleted==1,"Scrap="+s.Scrap+", car="+s.vehicle.transform.position);
                Isolate();h.harpoonLaunchSpeed=2.5f;h.harpoonRetractSpeed=3;s.pickups.DropGear(Ground(5,1)+Vector3.up,Vector3.right);
                yield return Phase(HarpoonState.Launching);s.vehicle.ResetPose(Ground(0,0)+Vector3.up*.65f,Quaternion.identity);s.vehicle.Body.constraints=originalConstraints;Drive(Vector3.forward,.45f);
                var began=s.vehicle.transform.position;float until=Time.time+1.8f;float cableError=0,pickupError=0;bool activeMoving=false;
                while(Time.time<until)
                {
                    yield return new WaitForEndOfFrame();Observe();
                    if(h.cable.enabled)
                    {
                        activeMoving=true;cableError=Mathf.Max(cableError,Vector3.Distance(h.cable.GetPosition(0),h.spawnPoint.position),Vector3.Distance(h.cable.GetPosition(1),h.projectile.position));
                        if(h.State==HarpoonState.Retracting&&h.Target)pickupError=Mathf.Max(pickupError,Vector3.Distance(h.Target.position,h.projectile.position));
                    }
                }
                float moved=Vector3.Distance(began,s.vehicle.transform.position);s.input.testDirection=Vector2.zero;s.input.testBrake=true;Capture("moving-retrieval",false);
                yield return Reward(1,8);
                Record("E moving vehicle keeps cable and pickup stable",activeMoving&&moved>.5f&&cableError<.001f&&pickupError<.001f&&s.Scrap==1,
                    "movement="+moved+", cable error="+cableError+", pickup error="+pickupError+", Scrap="+s.Scrap);
                Isolate();s.vehicle.ResetPose(Ground(0,0)+Vector3.up*.65f,Quaternion.identity);var victim=Human(0,7);s.turret.enabled=true;
                int shots=s.turret.ShotsFired;float timeout=Time.time+4;while(Time.time<timeout&&victim.Alive)yield return null;
                int shotCount=s.turret.ShotsFired-shots;
                Record("F existing roof gun kills with unchanged tuning",!victim.Alive&&shotCount==3&&s.turret.tuning.shotsPerSecond==5&&s.turret.tuning.bulletDamage==15&&s.turret.Range==10,
                    "actual shots="+shotCount+", rate="+s.turret.tuning.shotsPerSecond+", damage="+s.turret.tuning.bulletDamage+", range="+s.turret.Range);
                Isolate();s.vehicle.ResetPose(Ground(0,0)+Vector3.up*.65f,Quaternion.identity);s.vehicle.Body.constraints=originalConstraints;Drive(Vector3.forward);began=s.vehicle.transform.position;
                yield return new WaitForSeconds(2);s.input.testDirection=Vector2.zero;s.input.testBrake=true;
                int extraBodies=s.vehicle.GetComponentsInChildren<Rigidbody>().Length-1;int childColliders=0;
                foreach(var collider in s.vehicle.transform.Find("Replaceable visuals").GetComponentsInChildren<Collider>())childColliders++;
                Record("G driving and collisions remain clear",s.vehicle.GroundedWheels==4&&Vector3.Distance(began,s.vehicle.transform.position)>2&&extraBodies==0&&childColliders==0,
                    "grounded="+s.vehicle.GroundedWheels+", movement="+Vector3.Distance(began,s.vehicle.transform.position)+", extra rigidbodies="+extraBodies+", visual colliders="+childColliders);
                Isolate();h.harpoonLaunchSpeed=2;s.pickups.DropGear(Ground(5,1)+Vector3.up,Vector3.right);yield return Phase(HarpoonState.Launching);
                Vector3 tip=h.projectile.position,pick=h.Target.position;s.SetPaused(true);yield return new WaitForSecondsRealtime(.3f);
                bool frozen=(h.projectile.position-tip).sqrMagnitude<.000001f&&(h.Target.position-pick).sqrMagnitude<.000001f;
                s.SetPaused(false);yield return Reward(1,8);
                Record("pause resumes exactly one reward",frozen&&s.Scrap==1&&h.CollectionsCompleted==1,"frozen="+frozen+", Scrap="+s.Scrap);
                Isolate();s.pickups.DropGear(Ground(5,1)+Vector3.up,Vector3.right);yield return Phase(HarpoonState.Launching);s.Restart();yield return null;
                Record("restart clears target cable and reservation",h.State==HarpoonState.Idle&&!h.cable.enabled&&!h.projectile.gameObject.activeSelf&&s.pickups.ReservedGearCount==0&&s.pickups.ActiveCount==0&&s.Scrap==0,
                    "state="+h.State+", reservations="+s.pickups.ReservedGearCount+", pickups="+s.pickups.ActiveCount);
                Isolate();h.enabled=false;s.pickups.DropGear(Ground(5,1)+Vector3.up,Vector3.right);yield return new WaitForSeconds(.7f);
                bool reserved=s.pickups.TryReserveNearestGear(s.vehicle.transform.position,8,this,out var first);
                bool contested=s.pickups.TryReserveNearestGear(s.vehicle.transform.position,8,s,out _);s.pickups.ReleaseGear(first,this);
                bool released=s.pickups.TryReserveNearestGear(s.vehicle.transform.position,8,s,out var second);s.pickups.ReleaseGear(second,s);
                Record("reservation excludes another collector and releases",reserved&&!contested&&released,"first="+reserved+", contested="+contested+", released="+released);
                // Normal pickup uses the same Collect/AddScrap path when the harpoon is disabled.
                s.vehicle.ResetPose(s.pickups.items[first.Slot].position+Vector3.up*.4f,Quaternion.identity);Physics.SyncTransforms();yield return new WaitForSeconds(.2f);
                Record("normal collection still awards the same resource",s.Scrap==1&&s.pickups.ActiveCount==0,"Scrap="+s.Scrap);
                Isolate();h.enabled=false;for(int i=0;i<85;i++)s.pickups.DropGear(Ground(30,30)+Vector3.up,Vector3.right);
                Record("bounded pool preserves overflow reward",s.pickups.ActiveCount==60&&s.pickups.StoredValue==85,"pool="+s.pickups.ActiveCount+", stored value="+s.pickups.StoredValue);
                Record("completed",Failures==0,"A-G, pause, restart, reservation, normal pickup and capped overflow verified in actual Unity Play Mode.");Completed=true;
            }
            finally
            {
                s.vehicle.Body.constraints=originalConstraints;foreach(var r in s.spawns.pool)r.enabled=true;
                h.enabled=true;h.harpoonLaunchSpeed=launchSpeed;h.harpoonRetractSpeed=retractSpeed;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();Running=false;
            }
        }
    }
}
#endif
