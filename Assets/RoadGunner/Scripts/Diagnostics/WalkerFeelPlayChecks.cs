#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Actual motion/contact/presentation integration, with disclosed temporary terrain fixtures.</summary>
    public sealed class WalkerFeelPlayChecks : MonoBehaviour
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        public const string ReportPath=Folder+"/ContactFeel.txt";
        public bool Completed {get;private set;}
        public int Checks {get;private set;}
        public int Failures {get;private set;}
        GameSession s;VehicleController car;PlayerTrackVisuals rig;TireTracks prints;ArcadeAudio audio;
        readonly List<string> lines=new List<string>();readonly Dictionary<Behaviour,bool> enabledBefore=new Dictionary<Behaviour,bool>();
        readonly List<GameObject> fixtures=new List<GameObject>();
        bool saved,restored,damage;float scale;int fps,vsync;
        public void Begin(){Directory.CreateDirectory(Folder);StartCoroutine(Guarded());}
        IEnumerator Guarded()
        {
            lines.Add("Walker contact/gait/audio integration, actual saved scene; Unity "+Application.unityVersion);
            lines.Add("Movement uses production DriveInput test direction and Rigidbody physics. ResetPose/despawn isolate fixtures; temporary ramp and wall are explicitly test geometry, not saved level edits. Frame caps are requested render caps, not device-performance claims.");
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var r=stack.Peek();bool more=false;object value=null;try{more=r.MoveNext();if(more)value=r.Current;}catch(Exception e){Check("fixture exception",false,e.ToString());break;}if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(value is IEnumerator child)stack.Push(child);else yield return value;}
            while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Restore();lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; saved scene/assets not modified.");File.WriteAllLines(ReportPath,lines);Completed=true;
        }
        IEnumerator Run()
        {
            yield return null;yield return null;s=GetComponent<GameSession>();car=s.vehicle;rig=car.GetComponentInChildren<PlayerTrackVisuals>(true);prints=car.GetComponent<TireTracks>();audio=s.Feedback.GetComponentInChildren<ArcadeAudio>(true);
            scale=Time.timeScale;fps=Application.targetFrameRate;vsync=QualitySettings.vSyncCount;damage=s.health.DamageEnabled;saved=true;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;s.Restart();
            Disable(s.spawns);Disable(s.respawns);Disable(s.exploration);Disable(s.turret);Disable(car.GetComponent<HarpoonCollector>());
            foreach(var w in s.minimap.workshops)Disable(w);foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();s.zone1.DespawnAll();s.health.DamageEnabled=false;
            Check("saved balance retained",car.tuning.walkerDrive&&Mathf.Abs(car.tuning.maxSpeed-9.36f)<.001f&&s.health.Maximum==100&&Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage)-5.25f)<.001f&&s.turret.Range==10&&s.turret.tuning.shotsPerSecond==5,"max="+car.tuning.maxSpeed+"; HP="+s.health.Maximum);
            yield return ResetAt(s.startPosition,s.startRotation);prints.Clear();
            foreach(int cap in new[]{30,60,120})
            {
                Application.targetFrameRate=cap;yield return ResetAt(s.startPosition,s.startRotation);
                int before=prints.StampCount,beforeAudio=audio.FootfallsStarted,beforeDust=prints.FootDustBurstCount;int heldSamples=0,plantSequence=rig.PlantedFootCount;float stanceDrift=0;bool left=false,right=false;Vector3 lp=Vector3.zero,rp=Vector3.zero;
                Command(Vector2.up);float end=Time.time+.8f;int frames=0;float largestDt=0;
                while(Time.time<end)
                {
                    yield return null;frames++;largestDt=Mathf.Max(largestDt,Time.deltaTime);
                    if(plantSequence==rig.PlantedFootCount&&left&&rig.LeftFootInStance){stanceDrift=Mathf.Max(stanceDrift,Vector3.Distance(lp,rig.LeftFootWorldPosition));heldSamples++;}
                    if(plantSequence==rig.PlantedFootCount&&right&&rig.RightFootInStance){stanceDrift=Mathf.Max(stanceDrift,Vector3.Distance(rp,rig.RightFootWorldPosition));heldSamples++;}
                    left=rig.LeftFootInStance;right=rig.RightFootInStance;lp=rig.LeftFootWorldPosition;rp=rig.RightFootWorldPosition;
                    plantSequence=rig.PlantedFootCount;
                }
                Check("grounded drive at requested render cap "+cap,car.WalkerGrounded&&car.HorizontalSpeed>=car.tuning.maxSpeed*.98f,"speed="+car.HorizontalSpeed+"; observed frames="+frames+"; max render dt="+largestDt);
                Check("world stance anchors hold at cap "+cap,heldSamples>2&&stanceDrift<.09f,"held samples="+heldSamples+"; largest consecutive world drift="+stanceDrift);
                Command(Vector2.zero);yield return new WaitForFixedUpdate();yield return null;
                Check("same accepted plants produce audio dust and footprints at cap "+cap,prints.StampCount-before>=2&&prints.StampCount-before==audio.FootfallsStarted-beforeAudio&&prints.StampCount-before==prints.FootDustBurstCount-beforeDust,"prints="+(prints.StampCount-before)+"; sounds="+(audio.FootfallsStarted-beforeAudio)+"; dust bursts="+(prints.FootDustBurstCount-beforeDust));
                Check("walker tyre loop is completely silent at cap "+cap,audio.WalkerRollMuted,"roll playing="+(audio.roll&&audio.roll.isPlaying)+"; volume="+(audio.roll?audio.roll.volume:0));
                int idle=prints.StampCount,idleAudio=audio.FootfallsStarted;yield return new WaitForSeconds(.35f);
                Check("idle settles without new locomotion feedback at cap "+cap,car.HorizontalSpeed<.1f&&prints.StampCount==idle&&audio.FootfallsStarted==idleAudio&&rig.VisualReactionMagnitude<.08f,"speed="+car.HorizontalSpeed+"; reaction="+rig.VisualReactionMagnitude+"; added plants="+(prints.StampCount-idle));
            }
            Application.targetFrameRate=60;
            yield return ResetAt(s.startPosition,s.startRotation);Command(Vector2.up*.5f);yield return null;yield return new WaitForFixedUpdate();
            Check("partial stick preserves analog response",s.input.Magnitude>0&&s.input.Magnitude<1&&Mathf.Abs(car.HorizontalSpeed-car.tuning.maxSpeed*s.input.Magnitude)<.08f,"magnitude="+s.input.Magnitude+"; speed="+car.HorizontalSpeed);
            Command(Vector2.zero);yield return new WaitForFixedUpdate();
            int pausedPrints=prints.StampCount,pausedAudio=audio.FootfallsStarted;s.SetPaused(true);yield return new WaitForSecondsRealtime(.2f);
            Check("Pause freezes locomotion feedback",prints.StampCount==pausedPrints&&audio.FootfallsStarted==pausedAudio&&!audio.WinchPlaying,"prints="+prints.StampCount+"; audio="+audio.FootfallsStarted);s.SetPaused(false);
            s.Recover();int recoveredPrints=prints.StampCount,recoveredAudio=audio.FootfallsStarted;yield return new WaitForSeconds(.4f);
            Check("Recover does not invent steps",prints.StampCount==recoveredPrints&&audio.FootfallsStarted==recoveredAudio,"added prints="+(prints.StampCount-recoveredPrints)+"; sounds="+(audio.FootfallsStarted-recoveredAudio));
            yield return ResetAt(s.startPosition,s.startRotation);
            // A vertical wall beside a falling body cannot become supporting ground.
            var wall=Box("Temporary falling wall",s.startPosition+car.transform.right*2.25f+Vector3.up*5,new Vector3(.2f,12,12),s.startRotation);
            car.ResetPose(s.startPosition+Vector3.up*5,s.startRotation);Physics.SyncTransforms();Command(Vector2.right);int airPrint=prints.StampCount,airAudio=audio.FootfallsStarted;bool falseGround=false;float endAir=Time.time+.4f;
            while(Time.time<endAir){yield return new WaitForFixedUpdate();falseGround|=car.WalkerGrounded;}
            Check("brushing a vertical wall while falling cannot ground the walker",!falseGround&&car.Body.linearVelocity.y<-.5f&&prints.StampCount==airPrint&&audio.FootfallsStarted==airAudio,"false grounded="+falseGround+"; vertical="+car.Body.linearVelocity.y);
            Command(Vector2.zero);UnityEngine.Object.Destroy(wall);yield return null;
            int soundsBeforeLanding=audio.LandingSoundsStarted;float deadline=Time.time+3;
            while((!car.WalkerGrounded||car.LandingSerial==0)&&Time.time<deadline)yield return null;
            yield return new WaitForSeconds(.35f);
            Check("fall produces one valid scaled landing and recovery",car.WalkerGrounded&&car.LandingSerial==1&&car.LandingSpeed>1&&audio.LandingSoundsStarted==soundsBeforeLanding+1,"landing serial="+car.LandingSerial+"; speed="+car.LandingSpeed+"; sound delta="+(audio.LandingSoundsStarted-soundsBeforeLanding));
            Check("standing landing creates no locomotion footprints",prints.StampCount==airPrint&&audio.FootfallsStarted==airAudio,"added footprints="+(prints.StampCount-airPrint));
            // Walk up a real temporary collider slope and off its edge under gravity.
            var ramp=Box("Temporary traversable eight degree ramp",s.startPosition+Vector3.up*2.5f,new Vector3(14,.6f,18),s.startRotation*Quaternion.Euler(-8,0,0));
            yield return ResetAt(ramp.transform.TransformPoint(new Vector3(0,.3f,-5.5f))+Vector3.up*.72f,s.startRotation);
            Vector3 rampStart=car.Body.position;Command(WorldToScreen(s.startRotation*Vector3.forward));bool slopeSupport=false,airborne=false;float maxRise=0,minAirY=0;int landings=0;float until=Time.time+3.4f;
            while(Time.time<until){yield return new WaitForFixedUpdate();slopeSupport|=car.WalkerGrounded&&Vector3.Dot(car.WalkerSupportNormal,Vector3.up)<.998f;maxRise=Mathf.Max(maxRise,car.Body.position.y-rampStart.y);if(!car.WalkerGrounded){airborne=true;minAirY=Mathf.Min(minAirY,car.Body.linearVelocity.y);}landings=Mathf.Max(landings,car.LandingSerial);}
            Command(Vector2.zero);yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(.8f);landings=Mathf.Max(landings,car.LandingSerial);
            Check("traversable slope keeps real support without vertical snapping",slopeSupport&&maxRise>.4f,"sloped support="+slopeSupport+"; climb="+maxRise);
            Check("walking off a ledge keeps gravity and lands",airborne&&minAirY<-1&&landings>=1,"airborne="+airborne+"; min vertical velocity="+minAirY+"; landings="+landings+"; end grounded="+car.WalkerGrounded+"; end velocity="+car.Body.linearVelocity+"; end y="+car.Body.position.y);
            UnityEngine.Object.Destroy(ramp);yield return null;s.Restart();Command(Vector2.zero);yield return new WaitForSeconds(.5f);
            Check("Restart clears feedback and keeps ledger clean",prints.StampCount==0&&audio.FootfallsStarted==0&&s.Scrap==0&&s.Kills==0&&s.health.Current==100,"prints="+prints.StampCount+"; sounds="+audio.FootfallsStarted+"; scrap="+s.Scrap);
        }
        IEnumerator ResetAt(Vector3 p,Quaternion q){Command(Vector2.zero);car.ResetPose(p,q);Physics.SyncTransforms();yield return new WaitForSeconds(.8f);if(!car.WalkerGrounded)throw new InvalidOperationException("Fixture did not settle on real support at "+p);}
        void Command(Vector2 direction){s.input.Enabled=true;s.input.testInput=true;s.input.testDirection=direction;s.input.testBrake=false;}
        Vector2 WorldToScreen(Vector3 d){var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;var up=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;return new Vector2(Vector3.Dot(d,right),Vector3.Dot(d,up)).normalized;}
        GameObject Box(string name,Vector3 position,Vector3 size,Quaternion rotation){var g=new GameObject(name);g.transform.SetPositionAndRotation(position,rotation);g.AddComponent<BoxCollider>().size=size;fixtures.Add(g);return g;}
        void Disable(Behaviour b){if(b&&!enabledBefore.ContainsKey(b)){enabledBefore[b]=b.enabled;b.enabled=false;}}
        void Check(string title,bool pass,string details){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+title+": "+details);File.WriteAllLines(ReportPath,lines);Debug.Log("WALKER FEEL "+lines[lines.Count-1]);}
        void Restore(){if(restored)return;restored=true;foreach(var f in fixtures)if(f)UnityEngine.Object.Destroy(f);if(!saved)return;Time.timeScale=1;foreach(var p in enabledBefore)if(p.Key)p.Key.enabled=p.Value;if(s){s.input.testInput=false;s.input.testDirection=Vector2.zero;s.input.testBrake=false;s.health.DamageEnabled=damage;s.Restart();}Time.timeScale=scale;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;}
        void OnDestroy(){Restore();}
    }
}
#endif
