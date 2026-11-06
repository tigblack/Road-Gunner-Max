using UnityEngine;
using UnityEngine.Rendering;

namespace RoadGunner
{
    /// <summary>Fixed-capacity ground marks. Walkers stamp measured landings; other vehicles keep wheel marks.</summary>
    [DefaultExecutionOrder(12500)]
    public sealed class TireTracks : MonoBehaviour
    {
        public VehicleController vehicle;
        public Material material;
        const int Capacity=640,QuadsPerStamp=5,VerticesPerStamp=QuadsPerStamp*4;
        const float Lifetime=14,FreshAlpha=.30f;
        readonly Vector3[] vertices=new Vector3[Capacity*VerticesPerStamp];
        readonly Color[] colors=new Color[Capacity*VerticesPerStamp];
        readonly float[] born=new float[Capacity];
        readonly int[] usedQuads=new int[Capacity];
        readonly Vector3[] last=new Vector3[2];
        Mesh mesh;
        GameObject holder;
        PlayerTrackVisuals walker,subscribed;
        int next,poseSerial;
        float refresh;
        bool renderedFootprints;

        // A present active walker suppresses wheel stamping even while its visual rig is initializing.
        public bool IsFootprintMode=>walker&&walker.isActiveAndEnabled&&walker.gameObject.activeInHierarchy;
        public bool ContinuousMode=>!IsFootprintMode;
        public int StampCount {get;private set;}
        public int LeftFootprintCount {get;private set;}
        public int RightFootprintCount {get;private set;}
        public int WheelStampCount {get;private set;}
        public int ActiveStampCount {get;private set;}
        public Mesh ImprintMesh=>mesh;
        public int StampCapacity=>Capacity;
        public float FadeSeconds=>Lifetime;

        void Awake()
        {
            if(!vehicle)vehicle=GetComponent<VehicleController>();
            holder=new GameObject("Ground imprints · bounded pool");
            mesh=new Mesh {name="Dynamic discrete footprints and wheel marks"};mesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=holder.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var uv=new Vector2[vertices.Length];var triangles=new int[Capacity*QuadsPerStamp*6];
            for(int i=0;i<Capacity*QuadsPerStamp;i++)
            {
                int j=i*4,t=i*6;uv[j]=Vector2.zero;uv[j+1]=Vector2.right;uv[j+2]=Vector2.one;uv[j+3]=Vector2.up;
                triangles[t]=j;triangles[t+1]=j+2;triangles[t+2]=j+1;
                triangles[t+3]=j;triangles[t+4]=j+3;triangles[t+5]=j+2;
            }
            mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;
            BindWalker();renderedFootprints=IsFootprintMode;Clear();
        }
        void OnEnable(){BindWalker();}
        void OnDisable(){Unsubscribe();}
        void OnTransformChildrenChanged(){if(isActiveAndEnabled)BindWalker();}
        void BindWalker()
        {
            if(!vehicle)vehicle=GetComponent<VehicleController>();
            walker=vehicle?vehicle.GetComponentInChildren<PlayerTrackVisuals>(true):null;
            if(subscribed==walker)return;
            Unsubscribe();subscribed=walker;
            if(subscribed)subscribed.FootPlanted+=FootPlanted;
        }
        void Unsubscribe()
        {
            if(subscribed)subscribed.FootPlanted-=FootPlanted;
            subscribed=null;
        }
        void SynchronizeMode()
        {
            if(renderedFootprints==IsFootprintMode)return;
            Clear();renderedFootprints=IsFootprintMode;
        }
        public void Clear()
        {
            next=0;StampCount=LeftFootprintCount=RightFootprintCount=WheelStampCount=ActiveStampCount=0;
            for(int i=0;i<Capacity;i++){born[i]=-100;usedQuads[i]=0;}
            System.Array.Clear(colors,0,colors.Length);
            if(mesh)mesh.colors=colors;
            last[0]=last[1]=Vector3.positiveInfinity;
            poseSerial=vehicle?vehicle.PoseResetSerial:0;refresh=0;
        }

