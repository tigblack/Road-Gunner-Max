using System;
using UnityEngine;
using UnityEngine.AI;

namespace RoadGunner
{
    public enum TruckBossState { Patrol, Chase, RamWindup, Ramming, RamRecovery, Wrecked }

    /// <summary>A collidable kinematic arena truck. Every ram and gun burst has a cancellable warning.</summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Rigidbody))]
    public sealed class TruckArenaBoss : CombatTarget
    {
        public GameSession session;
        public NavMeshAgent agent;
        public Transform spawnPoint, arenaCenter;
        public Collider[] bodyColliders;
        public Transform visual;
        public Transform[] wheelVisuals;
        [Tooltip("Optional flags matching wheelVisuals. If empty, the first two wheels steer.")]
        public bool[] frontWheels;
        public Transform frontRam;
        public Collider frontRamCollider;
        public Transform turretYaw, turretPitch, muzzle, flash;
        public Transform aimAnchor;
        public GameObject telegraph, gunWarning;
        [Tooltip("Optional unit ground sector/radius marker for the locked ram lane.")]
        public Transform warningRadius;
        public Renderer[] armor;
        public ParticleSystem dust, smoke;
        public LayerMask hitMask = ~0;

        [Min(1)] public float maximumHealth = 650;
        [Min(4)] public float arenaRadius = 10.5f;
        [Tooltip("Reserves room for the truck body, so its nose does not sweep outside the arena leash.")]
        [Min(0)] public float bodyLeashPadding = 3.2f;
        [Min(.1f)] public float patrolSpeed = 1.5f;
        [Min(.1f)] public float chaseSpeed = 2.6f;
        [Min(.1f)] public float chargeSpeed = 6;
        [Min(0)] public float aggroRadius = 18;
        [Min(.1f)] public float playerRange = 10;
        [Min(.1f)] public float preferredRange = 7.8f;
        [Min(.1f)] public float wheelRadius = .62f;
        [Min(.3f)] public float ramWindup = .8f;
        [Min(.5f)] public float ramCooldown = 2.5f;
        [Min(.1f)] public float ramDistance = 5.2f;
        [Min(.1f)] public float ramHalfWidth = 1.55f;
        [Min(0)] public float ramDamage = 14;
        [Min(.1f)] public float playerRamCooldown = .55f;
        [Min(.3f)] public float gunWindup = .7f;
        [Min(.1f)] public float burstDuration = 1;
        [Min(.1f)] public float shotsPerSecond = 3;
        [Min(.1f)] public float gunRest = 2;
        [Min(0)] public float bulletDamage = 3;
        [Min(0)] public int scrapReward = 12;
        [Min(.1f)] public float wreckLifetime = 3;

        public Rigidbody Body { get; private set; }
        public float Health { get; private set; }
        public float MaximumHealth => Mathf.Max(1, maximumHealth);
        public TruckBossState State { get; private set; } = TruckBossState.Wrecked;
        public override bool Alive => gameObject.activeInHierarchy && Health > 0 && State != TruckBossState.Wrecked;
        public bool IsAlive => Alive;
        public override Vector3 AimPoint => aimAnchor ? aimAnchor.position : transform.TransformPoint(new Vector3(0, 1.55f, .25f));
        public bool AIEnabled { get; set; } = true;
        public bool RewardGranted { get; private set; }
        public int ShotsFired { get; private set; }
        public int PlayerHits { get; private set; }
        public int RamsStarted { get; private set; }
        public int RamsLanded { get; private set; }
        public bool IsTelegraphing => Alive && (State == TruckBossState.RamWindup || gunPhase == GunPhase.Warning);
        public bool IsGunTelegraphing => Alive && gunPhase == GunPhase.Warning;
        public bool IsFiringBurst => Alive && gunPhase == GunPhase.Burst;
        public float AttackProgress => State == TruckBossState.RamWindup ? Mathf.Clamp01((Time.time - ramWarningStarted) / Mathf.Max(.3f, ramWindup)) :
            gunPhase == GunPhase.Warning ? Mathf.Clamp01((Time.time - gunWarningStarted) / Mathf.Max(.3f, gunWindup)) : 0;
        public event Action Changed;

