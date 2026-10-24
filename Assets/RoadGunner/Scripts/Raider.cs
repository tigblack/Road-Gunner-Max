using UnityEngine;
using UnityEngine.AI;
namespace RoadGunner
{
    public enum RaiderState { Idle, Chase, Attack, HitReaction, Dead }
    public sealed class Raider : CombatTarget
    {
        public NavMeshAgent agent;
        public CapsuleCollider hitbox;
        public Transform visual,leftLeg,rightLeg,leftArm,rightArm;
        public Renderer torso;
        public RaiderState State { get; private set; }
        public float Health { get; private set; }
        public float MaximumHealth => tuning ? Mathf.Max(1,tuning.enemyHealth) : 1;
        public override bool Alive => gameObject.activeInHierarchy && State!=RaiderState.Dead && Health>0;
        public int Group { get; private set; }
        public override Vector3 AimPoint => transform.TransformPoint(new Vector3(0,1.05f,0));
        public GameSession session;
        public CombatTuning tuning;
        [Header("Optional final-boss encounter (same Raider combat framework)")]
        public bool arenaBoss;
        public Transform spawnPoint, bossWarning;
        [Min(.25f)] public float bossWindup=.8f;
        [Min(.1f)] public float bossStrikeRadius=2.2f;
        [Min(.1f)] public float bossStrikeOffset=2.6f;
        [Min(.1f)] public float bossRecovery=.55f;
        [Min(0)] public int bossGearReward=10;
        public bool IsTelegraphing => arenaBoss && Alive && pendingAttack;
        public bool IsRecovering => arenaBoss && Alive && Time.time<recoverUntil;
        public bool RewardGranted { get; private set; }
        public bool AIEnabled { get; set; } = true;
        float nextDestination,nextAttack,strikeAt,reactionUntil,deathAt,nextRam,recoverUntil,flashUntil;
        bool pendingAttack,homeCaptured;
        int slot;
        Vector3 homePosition,lockedStrike;
        Quaternion homeRotation;
        MaterialPropertyBlock block;
        void CaptureHome() {if(homeCaptured)return;homePosition=transform.position;homeRotation=transform.rotation;homeCaptured=true;}
        void SetWarning(bool shown) {if(bossWarning)bossWarning.gameObject.SetActive(shown);}
        bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;
        public void Spawn(Vector3 point,int group,int index) {
            Group=group;slot=index;Health=MaximumHealth;State=RaiderState.Idle;pendingAttack=false;nextRam=0;nextAttack=Time.time+.5f;recoverUntil=flashUntil=0;RewardGranted=false;AIEnabled=true;
            transform.position=point;transform.rotation=Quaternion.Euler(0,index*137.5f,0);visual.localRotation=Quaternion.identity;visual.localPosition=Vector3.zero;
            gameObject.SetActive(true);hitbox.enabled=true;agent.enabled=true;agent.Warp(point);agent.speed=tuning.moveSpeed;
            if(AgentReady){agent.isStopped=true;agent.ResetPath();}nextDestination=Time.time+(index%5)*.04f;
            block??=new MaterialPropertyBlock();torso.SetPropertyBlock(null);Pose(0);SetWarning(false);
        }
        public void ResetEncounter() {
            CaptureHome();var point=spawnPoint?spawnPoint.position:homePosition;var rotation=spawnPoint?spawnPoint.rotation:homeRotation;
            if(agent)agent.enabled=false;
            if(NavMesh.SamplePosition(point,out var hit,3,NavMesh.AllAreas))point=hit.position;
            Spawn(point,-1000,0);transform.rotation=rotation;
        }
        public void Despawn() { pendingAttack=false;SetWarning(false);if(AgentReady)agent.ResetPath();agent.enabled=false;hitbox.enabled=false;State=RaiderState.Dead;Health=0;gameObject.SetActive(false); }
        public void CancelAttack() { pendingAttack=false;SetWarning(false);recoverUntil=0;nextAttack=Time.time+.65f;if(Alive){State=RaiderState.Chase;if(AgentReady)agent.isStopped=true;} }
        void OnDisable() {SetWarning(false);}
        void Update() {
            if(!session || !session.Playing)return;
            if(State==RaiderState.Dead) {visual.localRotation=Quaternion.Slerp(visual.localRotation,Quaternion.Euler(85,0,15),Time.deltaTime*10);if(Time.time>deathAt)Despawn();return;}
            if(!Alive||!AgentReady||!AIEnabled)return;
            if(arenaBoss && flashUntil>0 && Time.time>=flashUntil){torso.SetPropertyBlock(null);flashUntil=0;}
            if(arenaBoss&&pendingAttack) {
                agent.isStopped=true;Pose(0);rightArm.localRotation=Quaternion.Euler(-135,0,-22);
                if(Time.time>=strikeAt)ResolveBossStrike();return;
            }
            if(IsRecovering){agent.isStopped=true;Pose(0);rightArm.localRotation=Quaternion.Euler(55,0,-12);return;}
            Vector3 toCar=session.vehicle.transform.position-transform.position;toCar.y=0;
            float distance=toCar.magnitude;
            if(State==RaiderState.HitReaction&&Time.time<reactionUntil){Pose(0);return;}
            if(State==RaiderState.HitReaction){State=RaiderState.Chase;torso.SetPropertyBlock(null);}
            if(State==RaiderState.Idle && distance>tuning.aggroRadius){Pose(0);return;}
            var carCollider=session.vehicle.GetComponent<BoxCollider>();
            Vector3 boundary=carCollider.ClosestPoint(AimPoint);Vector3 flat=boundary-AimPoint;flat.y=0;
            bool reach=flat.magnitude<tuning.meleeReach;
            bool clear=!Physics.Linecast(AimPoint,boundary,1,QueryTriggerInteraction.Ignore);
            if(reach&&clear) {
                agent.isStopped=true;State=RaiderState.Attack;
                if(toCar.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(toCar),Time.deltaTime*400);
                if(!pendingAttack&&Time.time>=nextAttack){
                    pendingAttack=true;strikeAt=Time.time+(arenaBoss?bossWindup:.32f);nextAttack=strikeAt+tuning.attackCooldown;
                    if(arenaBoss){lockedStrike=transform.position+transform.forward*bossStrikeOffset;if(bossWarning){bossWarning.position=lockedStrike+Vector3.up*.055f;bossWarning.rotation=Quaternion.identity;bossWarning.localScale=Vector3.one*(bossStrikeRadius/Mathf.Max(.01f,transform.lossyScale.x));}SetWarning(true);}
                }
                if(!arenaBoss&&pendingAttack&&Time.time>=strikeAt){pendingAttack=false;session.health.Damage(tuning.meleeDamage);session.effects.Spark(boundary);}
                Pose(0);rightArm.localRotation=Quaternion.Euler(pendingAttack?-125:-20,0,-12);
            } else {
                pendingAttack=false;State=RaiderState.Chase;agent.isStopped=false;
                if(Time.time>=nextDestination) {
                    nextDestination=Time.time+.2f;float angle=slot*2.39996f;Vector3 offset=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*2.6f;
                    Vector3 dest=session.vehicle.transform.position+offset;
                    if(NavMesh.SamplePosition(dest,out NavMeshHit hit,3,NavMesh.AllAreas))agent.SetDestination(hit.position);
                }
                Pose(Mathf.Sin(Time.time*10+slot)*Mathf.Clamp01(agent.velocity.magnitude/3.5f)*32);
            }
        }
        void ResolveBossStrike() {
            pendingAttack=false;SetWarning(false);recoverUntil=Time.time+bossRecovery;
            var car=session.vehicle.GetComponent<BoxCollider>();var nearest=car.ClosestPoint(lockedStrike+Vector3.up*.8f);var delta=nearest-lockedStrike;delta.y=0;
            if(delta.sqrMagnitude<=bossStrikeRadius*bossStrikeRadius&&!Physics.Linecast(AimPoint,nearest,1,QueryTriggerInteraction.Ignore)) {session.health.Damage(tuning.meleeDamage);session.effects.Spark(nearest);}
        }
        void Pose(float swing) {leftLeg.localRotation=Quaternion.Euler(swing,0,0);rightLeg.localRotation=Quaternion.Euler(-swing,0,0);leftArm.localRotation=Quaternion.Euler(-swing,0,12);rightArm.localRotation=Quaternion.Euler(swing,0,-12);}
        public override void Hit(float damage,Vector3 direction,bool ram=false) {
            if(!Alive || !session.Playing || (ram&&Time.time<nextRam))return;
            if(ram)nextRam=Time.time+tuning.ramCooldown;
            Health=Mathf.Max(0,Health-damage);
            if(Health<=0) {
                pendingAttack=false;SetWarning(false);State=RaiderState.Dead;hitbox.enabled=false;if(AgentReady){agent.isStopped=true;agent.ResetPath();}agent.enabled=false;deathAt=Time.time+.8f;torso.SetPropertyBlock(null);
                RewardGranted=true;
                if(arenaBoss){session.EnemyDefeated(transform.position,bossGearReward,true,direction);session.CompleteArena();}
                else session.RaiderDefeated(transform.position,direction);
                return;
            }
            block??=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(1,.66f,.37f));torso.SetPropertyBlock(block);
            // A final boss commits to its visible strike; rapid turret hits cannot stunlock its windup.
            if(arenaBoss){flashUntil=Time.time+.12f;return;}
            pendingAttack=false;State=RaiderState.HitReaction;reactionUntil=Time.time+.14f;if(AgentReady)agent.isStopped=true;
            if(ram&&NavMesh.SamplePosition(transform.position+Vector3.ProjectOnPlane(direction,Vector3.up).normalized*.8f,out NavMeshHit h,1,NavMesh.AllAreas))agent.Warp(h.position);
        }
    }
}
