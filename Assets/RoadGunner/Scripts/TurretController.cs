using System;
using UnityEngine;
namespace RoadGunner
{
    public readonly struct CannonShot
    {
        public readonly int Sequence;
        public readonly float Time, AppliedDamage;
        public readonly Vector3 Origin, Direction, End;
        public readonly Quaternion Rotation;
        public readonly Collider HitCollider;
        public readonly CombatTarget HitTarget;
        public CannonShot(int sequence,float time,Vector3 origin,Vector3 direction,Vector3 end,Quaternion rotation,Collider hitCollider,CombatTarget hitTarget,float appliedDamage)
        { Sequence=sequence;Time=time;Origin=origin;Direction=direction;End=end;Rotation=rotation;HitCollider=hitCollider;HitTarget=hitTarget;AppliedDamage=appliedDamage; }
    }

    // Gait places the shoulder at12000. Aim, real muzzle and shot resolve against that final visual pose.
    [DefaultExecutionOrder(12100)]
    public sealed class TurretController : MonoBehaviour
    {
        public GameSession session;
        public CombatTuning tuning;
        public Transform yaw,pitch,muzzle;
        public LayerMask hitMask;
        public CombatTarget Target { get; private set; }
        public int ShotsFired { get; private set; }
        public float Range => tuning.range;
        public CannonShot LastShot { get; private set; }
        public event Action<CannonShot> ShotResolved;
        public CannonVisualRecoil CannonVisual { get; private set; }
        float searchAt,fireAt,worldYaw,yawVelocity,pitchAngle,pitchVelocity;
        bool aimPoseCached;
        int aimPoseSerial;
        PlayerTrackVisuals walker;
        void Start()
        {
            CannonVisual=GetComponent<CannonVisualRecoil>();
            if(!CannonVisual)CannonVisual=gameObject.AddComponent<CannonVisualRecoil>();
            CannonVisual.Bind(this);
            if(session&&session.vehicle)walker=session.vehicle.GetComponentInChildren<PlayerTrackVisuals>(true);
        }
        void CaptureAimPose()
        {
            worldYaw=yaw.eulerAngles.y;pitchAngle=Mathf.DeltaAngle(0,pitch.localEulerAngles.x);
            yawVelocity=pitchVelocity=0;aimPoseCached=true;
            if(session&&session.vehicle)aimPoseSerial=session.vehicle.PoseResetSerial;
        }
        void OnDisable(){if(CannonVisual)CannonVisual.ResetRecoil();aimPoseCached=false;}
        float TrackAngle(float current,float target,ref float velocity,float dt)
        {
            // Do not introduce a steady-state aim lag against a moving target. Large retargets settle,
            // while a target reachable within this frame's original yaw-speed budget stays exactly tracked.
            float speed=Mathf.Max(0,tuning.yawSpeed);
            if(Mathf.Abs(Mathf.DeltaAngle(current,target))<=speed*dt){velocity=0;return target;}
            return Mathf.SmoothDampAngle(current,target,ref velocity,Mathf.Max(.01f,tuning.cannonMountSettleSeconds),speed,dt);
        }

