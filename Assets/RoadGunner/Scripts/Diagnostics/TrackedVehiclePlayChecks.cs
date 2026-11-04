#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Identical saved-vehicle straight course compared with the selected published speed baseline.</summary>
    public sealed class TrackedVehiclePlayChecks : MonoBehaviour
    {
        public static bool Baseline => Array.IndexOf(Environment.GetCommandLineArgs(), "-rgTrackedBaseline") >= 0;
        public static string ReportPath => "Documentation/RoadGunner/SceneryCrush-2026-11-03/"+(Baseline?"Baseline":"Tracked")+".txt";
        const string Folder="outputs/SceneryCrush-2026-11-03";
        const string SpeedReleaseBaseline="Documentation/RoadGunner/Speed60-2026-11-05/Baseline.json";
        const string LegacyWheeledBaseline="Documentation/RoadGunner/TrackedVehicle-2026-11-03/Baseline.json";
        public bool Completed {get;private set;}
        public int Checks {get;private set;}
        public int Failures {get;private set;}
        [Serializable] public sealed class Measurement { public string unityVersion; public float configuredMax,meanCruise,minCruise,maxCruise,speedAt3,speedAt6; }
        readonly List<string> lines=new List<string>();
        GameSession s; VehicleController car; GameObject floor;
        float step; int fps,vsync; bool restored,originalSpawns,originalTurret; RigidbodyConstraints originalConstraints;
        public void Begin(){Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));Directory.CreateDirectory(Folder);StartCoroutine(Guarded());}
        IEnumerator Guarded(){
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var r=stack.Peek();bool more=false;object value=null;
                try{more=r.MoveNext();if(more)value=r.Current;}catch(Exception e){Check("fixture exception",false,e.ToString());break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(value is IEnumerator nested)stack.Push(nested);else yield return value;
            }
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
            Restore();lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; saved scene not modified.");File.WriteAllLines(ReportPath,lines);Completed=true;
        }
        IEnumerator Run(){
            yield return null;yield return null;s=GetComponent<GameSession>();car=s.vehicle;
            originalConstraints=car.Body.constraints;originalSpawns=s.spawns.enabled;originalTurret=s.turret.enabled;step=Time.fixedDeltaTime;fps=Application.targetFrameRate;vsync=QualitySettings.vSyncCount;
            Time.fixedDeltaTime=1f/60;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;s.Restart();s.spawns.enabled=false;s.turret.enabled=false;s.health.DamageEnabled=false;
            foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();if(s.zone1)s.zone1.DespawnAll();
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Temporary A-B drive course";floor.hideFlags=HideFlags.DontSave;floor.transform.position=new Vector3(0,799,0);floor.transform.localScale=new Vector3(200,2,600);
            car.input.Enabled=true;car.input.testInput=true;car.input.testBrake=true;car.input.testDirection=Vector2.zero;
            car.ResetPose(new Vector3(0,800.8f,-100),Quaternion.identity);Physics.SyncTransforms();s.follow.Snap();
            yield return new WaitForSeconds(1);
            Check("dynamic chassis settles",car.GroundedWheels>=3&&!car.Body.isKinematic&&car.Speed<.25f,"contacts="+car.GroundedWheels+"; speed="+car.Speed);
            var tracks=car.GetComponentInChildren<PlayerTrackVisuals>();
            float leftStart=tracks?tracks.LeftDistanceMeters:0,rightStart=tracks?tracks.RightDistanceMeters:0;
            if(!Baseline){
                Check("two visible physical-contact track assemblies",tracks&&tracks.SideAssemblyCount==2&&tracks.TreadsPerSide>=32&&tracks.SupportRollersPerSide>=4&&tracks.leftMesh&&tracks.rightMesh,"track component="+(tracks!=null));
                Check("ordinary tyres hidden and no decorative collisions",car.wheelVisuals.All(w=>w.GetComponentsInChildren<MeshRenderer>(true).All(r=>!r.enabled))&&tracks&&tracks.GetComponentsInChildren<Collider>(true).Length==0,"original four WheelColliders remain on chassis; track meshes cannot hook obstacles");
            }
            var m=new Measurement{unityVersion=Application.unityVersion,configuredMax=car.tuning.maxSpeed,minCruise=float.MaxValue};
            Command(Vector3.forward);float start=Time.fixedTime,total=0;int count=0;
            while(Time.fixedTime-start<15){yield return new WaitForFixedUpdate();float t=Time.fixedTime-start,v=car.HorizontalSpeed;
                if(t<=3)m.speedAt3=v;if(t<=6)m.speedAt6=v;
                if(t>=13){total+=v;count++;m.minCruise=Mathf.Min(m.minCruise,v);m.maxCruise=Mathf.Max(m.maxCruise,v);}}
            m.meanCruise=total/Mathf.Max(1,count);
            Check("steady straight course reaches finite cruise",m.meanCruise>1&&m.meanCruise<car.tuning.maxSpeed*1.05f&&m.maxCruise-m.minCruise<.2f,"mean="+m.meanCruise+"; range="+m.minCruise+".."+m.maxCruise+"; configured="+m.configuredMax);
            File.WriteAllText(Folder+"/"+(Baseline?"baseline":"tracked")+".json",JsonUtility.ToJson(m,true));
            if(!Baseline)File.WriteAllText("Documentation/RoadGunner/SceneryCrush-2026-11-03/Tracked.json",JsonUtility.ToJson(m,true));
            if(!Baseline){bool speedRelease=File.Exists(SpeedReleaseBaseline);string baselinePath=speedRelease?SpeedReleaseBaseline:LegacyWheeledBaseline;
                var old=JsonUtility.FromJson<Measurement>(File.ReadAllText(baselinePath));
                float requestedRatio=speedRelease?1.6f:.5f;
                float configuredRatio=m.configuredMax/old.configuredMax,ratio=m.meanCruise/old.meanCruise;
                float relativeRatioError=Mathf.Abs(ratio/configuredRatio-1);
                Check("track belts advance with actual straight movement",tracks&&tracks.LeftDistanceMeters-leftStart>40&&tracks.RightDistanceMeters-rightStart>40&&Mathf.Abs((tracks.LeftDistanceMeters-leftStart)-(tracks.RightDistanceMeters-rightStart))<2,"left="+(tracks?tracks.LeftDistanceMeters-leftStart:0)+"; right="+(tracks?tracks.RightDistanceMeters-rightStart:0));
                Check("configured speed matches selected release baseline",old.configuredMax>0&&old.meanCruise>0&&Mathf.Abs(m.configuredMax-old.configuredMax*requestedRatio)<.001f,
                    "baseline="+baselinePath+"; old="+old.configuredMax+"; new="+m.configuredMax+"; requested ratio="+requestedRatio+"; configured ratio="+configuredRatio);
                Check("measured steady speed scales with configured speed",configuredRatio>0&&!float.IsNaN(relativeRatioError)&&!float.IsInfinity(relativeRatioError)&&relativeRatioError<=.03f,
                    "old="+old.meanCruise+"; new="+m.meanCruise+"; measured ratio="+ratio+"; expected configured ratio="+configuredRatio+"; relative error="+(relativeRatioError*100)+"% (limit3%)");
                car.input.testBrake=true;car.input.testDirection=Vector2.zero;yield return new WaitForSeconds(2);
                float lp=tracks?tracks.LeftDistanceMeters:0,rp=tracks?tracks.RightDistanceMeters:0;yield return new WaitForSeconds(.6f);
                Check("parked belts remain still",tracks&&car.HorizontalSpeed<.1f&&Mathf.Abs(tracks.LeftDistanceMeters-lp)<.03f&&Mathf.Abs(tracks.RightDistanceMeters-rp)<.03f,"speed="+car.HorizontalSpeed);
                // Hold position/roll while only the left contacts have support; yaw is physically free.
                float parkedY=car.Body.position.y;floor.transform.position=new Vector3(-100,799,0);
                car.Body.constraints=RigidbodyConstraints.FreezePosition|RigidbodyConstraints.FreezeRotationX|RigidbodyConstraints.FreezeRotationZ;
                car.ResetPose(new Vector3(0,parkedY,-100),Quaternion.identity);Physics.SyncTransforms();Command(Vector3.back);
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                float maximumAssist=0,maximumPivotForce=0,until=Time.fixedTime+.25f;int contacts=car.GroundedWheels;
                while(Time.fixedTime<until){yield return new WaitForFixedUpdate();maximumAssist=Mathf.Max(maximumAssist,Mathf.Abs(car.AppliedYawAssistTorque));maximumPivotForce=Mathf.Max(maximumPivotForce,Mathf.Abs(car.AppliedPivotLateralForce));contacts=Mathf.Max(contacts,car.GroundedWheels);}
                Check("one supported track cannot activate yaw assist",contacts>0&&contacts<=2&&maximumAssist<.01f&&maximumPivotForce<.01f,"contacts="+contacts+"; peak assist="+maximumAssist+"; pivot force="+maximumPivotForce);
                car.Body.constraints=originalConstraints;
            }
        }
        void Command(Vector3 direction){var right=Vector3.ProjectOnPlane(car.input.view.transform.right,Vector3.up).normalized;var up=Vector3.ProjectOnPlane(car.input.view.transform.forward,Vector3.up).normalized;car.input.testBrake=false;car.input.testDirection=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,up));}
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);File.WriteAllLines(ReportPath,lines);Debug.Log("TRACKED CHECK "+lines[lines.Count-1]);}
        void Restore(){if(restored)return;restored=true;if(floor)Destroy(floor);if(s){if(car&&car.Body)car.Body.constraints=originalConstraints;Time.timeScale=1;Time.fixedDeltaTime=step;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;s.spawns.enabled=originalSpawns;s.turret.enabled=originalTurret;car.input.testDirection=Vector2.zero;s.Restart();}}
        void OnDestroy(){Restore();}
    }
}
#endif
