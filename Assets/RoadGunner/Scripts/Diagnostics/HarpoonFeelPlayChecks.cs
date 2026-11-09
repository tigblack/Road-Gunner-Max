#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in tests of the saved collector and pool, with real automatic collection and moving endpoints.</summary>
    [DefaultExecutionOrder(12400)] // Sample after collector 12200 and its real audio sink 12300.
    public sealed class HarpoonFeelPlayChecks : MonoBehaviour
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        public const string ReportPath=Folder+"/Harpoon.txt";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        GameSession s; HarpoonCollector h; ArcadeAudio audio;
        readonly List<string> lines=new List<string>();
        readonly Dictionary<Behaviour,bool> states=new Dictionary<Behaviour,bool>();
        bool saved,restored,originalDamage,originalInput,originalTestInput,originalTestBrake,originalCollector;
        Vector2 originalDirection; float originalTime; int originalFps,originalVsync,cancellations;
        bool observeMoving,sawLaunch,sawPull,sawSlack,sawWinch,launchCapture,pullCapture;
        float endpointError,gearError,maxTension;
        Action reentrant;
        public void Begin() { Directory.CreateDirectory(Folder+"/Screenshots");StartCoroutine(Guarded()); }
        IEnumerator Guarded()
        {
            lines.Add("Actual saved HarpoonCollector/PickupPool, Unity "+Application.unityVersion);
            lines.Add("Isolated administrative Restart/despawn and production DropGear create controlled reward fixtures. Every award uses normal contact pickup, automatic harpoon or normal completion payout; no direct CollectReservedGear/Hit/score calls. Movement uses production DriveInput and the actual body. Not a natural combat playthrough; no scene/assets written.");
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                var r=stack.Peek();bool more=false;object current=null;
                try {more=r.MoveNext();if(more)current=r.Current;}
                catch(Exception e){Check("fixture exception",false,e.ToString());break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                if(current is IEnumerator child)stack.Push(child);else yield return current;
            }
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
            try{Restore();}catch(Exception e){Check("restore",false,e.ToString());}
            lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; no saved scene/assets modified.");
            File.WriteAllLines(ReportPath,lines);Completed=true;
        }
        IEnumerator Run()
        {
            yield return null;yield return null;s=GetComponent<GameSession>();h=s.vehicle.GetComponent<HarpoonCollector>();
            if(!h||!h.cable||!h.projectile||!s.pickups)throw new InvalidOperationException("Saved collector/pool/visual anchors missing.");
            audio=s.Feedback?s.Feedback.GetComponentInChildren<ArcadeAudio>(true):null;
            if(!audio||!audio.isActiveAndEnabled)throw new InvalidOperationException("Saved active ArcadeAudio sink missing.");
            originalTime=Time.timeScale;originalFps=Application.targetFrameRate;originalVsync=QualitySettings.vSyncCount;
            originalDamage=s.health.DamageEnabled;originalInput=s.input.Enabled;originalTestInput=s.input.testInput;originalTestBrake=s.input.testBrake;
            originalDirection=s.input.testDirection;originalCollector=h.enabled;saved=true;
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            Disable(s.spawns);Disable(s.turret);Disable(s.respawns);Disable(s.exploration);
            foreach(var w in s.minimap.workshops)Disable(w);
            foreach(var u in s.runStats.stations)Disable(u);
            h.CollectionCancelled+=OnCancelled;
            yield return Fresh();
            Check("saved harpoon keeps authoritative range and cadence",h.enabled&&h.pickups==s.pickups&&h.session==s&&h.collectionRadius==8&&h.harpoonLaunchSpeed==36&&h.harpoonRetractSpeed==30&&Mathf.Abs(h.delayBetweenCollections-.08f)<.0001f,
                "range="+h.collectionRadius+"; launch="+h.harpoonLaunchSpeed+"; pull="+h.harpoonRetractSpeed+"; delay="+h.delayBetweenCollections);

            yield return DropReady();observeMoving=true;yield return Phase(HarpoonState.Launching);
            Vector3 began=s.vehicle.Body.position;
            float deadline=Time.realtimeSinceStartup+5;
            while(Time.realtimeSinceStartup<deadline&&s.Scrap<1)
            {
                s.input.testDirection=(h.State==HarpoonState.Retracting?Vector2.left:Vector2.right)*.3f;
                yield return null;
            }
            observeMoving=false;s.input.testDirection=Vector2.zero;
            Check("moving and reversing preserves live cable and reserved gear endpoints",sawLaunch&&sawPull&&endpointError<.002f&&gearError<.002f&&Flat(s.vehicle.Body.position-began).magnitude>.03f,
                "launch="+sawLaunch+"; pull="+sawPull+"; cable error="+endpointError+"; gear error="+gearError+"; travel="+Flat(s.vehicle.Body.position-began).magnitude);
            Check("launch slack straightens under powered pull",sawSlack&&maxTension>.8f&&h.cable.positionCount>=3,"saw slack="+sawSlack+"; max tension="+maxTension+"; points="+h.cable.positionCount);
            Check("dock awards through the real pool exactly once",s.Scrap==1&&s.Kills==0&&s.pickups.StoredValue==0&&s.pickups.ReservedGearCount==0&&h.CollectionsCompleted==1&&h.State==HarpoonState.Idle&&!h.cable.enabled&&!h.projectile.gameObject.activeSelf,
                "Scrap="+s.Scrap+"; collections="+h.CollectionsCompleted+"; stored="+s.pickups.StoredValue);
            Check("one moving cycle voices launch latch and dock once then stops its winch",SingleAudioCycle()&&sawWinch,
                AudioDetail()+"; winch observed during pull="+sawWinch);

            foreach(var phase in new[]{HarpoonState.Launching,HarpoonState.Retracting})
            {
                yield return Fresh();yield return DropReady();yield return Phase(phase);
                var target=h.Target;Vector3 hook=h.HookPosition,gear=target.position;var positions=new Vector3[h.cable.positionCount];h.cable.GetPositions(positions);
                float tension=h.CableTension,kick=h.LaunchReaction;
                int launches=audio.HarpoonLaunchSoundsStarted,latches=audio.HarpoonLatchSoundsStarted,docks=audio.HarpoonDockSoundsStarted;
                s.SetPaused(true);yield return new WaitForSecondsRealtime(.25f);
                float cableError=positions.Select((p,i)=>Vector3.Distance(p,h.cable.GetPosition(i))).Max();
                Check("Pause freezes "+phase+" without releasing ownership",h.State==phase&&h.HasReservation&&!h.WinchActive&&s.pickups.ReservedGearCount==1&&h.Target==target&&
                    Vector3.Distance(hook,h.HookPosition)<.0001f&&Vector3.Distance(gear,target.position)<.0001f&&cableError<.0001f&&h.CableTension==tension&&h.LaunchReaction==kick&&
                    !audio.WinchPlaying&&audio.HarpoonLaunchSoundsStarted==launches&&audio.HarpoonLatchSoundsStarted==latches&&audio.HarpoonDockSoundsStarted==docks,
                    "state="+h.State+"; reservation="+h.HasReservation+"; cable drift="+cableError+"; "+AudioDetail());
                s.SetPaused(false);s.input.testInput=true;yield return Award(1);
                Check("Resume finishes held "+phase+" reward once",s.Scrap==1&&h.CollectionsCompleted==1&&s.pickups.StoredValue==0&&s.pickups.ReservedGearCount==0&&SingleAudioCycle(),"Scrap="+s.Scrap+"; collections="+h.CollectionsCompleted+"; "+AudioDetail());
            }
            yield return Fresh();yield return DropReady();yield return Phase(HarpoonState.Launching);
            var invalid=h.Target;int cancelled=cancellations;invalid.gameObject.SetActive(false);yield return null;yield return null;
            Check("inactive target cancels and releases its real reservation",Clean()&&s.Scrap==0&&s.pickups.StoredValue==1&&cancellations==cancelled+1,
                "reservations="+s.pickups.ReservedGearCount+"; stored="+s.pickups.StoredValue+"; cancellation delta="+(cancellations-cancelled)+"; "+AudioDetail());
            invalid.gameObject.SetActive(true);yield return Award(1);
            Check("released inactive gear can later be retrieved without duplication",s.Scrap==1&&h.CollectionsCompleted==1&&s.pickups.StoredValue==0,"Scrap="+s.Scrap+"; collections="+h.CollectionsCompleted);

            yield return Fresh();
            // The first drop lands at the player's contact site; the other remains a harpoon candidate.
            Vector3 contactTravel=Quaternion.Euler(0,28,0)*Vector3.forward*1.3f;
            s.pickups.DropGear(s.vehicle.transform.position-contactTravel+Vector3.up*.4f,Vector3.zero,2);
            s.pickups.DropGear(s.vehicle.transform.position+s.vehicle.transform.forward*5+Vector3.up*.4f,Vector3.zero,3);
            yield return Award(5);yield return new WaitForSeconds(.2f);
            Check("contact and harpoon paths settle each gear value only once",s.Scrap==5&&s.Kills==0&&s.pickups.StoredValue==0&&s.pickups.ActiveCount==0&&s.pickups.ReservedGearCount==0&&h.CollectionsCompleted==1,
                "Scrap="+s.Scrap+"; collector docks="+h.CollectionsCompleted+"; active="+s.pickups.ActiveCount);

            yield return Fresh();yield return DropReady();yield return Phase(HarpoonState.Retracting);
            int pose=s.vehicle.PoseResetSerial;cancelled=cancellations;s.Recover();
            Check("Recover cancels hook and reservation before changing pose",s.vehicle.PoseResetSerial>pose&&Clean()&&s.pickups.StoredValue==1&&s.Scrap==0&&cancellations==cancelled+1,
                "pose serial="+pose+" -> "+s.vehicle.PoseResetSerial+"; stored="+s.pickups.StoredValue+"; reservations="+s.pickups.ReservedGearCount+"; "+AudioDetail());
            yield return Fresh();yield return DropReady();yield return Phase(HarpoonState.Launching);s.Restart();Quiet();
            Check("Restart synchronously clears loot and all retrieval feedback",Clean()&&h.LaunchReaction==0&&h.DockReaction==0&&h.CableTension==0&&h.CollectionsCompleted==0&&s.pickups.StoredValue==0&&s.Scrap==0,
                "state="+h.State+"; stored="+s.pickups.StoredValue+"; launch="+h.LaunchReaction+"; dock="+h.DockReaction);

            yield return Fresh();yield return DropReady();yield return Phase(HarpoonState.Retracting);s.health.Damage(s.health.Current+100,false);
            Check("Defeat releases a live pull without awarding it",s.State==SessionState.Defeated&&Clean()&&s.Scrap==0&&s.pickups.StoredValue==1,"state="+s.State+"; stored="+s.pickups.StoredValue+"; Scrap="+s.Scrap);
            yield return Fresh();h.enabled=false;
            s.pickups.DropGear(s.vehicle.transform.position+s.vehicle.transform.forward*5+Vector3.up*.4f,Vector3.zero,2);
            s.pickups.DropGear(s.vehicle.transform.position+s.vehicle.transform.forward*14+Vector3.up*.4f,Vector3.zero,3);
            yield return new WaitForSeconds(.7f);h.enabled=true;yield return Phase(HarpoonState.Retracting);s.CompleteArena();int settled=s.Scrap;s.CompleteArena();
            Check("completion preserves earned pool payout and cancels the hook",s.State==SessionState.Completed&&Clean()&&settled==5&&s.Scrap==5&&s.pickups.StoredValue==0&&s.pickups.ActiveCount==0,
                "first payout="+settled+"; after repeat completion="+s.Scrap+"; stored="+s.pickups.StoredValue);

            yield return Fresh();yield return DropReady();int run=s.RunSerial;
            reentrant=()=>{if(s.Scrap<=0)return;s.Changed-=reentrant;reentrant=null;s.Restart();Quiet();};s.Changed+=reentrant;
            float until=Time.realtimeSinceStartup+5;while(s.RunSerial==run&&Time.realtimeSinceStartup<until)yield return null;
            Check("Restart from the real award callback cannot leave an old dock",s.RunSerial==run+1&&s.Scrap==0&&Clean()&&h.CollectionsCompleted==0&&h.DockReaction==0&&h.LaunchReaction==0,
                "run="+run+" -> "+s.RunSerial+"; Scrap="+s.Scrap+"; docks="+h.CollectionsCompleted+"; dock reaction="+h.DockReaction);
        }
        IEnumerator Fresh()
        {
            Time.timeScale=1;s.Restart();Quiet();h.enabled=true;s.health.DamageEnabled=true;
            s.input.Enabled=true;s.input.testInput=true;s.input.testDirection=Vector2.zero;s.input.testBrake=false;
            yield return new WaitForSeconds(.45f);
        }
        IEnumerator DropReady()
        {
            h.enabled=false;s.pickups.DropGear(s.vehicle.transform.position+s.vehicle.transform.forward*5+Vector3.up*.4f,Vector3.zero,1);
            yield return new WaitForSeconds(.7f);h.enabled=true;
        }
        IEnumerator Phase(HarpoonState phase)
        {
            float until=Time.realtimeSinceStartup+4;while(h.State!=phase&&Time.realtimeSinceStartup<until)yield return null;
            if(h.State!=phase)throw new InvalidOperationException("Did not reach "+phase+"; state="+h.State+"; stored="+s.pickups.StoredValue+"; Scrap="+s.Scrap);
        }
        IEnumerator Award(int value){float until=Time.realtimeSinceStartup+6;while(s.Scrap<value&&Time.realtimeSinceStartup<until)yield return null;}
        bool Clean()=>h.State==HarpoonState.Idle&&!h.cable.enabled&&!h.projectile.gameObject.activeSelf&&!h.HasReservation&&!h.WinchActive&&s.pickups.ReservedGearCount==0&&audio&&!audio.WinchPlaying;
        bool SingleAudioCycle()=>audio.HarpoonLaunchSoundsStarted==1&&audio.HarpoonLatchSoundsStarted==1&&audio.HarpoonDockSoundsStarted==1&&!audio.WinchPlaying;
        string AudioDetail()=>"launch/latch/dock="+audio.HarpoonLaunchSoundsStarted+"/"+audio.HarpoonLatchSoundsStarted+"/"+audio.HarpoonDockSoundsStarted+"; winch="+audio.WinchPlaying;
        void Quiet(){foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();s.zone1.DespawnAll();}
        void Disable(Behaviour b){if(b&&!states.ContainsKey(b)){states[b]=b.enabled;b.enabled=false;}}
        void OnCancelled(){cancellations++;}
        void LateUpdate()
        {
            if(!observeMoving||!h||!h.cable.enabled)return;
            sawWinch|=h.State==HarpoonState.Retracting&&audio.WinchPlaying;
            int last=h.cable.positionCount-1;if(last<1)return;
            endpointError=Mathf.Max(endpointError,Vector3.Distance(h.cable.GetPosition(0),h.spawnPoint.position),Vector3.Distance(h.cable.GetPosition(last),h.projectile.position));
            sawLaunch|=h.State==HarpoonState.Launching;sawPull|=h.State==HarpoonState.Retracting;maxTension=Mathf.Max(maxTension,h.CableTension);
            for(int i=1;i<last;i++)
                if(Vector3.Distance(h.cable.GetPosition(i),Vector3.Lerp(h.spawnPoint.position,h.projectile.position,i/(float)last))>.02f)sawSlack=true;
            if(h.State==HarpoonState.Retracting&&h.Target)gearError=Mathf.Max(gearError,Vector3.Distance(h.Target.position,h.projectile.position));
            if(h.State==HarpoonState.Launching&&!launchCapture){Capture("harpoon-launch");launchCapture=true;}
            if(h.State==HarpoonState.Retracting&&!pullCapture){Capture("harpoon-powered-pull");pullCapture=true;}
        }
        void Check(string title,bool success,string detail){Checks++;if(!success)Failures++;lines.Add((success?"PASS ":"FAIL ")+title+": "+detail);File.WriteAllLines(ReportPath,lines);Debug.Log("HARPOON FEEL "+lines[lines.Count-1]);}
        static Vector3 Flat(Vector3 v)=>Vector3.ProjectOnPlane(v,Vector3.up);
        sealed class CanvasState{public Canvas canvas;public RenderMode mode;public Camera camera;public float plane;}
        void Capture(string name)
        {
            var camera=s.input.view;var target=RenderTexture.GetTemporary(540,960,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float aspect=camera.aspect;
            var image=new Texture2D(540,960,TextureFormat.RGB24,false);
            var canvases=Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>new CanvasState{canvas=c,mode=c.renderMode,camera=c.worldCamera,plane=c.planeDistance}).ToArray();
            try{camera.aspect=540f/960;camera.targetTexture=target;foreach(var c in canvases){c.canvas.renderMode=RenderMode.ScreenSpaceCamera;c.canvas.worldCamera=camera;c.canvas.planeDistance=Mathf.Max(camera.nearClipPlane+.5f,1);}
                Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;image.ReadPixels(new Rect(0,0,540,960),0,0);image.Apply();File.WriteAllBytes(Folder+"/Screenshots/"+name+".png",image.EncodeToPNG());}
            finally{foreach(var c in canvases)if(c.canvas){c.canvas.renderMode=c.mode;c.canvas.worldCamera=c.camera;c.canvas.planeDistance=c.plane;}camera.targetTexture=oldTarget;camera.aspect=aspect;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
        }
        void Restore()
        {
            if(restored)return;restored=true;if(!saved)return;if(reentrant!=null){s.Changed-=reentrant;reentrant=null;}h.CollectionCancelled-=OnCancelled;
            Time.timeScale=1;foreach(var pair in states)if(pair.Key)pair.Key.enabled=pair.Value;
            h.enabled=originalCollector;s.Restart();s.health.DamageEnabled=originalDamage;s.input.Enabled=originalInput;
            s.input.testInput=originalTestInput;s.input.testDirection=originalDirection;s.input.testBrake=originalTestBrake;
            Time.timeScale=originalTime;Application.targetFrameRate=originalFps;QualitySettings.vSyncCount=originalVsync;
        }
        void OnDestroy(){Restore();}
    }
}
#endif
