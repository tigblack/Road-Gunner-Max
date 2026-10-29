using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace RoadGunner.Editor
{
    public static class RoadGunnerLevelAuthoring
    {
        public const string Root="Assets/RoadGunner";
        public const string Template=Root+"/Scenes/RoadGunner_Prototype.unity";
        public static T[] InScene<T>() where T:Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        public static LevelLayout Current=>InScene<LevelLayout>().FirstOrDefault();
        public static void Dirty(Object obj){EditorUtility.SetDirty(obj);if(obj is Component c)EditorSceneManager.MarkSceneDirty(c.gameObject.scene);}
        public static Transform Section(Transform parent,string name){var g=new GameObject(name);Undo.RegisterCreatedObjectUndo(g,"Create level section");if(parent)g.transform.SetParent(parent,false);return g.transform;}
        public static LevelLayout PrepareCurrent()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before authoring.");
            var session=InScene<GameSession>().FirstOrDefault();if(!session)throw new InvalidOperationException("Open a Road Gunner gameplay scene or create a new level.");
            var l=Current;
            if(!l){l=Undo.AddComponent<LevelLayout>(Section(null,"Level Authoring").gameObject);l.referenceUrl="";l.referenceInspected=true;l.authoringNotes="Scene-based level authored with Road Gunner Level Editor.";}
            Undo.RecordObject(l,"Prepare level authoring");l.session=session;if(string.IsNullOrEmpty(l.levelName))l.levelName=SceneManager.GetActiveScene().name;
            if(!l.ground)l.ground=InScene<MeshCollider>().Where(c=>c.sharedMesh&&c.sharedMesh.vertexCount>1000).OrderByDescending(c=>c.bounds.size.x*c.bounds.size.z).Select(c=>c.GetComponent<MeshFilter>()).FirstOrDefault();
            if(!l.playerSpawn){l.playerSpawn=Section(l.transform,"Player Spawn");l.playerSpawn.SetPositionAndRotation(session.startPosition,session.startRotation);}
            if(l.recoveryPoints==null||l.recoveryPoints.Length==0){l.recoveryPoints=new[]{l.playerSpawn};}
            if(!l.authoredContentRoot)l.authoredContentRoot=Section(l.transform,"Authored Content");
            if(!l.humanEnemiesRoot)l.humanEnemiesRoot=Section(l.authoredContentRoot,"Human Enemies");
            if(!l.enemyVehiclesRoot)l.enemyVehiclesRoot=Section(l.authoredContentRoot,"Enemy Vehicles");
            if(!l.obstaclesRoot)l.obstaclesRoot=Section(l.authoredContentRoot,"Gameplay Obstacles");
            if(!l.decorationRoot)l.decorationRoot=Section(l.authoredContentRoot,"Decoration");
            if(l.ground && !l.baselineTerrain)EnsureTerrainAssets(l,false);
            Wire(l);Dirty(l);return l;
        }
        public static string DataFolder()
        {
            var scene=SceneManager.GetActiveScene();if(string.IsNullOrEmpty(scene.path))throw new InvalidOperationException("Save the scene before editing relief.");
            string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/"+Path.GetFileNameWithoutExtension(scene.path)+"_Data";
            if(!Directory.Exists(folder)){AssetDatabase.SaveAssets();Directory.CreateDirectory(folder);AssetDatabase.Refresh();}return folder;
        }
        public static void EnsureTerrainAssets(LevelLayout l,bool forceCopy)
        {
            if(!l.ground||!l.ground.sharedMesh)return;string folder=DataFolder();string path=folder+"/Terrain.asset";
            var source=l.ground.sharedMesh;
            if(forceCopy||AssetDatabase.GetAssetPath(source)!=path){var copy=Object.Instantiate(source);copy.name=l.levelName+" terrain";path=AssetDatabase.GenerateUniqueAssetPath(path);AssetDatabase.CreateAsset(copy,path);Undo.RecordObject(l.ground,"Isolate level terrain");l.ground.sharedMesh=copy;var mc=l.ground.GetComponent<MeshCollider>();Undo.RecordObject(mc,"Isolate ground collision");mc.sharedMesh=copy;}
            if(!l.baselineTerrain||forceCopy){var b=Object.Instantiate(l.baselineTerrain?l.baselineTerrain:source);b.name="Unedited terrain baseline";AssetDatabase.CreateAsset(b,AssetDatabase.GenerateUniqueAssetPath(folder+"/Terrain baseline.asset"));l.baselineTerrain=b;}
            l.navigationNeedsBake=true;Dirty(l);
        }
        public static void Wire(LevelLayout l)
        {
            if(!l||!l.session||!l.session.spawns)return;var s=l.session;var d=s.spawns;
            var humans=l.humanEnemiesRoot?l.humanEnemiesRoot.GetComponentsInChildren<Raider>(true).Where(r=>!r.arenaBoss).ToArray():Array.Empty<Raider>();
            var vehicles=l.enemyVehiclesRoot?l.enemyVehiclesRoot.GetComponentsInChildren<EnemyBuggy>(true):Array.Empty<EnemyBuggy>();
            // Unity Duplicate copies config references. Keep explicit level overrides individual.
            foreach(var r in humans)if(r && r.tuning && !(d.authoredRaiders??Array.Empty<Raider>()).Contains(r) && humans.Any(other=>other!=r && other.tuning==r.tuning) && AssetDatabase.GetAssetPath(r.tuning).Contains("/Enemy Overrides/"))MakeOverride(r);
            foreach(var b in vehicles)if(b && b.tuning && !(d.authoredBuggies??Array.Empty<EnemyBuggy>()).Contains(b) && vehicles.Any(other=>other!=b && other.tuning==b.tuning) && AssetDatabase.GetAssetPath(b.tuning).Contains("/Enemy Overrides/"))MakeOverride(b);
            var pool=(d.pool??Array.Empty<Raider>()).Where(r=>r && !r.arenaBoss && !(d.authoredRaiders??Array.Empty<Raider>()).Contains(r)).Concat(humans).Distinct().ToArray();
            var bpool=(d.buggyPool??Array.Empty<EnemyBuggy>()).Where(b=>b && !(d.authoredBuggies??Array.Empty<EnemyBuggy>()).Contains(b)).Concat(vehicles).Distinct().ToArray();
            if(!pool.SequenceEqual(d.pool??Array.Empty<Raider>())||!bpool.SequenceEqual(d.buggyPool??Array.Empty<EnemyBuggy>())||!humans.SequenceEqual(d.authoredRaiders??Array.Empty<Raider>())||!vehicles.SequenceEqual(d.authoredBuggies??Array.Empty<EnemyBuggy>())){
                Undo.RecordObject(d,"Register authored enemies in existing pools");d.pool=pool;d.buggyPool=bpool;d.authoredRaiders=humans;d.authoredBuggies=vehicles;Dirty(d);
            }
            foreach(var r in humans)if(r.session!=s||!r.tuning){Undo.RecordObject(r,"Wire human session");r.session=s;if(!r.tuning)r.tuning=d.tuning;RecordPrefab(r);}
            foreach(var b in vehicles)if(b.session!=s){Undo.RecordObject(b,"Wire vehicle session");b.session=s;RecordPrefab(b);}
            WireBossEncounters(l);
            if(l.playerSpawn&&(s.startPosition!=l.playerSpawn.position||s.startRotation!=l.playerSpawn.rotation)) {Undo.RecordObject(s,"Update player start");s.startPosition=l.playerSpawn.position;s.startRotation=l.playerSpawn.rotation;Dirty(s);}
        }
        public static bool IsBoss(CombatTarget target) => target is WastelandMutant || target is TruckArenaBoss || target is Raider r && r.arenaBoss;
        static Transform SyncBossHome(CombatTarget target,Transform marker) {
            if(!marker)marker=Section(target.transform.parent,target.name+" home");
            if(marker.position!=target.transform.position||marker.rotation!=target.transform.rotation) {
                Undo.RecordObject(marker,"Move authored boss home");marker.SetPositionAndRotation(target.transform.position,target.transform.rotation);RecordPrefab(marker);
            }
            return marker;
        }
        static void WireBossEncounters(LevelLayout l) {
            if(!l.authoredContentRoot||EditorApplication.isPlaying)return;
            var s=l.session;var bosses=l.authoredContentRoot.GetComponentsInChildren<CombatTarget>(true).Where(IsBoss).ToArray();
            var roster=s.zone1?s.zone1:s.GetComponent<Zone1EnemyRoster>();
            if(!roster&&bosses.Length==0)return;
            if(!roster)roster=Undo.AddComponent<Zone1EnemyRoster>(s.gameObject);
            if(roster.session!=s||!bosses.SequenceEqual(roster.bosses??Array.Empty<CombatTarget>())) {
                Undo.RecordObject(roster,"Register authored boss encounters");roster.session=s;roster.bosses=bosses;Dirty(roster);
            }
            if(s.zone1!=roster){Undo.RecordObject(s,"Wire boss roster");s.zone1=roster;Dirty(s);}
            var assignedHomes=new HashSet<Transform>();
            foreach(var target in bosses) {
                if(target is Raider raider){var home=SyncBossHome(target,raider.spawnPoint&&!assignedHomes.Contains(raider.spawnPoint)?raider.spawnPoint:null);assignedHomes.Add(home);if(raider.session!=s||raider.spawnPoint!=home){Undo.RecordObject(raider,"Wire boss Raider");raider.session=s;raider.spawnPoint=home;RecordPrefab(raider);}}
                else if(target is WastelandMutant mutant){var home=SyncBossHome(target,mutant.spawnPoint&&!assignedHomes.Contains(mutant.spawnPoint)?mutant.spawnPoint:null);assignedHomes.Add(home);if(mutant.session!=s||mutant.spawnPoint!=home){Undo.RecordObject(mutant,"Wire mini-boss");mutant.session=s;mutant.spawnPoint=home;RecordPrefab(mutant);}}
                else if(target is TruckArenaBoss truck){var home=SyncBossHome(target,truck.spawnPoint&&!assignedHomes.Contains(truck.spawnPoint)?truck.spawnPoint:null);assignedHomes.Add(home);if(truck.session!=s||truck.spawnPoint!=home){Undo.RecordObject(truck,"Wire truck encounter");truck.session=s;truck.spawnPoint=home;RecordPrefab(truck);}}
            }
        }
        static void RecordPrefab(Object o){if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);Dirty(o);}
        public static GameObject Place(LevelLayout l,GameObject prefab,Vector3 ground,Vector3 normal,float yaw,float scale,bool align,bool decoration)
        {
            if(!l||!prefab||!PrefabUtility.IsPartOfPrefabAsset(prefab))throw new InvalidOperationException("Choose a prefab asset.");
            bool human=prefab.GetComponent<Raider>(),vehicle=prefab.GetComponent<EnemyBuggy>();
            bool boss=IsBoss(prefab.GetComponent<CombatTarget>());
            var parent=boss?l.authoredContentRoot:human?l.humanEnemiesRoot:vehicle?l.enemyVehiclesRoot:decoration?l.decorationRoot:l.obstaclesRoot;
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);Undo.RegisterCreatedObjectUndo(obj,"Place "+prefab.name);obj.name=prefab.name;obj.SetActive(true);
            obj.transform.rotation=Quaternion.Euler(0,yaw,0);obj.transform.localScale=prefab.transform.localScale*scale;
            if(!human&&!vehicle&&!boss&&align)obj.transform.rotation=Quaternion.FromToRotation(Vector3.up,normal)*obj.transform.rotation;
            obj.transform.position=ground;
            if(vehicle)obj.transform.position+=Vector3.up*.65f;
            if(!human&&!vehicle&&!boss){
                if(decoration){foreach(var collider in obj.GetComponentsInChildren<Collider>(true)){Undo.RecordObject(collider,"Make decoration nonblocking");collider.enabled=false;RecordPrefab(collider);}}
                else if(!obj.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled&&!c.isTrigger)){
                    Object.DestroyImmediate(obj);throw new InvalidOperationException("This prefab has no solid collider. Place it as Decoration or add a collider to the prefab.");}
                var renderers=obj.GetComponentsInChildren<Renderer>();if(renderers.Length>0){var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);obj.transform.position+=Vector3.up*(ground.y-bounds.min.y);}
                foreach(var t in obj.GetComponentsInChildren<Transform>())t.gameObject.isStatic=true;
                l.navigationNeedsBake=true;
            }
            RecordPrefab(obj.transform);Wire(l);Dirty(l);Selection.activeGameObject=obj;return obj;
        }
        public static void PlacePlayer(LevelLayout l,Vector3 ground,float yaw)
        {Undo.RecordObject(l.playerSpawn,"Place player spawn");l.playerSpawn.SetPositionAndRotation(ground+Vector3.up*.70f,Quaternion.Euler(0,yaw,0));Wire(l);Selection.activeTransform=l.playerSpawn;Dirty(l);}
        public static EncounterSpawn PlaceEncounter(LevelLayout l,Vector3 ground,float yaw)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Levels/Level01/EncounterSpawn.prefab");var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab,l.authoredContentRoot);Undo.RegisterCreatedObjectUndo(g,"Place encounter");g.transform.SetPositionAndRotation(ground,Quaternion.Euler(0,yaw,0));var e=g.GetComponent<EncounterSpawn>();var d=l.session.spawns;Undo.RecordObject(d,"Register encounter");d.encounters=(d.encounters??Array.Empty<EncounterSpawn>()).Where(x=>x).Append(e).ToArray();Dirty(d);Selection.activeGameObject=g;return e;
        }
        public static Transform PlaceVehicleSpawn(LevelLayout l,Vector3 ground,float yaw)
        {var t=Section(l.authoredContentRoot,"Vehicle Spawn");t.SetPositionAndRotation(ground,Quaternion.Euler(0,yaw,0));var d=l.session.spawns;Undo.RecordObject(d,"Register vehicle spawn");d.buggySpawnPoints=(d.buggySpawnPoints??Array.Empty<Transform>()).Where(x=>x).Append(t).ToArray();Dirty(d);Selection.activeTransform=t;return t;}
        public static ScriptableObject MakeOverride(Component enemy)
        {
            ScriptableObject source=enemy is Raider r?(ScriptableObject)r.tuning:enemy is EnemyBuggy b?b.tuning:null;if(!source)return null;
            string folder=DataFolder()+"/Enemy Overrides";if(!Directory.Exists(folder)){AssetDatabase.SaveAssets();Directory.CreateDirectory(folder);AssetDatabase.Refresh();}var copy=Object.Instantiate(source);copy.name=enemy.name+" level override";
            AssetDatabase.CreateAsset(copy,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+enemy.name+".asset"));Undo.RecordObject(enemy,"Use level stat override");if(enemy is Raider human)human.tuning=(CombatTuning)copy;else if(enemy is EnemyBuggy vehicle)vehicle.tuning=(BuggyTuning)copy;RecordPrefab(enemy);return copy;
        }
        public static void UseDefault(Component enemy)
        {
            if(!enemy||(!(enemy is Raider)&&!(enemy is EnemyBuggy)))return;
            var source=PrefabUtility.GetCorrespondingObjectFromSource(enemy);if(!source)return;
            if(enemy is Raider r&&source is Raider human){Undo.RecordObject(enemy,"Restore prefab enemy stats");r.tuning=human.tuning?human.tuning:Current.session.spawns.tuning;}
            else if(enemy is EnemyBuggy b&&source is EnemyBuggy vehicle){Undo.RecordObject(enemy,"Restore prefab enemy stats");b.tuning=vehicle.tuning;}
            else return;
            RecordPrefab(enemy);
        }
        public static void Bake(LevelLayout l)
        {
            Physics.SyncTransforms();var surface=InScene<NavMeshSurface>().FirstOrDefault();if(!surface)surface=Undo.AddComponent<NavMeshSurface>(l.gameObject);
            Undo.RecordObject(surface,"Bake level navigation");surface.collectObjects=CollectObjects.All;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=1;surface.overrideVoxelSize=true;surface.voxelSize=.18f;surface.minRegionArea=3;
            string path=DataFolder()+"/Navigation.asset";var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            surface.RemoveData();surface.BuildNavMesh();var fresh=surface.navMeshData;if(fresh)fresh.name="Navigation";
            if(!fresh)throw new InvalidOperationException("Navigation bake produced no data.");
            if(old){EditorUtility.CopySerialized(fresh,old);surface.RemoveData();Object.DestroyImmediate(fresh);surface.navMeshData=old;surface.AddData();EditorUtility.SetDirty(old);}else AssetDatabase.CreateAsset(fresh,path);
            Undo.RecordObject(l,"Navigation baked");l.navigationNeedsBake=false;Dirty(surface);Dirty(l);
        }
        public static void Save(LevelLayout l,bool bake=true)
        {Wire(l);if(bake)Bake(l);AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))throw new IOException("Could not save scene.");}
        public static void SaveAs(LevelLayout l,string path)
        {
            if(File.Exists(path))throw new InvalidOperationException("Choose a new scene path; Save As does not overwrite another level.");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path);l.levelName=Path.GetFileNameWithoutExtension(path);EnsureTerrainAssets(l,true);
            foreach(var c in l.authoredContentRoot.GetComponentsInChildren<CombatTarget>(true)){
                ScriptableObject src=c is Raider r?(ScriptableObject)r.tuning:c is EnemyBuggy b?b.tuning:null;
                if(src&&AssetDatabase.GetAssetPath(src).Contains("/Enemy Overrides/"))MakeOverride(c);
            }
            Save(l);
        }
        public static LevelLayout CreateLevel(string path)
        {
            if(File.Exists(path))throw new InvalidOperationException("A level already exists at this path.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));var scene=EditorSceneManager.OpenScene(Template);EditorSceneManager.SaveScene(scene,path);
            var l=PrepareCurrent();l.levelName=Path.GetFileNameWithoutExtension(path);var d=l.session.spawns;
            Undo.RecordObject(d,"Start empty authored level");d.groupCenters=Array.Empty<Vector3>();d.encounters=Array.Empty<EncounterSpawn>();d.buggySpawnPoints=Array.Empty<Transform>();
            var parent=l.ground.transform.parent;var remove=new List<GameObject>();foreach(Transform t in parent){if(t==l.ground.transform)continue;if(t.name.Contains("perimeter",StringComparison.OrdinalIgnoreCase)||new Vector2(t.position.x,t.position.z).magnitude>46)continue;remove.Add(t.gameObject);}foreach(var g in remove)Undo.DestroyObjectImmediate(g);
            // Legacy runtime pool objects stay inactive; its obsolete visual spawn marker folder is removed.
            foreach(Transform t in d.transform)if(t.name.Contains("spawn",StringComparison.OrdinalIgnoreCase)&&!t.GetComponent<CombatTarget>())Undo.DestroyObjectImmediate(t.gameObject);
            d.authoredRaiders=Array.Empty<Raider>();d.authoredBuggies=Array.Empty<EnemyBuggy>();Wire(l);Save(l);return l;
        }
        public static void Clear(LevelLayout l)
        {
            var children=new List<GameObject>();foreach(var section in new[]{l.humanEnemiesRoot,l.enemyVehiclesRoot,l.obstaclesRoot,l.decorationRoot})foreach(Transform t in section)children.Add(t.gameObject);
            foreach(Transform t in l.authoredContentRoot)if(t.GetComponent<EncounterSpawn>()||t.name=="Vehicle Spawn")children.Add(t.gameObject);
            foreach(var g in children)Undo.DestroyObjectImmediate(g);Wire(l);var d=l.session.spawns;Undo.RecordObject(d,"Remove cleared spawn references");d.encounters=(d.encounters??Array.Empty<EncounterSpawn>()).Where(x=>x).ToArray();d.buggySpawnPoints=(d.buggySpawnPoints??Array.Empty<Transform>()).Where(x=>x).ToArray();l.navigationNeedsBake=true;Dirty(l);
        }
        public static GameObject[] Prefabs(bool enemies)
        {return AssetDatabase.FindAssets("t:Prefab",new[]{Root}).Select(id=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id))).Where(g=>g&&(enemies?g.GetComponent<CombatTarget>()!=null:g.GetComponentsInChildren<Renderer>().Length>0&&!g.GetComponent<CombatTarget>()&&!g.GetComponent<VehicleController>()&&!g.name.Contains("pickup",StringComparison.OrdinalIgnoreCase))).OrderBy(g=>g.name).ToArray();}
        public static void ExtractEnvironmentPalette(LevelLayout l)
        {
            string folder=Root+"/Prefabs/Environment";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var candidates=InScene<Transform>();
            foreach(string name in new[]{"Rounded sandstone","Rusted corrugated barricade","Suspension lane buried plank"}){
                string path=folder+"/"+name+".prefab";if(File.Exists(path))continue;var t=candidates.FirstOrDefault(x=>x.name==name&&x.GetComponentsInChildren<Collider>().Any(c=>!c.isTrigger));if(!t)continue;
                var copy=Object.Instantiate(t.gameObject);copy.name=name;copy.transform.position=Vector3.zero;copy.transform.rotation=Quaternion.identity;PrefabUtility.SaveAsPrefabAsset(copy,path);Object.DestroyImmediate(copy);
            }
            string decorative=folder+"/Sandstone pebble (decoration).prefab";
            if(!File.Exists(decorative)){
                var t=candidates.FirstOrDefault(x=>x.name=="Rounded sandstone"&&!x.GetComponent<Collider>());if(t){var copy=Object.Instantiate(t.gameObject);copy.name="Sandstone pebble (decoration)";copy.transform.position=Vector3.zero;copy.transform.rotation=Quaternion.identity;PrefabUtility.SaveAsPrefabAsset(copy,decorative);Object.DestroyImmediate(copy);}
            }
            AssetDatabase.SaveAssets();
        }
        public static List<(MessageType type,string text)> Validate(LevelLayout l)
        {
            var a=new List<(MessageType,string)>();if(!l){a.Add((MessageType.Error,"No LevelLayout. Prepare this gameplay scene first."));return a;}
            if(!l.playerSpawn)a.Add((MessageType.Error,"Missing player spawn."));
            if(!l.ground||!l.ground.sharedMesh||!l.ground.GetComponent<MeshCollider>())a.Add((MessageType.Error,"Missing mesh ground / MeshCollider."));
            if(!l.session||!l.session.vehicle||!l.session.spawns||!l.session.pickups||!l.session.hud||!l.session.turret)a.Add((MessageType.Error,"Missing existing gameplay references."));
            if(l.gameplayBounds.size.x<=0||l.gameplayBounds.size.z<=0)a.Add((MessageType.Error,"Level bounds must have positive width/depth."));
            if(l.playerSpawn){var p=l.playerSpawn.position;if(p.x<l.gameplayBounds.min.x||p.x>l.gameplayBounds.max.x||p.z<l.gameplayBounds.min.z||p.z>l.gameplayBounds.max.z)a.Add((MessageType.Warning,"Player spawn is outside level bounds."));}
            if(!l.authoredContentRoot||!l.humanEnemiesRoot||!l.enemyVehiclesRoot||!l.obstaclesRoot||!l.decorationRoot)a.Add((MessageType.Warning,"Missing authoring section; Prepare Current Gameplay Scene repairs the hierarchy."));
            if(l.navigationNeedsBake)a.Add((MessageType.Warning,"Navigation is stale. Save Level or Bake Navigation before Play."));
            if(!InScene<NavMeshSurface>().Any(n=>n.navMeshData))a.Add((MessageType.Error,"No baked navigation data."));
            if(l.authoredContentRoot){
                foreach(var t in l.authoredContentRoot.GetComponentsInChildren<Transform>(true)){
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)a.Add((MessageType.Error,"Missing script: "+t.name));
                    if(PrefabUtility.GetPrefabInstanceStatus(t.gameObject)==PrefabInstanceStatus.MissingAsset)a.Add((MessageType.Error,"Broken prefab: "+t.name));
                }
                foreach(var c in l.authoredContentRoot.GetComponentsInChildren<CombatTarget>(true)){
                    var p=c.transform.position;bool inside=p.x>=l.gameplayBounds.min.x&&p.x<=l.gameplayBounds.max.x&&p.z>=l.gameplayBounds.min.z&&p.z<=l.gameplayBounds.max.z;
                    if(!inside)a.Add((MessageType.Warning,"Enemy outside level bounds: "+c.name));
                    if(!PrefabUtility.IsPartOfPrefabInstance(c))a.Add((MessageType.Warning,"Enemy is not linked to a prefab: "+c.name));
                    if(c is Raider r&&(!r.agent||!r.tuning||r.session!=l.session))a.Add((MessageType.Error,"Unwired human: "+c.name));
                    if(c is EnemyBuggy b&&(!b.tuning||b.session!=l.session||!b.gun||b.wheels.Length!=4))a.Add((MessageType.Error,"Unwired vehicle: "+c.name));
                    if(c is Raider statsHuman && statsHuman.tuning && (statsHuman.tuning.enemyHealth<=0||statsHuman.tuning.moveSpeed<0||statsHuman.tuning.meleeDamage<0||statsHuman.tuning.attackCooldown<=0||statsHuman.tuning.aggroRadius<0))a.Add((MessageType.Error,"Invalid human tuning values: "+c.name));
                    if(c is EnemyBuggy statsBuggy && statsBuggy.tuning && (statsBuggy.tuning.health<=0||statsBuggy.tuning.maxSpeed<0||statsBuggy.tuning.bulletDamage<0||statsBuggy.tuning.shotsPerSecond<0||statsBuggy.tuning.scrapReward<0))a.Add((MessageType.Error,"Invalid vehicle tuning values: "+c.name));
                    if(!NavMesh.SamplePosition(p,out var hit,2,NavMesh.AllAreas))a.Add((MessageType.Warning,"Enemy is outside walkable navigation: "+c.name));
                    else if(l.playerSpawn&&NavMesh.SamplePosition(l.playerSpawn.position,out var start,3,NavMesh.AllAreas)){var path=new NavMeshPath();if(!NavMesh.CalculatePath(hit.position,start.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)a.Add((MessageType.Warning,"Enemy has no path to player: "+c.name));}
                }
                if(l.obstaclesRoot)foreach(Transform t in l.obstaclesRoot)if(!t.GetComponentsInChildren<Collider>().Any(c=>c.enabled&&!c.isTrigger))a.Add((MessageType.Error,"Gameplay obstacle has no solid collider: "+t.name));
            }
            if(l.playerSpawn&&l.ground&&l.session&&l.session.vehicle){var p=l.playerSpawn.position;var bc=l.session.vehicle.GetComponent<BoxCollider>();if(Physics.CheckBox(p+l.playerSpawn.rotation*bc.center,bc.size*.48f,l.playerSpawn.rotation,1,QueryTriggerInteraction.Ignore))a.Add((MessageType.Warning,"Player spawn intersects ground or a solid obstacle; place on flatter clear ground."));}
            if(l.session&&l.session.spawns){var d=l.session.spawns;int n=(d.authoredRaiders?.Length??0)+(d.authoredBuggies?.Length??0);if(n>d.tuning.activeCap)a.Add((MessageType.Warning,"Placed enemies exceed activeCap="+d.tuning.activeCap+"; excess will not spawn."));a.Add((MessageType.Info,"Authored enemies: "+n+"; legacy encounter groups: "+d.GroupCount+"; shared resource pool retained."));}
            a.Add((MessageType.Info,"Scenes serialize placements, prefab links and markers. Relief and override settings are separate per-level assets. Bounds are authoring/validation guides; physical perimeter stays in the scene."));return a;
        }
    }
}
