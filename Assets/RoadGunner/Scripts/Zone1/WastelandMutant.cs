using System;
using UnityEngine;
using UnityEngine.AI;

namespace RoadGunner
{
    public enum MutantState { Idle, Chase, Telegraph, SmashRecovery, Dead }

    /// <summary>A heavy, entirely melee encounter. The locked ground warning is the actual damage area.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class WastelandMutant : CombatTarget
    {
        public GameSession session;
        public NavMeshAgent agent;
        public Collider hitbox;
        public Transform spawnPoint;
        public Transform visual, body, head, leftArm, rightArm, leftLeg, rightLeg;
        public Transform aimAnchor;
        public GameObject telegraph;
        [Tooltip("Optional unit-radius ground marker; its position/radius match the locked smash.")]
        public Transform warningRadius;
        public Renderer[] skinRenderers;
        public LayerMask attackMask = ~0;

        [Min(1)] public float maximumHealth = 330;
        [Min(0)] public float aggroRadius = 18;
        [Min(.1f)] public float moveSpeed = 2.25f;
        [Min(.1f)] public float meleeReach = 2.1f;
        [Min(.1f)] public float smashRadius = 2.25f;
        [Min(.1f)] public float smashForwardOffset = 1.8f;
        [Min(.25f)] public float windupDuration = .85f;
        [Min(.2f)] public float attackCooldown = 2;
        [Min(0)] public float smashDamage = 14;
        [Min(0)] public int scrapReward = 5;
        [Min(.1f)] public float corpseLifetime = .8f;
        [Min(.1f)] public float playerRamCooldown = .55f;

        public float Health { get; private set; }
        public float MaximumHealth => Mathf.Max(1, maximumHealth);
        public MutantState State { get; private set; } = MutantState.Dead;
        public override bool Alive => gameObject.activeInHierarchy && Health > 0 && State != MutantState.Dead;
        public bool IsAlive => Alive;
        public override Vector3 AimPoint => aimAnchor ? aimAnchor.position : transform.TransformPoint(new Vector3(0, 1.6f, 0));
        public bool IsTelegraphing => Alive && State == MutantState.Telegraph;
        public float AttackProgress => IsTelegraphing ? Mathf.Clamp01((Time.time - warningStartedAt) / Mathf.Max(.25f, windupDuration)) : 0;
        public bool AIEnabled { get; set; } = true;
        public bool RewardGranted { get; private set; }
        public int SmashesStarted { get; private set; }
        public int SmashesLanded { get; private set; }
        public event Action Changed;

        struct Pose
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public void Bind(Transform value)
            {
                if (transform == value) return;
                transform = value;
                if (value) { position = value.localPosition; rotation = value.localRotation; }
            }
            public void Reset()
            {
                if (!transform) return;
                transform.localPosition = position; transform.localRotation = rotation;
            }
            public void Rotate(float x, float y = 0, float z = 0)
            { if (transform) transform.localRotation = rotation * Quaternion.Euler(x, y, z); }
        }
        Pose visualPose, bodyPose, headPose, leftArmPose, rightArmPose, leftLegPose, rightLegPose;
        readonly RaycastHit[] sightHits = new RaycastHit[24];
        Vector3 homePosition, smashPoint;
        Quaternion homeRotation;
        bool homeCaptured, wasPlaying;
        float nextDestination, nextAttack, warningStartedAt, strikeAt, recoverAt, despawnAt, nextRam, flashUntil, retryNavigationAt;
        Collider playerCollider;
        MaterialPropertyBlock flashBlock;
        bool flashApplied;