        enum GunPhase { Rest, Warning, Burst }
        GunPhase gunPhase;
        readonly RaycastHit[] shotHits = new RaycastHit[24];
        Vector3 homePosition, centerPosition, chargeEnd, lockedForward, visualPosition;
        Quaternion homeRotation, visualRotation, pitchRest, yawRest;
        Quaternion[] wheelRest;
        bool initialized, homeCaptured, wasPlaying, ramConnected, flashApplied;
        float nextDestination, nextPatrol, nextRam, ramWarningStarted, chargeAt, chargeUntil, recoverUntil, wreckUntil, nextPlayerRam;
        float gunReadyAt, gunWarningStarted, burstUntil, nextShotAt, flashUntil, armorFlashUntil, wheelSpin, wheelSteer, retryNavigationAt;
        int patrolIndex, burstShots;
        Collider playerCollider;
        MaterialPropertyBlock flashBlock;
        ObstacleAvoidanceType normalAvoidance = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        Vector3 ArenaPoint => arenaCenter ? arenaCenter.position : centerPosition;
        float MovementRadius => Mathf.Max(1.5f, arenaRadius - Mathf.Max(0, bodyLeashPadding));
        float EffectiveRange => session && session.turret ? Mathf.Min(playerRange, session.turret.Range) : playerRange;
        bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;
        Vector3 PlayerAimPoint => session.vehicle.transform.TransformPoint(new Vector3(0, .65f, .15f));
        Vector3 RamPoint => frontRam ? frontRam.position : transform.TransformPoint(new Vector3(0, .72f, 3.5f));

