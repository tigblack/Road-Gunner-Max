using UnityEngine;
namespace RoadGunner
{
    public sealed class PickupPool : MonoBehaviour
    {
        public GameSession session;
        public CombatTuning tuning;
        public Transform[] items, gearVisuals, scrapVisuals;
        public Collider ground;
        public const float GearFlightTime = .6f;
        int[] values, generations;
        Vector3[] positions, launchFrom;
        float[] launchedAt;
        bool[] flyingGear, metalGear;
        Object[] reservedBy;
        int nextDrop, nextGeneration;
        public event System.Action PoolReset;
        public readonly struct GearHandle
        {
            public readonly int Slot, Generation;
            public GearHandle(int slot,int generation) { Slot=slot;Generation=generation; }
        }
        public int ActiveCount { get; private set; }
        public int FlyingGearCount { get { int n=0;if(values!=null)for(int i=0;i<values.Length;i++)if(values[i]>0&&flyingGear[i]&&Time.time-launchedAt[i]<GearFlightTime)n++;return n; } }
        public int MetalGearCount { get { int n=0;if(values!=null)for(int i=0;i<values.Length;i++)if(values[i]>0&&metalGear[i])n++;return n; } }
        public int ReservedGearCount { get { int n=0;if(values!=null)for(int i=0;i<values.Length;i++)if(values[i]>0&&reservedBy[i])n++;return n; } }
        public int StoredValue { get {int sum=0;if(values!=null)foreach(int v in values)sum+=v;return sum;} }
        public void ResetPool()
        {
            values=new int[items.Length];generations=new int[items.Length];positions=new Vector3[items.Length];launchFrom=new Vector3[items.Length];
            launchedAt=new float[items.Length];flyingGear=new bool[items.Length];metalGear=new bool[items.Length];reservedBy=new Object[items.Length];
            foreach(var p in items)p.gameObject.SetActive(false);ActiveCount=0;nextDrop=0;
            PoolReset?.Invoke();
        }
        public void Drop(Vector3 position,int value) { DropItem(position,Vector3.zero,value,false); }
        public void DropGear(Vector3 position,Vector3 direction,int value=1) { DropItem(position,direction,value,true); }
        Vector3 OnGround(Vector3 position)
        {
            var ray=new Ray(position+Vector3.up*15,Vector3.down);
            if(ground&&ground.Raycast(ray,out var h,40))return h.point+Vector3.up*.25f;
            if(Physics.Raycast(ray,out var hit,40,1,QueryTriggerInteraction.Ignore))return hit.point+Vector3.up*.25f;
            return position;
        }
        void DropItem(Vector3 origin,Vector3 direction,int value,bool gear)
        {
            if(value<=0||items.Length==0)return;if(values==null)ResetPool();
            int slot=-1,nearest=-1;float distance=float.MaxValue;
            for(int i=0;i<items.Length;i++)
            {
                if(values[i]==0&&slot<0)slot=i;
                // Overflow stays in the same resource pool and never overwrites a reservation.
                if(values[i]>0&&metalGear[i]==gear)
                {float d=(items[i].position-origin).sqrMagnitude;if(d<distance){distance=d;nearest=i;}}
            }
            if(slot<0)
            {
                if(nearest<0){nearest=0;for(int i=1;i<items.Length;i++)if((items[i].position-origin).sqrMagnitude<(items[nearest].position-origin).sqrMagnitude)nearest=i;}
                values[nearest]+=value;
                // All gameplay enemy drops use gears. Upgrade an unreserved legacy stack if needed.
                if(gear&&!metalGear[nearest])SetVisual(nearest,true);
                return;
            }
            Vector3 landing=origin;
            if(gear)
            {
                Vector3 flat=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
                if(flat.sqrMagnitude<.01f)flat=Quaternion.Euler(0,nextDrop*137.5f,0)*Vector3.forward;
                flat=Quaternion.Euler(0,nextDrop%2==0?28:-28,0)*flat;nextDrop++;
                float travel=1.3f;
                if(Physics.SphereCast(origin,.12f,flat,out var hit,travel,1,QueryTriggerInteraction.Ignore))travel=Mathf.Max(0,hit.distance-.2f);
                landing+=flat*travel;
            }
            positions[slot]=OnGround(landing);values[slot]=value;generations[slot]=++nextGeneration;reservedBy[slot]=null;
            flyingGear[slot]=gear;launchFrom[slot]=origin;launchedAt[slot]=Time.time;SetVisual(slot,gear);
            items[slot].position=gear?origin:positions[slot];items[slot].rotation=Quaternion.Euler(gear?15:0,slot*137.5f,0);
            items[slot].gameObject.SetActive(true);ActiveCount++;
        }
        void SetVisual(int slot,bool gear)
        {
            metalGear[slot]=gear;
            if(gearVisuals!=null&&slot<gearVisuals.Length&&gearVisuals[slot])gearVisuals[slot].gameObject.SetActive(gear);
            if(scrapVisuals!=null&&slot<scrapVisuals.Length&&scrapVisuals[slot])scrapVisuals[slot].gameObject.SetActive(!gear);
        }
        public bool TryReserveNearestGear(Vector3 origin,float radius,Object owner,out GearHandle handle)
        {
            handle=default;if(!owner||values==null||!session||!session.Playing)return false;
            int nearest=-1;float best=radius*radius;
            for(int i=0;i<values.Length;i++)
            {
                if(values[i]<=0||!metalGear[i]||reservedBy[i]||!items[i].gameObject.activeInHierarchy)continue;
                if(flyingGear[i]&&Time.time-launchedAt[i]<GearFlightTime)continue;
                float d=AttackRange.SquaredDistance(origin,items[i].position);
                if(d<=best){nearest=i;best=d;}
            }
            if(nearest<0)return false;
            reservedBy[nearest]=owner;handle=new GearHandle(nearest,generations[nearest]);return true;
        }
        public bool TryGetReservedGear(GearHandle handle,Object owner,out Transform item)
        {
            item=null;int i=handle.Slot;
            if(!owner||values==null||i<0||i>=values.Length||values[i]<=0||generations[i]!=handle.Generation||reservedBy[i]!=owner||!items[i].gameObject.activeInHierarchy)return false;
            item=items[i];return true;
        }
        public bool MoveReservedGear(GearHandle handle,Object owner,Vector3 position)
        {
            if(!TryGetReservedGear(handle,owner,out var item))return false;
            item.position=position;positions[handle.Slot]=position;return true;
        }
        public void ReleaseGear(GearHandle handle,Object owner)
        {
            if(!TryGetReservedGear(handle,owner,out var item))return;
            int i=handle.Slot;reservedBy[i]=null;positions[i]=OnGround(item.position);flyingGear[i]=false;
        }
        public bool CollectReservedGear(GearHandle handle,Object owner)
        {
            if(!session.Playing||!TryGetReservedGear(handle,owner,out _))return false;
            Collect(handle.Slot);return true;
        }
        void Collect(int i)
        {
            int reward=values[i];values[i]=0;reservedBy[i]=null;items[i].gameObject.SetActive(false);ActiveCount--;
            session.AddScrap(reward);session.effects.Pickup();
        }
        void Update()
        {
            if(!session.Playing||values==null)return;
            for(int i=0;i<items.Length;i++)if(values[i]>0)
            {
                if(reservedBy[i])continue;
                var p=items[i];float flight=(Time.time-launchedAt[i])/GearFlightTime;
                if(flyingGear[i]&&flight<1)
                {
                    p.position=Vector3.Lerp(launchFrom[i],positions[i],flight)+Vector3.up*(Mathf.Sin(flight*Mathf.PI)*.8f);
                    p.rotation=Quaternion.Euler(15+360*flight,i*137.5f+180*flight,75*Mathf.Sin(flight*Mathf.PI));continue;
                }
                if(flyingGear[i]){p.rotation=Quaternion.Euler(15,i*137.5f+180,0);flyingGear[i]=false;}
                p.position=positions[i]+Vector3.up*(Mathf.Sin(Time.time*3+i)*.10f);p.Rotate(0,55*Time.deltaTime,0,Space.World);
                if(AttackRange.SquaredDistance(p.position,session.vehicle.transform.position)<tuning.pickupRadius*tuning.pickupRadius)Collect(i);
            }
        }
    }
}
