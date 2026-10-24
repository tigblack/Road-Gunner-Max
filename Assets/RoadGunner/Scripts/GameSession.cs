using UnityEngine;
namespace RoadGunner
{
    public enum SessionState { Playing, Paused, Defeated, Completed }
    public sealed class GameSession : MonoBehaviour
    {
        public VehicleController vehicle;
        public VehicleHealth health;
        public DriveInput input;
        public FollowCamera follow;
        public SpawnDirector spawns;
        public PickupPool pickups;
        public TurretController turret;
        public CombatEffects effects;
        public HudView hud;
        public Zone1EnemyRoster zone1;
        public ArenaRunStats runStats;
        public ArenaMinimap minimap;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public Vector3[] recoveryPoints;
        public SessionState State { get; private set; }
        public int Scrap { get; private set; }
        public int Kills { get; private set; }
        public int ScrapEarned { get; private set; }
        public event System.Action Changed;
        public bool Playing => State==SessionState.Playing;
        public float PlayerDamage(float baseDamage) => runStats ? runStats.Damage(baseDamage) :
            turret && turret.tuning ? turret.tuning.ScalePlayerDamage(baseDamage) : baseDamage;
        float nextRecover;
        void Awake() { Application.targetFrameRate=60; Time.timeScale=1; }
        void Start() {
            input.PausePressed+=TogglePause;input.RecoverPressed+=Recover;input.DebugPressed+=hud.ToggleDebug;
            health.Died+=Defeat; Restart();
        }
        public void Restart() {
            Time.timeScale=1; State=SessionState.Playing; Scrap=0;Kills=0;ScrapEarned=0;nextRecover=0;
            input.testInput=false;input.testBrake=false;input.Clear();input.Enabled=true;vehicle.CanDrive=true;
            vehicle.ResetPose(startPosition,startRotation); if(vehicle.engine)vehicle.engine.UnPause(); if(runStats)runStats.ResetRun(); health.ResetHealth();turret.ResetCombat();effects.ResetPool();pickups.ResetPool();
            vehicle.GetComponent<TireTracks>()?.Clear();
            if(zone1)zone1.DespawnAll();
            Physics.SyncTransforms();spawns.ResetPopulation();
            if(zone1){zone1.session=this;zone1.ResetEncounters();}
            if(minimap)minimap.RefreshTargets();
            follow.Snap();Changed?.Invoke();
        }
        public void TogglePause() { if(State==SessionState.Defeated || State==SessionState.Completed)return;SetPaused(State==SessionState.Playing); }
        public void SetPaused(bool paused) {
            if(State==SessionState.Defeated || State==SessionState.Completed)return;
            State=paused?SessionState.Paused:SessionState.Playing; input.Clear(); input.Enabled=!paused;
            spawns.CancelAttacks();turret.ResetCombat();Time.timeScale=paused?0:1;
            if(vehicle.engine) vehicle.engine.Pause();if(!paused&&vehicle.engine)vehicle.engine.UnPause();Changed?.Invoke();
        }
        void Defeat() { State=SessionState.Defeated;input.Enabled=false;input.Clear();vehicle.CanDrive=false;spawns.CancelAttacks();turret.ResetCombat();Time.timeScale=0;Changed?.Invoke(); }
        public void CompleteArena() {
            if(!Playing)return;
            // Keep every resource already earned, including the boss drop and gear on a harpoon.
            vehicle.GetComponent<HarpoonCollector>()?.ResetCollection();
            if(pickups)pickups.CollectRemainingRewards();
            State=SessionState.Completed;input.Enabled=false;input.Clear();vehicle.CanDrive=false;
            health.DamageEnabled=false;spawns.CancelAttacks();turret.ResetCombat();
            if(vehicle.engine)vehicle.engine.Pause();
            Time.timeScale=0;Changed?.Invoke();
        }
        public void AddScrap(int amount) { if(!Playing)return;Scrap+=amount;Changed?.Invoke(); }
        public void RaiderDefeated(Vector3 position,Vector3 direction=default) { EnemyDefeated(position,1,true,direction); }
        public void EnemyDefeated(Vector3 position,int reward,bool gear=false,Vector3 direction=default) {
            if(!Playing)return;Kills++;ScrapEarned+=reward;
            if(gear)pickups.DropGear(position+Vector3.up*.95f,direction,reward);else pickups.Drop(position,reward);
            Changed?.Invoke();
        }
        public void Recover() {
            if(!Playing || Time.time<nextRecover) return;
            Vector3 best=startPosition;float distance=float.MaxValue;bool found=false;
            var carBounds=vehicle.GetComponent<BoxCollider>();
            foreach(var p in recoveryPoints) {
                if(Physics.CheckBox(p+startRotation*carBounds.center,carBounds.size*.5f,startRotation,1,QueryTriggerInteraction.Ignore))continue;
                float d=(p-vehicle.transform.position).sqrMagnitude;if(d<distance){distance=d;best=p;found=true;}
            }
            if(!found)return;
            nextRecover=Time.time+3;input.Clear();vehicle.GetComponent<HarpoonCollector>()?.ResetCollection();vehicle.ResetPose(best,startRotation);turret.ResetCombat();Physics.SyncTransforms();follow.Snap();
        }
        void OnApplicationFocus(bool focus) { if(!focus && Playing && Application.isMobilePlatform) SetPaused(true); }
        void OnApplicationPause(bool paused) { if(paused && Playing) SetPaused(true); }
        void OnDestroy() { Time.timeScale=1;if(input){input.PausePressed-=TogglePause;input.RecoverPressed-=Recover;if(hud)input.DebugPressed-=hud.ToggleDebug;}if(health)health.Died-=Defeat; }
    }
}
