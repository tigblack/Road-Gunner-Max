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
        public override bool Alive => gameObject.activeInHierarchy && State!=RaiderState.Dead && Health>0;
        public int Group { get; private set; }
        public override Vector3 AimPoint => transform.position+Vector3.up*1.05f;
        public GameSession session;
        public CombatTuning tuning;
        float nextDestination,nextAttack,strikeAt,reactionUntil,deathAt,nextRam;
        bool pendingAttack;
        int slot;
        MaterialPropertyBlock block;
        public void Spawn(Vector3 point,int group,int index) {
            Group=group;slot=index;Health=tuning.enemyHealth;State=RaiderState.Idle;pendingAttack=false;nextRam=0;nextAttack=Time.time+.5f;
            transform.position=point;transform.rotation=Quaternion.Euler(0,index*137.5f,0);visual.localRotation=Quaternion.identity;visual.localPosition=Vector3.zero;
            gameObject.SetActive(true);hitbox.enabled=true;agent.enabled=true;agent.Warp(point);agent.speed=tuning.moveSpeed;agent.isStopped=true;agent.ResetPath();nextDestination=Time.time+(index%5)*.04f;
            block??=new MaterialPropertyBlock();torso.SetPropertyBlock(null);Pose(0);
        }
        public void Despawn() { pendingAttack=false; if(agent.enabled&&agent.isOnNavMesh)agent.ResetPath();agent.enabled=false;hitbox.enabled=false;State=RaiderState.Dead;Health=0;gameObject.SetActive(false); }
        public void CancelAttack() { pendingAttack=false;nextAttack=Time.time+.45f;if(Alive){State=RaiderState.Chase;if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;} }
        void Update() {
            if(!session || !session.Playing)return;
            if(State==RaiderState.Dead) {visual.localRotation=Quaternion.Slerp(visual.localRotation,Quaternion.Euler(85,0,15),Time.deltaTime*10);if(Time.time>deathAt)Despawn();return;}
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
                if(!pendingAttack&&Time.time>=nextAttack){pendingAttack=true;strikeAt=Time.time+.32f;nextAttack=Time.time+tuning.attackCooldown;}
                if(pendingAttack&&Time.time>=strikeAt){pendingAttack=false;session.health.Damage(tuning.meleeDamage);session.effects.Spark(boundary);}
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
        void Pose(float swing) {leftLeg.localRotation=Quaternion.Euler(swing,0,0);rightLeg.localRotation=Quaternion.Euler(-swing,0,0);leftArm.localRotation=Quaternion.Euler(-swing,0,12);rightArm.localRotation=Quaternion.Euler(swing,0,-12);}
        public override void Hit(float damage,Vector3 direction,bool ram=false) {
            if(!Alive || !session.Playing || (ram&&Time.time<nextRam))return;
            if(ram)nextRam=Time.time+tuning.ramCooldown;
            Health=Mathf.Max(0,Health-damage);pendingAttack=false;
            if(Health<=0) {
                State=RaiderState.Dead;hitbox.enabled=false;agent.isStopped=true;agent.ResetPath();agent.enabled=false;deathAt=Time.time+.8f;torso.SetPropertyBlock(null);
                session.RaiderDefeated(transform.position,direction);return;
            }
            State=RaiderState.HitReaction;reactionUntil=Time.time+.14f;agent.isStopped=true;block.SetColor("_BaseColor",new Color(1,.66f,.37f));torso.SetPropertyBlock(block);
            if(ram&&NavMesh.SamplePosition(transform.position+Vector3.ProjectOnPlane(direction,Vector3.up).normalized*.8f,out NavMeshHit h,1,NavMesh.AllAreas))agent.Warp(h.position);
        }
    }
}
