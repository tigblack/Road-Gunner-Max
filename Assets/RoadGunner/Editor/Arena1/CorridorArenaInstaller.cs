using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reauthors routes, blocking landscape and placements inside the existing scene and Level Editor.</summary>
    public static class CorridorArenaInstaller
    {
        public const string AssetRoot = "Assets/RoadGunner/Levels/Greenwood/Arena1/Corridor";
        public const string ReportPath = "Documentation/RoadGunner/CorridorRevision-2026-10-25/Layout.txt";
        static readonly Quaternion Axis = Quaternion.Euler(0,45,0);
        static readonly Vector2[] MainKnots = {
            new Vector2(0,-48),new Vector2(0,-30),new Vector2(0,-28),new Vector2(-7,-19),
            new Vector2(-7,-7),new Vector2(2,3),new Vector2(2,16),new Vector2(-4,26),
            new Vector2(-4,34),new Vector2(-4,39),new Vector2(2,49),new Vector2(2,56)
        };
        static readonly Vector2[] BossKnots = {new Vector2(2,16),new Vector2(-10,17),new Vector2(-19,26),new Vector2(-34,30)};
        static readonly Vector2[] MonsterKnots = {new Vector2(-4,34),new Vector2(8,34),new Vector2(18,35),new Vector2(28,40),new Vector2(39,43)};
        static readonly Vector2[] WorkshopKnots = {new Vector2(0,-30),new Vector2(4,-27),new Vector2(8,-27)};
        static readonly Vector2[] RaiderPoints = {
            new Vector2(-1.7f,-30),new Vector2(1.7f,-27),new Vector2(-8,-14),new Vector2(-5.5f,-10),
            new Vector2(1,7),new Vector2(4,11),new Vector2(-5.2f,29),new Vector2(-2.8f,33),
            new Vector2(-16,22),new Vector2(-21,26),new Vector2(22,37),new Vector2(27,40)
        };
        struct Road { public Vector2[] points; public float width; public string name; }
        struct Pocket { public Vector2 center; public float radius; public string name; public Material material; }
        struct CellRect { public int x,z,width,depth; }
        static MeshCollider ground;
        static readonly List<Road> roads = new List<Road>();
        static readonly List<Pocket> pockets = new List<Pocket>();
        static readonly List<Vector3> footprint = new List<Vector3>();
        static readonly List<Transform> preserved = new List<Transform>();
        public static Vector3 World(float x,float z) => Arena1Installer.World(x,z);
        static Vector2 Local(Vector3 p) {var v=Quaternion.Inverse(Axis)*p;return new Vector2(v.x,v.z);}
        static Vector3 At(Vector2 p,float above=0) {
            var world=Axis*new Vector3(p.x,0,p.y);
            if(ground&&ground.Raycast(new Ray(world+Vector3.up*200,Vector3.down),out var hit,400))world.y=hit.point.y;
            return world+Vector3.up*above;
        }
        static void Dirty(Object obj) {if(!obj)return;EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
        static Transform Node(Transform parent,string name) => RoadGunnerLevelAuthoring.Section(parent,name);
        static T Save<T>(T asset,string name) where T:Object {
            string path=AssetRoot+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old){EditorUtility.CopySerialized(asset,old);Object.DestroyImmediate(asset);Dirty(old);return old;}
            AssetDatabase.CreateAsset(asset,path);return asset;
        }
        static Material Material(string name,Color color) {
            string path=AssetRoot+"/"+name+".mat";var value=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!value){value=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(value,path);}
            value.SetColor("_BaseColor",color);value.SetFloat("_Smoothness",.06f);value.enableInstancing=true;Dirty(value);return value;
        }
        static Vector2[] Sample(Vector2[] knots) {
            var points=new List<Vector2>();
            for(int i=0;i<knots.Length-1;i++) {
                // Keep the onboarding straight. Catmull-Rom softens the following bends for a physical car.
                Vector2 a=knots[Mathf.Max(0,i-1)]*2.5f,b=knots[i]*2.5f,c=knots[i+1]*2.5f,d=knots[Mathf.Min(knots.Length-1,i+2)]*2.5f;
                int steps=Mathf.Max(2,Mathf.CeilToInt(Vector2.Distance(b,c)/3));
                for(int j=0;j<steps;j++){float t=j/(float)steps;Vector2 p=i<2&&knots==MainKnots?Vector2.Lerp(b,c,t):.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);points.Add(p);}
            }
            points.Add(knots[knots.Length-1]*2.5f);return points.ToArray();
        }
        static float SegmentDistance(Vector2 point,Vector2 a,Vector2 b) {var d=b-a;float t=d.sqrMagnitude>.001f?Mathf.Clamp01(Vector2.Dot(point-a,d)/d.sqrMagnitude):0;return Vector2.Distance(point,a+d*t);}
        static bool Free(Vector2 point,float padding=0) {
            foreach(var road in roads)for(int i=1;i<road.points.Length;i++)if(SegmentDistance(point,road.points[i-1],road.points[i])<=road.width*.5f+padding)return true;
            foreach(var pocket in pockets)if(Vector2.Distance(point,pocket.center)<=pocket.radius+padding)return true;
            return false;
        }
        static Mesh Surface(string name,Vector3[] vertices,int[] triangles) {var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return Save(mesh,name);}
        static void Render(Transform parent,string name,Mesh mesh,Material material) {
            var go=Node(parent,name).gameObject;go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.receiveShadows=true;renderer.shadowCastingMode=ShadowCastingMode.Off;go.isStatic=true;
        }
        static void RoadMesh(Transform parent,Road road,Material material) {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<road.points.Length;i++) {
                Vector2 forward=road.points[Mathf.Min(i+1,road.points.Length-1)]-road.points[Mathf.Max(i-1,0)];forward.Normalize();var side=new Vector2(forward.y,-forward.x);
                vertices.Add(At(road.points[i]-side*road.width*.5f,.055f));vertices.Add(At(road.points[i]+side*road.width*.5f,.055f));
                if(i>0){int a=i*2;triangles.AddRange(new[]{a-2,a,a-1,a-1,a,a+1});}
                for(int s=-1;s<=1;s+=2)footprint.Add(At(road.points[i]+side*s*(road.width*.5f+7)));
            }
            Render(parent,"Corridor road · "+road.name,Surface("Corridor road "+road.name,vertices.ToArray(),triangles.ToArray()),material);
        }
        static void PocketMesh(Transform parent,Pocket pocket) {
            const int count=48;var vertices=new Vector3[count+1];var indices=new int[count*3];vertices[0]=At(pocket.center,.047f);
            for(int i=0;i<count;i++){float a=i*Mathf.PI*2/count;var p=pocket.center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*pocket.radius;vertices[i+1]=At(p,.047f);int k=i*3;indices[k]=0;indices[k+1]=(i+1)%count+1;indices[k+2]=i+1;footprint.Add(At(pocket.center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(pocket.radius+7)));}
            Render(parent,"Corridor region · "+pocket.name,Surface("Corridor region "+pocket.name,vertices,indices),pocket.material);
        }
        public static ArenaCorridorLayout Configure(LevelLayout layout) {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before corridor authoring.");
            if(!layout||!layout.session)throw new InvalidOperationException("Open the existing Arena 1 scene.");
            var s=layout.session;ground=layout.ground.GetComponent<MeshCollider>();
            if(!ground)throw new InvalidOperationException("Keep the existing terrain collider.");
            Directory.CreateDirectory(AssetRoot);Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));AssetDatabase.Refresh();
            roads.Clear();pockets.Clear();footprint.Clear();preserved.Clear();
            var roadMaterial=Material("Corridor warm road",new Color(.84f,.65f,.36f));
            var clearingMaterial=Material("Corridor yellow clearings",new Color(.90f,.73f,.43f));
            var workshopMaterial=Material("Corridor green service ground",new Color(.44f,.65f,.35f));
            var bossMaterial=Material("Corridor boss rust ground",new Color(.78f,.53f,.31f));
            var monsterMaterial=Material("Corridor mutant sage ground",new Color(.57f,.68f,.33f));
            var lockedMaterial=Material("Corridor closed arena grey",new Color(.49f,.48f,.58f));
            roads.Add(new Road{points=Sample(MainKnots),width=14,name="Main north road"});
            roads.Add(new Road{points=Sample(BossKnots),width=16,name="Left boss branch"});
            roads.Add(new Road{points=Sample(MonsterKnots),width=16,name="Far right monster branch"});
            roads.Add(new Road{points=Sample(WorkshopKnots),width=14,name="Right repair spur"});
            roads.Add(new Road{points=Sample(new[]{new Vector2(2,56),new Vector2(2,65)}),width=14,name="Closed north entrance"});
            pockets.Add(new Pocket{center=new Vector2(0,-46)*2.5f,radius=16,name="Start onboarding",material=clearingMaterial});
            pockets.Add(new Pocket{center=new Vector2(8,-27)*2.5f,radius=14,name="Repair workshop",material=workshopMaterial});
            pockets.Add(new Pocket{center=new Vector2(-7,-12)*2.5f,radius=15,name="Roadside Raiders",material=clearingMaterial});
            pockets.Add(new Pocket{center=new Vector2(2,10)*2.5f,radius=19,name="First Buggy clearing",material=clearingMaterial});
            pockets.Add(new Pocket{center=new Vector2(25,38)*2.5f,radius=19,name="Right Buggy clearing",material=clearingMaterial});
            pockets.Add(new Pocket{center=new Vector2(-34,30)*2.5f,radius=24,name="Boss Raider arena",material=bossMaterial});
            pockets.Add(new Pocket{center=new Vector2(39,43)*2.5f,radius=20,name="Monster arena",material=monsterMaterial});
            pockets.Add(new Pocket{center=new Vector2(2,52)*2.5f,radius=17,name="North turnaround",material=clearingMaterial});
            pockets.Add(new Pocket{center=new Vector2(2,65)*2.5f,radius=15,name="Locked future arena",material=lockedMaterial});
            var old=layout.authoredContentRoot.Find("Corridor Arena 1");if(old)Object.DestroyImmediate(old.gameObject);
            var root=Node(layout.authoredContentRoot,"Corridor Arena 1");var roadRoot=Node(root,"Corridor Routes and Pockets");var blockerRoot=Node(root,"Corridor solid rock bands");
            var data=layout.GetComponent<ArenaCorridorLayout>();if(!data)data=layout.gameObject.AddComponent<ArenaCorridorLayout>();
            data.geometryRoot=root;data.roadsRoot=roadRoot;data.blockersRoot=blockerRoot;data.roadWidth=14;data.branchWidth=16;
            var workshop=RoadGunnerLevelAuthoring.InScene<RepairWorkshop>().First(w=>w.session==s);
            workshop.transform.SetPositionAndRotation(At(new Vector2(8,-27)*2.5f),Quaternion.Euler(0,135,0));Dirty(workshop.transform);data.workshop=workshop.transform;
            // Preserve the old route meshes in the hierarchy but retire their contradictory open-field road network.
            var legacyRoads=layout.decorationRoot.Find("00 · Routes and clear ground");if(legacyRoads){legacyRoads.gameObject.SetActive(false);Dirty(legacyRoads.gameObject);}
            ClearLegacyLanes(layout,root,workshop.transform);
            foreach(var road in roads)RoadMesh(roadRoot,road,roadMaterial);
            foreach(var pocket in pockets)PocketMesh(roadRoot,pocket);
            RockBands(blockerRoot,Material("Corridor purple grey rock",new Color(.57f,.48f,.65f)));
            var landmarks=Node(root,"Corridor landmarks");
            data.bossArea=Marker(landmarks,"Left Boss Raider arena",new Vector2(-34,30));
            data.monsterArea=Marker(landmarks,"Far right Monster arena",new Vector2(39,43));
            data.lockedApproach=Marker(landmarks,"North turnaround and gate approach",new Vector2(2,56));
            data.lockedArena=Marker(landmarks,"Visibly locked future arena",new Vector2(2,65));
            data.lockedGate=PlaceGate(landmarks);data.gateColliders=data.lockedGate.GetComponentsInChildren<Collider>();data.hasRuntimeUnlockCondition=false;
            AddSign(landmarks,"REPAIR  >",new Vector2(2,-29));AddSign(landmarks,"<  BOSS",new Vector2(-1,16));AddSign(landmarks,"MONSTER  >",new Vector2(2,34));AddSign(landmarks,"LOCKED",new Vector2(2,57.2f));
            data.mainRoad=roads[0].points.Select(p=>At(p)).ToArray();data.bossRoad=roads[1].points.Select(p=>At(p)).ToArray();data.monsterRoad=roads[2].points.Select(p=>At(p)).ToArray();data.workshopRoad=roads[3].points.Select(p=>At(p)).ToArray();
            data.startHealthPoints=new[]{At(new Vector2(-1.4f,-46)*2.5f),At(new Vector2(0,-45.5f)*2.5f),At(new Vector2(1.4f,-47.5f)*2.5f)};
            data.mapFootprint=footprint.ToArray();data.authoredBounds=new Bounds(data.mapFootprint[0],Vector3.zero);foreach(var point in data.mapFootprint)data.authoredBounds.Encapsulate(point);
            data.blockers=blockerRoot.GetComponentsInChildren<Collider>();data.preservedLegacyProps=preserved.Where(t=>t).Distinct().ToArray();
            layout.navigationNeedsBake=true;RoadGunnerLevelAuthoring.Bake(layout);
            RoadGunnerLevelAuthoring.PlacePlayer(layout,At(new Vector2(0,-48)*2.5f),45);s.vehicle.transform.SetPositionAndRotation(layout.playerSpawn.position,layout.playerSpawn.rotation);Dirty(s.vehicle.transform);
            var humans=s.spawns.authoredRaiders.Where(r=>r&&!r.arenaBoss).ToArray();if(humans.Length!=12)throw new InvalidOperationException("Preserve the existing twelve Raiders; found "+humans.Length);
            for(int i=0;i<humans.Length;i++){Move(humans[i].transform,RaiderPoints[i],45+i*31);humans[i].name="Corridor · Raider "+(i+1).ToString("00");Dirty(humans[i]);}
            var vehicles=s.spawns.authoredBuggies.Where(b=>b).ToArray();if(vehicles.Length!=2)throw new InvalidOperationException("Preserve the existing two Buggies.");
            Move(vehicles[0].transform,new Vector2(2,10),225,.65f);Move(vehicles[1].transform,new Vector2(25,38),225,.65f);
            var monster=s.zone1.bosses.OfType<WastelandMutant>().Single();Move(monster.transform,new Vector2(39,43),225);if(monster.spawnPoint){monster.spawnPoint.SetPositionAndRotation(monster.transform.position,monster.transform.rotation);Dirty(monster.spawnPoint);}Dirty(monster);
            var boss=s.zone1.bosses.OfType<Raider>().Single(r=>r.arenaBoss);Move(boss.transform,new Vector2(-34,30),135);if(boss.spawnPoint){boss.spawnPoint.SetPositionAndRotation(boss.transform.position,boss.transform.rotation);Dirty(boss.spawnPoint);}Dirty(boss);
            var attack=s.runStats.stations.FirstOrDefault(station=>station&&station.kind==ArenaUpgradeKind.Attack);if(attack){Move(attack.transform,new Vector2(-14,20),45);Dirty(attack);}
            var recovery=layout.recoveryPoints.FirstOrDefault(t=>t&&t!=layout.playerSpawn);if(recovery){recovery.SetPositionAndRotation(workshop.transform.position+Vector3.up*.7f,Axis);Dirty(recovery);}
            RoadGunnerLevelAuthoring.Wire(layout);Dirty(data);Dirty(layout);AssetDatabase.SaveAssets();
            File.WriteAllLines(ReportPath,new[]{"Existing Arena 1 reauthored with Road Gunner Level Editor, terrain/art/enemy systems retained.","Spawn="+layout.playerSpawn.position,"Three Health +1 sites="+string.Join("; ",data.startHealthPoints.Select(p=>p.ToString())),"Main: 50m straight start, winding north14m road. Branches16m; boss48m clearing; Monster40m clearing.","Workshop="+workshop.transform.position,"Boss="+boss.transform.position,"Monster="+monster.transform.position,"North closed gate="+data.lockedGate.position+"; no pre-existing runtime unlock condition found.","Primitive rock-band colliders="+data.blockers.Length+"; preserved/relocated legacy props="+data.preservedLegacyProps.Length,"Saved navigation rebaked; scene saving remains the caller's responsibility."});
            return data;
        }
        static void Move(Transform target,Vector2 point,float yaw,float height=0) {
            var desired=At(point*2.5f);if(!NavMesh.SamplePosition(desired,out var hit,2,NavMesh.AllAreas))throw new InvalidOperationException("No corridor navigation at "+point+" for "+target.name);
            target.SetPositionAndRotation(hit.position+Vector3.up*height,Quaternion.Euler(0,yaw,0));Dirty(target);
        }
        static Transform Marker(Transform parent,string name,Vector2 point) {var t=Node(parent,name);t.position=At(point*2.5f);return t;}
        static Transform PlaceGate(Transform parent) {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Levels/WastelandRestored/Prefabs/Closed scrap gate.prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name="Corridor north CLOSED arena gate";go.transform.SetPositionAndRotation(At(new Vector2(2,59)*2.5f),Axis);go.transform.localScale=Vector3.one*.85f;
            GameplayObstacleAuthoring.Configure(go,false);Dirty(go.transform);return go.transform;
        }
        static void AddSign(Transform parent,string text,Vector2 point) {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Levels/WastelandRestored/Prefabs/Route sign.prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name="Corridor route sign · "+text;go.transform.SetPositionAndRotation(At(point*2.5f)+Axis*Vector3.right*5.5f,Axis);
            ConfigureSign(go,text);Dirty(go.transform);
        }
        /// <summary>Refreshes only the four authored signs, without regenerating routes or navigation.</summary>
        public static void ConfigureSigns(LevelLayout layout) {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before sign authoring.");
            var landmarks=layout.authoredContentRoot.Find("Corridor Arena 1/Corridor landmarks");
            if(!landmarks)throw new InvalidOperationException("Configure the existing corridor first.");
            const string prefix="Corridor route sign · ";
            foreach(Transform child in landmarks)if(child.name.StartsWith(prefix,StringComparison.Ordinal))
                ConfigureSign(child.gameObject,child.name.Substring(prefix.Length));
        }
        static void ConfigureSign(GameObject go,string destination) {
            // Historical text/arrow transforms have no renderers: ConceptEnvironment merged the
            // visible right arrow into Finished shared visuals. Cover that old arrow explicitly.
            var legacy=go.transform.Find("NORTH  /  DEPOT");if(legacy){legacy.gameObject.SetActive(false);Dirty(legacy.gameObject);}
            var cover=go.transform.Find("Corridor readable sign face");
            if(!cover){var face=GameObject.CreatePrimitive(PrimitiveType.Cube);face.name="Corridor readable sign face";Object.DestroyImmediate(face.GetComponent<Collider>());cover=face.transform;cover.SetParent(go.transform,false);}
            cover.localPosition=new Vector3(0,2.4f,-.16f);cover.localRotation=Quaternion.identity;cover.localScale=new Vector3(2.48f,.69f,.045f);
            var renderer=cover.GetComponent<Renderer>();renderer.sharedMaterial=Material("Corridor sign face",new Color(.12f,.15f,.17f));renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;Dirty(cover);
            var label=go.transform.Find("Corridor destination label");
            if(!label){var node=new GameObject("Corridor destination label",typeof(TextMesh));label=node.transform;label.SetParent(go.transform,false);}
            label.localPosition=new Vector3(0,3.22f,-.16f);label.rotation=Quaternion.Euler(57,45,0);label.localScale=Vector3.one;
            var text=label.GetComponent<TextMesh>();text.text=destination;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=48;text.fontStyle=FontStyle.Bold;text.characterSize=.10f;text.color=new Color(.99f,.97f,.86f);
            var textRenderer=text.GetComponent<MeshRenderer>();textRenderer.sharedMaterial=text.font.material;textRenderer.shadowCastingMode=ShadowCastingMode.Off;textRenderer.receiveShadows=false;
            var backing=label.Find("Destination contrast backing");
            if(!backing){var face=GameObject.CreatePrimitive(PrimitiveType.Cube);face.name="Destination contrast backing";Object.DestroyImmediate(face.GetComponent<Collider>());backing=face.transform;backing.SetParent(label,false);}
            backing.localPosition=new Vector3(0,0,.09f);backing.localRotation=Quaternion.identity;backing.localScale=new Vector3(Mathf.Max(2.2f,destination.Length*.34f),.72f,.05f);
            var backingRenderer=backing.GetComponent<Renderer>();backingRenderer.sharedMaterial=renderer.sharedMaterial;backingRenderer.shadowCastingMode=ShadowCastingMode.Off;backingRenderer.receiveShadows=false;Dirty(backing);
            Dirty(text);Dirty(textRenderer);Dirty(label);
            // Destination includes only explicit ASCII arrows: < BOSS, REPAIR >, MONSTER >.
            // LOCKED deliberately has no traversable-forward arrow.
        }
        static void ClearLegacyLanes(LevelLayout layout,Transform newRoot,Transform workshop) {
            Physics.SyncTransforms();var candidates=new HashSet<Transform>();
            foreach(var collider in layout.authoredContentRoot.GetComponentsInChildren<Collider>(true)) {
                if(!collider.enabled||collider.isTrigger||collider is WheelCollider||collider.transform.IsChildOf(newRoot)||collider.transform.IsChildOf(workshop)||collider.GetComponentInParent<CombatTarget>()||collider.GetComponentInParent<ArenaUpgradeStation>()||collider.GetComponentInParent<VehicleController>())continue;
                if(collider==ground||!collider.gameObject.activeInHierarchy)continue;
                var root=PrefabUtility.GetNearestPrefabInstanceRoot(collider.gameObject);var candidate=root?root.transform:collider.transform;
                if(candidate==layout.authoredContentRoot||candidate==layout.obstaclesRoot||candidate==layout.decorationRoot)continue;
                candidates.Add(candidate);
            }
            foreach(var candidate in candidates) {
                if(!candidate||!candidate.gameObject.activeInHierarchy)continue;
                var colliders=candidate.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger&&!(c is WheelCollider)).ToArray();if(colliders.Length==0)continue;
                Bounds bounds=colliders[0].bounds;foreach(var c in colliders)bounds.Encapsulate(c.bounds);
                var center=Local(bounds.center);float radius=new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
                bool intrudes=Free(center,radius+1.5f);if(!intrudes)continue;
                bool moved=false;
                // Keep compound art/collision together; never delete a source instance or its palette link.
                for(float r=8;r<=64&&!moved;r+=8)for(int j=0;j<24;j++) {
                    var p=center+new Vector2(Mathf.Cos(j*Mathf.PI/12),Mathf.Sin(j*Mathf.PI/12))*r;
                    if(p.x< -165||p.x>125||p.y< -155||p.y>190||Free(p,radius+3))continue;
                    var offset=Axis*new Vector3(p.x-center.x,0,p.y-center.y);candidate.position+=offset;Dirty(candidate);moved=true;break;
                }
                if(!moved){candidate.gameObject.SetActive(false);Dirty(candidate.gameObject);}preserved.Add(candidate);
            }
        }
        static void RockBands(Transform parent,Material material) {
            const float cell=2;const int nx=156,nz=186;const float x0=-176,z0=-168;
            var solid=new bool[nx,nz];for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)solid[x,z]=!Free(new Vector2(x0+(x+.5f)*cell,z0+(z+.5f)*cell),cell*.72f);
            var used=new bool[nx,nz];var rectangles=new List<CellRect>();
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++) {
                if(!solid[x,z]||used[x,z])continue;int width=1;while(x+width<nx&&solid[x+width,z]&&!used[x+width,z])width++;
                int depth=1;bool extend=true;while(z+depth<nz&&extend){for(int i=0;i<width;i++)if(!solid[x+i,z+depth]||used[x+i,z+depth]){extend=false;break;}if(extend)depth++;}
                for(int j=0;j<depth;j++)for(int i=0;i<width;i++)used[x+i,z+j]=true;rectangles.Add(new CellRect{x=x,z=z,width=width,depth=depth});
            }
            var vertices=new List<Vector3>();var triangles=new List<int>();int count=0;
            foreach(var rect in rectangles) {
                float xmin=x0+rect.x*cell,xmax=xmin+rect.width*cell,zmin=z0+rect.z*cell,zmax=zmin+rect.depth*cell;
                float height=4.2f+(count%5)*.35f;var groundPoint=At(new Vector2((xmin+xmax)*.5f,(zmin+zmax)*.5f));
                var node=Node(parent,"Solid rock band "+(++count));node.SetPositionAndRotation(groundPoint+Vector3.up*(height*.5f-.5f),Axis);
                var box=node.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(xmax-xmin+.08f,height+1,zmax-zmin+.08f);box.isTrigger=false;node.gameObject.layer=0;node.gameObject.isStatic=true;
                Vector3[] corners={new Vector3(xmin,-1,zmin),new Vector3(xmax,-1,zmin),new Vector3(xmax,-1,zmax),new Vector3(xmin,-1,zmax),new Vector3(xmin,height,zmin),new Vector3(xmax,height,zmin),new Vector3(xmax,height,zmax),new Vector3(xmin,height,zmax)};
                int[] faces={4,7,6,4,6,5,0,4,5,0,5,1,1,5,6,1,6,2,2,6,7,2,7,3,3,7,4,3,4,0};
                for(int i=0;i<faces.Length;i++){vertices.Add(Axis*corners[faces[i]]+Vector3.up*groundPoint.y);triangles.Add(vertices.Count-1);}
            }
            // Reuse the existing sculpted Boulder mesh along the visible rock faces. One combined
            // renderer softens the grid contour without adding individual prop updates/draw calls.
            var boulder=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/RoadGunner/Art/ConceptWasteland/Meshes/Env_Boulder_Palette.asset");
            if(boulder) {
                var source=boulder.vertices;var indices=boulder.triangles;int edge=0;
                for(int z=1;z<nz-1;z++)for(int x=1;x<nx-1;x++) {
                    if(!solid[x,z]||solid[x-1,z]&&solid[x+1,z]&&solid[x,z-1]&&solid[x,z+1]||++edge%4!=0)continue;
                    var center=new Vector2(x0+(x+.5f)*cell,z0+(z+.5f)*cell);
                    var rotation=Axis*Quaternion.Euler(0,(x*37+z*17)%360,0);float scale=1.25f+((x+z)%4)*.12f;
                    var position=At(center)-Vector3.up*.12f;
                    foreach(var index in indices){vertices.Add(position+rotation*(source[index]*scale));triangles.Add(vertices.Count-1);}
                }
            }
            Render(parent,"Corridor rock mass landscape",Surface("Corridor rock mass landscape",vertices.ToArray(),triangles.ToArray()),material);
        }
    }
}
