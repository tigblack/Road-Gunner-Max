#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadGunner
{
    /// <summary>Real saved-world stones, original probes/registry, and production physical input.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class StoneContactPlayChecks : MonoBehaviour
    {
        public const string ReportPath="Documentation/RoadGunner/StoneFix-2026-11-05/PlayMode.txt";
        const string Folder="outputs/StoneFix-2026-11-05";
        const string Prefix="shoulder/GlobalObjectId_V1-2-c699e8c72621f4262ab2e9410c7a77d8-";
        static readonly string[] Expected={Prefix+"4386920637868705970-2105868946/0-Boulder",Prefix+"4386920637868705970-372142831/0-Boulder",
            Prefix+"4386920637868705970-482917732/0-Boulder",Prefix+"8863308609420397489-1660182604/0-Boulder",Prefix+"8863308609420397489-992461607/0-Boulder"};
        const string HardId="standalone/concept-roadside/GlobalObjectId_V1-2-c699e8c72621f4262ab2e9410c7a77d8-6101300767503800227-1092554088/Boulder";
        public bool Completed{get;private set;} public int Checks{get;private set;} public int Failures{get;private set;}
        readonly List<string> lines=new List<string>();
        readonly Dictionary<Behaviour,bool> enabledStates=new Dictionary<Behaviour,bool>();
        GameSession s; VehicleController car; ArenaDecorationCrush manager; DecorationCrushChunk[] registry;
        DecorationCrushChunk chunk; int index; Mesh source; int[] indices; Vector3[] vertices; Matrix4x4 sourcePose;
        Collider ground,road; RigidbodyConstraints constraints; float step; int fps,vsync,registered;
        bool captured,running,restored; StoneContactCollisionProbe collision;
        struct Ledger{public float hp,max,damage,ram,bonusHp;public int kills,scrap,earned,value,attack,health,reward;}
        Ledger Snapshot()=>new Ledger{hp=s.health.Current,max=s.health.Maximum,kills=s.Kills,scrap=s.Scrap,earned=s.ScrapEarned,
            value=s.pickups.StoredValue,attack=s.runStats.AttackLevel,health=s.runStats.HealthLevel,damage=s.runStats.BonusDamagePercent,
            ram=s.runStats.BonusRamPercent,bonusHp=s.runStats.BonusMaxHP,reward=s.exploration.RewardSerial};
        sealed class Pass{public bool crossed,broken,debris;public float speed=-1,peak,magnitude;public int grounded;}
        public void Begin(){if(running)return;running=true;Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));Directory.CreateDirectory(Folder);
            lines.Add("Saved-world Stone contact — Unity "+Application.unityVersion);
            lines.Add("Original scene rocks/probes/source transforms/terrain and full crush registry are retained. Only the actual player's setup pose is changed. AI is administratively despawned, automatic combat/rewards isolated; respawn manager remains enabled. No direct crush calls, substituted probes, copied props, injected moving velocity or scene/asset writes.");Write();StartCoroutine(Guarded());}
        IEnumerator Guarded(){yield return null;yield return null;var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var part=stack.Peek();bool more=false;object value=null;try{more=part.MoveNext();if(more)value=part.Current;}
                catch(Exception e){Check("saved-world contact exception",false,e.ToString());break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(value is IEnumerator nested)stack.Push(nested);else yield return value;}
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();try{Restore();}catch(Exception e){Check("fixture restore",false,e.ToString());}
            lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; original settings restored, saved world/assets not written.");running=false;Completed=true;Write();}
        IEnumerator Run(){
            s=GetComponent<GameSession>();if(!s)throw new InvalidOperationException("Attach to saved GameSession.");car=s.vehicle;
            manager=UnityEngine.Object.FindAnyObjectByType<ArenaDecorationCrush>();if(!manager)throw new InvalidOperationException("Missing saved crush manager.");
            registry=manager.chunks;registered=manager.RegisteredCount;constraints=car.Body.constraints;step=Time.fixedDeltaTime;fps=Application.targetFrameRate;vsync=QualitySettings.vSyncCount;captured=true;
            ground=s.minimap.layout.ground.GetComponent<Collider>();var surface=s.minimap.layout.authoredContentRoot.Find("Arena exploration/Physical connected roads and combat pockets");road=surface?surface.GetComponent<Collider>():null;
            SaveDisable(s.turret);SaveDisable(s.spawns);SaveDisable(s.exploration);SaveDisable(car.GetComponent<HarpoonCollector>());
            foreach(var station in s.runStats.stations)SaveDisable(station);foreach(var workshop in s.minimap.workshops)SaveDisable(workshop);
            Time.fixedDeltaTime=1f/60;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;Fresh();yield return null;
            var found=Expected.Select(id=>registry.SelectMany(c=>c.items.Select((item,i)=>new{c,item,i})).FirstOrDefault(x=>x.item!=null&&x.item.id==id)).ToArray();
            bool eligible=manager.isActiveAndEnabled&&s.respawns&&s.respawns.isActiveAndEnabled&&s.respawns.PendingCount==0&&
                manager.RegisteredCount==registry.Sum(c=>c.items.Length)&&found.All(x=>x!=null&&x.item.kind==DecorationCrushKind.Stone&&
                x.c.isActiveAndEnabled&&x.c.ValidItem(x.i)&&x.item.probe&&x.item.probe.enabled&&x.item.probe.isTrigger&&x.item.probe.gameObject.layer==12);
            Check("all five exact-small saved Boulders have original enabled registered Stone probes",eligible,
                "Original chunks="+registry.Length+"; registered="+manager.RegisteredCount+"; expected5 found="+found.Count(x=>x!=null)+
                "; missing/wrong="+string.Join(" | ",Expected.Where((id,i)=>found[i]==null||found[i].item.kind!=DecorationCrushKind.Stone)));
            if(!eligible){lines.Add("Dependent driving checks skipped: a missing saved-world Stone entry is the baseline authoring failure, not repaired by this fixture.");Write();yield break;}
            chunk=found[0].c;index=found[0].i;source=chunk.OriginalMesh;indices=Flatten(source);vertices=source.vertices;sourcePose=chunk.source.transform.localToWorldMatrix;
            yield return Approach(chunk.GetWorldBounds(index).center,Vector3.left,8);
            var ledger=Snapshot();var low=new Pass();yield return Drive(.2f,45,low,"low");
            Check("actual saved Main01 rock crumbles under low physical input without corrupting neighbours or gameplay",low.crossed&&low.broken&&
                low.speed>=manager.minimumImpactSpeed&&low.speed<2&&low.magnitude>0&&low.magnitude<.2f&&low.grounded>=3&&low.debris&&
                CorrectGeometry()&&ledger.Equals(Snapshot())&&s.health.DamageEnabled,
                "ID="+Expected[0]+"; speed="+low.speed+"; mapped input="+low.magnitude+"; grounded="+low.grounded+"; crossed="+low.crossed+"; debris="+low.debris+"; ledger unchanged="+ledger.Equals(Snapshot()));
            bool broke=chunk.IsBroken(index);Fresh();yield return null;
            Check("Restart restores this same authored rock and probe in the untouched registry",broke&&!chunk.IsBroken(index)&&chunk.items[index].probe.enabled&&
                Flatten(chunk.source.sharedMesh).SequenceEqual(indices)&&manager.BrokenCount==0&&manager.ActiveDebrisCount==0&&RegistryUnchanged(),
                "Restored="+!chunk.IsBroken(index)+"; actual probe enabled="+chunk.items[index].probe.enabled+"; same registry="+RegistryUnchanged());
            yield return Approach(chunk.GetWorldBounds(index).center,Vector3.left,8);ledger=Snapshot();var full=new Pass();yield return Drive(1,16,full,"full");
            Check("full input re-crushes the original saved rock with real contacts and unchanged rewards",full.crossed&&full.broken&&full.speed>2&&
                full.magnitude>.99f&&full.grounded>=3&&full.debris&&CorrectGeometry()&&ledger.Equals(Snapshot())&&s.health.DamageEnabled,
                "Speed="+full.speed+"; peak="+full.peak+"; mapped input="+full.magnitude+"; crossed="+full.crossed+"; grounded="+full.grounded+"; geometry correct="+CorrectGeometry()+"; ledger unchanged="+ledger.Equals(Snapshot()));
            Check("saved source asset, transforms and complete manager registry survive real contact",Flatten(source).SequenceEqual(indices)&&source.vertices.SequenceEqual(vertices)&&
                chunk.source.transform.localToWorldMatrix==sourcePose&&RegistryUnchanged()&&s.respawns.isActiveAndEnabled&&s.respawns.PendingCount==0,
                "Asset indices/vertices unchanged; original pose="+(chunk.source.transform.localToWorldMatrix==sourcePose)+"; registered="+manager.RegisteredCount+"; respawns enabled="+s.respawns.isActiveAndEnabled);
            yield return HardControl();
        }
        void SaveDisable(Behaviour b){if(!b||enabledStates.ContainsKey(b))return;enabledStates.Add(b,b.enabled);b.enabled=false;}
        void Fresh(){s.Restart();foreach(var pair in enabledStates)if(pair.Key)pair.Key.enabled=false;
            foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();if(s.zone1)s.zone1.DespawnAll();
            car.Body.constraints=constraints;s.input.Enabled=true;car.CanDrive=true;s.health.DamageEnabled=true;Command(Vector3.zero,true);Physics.SyncTransforms();}
        bool RegistryUnchanged()=>ReferenceEquals(manager.chunks,registry)&&manager.RegisteredCount==registered&&manager.RegisteredCount==registry.Sum(c=>c.items.Length);
        Vector3 Ground(Vector3 point){if(!ground||!ground.Raycast(new Ray(point+Vector3.up*50,Vector3.down),out var hit,100))throw new InvalidOperationException("No authored ground below "+point);return hit.point;}
        IEnumerator Approach(Vector3 centre,Vector3 direction,float distance){
            Vector3 start=Ground(centre-direction*distance);Quaternion rotation=Quaternion.LookRotation(direction);
            // Read original physical geometry before using the course; no obstacle is moved or ignored during driving.
            var body=car.GetComponent<BoxCollider>();
            for(float at=0;at<distance+4;at+=.5f){Vector3 point=Ground(start+direction*at)+Vector3.up*.55f;
                foreach(var hit in Physics.OverlapBox(point+rotation*body.center,body.size*.5f,rotation,1,QueryTriggerInteraction.Ignore))
                    if(hit!=ground&&hit!=road)throw new InvalidOperationException("Authored approach blocked by "+hit.name+" at "+point);}
            car.ResetPose(start+Vector3.up*.8f,rotation);Physics.SyncTransforms();s.follow.Snap();Command(Vector3.zero,true);
            float until=Time.fixedTime+1;while(Time.fixedTime<until)yield return new WaitForFixedUpdate();}
        Vector3 drivingCentre;Vector3 drivingDirection;
        IEnumerator Drive(float input,float timeout,Pass result,string label){
            drivingCentre=chunk.GetWorldBounds(index).center;drivingDirection=Vector3.left;
            Command(drivingDirection*input);float began=Time.fixedTime,brokeAt=-1;bool before=false,after=false;
            while(Vector3.Dot(car.Body.position-drivingCentre,drivingDirection)<4&&Time.fixedTime-began<timeout){
                yield return new WaitForFixedUpdate();result.peak=Mathf.Max(result.peak,car.HorizontalSpeed);
                float along=Vector3.Dot(car.Body.position-drivingCentre,drivingDirection);
                if(!before&&!chunk.IsBroken(index)&&along>=-5){Capture(label+"-before");before=true;}
                if(chunk.IsBroken(index)&&brokeAt<0){brokeAt=Time.fixedTime;result.broken=true;result.speed=car.Body.GetPointVelocity(chunk.GetWorldBounds(index).ClosestPoint(car.Body.worldCenterOfMass)).magnitude;
                    result.magnitude=s.input.Magnitude;result.grounded=car.GroundedWheels;result.debris=manager.ActiveDebrisCount>=6&&manager.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.name.StartsWith("Fragment ",StringComparison.Ordinal));}
                if(!after&&brokeAt>=0&&Time.fixedTime-brokeAt>=.12f){Capture(label+"-debris");after=true;}}
            result.crossed=Vector3.Dot(car.Body.position-drivingCentre,drivingDirection)>=4;Command(Vector3.zero,true);}
        bool CorrectGeometry(){
            if(!chunk.IsBroken(index)||!chunk.RuntimeMesh)return false;var after=Flatten(chunk.RuntimeMesh);if(after.Length!=indices.Length)return false;
            var allowed=new HashSet<int>();for(int j=0;j<chunk.items.Length;j++)if(chunk.IsBroken(j)){var item=chunk.items[j];for(int r=0;r<item.rangeStarts.Length;r++)for(int k=item.rangeStarts[r];k<item.rangeStarts[r]+item.rangeCounts[r];k++)allowed.Add(k);}
            var selected=chunk.items[index];bool changed=false;
            for(int r=0;r<selected.rangeStarts.Length;r++)for(int i=selected.rangeStarts[r];i<selected.rangeStarts[r]+selected.rangeCounts[r];i+=3){if(after[i]!=after[i+1]||after[i]!=after[i+2])return false;changed|=after[i+1]!=indices[i+1]||after[i+2]!=indices[i+2];}
            return changed&&Enumerable.Range(0,after.Length).Where(i=>!allowed.Contains(i)).All(i=>after[i]==indices[i])&&
                chunk.RuntimeMesh.vertices.SequenceEqual(vertices)&&Flatten(source).SequenceEqual(indices)&&source.vertices.SequenceEqual(vertices);}
        IEnumerator HardControl(){
            Fresh();string suffix;using(var sha=SHA256.Create())suffix=string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(HardId)).Select(b=>b.ToString("x2"))).Substring(0,20);
            var root=s.minimap.layout.decorationRoot.Find("Scenery crush collisions/Hard exact collision (Default)/Hard "+suffix);
            var hard=root?root.GetComponent<MeshCollider>():null;if(!hard)throw new InvalidOperationException("Missing actual saved large Boulder "+HardId);
            Vector3 face=Vector3.zero,heading=Vector3.zero;bool approach=false;
            for(int n=0;n<8&&!approach;n++){var direction=Quaternion.Euler(0,n*45,0)*Vector3.forward;
                if(!hard.Raycast(new Ray(hard.bounds.center-direction*(hard.bounds.extents.magnitude+4),direction),out var hit,hard.bounds.size.magnitude+8))continue;
                var start=Ground(hit.point-direction*8);var box=car.GetComponent<BoxCollider>();Quaternion rotation=Quaternion.LookRotation(direction);bool clear=true;
                for(float at=0;at<4;at+=.5f)foreach(var c in Physics.OverlapBox(Ground(start+direction*at)+Vector3.up*.55f+rotation*box.center,box.size*.5f,rotation,1,QueryTriggerInteraction.Ignore))if(c!=ground&&c!=road&&c!=hard)clear=false;
                if(!clear)continue;face=hit.point;heading=direction;car.ResetPose(start+Vector3.up*.8f,rotation);Physics.SyncTransforms();s.follow.Snap();approach=true;}
            if(!approach)throw new InvalidOperationException("No physical approach to actual large Boulder.");
            collision=car.gameObject.AddComponent<StoneContactCollisionProbe>();collision.Target=hard;
            float until=Time.fixedTime+1;Command(Vector3.zero,true);while(Time.fixedTime<until)yield return new WaitForFixedUpdate();
            var ledger=Snapshot();float peak=0,furthest=float.NegativeInfinity,began=Time.fixedTime;Command(heading);
            while(collision.Contacts==0&&Time.fixedTime-began<10){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,car.HorizontalSpeed);furthest=Mathf.Max(furthest,Vector3.Dot(car.Body.position-face,heading));}
            Command(Vector3.zero,true);until=Time.fixedTime+.4f;while(Time.fixedTime<until){yield return new WaitForFixedUpdate();furthest=Mathf.Max(furthest,Vector3.Dot(car.Body.position-face,heading));}
            Check("an unchanged large Boulder physically blocks the real saved-world vehicle",hard.enabled&&!hard.isTrigger&&collision.Contacts>0&&collision.Impulse>car.Body.mass*.2f&&
                peak>2&&furthest<0&&ledger.Equals(Snapshot()),"Actual hard="+HardId+"; contacts="+collision.Contacts+"; impulse="+collision.Impulse+"; peak="+peak+"; furthest beyond face="+furthest+"; ledger unchanged="+ledger.Equals(Snapshot()));}
        void Command(Vector3 direction,bool brake=false){var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;var up=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;
            s.input.testInput=true;s.input.testDirection=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,up));s.input.testBrake=brake;}
        static int[] Flatten(Mesh mesh)=>Enumerable.Range(0,mesh.subMeshCount).SelectMany(i=>mesh.GetIndices(i)).ToArray();
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);Debug.Log("STONE CONTACT "+lines[lines.Count-1]);Write();}
        void Write()=>File.WriteAllLines(ReportPath,lines);
        sealed class CanvasState{public Canvas canvas;public RenderMode mode;public Camera camera;public float plane;}
        void Capture(string name){var camera=s.input.view;var target=RenderTexture.GetTemporary(540,960,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float aspect=camera.aspect;
            var texture=new Texture2D(540,960,TextureFormat.RGB24,false);var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>new CanvasState{canvas=c,mode=c.renderMode,camera=c.worldCamera,plane=c.planeDistance}).ToArray();
            try{camera.aspect=540f/960;camera.targetTexture=target;foreach(var c in canvases){c.canvas.renderMode=RenderMode.ScreenSpaceCamera;c.canvas.worldCamera=camera;c.canvas.planeDistance=Mathf.Max(camera.nearClipPlane+.5f,1);}
                Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,540,960),0,0);texture.Apply();File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG());}
            finally{foreach(var c in canvases)if(c.canvas){c.canvas.renderMode=c.mode;c.canvas.worldCamera=c.camera;c.canvas.planeDistance=c.plane;}camera.targetTexture=oldTarget;camera.aspect=aspect;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Destroy(texture);Canvas.ForceUpdateCanvases();}}
        void Restore(){if(restored)return;restored=true;if(collision)Destroy(collision);if(!captured)return;
            Time.timeScale=1;Time.fixedDeltaTime=step;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;car.Body.constraints=constraints;s.Restart();
            foreach(var pair in enabledStates)if(pair.Key)pair.Key.enabled=pair.Value;}
        void OnDestroy(){if(running)Restore();}
    }
    public sealed class StoneContactCollisionProbe:MonoBehaviour{public Collider Target;public int Contacts;public float Impulse;
        void OnCollisionEnter(Collision hit){if(hit.collider==Target){Contacts++;Impulse=Mathf.Max(Impulse,hit.impulse.magnitude);}}}
}
#endif
