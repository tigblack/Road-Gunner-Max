using UnityEngine;
namespace RoadGunner
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        public VehicleTuning tuning;
        public DriveInput input;
        public WheelCollider[] wheels;
        public Transform[] wheelVisuals;
        public ParticleSystem[] dust;
        public AudioSource engine;
        public Rigidbody Body { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float Speed => Body==null?0:Body.linearVelocity.magnitude;
        public float HorizontalSpeed => Body==null?0:Vector3.ProjectOnPlane(Body.linearVelocity,Vector3.up).magnitude;
        public float SteeringAngle => steering;
        public float Throttle { get; private set; }
        public float AppliedBrakeTorque { get; private set; }
        /// <summary>Actual longitudinal body point speeds at the two track centres, in metres per second.</summary>
        public float LeftTrackSpeed { get; private set; }
        public float RightTrackSpeed { get; private set; }
        /// <summary>Actual motor torque summed across the two wheels of each track, in newton metres.</summary>
        public float LeftTrackTorque { get; private set; }
        public float RightTrackTorque { get; private set; }
        public float RequestedYawRate { get; private set; }
        public float AppliedYawAssistTorque { get; private set; }
        /// <summary>Signed sideways stabilization force at the centre of mass during a low-speed pivot, in newtons.</summary>
        public float AppliedPivotLateralForce { get; private set; }
        public int GroundedWheels { get; private set; }
        public float LargestSlip { get; private set; }
        public bool CanDrive { get; set; } = true;
        /// <summary>Set by the arcade audio sink, which mutes the engine drone and voices movement as tyre roll instead.</summary>
        [System.NonSerialized] public bool externalEngineAudio;
        [System.NonSerialized] public ArcadeFeedback feedback;
        float steering,turnDirection,wheelbase,frontTrack,frontCentre;
        readonly float[] frontWheelSide = new float[2];
        readonly float[] trackSide = new float[4];
        Vector3 leftTrackCentre,rightTrackCentre;
        float trackWidth;
        void Awake() {
            Body=GetComponent<Rigidbody>(); Body.mass=tuning.mass; Body.centerOfMass=tuning.centerOfMass;
            Body.interpolation=RigidbodyInterpolation.Interpolate; Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            foreach(var w in wheels) {
                w.radius=tuning.wheelRadius; w.suspensionDistance=tuning.suspensionTravel;
                var s=w.suspensionSpring; s.spring=tuning.spring; s.damper=tuning.damper; s.targetPosition=tuning.suspensionTarget; w.suspensionSpring=s;
                w.forwardFriction=tuning.forwardFriction.ToWheelCurve();
                var lateral=tuning.sidewaysFriction.ToWheelCurve();
                if(tuning.trackedDrive)lateral.stiffness*=Mathf.Clamp(tuning.trackedSidewaysGrip,.1f,1);
                w.sidewaysFriction=lateral;
            }
            // Substep configuration is shared by the vehicle, not a separate simulation per wheel.
            wheels[0].ConfigureVehicleSubsteps(5,8,4);
            Vector3 front=(wheels[0].transform.position+wheels[1].transform.position)*.5f;
            Vector3 rear=(wheels[2].transform.position+wheels[3].transform.position)*.5f;
            wheelbase=Mathf.Max(.5f,Mathf.Abs(Vector3.Dot(front-rear,transform.forward)));
            frontTrack=Mathf.Abs(Vector3.Dot(wheels[1].transform.position-wheels[0].transform.position,transform.right));
            frontCentre=Vector3.Dot(front-transform.position,transform.right);
            for(int i=0;i<2;i++) frontWheelSide[i]=Mathf.Sign(Vector3.Dot(wheels[i].transform.position-transform.position,transform.right)-frontCentre);
            // Derive sides from the authored rig, not from a left/right wheel index convention.
            float centre=0;for(int i=0;i<4;i++)centre+=transform.InverseTransformPoint(wheels[i].transform.position).x*.25f;
            int left=0,right=0;
            for(int i=0;i<4;i++) {
                Vector3 local=transform.InverseTransformPoint(wheels[i].transform.position);
                trackSide[i]=local.x<centre?-1:1;
                if(trackSide[i]<0){leftTrackCentre+=local;left++;}else{rightTrackCentre+=local;right++;}
            }
            leftTrackCentre/=Mathf.Max(1,left);rightTrackCentre/=Mathf.Max(1,right);
            trackWidth=Mathf.Max(.1f,rightTrackCentre.x-leftTrackCentre.x);
        }
        void FixedUpdate() {
            ForwardSpeed=Vector3.Dot(Body.linearVelocity,transform.forward);
            GroundedWheels=0; LargestSlip=0;
            Vector3 supportNormal=Vector3.zero;bool leftContact=false,rightContact=false;
            for(int i=0;i<4;i++) if(wheels[i].GetGroundHit(out WheelHit h)) {
                GroundedWheels++; LargestSlip=Mathf.Max(LargestSlip,Mathf.Abs(h.sidewaysSlip));supportNormal+=h.normal;
                if(trackSide[i]<0)leftContact=true;else rightContact=true;
            }
            LeftTrackSpeed=Vector3.Dot(Body.GetPointVelocity(transform.TransformPoint(leftTrackCentre)),transform.forward);
            RightTrackSpeed=Vector3.Dot(Body.GetPointVelocity(transform.TransformPoint(rightTrackCentre)),transform.forward);
            float magnitude=CanDrive&&input.Enabled?input.Magnitude:0;
            float speedLimit=Mathf.Max(1,tuning.maxSpeed);
            float heading=magnitude>0?Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward,Vector3.up),input.WorldDirection,Vector3.up):0;
            // A directly rearward request has no unique left/right answer. Keep the chosen side
            // until the nose is well into the turn, instead of chattering around +/-180 degrees.
            if(magnitude<=0 || Mathf.Abs(heading)<110) turnDirection=0;
            else if(Mathf.Abs(heading)>150) {
                if(turnDirection==0) turnDirection=Mathf.Sign(heading);
                heading=Mathf.Abs(heading)*turnDirection;
            }
            AppliedYawAssistTorque=AppliedPivotLateralForce=0;
            if(tuning.trackedDrive)DriveTracked(magnitude,heading,speedLimit,supportNormal,leftContact&&rightContact);
            else {RequestedYawRate=0;DriveWheeled(magnitude,heading,speedLimit);}
            LeftTrackTorque=RightTrackTorque=0;
            for(int i=0;i<4;i++) {
                if(trackSide[i]<0)LeftTrackTorque+=wheels[i].motorTorque;else RightTrackTorque+=wheels[i].motorTorque;
            }
            AntiRoll(0,1); AntiRoll(2,3);
            // Rolling/air resistance acts in the ground plane. Suspension, jumps and impacts keep
            // their vertical dynamics; neither linear nor angular velocity is overwritten for driving.
            if(GroundedWheels>0) Body.AddForce(-Vector3.ProjectOnPlane(Body.linearVelocity,Vector3.up)*
                (tuning.rollingResistance+HorizontalSpeed*tuning.aerodynamicDrag),ForceMode.Force);
        }
        void DriveWheeled(float magnitude,float heading,float speedLimit) {
            float speed01=Mathf.Clamp01(HorizontalSpeed/speedLimit);
            float limit=tuning.steeringBySpeed.Evaluate(speed01);
            float yawRate=Vector3.Dot(Body.angularVelocity,transform.up)*Mathf.Rad2Deg;
            float targetSteer=magnitude>0?Mathf.Clamp(heading*tuning.headingSteerGain-yawRate*tuning.yawCounterSteer,-limit,limit):0;
            steering=Mathf.MoveTowards(steering,targetSteer,tuning.steeringRate*Time.fixedDeltaTime);
            // Nose-first directional input controls wheel steering and a speed request. In tight
            // turns the car must keep rolling: a physical vehicle cannot pivot around its centre.
            float turnAngle=Mathf.Clamp(tuning.turnBeforeDriveAngle,30,170);
            float fullAngle=Mathf.Clamp(tuning.fullDriveAngle,0,turnAngle-1);
            float alignment=Mathf.InverseLerp(turnAngle,fullAngle,Mathf.Abs(heading));
            float desiredSpeed=Mathf.Lerp(Mathf.Min(speedLimit,tuning.turningSpeed),speedLimit,alignment)*magnitude;
            float motor=0,brake=0;
            if(!CanDrive || input.Brake)brake=tuning.serviceBrakeTorque;
            else if(magnitude>0) {
                if(ForwardSpeed<-.25f)brake=tuning.serviceBrakeTorque;
                else {
                    float error=desiredSpeed-ForwardSpeed;
                    float power=tuning.torqueBySpeed.Evaluate(Mathf.Clamp01(Mathf.Abs(ForwardSpeed)/speedLimit));
                    float availableTorque=Mathf.Min(tuning.totalDriveTorque,Mathf.Max(.1f,tuning.acceleration)*tuning.mass*tuning.wheelRadius);
                    motor=availableTorque*power*Mathf.Clamp01(error/Mathf.Max(.1f,tuning.driveResponseSpeed));
                    // Braking is a tyre force. Preserve the body's momentum and collision impulses.
                    if(error<-.5f)brake=tuning.serviceBrakeTorque*Mathf.Clamp01(-error/6)*.65f;
                }
            } else brake=Mathf.Lerp(tuning.coastBrakeTorque,tuning.holdBrakeTorque,Mathf.InverseLerp(.65f,.08f,HorizontalSpeed));
            float available=Mathf.Min(tuning.totalDriveTorque,Mathf.Max(.1f,tuning.acceleration)*tuning.mass*tuning.wheelRadius);
            Throttle=available>0?motor/available:0; AppliedBrakeTorque=brake;
            for(int i=0;i<4;i++) { wheels[i].steerAngle=i<2?FrontSteer(i):0; wheels[i].motorTorque=motor*(i<2?(1-tuning.rearTorqueShare):tuning.rearTorqueShare)*.5f; wheels[i].brakeTorque=brake; }
        }
        void DriveTracked(float magnitude,float heading,float speedLimit,Vector3 supportNormal,bool bothTracksGrounded) {
            steering=0;
            float available=Mathf.Min(tuning.totalDriveTorque,Mathf.Max(.1f,tuning.acceleration)*tuning.mass*tuning.wheelRadius);
            float leftMotor=0,rightMotor=0,leftBrake=0,rightBrake=0;
            bool driving=CanDrive&&input.Enabled&&magnitude>0&&!input.Brake;
            if(!CanDrive||input.Brake) {
                RequestedYawRate=0;leftBrake=rightBrake=tuning.serviceBrakeTorque;
            } else if(!driving) {
                RequestedYawRate=0;
                leftBrake=rightBrake=Mathf.Lerp(tuning.coastBrakeTorque,tuning.holdBrakeTorque,Mathf.InverseLerp(.65f,.08f,HorizontalSpeed));
            } else {
                float turnAngle=Mathf.Clamp(tuning.turnBeforeDriveAngle,30,170);
                float fullAngle=Mathf.Clamp(tuning.fullDriveAngle,0,turnAngle-1);
                float alignment=Mathf.InverseLerp(turnAngle,fullAngle,Mathf.Abs(heading));
                // A large rearward request first removes forward travel through tyre braking.
                // Counter-rotating tracks can then pivot without requesting reverse chassis travel.
                float manoeuvring=Mathf.Max(.5f,tuning.turningSpeed);
                float pivotGate=Mathf.Abs(heading)>=turnAngle?1-Mathf.InverseLerp(manoeuvring,manoeuvring*1.5f,HorizontalSpeed):1;
                float yawTarget=Mathf.Clamp(heading*Mathf.Max(.1f,tuning.trackedHeadingGain),-tuning.trackedMaxYawRate,tuning.trackedMaxYawRate)*magnitude*pivotGate;
                RequestedYawRate=Mathf.MoveTowards(RequestedYawRate,yawTarget,Mathf.Max(1,tuning.trackedYawAcceleration)*Time.fixedDeltaTime);
                float forward=speedLimit*magnitude*Mathf.SmoothStep(0,1,alignment);
                float difference=RequestedYawRate*Mathf.Deg2Rad*trackWidth*.5f;
                float power=tuning.torqueBySpeed.Evaluate(Mathf.Clamp01(Mathf.Abs(ForwardSpeed)/speedLimit));
                TrackDrive(forward+difference,LeftTrackSpeed,available*.5f,power,out leftMotor,out leftBrake);
                TrackDrive(forward-difference,RightTrackSpeed,available*.5f,power,out rightMotor,out rightBrake);
                ApplyTrackedYaw(supportNormal,bothTracksGrounded);
                if(Mathf.Abs(heading)>=turnAngle&&HorizontalSpeed<=manoeuvring)
                    StabilizeTrackedPivot(supportNormal,bothTracksGrounded);
            }
            for(int i=0;i<4;i++) {
                bool left=trackSide[i]<0;
                wheels[i].steerAngle=0;
                wheels[i].motorTorque=(left?leftMotor:rightMotor)*.5f;
                wheels[i].brakeTorque=left?leftBrake:rightBrake;
            }
            Throttle=available>0?Mathf.Clamp01((Mathf.Abs(leftMotor)+Mathf.Abs(rightMotor))/available):0;
            AppliedBrakeTorque=(leftBrake+rightBrake)*.5f;
        }
        void TrackDrive(float target,float actual,float maximum,float power,out float motor,out float brake) {
            motor=brake=0;
            float service=Mathf.Max(0,tuning.serviceBrakeTorque);
            if(Mathf.Abs(target)<.02f) {
                brake=service*Mathf.Clamp01(Mathf.Abs(actual)/Mathf.Max(.5f,tuning.turningSpeed));
                return;
            }
            float sign=Mathf.Sign(target),along=actual*sign;
            if(along<-.1f) {brake=service;return;}
            float error=Mathf.Abs(target)-along;
            if(error>0)motor=sign*maximum*Mathf.Max(0,power)*Mathf.Clamp01(error/Mathf.Max(.1f,tuning.driveResponseSpeed));
            else if(error<-.1f)brake=service*.65f*Mathf.Clamp01(-error/3);
        }
        void ApplyTrackedYaw(Vector3 supportNormal,bool bothTracksGrounded) {
            // This is a contact-dependent torque, never a pose or angular-velocity command.
            // A single grounded track, a jump or an overturned body cannot steer the chassis with it.
            if(!bothTracksGrounded||GroundedWheels<2||Vector3.Dot(transform.up,Vector3.up)<.55f||supportNormal.sqrMagnitude<.01f)return;
            Vector3 axis=supportNormal.normalized;
            if(Vector3.Dot(axis,Vector3.up)<.5f)return;
            float actual=Vector3.Dot(Body.angularVelocity,axis);
            float acceleration=(RequestedYawRate*Mathf.Deg2Rad-actual)/Mathf.Max(.05f,tuning.trackedYawResponse);
            Quaternion principalRotation=Body.rotation*Body.inertiaTensorRotation;
            Vector3 principalAxis=Quaternion.Inverse(principalRotation)*axis;
            float inertia=Vector3.Dot(Vector3.Scale(Body.inertiaTensor,principalAxis),principalAxis);
            float maximum=Mathf.Max(0,tuning.trackedYawTorque)*GroundedWheels*.25f;
            AppliedYawAssistTorque=Mathf.Clamp(inertia*acceleration,-maximum,maximum);
            Body.AddTorque(axis*AppliedYawAssistTorque,ForceMode.Force);
        }
        void StabilizeTrackedPivot(Vector3 supportNormal,bool bothTracksGrounded) {
            if(!bothTracksGrounded||GroundedWheels<2||Vector3.Dot(transform.up,Vector3.up)<.55f||supportNormal.sqrMagnitude<.01f)return;
            if(Vector3.Dot(supportNormal.normalized,Vector3.up)<.5f)return;
            // Four-wheel track proxies scrub asymmetrically about the centre of pressure.
            // Supply a bounded horizontal contact force at the COM, without adding yaw or
            // cancelling vertical velocity. Released input, ordinary arcs and jumps retain inertia.
            Vector3 right=Vector3.ProjectOnPlane(transform.right,Vector3.up).normalized;
            float lateral=Vector3.Dot(Body.linearVelocity,right);
            float limit=Mathf.Max(0,tuning.trackedPivotLateralAcceleration)*GroundedWheels*.25f;
            float acceleration=Mathf.Clamp(-lateral*Mathf.Max(0,tuning.trackedPivotLateralDamping),-limit,limit);
            AppliedPivotLateralForce=Body.mass*acceleration;
            Body.AddForce(right*AppliedPivotLateralForce,ForceMode.Force);
        }
        float FrontSteer(int index) {
            if(Mathf.Abs(steering)<.1f || frontTrack<.1f) return steering;
            float sign=Mathf.Sign(steering);
            float radius=wheelbase/Mathf.Tan(Mathf.Abs(steering)*Mathf.Deg2Rad);
            bool inner=frontWheelSide[index]*sign>0;
            float wheelRadius=Mathf.Max(.5f,radius+(inner?-1:1)*frontTrack*.5f);
            float angle=sign*Mathf.Atan(wheelbase/wheelRadius)*Mathf.Rad2Deg;
            return Mathf.Lerp(steering,angle,tuning.ackermannStrength);
        }
        void AntiRoll(int a,int b) {
            bool ga=wheels[a].GetGroundHit(out WheelHit ha),gb=wheels[b].GetGroundHit(out WheelHit hb);
            if(!ga || !gb) return;
            float ta=(-wheels[a].transform.InverseTransformPoint(ha.point).y-wheels[a].radius)/wheels[a].suspensionDistance;
            float tb=(-wheels[b].transform.InverseTransformPoint(hb.point).y-wheels[b].radius)/wheels[b].suspensionDistance;
            float f=Mathf.Clamp((ta-tb)*tuning.antiRoll,-tuning.maxAntiRollForce,tuning.maxAntiRollForce);
            Body.AddForceAtPosition(wheels[a].transform.up*-f,wheels[a].transform.position);
            Body.AddForceAtPosition(wheels[b].transform.up*f,wheels[b].transform.position);
        }
        void LateUpdate() {
            for(int i=0;i<wheels.Length;i++) {
                wheels[i].GetWorldPose(out Vector3 p,out Quaternion q); wheelVisuals[i].SetPositionAndRotation(p,q);
                if(dust!=null && i<dust.Length && dust[i]) { var em=dust[i].emission; bool contact=wheels[i].GetGroundHit(out WheelHit hit); em.rateOverTime=CanDrive && contact && Speed>2?Mathf.Min(30,Speed*1.25f+Mathf.Abs(hit.sidewaysSlip)*14):0; dust[i].transform.position=p-Vector3.up*.35f; }
            }
            // Fallback without the arcade sink: no idle drone, only a quiet hum while actually moving.
            if(engine && !externalEngineAudio) { engine.pitch=.75f+Mathf.Clamp01(Speed/18)*.5f; engine.volume=CanDrive?Mathf.InverseLerp(1,18,Speed)*.05f:0; }
        }
        void OnCollisionEnter(Collision collision) {
            if(!feedback || collision.contactCount==0) return;
            // Ground contact is handled by the wheels; report real body impacts against obstacles and actors.
            float speed=collision.relativeVelocity.magnitude; var contact=collision.GetContact(0);
            if(speed<2.5f || Vector3.Dot(contact.normal,Vector3.up)>.75f) return;
            feedback.VehicleCollision(contact.point,speed);
        }
        public void ResetPose(Vector3 position,Quaternion rotation) {
            Body.position=position; Body.rotation=rotation; Body.linearVelocity=Vector3.zero; Body.angularVelocity=Vector3.zero;
            transform.SetPositionAndRotation(position,rotation); steering=0; turnDirection=0; ForwardSpeed=0; Throttle=0; AppliedBrakeTorque=tuning.holdBrakeTorque;
            RequestedYawRate=AppliedYawAssistTorque=LeftTrackSpeed=RightTrackSpeed=LeftTrackTorque=RightTrackTorque=0;
            AppliedPivotLateralForce=0;
            foreach(var w in wheels) { w.motorTorque=0; w.brakeTorque=tuning.holdBrakeTorque; w.steerAngle=0; }
            if(dust!=null) foreach(var p in dust) if(p) p.Clear();
        }
    }
}