        void Awake()
        {
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!hitbox) hitbox = GetComponent<Collider>();
            BindVisuals(); CaptureHome();
        }
        void CaptureHome()
        {
            if (homeCaptured) return;
            homePosition = spawnPoint ? spawnPoint.position : transform.position;
            homeRotation = spawnPoint ? spawnPoint.rotation : transform.rotation;
            homeCaptured = true;
        }
        void BindVisuals()
        {
            visualPose.Bind(visual); bodyPose.Bind(body); headPose.Bind(head);
            leftArmPose.Bind(leftArm); rightArmPose.Bind(rightArm); leftLegPose.Bind(leftLeg); rightLegPose.Bind(rightLeg);
        }
        void RestorePose()
        {
            visualPose.Reset(); bodyPose.Reset(); headPose.Reset(); leftArmPose.Reset(); rightArmPose.Reset(); leftLegPose.Reset(); rightLegPose.Reset();
        }
        bool AgentReady => agent && agent.enabled && agent.isOnNavMesh;
        void StopAgent(bool resetPath = false)
        {
            if (!AgentReady) return;
            agent.isStopped = true;
            if (resetPath) agent.ResetPath();
        }
        bool PlaceOnNavigation(Vector3 position)
        {
            if (!agent) return false;
            if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            if (!NavMesh.SamplePosition(position, out var sample, 3, agent.areaMask)) return false;
            transform.position = sample.position;
            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(sample.position)) { agent.enabled = false; return false; }
            agent.speed = moveSpeed; agent.acceleration = 9; agent.angularSpeed = 150;
            agent.stoppingDistance = .35f; agent.autoBraking = true; agent.updateRotation = true; agent.baseOffset = 0;
            agent.isStopped = true; return true;
        }
        public void ResetEncounter()
        {
            CaptureHome(); BindVisuals();
            var position = spawnPoint ? spawnPoint.position : homePosition;
            var rotation = spawnPoint ? spawnPoint.rotation : homeRotation;
            if (agent) agent.enabled = false;
            transform.SetPositionAndRotation(position, rotation); gameObject.SetActive(true);
            Health = MaximumHealth; State = MutantState.Idle; AIEnabled = true; RewardGranted = false;
            SmashesStarted = SmashesLanded = 0; nextRam = 0; flashUntil = 0;
            nextAttack = Time.time + .9f; nextDestination = 0; retryNavigationAt = Time.time + .5f;
            warningStartedAt = strikeAt = recoverAt = despawnAt = 0; wasPlaying = session && session.Playing;
            if (hitbox) hitbox.enabled = true;
            playerCollider = session && session.vehicle ? session.vehicle.GetComponent<Collider>() : null;
            RestorePose(); SetWarning(false); ClearFlash(); PlaceOnNavigation(position); Changed?.Invoke();
        }
        public void Despawn()
        {
            SetWarning(false); StopAgent(true);
            if (agent) agent.enabled = false;
            if (hitbox) hitbox.enabled = false;
            Health = 0; State = MutantState.Dead; ClearFlash(); gameObject.SetActive(false);
        }
        public void CancelAttack()
        {
            SetWarning(false); nextAttack = Time.time + .65f; strikeAt = 0;
            if (Alive) { State = MutantState.Chase; StopAgent(true); PoseIdle(); }
        }
        void SetWarning(bool enabled)
        {
            if (telegraph && telegraph.activeSelf != enabled) telegraph.SetActive(enabled);
            if (warningRadius && (!telegraph || warningRadius.gameObject != telegraph) && warningRadius.gameObject.activeSelf != enabled)
                warningRadius.gameObject.SetActive(enabled);
        }
        void OnDisable()
        {
            SetWarning(false); StopAgent(true); if (agent) agent.enabled = false; ClearFlash();
        }
        void Update()
        {
            if (!session || !session.vehicle || !session.health) { StopAgent(); SetWarning(false); return; }
            if (!session.Playing)
            {
                if (wasPlaying) CancelAttack();
                wasPlaying = false; StopAgent(); SetWarning(false); return;
            }
            if (State == MutantState.Dead)
            {
                if (visual) { visual.localRotation = Quaternion.Slerp(visual.localRotation, visualPose.rotation * Quaternion.Euler(82, 0, 13), Time.deltaTime * 9); }
                if (Time.time >= despawnAt) Despawn(); return;
            }
            if (!Alive) return;
            if (!AIEnabled) { if (wasPlaying) CancelAttack(); wasPlaying = false; StopAgent(); SetWarning(false); return; }
            if (!wasPlaying) { wasPlaying = true; nextAttack = Mathf.Max(nextAttack, Time.time + .65f); }
            if (flashApplied && Time.time >= flashUntil) ClearFlash();
            if (!playerCollider) playerCollider = session.vehicle.GetComponent<Collider>();
            if (!AgentReady)
            {
                if (Time.time >= retryNavigationAt) { retryNavigationAt = Time.time + .75f; PlaceOnNavigation(transform.position); }
                if (!AgentReady) { PoseIdle(); return; }
            }
            if (State == MutantState.Telegraph)
            {
                StopAgent(); AnimateWindup(AttackProgress);
                if (Time.time >= strikeAt) ResolveSmash(); return;
            }
            if (State == MutantState.SmashRecovery)
            {
                StopAgent(); AnimateRecovery(Mathf.Clamp01((Time.time - (recoverAt - .38f)) / .38f));
                if (Time.time >= recoverAt) { State = MutantState.Chase; PoseIdle(); } return;
            }
            Vector3 toPlayer = session.vehicle.transform.position - transform.position; toPlayer.y = 0;
            if (State == MutantState.Idle && toPlayer.sqrMagnitude > aggroRadius * aggroRadius) { StopAgent(); PoseIdle(); return; }
            State = MutantState.Chase;
            Vector3 boundary = PlayerBoundary(AimPoint);
            Vector3 toBoundary = boundary - transform.position; toBoundary.y = 0;
            bool close = toBoundary.sqrMagnitude <= meleeReach * meleeReach;
            if (close)
            {
                StopAgent();
                if (toPlayer.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer), Time.deltaTime * 180);
                PoseIdle();
                if (Time.time >= nextAttack && Vector3.Angle(transform.forward, toPlayer) < 32 && ClearStrike(AimPoint, boundary)) BeginSmash();
            }
            else
            {
                agent.isStopped = false;
                if (Time.time >= nextDestination)
                {
                    nextDestination = Time.time + .24f;
                    if (NavMesh.SamplePosition(session.vehicle.transform.position, out var destination, 2.5f, agent.areaMask)) agent.SetDestination(destination.position);
                }
                AnimateWalk(Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(.1f, moveSpeed)));
            }
        }
        Vector3 PlayerBoundary(Vector3 from) => playerCollider ? playerCollider.ClosestPoint(from) : session.vehicle.transform.position + Vector3.up * .6f;
        bool ClearStrike(Vector3 from, Vector3 to)
        {
            var delta = to - from; if (delta.sqrMagnitude < .001f) return true;
            int count = Physics.RaycastNonAlloc(from, delta.normalized, sightHits, delta.magnitude + .1f, attackMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity; Collider blocker = null;
            for (int i = 0; i < count; i++)
            {
                var collider = sightHits[i].collider; if (!collider || collider.transform.IsChildOf(transform)) continue;
                if (sightHits[i].distance < nearest) { nearest = sightHits[i].distance; blocker = collider; }
            }
            return !blocker || blocker == playerCollider || blocker.transform.IsChildOf(session.vehicle.transform);
        }
        void BeginSmash()
        {
            State = MutantState.Telegraph; StopAgent(true);
            warningStartedAt = Time.time; strikeAt = Time.time + Mathf.Max(.25f, windupDuration);
            nextAttack = strikeAt + Mathf.Max(.2f, attackCooldown);
            smashPoint = transform.position + transform.forward * smashForwardOffset;
            if (warningRadius)
            {
                warningRadius.position = smashPoint + Vector3.up * .045f;
                warningRadius.rotation = Quaternion.identity;
                warningRadius.localScale = new Vector3(smashRadius, 1, smashRadius);
            }
            else if (telegraph) telegraph.transform.position = smashPoint + Vector3.up * .045f;
            SetWarning(true); SmashesStarted++;
        }
        void ResolveSmash()
        {
            SetWarning(false); State = MutantState.SmashRecovery; recoverAt = Time.time + .38f;
            Vector3 boundary = PlayerBoundary(smashPoint + Vector3.up * .55f);
            Vector3 delta = boundary - smashPoint; delta.y = 0;
            bool inside = delta.sqrMagnitude <= smashRadius * smashRadius;
            if (inside && ClearStrike(smashPoint + Vector3.up * .55f, boundary) && session.health.Damage(smashDamage)) SmashesLanded++;
            if (session.effects) session.effects.Spark(smashPoint + Vector3.up * .08f);
        }
        void PoseIdle()
        {
            bodyPose.Rotate(0); headPose.Rotate(0); leftArmPose.Rotate(-8, 0, 8); rightArmPose.Rotate(-8, 0, -8);
            leftLegPose.Rotate(0); rightLegPose.Rotate(0);
            if (body) body.localPosition = bodyPose.position;
        }
        void AnimateWalk(float amount)
        {
            float phase = Time.time * 6.4f; float swing = Mathf.Sin(phase) * 22 * amount;
            leftLegPose.Rotate(swing); rightLegPose.Rotate(-swing);
            leftArmPose.Rotate(-swing * .7f - 8, 0, 8); rightArmPose.Rotate(swing * .7f - 8, 0, -8);
            bodyPose.Rotate(3 * amount, 0, Mathf.Sin(phase) * 2 * amount); headPose.Rotate(-3 * amount);
            if (body) body.localPosition = bodyPose.position + Vector3.up * (Mathf.Abs(Mathf.Sin(phase)) * .055f * amount);
        }
        void AnimateWindup(float progress)
        {
            float lift = Mathf.SmoothStep(0, 1, progress);
            leftArmPose.Rotate(-145 * lift, -8, 18); rightArmPose.Rotate(-145 * lift, 8, -18);
            bodyPose.Rotate(-12 * lift); headPose.Rotate(11 * lift); leftLegPose.Rotate(-7); rightLegPose.Rotate(5);
            if (body) body.localPosition = bodyPose.position - Vector3.up * (.07f * lift);
        }
        void AnimateRecovery(float progress)
        {
            float release = 1 - progress; leftArmPose.Rotate(-15 * release, 0, 12); rightArmPose.Rotate(-15 * release, 0, -12);
            bodyPose.Rotate(28 * release); headPose.Rotate(-10 * release);
            if (body) body.localPosition = bodyPose.position - Vector3.up * (.12f * release);
        }
        void ClearFlash()
        {
            if (skinRenderers != null) for (int i = 0; i < skinRenderers.Length; i++) if (skinRenderers[i]) skinRenderers[i].SetPropertyBlock(null);
            flashApplied = false;
        }
        public override void Hit(float damage, Vector3 direction, bool ram = false)
        {
            if (!Alive || !session || !session.Playing || damage <= 0 || (ram && Time.time < nextRam)) return;
            if (ram) nextRam = Time.time + Mathf.Max(.1f, playerRamCooldown);
            Health = Mathf.Max(0, Health - damage); Changed?.Invoke();
            if (Health <= 0)
            {
                State = MutantState.Dead; SetWarning(false); StopAgent(true);
                if (agent) agent.enabled = false;
                if (hitbox) hitbox.enabled = false;
                despawnAt = Time.time + Mathf.Max(.1f, corpseLifetime); ClearFlash();
                if (!RewardGranted) { RewardGranted = true; session.EnemyDefeated(transform.position, scrapReward, true, direction); }
                return;
            }
            // Heavy attacks are committed; small bullets do not silently cancel a warning the player is dodging.
            flashUntil = Time.time + .12f; flashApplied = true;
            if (skinRenderers != null)
            {
                if (flashBlock == null) flashBlock = new MaterialPropertyBlock();
                flashBlock.SetColor("_BaseColor", new Color(1, .64f, .36f));
                for (int i = 0; i < skinRenderers.Length; i++) if (skinRenderers[i]) skinRenderers[i].SetPropertyBlock(flashBlock);
            }
        }
    }
}
