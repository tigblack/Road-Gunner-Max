#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace RoadGunner
{
    /// <summary>Focused saved-cannon regression. Administrative target placement is fixture setup; every bullet follows the production hitscan.</summary>
    [DefaultExecutionOrder(12200)]
    public sealed class CannonFeelPlayChecks : MonoBehaviour
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        public const string ReportPath=Folder+"/Cannon.txt";
        public bool Completed {get;private set;}
        public int Checks {get;private set;}
        public int Failures {get;private set;}
        GameSession s; TurretController gun; Raider human; CannonVisualRecoil visual;
        CombatTuning originalTuning,coverTuning;
        Keyboard keyboard,oldKeyboard;
        InputSettings focus; InputSettings.EditorInputBehaviorInPlayMode oldFocus; InputSettings.BackgroundBehavior oldBackground;
        readonly Dictionary<Behaviour,bool> states=new Dictionary<Behaviour,bool>();
        readonly List<string> lines=new List<string>(); readonly List<CannonShot> shots=new List<CannonShot>(); readonly List<float> shotFrames=new List<float>();
        ArenaRewardCache cache; Vector3 cachePosition; Quaternion cacheRotation; bool cacheSaved;
        GameObject cover; Vector3[] sourceVertices; int[] sourceIndices; Material[] sourceMaterials;
        float originalTime,maxRecoil; int fps,vsync,invalidShots; bool originalDamage,saved,restored;
        bool trackReversal; Vector3 previousReversalAim; float reversalStepRatio,reversalTargetError;
        public void Begin(){Directory.CreateDirectory(Folder);StartCoroutine(Guarded());}
        IEnumerator Guarded()
        {
            lines.Add("Cannon presentation and hitscan; actual saved Arena1; Unity "+Application.unityVersion);
            lines.Add("Controlled regression: AI and unrelated collection isolated, actual saved Raider/cache moved for setup, one temporary cover collider, one temporary cloned tuning only to hold an allowed off-axis shot. Real TurretController acquisition/hitscan/effects and production keyboard driving. No shot/damage method injection, active driving teleport/velocity override, or scene/asset write. Not a natural full playthrough.");
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var part=stack.Peek();bool more=false;object next=null;try{more=part.MoveNext();if(more)next=part.Current;}catch(Exception e){Check("fixture exception",false,e.ToString());break;}if(!more){(stack.Pop()as IDisposable)?.Dispose();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;}
            while(stack.Count>0)(stack.Pop()as IDisposable)?.Dispose();
            Restore();lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; no saved scene/assets modified.");File.WriteAllLines(ReportPath,lines);Completed=true;
        }
        IEnumerator Run()
        {
            yield return null;yield return null;
            s=GetComponent<GameSession>();gun=s.turret;visual=gun.CannonVisual;
            originalTime=Time.timeScale;fps=Application.targetFrameRate;vsync=QualitySettings.vSyncCount;originalDamage=s.health.DamageEnabled;originalTuning=gun.tuning;saved=true;
            s.Restart();QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
            human=s.spawns.authoredRaiders.First(r=>r&&!r.arenaBoss);
            cache=s.exploration.caches.First(c=>c&&!c.proximityOnly);cachePosition=cache.transform.position;cacheRotation=cache.transform.rotation;cacheSaved=true;
            Disable(s.spawns);Disable(s.respawns);Disable(s.turret);Disable(s.exploration);Disable(s.vehicle.GetComponent<HarpoonCollector>());
            foreach(var station in s.runStats.stations)Disable(station);foreach(var workshop in s.minimap.workshops)Disable(workshop);
            QuietRoster();s.health.DamageEnabled=false;s.input.testInput=false;s.input.Clear();
            focus=InputSystem.settings;oldFocus=focus.editorInputBehaviorInPlayMode;oldBackground=focus.backgroundBehavior;
            focus.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;focus.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            oldKeyboard=Keyboard.current;keyboard=InputSystem.AddDevice<Keyboard>("Cannon acceptance keyboard");keyboard.MakeCurrent();
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            if(!visual||!visual.OriginalMesh||!visual.RuntimeMesh)throw new InvalidOperationException("Saved readable cannon did not bind to runtime barrel recoil.");
            sourceVertices=visual.OriginalMesh.vertices;sourceIndices=visual.OriginalMesh.triangles;sourceMaterials=visual.BarrelSource.GetComponent<Renderer>().sharedMaterials;
            gun.ShotResolved+=OnShot;
            Check("saved balance is unchanged",Mathf.Abs(gun.Range-10)<.001f&&Mathf.Abs(gun.tuning.shotsPerSecond-5)<.001f&&Mathf.Abs(s.PlayerDamage(gun.tuning.bulletDamage)-5.25f)<.001f,"range="+gun.Range+"; Hz="+gun.tuning.shotsPerSecond+"; damage="+s.PlayerDamage(gun.tuning.bulletDamage));
            Check("only the readable barrel has a private runtime mesh",visual.RuntimeMesh!=visual.OriginalMesh&&visual.RuntimeMesh.hideFlags==HideFlags.DontSave&&visual.MovingVertexCount>0&&visual.MovingVertexCount<visual.RuntimeMesh.vertexCount,"vertices="+visual.RuntimeMesh.vertexCount+"; moving="+visual.MovingVertexCount);
            yield return PrepareTarget();
            float before=human.Health;int healthEvents=0;Action onHealth=()=>healthEvents++;human.HealthChanged+=onHealth;
            ClearShotRecords();gun.enabled=true;yield return AwaitShots(5,3);
            human.HealthChanged-=onHealth;gun.enabled=false;
            Check("stationary cannon resolves five real target hits",shots.Count==5&&shots.All(q=>q.HitTarget==human&&q.AppliedDamage>0),"shots="+shots.Count+"; target hits="+shots.Count(q=>q.HitTarget==human&&q.AppliedDamage>0));
            Check("one damage event per bullet at original damage",shots.Count==5&&healthEvents==5&&Mathf.Abs(before-human.Health-26.25f)<.01f&&shots.All(q=>Mathf.Abs(q.AppliedDamage-5.25f)<.001f),"health events="+healthEvents+"; HP delta="+(before-human.Health));
            bool cadence=shots.Count==5;
            for(int i=1;i<shots.Count;i++){float interval=shots[i].Time-shots[i-1].Time;cadence&=interval>=.2f-.0001f&&interval<=.2f+shotFrames[i]+.003f;}
            Check("cadence remains .2 seconds within one render update",cadence,"intervals="+string.Join(",",shots.Skip(1).Select((q,i)=>(q.Time-shots[i].Time).ToString("F4"))));
            Check("each shot shares actual muzzle flash tracer and impact ray",invalidShots==0,"invalid snapshots="+invalidShots);
            yield return PrepareTarget();ClearShotRecords();gun.enabled=true;yield return AwaitShots(1,2);
            Vector3 movementStart=s.vehicle.Body.position;yield return Keys(Key.A);int initial=shots.Count;
            float until=Time.time+.43f;while(Time.time<until)yield return null;
            yield return Keys();gun.enabled=false;
            Check("cannon fires while actual walker moves",Vector3.Distance(s.vehicle.Body.position,movementStart)>2&&shots.Count-initial>=2&&shots.Skip(initial).All(q=>q.HitTarget==human&&q.AppliedDamage>0),"travel="+Vector3.Distance(s.vehicle.Body.position,movementStart)+"; moving shots="+(shots.Count-initial)+"; hits="+shots.Skip(initial).Count(q=>q.HitTarget==human&&q.AppliedDamage>0));
            Check("moving shots keep one consistent event pose",invalidShots==0,"invalid snapshots="+invalidShots);
            yield return PrepareTarget();ClearShotRecords();gun.enabled=true;yield return AwaitShots(1,2);
            Vector3 beforeReverse=Vector3.ProjectOnPlane(s.vehicle.Body.rotation*Vector3.forward,Vector3.up).normalized;
            Vector3 heldAim=gun.muzzle.forward;int beforeReverseShots=shots.Count;
            previousReversalAim=heldAim;reversalStepRatio=reversalTargetError=0;trackReversal=true;
            yield return Keys(Key.S);yield return new WaitForFixedUpdate();
            bool reversed=Vector3.Dot(Vector3.ProjectOnPlane(s.vehicle.Body.rotation*Vector3.forward,Vector3.up).normalized,beforeReverse)<-.98f;
            yield return new WaitForSeconds(.08f);yield return Keys();yield return AwaitShots(2,2);gun.enabled=false;
            trackReversal=false;
            float finalTargetError=Vector3.Angle(gun.muzzle.forward,human.AimPoint-gun.muzzle.position);
            Check("180-degree movement reversal keeps target-relative aim and next real hit",reversed&&shots.Count>beforeReverseShots&&shots.Skip(beforeReverseShots).All(q=>q.HitTarget==human&&q.AppliedDamage>0&&Vector3.Angle(q.Direction,human.AimPoint-q.Origin)<=gun.tuning.aimTolerance+.001f)&&finalTargetError<=gun.tuning.aimTolerance+.001f&&reversalStepRatio<=1.05f,
                "body reversed="+reversed+"; subsequent hits="+shots.Skip(beforeReverseShots).Count(q=>q.HitTarget==human&&q.AppliedDamage>0)+"; final target angular error="+finalTargetError+"; largest transition target error="+reversalTargetError+"; mount step/budget ratio="+reversalStepRatio+"; legitimate changed world aim="+Vector3.Angle(heldAim,gun.muzzle.forward));
            Check("reversal shots retain the exact final muzzle event pose",invalidShots==0,"invalid snapshots="+invalidShots);
            yield return PrepareTarget();ClearShotRecords();
            var other=s.spawns.authoredRaiders.First(r=>r&&r!=human&&!r.arenaBoss);
            Vector3 forwardTarget=s.input.ScreenToWorldDirection(Vector2.up);
            Vector3 secondPoint=s.vehicle.Body.position+Quaternion.AngleAxis(150,Vector3.up)*forwardTarget*9.4f;
            if(!NavMesh.SamplePosition(secondPoint,out var secondSite,1,NavMesh.AllAreas))throw new InvalidOperationException("No real NavMesh for opposite-side live retarget fixture.");
            other.Spawn(secondSite.position,-1,1);other.AIEnabled=false;Physics.SyncTransforms();
            bool bothTargets=gun.CanAttack(human)&&gun.CanAttack(other);int killsBefore=s.Kills;float diedAt=-1;
            gun.enabled=true;float retargetDeadline=Time.realtimeSinceStartup+6;
            while(shots.Count(q=>q.HitTarget==other)<2&&Time.realtimeSinceStartup<retargetDeadline)
            {yield return null;if(!human.Alive&&diedAt<0)diedAt=Time.time;}
            gun.enabled=false;var newTargetShots=shots.Where(q=>q.HitTarget==other).ToArray();
            Check("two live enemies retarget across the chassis after a normal gun kill",bothTargets&&!human.Alive&&other.Alive&&s.Kills==killsBefore+1&&newTargetShots.Length>=2&&newTargetShots.All(q=>q.AppliedDamage>0)&&diedAt>=0&&newTargetShots[0].Time-diedAt<1.3f,
                "both initially visible="+bothTargets+"; normal kills="+(s.Kills-killsBefore)+"; second target hits="+newTargetShots.Length+"; retarget seconds="+(newTargetShots.Length>0?newTargetShots[0].Time-diedAt:-1));
            Check("retargeted shots resolve one valid muzzle-to-impact ray",invalidShots==0,"invalid snapshots="+invalidShots);
            yield return PrepareTarget();
            Vector3 ray=human.AimPoint-gun.muzzle.position;cover=new GameObject("Temporary cannon LOS cover");var obstacle=cover.AddComponent<BoxCollider>();cover.transform.position=gun.muzzle.position+ray*.5f;obstacle.size=new Vector3(1.4f,2,1.4f);Physics.SyncTransforms();
            int count=gun.ShotsFired;float hp=human.Health;gun.enabled=true;yield return new WaitForSeconds(.7f);
            Check("cover blocks acquisition and damage",!gun.CanAttack(human)&&gun.ShotsFired==count&&Mathf.Abs(human.Health-hp)<.001f,"shots added="+(gun.ShotsFired-count)+"; HP="+human.Health);
            gun.enabled=false;Destroy(cover);cover=null;yield return null;
            // Preserve the existing forward ray rather than silently redirecting every allowed shot to AimPoint.
            yield return PrepareTarget();coverTuning=Instantiate(originalTuning);coverTuning.yawSpeed=0;gun.tuning=coverTuning;
            Vector3 aim=human.AimPoint-gun.muzzle.position,flat=Vector3.ProjectOnPlane(aim,Vector3.up).normalized;
            gun.yaw.rotation=Quaternion.LookRotation(Quaternion.AngleAxis(4,Vector3.up)*flat);
            var local=gun.yaw.InverseTransformDirection(human.AimPoint-gun.pitch.position);gun.pitch.localRotation=Quaternion.Euler(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,0,0);
            cover=new GameObject("Temporary off-axis first impact cover");obstacle=cover.AddComponent<BoxCollider>();obstacle.size=Vector3.one*.16f;cover.transform.position=gun.muzzle.position+gun.muzzle.forward*4;Physics.SyncTransforms();
            bool targetVisible=gun.CanAttack(human);hp=human.Health;ClearShotRecords();gun.ResetCombat();gun.enabled=true;yield return AwaitShots(1,2);gun.enabled=false;
            var shot=shots.Count>0?shots[0]:default;
            Check("first cover impact stays authoritative on an allowed off-axis shot",targetVisible&&shots.Count==1&&shot.HitCollider==obstacle&&!shot.HitTarget&&shot.AppliedDamage==0&&Mathf.Abs(human.Health-hp)<.001f,"target LOS clear="+targetVisible+"; hit="+(shot.HitCollider?shot.HitCollider.name:"none")+"; HP delta="+(hp-human.Health));
            Check("cover flash and tracer use resolved first impact",invalidShots==0&&shot.HitCollider&&Vector3.Cross(shot.End-shot.Origin,shot.Direction).magnitude<.001f,"invalid snapshots="+invalidShots+"; impact="+shot.End.ToString("F3"));
            Destroy(cover);cover=null;gun.tuning=originalTuning;Destroy(coverTuning);coverTuning=null;yield return null;
            yield return PrepareTarget();s.exploration.enabled=true;
            Vector3 cameraRight=s.input.ScreenToWorldDirection(Vector2.right);cache.transform.position=s.vehicle.Body.position+cameraRight*4;cache.transform.rotation=Quaternion.identity;cache.ResetCache();Physics.SyncTransforms();
            float cacheHealth=cache.CurrentHealth;bool both=gun.CanAttack(human)&&gun.CanAttack(cache);ClearShotRecords();gun.enabled=true;yield return AwaitShots(3,3);gun.enabled=false;
            Check("eligible enemy stays ahead of nearer supply cache",both&&shots.Count==3&&shots.All(q=>q.HitTarget==human)&&Mathf.Abs(cache.CurrentHealth-cacheHealth)<.001f,"both attackable="+both+"; enemy hits="+shots.Count(q=>q.HitTarget==human)+"; cache HP="+cache.CurrentHealth+" / "+cacheHealth);
            s.exploration.enabled=false;cache.transform.SetPositionAndRotation(cachePosition,cacheRotation);Physics.SyncTransforms();
            yield return PrepareTarget();ClearShotRecords();maxRecoil=0;gun.enabled=true;yield return AwaitShots(7,4);human.Despawn();
            yield return new WaitForSeconds(.3f);
            Check("continuous fire has bounded travel and full return",maxRecoil>.02f&&maxRecoil<=originalTuning.cannonRecoilMeters+.0001f&&visual.RecoilMeters<.0001f,"peak metres="+maxRecoil+"; limit="+originalTuning.cannonRecoilMeters+"; resting="+visual.RecoilMeters);
            Check("shared source vertices indices and materials remain immutable",sourceVertices.SequenceEqual(visual.OriginalMesh.vertices)&&sourceIndices.SequenceEqual(visual.OriginalMesh.triangles)&&sourceMaterials.SequenceEqual(visual.BarrelSource.GetComponent<Renderer>().sharedMaterials),"source vertices="+sourceVertices.Length+"; runtime clone="+(visual.RuntimeMesh!=visual.OriginalMesh));
            gun.enabled=false;yield return PrepareTarget();ClearShotRecords();gun.enabled=true;yield return AwaitShots(1,2);
            float pauseDeadline=Time.time+.1f;while(visual.RecoilMeters<.02f&&Time.time<pauseDeadline)yield return null;
            s.SetPaused(true);float held=visual.RecoilMeters;int heldShots=gun.ShotsFired;yield return new WaitForSecondsRealtime(.15f);
            Check("pause freezes the shot and cosmetic recoil",held>.01f&&Mathf.Abs(visual.RecoilMeters-held)<.000001f&&gun.ShotsFired==heldShots,"held metres="+held+"; current="+visual.RecoilMeters+"; added shots="+(gun.ShotsFired-heldShots));
            s.SetPaused(false);human.Despawn();yield return new WaitForSeconds(.3f);
            Check("resume lets a held recoil settle",visual.RecoilMeters<.0001f,"resting metres="+visual.RecoilMeters);
            s.Restart();QuietRoster();s.health.DamageEnabled=false;gun.enabled=false;yield return null;
            Check("restart leaves no recoil or stale target",visual.RecoilMeters<.0001f&&!gun.Target&&s.Kills==0&&s.Scrap==0&&s.health.Current==100,"recoil="+visual.RecoilMeters+"; target="+(gun.Target?gun.Target.name:"none")+"; ledger="+s.Kills+" / "+s.Scrap);
        }
        void LateUpdate()
        {
            if(visual)maxRecoil=Mathf.Max(maxRecoil,visual.RecoilMeters);
            if(trackReversal&&gun&&human)
            {
                var direction=gun.muzzle.forward;
                // Sample after the production cannon at12100. The mount may follow its moved shoulder,
                // but must not inherit the body's instantaneous180-degree yaw.
                float budget=Mathf.Sqrt(2)*gun.tuning.yawSpeed*Time.deltaTime+.5f;
                reversalStepRatio=Mathf.Max(reversalStepRatio,Vector3.Angle(previousReversalAim,direction)/Mathf.Max(.5f,budget));
                reversalTargetError=Mathf.Max(reversalTargetError,Vector3.Angle(direction,human.AimPoint-gun.muzzle.position));
                previousReversalAim=direction;
            }
        }
        void OnShot(CannonShot q)
        {
            shots.Add(q);shotFrames.Add(Time.deltaTime);
            bool tracer=s.effects.tracers.Any(t=>t&&t.enabled&&Vector3.Distance(t.GetPosition(0),q.Origin)<.001f&&Vector3.Distance(t.GetPosition(1),q.End)<.001f);
            bool pose=Vector3.Distance(gun.muzzle.position,q.Origin)<.001f&&Vector3.Dot(gun.muzzle.forward,q.Direction)>.99999f&&Vector3.Distance(s.effects.flash.position,q.Origin)<.001f&&Vector3.Cross(q.End-q.Origin,q.Direction).magnitude<.001f;
            // A real lethal Hit disables its collider before this presentation callback; its recorded hit remains authoritative.
            bool first=!q.HitCollider||!q.HitCollider.enabled||q.HitCollider.Raycast(new Ray(q.Origin,q.Direction),out var hit,Vector3.Distance(q.Origin,q.End)+.03f)&&Vector3.Distance(hit.point,q.End)<.002f;
            if(!tracer||!pose||!first)invalidShots++;
        }
        void ClearShotRecords(){shots.Clear();shotFrames.Clear();invalidShots=0;}
        IEnumerator Keys(params Key[] keys){keyboard.MakeCurrent();InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));yield return null;}
        IEnumerator AwaitShots(int expected,float timeout){float end=Time.realtimeSinceStartup+timeout;while(shots.Count<expected&&Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator PrepareTarget()
        {
            gun.enabled=false;gun.tuning=originalTuning;s.exploration.enabled=false;QuietRoster();yield return Keys();s.input.Clear();
            s.vehicle.ResetPose(s.startPosition,s.startRotation);Physics.SyncTransforms();float start=Time.fixedTime,until=Time.realtimeSinceStartup+3;int stable=0;
            do{yield return new WaitForFixedUpdate();stable=s.vehicle.WalkerGrounded&&Mathf.Abs(s.vehicle.Body.linearVelocity.y)<.15f?stable+1:0;}while((Time.fixedTime-start<.6f||stable<8)&&Time.realtimeSinceStartup<until);
            if(stable<8)throw new InvalidOperationException("Player did not settle before cannon fixture.");
            Vector3 forward=s.input.ScreenToWorldDirection(Vector2.up);Vector3 point=s.vehicle.Body.position+forward*8;
            if(!NavMesh.SamplePosition(point,out var site,1.5f,NavMesh.AllAreas))throw new InvalidOperationException("No real NavMesh eight metres ahead of saved start.");
            human.Spawn(site.position,-1,0);human.AIEnabled=false;
            gun.yaw.rotation=Quaternion.LookRotation(forward);var local=gun.yaw.InverseTransformDirection(human.AimPoint-gun.pitch.position);gun.pitch.localRotation=Quaternion.Euler(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,0,0);
            gun.ResetCombat();Physics.SyncTransforms();
        }
        void QuietRoster(){foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();s.zone1.DespawnAll();}
        void Disable(Behaviour b){if(b&&!states.ContainsKey(b)){states[b]=b.enabled;b.enabled=false;}}
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);File.WriteAllLines(ReportPath,lines);Debug.Log("CANNON FEEL "+lines[lines.Count-1]);}
        void Restore()
        {
            if(restored)return;restored=true;if(gun)gun.ShotResolved-=OnShot;
            if(cover)Destroy(cover);if(coverTuning)Destroy(coverTuning);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();
            if(focus){focus.backgroundBehavior=oldBackground;focus.editorInputBehaviorInPlayMode=oldFocus;focus=null;}
            if(!saved)return;Time.timeScale=1;
            if(cacheSaved&&cache)cache.transform.SetPositionAndRotation(cachePosition,cacheRotation);
            foreach(var state in states)if(state.Key)state.Key.enabled=state.Value;
            if(s){gun.tuning=originalTuning;s.input.testInput=false;s.input.Clear();s.health.DamageEnabled=originalDamage;s.Restart();}
            Time.timeScale=originalTime;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;
        }
        void OnDestroy(){Restore();}
    }
}
#endif
