using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    /// <summary>Deterministic, non-blocking art dressing. No road/terrain/nav/encounter changes.</summary>
    public static class ConceptDressing
    {
        const string SceneRoot="Concept route-shoulder composition";
        const string OldProps="Assets/RoadGunner/Levels/WastelandRestored/Prefabs/";
        static readonly Quaternion Axis=Quaternion.Euler(0,45,0);
        static readonly Dictionary<string,GameObject> sources=new Dictionary<string,GameObject>();
        struct Route
        {
            public string name;public Vector2[] points;public float shoulder,spacing;
            public Route(string n,float w,float interval,params Vector2[] p){name=n;shoulder=w;spacing=interval;points=p.Select(v=>v*2.5f).ToArray();}
        }
        static Vector2 V(float x,float z)=>new Vector2(x,z);
        static GameObject Source(string kind)
        {
            if(sources.TryGetValue(kind,out var old)&&old)return old;
            string path=(kind=="Cactus"||kind=="Boulder"||kind=="Wooden supply crate"?ArtKit.Root+"/Prefabs/":OldProps)+kind+".prefab";
            var g=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!g)throw new Exception("Missing shoulder-dressing source: "+path);return sources[kind]=g;
        }
        static void Add(Transform p,string kind,Vector3 pos,float scale=1,float yaw=0)
        {
            var g=Object.Instantiate(Source(kind),p);g.name=kind;g.transform.localPosition=pos;g.transform.localRotation=Quaternion.Euler(0,yaw,0);g.transform.localScale=Vector3.one*scale;
            foreach(var c in g.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.isStatic=true;
        }
        static GameObject[] Groups()
        {
            var result=new GameObject[8];
            for(int i=0;i<8;i++)
            {
                var g=ArtKit.Node("Shoulder composition "+i);var p=g.transform;
                try
                {
                    // Positive local X points away from the driving lane. The inward edge stays near x=0.
                    if(i<5)
                    {
                        Add(p,"Boulder",new Vector3(.63f,0,.30f),.38f+(i%3)*.10f,i*37);
                        Add(p,"Boulder",new Vector3(1.23f,0,-.63f),.18f+(i%2)*.08f,i*17+42);
                        Add(p,"Dry scrub",new Vector3(.10f,0,-.70f),.82f,i*43);
                        Add(p,"Dry scrub",new Vector3(1.58f,0,.55f),.68f,i*19);
                        if(i!=2)Add(p,"Cactus",new Vector3(1.17f,0,.92f),.68f+(i%3)*.15f,i*71);
                        if(i==3)Add(p,"Boulder",new Vector3(.23f,0,1.23f),.21f,27);
                    }
                    else if(i==5)
                    {
                        Add(p,"Tire stack",new Vector3(.52f,0,.13f),.80f,12);Add(p,"Dry scrub",new Vector3(1.18f,0,-.64f),.85f,84);
                        Add(p,"Boulder",new Vector3(.66f,0,-.86f),.23f,7);Add(p,"Cactus",new Vector3(1.45f,0,.95f),.68f,30);
                    }
                    else if(i==6)
                    {
                        Add(p,"Fuel drums",new Vector3(.68f,0,.17f),.72f,98);Add(p,"Boulder",new Vector3(.48f,0,-.89f),.26f,94);
                        Add(p,"Dry scrub",new Vector3(1.34f,0,1.12f),.82f,140);
                    }
                    else
                    {
                        Add(p,"Wooden supply crate",new Vector3(.62f,0,.10f),.85f,17);Add(p,"Boulder",new Vector3(.96f,0,-.94f),.28f,13);
                        Add(p,"Dry scrub",new Vector3(.06f,0,.92f),.80f,120);Add(p,"Cactus",new Vector3(1.68f,0,.37f),.71f,150);
                    }
                    ArtKit.Bake(p,"Env_Shoulder_Composition_"+i);
                    var visual=p.Find("Finished shared visuals");foreach(Transform child in p.Cast<Transform>().ToArray())if(child!=visual)Object.DestroyImmediate(child.gameObject);
                    g.isStatic=true;if(visual)visual.gameObject.isStatic=true;
                    string path=ArtKit.Root+"/Prefabs/Shoulder composition "+i+".prefab";result[i]=PrefabUtility.SaveAsPrefabAsset(g,path);
                }
                finally{Object.DestroyImmediate(g);}
            }
            return result;
        }
        static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {var delta=b-a;float t=delta.sqrMagnitude<.0001f?0:Mathf.Clamp01(Vector2.Dot(p-a,delta)/delta.sqrMagnitude);return Vector2.Distance(p,a+t*delta);}
        static float RouteDistance(Vector2 p,Route route)
        {float best=float.PositiveInfinity;for(int i=1;i<route.points.Length;i++)best=Mathf.Min(best,SegmentDistance(p,route.points[i-1],route.points[i]));return best;}
        static void AtDistance(Route route,float distance,out Vector2 at,out Vector2 tangent)
        {
            for(int i=1;i<route.points.Length;i++)
            {
                var delta=route.points[i]-route.points[i-1];float length=delta.magnitude;if(distance<=length||i==route.points.Length-1){tangent=delta.normalized;at=route.points[i-1]+tangent*Mathf.Min(distance,length);return;}distance-=length;
            }
            at=route.points[0];tangent=Vector2.up;
        }
        static float Total(Route route){float length=0;for(int i=1;i<route.points.Length;i++)length+=Vector2.Distance(route.points[i-1],route.points[i]);return length;}
        public static string Apply(LevelLayout level)
        {
            if(!level||!level.ground||!level.decorationRoot)throw new Exception("No active gameplay level for shoulder art");
            ArtKit.Ensure();var old=level.decorationRoot.Find(SceneRoot);if(old)Object.DestroyImmediate(old.gameObject);
            var prefabs=Groups();var p=ArtKit.Node(SceneRoot,level.decorationRoot).transform;
            var routes=new[]
            {
                new Route("Main route",7.4f,12.8f,V(0,-55),V(0,-43),V(1.5f,-25),V(0,-5),V(0,22),V(-1,42),V(0,60)),
                new Route("Western road",7.5f,13.1f,V(0,22),V(-17,22),V(-30,24),V(-43,24)),
                new Route("Eastern road",7.2f,13.3f,V(0,22),V(15,22),V(35,23),V(39,33)),
                new Route("Northeast entrance",6.5f,12.1f,V(0,36.5f),V(16,36.5f),V(24,40)),
                new Route("Southwest spur",6.5f,12.7f,V(0,-9),V(-11,-9),V(-22,-16))
            };
            var colliders=level.obstaclesRoot.GetComponentsInChildren<Collider>(true).Concat(level.decorationRoot.GetComponentsInChildren<Collider>(true)).Where(c=>c.enabled&&!c.isTrigger).ToArray();
            var placed=new List<Vector2>();var workshops=Object.FindObjectsByType<RepairWorkshop>(FindObjectsSortMode.None);var inverse=Quaternion.Inverse(Axis);
            var enemyAt=level.session.spawns.authoredRaiders.Where(r=>r).Select(r=>inverse*r.transform.position).Select(v=>new Vector2(v.x,v.z)).ToArray();
            int count=0,rejected=0;var terrain=level.ground.GetComponent<Collider>();
            for(int r=0;r<routes.Length;r++)
            {
                var route=routes[r];float total=Total(route);int station=0;
                for(float distance=5.1f+r*1.71f;distance<total-4;distance+=route.spacing,station++)for(int side=-1;side<=1;side+=2)
                {
                    if(count>=100)break;float offset=(station%3)*.47f;
                    AtDistance(route,distance+(side==1?2.6f:0),out var centre,out var tangent);
                    var normal=new Vector2(tangent.y,-tangent.x)*side;var at=centre+normal*(route.shoulder+offset);
                    bool reject=false;
                    for(int j=0;j<routes.Length;j++)if(j!=r&&RouteDistance(at,routes[j])<routes[j].shoulder+2.2f){reject=true;break;}
                    if(placed.Any(q=>Vector2.Distance(q,at)<6.1f)||enemyAt.Any(q=>Vector2.Distance(q,at)<3.2f))reject=true;
                    var world=Axis*new Vector3(at.x,0,at.y);
                    foreach(var w in workshops){var delta=world-w.transform.position;delta.y=0;if(delta.magnitude<w.healingRadius+4.0f)reject=true;}
                    foreach(var obstacle in colliders)
                    {
                        if(obstacle is BoxCollider){var closest=obstacle.ClosestPoint(world+Vector3.up*.6f);var delta=closest-world;delta.y=0;if(delta.sqrMagnitude<2.5f*2.5f){reject=true;break;}}
                        else {var b=obstacle.bounds;float dx=Mathf.Max(b.min.x-world.x,0,world.x-b.max.x),dz=Mathf.Max(b.min.z-world.z,0,world.z-b.max.z);if(dx*dx+dz*dz<2.5f*2.5f){reject=true;break;}}
                    }
                    var localGround=level.ground.transform.InverseTransformPoint(world);var groundBounds=level.ground.sharedMesh.bounds;if(localGround.x<groundBounds.min.x+3||localGround.x>groundBounds.max.x-3||localGround.z<groundBounds.min.z+3||localGround.z>groundBounds.max.z-3)reject=true;
                    if(reject){rejected++;continue;}
                    if(terrain&&terrain.Raycast(new Ray(world+Vector3.up*30,Vector3.down),out var hit,70))world.y=hit.point.y+.012f;
                    var worldNormal=Axis*new Vector3(normal.x,0,normal.y);var outwardTangent=Vector3.Cross(worldNormal,Vector3.up);var rotation=Quaternion.LookRotation(outwardTangent,Vector3.up);
                    int type=(station*3+r*2+(side==1?1:0))%prefabs.Length;var g=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[type],p);g.name=route.name+" shoulder · "+(count+1).ToString("00");g.transform.SetPositionAndRotation(world,rotation);g.transform.localScale=Vector3.one*(.88f+(station%3)*.09f);PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);placed.Add(at);count++;
                }
            }
            EditorUtility.SetDirty(level);return "Added "+count+" deterministic shared-mesh shoulder compositions; "+rejected+" candidates excluded near obstacles, intersections, enemies or workshop. No colliders, route meshes, navigation or encounter positions changed.";
        }
    }
}
