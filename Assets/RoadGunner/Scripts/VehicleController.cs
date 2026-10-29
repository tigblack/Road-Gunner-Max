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
        public int GroundedWheels { get; private set; }
        public float LargestSlip { get; private set; }
        public bool CanDrive { get; set; } = true;
        float steering,assistedYawRate;
        void Awake() {
            Body=GetComponent<Rigidbody>(); Body.mass=tuning.mass; Body.centerOfMass=tuning.centerOfMass;
            Body.interpolation=RigidbodyInterpolation.Interpolate; Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            foreach(var w in wheels) {
                w.radius=tuning.wheelRadius; w.suspensionDistance=tuning.suspensionTravel;
                var s=w.suspensionSpring; s.spring=tuning.spring; s.damper=tuning.damper; s.targetPosition=tuning.suspensionTarget; w.suspensionSpring=s;
                w.forwardFriction=tuning.forwardFriction.ToWheelCurve(); w.sidewaysFriction=tuning.sidewaysFriction.ToWheelCurve();
                w.ConfigureVehicleSubsteps(5,8,4);
            }
        }
        void FixedUpdate() {
            ForwardSpeed=Vector3.Dot(Body.linearVelocity,transform.forward);
            GroundedWheels=0; LargestSlip=0;
            for(int i=0;i<4;i++) if(wheels[i].GetGroundHit(out WheelHit h)) { GroundedWheels++; LargestSlip=Mathf.Max(LargestSlip,Mathf.Abs(h.sidewaysSlip)); }
            float magnitude=CanDrive&&input.Enabled?input.Magnitude:0;
            if(magnitude<=0||input.Brake||!CanDrive)assistedYawRate=0;
            float speedLimit=Mathf.Max(1,tuning.maxSpeed);
            float heading=magnitude>0?Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward,Vector3.up),input.WorldDirection,Vector3.up):0;
            float speed01=Mathf.Clamp01(Mathf.Abs(ForwardSpeed)/speedLimit);
            float limit=tuning.steeringBySpeed.Evaluate(speed01);
            float targetSteer=magnitude>0?Mathf.Clamp(heading*.85f,-limit,limit):0;
            steering=Mathf.MoveTowards(steering,targetSteer,tuning.steeringRate*Time.fixedDeltaTime);
            // A rearward screen command means turn the nose around, then travel forward. Never select reverse.
            float turnAngle=Mathf.Clamp(tuning.turnBeforeDriveAngle,30,170);
            float fullAngle=Mathf.Clamp(tuning.fullDriveAngle,0,turnAngle-1);
            float alignment=Mathf.InverseLerp(turnAngle,fullAngle,Mathf.Abs(heading));
            float desiredSpeed=speedLimit*magnitude*alignment;
            float motor=0,brake=0;
            if(!CanDrive || input.Brake)brake=tuning.serviceBrakeTorque;
            else if(magnitude>0) {
                // Wheel steering needs forward motion. Bounded Rigidbody yaw also turns the nose at rest,
                // retaining the dynamic body, collision response, suspension and all four WheelColliders.
                ApplyHeadingAssist(heading,speed01);
                if(ForwardSpeed<-.25f)brake=tuning.serviceBrakeTorque;
                else {
                    float error=desiredSpeed-ForwardSpeed;
                    float power=tuning.torqueBySpeed.Evaluate(Mathf.Clamp01(Mathf.Abs(ForwardSpeed)/speedLimit));
                    float availableTorque=Mathf.Min(tuning.totalDriveTorque,Mathf.Max(.1f,tuning.acceleration)*tuning.mass*tuning.wheelRadius);
                    motor=availableTorque*power*Mathf.Clamp01(error/Mathf.Max(.1f,tuning.driveResponseSpeed));
                    if(error<-.3f)brake=tuning.serviceBrakeTorque*Mathf.Clamp01(-error/3);
                    if(desiredSpeed<.02f&&Speed<.3f)brake=tuning.holdBrakeTorque*.15f;
                }
            } else brake=Speed<.65f?tuning.holdBrakeTorque:tuning.coastBrakeTorque;
            for(int i=0;i<4;i++) { wheels[i].steerAngle=i<2?steering:0; wheels[i].motorTorque=motor*(i<2?(1-tuning.rearTorqueShare):tuning.rearTorqueShare)*.5f; wheels[i].brakeTorque=brake; }
            AntiRoll(0,1); AntiRoll(2,3);
            // Small rolling/air resistance preserves momentum, with no lateral velocity replacement.
            if(GroundedWheels>0) Body.AddForce(-Body.linearVelocity*(18+Speed*1.5f),ForceMode.Force);
        }
        void ApplyHeadingAssist(float heading,float speed01) {
            if(GroundedWheels<2){assistedYawRate=0;return;}
            float response=Mathf.Max(.1f,tuning.steeringResponse);
            float turnLimit=Mathf.Max(1,tuning.rotationSpeed)*Mathf.Lerp(1,.7f,speed01);
            float desiredRate=Mathf.Clamp(heading*response,-turnLimit,turnLimit);
            assistedYawRate=Mathf.MoveTowards(assistedYawRate,desiredRate,Mathf.Max(1,tuning.rotationAcceleration)*Time.fixedDeltaTime);
            float yawStep=assistedYawRate*Time.fixedDeltaTime;
            if(Mathf.Sign(yawStep)==Mathf.Sign(heading)&&Mathf.Abs(yawStep)>Mathf.Abs(heading))yawStep=heading;
            // Static wheel friction can pin a force-only pivot. Apply a small, rate-limited yaw step through
            // the Rigidbody; world-up multiplication preserves its physical pitch/roll instead of flattening it.
            Body.MoveRotation(Quaternion.AngleAxis(yawStep,Vector3.up)*Body.rotation);
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
            transform.SetPositionAndRotation(position,rotation); steering=0; assistedYawRate=0; ForwardSpeed=0;
            foreach(var w in wheels) { w.motorTorque=0; w.brakeTorque=tuning.holdBrakeTorque; w.steerAngle=0; }
            if(dust!=null) foreach(var p in dust) if(p) p.Clear();
        }
    }
}
