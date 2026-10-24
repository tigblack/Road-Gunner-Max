using UnityEngine;
namespace RoadGunner
{
    public enum HarpoonState { Idle, Aiming, Launching, Retracting }
    public sealed class HarpoonCollector : MonoBehaviour
    {
        public GameSession session;
        public PickupPool pickups;
        public Transform pivot, spawnPoint, collectionPoint, projectile;
        public LineRenderer cable;
        [Header("Automatic Metal Gear collection")]
        [Min(0)] public float collectionRadius=8;
        [Min(.1f)] public float harpoonLaunchSpeed=18, harpoonRetractSpeed=12;
        [Min(0)] public float delayBetweenCollections=.3f;
        [Min(.02f)] public float searchInterval=.15f;
        [Min(1)] public float aimSpeed=480, maxTetherLength=20;
        public HarpoonState State { get; private set; }
        public Transform Target { get; private set; }
        public int CollectionsCompleted { get; private set; }
        PickupPool.GearHandle handle;
        Vector3 tipPosition;
        float searchAt;
        PickupPool subscribedPool;
        void OnEnable() { Subscribe();ResetCollection(); }
        void Start() { Subscribe();ResetCollection(); }
        void Subscribe()
        {
            if(subscribedPool==pickups)return;
            if(subscribedPool)subscribedPool.PoolReset-=OnPoolReset;
            subscribedPool=pickups;
            if(subscribedPool)subscribedPool.PoolReset+=OnPoolReset;
        }
        void OnPoolReset() { ResetCollection();CollectionsCompleted=0; }
        void OnDisable()
        {
            ResetCollection();if(subscribedPool)subscribedPool.PoolReset-=OnPoolReset;subscribedPool=null;
        }
        public void ResetCollection()
        {
            if(pickups&&State!=HarpoonState.Idle)pickups.ReleaseGear(handle,this);
            State=HarpoonState.Idle;Target=null;searchAt=Time.time;
            if(projectile)projectile.gameObject.SetActive(false);
            if(cable){cable.enabled=false;cable.positionCount=2;}
        }
        void Aim(Vector3 target)
        {
            Vector3 horizontal=Vector3.ProjectOnPlane(target-pivot.position,Vector3.up);
            if(horizontal.sqrMagnitude>.001f)pivot.rotation=Quaternion.RotateTowards(pivot.rotation,Quaternion.LookRotation(horizontal),Mathf.Max(1,aimSpeed)*Time.deltaTime);
        }
        void LateUpdate()
        {
            if(!session||!session.Playing||!pickups||!pivot||!spawnPoint||!collectionPoint||!projectile||!cable)return;
            if(State==HarpoonState.Idle)
            {
                if(Time.time<searchAt)return;searchAt=Time.time+Mathf.Max(.02f,searchInterval);
                if(!pickups.TryReserveNearestGear(session.vehicle.transform.position,Mathf.Max(0,collectionRadius),this,out handle))return;
                State=HarpoonState.Aiming;
            }
            if(!pickups.TryGetReservedGear(handle,this,out var target)){ResetCollection();return;}
            Target=target;
            if(AttackRange.SquaredDistance(collectionPoint.position,target.position)>maxTetherLength*maxTetherLength){ResetCollection();searchAt=Time.time+delayBetweenCollections;return;}
            if(State==HarpoonState.Aiming||State==HarpoonState.Launching)Aim(target.position);
            if(State==HarpoonState.Aiming)
            {
                Vector3 flat=Vector3.ProjectOnPlane(target.position-pivot.position,Vector3.up);
                if(flat.sqrMagnitude>.001f&&Vector3.Angle(pivot.forward,flat)>5)return;
                tipPosition=spawnPoint.position;projectile.gameObject.SetActive(true);cable.enabled=true;State=HarpoonState.Launching;
            }
            if(State==HarpoonState.Launching)
            {
                Vector3 delta=target.position-tipPosition;
                if(delta.sqrMagnitude>.001f)projectile.rotation=Quaternion.LookRotation(delta);
                tipPosition=Vector3.MoveTowards(tipPosition,target.position,Mathf.Max(.1f,harpoonLaunchSpeed)*Time.deltaTime);
                if((tipPosition-target.position).sqrMagnitude<.0025f)State=HarpoonState.Retracting;
            }
            if(State==HarpoonState.Retracting)
            {
                Vector3 delta=collectionPoint.position-tipPosition;
                if(delta.sqrMagnitude>.001f)projectile.rotation=Quaternion.LookRotation(-delta);
                tipPosition=Vector3.MoveTowards(tipPosition,collectionPoint.position,Mathf.Max(.1f,harpoonRetractSpeed)*Time.deltaTime);
                pickups.MoveReservedGear(handle,this,tipPosition);
                if((tipPosition-collectionPoint.position).sqrMagnitude<.04f)
                {
                    if(pickups.CollectReservedGear(handle,this))CollectionsCompleted++;
                    ResetCollection();searchAt=Time.time+Mathf.Max(0,delayBetweenCollections);return;
                }
            }
            // Numeric world position avoids accumulating movement from the vehicle parent.
            projectile.position=tipPosition;
            cable.SetPosition(0,spawnPoint.position);cable.SetPosition(1,tipPosition);
        }
    }
}
