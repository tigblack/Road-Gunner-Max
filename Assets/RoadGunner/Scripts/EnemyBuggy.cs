using UnityEngine;
using UnityEngine.AI;
namespace RoadGunner
{
    public enum BuggyState { Idle, Chase, Firing, Retreat, Reversing, Wrecked }

    [RequireComponent(typeof(Rigidbody))]
    public sealed class EnemyBuggy : CombatTarget
    {
        public GameSession session;
        public BuggyTuning tuning;
        public WheelCollider[] wheels;
        public Transform[] wheelVisuals;
        public Collider[] bodyColliders;
        public Renderer[] armor;
        public ParticleSystem dust, smoke;
        public BuggyGun gun;
        public Rigidbody Body { get; private set; }
        public float Health { get; private set; }
        public float MaximumHealth => tuning ? Mathf.Max(1,tuning.health) : 1;
        public override float CurrentHealth => Health;
        public override float MaxHealth => MaximumHealth;
        public BuggyState State { get; private set; }
        public override bool Alive => gameObject.activeInHierarchy && Health > 0 && State != BuggyState.Wrecked;
        public override Vector3 AimPoint => transform.TransformPoint(new Vector3(0, .55f, 0));
        public int SpawnSlot { get; private set; }
        public int GroundedWheels { get; private set; }
        public bool AIEnabled { get; set; } = true;
        NavMeshPath path;
        readonly Vector3[] corners = new Vector3[64];
        int cornerCount, corner;
        float repathAt, nextPlayerRam, flashUntil, wreckUntil, steering, stuckFor, reverseUntil;
        Vector3 lastPosition;
        MaterialPropertyBlock block;
        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = 620; Body.centerOfMass = new Vector3(0, -.05f, -.12f);
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            path = new NavMeshPath(); block = new MaterialPropertyBlock();
            foreach (var wheel in wheels) wheel.ConfigureVehicleSubsteps(5, 8, 4);
        }
        public void Spawn(Vector3 ground, Quaternion rotation, int slot)
        {
            gameObject.SetActive(true);
            SpawnSlot = slot; Health = tuning.health; State = BuggyState.Idle; AIEnabled = true;
            Body.isKinematic = false;
            Body.position = ground + Vector3.up * .65f; Body.rotation = rotation;
            transform.SetPositionAndRotation(Body.position, rotation);
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            foreach (var collider in bodyColliders) collider.enabled = true;
            foreach (var wheel in wheels) { wheel.enabled = true; wheel.motorTorque = 0; wheel.brakeTorque = 500; wheel.steerAngle = 0; }
            steering = 0; repathAt = 0; cornerCount = 0; stuckFor = 0; reverseUntil = 0;
            nextPlayerRam = 0; flashUntil = 0;
            lastPosition = transform.position;
            foreach (var panel in armor) panel.SetPropertyBlock(null);
            if (dust) dust.Clear(); if (smoke) { smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            if (gun) { gun.CanShoot = true; gun.ResetCombat(); }
            NotifyHealthChanged();
        }
        public void Despawn()
        {
            Health = 0; State = BuggyState.Wrecked;
            foreach (var collider in bodyColliders) collider.enabled = false;
            foreach (var wheel in wheels) { wheel.motorTorque = 0; wheel.brakeTorque = 500; wheel.enabled = false; }
            if (Body) { if(!Body.isKinematic){Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;} Body.isKinematic = true; }
            if (dust) dust.Clear(); if (smoke) smoke.Clear();
            if (gun) gun.ResetCombat();
            NotifyHealthChanged(); gameObject.SetActive(false);
        }
        public void CancelAttack() { if (gun) gun.ResetCombat(); }
        void FixedUpdate()
        {
            if (!Alive || !session) return;
            GroundedWheels = 0;
            foreach (var wheel in wheels) if (wheel.GetGroundHit(out _)) GroundedWheels++;
            float motor = 0, brake = 500;
            Vector3 toPlayer = session.vehicle.transform.position - transform.position;
            toPlayer.y = 0;
            if (AIEnabled && session.Playing && (State != BuggyState.Idle || toPlayer.sqrMagnitude < tuning.aggroRadius * tuning.aggroRadius))
            {
                float distance = toPlayer.magnitude;
                float preferred = Mathf.Clamp(tuning.preferredRange, 3, Mathf.Max(3, gun.Range - .75f));
                float retreatAt = Mathf.Min(tuning.retreatDistance, preferred - 1);
                bool retreat = distance < retreatAt || State == BuggyState.Retreat && distance < preferred;
                bool clear = gun.HasLineOfSight();
                bool hold = !retreat && clear && distance < preferred + (State == BuggyState.Firing ? .8f : .25f);
                bool recovery = !retreat && !hold && Time.time < reverseUntil;
                State = retreat ? BuggyState.Retreat : hold ? BuggyState.Firing : recovery ? BuggyState.Reversing : BuggyState.Chase;
                if (hold)
                {
                    brake = tuning.brakeTorque;
                    stuckFor = 0;
                }
                else
                {
                    if (Time.time >= repathAt && !retreat)
                    {
                        repathAt = Time.time + .6f;
                        RepathToFiringPosition(toPlayer, preferred);
                    }
                    bool reverse = retreat ? Vector3.Dot(transform.forward, toPlayer) > 0 : recovery;
                    Vector3 desired = retreat ? (reverse ? toPlayer : -toPlayer) : toPlayer;
                    if (!retreat && cornerCount > 0)
                    {
                        while (corner < cornerCount - 1 && Vector3.ProjectOnPlane(corners[corner] - transform.position, Vector3.up).magnitude < 2) corner++;
                        desired = corners[corner] - transform.position; desired.y = 0;
                    }
                    float avoid = Avoidance(reverse ? -1 : 1);
                    float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), desired, Vector3.up);
                    float targetSteer = Mathf.Clamp(angle * .9f + avoid * 42, -38, 38);
                    if (reverse) targetSteer = -targetSteer;
                    steering = Mathf.MoveTowards(steering, targetSteer, Time.fixedDeltaTime * 120);
                    float speed = Vector3.Dot(Body.linearVelocity, transform.forward);
                    // Slow down before reaching the firing position instead of driving into the player.
                    float approach = Mathf.Sqrt(2 * tuning.approachDeceleration * Mathf.Max(.25f, distance - preferred));
                    float goal = retreat ? tuning.retreatSpeed : recovery ? 3 : Mathf.Min(tuning.maxSpeed, Mathf.Max(clear ? 1.5f : 3, approach));
                    if (!retreat && !recovery) goal = Mathf.Min(goal, Mathf.Lerp(tuning.maxSpeed, 3.5f, Mathf.InverseLerp(20, 100, Mathf.Abs(angle))));
                    if (Mathf.Abs(avoid) > .5f) goal = Mathf.Min(goal, 3.5f);
                    float sign = reverse ? -1 : 1;
                    float error = goal - speed * sign;
                    brake = speed * sign < -.4f ? tuning.brakeTorque : error < -.25f ? Mathf.Min(tuning.brakeTorque, -error * 350) : 0;
                    if (brake == 0) motor = sign * tuning.driveTorque * Mathf.Clamp01(error / 3);
                    if (!retreat && (transform.position - lastPosition).sqrMagnitude < .0025f && distance > preferred) stuckFor += Time.fixedDeltaTime;
                    else stuckFor = 0;
                    if (stuckFor > 2.2f && !recovery) { reverseUntil = Time.time + 1.3f; stuckFor = 0; repathAt = 0; }
                }
                lastPosition = transform.position;
            }
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].steerAngle = i < 2 ? steering : 0;
                wheels[i].motorTorque = motor * .25f; wheels[i].brakeTorque = brake;
            }
            if (GroundedWheels > 0) Body.AddForce(-Body.linearVelocity * 24);
        }
        static readonly float[] FiringAngles = { 0, 45, -45, 90, -90, 135, -135, 180 };
        void RepathToFiringPosition(Vector3 toPlayer, float distance)
        {
            cornerCount = 0;
            if (!NavMesh.SamplePosition(transform.position, out var from, 4, NavMesh.AllAreas)) return;
            Vector3 radial = toPlayer.sqrMagnitude > .01f ? -toPlayer.normalized : -transform.forward;
            Vector3 player = session.vehicle.transform.position;
            foreach (float angle in FiringAngles)
            {
                Vector3 candidate = player + Quaternion.Euler(0, angle, 0) * radial * distance;
                if (!NavMesh.SamplePosition(candidate, out var to, 2, NavMesh.AllAreas)) continue;
                if (!AttackRange.Contains(player, to.position, gun.Range - .25f)) continue;
                if (Physics.Linecast(to.position + Vector3.up * 2.1f, player + Vector3.up * .5f, 1, QueryTriggerInteraction.Ignore)) continue;
                if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                cornerCount = path.GetCornersNonAlloc(corners); corner = Mathf.Min(1, cornerCount - 1); return;
            }
        }
        float Avoidance(float driveSign)
        {
            Vector3 forward = transform.forward * driveSign;
            Vector3 origin = transform.position + forward * .8f + Vector3.up * .55f;
            float distance = 3.5f + Mathf.Min(Body.linearVelocity.magnitude, 8) * .35f;
            bool left = Physics.SphereCast(origin - transform.right * .7f, .38f, forward, out _, distance, 1, QueryTriggerInteraction.Ignore);
            bool right = Physics.SphereCast(origin + transform.right * .7f, .38f, forward, out _, distance, 1, QueryTriggerInteraction.Ignore);
            if (!left && !right) return 0;
            if (left && !right) return 1;
            if (right && !left) return -1;
            bool clearLeft = !Physics.SphereCast(origin, .5f, (forward - transform.right).normalized, out _, 4.5f, 1, QueryTriggerInteraction.Ignore);
            bool clearRight = !Physics.SphereCast(origin, .5f, (forward + transform.right).normalized, out _, 4.5f, 1, QueryTriggerInteraction.Ignore);
            return clearLeft && !clearRight ? -1 : 1;
        }
        void LateUpdate()
        {
            if (!Alive) { if (gameObject.activeSelf && session && session.Playing && Time.time >= wreckUntil) Despawn(); return; }
            for (int i = 0; i < wheels.Length; i++) { wheels[i].GetWorldPose(out var p, out var q); wheelVisuals[i].SetPositionAndRotation(p, q); }
            if (dust) { var emission = dust.emission; emission.rateOverTime = session.Playing && GroundedWheels > 0 && Body.linearVelocity.magnitude > 2 ? 14 : 0; }
            block.Clear(); if (Time.time < flashUntil) block.SetColor("_BaseColor", new Color(1, .6f, .25f));
            foreach (var panel in armor) panel.SetPropertyBlock(block);
        }
        public override void Hit(float damage, Vector3 direction, bool ram = false)
        {
            if (!Alive || !session.Playing || (ram && Time.time < nextPlayerRam)) return;
            if (ram) nextPlayerRam = Time.time + session.turret.tuning.ramCooldown;
            Health = Mathf.Max(0, Health - damage); NotifyHealthChanged(); flashUntil = Time.time + .13f;
            if (Health > 0) return;
            State = BuggyState.Wrecked; wreckUntil = Time.time + tuning.wreckLifetime;
            if (gun) gun.ResetCombat();
            foreach (var collider in bodyColliders) collider.enabled = false;
            foreach (var wheel in wheels) wheel.enabled = false;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; Body.isKinematic = true;
            if (dust) { var em = dust.emission; em.rateOverTime = 0; }
            if (smoke) { smoke.Play(); smoke.Emit(8); }
            session.EnemyDefeated(transform.position, tuning.scrapReward, true, direction);
            block.Clear(); block.SetColor("_BaseColor", new Color(.22f, .18f, .14f));
            foreach (var panel in armor) panel.SetPropertyBlock(block);
            session.effects.Explode(transform.position);
        }
    }
}
