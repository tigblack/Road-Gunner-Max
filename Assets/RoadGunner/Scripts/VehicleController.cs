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
        public int GroundedWheels { get; private set; }
        public float LargestSlip { get; private set; }
        public bool CanDrive { get; set; } = true;
        float steering,turnDirection,wheelbase,frontTrack,frontCentre;
        readonly float[] frontWheelSide = new float[2];
        void Awake() {
            Body=GetComponent<Rigidbody>(); Body.mass=tuning.mass; Body.centerOfMass=tuning.centerOfMass;
            Body.interpolation=RigidbodyInterpolation.Interpolate; Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            foreach(var w in wheels) {
                w.radius=tuning.wheelRadius; w.suspensionDistance=tuning.suspensionTravel;
                var s=w.suspensionSpring; s.spring=tuning.spring; s.damper=tuning.damper; s.targetPosition=tuning.suspensionTarget; w.suspensionSpring=s;
                w.forwardFriction=tuning.forwardFriction.ToWheelCurve(); w.sidewaysFriction=tuning.sidewaysFriction.ToWheelCurve();
            }
            // Substep configuration is shared by the vehicle, not a separate simulation per wheel.
            wheels[0].ConfigureVehicleSubsteps(5,8,4);
            Vector3 front=(wheels[0].transform.position+wheels[1].transform.position)*.5f;
            Vector3 rear=(wheels[2].transform.position+wheels[3].transform.position)*.5f;
            wheelbase=Mathf.Max(.5f,Mathf.Abs(Vector3.Dot(front-rear,transform.forward)));
            frontTrack=Mathf.Abs(Vector3.Dot(wheels[1].transform.position-wheels[0].transform.position,transform.right));
            frontCentre=Vector3.Dot(front-transform.position,transform.right);
            for(int i=0;i<2;i++) frontWheelSide[i]=Mathf.Sign(Vector3.Dot(wheels[i].transform.position-transform.position,transform.right)-frontCentre);
        }
        void FixedUpdate() {
            ForwardSpeed=Vector3.Dot(Body.linearVelocity,transform.forward);
            GroundedWheels=0; LargestSlip=0;
            for(int i=0;i<4;i++) if(wheels[i].GetGroundHit(out WheelHit h)) { GroundedWheels++; LargestSlip=Mathf.Max(LargestSlip,Mathf.Abs(h.sidewaysSlip)); }
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
            AntiRoll(0,1); AntiRoll(2,3);
            // Rolling/air resistance acts in the ground plane. Suspension, jumps and impacts keep
            // their vertical dynamics; neither linear nor angular velocity is overwritten for driving.
            if(GroundedWheels>0) Body.AddForce(-Vector3.ProjectOnPlane(Body.linearVelocity,Vector3.up)*
                (tuning.rollingResistance+HorizontalSpeed*tuning.aerodynamicDrag),ForceMode.Force);
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
            if(engine) { engine.pitch=.7f+Mathf.Clamp01(Speed/18)*1.1f; engine.volume=CanDrive?.08f+Mathf.Clamp01(Speed/18)*.09f:0; }
        }
        public void ResetPose(Vector3 position,Quaternion rotation) {
            Body.position=position; Body.rotation=rotation; Body.linearVelocity=Vector3.zero; Body.angularVelocity=Vector3.zero;
            transform.SetPositionAndRotation(position,rotation); steering=0; turnDirection=0; ForwardSpeed=0; Throttle=0; AppliedBrakeTorque=tuning.holdBrakeTorque;
            foreach(var w in wheels) { w.motorTorque=0; w.brakeTorque=tuning.holdBrakeTorque; w.steerAngle=0; }
            if(dust!=null) foreach(var p in dust) if(p) p.Clear();
        }
    }
}
