#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Input-only route through the actual saved roster. Failure/timeout is reported rather than manufacturing a win.</summary>
    public sealed class WalkerArenaRunPlayChecks : MonoBehaviour
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        public const string ReportPath=Folder+"/ArenaRun.txt";
        public bool Completed {get;private set;}
        public int Checks {get;private set;}
        public int Failures {get;private set;}
        readonly List<string> lines=new List<string>(),samples=new List<string>();
        GameSession s;HarpoonCollector harpoon;WalkerArenaRunObserver observer;
        bool restored,saved,originalInput,originalBrake,originalTurret,winning=true;
        Vector2 originalDirection;float originalTime,started,nextSample,travel;int originalFps,originalVsync;
        Vector3 previousPosition;string phase="setup";
        CombatTarget firstKilled;int firstLife;float firstKilledAt;float damage,repair;
        int rams,killsObserved,launches,reels,realRepairs;

        public void Begin(){Directory.CreateDirectory(Folder+"/Screenshots");StartCoroutine(Guarded());}
        IEnumerator Guarded()
        {
            lines.Add("Normal saved Arena 1 route — Unity "+Application.unityVersion);
            lines.Add("Winning attempt: administrative Restart at the ordinary start, then only DriveInput.testDirection through its normal Update/FixedUpdate. All saved enemies, AI, auto-gun, harpoon, rewards, damage and respawn stay enabled. No Hit/Damage/Collect/Drop/score calls, health injection, enemy despawn or pose reset during the winning attempt. A scripted input route is not a human playthrough; failure or timeout remains a failure.");
            samples.Add("game_seconds,phase,x,y,z,hp,max_hp,kills,scrap,earned,shots,ram_hits,harpoon_launches,harpoon_reels,pending_respawns");
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                var it=stack.Peek();bool more=false;object value=null;
                try{more=it.MoveNext();if(more)value=it.Current;}catch(Exception e){Check("run exception",false,e.ToString());break;}
                if(!more){(stack.Pop()as IDisposable)?.Dispose();continue;}
                if(value is IEnumerator child)stack.Push(child);else yield return value;
            }
            while(stack.Count>0)(stack.Pop()as IDisposable)?.Dispose();
            try{Restore();}catch(Exception e){Check("restore",false,e.ToString());}
            lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; no saved scene/assets modified.");Write();Completed=true;
        }
        IEnumerator Run()
        {
            yield return null;yield return null;s=GetComponent<GameSession>();if(!s||!s.Feedback)throw new InvalidOperationException("Actual saved GameSession/feedback required.");
            harpoon=s.vehicle.GetComponent<HarpoonCollector>();
            originalTime=Time.timeScale;originalFps=Application.targetFrameRate;originalVsync=QualitySettings.vSyncCount;
            originalInput=s.input.testInput;originalBrake=s.input.testBrake;originalDirection=s.input.testDirection;originalTurret=s.turret.enabled;saved=true;
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            observer=gameObject.AddComponent<WalkerArenaRunObserver>();observer.owner=this;s.Feedback.sinks.Add(observer);
            s.Restart();s.input.testInput=true;s.input.testDirection=Vector2.zero;started=Time.time;previousPosition=s.vehicle.Body.position;
            Check("ordinary saved roster and health are live",s.Playing&&s.health.DamageEnabled&&s.health.Current==s.health.Maximum&&s.health.Maximum==100&&
                s.spawns.authoredRaiders.Length==12&&s.spawns.authoredBuggies.Length==2&&s.zone1.ExtraTargets.Count(t=>t&&t.Alive)==2&&
                s.turret.enabled&&harpoon&&harpoon.enabled&&s.respawns&&s.respawns.enabled,"HP="+s.health.Current+"; initial enemies="+LiveEnemies()+"; no winning-run isolation");
            Capture("normal-start");
            // Saved crossroads source: W=2.5m, yaw45. Main south joins workshop loop and NW branch.
            yield return GoTo(World(new Vector2(0,-30)),15,"first-raiders");
            SetPhase("first-live-fight");Stop();float until=Time.time+16,realUntil=Time.realtimeSinceStartup+40;
            while(s.Playing&&s.spawns.authoredRaiders.Take(2).Any(r=>r.Alive)&&Time.time<until&&Time.realtimeSinceStartup<realUntil)
            {
                // Allow one real melee hit for the repair visit, then use ordinary movement to keep
                // the remaining attacker within gun range instead of standing inside its pitch limit.
                if(damage>=6)
                {
                    var target=s.spawns.authoredRaiders.Take(2).Where(r=>r.Alive).OrderBy(r=>Flat(r.transform.position-s.vehicle.Body.position).sqrMagnitude).FirstOrDefault();
                    if(target)
                    {
                        var radial=Flat(s.vehicle.Body.position-target.transform.position);float distance=radial.magnitude;
                        if(distance<.1f)radial=-s.vehicle.transform.forward;else radial/=distance;
                        Steer(Vector3.Cross(Vector3.up,radial)*.4f+radial*Mathf.Clamp((6.5f-distance)*.8f,-1,1),1);
                    }
                }
                yield return null;
            }
            Stop();
            Check("actual auto-gun defeats nearby saved Raiders",s.Playing&&s.Kills>=2&&s.turret.ShotsFired>0&&killsObserved>=2,Evidence());Capture("normal-first-fight");
            yield return PlayingWait(1.5f);
            foreach(var w in new[]{new Vector2(0,-26),new Vector2(12,-26),new Vector2(22,-25),new Vector2(22,-14)})yield return GoTo(World(w),12,"workshop-route");
            SetPhase("workshop");Stop();yield return PlayingWait(4);
            Check("actual enemy damage is repaired by the workshop",s.Playing&&damage>0&&repair>0&&realRepairs>0,"actual damage="+damage+"; workshop healing="+repair+"; active repair cues="+realRepairs+"; "+Evidence());Capture("normal-workshop");
            var firstEntry=firstKilled?s.respawns.Find(firstKilled):null;
            float remaining=firstEntry!=null?firstEntry.Remaining:-1;var paused=s.vehicle.Body.position;
            s.SetPaused(true);yield return new WaitForSecondsRealtime(.3f);
            Check("normal-run pause holds body and active countdown",s.State==SessionState.Paused&&Vector3.Distance(paused,s.vehicle.Body.position)<.001f&&
                (firstEntry==null||Mathf.Abs(firstEntry.Remaining-remaining)<.001f),"remaining="+remaining+" -> "+(firstEntry!=null?firstEntry.Remaining:-1));
            s.SetPaused(false);s.input.testInput=true;
            yield return GoTo(World(new Vector2(26,-7)),12,"east-ram-approach");
            SetPhase("actual-ram");until=Time.time+4;realUntil=Time.realtimeSinceStartup+12;
            while(s.Playing&&rams==0&&Time.time<until&&Time.realtimeSinceStartup<realUntil)
            {
                var target=s.spawns.authoredRaiders.Skip(7).Take(2).Where(r=>r.Alive).OrderBy(r=>Flat(r.transform.position-s.vehicle.Body.position).sqrMagnitude).FirstOrDefault();
                if(!target)break;Steer(target.transform.position-s.vehicle.Body.position,1);yield return null;
            }
            Stop();Check("real movement produces a hostile ram hit",rams>0,"observed production EnemyHit(ram=true)="+rams+"; "+Evidence());
            yield return GoTo(World(new Vector2(22,-14)),15,"return-workshop");SetPhase("workshop");Stop();yield return PlayingWait(3);
            SetPhase("wait-real-return");until=Time.time+40;realUntil=Time.realtimeSinceStartup+90;
            while(s.Playing&&firstEntry!=null&&firstEntry.RespawnCount==0&&Time.time<until&&Time.realtimeSinceStartup<realUntil)yield return null;
            Check("a naturally killed actor returns after its real countdown",firstEntry!=null&&firstEntry.RespawnCount>0&&firstKilled.Alive&&Life(firstKilled)>firstLife&&Time.time-firstKilledAt>=29.9f,
                "first="+(firstKilled?firstKilled.name:"none")+"; elapsed="+(Time.time-firstKilledAt)+"; life="+(firstKilled?Life(firstKilled):0)+"; returns="+(firstEntry!=null?firstEntry.RespawnCount:0)+"; "+Evidence());
            foreach(var w in new[]{new Vector2(22,-3),new Vector2(22,8),new Vector2(0,8),new Vector2(-25,8)})yield return GoTo(World(w),16,"northwest-route");
            var attack=s.runStats.stations.First(t=>t.kind==ArenaUpgradeKind.Attack);
            yield return GoTo(attack.transform.position,12,"actual-upgrade");Stop();yield return PlayingWait(.25f);
            Check("ordinary approach claims the run Attack station",s.runStats.AttackLevel==2&&attack.Consumed,"ATK="+s.runStats.AttackLevel+"; shot="+s.PlayerDamage(s.turret.tuning.bulletDamage));
            // Return from the off-road station along its already-traversed access before taking
            // the authored NW branch; the direct station-to-boss chord crosses solid scenery.
            yield return GoTo(World(new Vector2(-25,8)),12,"warlord-road-return");
            yield return GoTo(World(new Vector2(-30,8)),8,"warlord-branch-entry");
            yield return GoTo(World(new Vector2(-40,15)),15,"warlord-approach");
            var boss=s.zone1.ExtraTargets.OfType<Raider>().Single(r=>r.arenaBoss);
            SetPhase("warlord-live-orbit");until=Time.time+90;realUntil=Time.realtimeSinceStartup+210;
            while(s.Playing&&boss.Alive&&Time.time<until&&Time.realtimeSinceStartup<realUntil)
            {
                var radial=Flat(s.vehicle.Body.position-boss.transform.position);float distance=radial.magnitude;
                if(distance<.1f)radial=-s.vehicle.transform.forward;else radial/=distance;
                // Enter shared10m range, then orbit outside the committed melee warning with normal directional input.
                Vector3 wish=Vector3.Cross(Vector3.up,radial)*.65f+radial*Mathf.Clamp((8.8f-distance)*.8f,-1,1);
                Steer(wish,1);yield return null;
            }
            Stop();Capture("normal-warlord-outcome");
            Check("normal input run defeats Warlord and completes",s.State==SessionState.Completed&&!boss.Alive&&s.Kills>=3,Evidence()+"; bossHP="+boss.Health);
            Check("completion settles every earned drop once",s.State==SessionState.Completed&&s.Scrap==s.ScrapEarned&&s.pickups.StoredValue==0&&s.pickups.ReservedGearCount==0&&
                s.respawns.PendingCount==0&&(!harpoon||!harpoon.HasReservation),"collected="+s.Scrap+"; earned="+s.ScrapEarned+"; stored="+s.pickups.StoredValue+"; reserved="+s.pickups.ReservedGearCount);
            Check("winning route uses automatic harpoon while retaining live systems",launches>0&&reels>0&&harpoon.CollectionsCompleted>0&&s.spawns.enabled&&s.exploration.enabled,
                "launches="+launches+"; reels="+reels+"; collections="+harpoon.CollectionsCompleted+"; travelled="+travel.ToString("F1")+"m");
            winning=false;s.Restart();s.input.testInput=true;
            Check("Restart clears normal run loot feedback and progression",s.Playing&&s.Kills==0&&s.Scrap==0&&s.ScrapEarned==0&&s.health.Current==s.health.Maximum&&s.runStats.AttackLevel==1&&
                s.exploration.RewardSerial==0&&s.respawns.PendingCount==0&&(!harpoon.HasReservation),Evidence());
            // Separate passive defeat fixture, explicitly disclosed: the gun is disabled so real enemies can kill the player.
            lines.Add("SEPARATE PASSIVE DEFEAT FIXTURE: after Restart only the auto-gun is disabled; actual AI, health damage and movement remain production. No Damage/Hit injection.");s.turret.enabled=false;
            yield return GoTo(World(new Vector2(0,-30)),15,"passive-defeat-approach");Stop();SetPhase("passive-real-defeat");until=Time.time+65;realUntil=Time.realtimeSinceStartup+150;
            while(s.Playing&&Time.time<until&&Time.realtimeSinceStartup<realUntil)yield return null;
            Check("actual enemy attacks produce defeat without health injection",s.State==SessionState.Defeated&&s.health.Current==0&&!s.input.Enabled&&!s.vehicle.CanDrive,Evidence());Capture("normal-passive-defeat");
            s.turret.enabled=originalTurret;s.Restart();
            Check("Restart after actual defeat restores a fresh arena",s.Playing&&s.health.Current==s.health.Maximum&&s.Kills==0&&s.Scrap==0&&s.pickups.StoredValue==0&&LiveEnemies()==16,Evidence());
        }
        IEnumerator GoTo(Vector3 target,float budget,string label)
        {
            SetPhase(label);float until=Time.time+budget,realUntil=Time.realtimeSinceStartup+budget*3;
            while(s.Playing&&Flat(target-s.vehicle.Body.position).magnitude>1.3f&&Time.time<until&&Time.realtimeSinceStartup<realUntil)
            {var delta=Flat(target-s.vehicle.Body.position);Steer(delta,Mathf.Clamp01(delta.magnitude/3));yield return null;}
            Stop();Check("input route "+label,s.Playing&&Flat(target-s.vehicle.Body.position).magnitude<=1.3f,"remaining="+Flat(target-s.vehicle.Body.position).magnitude.ToString("F2")+"m; "+Evidence());
        }
        IEnumerator PlayingWait(float seconds)
        {float until=Time.time+seconds,realUntil=Time.realtimeSinceStartup+seconds*3+1;while(s.Playing&&Time.time<until&&Time.realtimeSinceStartup<realUntil)yield return null;}
        void Steer(Vector3 world,float magnitude)
        {
            var right=Flat(s.input.view.transform.right).normalized;var up=Flat(s.input.view.transform.forward).normalized;world=Flat(world).normalized;
            s.input.testDirection=new Vector2(Vector3.Dot(world,right),Vector3.Dot(world,up))*Mathf.Clamp01(magnitude);
        }
        void Stop(){if(s)s.input.testDirection=Vector2.zero;}
        void SetPhase(string value){phase=value;lines.Add("PHASE "+value+" t="+(s?Time.time-started:0).ToString("F2"));Write();}
        void Update()
        {
            if(!s||Completed)return;var p=s.vehicle.Body.position;travel+=Flat(p-previousPosition).magnitude;previousPosition=p;
            if(Time.realtimeSinceStartup<nextSample)return;nextSample=Time.realtimeSinceStartup+.5f;
            samples.Add(string.Join(",",N(Time.time-started),phase,N(p.x),N(p.y),N(p.z),N(s.health.Current),N(s.health.Maximum),s.Kills,s.Scrap,s.ScrapEarned,s.turret.ShotsFired,rams,launches,reels,s.respawns.PendingCount));
        }
        internal void Killed(CombatTarget target)
        {if(!winning)return;killsObserved++;if(firstKilled)return;firstKilled=target;firstLife=Life(target);firstKilledAt=Time.time;}
        internal void Hit(bool ram){if(winning&&ram)rams++;}
        internal void Damaged(float amount){if(winning)damage+=amount;}
        internal void Healed(float amount)
        {
            // The automatic bay starts healing at its real radius, before GoTo reaches its centre.
            // Observe actual healing inside an enabled production zone, including that approach.
            if(winning&&s&&s.minimap&&s.minimap.workshops.Any(w=>w&&w.isActiveAndEnabled&&w.session==s&&w.Contains(s.vehicle.Body.position)))repair+=amount;
        }
        internal void Repair(bool active){if(winning&&active)realRepairs++;}
        internal void Launched(){if(winning)launches++;}
        internal void Reeled(){if(winning)reels++;}
        static int Life(CombatTarget t)=>t is Raider r?r.LifeSerial:t is EnemyBuggy b?b.LifeSerial:t is WastelandMutant m?m.LifeSerial:0;
        int LiveEnemies()=>s.spawns.authoredRaiders.Count(r=>r&&r.Alive)+s.spawns.authoredBuggies.Count(b=>b&&b.Alive)+s.zone1.ExtraTargets.Count(t=>t&&t.Alive);
        string Evidence()=>"state="+s.State+"; t="+(Time.time-started).ToString("F2")+"; hp="+s.health.Current+"/"+s.health.Maximum+"; kills="+s.Kills+"; scrap="+s.Scrap+"/"+s.ScrapEarned+"; shots="+s.turret.ShotsFired+"; pos="+s.vehicle.Body.position;
        static Vector3 World(Vector2 w)=>Quaternion.Euler(0,45,0)*new Vector3(w.x*2.5f,0,w.y*2.5f);
        static Vector3 Flat(Vector3 p)=>Vector3.ProjectOnPlane(p,Vector3.up);
        static string N(float value)=>value.ToString("F4",CultureInfo.InvariantCulture);
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);Debug.Log("WALKER ARENA "+lines[lines.Count-1]);Write();}
        void Capture(string name){ScreenCapture.CaptureScreenshot(Folder+"/Screenshots/"+name+".png");}
        void Write(){File.WriteAllLines(ReportPath,lines);File.WriteAllLines(Folder+"/ArenaRunSamples.csv",samples);}
        void Restore()
        {
            if(restored)return;restored=true;if(!saved||!s)return;Stop();if(observer){s.Feedback.sinks.Remove(observer);Destroy(observer);}
            s.turret.enabled=originalTurret;s.Restart();s.input.testInput=originalInput;s.input.testBrake=originalBrake;s.input.testDirection=originalDirection;
            Application.targetFrameRate=originalFps;QualitySettings.vSyncCount=originalVsync;Time.timeScale=originalTime;
        }
        void OnDestroy(){Restore();}
    }
    public sealed class WalkerArenaRunObserver : ArcadeFeedbackSink
    {
        public WalkerArenaRunPlayChecks owner;
        public override void EnemyKilled(CombatTarget target,Vector3 position,int reward){owner.Killed(target);}
        public override void EnemyHit(CombatTarget target,Vector3 point,float damage,bool ram,bool lethal){owner.Hit(ram);}
        public override void PlayerDamaged(float amount,float healthFraction){owner.Damaged(amount);}
        public override void PlayerHealed(float amount,float healthFraction){owner.Healed(amount);}
        public override void RepairChanged(bool active,Vector3 position){owner.Repair(active);}
        public override void HarpoonLaunched(Vector3 from){owner.Launched();}
        public override void HarpoonReeled(Vector3 at){owner.Reeled();}
    }
}
#endif
