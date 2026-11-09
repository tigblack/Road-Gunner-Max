#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Production input against the unchanged saved Reference-arena boulder. No copied mesh, proxy wall or impact-impulse requirement.</summary>
    public sealed class WalkerRockContactPlayChecks : MonoBehaviour
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        public const string ReportPath=Folder+"/RockContact.txt";
        const string RockPath="Reference arena/Solid scattered rocks/scenery-052";
        public bool Completed{get;private set;}
        public int Checks{get;private set;}
        public int Failures{get;private set;}
        GameSession s;VehicleController car;MeshCollider rock;Collider ground;
        readonly Dictionary<Behaviour,bool> states=new Dictionary<Behaviour,bool>();
        readonly List<string> report=new List<string>(),samples=new List<string>();
        BoxCollider[] chassis;Vector3 face,inward,tangent;Matrix4x4 rockPose;Mesh rockMesh;Vector3[] rockVertices;int[] rockIndices;
        readonly RaycastHit[] nearbyRockHits=new RaycastHit[64];
        bool saved,restored,damage,inputTest,inputBrake;Vector2 inputDirection;float timeScale,restHeight;int fps,vsync;
        public void Begin(){Directory.CreateDirectory(Folder);StartCoroutine(Guarded());}
        IEnumerator Guarded()
        {
            report.Add("Actual saved rock contact — Unity "+Application.unityVersion);
            report.Add("One unchanged Default-layer nonconvex MeshCollider at "+RockPath+". Inputs enter the production DriveInput testDirection -> Update -> walker FixedUpdate path. ResetPose only sets up independent trials. No driving position/velocity injection, substitute colliders, copied props, geometry mutation or required impact impulse (the walker deliberately sweeps before touching). No scene/asset writes; limited regression, not exhaustive rock certification.");
            samples.Add("trial,fixed_seconds,x,y,z,horizontal_mps,grounded,blocked,exact_rock_penetration_m,lift_m");
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var it=stack.Peek();bool more=false;object next=null;try{more=it.MoveNext();if(more)next=it.Current;}catch(Exception e){Check("fixture exception",false,e.ToString());break;}if(!more){(stack.Pop()as IDisposable)?.Dispose();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;}
            while(stack.Count>0)(stack.Pop()as IDisposable)?.Dispose();Restore();report.Add("RESULT checks="+Checks+"; failures="+Failures+"; saved rock and scene not modified.");Write();Completed=true;
        }
        IEnumerator Run()
        {
            yield return null;yield return null;s=GetComponent<GameSession>();if(!s||!s.vehicle)throw new InvalidOperationException("Saved session/player missing.");car=s.vehicle;
            timeScale=Time.timeScale;fps=Application.targetFrameRate;vsync=QualitySettings.vSyncCount;damage=s.health.DamageEnabled;inputTest=s.input.testInput;inputBrake=s.input.testBrake;inputDirection=s.input.testDirection;saved=true;
            s.Restart();Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            Disable(s.spawns);Disable(s.respawns);Disable(s.turret);Disable(s.exploration);Disable(car.GetComponent<HarpoonCollector>());
            foreach(var station in s.runStats.stations)Disable(station);foreach(var workshop in s.minimap.workshops)Disable(workshop);
            foreach(var human in s.spawns.pool)if(human)human.Despawn();foreach(var buggy in s.spawns.buggyPool)if(buggy)buggy.Despawn();s.zone1.DespawnAll();s.health.DamageEnabled=false;
            var level=s.minimap.layout;var node=level&&level.authoredContentRoot?level.authoredContentRoot.Find(RockPath):null;
            rock=node?node.GetComponent<MeshCollider>():null;ground=level&&level.ground?level.ground.GetComponent<Collider>():null;
            if(!rock||!ground)throw new InvalidOperationException("Actual saved rock/terrain missing; historical SceneryCrush Hard IDs are not substitutes.");
            chassis=car.GetComponentsInChildren<BoxCollider>(true).Where(b=>b.enabled&&!b.isTrigger&&b.attachedRigidbody==car.Body).ToArray();
            rockMesh=rock.sharedMesh;rockPose=rock.transform.localToWorldMatrix;rockVertices=rockMesh.vertices;rockIndices=rockMesh.triangles;
            Check("fixture uses the actual saved solid Boulder",rock.enabled&&!rock.isTrigger&&!rock.convex&&rock.gameObject.layer==0&&rockMesh.vertexCount==330&&rockIndices.Length==330&&Vector3.Distance(rock.transform.position,new Vector3(-58.66428f,0,-91.414055f))<.01f,
                "path="+RockPath+"; world="+rock.transform.position.ToString("F5")+"; bounds="+rock.bounds+"; vertices="+rockMesh.vertexCount+"; chassis boxes="+chassis.Length);
            yield return Reset(Ground(s.startPosition)+Vector3.up*.8f,s.startRotation);restHeight=car.Body.position.y-Ground(car.Body.position).y;
            if(!FindFace())throw new InvalidOperationException("No clear grounded full-compound approach to saved scenery-052; do not replace or move it.");
            report.Add("SELECTED actual face="+face.ToString("F5")+"; inward="+inward.ToString("F5")+"; tangent="+tangent.ToString("F5")+"; rest root height="+restHeight);
            var start=Ground(face-inward*8)+Vector3.up*.8f;yield return Reset(start,Quaternion.LookRotation(inward));
            var p0=car.Body.position;float y0=p0.y,maxLift=0,maxPen=0,maxPlane=float.NegativeInfinity,peak=0;int blocked=0,rockBlocked=0;
            Command(inward);float end=Time.fixedTime+1.5f;
            while(Time.fixedTime<end){yield return new WaitForFixedUpdate();if(car.WalkerBlocked){blocked++;if(RockAhead(inward))rockBlocked++;}peak=Mathf.Max(peak,car.HorizontalSpeed);maxLift=Mathf.Max(maxLift,car.Body.position.y-y0);maxPen=Mathf.Max(maxPen,Penetration());maxPlane=Mathf.Max(maxPlane,Vector3.Dot(car.Body.position-face,inward));Sample("head-on",y0);}
            Check("head-on walker is stopped by the real rock without lift or tunneling",rockBlocked>0&&peak>=car.tuning.maxSpeed*.9f&&Vector3.Distance(p0,car.Body.position)>2&&maxPlane<-.15f&&maxPen<=.04f&&maxLift<.35f,
                "blocked Fixed="+blocked+"; verified rock-adjacent="+rockBlocked+"; peak="+peak+"; travel="+Vector3.Distance(p0,car.Body.position)+"; furthest face plane="+maxPlane+"; exact penetration="+maxPen+"; lift="+maxLift);
            var contact=car.Body.position;Command(-inward);yield return null;yield return new WaitForFixedUpdate();float escape=Vector3.Dot(Flat(car.Body.linearVelocity),-inward);
            yield return new WaitForFixedUpdate();
            Check("steering away from actual rock responds on the first eligible Fixed",car.WalkerGrounded&&escape>=car.tuning.maxSpeed*.85f&&Vector3.Dot(car.Body.position-contact,-inward)>.15f&&Penetration()<=.04f,
                "first escape mps="+escape+"; escape distance="+Vector3.Dot(car.Body.position-contact,-inward)+"; grounded="+car.GroundedWheels);
            Command(Vector3.zero);
            Vector3 shallow=(inward*.35f+tangent*.94f).normalized;
            start=Ground(face-shallow*8)+Vector3.up*.8f;
            if(!ClearPose(Ground(start)+Vector3.up*restHeight,Quaternion.LookRotation(shallow)))throw new InvalidOperationException("Saved rock shallow setup is physically occupied; no obstacle deletion is allowed.");
            yield return Reset(start,Quaternion.LookRotation(shallow));p0=car.Body.position;y0=p0.y;maxLift=maxPen=0;blocked=rockBlocked=0;Command(shallow);end=Time.fixedTime+1.15f;
            while(Time.fixedTime<end){yield return new WaitForFixedUpdate();if(car.WalkerBlocked){blocked++;if(RockAhead(shallow))rockBlocked++;}maxLift=Mathf.Max(maxLift,car.Body.position.y-y0);maxPen=Mathf.Max(maxPen,Penetration());Sample("shallow",y0);}
            Check("shallow input slides at the saved rock without penetrating or climbing",rockBlocked>0&&Vector3.Dot(car.Body.position-p0,tangent)>4&&maxPen<=.04f&&maxLift<.35f,
                "blocked Fixed="+blocked+"; verified rock-adjacent="+rockBlocked+"; tangent travel="+Vector3.Dot(car.Body.position-p0,tangent)+"; exact penetration="+maxPen+"; lift="+maxLift);
            Command(Vector3.zero);yield return null;yield return new WaitForFixedUpdate();
            Check("saved collision geometry and ledger remain unchanged",rock.enabled&&!rock.isTrigger&&rock.sharedMesh==rockMesh&&rock.transform.localToWorldMatrix==rockPose&&rockVertices.SequenceEqual(rockMesh.vertices)&&rockIndices.SequenceEqual(rockMesh.triangles)&&s.Kills==0&&s.Scrap==0&&s.health.Current==s.health.Maximum,
                "same mesh="+(rock.sharedMesh==rockMesh)+"; same pose="+(rock.transform.localToWorldMatrix==rockPose)+"; ledger="+s.Kills+" / "+s.Scrap+"; HP="+s.health.Current);
        }
        bool FindFace()
        {
            // Try the main-road side first (logical eastward), then actual rays around the same unchanged boulder.
            for(int i=0;i<8;i++)
            {
                Vector3 rayDirection=Quaternion.Euler(0,135+i*45,0)*Vector3.forward;
                Vector3 centre=rock.bounds.center;centre.y=rock.bounds.min.y+.9f;
                if(!rock.Raycast(new Ray(centre-rayDirection*(rock.bounds.extents.magnitude+5),rayDirection),out var hit,rock.bounds.size.magnitude+12)||Mathf.Abs(hit.normal.y)>.5f)continue;
                Vector3 approach=-Flat(hit.normal).normalized;if(approach.sqrMagnitude<.9f)continue;
                Vector3 side=Vector3.Cross(Vector3.up,approach);bool clear=true;
                for(float d=0;d<=3;d+=.5f)if(!ClearPose(Ground(hit.point-approach*(8-d))+Vector3.up*restHeight,Quaternion.LookRotation(approach))){clear=false;break;}
                if(!clear)continue;
                // Both tangential senses remain genuine scene geometry; choose a clear starting pose.
                for(int sign=1;sign>=-1;sign-=2)
                {var shallow=(approach*.35f+side*sign*.94f).normalized;var start=Ground(hit.point-shallow*8)+Vector3.up*restHeight;if(!ClearPose(start,Quaternion.LookRotation(shallow)))continue;face=hit.point;inward=approach;tangent=side*sign;return true;}
            }
            return false;
        }
        bool ClearPose(Vector3 position,Quaternion rotation)
        {
            foreach(var box in chassis)
            {
                BoxPose(box,position,rotation,out var centre,out var half,out var orientation,out var origin);
                foreach(var other in Physics.OverlapBox(centre,half,orientation,~0,QueryTriggerInteraction.Ignore))
                {
                    if(!Solid(other))continue;
                    if(Physics.ComputePenetration(box,origin,orientation,other,other.transform.position,other.transform.rotation,out var normal,out float depth)&&Vector3.Dot(normal,Vector3.up)<.6f&&depth>.005f)return false;
                }
            }
            return true;
        }
        void BoxPose(BoxCollider box,Vector3 position,Quaternion rotation,out Vector3 centre,out Vector3 half,out Quaternion orientation,out Vector3 origin)
        {
            var inverse=Quaternion.Inverse(car.transform.rotation);centre=position+rotation*(inverse*(box.transform.TransformPoint(box.center)-car.transform.position));origin=position+rotation*(inverse*(box.transform.position-car.transform.position));orientation=rotation*inverse*box.transform.rotation;
            var scale=box.transform.lossyScale;half=Vector3.Scale(box.size*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
        }
        bool Solid(Collider other)=>other&&other.enabled&&!other.isTrigger&&other.attachedRigidbody!=car.Body&&!other.transform.IsChildOf(car.transform)&&!Physics.GetIgnoreLayerCollision(car.gameObject.layer,other.gameObject.layer);
        float Penetration()
        {
            float maximum=0;foreach(var box in chassis){BoxPose(box,car.Body.position,car.Body.rotation,out _,out _,out var orientation,out var origin);if(Physics.ComputePenetration(box,origin,orientation,rock,rock.transform.position,rock.transform.rotation,out var normal,out float depth)&&Vector3.Dot(normal,Vector3.up)<.6f)maximum=Mathf.Max(maximum,depth);}return maximum;
        }
        bool RockAhead(Vector3 direction)
        {
            // Distinguish this real rock from an unrelated blocker; a pre-contact sweep may correctly produce no Collision event.
            foreach(var box in chassis)
            {
                BoxPose(box,car.Body.position,car.Body.rotation,out var centre,out var half,out var orientation,out _);
                int count=Physics.BoxCastNonAlloc(centre,half,direction,nearbyRockHits,orientation,.2f,1,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)if(nearbyRockHits[i].collider==rock&&Vector3.Dot(nearbyRockHits[i].normal,Vector3.up)<.6f&&Vector3.Dot(nearbyRockHits[i].normal,direction)<-.01f)return true;
            }
            return false;
        }
        Vector3 Ground(Vector3 point){if(!ground.Raycast(new Ray(point+Vector3.up*20,Vector3.down),out var hit,50))throw new InvalidOperationException("No actual saved ground under fixture "+point);return hit.point;}
        IEnumerator Reset(Vector3 position,Quaternion rotation)
        {
            Command(Vector3.zero);yield return null;s.input.Clear();car.ResetPose(position,rotation);Physics.SyncTransforms();float start=Time.fixedTime,until=Time.realtimeSinceStartup+3;int stable=0;
            do{yield return new WaitForFixedUpdate();stable=car.WalkerGrounded&&Mathf.Abs(car.Body.linearVelocity.y)<.15f?stable+1:0;}while((Time.fixedTime-start<.6f||stable<8)&&Time.realtimeSinceStartup<until);
            if(stable<8)throw new InvalidOperationException("Rock fixture failed to settle on real terrain.");
        }
        void Command(Vector3 world){s.input.testInput=true;s.input.testBrake=false;world=Flat(world).normalized;s.input.testDirection=new Vector2(Vector3.Dot(world,s.input.ScreenToWorldDirection(Vector2.right)),Vector3.Dot(world,s.input.ScreenToWorldDirection(Vector2.up)));}
        static Vector3 Flat(Vector3 value)=>Vector3.ProjectOnPlane(value,Vector3.up);
        void Sample(string name,float startY){var p=car.Body.position;samples.Add(string.Join(",",name,Time.fixedTime.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),p.x.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),p.y.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),p.z.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),car.HorizontalSpeed.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),car.GroundedWheels,car.WalkerBlocked,Penetration().ToString("F5",System.Globalization.CultureInfo.InvariantCulture),(p.y-startY).ToString("F5",System.Globalization.CultureInfo.InvariantCulture)));}
        void Disable(Behaviour b){if(b&&!states.ContainsKey(b)){states[b]=b.enabled;b.enabled=false;}}
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;report.Add((pass?"PASS ":"FAIL ")+name+": "+detail);Debug.Log("WALKER ROCK "+report[report.Count-1]);Write();}
        void Write(){File.WriteAllLines(ReportPath,report);File.WriteAllLines(Folder+"/RockContact.csv",samples);}
        void Restore(){if(restored)return;restored=true;if(!saved)return;Time.timeScale=1;foreach(var state in states)if(state.Key)state.Key.enabled=state.Value;if(s){s.input.testInput=false;s.input.Clear();s.health.DamageEnabled=damage;s.Restart();s.input.testInput=inputTest;s.input.testBrake=inputBrake;s.input.testDirection=inputDirection;}Time.timeScale=timeScale;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;}
        void OnDestroy(){Restore();}
    }
}
#endif
