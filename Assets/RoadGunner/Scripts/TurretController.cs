using UnityEngine;
namespace RoadGunner
{
    public sealed class TurretController : MonoBehaviour
    {
        public GameSession session;
        public CombatTuning tuning;
        public Transform yaw,pitch,muzzle;
        public LayerMask hitMask;
        public CombatTarget Target { get; private set; }
        public int ShotsFired { get; private set; }
        public float Range => tuning.range;
        float searchAt,fireAt;
        public bool CanAttack(CombatTarget r) {
            if(!r||!r.Alive)return false;
            if(!AttackRange.Contains(session.vehicle.transform.position,r.transform.position,Range))return false;
            Vector3 delta=r.AimPoint-muzzle.position;
            if(Physics.Raycast(muzzle.position,delta.normalized,out RaycastHit hit,delta.magnitude+.25f,hitMask,QueryTriggerInteraction.Collide))return hit.collider.GetComponentInParent<CombatTarget>()==r;
            return false;
        }
        void Update() {
            if(!session||!session.Playing)return;
            if(Target is ArenaRewardCache && (!session.exploration || !session.exploration.isActiveAndEnabled)){Target=null;searchAt=0;}
            if(Time.time>=searchAt){searchAt=Time.time+.2f;Acquire();}
            if(!Target||!Target.Alive){Target=null;return;}
            Vector3 to=Target.AimPoint-yaw.position;Vector3 horizontal=Vector3.ProjectOnPlane(to,Vector3.up);
            if(horizontal.sqrMagnitude>.001f)yaw.rotation=Quaternion.RotateTowards(yaw.rotation,Quaternion.LookRotation(horizontal,Vector3.up),tuning.yawSpeed*Time.deltaTime);
            Vector3 local=yaw.InverseTransformDirection(Target.AimPoint-pitch.position);
            float elevation=-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
            pitch.localRotation=Quaternion.RotateTowards(pitch.localRotation,Quaternion.Euler(Mathf.Clamp(elevation,-20,35),0,0),tuning.yawSpeed*Time.deltaTime);
            if(Time.time<fireAt || Vector3.Angle(muzzle.forward,Target.AimPoint-muzzle.position)>tuning.aimTolerance)return;
            if(!CanAttack(Target)){Target=null;searchAt=0;return;}
            fireAt=Time.time+1/tuning.shotsPerSecond;ShotsFired++;
            float shotLength=(Target.AimPoint-muzzle.position).magnitude+.25f;
            Vector3 endpoint=muzzle.position+muzzle.forward*shotLength;
            if(Physics.Raycast(muzzle.position,muzzle.forward,out RaycastHit hit,shotLength,hitMask,QueryTriggerInteraction.Collide)) {
                endpoint=hit.point;var enemy=hit.collider.GetComponentInParent<CombatTarget>();if(enemy&&AttackRange.Contains(session.vehicle.transform.position,enemy.transform.position,Range))enemy.Hit(session.PlayerDamage(tuning.bulletDamage),muzzle.forward);
                session.effects.Spark(endpoint);
            }
            session.effects.Shot(muzzle.position,endpoint,muzzle.rotation);
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
        public void ResetCombat(){Target=null;searchAt=Time.time+.1f;fireAt=Time.time+.2f;}
    }
}
