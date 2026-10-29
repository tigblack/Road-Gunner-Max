using UnityEngine;
namespace RoadGunner
{
    [System.Serializable]
    public struct TireCurve {
        public float extremumSlip, extremumValue, asymptoteSlip, asymptoteValue, stiffness;
        public WheelFrictionCurve ToWheelCurve() => new WheelFrictionCurve { extremumSlip=extremumSlip, extremumValue=extremumValue, asymptoteSlip=asymptoteSlip, asymptoteValue=asymptoteValue, stiffness=stiffness };
    }
    [CreateAssetMenu(menuName = "Road Gunner/Vehicle Tuning")]
    public sealed class VehicleTuning : ScriptableObject
    {
        public float mass = 1400, maxSpeed = 18, totalDriveTorque = 3100;
        [Header("Wheel-driven handling")]
        [Min(.1f), Tooltip("Maximum requested wheel-drive acceleration in metres per second squared.")]
        public float acceleration = 4;
        // Retained for old editor installers/serialized assets. Wheel driving does not rotate the body.
        [HideInInspector]
        public float rotationSpeed = 150;
        [HideInInspector]
        public float rotationAcceleration = 540;
        [HideInInspector]
        public float steeringResponse = 4;
        [Min(.1f), Tooltip("Speed error over which wheel torque rises to full output.")]
        public float driveResponseSpeed = 3;
        [Range(30,170), Tooltip("Heading error at which the physical turn uses its manoeuvring speed.")]
        public float turnBeforeDriveAngle = 100;
        [Range(0,60)] public float fullDriveAngle = 25;
        [Min(.5f), Tooltip("Forward speed in tight turns. Wheels need rolling motion; there is no pivot/yaw assist.")]
        public float turningSpeed = 4.2f;
        [Min(.1f), Tooltip("Requested front-wheel angle per degree of heading error.")]
        public float headingSteerGain = .85f;
        [Range(0,1), Tooltip("Countersteer from actual yaw rate, applied only through the front tyres.")]
        public float yawCounterSteer = .4f;
        [Range(0,1), Tooltip("Inner/outer front wheel steering geometry, calculated from the actual wheelbase and track.")]
        public float ackermannStrength = 1;
        [Min(0)] public float rollingResistance = 18, aerodynamicDrag = 1.5f;
        [Min(1)] public float maxReverseSpeed = 7;
        [Range(.1f,1)] public float reverseTorqueMultiplier = .7f;
        [Range(0,1)] public float rearTorqueShare = .62f;
        public AnimationCurve torqueBySpeed = new AnimationCurve(new Keyframe(0,1),new Keyframe(.65f,.85f),new Keyframe(1,0));
        public AnimationCurve steeringBySpeed = new AnimationCurve(new Keyframe(0,34),new Keyframe(.5f,25),new Keyframe(1,14));
        public float steeringRate = 125, serviceBrakeTorque = 1100, coastBrakeTorque = 50, holdBrakeTorque = 450;
        public float wheelRadius = .42f, suspensionTravel = .30f, spring = 35000, damper = 4500, suspensionTarget = .5f;
        public float antiRoll = 6500, maxAntiRollForce = 4500;
        public Vector3 centerOfMass = new Vector3(0,.05f,-.10f);
        public TireCurve forwardFriction = new TireCurve { extremumSlip=.4f, extremumValue=1, asymptoteSlip=.8f, asymptoteValue=.65f, stiffness=1.25f };
        public TireCurve sidewaysFriction = new TireCurve { extremumSlip=.22f, extremumValue=1, asymptoteSlip=.55f, asymptoteValue=.7f, stiffness=1.15f };
    }
}