        public bool CanAttack(CombatTarget r) {
            if(!r||!r.Alive)return false;
            if(!AttackRange.Contains(session.vehicle.transform.position,r.transform.position,Range))return false;
            Vector3 delta=r.AimPoint-muzzle.position;
            if(Physics.Raycast(muzzle.position,delta.normalized,out RaycastHit hit,delta.magnitude+.25f,hitMask,QueryTriggerInteraction.Collide))return hit.collider.GetComponentInParent<CombatTarget>()==r;
            return false;
        }
        void LateUpdate() {
            if(CannonVisual)CannonVisual.PreparePose(Time.deltaTime);
            if(!session||!session.Playing||!tuning||!yaw||!pitch||!muzzle)return;
            if(!aimPoseCached||aimPoseSerial!=session.vehicle.PoseResetSerial)CaptureAimPose();
            // Keep world-space aim across instantaneous chassis reversals; legs never rotate the weapon lock.
            yaw.rotation=Quaternion.Euler(0,worldYaw,0);pitch.localRotation=Quaternion.Euler(pitchAngle,0,0);
            if(Target is ArenaRewardCache && (!session.exploration || !session.exploration.isActiveAndEnabled)){Target=null;searchAt=0;}
            if(Time.time>=searchAt){searchAt=Time.time+.2f;Acquire();}
            if(!Target||!Target.Alive){Target=null;return;}
            Vector3 to=Target.AimPoint-yaw.position;Vector3 horizontal=Vector3.ProjectOnPlane(to,Vector3.up);
            float dt=Time.deltaTime;
            if(horizontal.sqrMagnitude>.001f) {
                float targetYaw=Mathf.Atan2(horizontal.x,horizontal.z)*Mathf.Rad2Deg;
                worldYaw=TrackAngle(worldYaw,targetYaw,ref yawVelocity,dt);
                yaw.rotation=Quaternion.Euler(0,worldYaw,0);
            }
            Vector3 local=yaw.InverseTransformDirection(Target.AimPoint-pitch.position);
            float elevation=-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
            pitchAngle=TrackAngle(pitchAngle,Mathf.Clamp(elevation,-20,35),ref pitchVelocity,dt);
            pitch.localRotation=Quaternion.Euler(pitchAngle,0,0);
            if(Time.time<fireAt || Vector3.Angle(muzzle.forward,Target.AimPoint-muzzle.position)>tuning.aimTolerance)return;
            if(!CanAttack(Target)){Target=null;searchAt=0;return;}
            fireAt=Time.time+1/tuning.shotsPerSecond;ShotsFired++;
            // Resolve the existing forward hitscan once. Every presentation consumer gets this same immutable result.
            Vector3 origin=muzzle.position,direction=muzzle.forward;Quaternion rotation=muzzle.rotation;
            float shotTime=Time.time,shotLength=(Target.AimPoint-origin).magnitude+.25f;
            Vector3 endpoint=origin+direction*shotLength;Collider collider=null;CombatTarget hitTarget=null;float damage=0;
            if(Physics.Raycast(origin,direction,out RaycastHit hit,shotLength,hitMask,QueryTriggerInteraction.Collide)) {
                endpoint=hit.point;collider=hit.collider;hitTarget=collider.GetComponentInParent<CombatTarget>();
                if(hitTarget&&AttackRange.Contains(session.vehicle.transform.position,hitTarget.transform.position,Range)) {
                    float before=hitTarget.CurrentHealth;hitTarget.Hit(session.PlayerDamage(tuning.bulletDamage),direction);
                    damage=Mathf.Max(0,before-hitTarget.CurrentHealth);
                }
                session.effects.Spark(endpoint);
            }
            LastShot=new CannonShot(ShotsFired,shotTime,origin,direction,endpoint,rotation,collider,hitTarget,damage);
            session.effects.Shot(origin,endpoint,rotation);
            if(CannonVisual)CannonVisual.Shot();
            if(walker)walker.CannonImpulse(1);
            ShotResolved?.Invoke(LastShot);
        }
        void Acquire() {
            // A nearby crate must never hold target lock while an enemy can be attacked.
            CombatTarget best=!(Target is ArenaRewardCache)&&CanAttack(Target)?Target:null;float nearest=best?AttackRange.SquaredDistance(session.vehicle.transform.position,best.transform.position)*.65f:float.MaxValue;
            foreach(var r in session.spawns.pool){if(!r||!r.Alive)continue;float d=AttackRange.SquaredDistance(session.vehicle.transform.position,r.transform.position);if(d<nearest&&CanAttack(r)){nearest=d;best=r;}}
            if(session.spawns.buggyPool!=null)foreach(var buggy in session.spawns.buggyPool){if(!buggy||!buggy.Alive)continue;float d=AttackRange.SquaredDistance(session.vehicle.transform.position,buggy.transform.position);if(d<nearest&&CanAttack(buggy)){nearest=d;best=buggy;}}
            if(session.zone1&&session.zone1.ExtraTargets!=null)foreach(var extra in session.zone1.ExtraTargets){if(!extra||!extra.Alive)continue;float d=AttackRange.SquaredDistance(session.vehicle.transform.position,extra.transform.position);if(d<nearest&&CanAttack(extra)){nearest=d;best=extra;}}
            if(!best && session.exploration && session.exploration.isActiveAndEnabled) {
                if(Target is ArenaRewardCache && CanAttack(Target)){best=Target;nearest=AttackRange.SquaredDistance(session.vehicle.transform.position,best.transform.position)*.65f;}
                foreach(var cache in session.exploration.Targets){float d=AttackRange.SquaredDistance(session.vehicle.transform.position,cache.transform.position);if(d<nearest&&CanAttack(cache)){nearest=d;best=cache;}}
            }
            Target=best;
        }
        public void ResetCombat(){Target=null;searchAt=Time.time+.1f;fireAt=Time.time+.2f;aimPoseCached=false;}
    }
}
