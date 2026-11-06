using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Explicit full environment replacement; never runs a historical world/art/balance installer.</summary>
    public static class ReferenceArenaInstaller
    {
        public const string AssetRoot="Assets/RoadGunner/Levels/Greenwood/ReferenceArena";
        public const string LayoutAssetPath=AssetRoot+"/Reference layout.asset";
        public const string ReportFolder="Documentation/RoadGunner/ReferenceArena-2026-11-06";
        public const string SceneRootName="Reference arena";
        static readonly Quaternion Axis=Quaternion.Euler(0,45,0);
        static readonly List<string> log=new List<string>();
        static ReferenceArenaLayoutData data;
        static ExplorationArenaLayoutData geometry;
        static LevelLayout level;
        static Transform world,roads;
        static ReferenceArenaScenery art;
        static ArcadeVisualGround.Lane[] lanes;
        static Font font;
        static int solidSegments,carSamples,navTargets;
        [Serializable] sealed class Manifest
        {
            public int revision,roads,pockets,rewards,softProps,hardProps,carClearanceSamples,navTargets,closedGates;
            public float widthScale;
            public string verifiedAtUtc,source,scope;
            public Vector2 minW,maxW;
            public ReferenceArenaScenery.Record[] scenery;
        }
        public static Vector3 World(Vector2 w,float height=0)=>ExplorationArenaInstaller.World(w,height);
        public static Vector2 ToW(Vector3 p)=>ExplorationArenaInstaller.ToW(p);
        static Transform Node(Transform p,string name)
        {var go=new GameObject(name);go.transform.SetParent(p,false);return go.transform;}
        static void Dirty(Object o){if(o)EditorUtility.SetDirty(o);}
        public static Mesh SaveMesh(string path,Mesh mesh)
        {
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!saved){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);Dirty(saved);return saved;
        }

        [MenuItem("Tools/Road Gunner/Arena 1/Install Reference Crossroads Arena")]
        public static void InstallBatch()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before replacing environment.");
            if(SceneManager.GetActiveScene().path!=ExplorationArenaInstaller.ScenePath)EditorSceneManager.OpenScene(ExplorationArenaInstaller.ScenePath);
            Install();
        }
        public static void Install()
        {
            if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=ExplorationArenaInstaller.ScenePath)
                throw new InvalidOperationException("Open the saved Greenwood scene in Edit Mode.");
            log.Clear();solidSegments=carSamples=navTargets=0;
            level=RoadGunnerLevelAuthoring.Current;
            if(!level||!level.session||!level.ground||!level.session.exploration)throw new InvalidOperationException("The existing authored arena/session is required.");
            var session=level.session;
            string tuning=PreservedTuning(session);
            var vehicle=session.vehicle;var hud=session.hud;var input=session.input;var turret=session.turret;
            Directory.CreateDirectory(AssetRoot);Directory.CreateDirectory(AssetRoot+"/Models");Directory.CreateDirectory(ReportFolder);AssetDatabase.Refresh();
            data=AssetDatabase.LoadAssetAtPath<ReferenceArenaLayoutData>(LayoutAssetPath);
            if(!data){data=ScriptableObject.CreateInstance<ReferenceArenaLayoutData>();AssetDatabase.CreateAsset(data,LayoutAssetPath);}
            if(!data.geometry)data.geometry=AssetDatabase.LoadAssetAtPath<ExplorationArenaLayoutData>(ExplorationArenaInstaller.LayoutAssetPath);
            if(!data.geometry)throw new InvalidOperationException("The common editable geometry asset is missing.");
            if(data.revision<ReferenceArenaLayoutData.CurrentRevision)data.InitializeGeometry();
            geometry=data.geometry;ValidateDefinition();lanes=ArcadeVisualGround.Lanes(geometry);
            // Preserve actual gameplay objects before removing any historical environment root.
            ReferenceArenaGameplayAuthoring.Preserve(level);
            var crush=RoadGunnerLevelAuthoring.InScene<ArenaDecorationCrush>().Single();
            // The previous manager lived on the generated collision root. Preserve its real
            // configuration while replacing that root's obsolete probes and hard proxies.
            crush.transform.SetParent(level.transform,true);
            foreach(var child in crush.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            crush.chunks=Array.Empty<DecorationCrushChunk>();
            RemoveEnvironment();
            geometry.baselineMoves.Clear();geometry.baselineSurfaces.Clear();
            world=Node(level.authoredContentRoot,SceneRootName);
            roads=Node(level.authoredContentRoot,ExplorationArenaInstaller.SceneRootName);
            font=session.hud&&session.hud.statsText?session.hud.statsText.font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildTerrain();
            art=new ReferenceArenaScenery(AssetRoot+"/Models");
            BuildRoadSurface();BuildMarkers();BuildPerimeterAndGates();BuildLandmarks();
            ReferenceArenaGameplayAuthoring.Apply(level,data);
            BuildRewardsAndRegions();Physics.SyncTransforms();BuildScenery();
            art.Bind(crush,session);
            ConfigureBounds();Physics.SyncTransforms();
            ValidateChassisRoutes();ValidateGateSeals();
            level.navigationNeedsBake=true;RoadGunnerLevelAuthoring.Bake(level);ValidateNavigation();
            ArcadeVisualGround.Apply(session,log);
            var smallScenery=world.Find("Vehicle-crush scenery");
            try{smallScenery.gameObject.SetActive(false);ArcadeVisualGround.BakeMinimap(session,log,transparentOutside:true);}
            finally{smallScenery.gameObject.SetActive(true);}
            if(vehicle!=session.vehicle||hud!=session.hud||input!=session.input||turret!=session.turret||PreservedTuning(session)!=tuning)
                throw new InvalidOperationException("Environment authoring changed a preserved player/UI/tuning reference.");
            level.referenceUrl="https://www.youtube.com/watch?v=i2-6e8ymC_8";level.referenceInspected=true;
            level.requestedFootprintScale=1.5f;level.appliedFootprintScale=1.5f;
            level.authoringNotes="June2026 observed reference connectivity adapted to our tracked vehicle/wasteland. Normalized width233.333m to350m (x1.5); unknown reference metres are not inferred. Four-arm crossroads, SErepair, SWguardpen, NEcamp/optionalMonster, NWfinalWarlord; three closed gates. Existing combat/player tuning retained.";
            Dirty(data);Dirty(geometry);Dirty(session.exploration);Dirty(session);Dirty(level);
            RoadGunnerLevelAuthoring.Save(level,false);
            WriteManifest();Debug.Log(string.Join("\n",log));
        }

        static string PreservedTuning(GameSession s)=>JsonUtility.ToJson(s.vehicle.tuning)+"|"+JsonUtility.ToJson(s.turret.tuning)+"|"+JsonUtility.ToJson(s.health.tuning);
        static void ValidateDefinition()
        {
            if(data.raidersW==null||data.raidersW.Length!=12||data.buggiesW==null||data.buggiesW.Length!=2||data.gatesW==null||data.gatesW.Length!=3)
                throw new InvalidOperationException("Reference roster requires12Raiders,2Buggies,and3gates.");
            if(geometry.routes==null||geometry.pockets==null||geometry.regions==null||geometry.regions.Length!=4||geometry.caches==null)
                throw new InvalidOperationException("The reference geometry definition is incomplete.");
            if(geometry.routes.Select(r=>r.id).Distinct().Count()!=geometry.routes.Length)throw new InvalidOperationException("Duplicate road IDs.");
        }
        static bool GameplayInside(Transform t)=>t.GetComponentInChildren<VehicleController>(true)||t.GetComponentInChildren<RepairWorkshop>(true)||t.GetComponentInChildren<ArenaUpgradeStation>(true)||
            t.GetComponentsInChildren<CombatTarget>(true).Any(c=>!(c is ArenaRewardCache));
        static void RemoveEnvironment()
        {
            foreach(var root in new[]{level.obstaclesRoot,level.decorationRoot})
            {
                if(!root)throw new InvalidOperationException("Missing environment owner root.");
                foreach(var child in root.Cast<Transform>().ToArray())
                {
                    if(GameplayInside(child))throw new InvalidOperationException("Gameplay object still resides in replaceable environment: "+child.name);
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            foreach(string name in new[]{SceneRootName,ExplorationArenaInstaller.SceneRootName})
            {
                var old=level.authoredContentRoot.Find(name);
                if(old){if(GameplayInside(old))throw new InvalidOperationException("Preserve gameplay before replacing "+name);Object.DestroyImmediate(old.gameObject);}
            }
            log.Add("Replaced only authored environment/old decoration/proxies; historical SceneryCrush catalog/assets retained as research history.");
        }
        static void BuildTerrain()
        {
            Vector2 min=geometry.minW-Vector2.one*3,max=geometry.maxW+Vector2.one*3;
            int nx=Mathf.CeilToInt((max.x-min.x)*2.5f/4),nz=Mathf.CeilToInt((max.y-min.y)*2.5f/4);
            var vertices=new Vector3[(nx+1)*(nz+1)];var uv=new Vector2[vertices.Length];var triangles=new List<int>();
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
            {int index=z*(nx+1)+x;vertices[index]=World(new Vector2(Mathf.Lerp(min.x,max.x,x/(float)nx),Mathf.Lerp(min.y,max.y,z/(float)nz)));uv[index]=new Vector2(vertices[index].x/512+.5f,vertices[index].z/512+.5f);}
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){int a=z*(nx+1)+x,b=a+nx+1;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
            var mesh=new Mesh {name="Reference arena flat drivable ground",indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.uv=uv;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(AssetRoot+"/Terrain.asset",mesh);
            level.ground.sharedMesh=mesh;level.ground.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);level.ground.transform.localScale=Vector3.one;
            var collider=level.ground.GetComponent<MeshCollider>();if(!collider)collider=level.ground.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            collider.enabled=true;collider.isTrigger=false;level.ground.gameObject.layer=0;
            level.ground.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(ArcadeVisualGround.GroundMaterialPath);
            var baseline=Object.Instantiate(mesh);baseline.name="Reference terrain baseline";level.baselineTerrain=SaveMesh(AssetRoot+"/Terrain baseline.asset",baseline);
            Dirty(level.ground);Dirty(collider);log.Add("New ground:350m×310m design bounds, yaw45; flat true collision, finite ground margin7.5m.");
        }
        static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {var d=b-a;float t=d.sqrMagnitude<.00001f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t)*2.5f;}
        static float RoadDistance(Vector2 w)
        {
            float result=float.PositiveInfinity;
            foreach(var lane in lanes)for(int i=1;i<lane.points.Length;i++)result=Mathf.Min(result,SegmentDistance(w,lane.points[i-1],lane.points[i])-lane.route.widthMeters*.5f);
            return result;
        }
        static float ClearingDistance(Vector2 w)
        {
            float d=RoadDistance(w);foreach(var pocket in geometry.pockets)d=Mathf.Min(d,Vector2.Distance(w,pocket.centerW)*2.5f-pocket.radiusMeters);return d;
        }
        static void BuildRoadSurface()
        {
            const float step=.8f;int nx=Mathf.CeilToInt((geometry.maxW.x-geometry.minW.x)/step),nz=Mathf.CeilToInt((geometry.maxW.y-geometry.minW.y)/step);
            var verts=new List<Vector3>();var triangles=new List<int>();var lookup=new Dictionary<Vector2Int,int>();
            Func<int,int,int> vertex=(x,z)=>{var key=new Vector2Int(x,z);if(lookup.TryGetValue(key,out var found))return found;int at=verts.Count;verts.Add(World(geometry.minW+new Vector2(x*step,z*step),.035f));lookup.Add(key,at);return at;};
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {var w=geometry.minW+new Vector2((x+.5f)*step,(z+.5f)*step);if(ClearingDistance(w)>1.5f)continue;int a=vertex(x,z),b=vertex(x,z+1),c=vertex(x+1,z),d=vertex(x+1,z+1);triangles.AddRange(new[]{a,b,c,c,b,d});}
            var mesh=new Mesh {name="Road Reference connected roads and pockets",indexFormat=IndexFormat.UInt32};mesh.SetVertices(verts);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=SaveMesh(AssetRoot+"/Physical roads.asset",mesh);
            var node=Node(roads,"Physical connected roads and combat pockets");node.gameObject.isStatic=true;node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(ArcadeVisualGround.GroundMaterialPath);node.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            log.Add("Road source: "+geometry.routes.Length+" routes / "+geometry.pockets.Length+" pockets, common indexed surface; physical support path retained for enemy respawns.");
        }
        static void BuildMarkers()
        {
            var root=Node(roads,"Routes");foreach(var lane in lanes){var route=Node(root,lane.route.id);for(int i=0;i<lane.points.Length;i++)Node(route,i.ToString("000")).position=World(lane.points[i],.06f);}
            root=Node(roads,"Combat pockets");foreach(var p in geometry.pockets)Node(root,p.id).position=World(p.centerW,.06f);
        }

        static Material Material(string key)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/RoadGunner/Art/ConceptWasteland/Materials/"+key+".mat");
            if(!material)throw new InvalidOperationException("Missing finished Arcade material: "+key);return material;
        }
        static GameObject Box(Transform parent,string name,Vector3 local,Vector3 size,string material,bool collision)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=local;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=Material(material);
            if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        static void SolidSegment(Transform parent,string id,Vector2 a,Vector2 b,float height=5,float width=3)
        {
            var at=Node(parent,id);at.position=World((a+b)*.5f);var delta=World(b)-World(a);at.rotation=Quaternion.LookRotation(delta);
            var go=Box(at,"Continuous physical foundation",new Vector3(0,height*.5f,0),new Vector3(width,height,delta.magnitude+.5f),"Rock",true);
            go.isStatic=true;solidSegments++;
        }
        static void RockLine(Transform parent,string id,Vector2 a,Vector2 b,float height=7)
        {
            float length=Vector2.Distance(a,b)*2.5f;int count=Mathf.Max(1,Mathf.CeilToInt(length/10));
            for(int i=0;i<=count;i++)
            {var w=Vector2.Lerp(a,b,i/(float)count);art.Place(parent,id+"/rock-"+i,"Boulder",w,new Vector3(13+(i%3),height+(i%3)*.65f,8+(i%2)*2),i*47,false);}
        }
        static void BuildPerimeterAndGates()
        {
            var walls=Node(world,"Continuous rock perimeter");
            Vector2 a=new Vector2(-70,-58),b=new Vector2(70,-58),c=new Vector2(70,66),d=new Vector2(-70,66);
            SolidSegment(walls,"Outer south",a,b);SolidSegment(walls,"Outer east",b,c);SolidSegment(walls,"Outer north",c,d);SolidSegment(walls,"Outer west",d,a);
            RockLine(walls,"south",a,b,6);
            // Inner gate fronts join the outer wall. No driveable route exists around their ends.
            var root=Node(world,"Closed gates");string[] names={"West","East","North"};
            for(int i=0;i<3;i++)
            {
                Vector2 p=data.gatesW[i];Vector2 outward=(p-data.crossW).normalized;
                var gate=Node(root,"Gate "+names[i]);gate.position=World(p);gate.rotation=Quaternion.LookRotation(World(p+outward)-World(p));
                float span=i<2?31:34;
                art.Place(gate,"Gate "+names[i]+" finished model","Closed scrap gate",p,new Vector3(span,6.2f,3),gate.rotation.eulerAngles.y-45,false);
                var panel=Box(gate,"Unbroken closed gate panel",new Vector3(0,2.8f,0),new Vector3(span,5.6f,1.1f),"Steel",true);panel.isStatic=true;
                // The physical panel is backed by the finished sculpted gate. Its closed state is immutable.
                Node(gate,"Inside approach").position=World(p-outward*7,.06f);Node(gate,"Outside preview").position=World(p+outward*4,.06f);
                Vector2 side=new Vector2(-outward.y,outward.x);Vector2 left=p-side*(span*.5f/2.5f),right=p+side*(span*.5f/2.5f);
                Vector2 leftEnd,rightEnd;
                if(i<2){leftEnd=new Vector2(p.x,side.y<0?66:-58);rightEnd=new Vector2(p.x,side.y<0?-58:66);}
                else{leftEnd=new Vector2(side.x<0?70:-70,p.y);rightEnd=new Vector2(side.x<0?-70:70,p.y);}
                SolidSegment(gate,"Left solid flank",left,leftEnd,6,4);SolidSegment(gate,"Right solid flank",right,rightEnd,6,4);
                RockLine(walls,"gate-"+names[i]+"-left",left,leftEnd);RockLine(walls,"gate-"+names[i]+"-right",right,rightEnd);
                Label(gate,"Closed landmark name","SEALED ROUTE",new Vector3(0,7,0),.16f,new Color(1,.85f,.4f));
            }
            log.Add("Continuous perimeter +3 closed gate assemblies; solid flanks connect every gate to outer walls, with no drive-around opening.");
        }
        static TextMesh Label(Transform parent,string name,string text,Vector3 local,float size,Color tint)
        {
            var node=Node(parent,name);node.localPosition=local;node.rotation=level.session.input.view?level.session.input.view.transform.rotation:Quaternion.Euler(57,45,0);
            var label=node.gameObject.AddComponent<TextMesh>();label.font=font;label.text=text;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.fontSize=64;label.characterSize=size;label.color=tint;
            var renderer=node.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return label;
        }
        static void BuildLandmarks()
        {
            var root=Node(world,"Finished wasteland landmarks");
            art.Place(root,"Bunker landmark","Sealed industrial bunker",data.bunkerW+new Vector2(0,-8),new Vector3(20,6,13),0);
            art.Place(root,"SWoutpost lookout","Rusted lookout",new Vector2(-42,-10),new Vector3(7,6,7),10);
            art.Place(root,"SWoutpost shelter","Shipping container",new Vector2(-44,-28),new Vector3(5,3.5f,11),-8);
            art.Place(root,"SWoutpost salvage","Abandoned pickup",new Vector2(-38,-33),new Vector3(4,2.5f,7),70);
            art.Place(root,"NWguard gantry","Salvage gantry",new Vector2(-49,30),new Vector3(10,7,7),0);
            art.Place(root,"NWguard wreck","Bus wreck",new Vector2(-46,35),new Vector3(4,3.5f,11),65);
            art.Place(root,"NEcamp lookout","Rusted lookout",new Vector2(16,40),new Vector3(7,6,7),-8);
            art.Place(root,"NEcamp windpump","Scrap wind pump",new Vector2(31,48),new Vector3(6,8,6),0);
            art.Place(root,"NEcamp shelter","Shipping container",new Vector2(43,20),new Vector3(5,3.5f,11),12);
            art.Place(root,"NEcamp workshop wreck","Abandoned pickup",new Vector2(21,40),new Vector3(4,2.5f,7),75);
            // Broken camp edges define pockets without forcing narrow vehicle tunnels.
            FenceLine(root,"SWpen west",new Vector2(-46,-35),new Vector2(-46,-26));
            FenceLine(root,"SWpen south",new Vector2(-46,-35),new Vector2(-22,-35));
            FenceLine(root,"SWpen north",new Vector2(-46,-13),new Vector2(-37,-13));
            FenceLine(root,"SWpen east lower",new Vector2(-22,-35),new Vector2(-22,-29));
            FenceLine(root,"NEcamp west lower",new Vector2(13,17),new Vector2(13,23));
            FenceLine(root,"NEcamp west upper",new Vector2(13,37),new Vector2(13,47));
            FenceLine(root,"NEcamp south right",new Vector2(39,14),new Vector2(48,14));
            FenceLine(root,"NEcamp north",new Vector2(25,50),new Vector2(38,50));
            var j=Node(root,"Crossroads route signs");j.position=World(new Vector2(-9,16));j.rotation=Axis;
            Box(j,"Steel sign post",new Vector3(0,1.4f,0),new Vector3(.3f,2.8f,.3f),"Steel",true);
            Box(j,"Wide ivory route board",new Vector3(0,2.5f,0),new Vector3(5,1.1f,.25f),"Ivory",false);
            Label(j,"Readable repair direction","REPAIR  ↘",new Vector3(0,3.5f,0),.14f,new Color(.4f,1,.7f));
        }
        static void FenceLine(Transform root,string id,Vector2 a,Vector2 b)
        {
            float length=Vector2.Distance(a,b)*2.5f;int n=Mathf.Max(1,Mathf.FloorToInt(length/6));var delta=World(b)-World(a);float yaw=Quaternion.LookRotation(delta).eulerAngles.y-45+90;
            for(int i=0;i<n;i++)art.Place(root,id+"/"+i,"Corrugated fence",Vector2.Lerp(a,b,(i+.5f)/n),new Vector3(length/n+.12f,2.5f,.5f),yaw);
        }

        // Rewards use the existing gameplay cache API; only the surrounding level changes.
        static void BuildRewardsAndRegions()
        {
            var session=level.session;var exploration=session.exploration;exploration.session=session;
            exploration.zones=geometry.regions.Select(r=>new ArenaExplorationZone {id=r.id,displayName=r.displayName,hint=r.hint,center=World(r.centerW),radius=r.radiusMeters}).ToArray();
            var root=Node(roads,"Rewards");var caches=new List<ArenaRewardCache>();
            foreach(var spec in geometry.caches)
            {
                var at=Node(root,spec.id);at.position=World(spec.positionW,.04f);at.rotation=Axis;at.gameObject.layer=9;
                var cache=at.gameObject.AddComponent<ArenaRewardCache>();cache.id=spec.id;cache.session=session;cache.exploration=exploration;cache.reward=spec.reward;
                cache.amount=spec.overrideTuningAmount?spec.amount:exploration.tuning.DefaultAmount(spec.reward);cache.secret=spec.secret;cache.proximityOnly=spec.secret;cache.repairAmount=spec.repairAmount;cache.durability=spec.durability;cache.radius=exploration.tuning.collectionRadius;
                if(!spec.secret){var box=at.gameObject.AddComponent<BoxCollider>();box.center=new Vector3(0,.65f,0);box.size=new Vector3(1.9f,1.3f,1.5f);}
                var visible=Node(at,"Available reward");cache.availableVisual=visible.gameObject;
                var template=art.Model("Wooden supply crate");var model=Node(visible,"Finished supply crate");var size=template.mesh.bounds.size;model.localScale=new Vector3(1.85f/size.x,1.3f/size.y,1.5f/size.z);model.localPosition=-Vector3.up*template.mesh.bounds.min.y*model.localScale.y;
                model.gameObject.AddComponent<MeshFilter>().sharedMesh=template.mesh;model.gameObject.AddComponent<MeshRenderer>().sharedMaterials=template.materials;
                string cap=spec.secret?"Teal":spec.reward==ArenaRewardKind.Health?"Mint":"Yellow";
                Box(visible,"Bright reward cap",new Vector3(0,1.32f,0),new Vector3(1.95f,.16f,1.58f),cap,false);
                var consumed=Node(at,"Consumed cache");cache.consumedVisual=consumed.gameObject;Box(consumed,"Empty crate base",new Vector3(0,.12f,0),new Vector3(1.8f,.24f,1.4f),"Wood",false);consumed.gameObject.SetActive(false);
                cache.displayText=Label(at,"Exact reward",cache.RewardLabel,new Vector3(0,3,0),.105f,spec.secret?new Color(.5f,1,1):new Color(1,.9f,.45f));
                if(!spec.secret){var bar=EnemyHealthBarAuthoring.ConfigureTarget(cache,session.input.view);bar.height=1.7f;bar.anchor.localPosition=Vector3.up*1.7f;Dirty(bar);}
                foreach(var t in at.GetComponentsInChildren<Transform>(true))t.gameObject.layer=9;
                caches.Add(cache);Dirty(cache);
            }
            exploration.caches=caches.ToArray();Dirty(exploration);
        }

        static void BuildScenery()
        {
            var root=Node(world,"Vehicle-crush scenery");var hardRoot=Node(world,"Solid scattered rocks");var random=new System.Random(20261106);int item=0;
            var actorW=data.raidersW.Concat(data.buggiesW).Concat(new[]{data.spawnW,data.workshopW,data.bossW,data.monsterW}).ToArray();
            for(float n=-51;n<53;n+=4.8f)for(float e=-57;e<58;e+=4.8f)
            {
                var w=new Vector2(e+(float)(random.NextDouble()-.5)*3,n+(float)(random.NextDouble()-.5)*3);
                float choice=(float)random.NextDouble(),yaw=(float)random.NextDouble()*360;
                if(RoadDistance(w)<3||actorW.Any(p=>Vector2.Distance(w,p)*2.5f<8)||geometry.caches.Any(c=>Vector2.Distance(w,c.positionW)*2.5f<7))continue;
                if(Physics.CheckSphere(World(w,2.5f),2.2f,1,QueryTriggerInteraction.Ignore))continue;
                bool hard=choice>.82f;float clear=ClearingDistance(w);
                if(hard&&clear<5)continue;
                string kind;Vector3 size;DecorationCrushKind crush;
                if(hard){kind="Boulder";size=new Vector3(4+(float)random.NextDouble()*3,2+(float)random.NextDouble()*2.5f,3.8f+(float)random.NextDouble()*3);crush=DecorationCrushKind.Stone;}
                else if(choice<.30f){kind="Cactus";float h=1.1f+(float)random.NextDouble()*1.4f;size=new Vector3(h*.7f,h,h*.7f);crush=DecorationCrushKind.Vegetation;}
                else if(choice<.49f){kind="Boulder";size=new Vector3(.65f,.35f+(float)random.NextDouble()*.2f,.7f);crush=DecorationCrushKind.Stone;}
                else if(choice<.62f){kind="Wooden supply crate";size=new Vector3(1.2f,1.1f,1.0f);crush=DecorationCrushKind.Wood;}
                else if(choice<.73f){kind="Fuel drums";size=new Vector3(2,1.5f,1.6f);crush=DecorationCrushKind.Metal;}
                else{kind="Tire stack";size=new Vector3(1.1f,.9f,1.1f);crush=DecorationCrushKind.Rubber;}
                art.Place(hard?hardRoot:root,"scenery-"+(item++).ToString("000"),kind,w,size,yaw,!hard,crush);
                // The next occupancy query must see the same newly authored colliders on every install.
                Physics.SyncTransforms();
            }
            log.Add("Finished reusable models placed deterministically; soft cacti/crates/drums/tires/smallstones use readable mutable meshes and layer12 triggers, large rocks remain exact hard meshes.");
        }
        static void ConfigureBounds()
        {
            var bounds=new Bounds(World(geometry.minW),Vector3.zero);bounds.Encapsulate(World(geometry.maxW));bounds.Encapsulate(World(new Vector2(geometry.minW.x,geometry.maxW.y)));bounds.Encapsulate(World(new Vector2(geometry.maxW.x,geometry.minW.y)));bounds.Expand(new Vector3(6,20,6));level.gameplayBounds=bounds;
        }
        static void ValidateChassisRoutes()
        {
            var car=level.session.vehicle;var failures=new List<string>();
            foreach(var lane in lanes)for(int i=0;i<lane.points.Length;i++)
            {
                Vector3 before=World(lane.points[Mathf.Max(0,i-1)]),after=World(lane.points[Mathf.Min(lane.points.Length-1,i+1)]);var rotation=Quaternion.LookRotation(after-before);
                var root=World(lane.points[i],.7f);
                foreach(var box in car.GetComponents<BoxCollider>())
                {
                    var half=Vector3.Scale(box.size*.5f,car.transform.lossyScale);half.x+=.2f;half.z+=.15f;
                    var center=root+rotation*Vector3.Scale(box.center,car.transform.lossyScale);
                    foreach(var hit in Physics.OverlapBox(center,half,rotation,1,QueryTriggerInteraction.Ignore))
                    {
                        if(hit.transform.IsChildOf(car.transform)||hit.transform==level.ground.transform||hit.GetComponentInParent<CombatTarget>())continue;
                        if(hit.bounds.max.y<=center.y-half.y+.04f)continue;
                        failures.Add(lane.route.id+"/"+i+" W="+lane.points[i]+" -> "+hit.name);
                    }
                }
                carSamples++;
            }
            if(failures.Count>0)throw new InvalidOperationException("Reference full-chassis route guard failed; scene not saved:\n"+string.Join("\n",failures.Distinct().Take(30)));
            log.Add("Full original chassis +0.20m lateral/+0.15m longitudinal margin: "+carSamples+" route samples clear.");
        }
        static void ValidateGateSeals()
        {
            foreach(var p in data.gatesW)
            {
                var outward=(p-data.crossW).normalized;var from=World(p-outward*4,1.2f);var direction=(World(p+outward)-World(p)).normalized;
                if(!Physics.Raycast(from,direction,out var hit,25,1,QueryTriggerInteraction.Ignore)||!hit.transform.IsChildOf(world.Find("Closed gates")))
                    throw new InvalidOperationException("Closed gate is not physically sealed at W="+p);
            }
            log.Add("Three closed gate centreline collision rays verified; continuous flank foundations are authored without gaps.");
        }
        static void ValidateNavigation()
        {
            if(!NavMesh.SamplePosition(World(data.spawnW),out var start,3,NavMesh.AllAreas))throw new InvalidOperationException("New start is outside navigation.");
            var destinations=data.raidersW.Concat(data.buggiesW).Concat(new[]{data.bossW,data.monsterW,data.workshopW}).Concat(geometry.caches.Select(c=>c.positionW));
            var path=new NavMeshPath();
            foreach(var w in destinations)
            {
                if(!NavMesh.SamplePosition(World(w),out var end,3,NavMesh.AllAreas)||!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("New reachable encounter/reward guard failed at W="+w);
                navTargets++;
            }
            log.Add("New navigation: all16combat encounters +repair +8rewards reachable from ordinary start ("+navTargets+" complete paths).");
        }
        static void WriteManifest()
        {
            var manifest=new Manifest {revision=data.revision,widthScale=data.widthScale,source="https://www.youtube.com/watch?v=i2-6e8ymC_8",verifiedAtUtc=DateTime.UtcNow.ToString("O"),
                scope="Explicit new vehicle-scale environment. Reference connectivity observed; metres/complete enemy statistics not inferred. Existing player/health/combat tuning retained. Historical scenery catalog not overwritten.",
                roads=geometry.routes.Length,pockets=geometry.pockets.Length,rewards=geometry.caches.Length,softProps=art.SoftCount,hardProps=art.records.Count(r=>r.handling=="hard"),closedGates=3,
                carClearanceSamples=carSamples,navTargets=navTargets,minW=geometry.minW,maxW=geometry.maxW,scenery=art.records.ToArray()};
            File.WriteAllText(ReportFolder+"/Authoring.json",JsonUtility.ToJson(manifest,true));
            File.WriteAllText(ReportFolder+"/Gameplay-preservation.json",ReferenceArenaGameplayAuthoring.LastConfigurationSnapshotJson);
            log.Add(ReferenceArenaGameplayAuthoring.LastReport);
            log.Insert(0,"REFERENCE_CROSSROADS_INSTALLED revision"+data.revision+";4mainarms;3closedgates;16existingcombat encounters;soft"+manifest.softProps+"/hard"+manifest.hardProps+";no historical full-art installer invoked.");
            File.WriteAllText(ReportFolder+"/Authoring.txt",string.Join("\n",log));
        }
    }
}