        void FootPlanted(PlayerTrackVisuals.FootContact contact)
        {
            if(!mesh||!IsFootprintMode||!walker.IsWalkingRig||!vehicle||!vehicle.CanDrive||Time.timeScale<=0)return;
            SynchronizeMode();
            // The visual contact producer also rejects reset motion. Do not accept a stale landing before our next frame.
            if(poseSerial!=vehicle.PoseResetSerial){poseSerial=vehicle.PoseResetSerial;return;}
            var normal=contact.Normal;
            if(!Finite(contact.Position)||!Finite(normal)||normal.sqrMagnitude<.1f)return;
            normal.Normalize();
            var forward=Vector3.ProjectOnPlane(contact.Forward,normal);
            if(forward.sqrMagnitude<.001f)forward=Vector3.ProjectOnPlane(transform.forward,normal);
            if(forward.sqrMagnitude<.001f)return;
            forward.Normalize();var right=Vector3.Cross(normal,forward).normalized;
            var at=contact.Position+normal*.016f;
            // The actual shoe has two forward talons, a rear heel and an outer stabilizer.
            // Five geometric pads form its three-point mechanical sole, with no tire texture or connecting ribbon.
            Quad(next,0,at,right,forward,0,.02f,.48f,.52f,0);
            Quad(next,1,at,right,forward,-.145f,.42f,.17f,.46f,-13);
            Quad(next,2,at,right,forward,.145f,.42f,.17f,.46f,13);
            Quad(next,3,at,right,forward,0,-.39f,.26f,.40f,0);
            Quad(next,4,at,right,forward,contact.IsLeft?-.30f:.30f,-.02f,.12f,.26f,0);
            CommitStamp(QuadsPerStamp);
            if(contact.IsLeft)LeftFootprintCount++;else RightFootprintCount++;
            LandingDust(contact.Position,normal,forward,contact.IsLeft);
        }
        static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
        void Quad(int stamp,int quad,Vector3 at,Vector3 right,Vector3 forward,float x,float z,float width,float length,float yaw)
        {
            float a=yaw*Mathf.Deg2Rad,c=Mathf.Cos(a),s=Mathf.Sin(a);
            var f=(forward*c+right*s)*(length*.5f);var r=(right*c-forward*s)*(width*.5f);
            var p=at+right*x+forward*z;int j=stamp*VerticesPerStamp+quad*4;
            vertices[j]=p-r-f;vertices[j+1]=p+r-f;vertices[j+2]=p+r+f;vertices[j+3]=p-r+f;
        }
        void CommitStamp(int quads)
        {
            born[next]=Time.time;usedQuads[next]=quads;StampCount++;next=(next+1)%Capacity;
        }
        void LandingDust(Vector3 position,Vector3 normal,Vector3 forward,bool isLeft)
        {
            var systems=vehicle.dust;if(systems==null||systems.Length==0)return;
            int index=isLeft?2:3;if(index>=systems.Length)index=isLeft?0:systems.Length-1;
            var dust=systems[index];if(!dust||dust.main.simulationSpace!=ParticleSystemSimulationSpace.World)return;
            var parameters=new ParticleSystem.EmitParams {position=position+normal*.04f,velocity=normal*.5f-forward*.18f,applyShapeToPosition=false};
            dust.Emit(parameters,3);
        }
        void SuppressContinuousDust()
        {
            if(vehicle.dust==null)return;
            foreach(var dust in vehicle.dust)if(dust)
            {var emission=dust.emission;emission.rateOverTime=0;emission.rateOverDistance=0;}
        }
        void LateUpdate()
        {
            if(!mesh||!vehicle)return;
            SynchronizeMode();
            if(IsFootprintMode)SuppressContinuousDust();
            if(poseSerial!=vehicle.PoseResetSerial)
            {poseSerial=vehicle.PoseResetSerial;last[0]=last[1]=Vector3.positiveInfinity;}
            if(!vehicle.Body||Time.timeScale<=0)return;
            if(!IsFootprintMode)StampWheels();
            if(Time.time<refresh)return;refresh=Time.time+.06f;ActiveStampCount=0;
            for(int i=0;i<Capacity;i++)
            {
                float alpha=Mathf.Clamp01(1-(Time.time-born[i])/Lifetime)*FreshAlpha;
                if(usedQuads[i]>0&&alpha>0)ActiveStampCount++;
                int first=i*VerticesPerStamp,used=usedQuads[i]*4;
                for(int j=0;j<VerticesPerStamp;j++)colors[first+j]=new Color(1,1,1,j<used?alpha:0);
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.RecalculateBounds();
        }
        void StampWheels()
        {
            if(vehicle.wheels==null||vehicle.wheels.Length<4)return;
            bool tracked=vehicle.tuning&&vehicle.tuning.trackedDrive;
            for(int i=0;i<2;i++)
            {
                var wheel=vehicle.wheels[i+2];if(!wheel)continue;
                bool moving=tracked?Mathf.Abs(Vector3.Dot(vehicle.Body.GetPointVelocity(wheel.transform.position),transform.forward))>.35f:vehicle.Speed>1;
                if(!moving||!wheel.GetGroundHit(out WheelHit hit)||Vector3.Distance(last[i],hit.point)<.32f)continue;
                last[i]=hit.point;
                var forward=Vector3.ProjectOnPlane(transform.forward,hit.normal).normalized;
                var right=Vector3.Cross(hit.normal,forward);
                Quad(next,0,hit.point+hit.normal*.016f,right,forward,0,0,tracked?.68f:.38f,.58f,0);
                CommitStamp(1);WheelStampCount++;
            }
        }
        void OnDestroy()
        {
            Unsubscribe();if(holder)Destroy(holder);if(mesh)Destroy(mesh);
        }
    }
}
