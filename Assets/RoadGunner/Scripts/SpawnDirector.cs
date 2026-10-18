using UnityEngine;
using UnityEngine.AI;
namespace RoadGunner
{
    public sealed class SpawnDirector : MonoBehaviour
    {
        public GameSession session;
        public CombatTuning tuning;
        public Raider[] pool;
        public EnemyBuggy[] buggyPool;
        public Transform[] buggySpawnPoints;
        public Vector3[] groupCenters;
        [Tooltip("Optional authored encounters. Empty preserves the prototype's legacy group centers.")]
        public EncounterSpawn[] encounters;
        [Header("Scene-authored enemies (also registered in the existing pools)")]
        public Raider[] authoredRaiders = System.Array.Empty<Raider>();
        public EnemyBuggy[] authoredBuggies = System.Array.Empty<EnemyBuggy>();
        public bool repeatAuthoredEnemies;
        public Camera view;
        Vector3[] authoredHumanPoints, authoredVehiclePoints;
        Quaternion[] authoredHumanRotations, authoredVehicleRotations;
        float[] authoredHumanReady, authoredVehicleReady;
        [System.NonSerialized] bool authoredStartsCaptured;
        void CaptureAuthoredStarts() {
            if(authoredStartsCaptured && authoredHumanPoints.Length==authoredRaiders.Length && authoredVehiclePoints.Length==authoredBuggies.Length)return;
            authoredHumanPoints=new Vector3[authoredRaiders.Length];authoredHumanRotations=new Quaternion[authoredRaiders.Length];authoredHumanReady=new float[authoredRaiders.Length];
            authoredVehiclePoints=new Vector3[authoredBuggies.Length];authoredVehicleRotations=new Quaternion[authoredBuggies.Length];authoredVehicleReady=new float[authoredBuggies.Length];
            for(int i=0;i<authoredRaiders.Length;i++)if(authoredRaiders[i]){authoredHumanPoints[i]=authoredRaiders[i].transform.position;authoredHumanRotations[i]=authoredRaiders[i].transform.rotation;}
            for(int i=0;i<authoredBuggies.Length;i++)if(authoredBuggies[i]){authoredVehiclePoints[i]=authoredBuggies[i].transform.position;authoredVehicleRotations[i]=authoredBuggies[i].transform.rotation;}
            authoredStartsCaptured=true;
        }
        void SpawnAuthoredHuman(int i) {
            var r=authoredRaiders[i];if(!r || ActiveCount>=tuning.activeCap)return;
            if(!NavMesh.SamplePosition(authoredHumanPoints[i],out var hit,3,NavMesh.AllAreas))return;
            r.Spawn(hit.position,-i-1,i);r.transform.rotation=authoredHumanRotations[i];poolActiveCount++;
        }
        void SpawnAuthoredVehicle(int i) {
            var b=authoredBuggies[i];if(!b || ActiveCount>=tuning.activeCap)return;
            if(!NavMesh.SamplePosition(authoredVehiclePoints[i],out var hit,3,NavMesh.AllAreas))return;
            b.Spawn(hit.position,authoredVehicleRotations[i],-i-1);poolActiveCount++;
        }
        int poolActiveCount;
        public int ActiveCount => poolActiveCount + (session && session.zone1 ? session.zone1.ActiveBossCount : 0);
        float[] readyAt;
        float[] buggyReadyAt;
        float nextCheck;
        NavMeshPath spawnPath;
        Vector3 reachableAnchor;
        readonly Collider[] occupancy=new Collider[8];
        public int GroupCount => encounters!=null && encounters.Length>0 ? encounters.Length : groupCenters?.Length??0;
        Vector3 Center(int group) => encounters!=null && group<encounters.Length && encounters[group]
            ? encounters[group].transform.position : groupCenters!=null && group<groupCenters.Length ? groupCenters[group] : session.startPosition;
        int GroupSize(int group) => encounters!=null && group<encounters.Length && encounters[group]
            ? Mathf.Max(1,encounters[group].enemyCount) : tuning.groupSize;
        Vector3 SpawnPoint(int group,int index,int size) {
            if(encounters!=null && group<encounters.Length && encounters[group])return encounters[group].Position(index);
            float angle=index*Mathf.PI*2/size+group;
            return Center(group)+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*2.3f;
        }
        public void ResetPopulation() {
            CaptureAuthoredStarts();
            spawnPath??=new NavMeshPath();
            if(NavMesh.SamplePosition(session.startPosition,out NavMeshHit anchor,3,NavMesh.AllAreas))reachableAnchor=anchor.position;
            readyAt=new float[GroupCount];foreach(var r in pool)r.Despawn();
            buggyReadyAt=new float[buggySpawnPoints?.Length??0];
            if(buggyPool!=null)foreach(var buggy in buggyPool)if(buggy)buggy.Despawn();
            poolActiveCount=0;nextCheck=Time.time+.5f;
            for(int i=0;i<authoredRaiders.Length;i++){authoredHumanReady[i]=0;SpawnAuthoredHuman(i);}
            for(int i=0;i<authoredBuggies.Length;i++){authoredVehicleReady[i]=0;SpawnAuthoredVehicle(i);}
            for(int g=0;g<GroupCount;g++)SpawnGroup(g,true);
            for(int i=0;i<buggyReadyAt.Length;i++)SpawnBuggy(i,true);
        }
        void Update() {
            if(!session.Playing||Time.time<nextCheck)return;nextCheck=Time.time+.5f;Count();
            if(repeatAuthoredEnemies) {
                for(int i=0;i<authoredRaiders.Length;i++) {
                    var r=authoredRaiders[i];if(!r)continue;
                    if(r.Alive)authoredHumanReady[i]=Time.time+r.tuning.replenishDelay;
                    else if(!r.gameObject.activeSelf&&Time.time>=authoredHumanReady[i]&&!InView(authoredHumanPoints[i],.1f)&&(authoredHumanPoints[i]-session.vehicle.transform.position).sqrMagnitude>tuning.spawnDistance*tuning.spawnDistance)SpawnAuthoredHuman(i);
                }
                for(int i=0;i<authoredBuggies.Length;i++) {
                    var b=authoredBuggies[i];if(!b)continue;
                    if(b.Alive)authoredVehicleReady[i]=Time.time+b.tuning.respawnDelay;
                    else if(!b.gameObject.activeSelf&&Time.time>=authoredVehicleReady[i]&&!InView(authoredVehiclePoints[i],.2f)&&(authoredVehiclePoints[i]-session.vehicle.transform.position).sqrMagnitude>tuning.spawnDistance*tuning.spawnDistance)SpawnAuthoredVehicle(i);
                }
            }
            for(int g=0;g<readyAt.Length;g++) {
                bool occupied=false;foreach(var r in pool)if(r.Alive&&r.Group==g){occupied=true;break;}
                if(occupied){readyAt[g]=Time.time+tuning.replenishDelay;continue;}
                if(Time.time>=readyAt[g]&&SpawnGroup(g,false))readyAt[g]=Time.time+tuning.replenishDelay;
            }
            for(int i=0;i<buggyReadyAt.Length;i++) {
                bool occupied=false;if(buggyPool!=null)foreach(var buggy in buggyPool)if(buggy && buggy.Alive && buggy.SpawnSlot==i){occupied=true;buggyReadyAt[i]=Time.time+buggy.tuning.respawnDelay;break;}
                if(!occupied && Time.time>=buggyReadyAt[i])SpawnBuggy(i,false);
            }
        }
        void Count(){poolActiveCount=0;foreach(var r in pool)if(r.Alive)poolActiveCount++;if(buggyPool!=null)foreach(var buggy in buggyPool)if(buggy && buggy.Alive)poolActiveCount++;}
        bool SpawnBuggy(int slot,bool initial) {
            if(buggyPool==null || ActiveCount>=tuning.activeCap || !buggySpawnPoints[slot])return false;
            var marker=buggySpawnPoints[slot];
            if(!NavMesh.SamplePosition(marker.position,out NavMeshHit hit,3,NavMesh.AllAreas))return false;
            if(!initial && (InView(hit.position,.2f)||(hit.position-session.vehicle.transform.position).sqrMagnitude<tuning.spawnDistance*tuning.spawnDistance))return false;
            if(Physics.CheckBox(hit.position+Vector3.up*.95f,new Vector3(1.25f,.3f,1.9f),marker.rotation,1,QueryTriggerInteraction.Ignore))return false;
            if(!NavMesh.CalculatePath(hit.position,reachableAnchor,NavMesh.AllAreas,spawnPath)||spawnPath.status!=NavMeshPathStatus.PathComplete)return false;
            foreach(var buggy in buggyPool)if(buggy && System.Array.IndexOf(authoredBuggies,buggy)<0 && !buggy.gameObject.activeSelf){buggy.Spawn(hit.position,marker.rotation,slot);poolActiveCount++;return true;}
            return false;
        }
        bool SpawnGroup(int group,bool initial) {
            Vector3 center=Center(group);
            if(!initial && ((center-session.vehicle.transform.position).sqrMagnitude<tuning.spawnDistance*tuning.spawnDistance || InView(center, .12f)))return false;
            bool any=false;
            int size=GroupSize(group);
            for(int j=0;j<size&&ActiveCount<tuning.activeCap;j++) {
                Vector3 desired=SpawnPoint(group,j,size);
                if(!NavMesh.SamplePosition(desired,out NavMeshHit hit,3,NavMesh.AllAreas))continue;
                if(!initial&&(InView(hit.position,.1f)||(hit.position-session.vehicle.transform.position).sqrMagnitude<tuning.spawnDistance*tuning.spawnDistance))continue;
                int count=Physics.OverlapCapsuleNonAlloc(hit.position+Vector3.up*.55f,hit.position+Vector3.up*1.25f,.4f,occupancy,1,QueryTriggerInteraction.Ignore);
                if(count>0)continue;
                if(!NavMesh.CalculatePath(hit.position,reachableAnchor,NavMesh.AllAreas,spawnPath)||spawnPath.status!=NavMeshPathStatus.PathComplete)continue;
                for(int i=0;i<pool.Length;i++)if(pool[i] && System.Array.IndexOf(authoredRaiders,pool[i])<0 && !pool[i].gameObject.activeSelf){pool[i].Spawn(hit.position,group,i);poolActiveCount++;any=true;break;}
            }
            return any;
        }
        public bool InView(Vector3 p,float margin=0){Vector3 v=view.WorldToViewportPoint(p+Vector3.up);return v.z>0&&v.x>-margin&&v.x<1+margin&&v.y>-margin&&v.y<1+margin;}
        public void CancelAttacks(){foreach(var r in pool)if(r)r.CancelAttack();if(buggyPool!=null)foreach(var buggy in buggyPool)if(buggy)buggy.CancelAttack();if(session&&session.zone1)session.zone1.CancelAttacks();}
    }
}
