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
