using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadGunner
{
    /// <summary>
    /// Articulated Armored Core-style bipedal walker leg & torso locomotion visuals.
    /// The four original WheelColliders remain the sole physical ground contacts; this component
    /// never applies forces, changes inputs, or adds colliders. Each leg's stride phase advances
    /// from its measured ground-contact displacement and drives 2-bone analytical IK (Hip -> Knee -> Ankle -> Foot)
    /// plus synchronized pelvis, torso, shoulder-cannon, and left-arm harpoon motion.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    [DisallowMultipleComponent]
    public sealed class PlayerTrackVisuals : MonoBehaviour
    {
        public VehicleController vehicle;
        public GameSession session;
        public WheelCollider frontLeft, rearLeft, frontRight, rearRight;
        public MeshFilter leftMesh, rightMesh;
        public Renderer[] hiddenWheelRenderers = Array.Empty<Renderer>();
        [Range(32, 56)] public int treadsPerSide = 44;
        [Range(.45f, .8f)] public float trackWidth = .64f;
        [Range(.045f, .1f)] public float shoeThickness = .074f;
        [Range(.02f, .07f)] public float grouserHeight = .045f;
        [Range(0, .12f)] public float returnSag = .045f;
        [Header("Walker feel — cosmetic only")]
        [Range(.15f,.25f)] public float bodySettleSeconds = .20f;
        [Range(0,4)] public float motionLeanDegrees = 2.2f;
        [Range(0,.16f)] public float landingCompression = .09f;
        [Range(0,2)] public float footfallIntensity = 1;

        const float StrideLength = 2.48f;
        const float ThighLength = 0.68f;
        const float ShinLength = 0.74f;
        const float FootAnkleHeight = 0.16f;
        const float HipHeightAboveGround = 1.38f;
        const float HipLateralOffset = 0.72f;
        const float FootLateralOffset = 0.78f;
        const float StanceFraction = 0.54f;

        public readonly struct FootContact
        {
            public readonly Vector3 Position, Normal, Forward;
            public readonly bool IsLeft;
            public readonly float Intensity;
            public readonly int PlantId;
            public FootContact(Vector3 position, Vector3 normal, Vector3 forward, bool isLeft, float intensity = 1, int plantId = 0)
            { Position = position; Normal = normal; Forward = forward; IsLeft = isLeft; Intensity = intensity; PlantId = plantId; }
        }
        /// <summary>One actual, supported swing-to-stance landing; never a wheel-track sample.</summary>
        public event Action<FootContact> FootPlanted;
        public int PlantedFootCount { get; private set; }
        public bool IsWalkingRig => left != null && right != null && HasValidRig();
        public Vector3 LeftFootWorldPosition => left == null ? transform.position : left.CurrentSole;
        public Vector3 RightFootWorldPosition => right == null ? transform.position : right.CurrentSole;
        public bool LeftFootInStance => left != null && left.HasPlant && !left.Swinging;
        public bool RightFootInStance => right != null && right.HasPlant && !right.Swinging;
        public float VisualReactionMagnitude => reactionPosition.magnitude + reactionAngles.magnitude * .01f;

        public float LeftPhase => left == null ? 0 : Mathf.Repeat(LeftDistanceMeters / left.LoopLength, 1);
        public float RightPhase => right == null ? 0 : Mathf.Repeat(RightDistanceMeters / right.LoopLength, 1);
        public float LeftDistanceMeters { get; private set; }
        public float RightDistanceMeters { get; private set; }
        public float LeftBeltSpeed { get; private set; }
        public float RightBeltSpeed { get; private set; }
        public int TreadsPerSide => treadsPerSide;
        public int SupportRollersPerSide => 6;
        public int SideAssemblyCount => (leftMesh ? 1 : 0) + (rightMesh ? 1 : 0);
        public float LeftLoopLength => left == null ? 0 : left.LoopLength;
        public float RightLoopLength => right == null ? 0 : right.LoopLength;

        Side left, right;
        Vector3 previousLeft, previousRight, previousForward;
        int runSerial, poseResetSerial;
        bool sampled;
        readonly RaycastHit[] footGroundHits = new RaycastHit[16];
        Vector3 previousBodyPosition,previousPlanarVelocity;
        Vector3 reactionPosition,reactionVelocity,reactionAngles,reactionAngularVelocity;
        int observedLanding;
        bool sharpTurn;
        float planarSpeed;
        bool WalkerMode => vehicle && vehicle.tuning && vehicle.tuning.walkerDrive;

        /// <summary>One bounded visual kick from the actual cannon firing event.</summary>
        public void CannonImpulse(float strength01) { AddReaction(new Vector3(0,0,-.012f),new Vector3(-1.1f,0,0),strength01); }
        public void ImpactImpulse(Vector3 worldDirection,float strength01)
        {
            var local=transform.InverseTransformDirection(Vector3.ProjectOnPlane(worldDirection,Vector3.up).normalized);
            AddReaction(new Vector3(local.x,0,local.z)*.025f,new Vector3(-local.z*2,0,local.x*2),strength01);
        }
        void AddReaction(Vector3 offset,Vector3 angles,float strength)
        {
            if(!Application.isPlaying||(session&&!session.Playing))return;
            float rate=8/Mathf.Clamp(bodySettleSeconds,.15f,.25f),gain=rate*2.71828f*Mathf.Clamp01(strength);
            reactionVelocity=Vector3.ClampMagnitude(reactionVelocity+offset*gain,12);
            reactionAngularVelocity=Vector3.ClampMagnitude(reactionAngularVelocity+angles*gain,500);
        }
        static void DecayReaction(ref Vector3 position,ref Vector3 velocity,float rate,float dt)
        {
            var c=velocity+position*rate;float decay=Mathf.Exp(-rate*dt);
            velocity=(velocity-c*rate*dt)*decay;position=(position+c*dt)*decay;
        }

        // Upper mech visual hierarchy nodes (under "Replaceable visuals")
        Transform visualRoot;
        Transform pelvisNode;      // "Concept · Hunched harpoon mechanic" -> Mech Pelvis & Waist Core
        Transform torsoNode;       // "Concept · Body and open cargo bed"  -> Mech Upper Chest, Head & Booster Pack
        Transform rightArmNode;    // "Concept · Cabin driver"             -> Mech Right Arm & Stabilizer Gauntlet
        Transform shoulderGunNode; // "RoofMachineGun"                     -> Right Shoulder Heavy Autocannon Mount
        Transform leftArmMount;    // "RearCompanionArea/HarpoonMount"     -> Left Shoulder Harpoon Arm Socket
        Transform leftArmVisual;   // "Concept · Rear mechanical harpoon launcher" -> Left Arm Harpoon Gauntlet Mesh
        HarpoonCollector harpoon;

        Vector3 baseShoulderGunPos = new Vector3(0.56f, 1.965f, -0.12f);
        Vector3 baseLeftArmMountPos = new Vector3(-0.84f, 1.92f, 0.14f);
        float walkBlend;
        float smoothedForwardSpeed;
        float smoothedTurnSpeed;
        float leftArmAimBlend;
        float launchRecoilTimer;
        HarpoonState previousHarpoonState;

        // Shared stride state computed each frame for both legs + upper torso synchronization
        float currentPelvisBobY;
        float currentPelvisSwayX;
        float currentPelvisBobZ;
        Quaternion currentPelvisRot = Quaternion.identity;

        void Awake()
        {
            if (!HasValidRig()) { Debug.LogError("PLAYER TRACKS: incomplete player-only visual rig.", this); enabled = false; return; }
            BindUpperMechNodes();
            left = BuildSide(true); right = BuildSide(false);
            left.Mesh.hideFlags = right.Mesh.hideFlags = HideFlags.DontSave;
            leftMesh.sharedMesh = left.Mesh; rightMesh.sharedMesh = right.Mesh;
            foreach (var renderer in hiddenWheelRenderers) if (renderer) renderer.enabled = false;
            ResetMotion();
        }

        bool HasValidRig() => vehicle && frontLeft && rearLeft && frontRight && rearRight && leftMesh && rightMesh;

        void BindUpperMechNodes()
        {
            if (!vehicle) return;
            harpoon = vehicle.GetComponent<HarpoonCollector>();
            visualRoot = vehicle.transform.Find("Replaceable visuals");
            if (!visualRoot) return;

            torsoNode = Preferred(visualRoot, "Walker torso", "Concept \u00B7 Body and open cargo bed");
            rightArmNode = Preferred(visualRoot, "Walker right arm", "Concept \u00B7 Cabin driver");
            pelvisNode = Preferred(visualRoot, "Walker pelvis", "Concept \u00B7 Hunched harpoon mechanic");
            shoulderGunNode = visualRoot.Find("RoofMachineGun");
            if (shoulderGunNode) baseShoulderGunPos = shoulderGunNode.localPosition;

            leftArmMount = visualRoot.Find("RearCompanionArea/HarpoonMount");
            if (leftArmMount)
            {
                baseLeftArmMountPos = leftArmMount.localPosition;
                var pivot = leftArmMount.Find("HarpoonPivot");
                if (pivot)
                {
                    leftArmVisual = Preferred(pivot, "Walker harpoon arm", "Concept \u00B7 Rear mechanical harpoon launcher");
                }
            }

            // Include all mech body & leg renderers in the warm damage flash when hit
            var health = vehicle.GetComponent<VehicleHealth>();
            if (health)
            {
                var panels = new List<Renderer>();
                if (torsoNode) panels.AddRange(torsoNode.GetComponentsInChildren<MeshRenderer>(true));
                if (pelvisNode) panels.AddRange(pelvisNode.GetComponentsInChildren<MeshRenderer>(true));
                if (rightArmNode) panels.AddRange(rightArmNode.GetComponentsInChildren<MeshRenderer>(true));
                if (leftArmVisual) panels.AddRange(leftArmVisual.GetComponentsInChildren<MeshRenderer>(true));
                if (leftMesh && leftMesh.TryGetComponent<Renderer>(out var lr)) panels.Add(lr);
                if (rightMesh && rightMesh.TryGetComponent<Renderer>(out var rr)) panels.Add(rr);
                if (panels.Count > 0) health.bodyPanels = panels.ToArray();
            }
        }

        static Transform Preferred(Transform parent, string current, string legacy)
        { var node = parent.Find(current); return node ? node : parent.Find(legacy); }

        /// <summary>Visual phase reset only. Does not alter any physical body or wheel.</summary>
        public void ResetMotion()
        {
            LeftDistanceMeters = RightDistanceMeters = LeftBeltSpeed = RightBeltSpeed = 0;
            walkBlend = smoothedForwardSpeed = smoothedTurnSpeed = leftArmAimBlend = launchRecoilTimer = 0;
            currentPelvisBobY = currentPelvisSwayX = currentPelvisBobZ = 0;
            currentPelvisRot = Quaternion.identity;
            previousHarpoonState = HarpoonState.Idle;
            PlantedFootCount = 0;
            reactionPosition=reactionVelocity=reactionAngles=reactionAngularVelocity=Vector3.zero;
            previousPlanarVelocity=Vector3.zero;sharpTurn=false;planarSpeed=0;
            observedLanding=vehicle?vehicle.LandingSerial:0;
            if(left!=null)ResetLeg(left,true);if(right!=null)ResetLeg(right,false);
            ResetFootTracking();
            sampled = false;
            if (session) runSerial = session.RunSerial;
            if (vehicle) poseResetSerial = vehicle.PoseResetSerial;
            ResetUpperMechPose();
        }

        void ResetFootTracking()
        {
            if (left != null) left.ContactSampled = false;
            if (right != null) right.ContactSampled = false;
        }

        static void ResetLeg(Side side,bool isLeft)
        {side.HasPlant=side.Swinging=side.PendingPlant=side.StepEligible=false;side.GaitPhase=isLeft?0:.5f;side.CurrentSole=Vector3.zero;side.WasMoving=false;}

        void OnDisable() { sampled = false; ResetFootTracking(); }

        void ResetUpperMechPose()
        {
            if (pelvisNode) { pelvisNode.localPosition = Vector3.zero; pelvisNode.localRotation = Quaternion.identity; }
            if (torsoNode) { torsoNode.localPosition = Vector3.zero; torsoNode.localRotation = Quaternion.identity; }
            if (rightArmNode) { rightArmNode.localPosition = Vector3.zero; rightArmNode.localRotation = Quaternion.identity; }
            if (shoulderGunNode) shoulderGunNode.localPosition = baseShoulderGunPos;
            if (leftArmMount) leftArmMount.localPosition = baseLeftArmMountPos;
            if (leftArmVisual) { leftArmVisual.localPosition = Vector3.zero; leftArmVisual.localRotation = Quaternion.identity; }
        }

        void LateUpdate()
        {
            if (left == null || right == null || !HasValidRig()) return;
            if ((session && runSerial != session.RunSerial) || poseResetSerial != vehicle.PoseResetSerial) ResetMotion();
            if ((session && !session.Playing) || Time.deltaTime <= .00001f)
            {
                LeftBeltSpeed = RightBeltSpeed = 0;
                sampled = false; ResetFootTracking();
                return;
            }
            Vector3 l = SideAnchor(frontLeft, rearLeft), r = SideAnchor(frontRight, rearRight);
            Vector3 forward = vehicle.transform.forward;
            float dt = Time.deltaTime;
            // Recover/Restart can reposition the chassis. A teleport is not stride travel.
            float discontinuity = Mathf.Max(4, 80 * dt);
            if (sampled && ((l - previousLeft).sqrMagnitude > discontinuity * discontinuity ||
                            (r - previousRight).sqrMagnitude > discontinuity * discontinuity ||
                            (!WalkerMode&&Vector3.Dot(forward, previousForward) < .5f))) ResetMotion();
            sharpTurn=sampled&&Vector3.Dot(forward,previousForward)<.82f;
            if (sampled && dt > .00001f)
            {
                Vector3 direction = (previousForward + forward).normalized;
                float dl,dr;
                if(WalkerMode)dl=dr=vehicle.WalkerGrounded?Vector3.ProjectOnPlane(vehicle.transform.position-previousBodyPosition,Vector3.up).magnitude:0;
                else {dl=Vector3.Dot(l-previousLeft,direction);dr=Vector3.Dot(r-previousRight,direction);}
                LeftDistanceMeters += dl; RightDistanceMeters += dr;
                if(WalkerMode)LeftBeltSpeed=RightBeltSpeed=vehicle.WalkerGrounded?vehicle.HorizontalSpeed:0;
                else {LeftBeltSpeed = dl / dt; RightBeltSpeed = dr / dt;}
            }
            else LeftBeltSpeed = RightBeltSpeed = 0;
            previousLeft = l; previousRight = r; previousForward = forward; sampled = true;
            previousBodyPosition=vehicle.transform.position;
            if(WalkerMode)UpdateWalkerReaction(dt);

            ComputeLocomotionState(dt);
            UpdateSide(left, true, LeftDistanceMeters);
            UpdateSide(right, false, RightDistanceMeters);
            AnimateUpperMech(dt);
        }

        void UpdateWalkerReaction(float dt)
        {
            Vector3 velocity=Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up);
            planarSpeed=vehicle.WalkerGrounded?velocity.magnitude:0;
            Vector3 change=transform.InverseTransformDirection(velocity-previousPlanarVelocity)/Mathf.Max(1,vehicle.tuning.maxSpeed);
            if(vehicle.WalkerGrounded&&change.magnitude>.06f)
                AddReaction(Vector3.zero,new Vector3(change.z*motionLeanDegrees,0,-change.x*motionLeanDegrees),Mathf.Clamp01(change.magnitude));
            if(sharpTurn&&vehicle.WalkerGrounded)AddReaction(Vector3.zero,new Vector3(0,0,Mathf.Sign(change.x)*motionLeanDegrees),.6f);
            if(observedLanding!=vehicle.LandingSerial)
            {
                observedLanding=vehicle.LandingSerial;
                if(observedLanding>0)AddReaction(Vector3.down*landingCompression,new Vector3(1.5f,0,0),Mathf.Clamp01(vehicle.LandingSpeed/7));
            }
            previousPlanarVelocity=velocity;
            float rate=8/Mathf.Clamp(bodySettleSeconds,.15f,.25f);
            DecayReaction(ref reactionPosition,ref reactionVelocity,rate,dt);
            DecayReaction(ref reactionAngles,ref reactionAngularVelocity,rate,dt);
            reactionPosition=Vector3.ClampMagnitude(reactionPosition,.12f);reactionAngles=Vector3.ClampMagnitude(reactionAngles,5);
        }

        void ComputeLocomotionState(float dt)
        {
            if (!Application.isPlaying)
            {
                walkBlend = 0f;
                currentPelvisBobY = currentPelvisSwayX = currentPelvisBobZ = 0f;
                currentPelvisRot = Quaternion.identity;
                return;
            }

            float avgSpeed = 0.5f * (LeftBeltSpeed + RightBeltSpeed);
            float diffSpeed = RightBeltSpeed - LeftBeltSpeed;
            float maxSideSpeed = Mathf.Max(Mathf.Abs(LeftBeltSpeed), Mathf.Abs(RightBeltSpeed));
            float targetWalk = Mathf.Clamp01((maxSideSpeed - 0.04f) / 1.45f);

            float rate = dt > 0f ? dt : 0.016f;
            walkBlend = Mathf.MoveTowards(walkBlend, targetWalk, rate * 6.5f);
            smoothedForwardSpeed = Mathf.Lerp(smoothedForwardSpeed, avgSpeed, Mathf.Clamp01(rate * 8f));
            smoothedTurnSpeed = Mathf.Lerp(smoothedTurnSpeed, diffSpeed, Mathf.Clamp01(rate * 8f));

            // Unified walk cycle angle from actual ground distance
            float meanDist = 0.5f * (LeftDistanceMeters + RightDistanceMeters);
            float turnDist = 0.5f * (LeftDistanceMeters - RightDistanceMeters);
            float cyclePhase = Mathf.Repeat((Mathf.Abs(meanDist) > 0.01f ? meanDist : turnDist) / StrideLength, 1f);
            float walkRad = cyclePhase * Mathf.PI * 2f;

            // Idle reactor breathing when stationary
            float idlePulse = Mathf.Sin(Time.time * 2.4f) * (1f - walkBlend);

            // Double-frequency vertical pelvis bounce (peaks during mid-stance of each leg, dips at heel-strike)
            float strideBounce = -Mathf.Cos(walkRad * 2f) * 0.068f * walkBlend;
            currentPelvisBobY = strideBounce + idlePulse * 0.016f;

            // Lateral weight transfer toward whichever leg is planted in stance
            currentPelvisSwayX = -Mathf.Sin(walkRad) * 0.062f * walkBlend;
            currentPelvisBobZ = Mathf.Sin(walkRad * 2f) * 0.024f * walkBlend;

            // Pelvis hip-twist & roll with stride
            float pelvisYaw = Mathf.Sin(walkRad) * 6.5f * walkBlend;
            float pelvisRoll = -Mathf.Sin(walkRad) * 3.2f * walkBlend;
            float pelvisPitch = Mathf.Clamp(smoothedForwardSpeed / Mathf.Max(1,vehicle.tuning.maxSpeed), -0.5f, 1f) * 3.0f * walkBlend;
            currentPelvisRot = Quaternion.Euler(pelvisPitch, pelvisYaw, pelvisRoll);
            if(WalkerMode)
            {
                currentPelvisBobY+=reactionPosition.y;
                currentPelvisSwayX+=reactionPosition.x;currentPelvisBobZ+=reactionPosition.z;
                currentPelvisRot*=Quaternion.Euler(reactionAngles*.45f);
            }
        }

        void AnimateUpperMech(float dt)
        {
            if (!Application.isPlaying || !visualRoot) return;

            float meanDist = 0.5f * (LeftDistanceMeters + RightDistanceMeters);
            float turnDist = 0.5f * (LeftDistanceMeters - RightDistanceMeters);
            float cyclePhase = Mathf.Repeat((Mathf.Abs(meanDist) > 0.01f ? meanDist : turnDist) / StrideLength, 1f);
            float walkRad = cyclePhase * Mathf.PI * 2f;
            float idlePulse = Mathf.Sin(Time.time * 2.4f) * (1f - walkBlend);

            Vector3 pelvisOffset = new Vector3(currentPelvisSwayX, currentPelvisBobY, currentPelvisBobZ);
            Vector3 pelvisPivot = new Vector3(0f, 1.38f, 0f);
            if (pelvisNode)
            {
                pelvisNode.localRotation = currentPelvisRot;
                pelvisNode.localPosition = pelvisOffset + pelvisPivot - (currentPelvisRot * pelvisPivot);
            }

            // Torso leans into forward acceleration, banks into turns, and counter-rotates against the pelvis
            float forwardLean = Mathf.Clamp(smoothedForwardSpeed / Mathf.Max(1,vehicle.tuning.maxSpeed), -0.6f, 1f) * 8.5f * walkBlend + idlePulse * 1.1f;
            float turnBank = Mathf.Clamp(-smoothedTurnSpeed * 1.8f, -6.5f, 6.5f) * walkBlend;
            float torsoCounterYaw = -Mathf.Sin(walkRad) * 7.5f * walkBlend;

            // When the left-arm harpoon fires or reels, twist the upper torso slightly toward the harpoon's aim
            bool harpoonActive = harpoon && harpoon.State != HarpoonState.Idle;
            launchRecoilTimer=harpoon?harpoon.LaunchReaction:0;
            previousHarpoonState = harpoon ? harpoon.State : HarpoonState.Idle;

            leftArmAimBlend = Mathf.MoveTowards(leftArmAimBlend, harpoonActive ? 1f : 0f, dt * (harpoonActive ? 12f : 4.5f));

            Quaternion torsoRot = Quaternion.Euler(forwardLean, torsoCounterYaw, turnBank)*Quaternion.Euler(reactionAngles);
            Vector3 torsoPivot = new Vector3(0f, 1.56f, 0f);
            Vector3 torsoOffset = pelvisOffset + new Vector3(0f, Mathf.Sin(walkRad * 2f) * 0.018f * walkBlend, 0f);
            if (torsoNode)
            {
                torsoNode.localRotation = torsoRot;
                torsoNode.localPosition = torsoOffset + torsoPivot - (torsoRot * torsoPivot);
            }

            // Right Arm swings in counter-phase to the right leg (i.e. in phase with left leg)
            if (rightArmNode)
            {
                Vector3 rightShoulderPivot = new Vector3(0.88f, 1.94f, 0.0f);
                Vector3 movedRightShoulder = torsoOffset + torsoPivot + torsoRot * (rightShoulderPivot - torsoPivot);
                float rightArmPitch = -Mathf.Cos(walkRad) * 19f * walkBlend + idlePulse * 1.8f;
                float rightArmRoll = -3.5f * walkBlend;
                Quaternion rightArmRot = torsoRot * Quaternion.Euler(rightArmPitch, 0f, rightArmRoll);
                rightArmNode.localRotation = rightArmRot;
                rightArmNode.localPosition = movedRightShoulder - (rightArmRot * rightShoulderPivot);
            }

            // Right Shoulder Heavy Autocannon follows the torso's right shoulder pylon
            if (shoulderGunNode)
            {
                Vector3 movedPylon = torsoOffset + torsoPivot + torsoRot * (baseShoulderGunPos - torsoPivot);
                shoulderGunNode.localPosition = movedPylon;
            }

            // Left Shoulder Harpoon Arm socket follows the torso's left shoulder
            if (leftArmMount)
            {
                Vector3 movedLeftShoulder = torsoOffset + torsoPivot + torsoRot * (baseLeftArmMountPos - torsoPivot);
                float tension=harpoon?harpoon.CableTension:0;
                leftArmMount.localPosition = movedLeftShoulder+torsoRot*new Vector3(0,-.018f*tension,-.015f*tension);
            }

            // Left Arm Harpoon Gauntlet visual mesh:
            // - While idle/walking: faces forward and counter-swings opposite the left leg
            // - While aiming/launching/retracting: aligns with HarpoonPivot toward the target pickup + launch recoil kick
            if (leftArmVisual && leftArmMount)
            {
                float leftArmPitch = Mathf.Cos(walkRad) * 18f * walkBlend + forwardLean * 0.6f - idlePulse * 1.5f;
                Quaternion idleInPivotSpace = Quaternion.Euler(leftArmPitch,0,3.5f * walkBlend);
                Quaternion activeInPivotSpace = Quaternion.Euler(-launchRecoilTimer * 8f, 0f, 0f);
                leftArmVisual.localRotation = Quaternion.Slerp(idleInPivotSpace, activeInPivotSpace, leftArmAimBlend);
                leftArmVisual.localPosition = new Vector3(0f, -(harpoon?harpoon.DockReaction:0)*.015f, -launchRecoilTimer * 0.16f);
            }
        }

        Vector3 SideAnchor(WheelCollider a, WheelCollider b) =>
            (a.transform.TransformPoint(a.center) + b.transform.TransformPoint(b.center)) * .5f;

        void OnDestroy()
        {
            if (left != null && left.Mesh) Destroy(left.Mesh);
            if (right != null && right.Mesh) Destroy(right.Mesh);
        }

        /// <summary>Used by the narrow editor installer to persist an Edit Mode preview mesh.</summary>
        public Mesh CreatePreviewMesh(bool isLeft)
        {
            if (!HasValidRig()) throw new InvalidOperationException("Player tracks need four original wheel contacts and two mesh filters.");
            return BuildSide(isLeft).Mesh;
        }

        Side BuildSide(bool isLeft)
        {
            ReadShape(isLeft, out Vector3 rear, out Vector3 front, out float radius);
            float span = Vector3.Distance(front, rear), width = trackWidth;
            float loopLength = 2 * span + 2 * Mathf.PI * radius;
            var builder = new Geometry();
            float dir = isLeft ? -1f : 1f;

            // 1. PartKind.Frame: Hip Ball-Joint Housing, Angled Side Skirt Armor & Lateral Attitude Thruster
            builder.Begin(PartKind.Frame, 0, 0);
            // Hip axle bridge connecting pelvis to leg socket
            builder.Cylinder(new Vector3(-dir * 0.14f, 0f, 0f), 0.115f, 0.30f, DarkSteel, 14);
            builder.Cylinder(new Vector3(-dir * 0.04f, 0f, 0f), 0.145f, 0.08f, Brass, 14);
            builder.Cylinder(Vector3.zero, 0.135f, 0.16f, Steel, 14);
            // Upper Armored Core side hip skirt plate & vernier pod
            builder.BevelBox(new Vector3(dir * 0.06f, 0.14f, 0.02f), new Vector3(0.22f, 0.24f, 0.56f), 0.04f, Teal);
            builder.Box(new Vector3(dir * 0.15f, 0.22f, 0.02f), new Vector3(0.08f, 0.07f, 0.48f), Bolt, Quaternion.Euler(0, 0, dir * -12f));
            builder.Box(new Vector3(dir * 0.175f, 0.12f, 0.04f), new Vector3(0.04f, 0.065f, 0.38f), Brass);
            // Lateral vernier thruster nozzle on hip skirt
            builder.Cylinder(new Vector3(dir * 0.19f, 0.14f, -0.14f), 0.068f, 0.08f, DarkSteel, 12);
            builder.Cylinder(new Vector3(dir * 0.225f, 0.14f, -0.14f), 0.048f, 0.04f, Brass, 10);
            builder.End();

            // 2. PartKind.FrontWheel: Upper Thigh Assembly (origin at Hip Joint, extends along -Y to -ThighLength)
            builder.Begin(PartKind.FrontWheel, 1, ThighLength);
            float th = ThighLength;
            // Inner structural femoral beam
            builder.BevelBox(new Vector3(0f, -th * 0.5f, 0f), new Vector3(0.22f, th * 0.92f, 0.26f), 0.035f, DarkSteel);
            // Outer sculpted Armored Core thigh armor casing
            builder.BevelBox(new Vector3(dir * 0.045f, -th * 0.46f, 0.03f), new Vector3(0.32f, th * 0.78f, 0.36f), 0.05f, Teal);
            // High outer femoral shoulder guard jutting above hip joint
            builder.Box(new Vector3(dir * 0.11f, -0.06f, 0.04f), new Vector3(0.14f, 0.26f, 0.32f), Bolt, Quaternion.Euler(0, 0, dir * 10f));
            builder.Box(new Vector3(dir * 0.19f, -th * 0.38f, 0.05f), new Vector3(0.05f, th * 0.52f, 0.12f), Bolt);
            // Forward angled thigh deflectors & yellow/brass hazard chevrons
            builder.Box(new Vector3(0f, -th * 0.42f, 0.19f), new Vector3(0.24f, th * 0.68f, 0.08f), Teal, Quaternion.Euler(-8f, 0, 0));
            builder.Box(new Vector3(dir * 0.02f, -th * 0.28f, 0.235f), new Vector3(0.18f, 0.08f, 0.04f), Brass, Quaternion.Euler(-8f, 0, dir * -12f));
            // Rear femoral hydraulic actuator cylinder & chrome piston rod
            builder.Box(new Vector3(0f, -th * 0.24f, -0.17f), new Vector3(0.14f, th * 0.42f, 0.12f), Brass);
            builder.Box(new Vector3(0f, -th * 0.62f, -0.17f), new Vector3(0.085f, th * 0.46f, 0.085f), BrightSteel);
            for (int i = 0; i < 3; i++)
                builder.Cylinder(new Vector3(dir * 0.205f, -0.16f - i * 0.16f, -0.04f), 0.032f, 0.04f, BrightSteel, 8);
            builder.End();

            // 3. PartKind.RearWheel: Knee Actuator Drum & Forward Knee Breaker Spike (origin at Knee Joint)
            builder.Begin(PartKind.RearWheel, 0, 0.22f);
            builder.Cylinder(Vector3.zero, 0.155f, 0.36f, DarkSteel, 16);
            builder.Cylinder(Vector3.zero, 0.12f, 0.40f, Steel, 14);
            builder.Cylinder(new Vector3(dir * 0.19f, 0f, 0f), 0.095f, 0.05f, Brass, 12);
            builder.Cylinder(new Vector3(dir * 0.21f, 0f, 0f), 0.052f, 0.04f, BrightSteel, 8);
            // Forward projecting Armored Core knee guard & twin ram spikes
            builder.BevelBox(new Vector3(0f, 0.03f, 0.20f), new Vector3(0.28f, 0.24f, 0.22f), 0.04f, Teal);
            builder.Box(new Vector3(0f, 0.11f, 0.26f), new Vector3(0.16f, 0.14f, 0.18f), Bolt, Quaternion.Euler(-18f, 0, 0));
            builder.Box(new Vector3(0f, -0.02f, 0.30f), new Vector3(0.22f, 0.065f, 0.08f), Brass);
            for (int s = -1; s <= 1; s += 2)
                builder.Box(new Vector3(s * 0.075f, 0.02f, 0.36f), new Vector3(0.045f, 0.055f, 0.14f), BrightSteel, Quaternion.Euler(8f, 0, 0));
            builder.End();

            // 4. PartKind.RoadWheel: Lower Shin Greave, Calf Hydraulic Shocks & Jump-Jet Booster (origin at Knee, extends along -Y to -ShinLength)
            builder.Begin(PartKind.RoadWheel, 0.5f, ShinLength);
            float sh = ShinLength;
            // Core structural tibia column
            builder.BevelBox(new Vector3(0f, -sh * 0.5f, -0.02f), new Vector3(0.24f, sh * 0.94f, 0.26f), 0.035f, Steel);
            // Swept Armored Core forward blade-greave armor
            builder.Box(new Vector3(0f, -sh * 0.48f, 0.13f), new Vector3(0.30f, sh * 0.84f, 0.16f), Teal, Quaternion.Euler(6f, 0, 0));
            builder.Box(new Vector3(0f, -sh * 0.46f, 0.205f), new Vector3(0.12f, sh * 0.76f, 0.06f), Bolt, Quaternion.Euler(6f, 0, 0));
            builder.Box(new Vector3(0f, -sh * 0.18f, 0.22f), new Vector3(0.24f, 0.08f, 0.05f), Brass, Quaternion.Euler(6f, 0, 0));
            // Wide flared outer calf armor wing
            builder.Box(new Vector3(dir * 0.16f, -sh * 0.52f, -0.03f), new Vector3(0.12f, sh * 0.78f, 0.38f), Teal, Quaternion.Euler(0, 0, dir * -8f));
            builder.Box(new Vector3(dir * 0.22f, -sh * 0.56f, 0.02f), new Vector3(0.045f, sh * 0.54f, 0.18f), Bolt, Quaternion.Euler(0, 0, dir * -8f));
            builder.Box(new Vector3(dir * 0.21f, -sh * 0.78f, -0.02f), new Vector3(0.06f, 0.14f, 0.34f), Dust);
            // Rear calf jump-jet booster pod & nozzle
            builder.BevelBox(new Vector3(0f, -sh * 0.34f, -0.20f), new Vector3(0.28f, 0.32f, 0.22f), 0.035f, DarkSteel);
            builder.Box(new Vector3(0f, -sh * 0.28f, -0.24f), new Vector3(0.22f, 0.22f, 0.16f), Teal, Quaternion.Euler(18f, 0, 0));
            builder.Box(new Vector3(0f, -sh * 0.48f, -0.26f), new Vector3(0.18f, 0.10f, 0.14f), Brass, Quaternion.Euler(22f, 0, 0));
            // Twin rear hydraulic shock absorbers running down the calf
            for (int s = -1; s <= 1; s += 2)
            {
                builder.Box(new Vector3(s * 0.085f, -sh * 0.42f, -0.15f), new Vector3(0.06f, sh * 0.38f, 0.06f), Brass);
                builder.Box(new Vector3(s * 0.085f, -sh * 0.74f, -0.15f), new Vector3(0.042f, sh * 0.38f, 0.042f), BrightSteel);
            }
            // Flared lower ankle armor collar
            builder.BevelBox(new Vector3(0f, -sh * 0.88f, 0.02f), new Vector3(0.38f, 0.16f, 0.42f), 0.04f, Teal);
            builder.Box(new Vector3(0f, -sh * 0.88f, 0.22f), new Vector3(0.26f, 0.10f, 0.06f), Bolt);
            builder.End();

            // 5. PartKind.ReturnWheel: Ankle Universal Actuator & Damper Struts (origin at Ankle Joint)
            builder.Begin(PartKind.ReturnWheel, 0.5f, 0.14f);
            builder.Cylinder(Vector3.zero, 0.125f, 0.32f, DarkSteel, 14);
            builder.Cylinder(Vector3.zero, 0.085f, 0.38f, Brass, 12);
            for (int s = -1; s <= 1; s += 2)
                builder.Box(new Vector3(s * 0.14f, -0.04f, 0.06f), new Vector3(0.045f, 0.16f, 0.055f), BrightSteel, Quaternion.Euler(-16f, 0, 0));
            builder.End();

            // 6. PartKind.Shoe: 3-Clawed Armored Core Mech Foot & Heel Stabilizer Spur (origin at Ankle Joint, sole at y = -FootAnkleHeight)
            builder.Begin(PartKind.Shoe, 0, FootAnkleHeight);
            float fy = -FootAnkleHeight;
            // Central instep bridge & ankle armor hood
            builder.BevelBox(new Vector3(0f, fy + 0.11f, 0.04f), new Vector3(0.42f, 0.14f, 0.38f), 0.04f, Teal);
            builder.Box(new Vector3(0f, fy + 0.18f, 0.08f), new Vector3(0.26f, 0.06f, 0.28f), Bolt, Quaternion.Euler(10f, 0, 0));
            builder.Cylinder(new Vector3(0f, fy + 0.09f, 0.22f), 0.065f, 0.44f, Brass, 12);
            // Main shock-absorbing rubberized sole plate
            builder.BevelBox(new Vector3(0f, fy + 0.035f, 0.02f), new Vector3(0.48f, 0.07f, 0.54f), 0.03f, Rubber);
            // Twin splayed forward talon-toes
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion toeYaw = Quaternion.Euler(0f, s * 13f, 0f);
                Vector3 toeCenter = new Vector3(s * 0.145f, fy + 0.065f, 0.36f);
                builder.Box(toeCenter, new Vector3(0.16f, 0.11f, 0.34f), Teal, toeYaw);
                builder.Box(toeCenter + new Vector3(0f, -0.04f, 0f), new Vector3(0.17f, 0.05f, 0.36f), Rubber, toeYaw);
                builder.Box(toeCenter + new Vector3(s * 0.02f, 0.05f, 0.02f), new Vector3(0.09f, 0.04f, 0.24f), Bolt, toeYaw);
                // Front steel anchor claw tip
                Vector3 clawPos = new Vector3(s * 0.18f, fy + 0.045f, 0.56f);
                builder.Box(clawPos, new Vector3(0.11f, 0.075f, 0.14f), BrightSteel, toeYaw * Quaternion.Euler(18f, 0, 0));
            }
            // Rear hydraulic heel stabilizer spur
            builder.Box(new Vector3(0f, fy + 0.06f, -0.34f), new Vector3(0.24f, 0.10f, 0.32f), Steel, Quaternion.Euler(-8f, 0, 0));
            builder.Box(new Vector3(0f, fy + 0.025f, -0.36f), new Vector3(0.26f, 0.05f, 0.34f), Rubber);
            builder.Box(new Vector3(0f, fy + 0.05f, -0.52f), new Vector3(0.14f, 0.07f, 0.14f), BrightSteel, Quaternion.Euler(-16f, 0, 0));
            builder.Box(new Vector3(0f, fy + 0.12f, -0.28f), new Vector3(0.16f, 0.06f, 0.18f), Brass, Quaternion.Euler(-12f, 0, 0));
            // Outer lateral stabilizer outrigger pad
            builder.Box(new Vector3(dir * 0.27f, fy + 0.05f, -0.02f), new Vector3(0.12f, 0.08f, 0.28f), Teal, Quaternion.Euler(0, 0, dir * -10f));
            builder.Box(new Vector3(dir * 0.31f, fy + 0.03f, -0.02f), new Vector3(0.08f, 0.05f, 0.24f), BrightSteel);
            builder.End();

            var side = builder.Finish(isLeft ? "Player left articulated mech leg" : "Player right articulated mech leg");
            side.LoopLength = loopLength;
            UpdateSide(side, isLeft, 0);
            return side;
        }

        void ReadShape(bool isLeft, out Vector3 rear, out Vector3 front, out float radius)
        {
            var a = isLeft ? rearLeft : rearRight; var b = isLeft ? frontLeft : frontRight;
            rear = WheelCenter(a); front = WheelCenter(b);
            radius = Mathf.Max(.25f, (a.radius + b.radius) * .5f - shoeThickness * .5f - grouserHeight);
            rear.x = front.x = (rear.x + front.x) * .5f;
        }

        Vector3 WheelCenter(WheelCollider wheel)
        {
            Vector3 position;
            if (Application.isPlaying) wheel.GetWorldPose(out position, out _);
            else position = wheel.transform.TransformPoint(wheel.center) - wheel.transform.up *
                wheel.suspensionDistance * (1 - wheel.suspensionSpring.targetPosition);
            return transform.InverseTransformPoint(position);
        }

        void UpdateSide(Side side, bool isLeft, float distance)
        {
            ReadShape(isLeft, out Vector3 rear, out Vector3 front, out float radius);
            float span = Mathf.Max(.1f, Vector3.Distance(front, rear));
            side.LoopLength = 2 * span + 2 * Mathf.PI * radius;

            float dir = isLeft ? -1f : 1f;
            var a = isLeft ? rearLeft : rearRight;
            var b = isLeft ? frontLeft : frontRight;
            float wheelRadius = (a.radius + b.radius) * 0.5f;
            float groundY = (rear.y + front.y) * 0.5f - wheelRadius;
            Vector3 groundSlopeAxis = (front - rear).normalized;
            Vector3 groundUp = Vector3.Cross(groundSlopeAxis, Vector3.right).normalized;

            // Left and right legs are 180 degrees (0.5 cycle) out of phase for true alternating bipedal walking
            float rawPhase = Mathf.Repeat(distance / StrideLength + (isLeft ? 0f : 0.5f), 1f);
            SampleLegStride(rawPhase, isLeft, walkBlend, out float footZ, out float footLiftY, out float footPitchDeg, out float footOutwardX);

            // Hip joint position follows pelvis bob & sway
            Vector3 hipPos = new Vector3(
                dir * HipLateralOffset + currentPelvisSwayX,
                groundY + HipHeightAboveGround + currentPelvisBobY,
                currentPelvisBobZ);

            // Ankle joint target position in chassis local space
            float slopeDeltaY = groundSlopeAxis.y * footZ;
            Vector3 anklePos = new Vector3(
                dir * FootLateralOffset + footOutwardX + currentPelvisSwayX * 0.35f,
                groundY + FootAnkleHeight + slopeDeltaY + footLiftY,
                footZ);
            float legYawDeg = Mathf.Clamp(-smoothedTurnSpeed * 1.6f, -10f, 10f) * walkBlend + dir * 4f;
            Quaternion legYawRot = Quaternion.Euler(0f, legYawDeg, 0f);
            Quaternion footRot = Quaternion.LookRotation(groundSlopeAxis, groundUp) * legYawRot * Quaternion.Euler(footPitchDeg, 0f, 0f);
            if(WalkerMode)PrepareWalkerFoot(side,isLeft,groundY,ref anklePos,ref footRot);

            // 2-Bone Analytical Inverse Kinematics (Hip -> Knee -> Ankle)
            Vector3 toAnkle = anklePos - hipPos;
            float maxReach = ThighLength + ShinLength - 0.015f;
            if(WalkerMode&&toAnkle.magnitude>maxReach){anklePos=hipPos+toAnkle.normalized*maxReach;toAnkle=anklePos-hipPos;}
            float dist = Mathf.Clamp(toAnkle.magnitude, 0.25f, maxReach);
            Vector3 axisToAnkle = toAnkle.sqrMagnitude > 0.0001f ? toAnkle / toAnkle.magnitude : Vector3.down;

            float cosHip = Mathf.Clamp((ThighLength * ThighLength + dist * dist - ShinLength * ShinLength) / (2f * ThighLength * dist), -1f, 1f);
            float hipBendRad = Mathf.Acos(cosHip);

            // Knee bends forward (+Z) and slightly outward (dir * X) for an aggressive Armored Core stance
            Vector3 kneeForwardHint = legYawRot * new Vector3(dir * 0.10f, 0.20f, 1f).normalized;
            Vector3 bendNormal = Vector3.Cross(axisToAnkle, kneeForwardHint);
            if (bendNormal.sqrMagnitude < 0.0001f) bendNormal = Vector3.right;
            else bendNormal.Normalize();
            Vector3 bendForward = Vector3.Cross(bendNormal, axisToAnkle).normalized;

            Vector3 thighDir = (axisToAnkle * Mathf.Cos(hipBendRad) + bendForward * Mathf.Sin(hipBendRad)).normalized;
            Vector3 kneePos = hipPos + thighDir * ThighLength;
            Vector3 shinDir = (anklePos - kneePos).normalized;

            Vector3 thighFwd = Vector3.ProjectOnPlane(kneeForwardHint, thighDir);
            if (thighFwd.sqrMagnitude < 0.0001f) thighFwd = Vector3.forward;
            Quaternion thighRot = Quaternion.LookRotation(thighFwd.normalized, -thighDir);

            Vector3 shinFwd = Vector3.ProjectOnPlane(kneeForwardHint, shinDir);
            if (shinFwd.sqrMagnitude < 0.0001f) shinFwd = Vector3.forward;
            Quaternion shinRot = Quaternion.LookRotation(shinFwd.normalized, -shinDir);

            Quaternion kneeRot = Quaternion.Slerp(thighRot, shinRot, 0.5f);
            Quaternion ankleRot = Quaternion.Slerp(shinRot, footRot, 0.5f);

            foreach (var group in side.Groups)
            {
                Vector3 position; Quaternion rotation;
                switch (group.Kind)
                {
                    case PartKind.Frame:
                        position = hipPos;
                        rotation = currentPelvisRot;
                        break;
                    case PartKind.FrontWheel:
                        position = hipPos;
                        rotation = thighRot;
                        break;
                    case PartKind.RearWheel:
                        position = kneePos;
                        rotation = kneeRot;
                        break;
                    case PartKind.RoadWheel:
                        position = kneePos;
                        rotation = shinRot;
                        break;
                    case PartKind.ReturnWheel:
                        position = anklePos;
                        rotation = ankleRot;
                        break;
                    case PartKind.Shoe:
                    default:
                        position = anklePos;
                        rotation = footRot;
                        break;
                }
                int end = group.Start + group.Count;
                for (int i = group.Start; i < end; i++)
                {
                    side.Vertices[i] = position + rotation * side.RestVertices[i];
                    side.Normals[i] = rotation * side.RestNormals[i];
                }
            }
            side.Mesh.SetVertices(side.Vertices); side.Mesh.SetNormals(side.Normals);
            side.Mesh.bounds = new Bounds(new Vector3(dir * HipLateralOffset, groundY + 0.85f, 0f), new Vector3(1.4f, 2.2f, 2.4f));
            if(WalkerMode)FinishWalkerPlant(side,isLeft,anklePos,footRot);
            else SampleFootContact(side, isLeft, distance, rawPhase, anklePos, footRot, a, b);
        }

        bool FootSupported(bool isLeft)
        {
            var a=isLeft?rearLeft:rearRight;var b=isLeft?frontLeft:frontRight;
            return vehicle.WalkerGrounded&&a.GetGroundHit(out var ah)&&b.GetGroundHit(out var bh)&&ah.normal.y>.5f&&bh.normal.y>.5f;
        }
        bool FootGround(Vector3 point,out RaycastHit ground,float tolerance=.7f)
        {
            int count=Physics.RaycastNonAlloc(point+Vector3.up*.6f,Vector3.down,footGroundHits,1.3f,~0,QueryTriggerInteraction.Ignore);
            int nearest=-1;float distance=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=footGroundHits[i];
                if(!hit.collider||hit.collider.transform.IsChildOf(vehicle.transform)||hit.normal.y<.6f||
                    Mathf.Abs(Vector3.Dot(point-hit.point,hit.normal))>tolerance||hit.distance>=distance)continue;
                nearest=i;distance=hit.distance;
            }
            ground=nearest>=0?footGroundHits[nearest]:default;return nearest>=0;
        }
        void PrepareWalkerFoot(Side side,bool isLeft,float groundY,ref Vector3 ankle,ref Quaternion rotation)
        {
            float dt=Application.isPlaying?Time.deltaTime:0,dir=isLeft?-1:1;
            var meshTransform=(isLeft?leftMesh:rightMesh).transform;
            Vector3 nominal=meshTransform.TransformPoint(new Vector3(dir*FootLateralOffset,groundY,isLeft?.16f:-.16f));
            bool moving=planarSpeed>.08f&&vehicle.CanDrive&&(!session||session.Playing);
            if(!Application.isPlaying||!FootSupported(isLeft))
            {side.HasPlant=side.Swinging=side.PendingPlant=side.StepEligible=false;side.WasMoving=false;return;}
            if(!side.HasPlant&&!side.Swinging)
            {
                if(!FootGround(nominal,out var initial))return;
                side.Plant=initial.point;side.PlantNormal=initial.normal;side.PlantForward=Vector3.ProjectOnPlane(vehicle.transform.forward,initial.normal).normalized;
                side.CurrentSole=initial.point;side.HasPlant=true;
            }
            if(!side.Swinging&&moving)
            {
                side.GaitPhase+=planarSpeed*dt/StrideLength;
                var local=meshTransform.InverseTransformPoint(side.Plant);
                bool replant=sharpTurn||Mathf.Abs(local.x-dir*FootLateralOffset)>.35f;
                bool starting=!side.WasMoving&&!isLeft;
                if(side.GaitPhase>=StanceFraction||local.z<-.44f||replant||starting)
                    BeginWalkerSwing(side,isLeft,groundY,starting?.08f:replant?.10f:Mathf.Clamp(StrideLength*(1-StanceFraction)/Mathf.Max(.1f,planarSpeed),.10f,.28f),replant||starting);
            }
            if(side.Swinging)
            {
                if((!moving&&side.WasMoving)||sharpTurn)
                {
                    side.SwingStart=side.CurrentSole;side.SwingProgress=0;side.SwingDuration=.12f;
                    Vector3 target=nominal+(moving?Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up)*.12f:Vector3.zero);
                    side.TargetValid=FootGround(target,out var shortened);
                    side.SwingTarget=side.TargetValid?shortened.point:target;
                    if(side.TargetValid)side.TargetNormal=shortened.normal;
                    side.StepEligible&=moving;
                }
                side.SwingProgress=Mathf.Min(1,side.SwingProgress+dt/Mathf.Max(.05f,side.SwingDuration));
                float u=side.SwingProgress,smooth=u*u*(3-2*u);
                side.CurrentSole=Vector3.Lerp(side.SwingStart,side.SwingTarget,smooth)+Vector3.up*(Mathf.Sin(u*Mathf.PI)*(moving?.30f:.13f));
                side.GaitPhase=StanceFraction+(1-StanceFraction)*u;
                if(u>=1)
                {
                    side.Swinging=false;side.HasPlant=side.TargetValid;side.GaitPhase=0;
                    side.Plant=side.SwingTarget;side.PlantNormal=side.TargetNormal;
                    side.PlantForward=Vector3.ProjectOnPlane(vehicle.transform.forward,side.PlantNormal).normalized;
                    side.PendingPlant=side.HasPlant&&side.StepEligible&&moving;
                }
            }
            else side.CurrentSole=side.Plant;
            side.WasMoving=moving;
            Vector3 normal=side.Swinging?Vector3.Slerp(side.PlantNormal,side.TargetNormal,side.SwingProgress):side.PlantNormal;
            if(normal.sqrMagnitude<.5f)normal=Vector3.up;
            Vector3 forward=side.Swinging?Vector3.ProjectOnPlane(vehicle.transform.forward,normal):side.PlantForward;
            if(forward.sqrMagnitude<.1f)forward=Vector3.ProjectOnPlane(vehicle.transform.forward,normal);
            var worldRotation=Quaternion.LookRotation(forward.normalized,normal.normalized);
            rotation=Quaternion.Inverse(meshTransform.rotation)*worldRotation;
            ankle=meshTransform.InverseTransformPoint(side.CurrentSole+worldRotation*Vector3.up*FootAnkleHeight);
        }
        void BeginWalkerSwing(Side side,bool isLeft,float groundY,float duration,bool shortStep)
        {
            var meshTransform=(isLeft?leftMesh:rightMesh).transform;
            side.SwingStart=side.CurrentSole;side.SwingProgress=0;side.SwingDuration=duration;
            Vector3 velocity=Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up);
            Vector3 target=meshTransform.TransformPoint(new Vector3((isLeft?-1:1)*FootLateralOffset,groundY,shortStep?.25f:.48f))+velocity*duration;
            side.TargetValid=FootGround(target,out var ground);side.SwingTarget=side.TargetValid?ground.point:target;
            side.TargetNormal=side.TargetValid?ground.normal:Vector3.up;
            side.Swinging=true;side.HasPlant=false;side.StepEligible=true;
        }
        void FinishWalkerPlant(Side side,bool isLeft,Vector3 ankle,Quaternion rotation)
        {
            var meshTransform=(isLeft?leftMesh:rightMesh).transform;
            side.CurrentSole=meshTransform.TransformPoint(ankle+rotation*(Vector3.down*FootAnkleHeight));
            if(!side.PendingPlant)return;side.PendingPlant=false;
            if(!FootSupported(isLeft)||planarSpeed<=.08f||!vehicle.CanDrive||(session&&!session.Playing)||
                !FootGround(side.CurrentSole,out var ground,.18f))return;
            Vector3 forward=Vector3.ProjectOnPlane(meshTransform.TransformDirection(rotation*Vector3.forward),ground.normal).normalized;
            if(forward.sqrMagnitude<.5f)return;
            PlantedFootCount++;
            float intensity=Mathf.Clamp01(footfallIntensity*Mathf.Lerp(.35f,1,Mathf.Clamp01(planarSpeed/Mathf.Max(1,vehicle.tuning.maxSpeed))));
            FootPlanted?.Invoke(new FootContact(ground.point,ground.normal.normalized,forward,isLeft,intensity,PlantedFootCount));
        }

        void SampleFootContact(Side side, bool isLeft, float distance, float phase, Vector3 ankle, Quaternion footRotation,
            WheelCollider rear, WheelCollider front)
        {
            bool swing = phase >= StanceFraction;
            bool moved = side.ContactSampled && Mathf.Abs(distance - side.ContactDistance) > .0001f;
            bool landed = side.ContactSampled && side.WasSwing && !swing;
            side.ContactDistance = distance; side.WasSwing = swing;
            bool supported = rear.GetGroundHit(out _) && front.GetGroundHit(out _);
            side.ContactSampled = Application.isPlaying && supported && (!session || session.Playing) && Time.deltaTime > .00001f;
            if (!side.ContactSampled || !moved || !landed || walkBlend < .1f ||
                Mathf.Abs(isLeft ? LeftBeltSpeed : RightBeltSpeed) < .04f || Vector3.Dot(vehicle.transform.up, Vector3.up) < .5f) return;
            var meshTransform = (isLeft ? leftMesh : rightMesh).transform;
            Vector3 sole = meshTransform.TransformPoint(ankle + footRotation * (Vector3.down * FootAnkleHeight));
            // Query the actual sole, rather than stamping either of the two supporting wheel centres.
            // The fixed buffer avoids allocating even when several ground meshes overlap.
            int hits = Physics.RaycastNonAlloc(sole + Vector3.up * .45f, Vector3.down, footGroundHits, .8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int nearest = -1; float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits; i++)
            {
                var hit = footGroundHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(vehicle.transform) || Vector3.Dot(hit.normal, Vector3.up) < .5f ||
                    Mathf.Abs(Vector3.Dot(sole - hit.point, hit.normal)) > .22f || hit.distance >= nearestDistance) continue;
                nearest = i; nearestDistance = hit.distance;
            }
            if (nearest < 0) return;
            var ground = footGroundHits[nearest]; Vector3 normal = ground.normal.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(meshTransform.TransformDirection(footRotation * Vector3.forward), normal).normalized;
            if (forward.sqrMagnitude < .5f) return;
            PlantedFootCount++;
            FootPlanted?.Invoke(new FootContact(ground.point, normal, forward, isLeft));
        }

        static void SampleLegStride(float phase, bool isLeft, float walkBlend, out float footZ, out float footLiftY, out float footPitchDeg, out float footOutwardX)
        {
            const float activeHalfSpan = 0.62f;
            float idleStaggerZ = isLeft ? 0.16f : -0.16f;
            float dir = isLeft ? -1f : 1f;

            float strideZ;
            float liftY;
            float pitchDeg;
            float outwardX;

            if (phase < StanceFraction)
            {
                // Stance phase: foot is planted on the ground and sweeps from front (+activeHalfSpan) to rear (-activeHalfSpan)
                float u = phase / StanceFraction;
                strideZ = Mathf.Lerp(activeHalfSpan, -activeHalfSpan, u);
                liftY = 0f;
                outwardX = 0f;

                // Crisp heel-strike flatten at start of stance, flat plant in mid-stance, and powerful toe-off push at end of stance
                if (u < 0.14f)
                    pitchDeg = Mathf.Lerp(-16f, 0f, u / 0.14f);
                else if (u < 0.76f)
                    pitchDeg = 0f;
                else
                    pitchDeg = Mathf.Lerp(0f, 25f, (u - 0.76f) / 0.24f);
            }
            else
            {
                // Swing phase: knee bends high and lifts foot cleanly forward to next heel-strike
                float v = (phase - StanceFraction) / (1f - StanceFraction);
                float smoothV = v * v * (3f - 2f * v);
                strideZ = Mathf.Lerp(-activeHalfSpan, activeHalfSpan, smoothV);

                // High, punchy mecha step clearance arc
                float arc = Mathf.Sin(v * Mathf.PI);
                float earlyKick = Mathf.Sin(v * Mathf.PI * 2f) * 0.055f;
                liftY = Mathf.Max(0f, arc * 0.36f + earlyKick);
                outwardX = dir * arc * 0.045f;

                // Toe-off drag (+25 deg) -> mid-swing level -> heel-strike dorsiflexion (-16 deg)
                if (v < 0.45f)
                    pitchDeg = Mathf.Lerp(25f, 4f, v / 0.45f);
                else
                    pitchDeg = Mathf.Lerp(4f, -16f, (v - 0.45f) / 0.55f);
            }

            footZ = Mathf.Lerp(idleStaggerZ, strideZ, walkBlend);
            footLiftY = liftY * walkBlend;
            footPitchDeg = pitchDeg * walkBlend;
            footOutwardX = outwardX * walkBlend;
        }

        const int Rubber = 0, Steel = 1, BrightSteel = 2, Teal = 3, Brass = 4, Bolt = 5, DarkSteel = 6, Dust = 7;
        enum PartKind { Shoe, FrontWheel, RearWheel, RoadWheel, ReturnWheel, Frame }
        sealed class Group { public PartKind Kind; public float Fraction, Radius; public int Start, Count; }
        sealed class Side
        {
            public Mesh Mesh;
            public Vector3[] RestVertices, RestNormals, Vertices, Normals;
            public Group[] Groups;
            public float LoopLength = 1;
            public bool ContactSampled, WasSwing;
            public float ContactDistance;
            public bool HasPlant,Swinging,PendingPlant,StepEligible,WasMoving,TargetValid;
            public Vector3 Plant,PlantNormal=Vector3.up,PlantForward=Vector3.forward,CurrentSole,SwingStart,SwingTarget,TargetNormal=Vector3.up;
            public float GaitPhase,SwingProgress,SwingDuration;
        }

        // One UV palette material and one combined dynamic mesh per side. There are no per-part
        // GameObjects, material instances, allocations or mesh topology rebuilds in LateUpdate.
        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> indices = new List<int>();
            readonly List<Group> groups = new List<Group>();
            Group group;
            public void Begin(PartKind kind, float fraction, float radius)
            { group = new Group { Kind = kind, Fraction = fraction, Radius = radius, Start = vertices.Count }; groups.Add(group); }
            public void End() { group.Count = vertices.Count - group.Start; }
            void Vertex(Vector3 p, Vector3 n, int color)
            { vertices.Add(p); normals.Add(n); uv.Add(new Vector2((color + .5f) / 8, .5f)); }
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int color)
            {
                int k = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var swap = b; b = c; c = swap; }
                Vertex(a, n, color); Vertex(b, n, color); Vertex(c, n, color);
                indices.Add(k); indices.Add(k + 1); indices.Add(k + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int color)
            {
                int k = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var swap = b; b = d; d = swap; }
                Vertex(a, n, color); Vertex(b, n, color); Vertex(c, n, color); Vertex(d, n, color);
                indices.Add(k); indices.Add(k + 1); indices.Add(k + 2); indices.Add(k); indices.Add(k + 2); indices.Add(k + 3);
            }
            public void Box(Vector3 center, Vector3 size, int color, Quaternion? rotation = null)
            {
                Quaternion q = rotation ?? Quaternion.identity; Vector3 h = size * .5f;
                Face(Vector3.right, Vector3.up * h.y, Vector3.forward * h.z, h.x);
                Face(-Vector3.right, Vector3.up * h.y, -Vector3.forward * h.z, h.x);
                Face(Vector3.up, Vector3.forward * h.z, Vector3.right * h.x, h.y);
                Face(-Vector3.up, Vector3.forward * h.z, -Vector3.right * h.x, h.y);
                Face(Vector3.forward, Vector3.right * h.x, Vector3.up * h.y, h.z);
                Face(-Vector3.forward, Vector3.right * h.x, -Vector3.up * h.y, h.z);
                void Face(Vector3 n, Vector3 u, Vector3 v, float depth)
                {
                    Vector3 c = center + q * n * depth;
                    Quad(c - q * u - q * v, c + q * u - q * v, c + q * u + q * v, c - q * u + q * v, q * n, color);
                }
            }
            public void BevelBox(Vector3 center, Vector3 size, float bevel, int color)
            {
                float x = size.x * .5f, z = size.z * .5f, y = size.y * .5f;
                float b = Mathf.Min(bevel, Mathf.Min(x, z) * .45f);
                Vector3[] ring = { new Vector3(-x + b, 0, -z), new Vector3(x - b, 0, -z), new Vector3(x, 0, -z + b),
                    new Vector3(x, 0, z - b), new Vector3(x - b, 0, z), new Vector3(-x + b, 0, z),
                    new Vector3(-x, 0, z - b), new Vector3(-x, 0, -z + b) };
                for (int i = 0; i < 8; i++)
                {
                    Vector3 a = ring[i], d = ring[(i + 1) % 8];
                    Vector3 n = Vector3.Cross(Vector3.up, d - a).normalized;
                    Quad(center + a - Vector3.up * y, center + d - Vector3.up * y, center + d + Vector3.up * y, center + a + Vector3.up * y, n, color);
                    Triangle(center + Vector3.up * y, center + a + Vector3.up * y, center + d + Vector3.up * y, Vector3.up, color);
                    Triangle(center - Vector3.up * y, center + a - Vector3.up * y, center + d - Vector3.up * y, -Vector3.up, color);
                }
            }
            // Axle is local X, with flat end caps and readable low-poly radial facets.
            public void Cylinder(Vector3 center, float radius, float length, int color, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector3 r0 = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)), r1 = new Vector3(0, Mathf.Cos(b), Mathf.Sin(b));
                    Vector3 left = center - Vector3.right * length * .5f, right = center + Vector3.right * length * .5f;
                    Quad(left + r0 * radius, right + r0 * radius, right + r1 * radius, left + r1 * radius, (r0 + r1).normalized, color);
                    Triangle(left, left + r0 * radius, left + r1 * radius, -Vector3.right, color);
                    Triangle(right, right + r0 * radius, right + r1 * radius, Vector3.right, color);
                }
            }
            public Side Finish(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.MarkDynamic(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
                return new Side { Mesh = mesh, RestVertices = vertices.ToArray(), RestNormals = normals.ToArray(),
                    Vertices = new Vector3[vertices.Count], Normals = new Vector3[vertices.Count], Groups = groups.ToArray() };
            }
        }
    }
}
