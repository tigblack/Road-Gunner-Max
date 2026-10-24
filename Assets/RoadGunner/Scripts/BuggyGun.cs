using UnityEngine;
namespace RoadGunner
{
    public sealed class BuggyGun : MonoBehaviour
    {
        public EnemyBuggy buggy;
        public Transform yaw, pitch, muzzle, flash;
        public LayerMask hitMask;
        public bool CanShoot { get; set; } = true;
        public int ShotsFired { get; private set; }
        public int PlayerHits { get; private set; }
        public float Range => buggy && buggy.session ? buggy.session.turret.Range : 0;
        float fireAt, flashUntil;
        Vector3 AimPoint => buggy.session.vehicle.transform.TransformPoint(new Vector3(0, .5f, 0));
        public bool CanAttackPlayer() => CanShoot && HasLineOfSight();
        public bool HasLineOfSight()
        {
            if (!buggy || !buggy.isActiveAndEnabled || !buggy.Alive || !buggy.session || !buggy.session.Playing) return false;
            var session = buggy.session;
            if (!AttackRange.Contains(buggy.transform.position, session.vehicle.transform.position, Range)) return false;
            Vector3 delta = AimPoint - muzzle.position;
            return Physics.Raycast(muzzle.position, delta.normalized, out var hit, delta.magnitude + .25f, hitMask, QueryTriggerInteraction.Ignore)
                && hit.rigidbody == session.vehicle.Body;
        }
        public void ResetCombat()
        {
            fireAt = Time.time + (buggy && buggy.tuning ? buggy.tuning.gunWarmup : .8f);
            flashUntil = 0;
            if (flash) flash.gameObject.SetActive(false);
        }
        void OnEnable() { ResetCombat(); }
        void OnDisable() { if (flash) flash.gameObject.SetActive(false); }
        void Update()
        {
            if (flash && Time.time >= flashUntil) flash.gameObject.SetActive(false);
            if (!CanAttackPlayer()) return;
            var tuning = buggy.tuning;
            Vector3 horizontal = Vector3.ProjectOnPlane(AimPoint - yaw.position, Vector3.up);
            if (horizontal.sqrMagnitude > .001f)
                yaw.rotation = Quaternion.RotateTowards(yaw.rotation, Quaternion.LookRotation(horizontal), tuning.yawSpeed * Time.deltaTime);
            Vector3 local = yaw.InverseTransformDirection(AimPoint - pitch.position);
            float elevation = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            pitch.localRotation = Quaternion.RotateTowards(pitch.localRotation, Quaternion.Euler(Mathf.Clamp(elevation, -35, 55), 0, 0), tuning.yawSpeed * Time.deltaTime);
            if (tuning.shotsPerSecond <= 0 || Time.time < fireAt || Vector3.Angle(muzzle.forward, AimPoint - muzzle.position) > tuning.aimTolerance) return;
            fireAt = Time.time + 1 / tuning.shotsPerSecond; ShotsFired++;
            float length = (AimPoint - muzzle.position).magnitude + .25f;
            Vector3 end = muzzle.position + muzzle.forward * length;
            var session = buggy.session;
            if (Physics.Raycast(muzzle.position, muzzle.forward, out var hit, length, hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                if (hit.rigidbody == session.vehicle.Body && session.health.Damage(tuning.bulletDamage, false)) PlayerHits++;
                session.effects.Spark(end);
            }
            session.effects.Shot(muzzle.position, end, muzzle.rotation, true);
            // Damage can end the run synchronously; do not leave a flash on the frozen defeat screen.
            if (flash && session.Playing) { flash.gameObject.SetActive(true); flashUntil = Time.time + .065f; }
        }
    }
}