        void Awake()
        {
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (agent) normalAvoidance = agent.obstacleAvoidanceType;
            Body = GetComponent<Rigidbody>(); ConfigureBody(); CaptureHome(); BindVisuals();
            if (bodyColliders == null || bodyColliders.Length == 0) bodyColliders = GetComponentsInChildren<Collider>(true);
        }
        void ConfigureBody()
        {
            if (!Body) Body = GetComponent<Rigidbody>();
            if (!Body) return;
            Body.isKinematic = true; Body.useGravity = false; Body.mass = 2400;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        void CaptureHome()
        {
            if (homeCaptured) return;
            homePosition = spawnPoint ? spawnPoint.position : transform.position;
            homeRotation = spawnPoint ? spawnPoint.rotation : transform.rotation;
            centerPosition = arenaCenter ? arenaCenter.position : homePosition;
            homeCaptured = true;
        }
        void BindVisuals()
        {
            if (!initialized)
            {
                visualPosition = visual ? visual.localPosition : Vector3.zero;
                visualRotation = visual ? visual.localRotation : Quaternion.identity;
                pitchRest = turretPitch ? turretPitch.localRotation : Quaternion.identity;
                yawRest = turretYaw ? turretYaw.localRotation : Quaternion.identity;
                initialized = true;
            }
            int count = wheelVisuals == null ? 0 : wheelVisuals.Length;
            if (wheelRest == null || wheelRest.Length != count)
            {
                wheelRest = new Quaternion[count];
                for (int i = 0; i < count; i++) wheelRest[i] = wheelVisuals[i] ? wheelVisuals[i].localRotation : Quaternion.identity;
            }
        }
        void SetBodyCollision(bool enabled)
        {
            if (bodyColliders != null) for (int i = 0; i < bodyColliders.Length; i++) if (bodyColliders[i]) bodyColliders[i].enabled = enabled;
            if (frontRamCollider) frontRamCollider.enabled = enabled;
        }
        void StopAgent(bool resetPath = false)
        {
            if (!AgentReady) return;
            agent.isStopped = true;
            if (resetPath) agent.ResetPath();
        }
        Vector3 ClampToArena(Vector3 position)
        {
            Vector3 delta = position - ArenaPoint; delta.y = 0;
            if (delta.sqrMagnitude > MovementRadius * MovementRadius) position = ArenaPoint + delta.normalized * MovementRadius;
            return position;
        }
        bool PlaceOnNavigation(Vector3 position)
        {
            if (!agent) return false;
            StopAgent(true); agent.enabled = false;
            position = ClampToArena(position);
            if (!NavMesh.SamplePosition(position, out var sample, 3, agent.areaMask)) return false;
            if (AttackRange.SquaredDistance(sample.position, ArenaPoint) > MovementRadius * MovementRadius + .1f) return false;
            transform.position = sample.position; agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(sample.position)) { agent.enabled = false; return false; }
            agent.radius = 1.5f; agent.height = 3; agent.baseOffset = 0;
            agent.speed = patrolSpeed; agent.acceleration = 7; agent.angularSpeed = 75;
            agent.stoppingDistance = .35f; agent.autoBraking = true; agent.updateRotation = true; agent.obstacleAvoidanceType = normalAvoidance; agent.isStopped = true;
            return true;
        }
        bool Destination(Vector3 point, float speed)
        {
            if (!AgentReady) return false;
            point = ClampToArena(point);
            if (!NavMesh.SamplePosition(point, out var sample, 1.5f, agent.areaMask)) return false;
            if (AttackRange.SquaredDistance(sample.position, ArenaPoint) > MovementRadius * MovementRadius + .1f) return false;
            agent.speed = speed; agent.isStopped = false; return agent.SetDestination(sample.position);
        }
        public void ResetEncounter()
        {
            CaptureHome(); BindVisuals(); ConfigureBody();
            var position = spawnPoint ? spawnPoint.position : homePosition;
            var rotation = spawnPoint ? spawnPoint.rotation : homeRotation;
            if (agent) agent.enabled = false;
            transform.SetPositionAndRotation(ClampToArena(position), rotation); gameObject.SetActive(true);
            if (Body) { Body.position = transform.position; Body.rotation = rotation; }
            Health = MaximumHealth; State = TruckBossState.Patrol; AIEnabled = true; RewardGranted = false;
            ShotsFired = PlayerHits = RamsStarted = RamsLanded = 0; wheelSpin = wheelSteer = 0;
            nextDestination = nextPatrol = 0; patrolIndex = 0; nextPlayerRam = 0; nextRam = Time.time + 1.3f;
            chargeAt = chargeUntil = recoverUntil = wreckUntil = 0; ramConnected = false;
            flashUntil = armorFlashUntil = 0; retryNavigationAt = Time.time + .5f; wasPlaying = session && session.Playing;
            playerCollider = session && session.vehicle ? session.vehicle.GetComponent<Collider>() : null;
            SetBodyCollision(true); SetTelegraph(false); ResetGun(.9f); ClearFlash();
            if (visual) { visual.localPosition = visualPosition; visual.localRotation = visualRotation; }
            if (turretPitch) turretPitch.localRotation = pitchRest;
            if (turretYaw) turretYaw.localRotation = yawRest;
            if (wheelVisuals != null) for (int i = 0; i < wheelVisuals.Length; i++) if (wheelVisuals[i]) wheelVisuals[i].localRotation = wheelRest[i];
            if (dust) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (smoke) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            PlaceOnNavigation(transform.position); Changed?.Invoke();
        }
        public void Despawn()
        {
            SetTelegraph(false); ResetGun(.9f); StopAgent(true); if (agent) agent.enabled = false;
            SetBodyCollision(false); Health = 0; State = TruckBossState.Wrecked;
            if (dust) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (smoke) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ClearFlash(); gameObject.SetActive(false);
        }
        public void CancelAttack()
        {
            SetTelegraph(false); ResetGun(.75f); StopAgent(true);
            if (AgentReady) { agent.updateRotation = true; agent.autoBraking = true; agent.angularSpeed = 75; agent.acceleration = 7; agent.speed = chaseSpeed; agent.obstacleAvoidanceType = normalAvoidance; }
            nextRam = Time.time + 1; ramConnected = false;
            if (Alive) State = TruckBossState.Chase;
        }
        void SetTelegraph(bool enabled)
        {
            if (telegraph && telegraph.activeSelf != enabled) telegraph.SetActive(enabled);
            if (warningRadius && (!telegraph || warningRadius.gameObject != telegraph) && warningRadius.gameObject.activeSelf != enabled)
                warningRadius.gameObject.SetActive(enabled);
        }
        void ResetGun(float delay)
        {
            gunPhase = GunPhase.Rest; gunReadyAt = Time.time + Mathf.Max(0, delay); burstShots = 0;
            if (flash) flash.gameObject.SetActive(false);
            if (gunWarning) gunWarning.SetActive(false);
        }
        void OnDisable()
        {
            SetTelegraph(false); ResetGun(.75f); StopAgent(true); if (agent) agent.enabled = false;
        }
        void Update()
        {
            if (flash && flash.gameObject.activeSelf && Time.time >= flashUntil) flash.gameObject.SetActive(false);
            if (!session || !session.vehicle || !session.health) { StopAgent(); SetTelegraph(false); return; }
            if (!session.Playing)
            {
                if (wasPlaying) CancelAttack(); wasPlaying = false; StopAgent(); SetTelegraph(false); return;
            }
            if (State == TruckBossState.Wrecked) { if (Time.time >= wreckUntil) Despawn(); return; }
            if (!Alive) return;
            if (!AIEnabled) { if (wasPlaying) CancelAttack(); wasPlaying = false; StopAgent(); return; }
            if (!wasPlaying) { wasPlaying = true; nextRam = Mathf.Max(nextRam, Time.time + 1); ResetGun(.75f); }
            if (flashApplied && Time.time >= armorFlashUntil) ClearFlash();
            if (!playerCollider) playerCollider = session.vehicle.GetComponent<Collider>();
            if (!AgentReady)
            {
                if (Time.time >= retryNavigationAt) { retryNavigationAt = Time.time + .75f; PlaceOnNavigation(transform.position); }
                if (!AgentReady) return;
            }
            // A NavMesh path may curve around obstacles: enforce the leash on actual movement as well as goals.
            if (AttackRange.SquaredDistance(transform.position, ArenaPoint) > (MovementRadius + .18f) * (MovementRadius + .18f))
            { CancelAttack(); PlaceOnNavigation(ClampToArena(transform.position)); return; }
            if (State == TruckBossState.RamWindup)
            {
                StopAgent(); transform.rotation = Quaternion.LookRotation(lockedForward);
                if (Time.time >= chargeAt) StartCharge(); AimTurret(); return;
            }
            if (State == TruckBossState.Ramming)
            {
                AimTurret(); TryRamContact();
                if (Time.time >= chargeUntil || (!agent.pathPending && agent.remainingDistance < .45f) || !InPlayerRange()) FinishCharge();
                return;
            }
            if (State == TruckBossState.RamRecovery)
            {
                StopAgent(); AimTurret();
                if (Time.time >= recoverUntil) { State = TruckBossState.Chase; nextDestination = 0; }
                UpdateGun(); return;
            }
            Vector3 toPlayer = session.vehicle.transform.position - transform.position; toPlayer.y = 0;
            bool engaged = toPlayer.sqrMagnitude <= aggroRadius * aggroRadius &&
                AttackRange.SquaredDistance(session.vehicle.transform.position, ArenaPoint) <= (arenaRadius + EffectiveRange) * (arenaRadius + EffectiveRange);
            if (!engaged) Patrol();
            else
            {
                State = TruckBossState.Chase;
                AimTurret();
                if (CanStartRam(toPlayer)) { BeginRam(toPlayer); return; }
                bool hold = toPlayer.sqrMagnitude <= preferredRange * preferredRange && HasLineOfSight();
                if (hold) StopAgent();
                else if (Time.time >= nextDestination)
                {
                    nextDestination = Time.time + .3f;
                    Vector3 target = session.vehicle.transform.position;
                    if (toPlayer.sqrMagnitude > .01f) target -= toPlayer.normalized * Mathf.Min(preferredRange - 1, toPlayer.magnitude * .55f);
                    Destination(target, chaseSpeed);
                }
            }
            UpdateGun();
        }
        void Patrol()
        {
            State = TruckBossState.Patrol;
            if (Time.time < nextPatrol && (agent.pathPending || agent.remainingDistance > .7f)) return;
            if (Time.time < nextPatrol && !agent.hasPath) return;
            float angle = (patrolIndex++ * 137.5f + 35) * Mathf.Deg2Rad;
            float radius = MovementRadius * .72f;
            Vector3 target = ArenaPoint + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
            nextPatrol = Time.time + 5.5f; Destination(target, patrolSpeed);
        }
        bool InPlayerRange() => session && session.vehicle && AttackRange.Contains(transform.position, session.vehicle.transform.position, EffectiveRange);
        bool CanStartRam(Vector3 toPlayer)
        {
            if (Time.time < nextRam || !InPlayerRange() || toPlayer.sqrMagnitude < .1f) return false;
            if (Vector3.Angle(transform.forward, toPlayer) > 42) return false;
            Vector3 boundary = playerCollider ? playerCollider.ClosestPoint(RamPoint) : session.vehicle.transform.position;
            Vector3 delta = boundary - RamPoint; delta.y = 0;
            if (delta.sqrMagnitude > 3.0f * 3.0f) return false;
            return TryChargeEnd(toPlayer.normalized, out _) && ClearRamSight();
        }
        bool ClearRamSight()
        {
            if (playerCollider && (playerCollider.ClosestPoint(RamPoint) - RamPoint).sqrMagnitude < .001f) return true;
            Vector3 delta = PlayerAimPoint - RamPoint;
            return delta.sqrMagnitude > .001f && TracePlayer(RamPoint, delta.normalized, delta.magnitude + .25f, out var first) &&
                (first.rigidbody == session.vehicle.Body || first.collider.transform.IsChildOf(session.vehicle.transform));
        }
        bool TryChargeEnd(Vector3 direction, out Vector3 end)
        {
            // Intersect a straight charge ray with the circular leash. Radially clamping an arbitrary end
            // would make a truck near the edge slide sideways, outside the warned lane.
            Vector3 offset = transform.position - ArenaPoint; offset.y = 0;
            float projection = Vector3.Dot(offset, direction);
            float discriminant = projection * projection + MovementRadius * MovementRadius - offset.sqrMagnitude;
            float available = discriminant > 0 ? -projection + Mathf.Sqrt(discriminant) : 0;
            float travel = Mathf.Min(ramDistance, Mathf.Max(0, available - .12f));
            end = transform.position + direction * travel;
            if (travel < .85f) return false;
            if (NavMesh.Raycast(transform.position, end, out var boundary, agent.areaMask))
            {
                travel = Mathf.Max(0, Vector3.Dot(boundary.position - transform.position, direction) - .45f);
                end = transform.position + direction * travel;
            }
            return travel >= .85f;
        }
        void BeginRam(Vector3 toPlayer)
        {
            State = TruckBossState.RamWindup; StopAgent(true); ResetGun(gunRest);
            lockedForward = toPlayer.normalized;
            if (!TryChargeEnd(lockedForward, out chargeEnd)) { State = TruckBossState.Chase; nextRam = Time.time + 1; return; }
            transform.rotation = Quaternion.LookRotation(lockedForward);
            ramWarningStarted = Time.time; chargeAt = Time.time + Mathf.Max(.3f, ramWindup); ramConnected = false;
            nextRam = chargeAt + Mathf.Max(.5f, ramCooldown); RamsStarted++;
            if (warningRadius)
            {
                warningRadius.position = transform.position + Vector3.up * .05f;
                warningRadius.rotation = Quaternion.LookRotation(lockedForward);
                warningRadius.localScale = new Vector3(ramHalfWidth, 1, Mathf.Max(1, (chargeEnd - transform.position).magnitude + 3.5f));
            }
            else if (telegraph) { telegraph.transform.position = transform.position + Vector3.up * .05f; telegraph.transform.rotation = Quaternion.LookRotation(lockedForward); }
            SetTelegraph(true);
        }
        void StartCharge()
        {
            SetTelegraph(false); State = TruckBossState.Ramming;
            if (!AgentReady || !InPlayerRange()) { FinishCharge(); return; }
            agent.updateRotation = false; agent.autoBraking = false; agent.acceleration = 20; agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            chargeUntil = Time.time + Mathf.Min(1.65f, (chargeEnd - transform.position).magnitude / Mathf.Max(.1f, chargeSpeed) + .28f);
            if (!Destination(chargeEnd, chargeSpeed)) FinishCharge();
        }
        void FinishCharge()
        {
            SetTelegraph(false); StopAgent(true); State = TruckBossState.RamRecovery; recoverUntil = Time.time + .65f;
            if (AgentReady) { agent.updateRotation = true; agent.autoBraking = true; agent.angularSpeed = 75; agent.acceleration = 7; agent.obstacleAvoidanceType = normalAvoidance; }
            nextRam = Mathf.Max(nextRam, Time.time + Mathf.Max(.5f, ramCooldown)); ResetGun(.9f);
        }
        void TryRamContact()
        {
            if (ramConnected || !session.Playing || !InPlayerRange() || !playerCollider) return;
            Vector3 boundary = playerCollider.ClosestPoint(RamPoint);
            Vector3 delta = boundary - RamPoint; delta.y = 0;
            bool contact = delta.sqrMagnitude <= .38f * .38f;
            if (!contact && frontRamCollider)
                contact = Physics.ComputePenetration(frontRamCollider, frontRamCollider.transform.position, frontRamCollider.transform.rotation,
                    playerCollider, playerCollider.transform.position, playerCollider.transform.rotation, out _, out _);
            Vector3 toPlayer = session.vehicle.transform.position - transform.position; toPlayer.y = 0;
            if (contact && Vector3.Dot(lockedForward, toPlayer) > 0) RamImpact(boundary);
        }
        void OnCollisionStay(Collision collision)
        {
            if (State != TruckBossState.Ramming || ramConnected || !session || !session.Playing || collision.rigidbody != session.vehicle.Body) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i); Vector3 local = transform.InverseTransformPoint(contact.point);
                if (local.z >= 2.65f && Mathf.Abs(local.x) <= ramHalfWidth + .2f) { RamImpact(contact.point); return; }
            }
        }
        void RamImpact(Vector3 at)
        {
            if (ramConnected || !session.Playing || !InPlayerRange()) return;
            ramConnected = true;
            if (session.health.Damage(ramDamage)) RamsLanded++;
            if (session.effects) session.effects.Spark(at);
            // The collision and player Rigidbody remain authoritative; no teleport or hidden knockback force.
            FinishCharge();
        }
        void AimTurret()
        {
            if (!turretYaw || !turretPitch || !session || !session.vehicle || !InPlayerRange()) return;
            Vector3 horizontal = Vector3.ProjectOnPlane(PlayerAimPoint - turretYaw.position, Vector3.up);
            if (horizontal.sqrMagnitude > .001f) turretYaw.rotation = Quaternion.RotateTowards(turretYaw.rotation, Quaternion.LookRotation(horizontal), Time.deltaTime * 100);
            Vector3 local = turretYaw.InverseTransformDirection(PlayerAimPoint - turretPitch.position);
            float elevation = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            turretPitch.localRotation = Quaternion.RotateTowards(turretPitch.localRotation, pitchRest * Quaternion.Euler(Mathf.Clamp(elevation, -18, 68), 0, 0), Time.deltaTime * 100);
        }
        bool TracePlayer(Vector3 from, Vector3 direction, float length, out RaycastHit first)
        {
            first = default; int count = Physics.RaycastNonAlloc(from, direction, shotHits, length, hitMask, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity; bool found = false;
            for (int i = 0; i < count; i++)
            {
                var collider = shotHits[i].collider; if (!collider || collider.transform.IsChildOf(transform)) continue;
                if (shotHits[i].distance < distance) { distance = shotHits[i].distance; first = shotHits[i]; found = true; }
            }
            return found;
        }
        public bool HasLineOfSight()
        {
            if (!Alive || !session || !session.Playing || !muzzle || !InPlayerRange()) return false;
            Vector3 delta = PlayerAimPoint - muzzle.position;
            return delta.sqrMagnitude > .001f && TracePlayer(muzzle.position, delta.normalized, delta.magnitude + .3f, out var first) &&
                (first.rigidbody == session.vehicle.Body || first.collider.transform.IsChildOf(session.vehicle.transform));
        }
        void UpdateGun()
        {
            if (!muzzle || !session.Playing || State == TruckBossState.RamWindup || State == TruckBossState.Ramming || !HasLineOfSight())
            { if (gunPhase != GunPhase.Rest) ResetGun(gunRest); return; }
            AimTurret();
            if (gunPhase == GunPhase.Rest)
            {
                if (Time.time < gunReadyAt) return;
                gunPhase = GunPhase.Warning; gunWarningStarted = Time.time;
                if (gunWarning) gunWarning.SetActive(true); return;
            }
            if (gunPhase == GunPhase.Warning)
            {
                if (Time.time - gunWarningStarted < Mathf.Max(.3f, gunWindup)) return;
                gunPhase = GunPhase.Burst; burstUntil = Time.time + Mathf.Max(.1f, burstDuration); nextShotAt = Time.time; burstShots = 0;
                if (gunWarning) gunWarning.SetActive(false);
            }
            if (gunPhase == GunPhase.Burst)
            {
                int maxShots = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(.1f, burstDuration) * Mathf.Max(.1f, shotsPerSecond)));
                if (Time.time >= burstUntil || burstShots >= maxShots) { ResetGun(gunRest); return; }
                if (Time.time < nextShotAt || Vector3.Angle(muzzle.forward, PlayerAimPoint - muzzle.position) > 6) return;
                nextShotAt = Time.time + 1 / Mathf.Max(.1f, shotsPerSecond); burstShots++; FireShot();
            }
        }
        void FireShot()
        {
            if (!InPlayerRange() || !HasLineOfSight()) { ResetGun(gunRest); return; }
            float length = (PlayerAimPoint - muzzle.position).magnitude + .3f;
            Vector3 end = muzzle.position + muzzle.forward * length;
            if (TracePlayer(muzzle.position, muzzle.forward, length, out var hit))
            {
                end = hit.point;
                if ((hit.rigidbody == session.vehicle.Body || hit.collider.transform.IsChildOf(session.vehicle.transform)) && session.health.Damage(bulletDamage, false)) PlayerHits++;
                if (session.effects) session.effects.Spark(end);
            }
            ShotsFired++;
            if (session.effects) session.effects.Shot(muzzle.position, end, muzzle.rotation, true);
            if (flash && session.Playing) { flash.gameObject.SetActive(true); flashUntil = Time.time + .065f; }
        }
        void LateUpdate()
        {
            if (!Alive || !session || !session.Playing) return;
            float speed = AgentReady ? Vector3.Dot(agent.velocity, transform.forward) : 0;
            wheelSpin = Mathf.Repeat(wheelSpin + speed / Mathf.Max(.1f, wheelRadius) * Mathf.Rad2Deg * Time.deltaTime, 360);
            float desiredSteer = AgentReady && agent.desiredVelocity.sqrMagnitude > .05f ? Mathf.Clamp(Vector3.SignedAngle(transform.forward, agent.desiredVelocity, Vector3.up), -30, 30) : 0;
            wheelSteer = Mathf.MoveTowards(wheelSteer, desiredSteer, Time.deltaTime * 70);
            if (wheelVisuals != null && wheelRest != null)
                for (int i = 0; i < wheelVisuals.Length; i++) if (wheelVisuals[i])
                {
                    bool front = frontWheels != null && frontWheels.Length > i ? frontWheels[i] : i < 2;
                    wheelVisuals[i].localRotation = wheelRest[i] * Quaternion.Euler(0, front ? wheelSteer : 0, 0) * Quaternion.AngleAxis(wheelSpin, Vector3.right);
                }
            if (visual)
            {
                float motion = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(.1f, chaseSpeed));
                visual.localPosition = visualPosition + Vector3.up * (Mathf.Sin(Time.time * 8) * .025f * motion);
                visual.localRotation = visualRotation * Quaternion.Euler(Mathf.Sin(Time.time * 8) * .5f * motion, 0, -wheelSteer * .025f * motion);
            }
            if (dust)
            {
                var emission = dust.emission; emission.rateOverTime = Mathf.Abs(speed) > 1.5f ? 10 : 0;
                if (!dust.isPlaying && Mathf.Abs(speed) > 1.5f) dust.Play();
            }
        }
        void ClearFlash()
        {
            if (armor != null) for (int i = 0; i < armor.Length; i++) if (armor[i]) armor[i].SetPropertyBlock(null);
            flashApplied = false;
        }
        public override void Hit(float damage, Vector3 direction, bool ram = false)
        {
            if (!Alive || !session || !session.Playing || damage <= 0 || (ram && Time.time < nextPlayerRam)) return;
            if (ram) nextPlayerRam = Time.time + Mathf.Max(.1f, playerRamCooldown);
            Health = Mathf.Max(0, Health - damage); Changed?.Invoke();
            if (Health <= 0)
            {
                State = TruckBossState.Wrecked; wreckUntil = Time.time + Mathf.Max(.1f, wreckLifetime);
                SetTelegraph(false); ResetGun(gunRest); StopAgent(true); if (agent) agent.enabled = false; SetBodyCollision(false); ClearFlash();
                if (dust) dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (smoke) { smoke.Play(); smoke.Emit(8); }
                if (!RewardGranted)
                {
                    RewardGranted = true; session.EnemyDefeated(transform.position, scrapReward, true, direction);
                    if (session.effects) session.effects.Explode(transform.position);
                }
                return;
            }
            armorFlashUntil = Time.time + .12f; flashApplied = true;
            if (armor != null)
            {
                if (flashBlock == null) flashBlock = new MaterialPropertyBlock();
                flashBlock.SetColor("_BaseColor", new Color(1, .64f, .36f));
                for (int i = 0; i < armor.Length; i++) if (armor[i]) armor[i].SetPropertyBlock(flashBlock);
            }
        }
    }
}
