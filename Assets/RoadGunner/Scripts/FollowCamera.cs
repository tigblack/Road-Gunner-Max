using UnityEngine;
namespace RoadGunner
{
    public sealed class FollowCamera : MonoBehaviour
    {
        public VehicleController vehicle;
        public float damping=.2f, distance=35;
        [Header("Arcade shake: position-only offset in the camera plane, never rotation")]
        [Min(0)] public float shakeDistance=.8f;
        [Min(.1f)] public float shakeFrequency=17, traumaDecay=1.9f;
        public float Trauma => trauma;
        Vector3 smooth,velocity,look;
        float height,heightVelocity,trauma,shakeTime;
        readonly Quaternion rotation=Quaternion.Euler(50,45,0);
        void LateUpdate() {
            if(!vehicle || Time.deltaTime<=0) return;
            height=Mathf.SmoothDamp(height,vehicle.transform.position.y,ref heightVelocity,.4f);
            Vector3 p=vehicle.transform.position; p.y=height;
            Vector3 ahead=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up)*.2f,2.5f);
            look=Vector3.Lerp(look,ahead,1-Mathf.Exp(-Time.deltaTime*4));
            smooth=Vector3.SmoothDamp(smooth,p+look,ref velocity,damping);
            transform.SetPositionAndRotation(smooth-rotation*Vector3.forward*distance+rotation*Vector3.up*.7f+Shake(),rotation);
        }
        // Trauma^2 gives small hits a light tap and explosions a real kick; the offset is recomputed every frame, so trauma 0 follows exactly.
        Vector3 Shake() {
            if(trauma<=0) return Vector3.zero;
            shakeTime+=Time.deltaTime; float amount=trauma*trauma*shakeDistance,t=shakeTime*shakeFrequency;
            trauma=Mathf.Max(0,trauma-traumaDecay*Time.deltaTime);
            return rotation*new Vector3((Mathf.PerlinNoise(t,.31f)-.5f)*2,(Mathf.PerlinNoise(.73f,t)-.5f)*2,0)*amount;
        }
        /// <summary>Presentation-only camera kick (0..1 accumulates and decays quickly).</summary>
        public void AddTrauma(float amount) { if(amount>0 && isActiveAndEnabled) trauma=Mathf.Clamp01(trauma+amount); }
        public void Snap() { trauma=0; smooth=vehicle.transform.position; height=smooth.y; heightVelocity=0; velocity=Vector3.zero; look=Vector3.zero; transform.SetPositionAndRotation(smooth-rotation*Vector3.forward*distance+rotation*Vector3.up*.7f,rotation); }
    }
}
