using UnityEngine;
namespace RoadGunner
{
    public enum HarpoonState { Idle, Aiming, Launching, Retracting }
    [DefaultExecutionOrder(12200)] // Resolve after the gait (12000) has placed the live weapon mount.
    public sealed class HarpoonCollector : MonoBehaviour
    {
        public GameSession session;
        public PickupPool pickups;
        public Transform pivot, spawnPoint, collectionPoint, projectile;
        public LineRenderer cable;
        [Header("Automatic Metal Gear collection")]
        [Min(0)] public float collectionRadius=8;
        [Min(.1f)] public float harpoonLaunchSpeed=36, harpoonRetractSpeed=30;
        [Min(0)] public float delayBetweenCollections=.08f;
        [Min(.02f)] public float searchInterval=.05f;
        [Min(1)] public float aimSpeed=960, maxTetherLength=20;
        [Header("Presentation only")]
        [Range(3,16)] public int cableSegments=8;
        [Range(0,.5f)] public float launchCableSlack=.16f;
        [Min(.01f)] public float cableTensionSeconds=.06f;
        [Min(.01f)] public float launchReactionSeconds=.18f, dockReactionSeconds=.12f;
        public HarpoonState State { get; private set; }
        public Transform Target { get; private set; }
        public int CollectionsCompleted { get; private set; }
        public bool HasReservation => State!=HarpoonState.Idle && Target && pickups && pickups.TryGetReservedGear(handle,this,out _);
        public bool WinchActive => isActiveAndEnabled && session && session.Playing && State==HarpoonState.Retracting && HasReservation;
        public Vector3 HookPosition => tipPosition;
        public float CableTension { get; private set; }
        public float LaunchReaction { get; private set; }
        public float DockReaction { get; private set; }
        /// <summary>A cancelled cycle, never a successful dock. Presentation listeners may stop their reel voice immediately.</summary>
        public event System.Action CollectionCancelled;
        PickupPool.GearHandle handle;
        Vector3 tipPosition;
        float searchAt;
        PickupPool subscribedPool;
        GameSession subscribedSession;
        void OnEnable() { Subscribe();ResetCollection(); }
        void Start() { Subscribe();ResetCollection(); }
        void Subscribe()
        {
            if(subscribedPool!=pickups)
            {
                ResetCollection();
                if(subscribedPool)subscribedPool.PoolReset-=OnPoolReset;
                subscribedPool=pickups;
                if(subscribedPool)subscribedPool.PoolReset+=OnPoolReset;
            }
            if(subscribedSession!=session)
            {
                if(subscribedSession)subscribedSession.Changed-=OnSessionChanged;
                subscribedSession=session;
                if(subscribedSession)subscribedSession.Changed+=OnSessionChanged;
            }
        }
        void OnSessionChanged() { if(session && session.State!=SessionState.Playing && session.State!=SessionState.Paused)ResetCollection(); }
        void OnPoolReset() { ResetCollection();CollectionsCompleted=0; }
        void OnDisable()
        {
            ResetCollection();if(subscribedPool)subscribedPool.PoolReset-=OnPoolReset;subscribedPool=null;
            if(subscribedSession)subscribedSession.Changed-=OnSessionChanged;subscribedSession=null;
        }
        public void ResetCollection() => ClearCycle(true,false);
        void ClearCycle(bool cancelled,bool keepDockReaction)
        {
            bool active=State!=HarpoonState.Idle;
            var pool=subscribedPool?subscribedPool:pickups;
            if(pool&&active)pool.ReleaseGear(handle,this);
            State=HarpoonState.Idle;Target=null;searchAt=Time.time;
            CableTension=LaunchReaction=0;if(!keepDockReaction)DockReaction=0;
            handle=default;tipPosition=spawnPoint?spawnPoint.position:transform.position;
            if(projectile)projectile.gameObject.SetActive(false);
            if(cable)cable.enabled=false;
            if(cancelled&&active)CollectionCancelled?.Invoke();
        }
        void Aim(Vector3 target)
        {
            Vector3 horizontal=Vector3.ProjectOnPlane(target-pivot.position,Vector3.up);
            if(horizontal.sqrMagnitude>.001f)pivot.rotation=Quaternion.RotateTowards(pivot.rotation,Quaternion.LookRotation(horizontal),Mathf.Max(1,aimSpeed)*Time.deltaTime);
        }
        void LateUpdate()
        {
            Subscribe();
            if(!session||!pickups||!pivot||!spawnPoint||!collectionPoint||!projectile||!cable){ResetCollection();return;}
            // Validate held ownership even during Pause; a valid frozen pull is retained unchanged.
            if(State!=HarpoonState.Idle && !HasReservation){ResetCollection();return;}
            if(!session.Playing)
            {
                if(session.State!=SessionState.Paused)ResetCollection();
                return;
            }
            float dt=Time.deltaTime;
            LaunchReaction=Mathf.MoveTowards(LaunchReaction,0,dt/Mathf.Max(.01f,launchReactionSeconds));
            DockReaction=Mathf.MoveTowards(DockReaction,0,dt/Mathf.Max(.01f,dockReactionSeconds));
            int run=session.RunSerial;
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
                tipPosition=spawnPoint.position;projectile.position=tipPosition;projectile.gameObject.SetActive(true);cable.enabled=true;State=HarpoonState.Launching;
                LaunchReaction=1;
                var fx=session.Feedback;if(fx)fx.HarpoonLaunched(tipPosition);
                if(session.RunSerial!=run||State!=HarpoonState.Launching||!session.Playing)return;
            }
            if(State==HarpoonState.Launching)
            {
                Vector3 delta=target.position-tipPosition;
                if(delta.sqrMagnitude>.001f)projectile.rotation=Quaternion.LookRotation(delta);
                tipPosition=Vector3.MoveTowards(tipPosition,target.position,Mathf.Max(.1f,harpoonLaunchSpeed)*Time.deltaTime);
                if((tipPosition-target.position).sqrMagnitude<.0025f)
                {
                    State=HarpoonState.Retracting;var fx=session.Feedback;if(fx)fx.HarpoonLatched(tipPosition);
                    if(session.RunSerial!=run||State!=HarpoonState.Retracting||!session.Playing)return;
                }
            }
            if(State==HarpoonState.Retracting)
            {
                Vector3 delta=collectionPoint.position-tipPosition;
                if(delta.sqrMagnitude>.001f)projectile.rotation=Quaternion.LookRotation(-delta);
                tipPosition=Vector3.MoveTowards(tipPosition,collectionPoint.position,Mathf.Max(.1f,harpoonRetractSpeed)*Time.deltaTime);
                if(!pickups.MoveReservedGear(handle,this,tipPosition)){ResetCollection();return;}
                if((tipPosition-collectionPoint.position).sqrMagnitude<.04f)
                {
                    bool collected=pickups.CollectReservedGear(handle,this);
                    // Collect emits Changed synchronously: Restart/completion must not receive stale dock feedback.
                    if(session.RunSerial!=run||State!=HarpoonState.Retracting)return;
                    if(collected)
                    {
                        CollectionsCompleted++;DockReaction=1;
                        var fx=session.Feedback;if(fx)fx.HarpoonReeled(tipPosition);
                        if(session.RunSerial!=run||State!=HarpoonState.Retracting)return;
                        ClearCycle(false,true);
                    }
                    else ResetCollection();
                    searchAt=Time.time+Mathf.Max(0,delayBetweenCollections);return;
                }
            }
            // Numeric world position avoids accumulating movement from the vehicle parent.
            projectile.position=tipPosition;
            CableTension=Mathf.MoveTowards(CableTension,State==HarpoonState.Retracting?1:0,dt/Mathf.Max(.01f,cableTensionSeconds));
            DrawCable();
        }
        void DrawCable()
        {
            cable.useWorldSpace=true;
            int count=Mathf.Clamp(cableSegments,3,16);if(cable.positionCount!=count)cable.positionCount=count;
            Vector3 start=spawnPoint.position;
            float sag=Mathf.Min(Mathf.Max(0,launchCableSlack),Vector3.Distance(start,tipPosition)*.15f)*(1-CableTension);
            for(int i=0;i<count;i++)
            {
                float t=i/(float)(count-1);
                cable.SetPosition(i,Vector3.Lerp(start,tipPosition,t)-Vector3.up*(4*t*(1-t)*sag));
            }
        }
    }
}
