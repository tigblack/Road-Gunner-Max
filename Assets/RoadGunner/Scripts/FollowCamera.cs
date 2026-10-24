using UnityEngine;
namespace RoadGunner
{
    public sealed class FollowCamera : MonoBehaviour
    {
        public VehicleController vehicle;
        public float damping=.2f, distance=35;
        Vector3 smooth,velocity,look;
        float height,heightVelocity;
        readonly Quaternion rotation=Quaternion.Euler(50,45,0);
        void LateUpdate() {
            if(!vehicle || Time.deltaTime<=0) return;
            height=Mathf.SmoothDamp(height,vehicle.transform.position.y,ref heightVelocity,.4f);
            Vector3 p=vehicle.transform.position; p.y=height;
            Vector3 ahead=Vector3.ClampMagnitude(Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,Vector3.up)*.2f,2.5f);
            look=Vector3.Lerp(look,ahead,1-Mathf.Exp(-Time.deltaTime*4));
            smooth=Vector3.SmoothDamp(smooth,p+look,ref velocity,damping);
            transform.SetPositionAndRotation(smooth-rotation*Vector3.forward*distance+rotation*Vector3.up*.7f,rotation);
        }
        public void Snap() { smooth=vehicle.transform.position; height=smooth.y; heightVelocity=0; velocity=Vector3.zero; look=Vector3.zero; transform.SetPositionAndRotation(smooth-rotation*Vector3.forward*distance+rotation*Vector3.up*.7f,rotation); }
    }
}
