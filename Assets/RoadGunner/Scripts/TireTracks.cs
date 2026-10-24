using UnityEngine;
using UnityEngine.Rendering;
namespace RoadGunner
{
    // Fixed-capacity mesh: no per-stamp GameObjects, materials or allocations.
    public sealed class TireTracks : MonoBehaviour
    {
        public VehicleController vehicle;
        public Material material;
        const int Capacity=640;
        readonly Vector3[] vertices=new Vector3[Capacity*4];
        readonly Color[] colors=new Color[Capacity*4];
        readonly float[] born=new float[Capacity];
        readonly Vector3[] last=new Vector3[2];
        Mesh mesh;GameObject holder;int next;float refresh;
        void Awake(){
            holder=new GameObject("Tire imprints • bounded pool");mesh=new Mesh{name="Dynamic sand tracks"};mesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=holder.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var uv=new Vector2[Capacity*4];var triangles=new int[Capacity*6];
            for(int i=0;i<Capacity;i++){int j=i*4,t=i*6;uv[j]=Vector2.zero;uv[j+1]=Vector2.right;uv[j+2]=Vector2.one;uv[j+3]=Vector2.up;triangles[t]=j;triangles[t+1]=j+2;triangles[t+2]=j+1;triangles[t+3]=j;triangles[t+4]=j+3;triangles[t+5]=j+2;born[i]=-100;}
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;
        }
        public void Clear(){next=0;for(int i=0;i<Capacity;i++)born[i]=-100;if(mesh){System.Array.Clear(colors,0,colors.Length);mesh.colors=colors;}last[0]=last[1]=Vector3.positiveInfinity;}
        void LateUpdate(){
            if(!mesh||!vehicle.Body||Time.timeScale<=0)return;
            if(vehicle.Speed>1)for(int i=0;i<2;i++)if(vehicle.wheels[i+2].GetGroundHit(out WheelHit hit)){
                if(Vector3.Distance(last[i],hit.point)<.32f)continue;last[i]=hit.point;
                var forward=Vector3.ProjectOnPlane(transform.forward,hit.normal).normalized*.29f;var right=Vector3.Cross(hit.normal,forward.normalized)*.19f;var p=hit.point+hit.normal*.016f;int j=next*4;
                vertices[j]=p-right-forward;vertices[j+1]=p+right-forward;vertices[j+2]=p+right+forward;vertices[j+3]=p-right+forward;born[next]=Time.time;next=(next+1)%Capacity;
            }
            if(Time.time<refresh)return;refresh=Time.time+.06f;
            for(int i=0;i<Capacity;i++){float alpha=Mathf.Clamp01(1-(Time.time-born[i])/14)*.30f;for(int j=0;j<4;j++)colors[i*4+j]=new Color(1,1,1,alpha);}
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
        }
        void OnDestroy(){if(holder)Destroy(holder);if(mesh)Destroy(mesh);}
    }
}
