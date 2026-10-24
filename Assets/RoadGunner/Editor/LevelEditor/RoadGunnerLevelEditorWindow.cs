using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace RoadGunner.Editor
{
    public sealed class RoadGunnerLevelEditorWindow:EditorWindow
    {
        enum Mode { None, Prefab, Terrain, Player, Encounter, VehicleSpawn }
        [SerializeField] int tab;
        [SerializeField] Vector2 scroll;
        [SerializeField] GameObject selectedPrefab;
        [SerializeField] float yaw,scale=1,grid=1,brushRadius=5,brushStrength=3,flattenHeight;
        [SerializeField] bool randomYaw,randomScale,alignGround,gridSnap,decoration,showRanges,showBounds=true;
        [SerializeField] Vector2 scaleRange=new Vector2(.85f,1.15f);
        [SerializeField] ReliefOperation operation;
        Mode mode;bool stroke;int undoGroup;double lastDab;bool hierarchyPending;
        GameObject[] enemyPalette=Array.Empty<GameObject>(),propPalette=Array.Empty<GameObject>();
        List<(MessageType type,string text)> validation=new List<(MessageType,string)>();
        BoxBoundsHandleWrapper boundsHandle=new BoxBoundsHandleWrapper();
        [MenuItem("Tools/Road Gunner/Level Editor")]
        public static void Open(){var w=GetWindow<RoadGunnerLevelEditorWindow>();w.titleContent=new GUIContent("Road Gunner Level Editor");w.minSize=new Vector2(430,620);w.Show();}
        void OnEnable(){titleContent=new GUIContent("Road Gunner Level Editor");SceneView.duringSceneGui+=SceneGUI;Undo.undoRedoPerformed+=UndoRedo;EditorApplication.hierarchyChanged+=HierarchyChanged;EditorApplication.update+=Tick;RefreshPalette();}
        void OnDisable(){Cancel();SceneView.duringSceneGui-=SceneGUI;Undo.undoRedoPerformed-=UndoRedo;EditorApplication.hierarchyChanged-=HierarchyChanged;EditorApplication.update-=Tick;}
        void RefreshPalette(){enemyPalette=RoadGunnerLevelAuthoring.Prefabs(true);propPalette=RoadGunnerLevelAuthoring.Prefabs(false);}
        void HierarchyChanged(){hierarchyPending=true;}
        void Tick(){if(!hierarchyPending||EditorApplication.isPlayingOrWillChangePlaymode)return;hierarchyPending=false;var l=RoadGunnerLevelAuthoring.Current;if(l&&l.authoredContentRoot){RoadGunnerLevelAuthoring.Wire(l);var d=l.session.spawns;if(d.encounters!=null&&d.encounters.Any(e=>!e)){Undo.RecordObject(d,"Remove deleted encounters");d.encounters=d.encounters.Where(e=>e).ToArray();}if(d.buggySpawnPoints!=null&&d.buggySpawnPoints.Any(t=>!t)){Undo.RecordObject(d,"Remove deleted spawn markers");d.buggySpawnPoints=d.buggySpawnPoints.Where(t=>t).ToArray();}}Repaint();}
        void UndoRedo(){var l=RoadGunnerLevelAuthoring.Current;if(l&&l.ground)RoadGunnerTerrainBrush.Refresh(l);hierarchyPending=true;Repaint();}
        void Act(Action action){try{action();validation.Clear();RefreshPalette();Repaint();SceneView.RepaintAll();}catch(Exception ex){validation=new List<(MessageType,string)>{(MessageType.Error,ex.Message)};Debug.LogError("Road Gunner Level Editor: "+ex.Message);}}
        void StartMode(Mode m){Cancel();mode=m;SceneView.lastActiveSceneView?.Focus();SceneView.RepaintAll();}
        void Cancel(){if(stroke){Undo.CollapseUndoOperations(undoGroup);stroke=false;}mode=Mode.None;GUIUtility.hotControl=0;SceneView.RepaintAll();}
        void OnGUI()
        {
            var l=RoadGunnerLevelAuthoring.Current;EditorGUILayout.LabelField("ROAD GUNNER",EditorStyles.boldLabel);
            EditorGUILayout.LabelField(l?l.levelName:"No prepared level",EditorStyles.largeLabel);EditorGUILayout.LabelField(SceneManager.GetActiveScene().path,EditorStyles.miniLabel);
            if(EditorApplication.isPlaying){EditorGUILayout.HelpBox("Play Mode — authoring is paused. Stop to continue editing.",MessageType.Info);if(GUILayout.Button("STOP TEST"))EditorApplication.isPlaying=false;return;}
            EditorGUI.BeginChangeCheck();tab=GUILayout.Toolbar(tab,new[]{"LEVEL","ENEMIES","OBSTACLES","TERRAIN","SETTINGS","VALIDATION"});if(EditorGUI.EndChangeCheck())Cancel();
            if(mode!=Mode.None){EditorGUILayout.HelpBox("Active: "+mode+" — click/drag in Scene View. Escape cancels; Alt navigates.",MessageType.Info);if(GUILayout.Button("Cancel placement / brush"))Cancel();}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            if(tab==0)LevelUI(l);else if(!l||!l.authoredContentRoot){EditorGUILayout.HelpBox("Prepare the current gameplay scene from LEVEL first.",MessageType.Info);}else if(tab==1)EnemiesUI(l);else if(tab==2)ObstaclesUI(l);else if(tab==3)TerrainUI(l);else if(tab==4)SettingsUI(l);else ValidationUI(l);
            if(tab!=5)foreach(var v in validation.Where(x=>x.type==MessageType.Error))EditorGUILayout.HelpBox(v.text,v.type);
            EditorGUILayout.EndScrollView();
        }
        void LevelUI(LevelLayout l)
        {
            EditorGUILayout.HelpBox("Levels are Unity scenes. New Level copies the working gameplay setup, keeps ground and perimeter, and starts with no encounters or interior props.",MessageType.Info);
            if(GUILayout.Button("Create New Level")){Cancel();if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()){string p=EditorUtility.SaveFilePanelInProject("New Road Gunner level","Level_New","unity","Choose a new scene path",RoadGunnerLevelAuthoring.Root+"/Scenes");if(!string.IsNullOrEmpty(p))Act(()=>RoadGunnerLevelAuthoring.CreateLevel(p));}}
            EditorGUILayout.LabelField("Open existing level",EditorStyles.boldLabel);var scenes=AssetDatabase.FindAssets("t:Scene",new[]{RoadGunnerLevelAuthoring.Root+"/Scenes"});foreach(var id in scenes){string p=AssetDatabase.GUIDToAssetPath(id);if(GUILayout.Button(Path.GetFileNameWithoutExtension(p)))OpenScene(p);}
            var scene=(SceneAsset)EditorGUILayout.ObjectField("Other scene",null,typeof(SceneAsset),false);if(scene)OpenScene(AssetDatabase.GetAssetPath(scene));
            if(GUILayout.Button("Prepare Current Gameplay Scene"))Act(()=>{var prepared=RoadGunnerLevelAuthoring.PrepareCurrent();RoadGunnerLevelAuthoring.ExtractEnvironmentPalette(prepared);});
            if(!l)return;
            var so=new SerializedObject(l);so.Update();EditorGUILayout.PropertyField(so.FindProperty("levelName"),new GUIContent("Level name"));if(so.ApplyModifiedProperties())RoadGunnerLevelAuthoring.Dirty(l);
            using(new EditorGUILayout.HorizontalScope()){
                if(GUILayout.Button("Save Level"))Act(()=>RoadGunnerLevelAuthoring.Save(l));
                if(GUILayout.Button("Save As")){string p=EditorUtility.SaveFilePanelInProject("Save level as",l.levelName+"_Copy","unity","Creates independent terrain and stat overrides",RoadGunnerLevelAuthoring.Root+"/Scenes");if(!string.IsNullOrEmpty(p))Act(()=>RoadGunnerLevelAuthoring.SaveAs(l,p));}
            }
            if(GUILayout.Button("PLACE PLAYER SPAWN"))StartMode(Mode.Player);yaw=EditorGUILayout.Slider("Facing / placement yaw",yaw,0,360);
            EditorGUILayout.ObjectField("Spawn marker",l.playerSpawn,typeof(Transform),true);
            if(l.playerSpawn&&GUILayout.Button("Select spawn (Move / Rotate tools)")){Selection.activeTransform=l.playerSpawn;SceneView.lastActiveSceneView?.FrameSelected();}
            if(GUILayout.Button("Clear authored enemies, props and spawn markers")&&EditorUtility.DisplayDialog("Clear authored content?","Removes content placed through this editor. Keeps ground, perimeter, gameplay systems and player spawn. Undo is available.","Clear","Cancel"))Act(()=>RoadGunnerLevelAuthoring.Clear(l));
            if(GUILayout.Button("PLAY LEVEL",GUILayout.Height(32)))Play(l);
        }
        void OpenScene(string p){Cancel();if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())Act(()=>EditorSceneManager.OpenScene(p));}
        void Palette(GameObject[] prefabs)
        {
            foreach(var p in prefabs){using(new EditorGUILayout.HorizontalScope()){var image=AssetPreview.GetAssetPreview(p)??AssetPreview.GetMiniThumbnail(p);GUILayout.Label(image,GUILayout.Width(42),GUILayout.Height(42));bool active=selectedPrefab==p;if(GUILayout.Toggle(active,p.name,"Button",GUILayout.Height(42))&&!active){selectedPrefab=p;Cancel();}}}
            selectedPrefab=(GameObject)EditorGUILayout.ObjectField("Additional prefab",selectedPrefab,typeof(GameObject),false);
            if(GUILayout.Button("Refresh palette"))RefreshPalette();
        }
        void PlacementOptions(bool props)
        {
            yaw=EditorGUILayout.Slider("Y rotation",yaw,0,360);gridSnap=EditorGUILayout.Toggle("Grid snapping",gridSnap);if(gridSnap)grid=Mathf.Max(.1f,EditorGUILayout.FloatField("Grid metres",grid));
            if(props){scale=EditorGUILayout.Slider("Scale",scale,.1f,3);randomYaw=EditorGUILayout.Toggle("Random Y rotation",randomYaw);randomScale=EditorGUILayout.Toggle("Random scale",randomScale);if(randomScale){scaleRange=EditorGUILayout.Vector2Field("Scale min / max",scaleRange);scaleRange.x=Mathf.Max(.1f,scaleRange.x);scaleRange.y=Mathf.Max(scaleRange.x,scaleRange.y);}alignGround=EditorGUILayout.Toggle("Align to ground normal",alignGround);decoration=EditorGUILayout.Toggle("Decoration (no collisions)",decoration);}
            if(GUILayout.Button("PLACE SELECTED PREFAB")) {if(selectedPrefab)StartMode(Mode.Prefab);else validation=new List<(MessageType,string)>{(MessageType.Warning,"Choose a prefab first.")};}
        }
        void EnemiesUI(LevelLayout l)
        {
            EditorGUILayout.LabelField("Human enemies and enemy vehicles",EditorStyles.boldLabel);Palette(enemyPalette);PlacementOptions(false);
            EditorGUILayout.Space();using(new EditorGUILayout.HorizontalScope()){if(GUILayout.Button("Place Human Encounter"))StartMode(Mode.Encounter);if(GUILayout.Button("Place Vehicle Spawn"))StartMode(Mode.VehicleSpawn);}
            EditorGUILayout.HelpBox("Individual prefabs start at the placed position and retain their own tuning. Encounter / Vehicle Spawn markers use the existing replenishing pools. Do not place both at the same point.",MessageType.Info);
            showRanges=EditorGUILayout.Toggle("Show all authored ranges",showRanges);
            var c=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<CombatTarget>():null;
            if(c&&c.transform.IsChildOf(l.authoredContentRoot))StatsUI(c);
            else {var encounter=Selection.activeGameObject?Selection.activeGameObject.GetComponent<EncounterSpawn>():null;if(encounter){var so=new SerializedObject(encounter);so.Update();EditorGUILayout.PropertyField(so.FindProperty("enemyCount"));EditorGUILayout.PropertyField(so.FindProperty("fallbackRadius"));EditorGUILayout.PropertyField(so.FindProperty("spawnPoints"),true);if(so.ApplyModifiedProperties())RoadGunnerLevelAuthoring.Dirty(encounter);}else EditorGUILayout.HelpBox("Select an authored enemy to edit its existing stats. W/E/R, Delete and Cmd-D use normal Unity selection and Undo.",MessageType.Info);}
        }
        static readonly string[] HumanStats={"enemyHealth","moveSpeed","aggroRadius","meleeReach","meleeDamage","attackCooldown","replenishDelay"};
        static readonly string[] VehicleStats={"health","aggroRadius","maxSpeed","driveTorque","preferredRange","retreatDistance","retreatSpeed","shotsPerSecond","bulletDamage","yawSpeed","aimTolerance","gunWarmup","scrapReward","respawnDelay","wreckLifetime"};
        void StatsUI(CombatTarget c)
        {
            var r=c as Raider;var b=c as EnemyBuggy;
            var identity=c.GetComponent<EnemyIdentity>();if(identity)EditorGUILayout.LabelField("Rank",identity.Rank.ToString());
            if(!r&&!b){EditorGUILayout.LabelField(c.name,EditorStyles.boldLabel);EditorGUILayout.HelpBox("This enemy stores its encounter settings on its component. Edit them in the Inspector.",MessageType.Info);if(GUILayout.Button("Edit enemy in Inspector")){Selection.activeObject=c;EditorGUIUtility.PingObject(c);EditorApplication.ExecuteMenuItem("Window/General/Inspector");}return;}
            var source=PrefabUtility.GetCorrespondingObjectFromSource(c);ScriptableObject tuning=r?(ScriptableObject)r.tuning:b.tuning;
            ScriptableObject defaults=source is Raider sr?(sr.tuning?sr.tuning:RoadGunnerLevelAuthoring.Current.session.spawns.tuning):source is EnemyBuggy sb?sb.tuning:null;
            bool own=tuning&&AssetDatabase.GetAssetPath(tuning).StartsWith(RoadGunnerLevelAuthoring.DataFolder()+"/Enemy Overrides/",StringComparison.Ordinal);
            EditorGUILayout.LabelField(c.name,EditorStyles.boldLabel);EditorGUILayout.LabelField(own?"LEVEL OVERRIDE":"DEFAULT VALUE — shared config",EditorStyles.boldLabel);EditorGUILayout.ObjectField("Current tuning",tuning,typeof(ScriptableObject),false);
            if(!own&&GUILayout.Button("Create per-instance LEVEL OVERRIDE"))Act(()=>RoadGunnerLevelAuthoring.MakeOverride(c));
            if(own&&GUILayout.Button("Use prefab DEFAULT VALUE"))Act(()=>RoadGunnerLevelAuthoring.UseDefault(c));
            if(!tuning)return;var so=new SerializedObject(tuning);SerializedObject def=defaults?new SerializedObject(defaults):null;so.Update();
            using(new EditorGUI.DisabledScope(!own))foreach(string name in r?HumanStats:VehicleStats){var p=so.FindProperty(name);if(p==null)continue;using(new EditorGUILayout.HorizontalScope()){EditorGUILayout.PropertyField(p);var dp=def?.FindProperty(name);if(dp!=null)GUILayout.Label("Default "+(dp.propertyType==SerializedPropertyType.Integer?dp.intValue.ToString():dp.floatValue.ToString("0.##")),EditorStyles.miniLabel,GUILayout.Width(90));}}
            if(so.ApplyModifiedProperties()) {EditorUtility.SetDirty(tuning);RoadGunnerLevelAuthoring.Dirty(c);}
            if(r&&r.arenaBoss){var encounter=new SerializedObject(r);encounter.Update();foreach(var field in new[]{"bossWindup","bossStrikeRadius","bossStrikeOffset","bossRecovery","bossGearReward"})EditorGUILayout.PropertyField(encounter.FindProperty(field));if(encounter.ApplyModifiedProperties())RoadGunnerLevelAuthoring.Dirty(r);}
            EditorGUILayout.HelpBox(r?(r.arenaBoss?"Final Boss Raider uses this health/damage tuning, a committed ground-warning strike, and the configured Metal Gear reward. Its death completes the Arena.":"Human drop is the existing fixed 1 Metal Gear. No duplicate drop chance or patrol system was added."):"Gun range is the existing shared player range; damage/fire rate and vehicle reward use BuggyTuning.",MessageType.Info);
            if(!r&&!b)return;var t=new SerializedObject(c.transform);t.Update();EditorGUILayout.PropertyField(t.FindProperty("m_LocalPosition"));t.ApplyModifiedProperties();EditorGUI.BeginChangeCheck();var euler=EditorGUILayout.Vector3Field("Rotation",c.transform.eulerAngles);if(EditorGUI.EndChangeCheck()){Undo.RecordObject(c.transform,"Rotate enemy");c.transform.eulerAngles=euler;PrefabUtility.RecordPrefabInstancePropertyModifications(c.transform);RoadGunnerLevelAuthoring.Dirty(c);}
        }
        void ObstaclesUI(LevelLayout l)
        {
            EditorGUILayout.HelpBox("Blocking props use the existing Default-layer solid colliders; decorations disable their colliders. Both remain static, with no added runtime scripts.",MessageType.Info);
            Palette(propPalette);PlacementOptions(true);
            if(GUILayout.Button("Extract palette from existing environment"))Act(()=>RoadGunnerLevelAuthoring.ExtractEnvironmentPalette(l));
            using(new EditorGUILayout.HorizontalScope()){
                if(GUILayout.Button("Delete selection"))Act(()=>{foreach(var g in SelectedAuthored(l))Undo.DestroyObjectImmediate(g);RoadGunnerLevelAuthoring.Wire(l);l.navigationNeedsBake=true;RoadGunnerLevelAuthoring.Dirty(l);});
                if(GUILayout.Button("Duplicate selection"))Act(()=>{var copies=new List<Object>();foreach(var g in SelectedAuthored(l)){var prefab=PrefabUtility.GetCorrespondingObjectFromSource(g);if(!prefab)continue;var copy=RoadGunnerLevelAuthoring.Place(l,prefab,RoadGunnerTerrainBrush.GroundPoint(l,g.transform.position.x+2,g.transform.position.z),Vector3.up,g.transform.eulerAngles.y,g.transform.localScale.x/prefab.transform.localScale.x,false,g.transform.IsChildOf(l.decorationRoot));copies.Add(copy);}Selection.objects=copies.ToArray();});
            }
            if(GUILayout.Button("Clear selected prefab type")&&selectedPrefab&&EditorUtility.DisplayDialog("Clear selected type?","Remove all authored instances of "+selectedPrefab.name+"? Undo is available.","Clear","Cancel"))Act(()=>{foreach(var parent in new[]{l.obstaclesRoot,l.decorationRoot}){var remove=new List<GameObject>();foreach(Transform t in parent)if(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)==selectedPrefab)remove.Add(t.gameObject);foreach(var g in remove)Undo.DestroyObjectImmediate(g);}l.navigationNeedsBake=true;RoadGunnerLevelAuthoring.Dirty(l);});
        }
        static IEnumerable<GameObject> SelectedAuthored(LevelLayout l)=>Selection.gameObjects.Where(g=>g.transform.parent&&(g.transform.parent==l.humanEnemiesRoot||g.transform.parent==l.enemyVehiclesRoot||g.transform.parent==l.obstaclesRoot||g.transform.parent==l.decorationRoot));
        void TerrainUI(LevelLayout l)
        {
            EditorGUILayout.ObjectField("Mesh ground",l.ground,typeof(MeshFilter),true);EditorGUILayout.HelpBox("Edits this level's existing mesh terrain. Brush size is radius in metres; strength is height change per second. Save / Play rebuilds navigation.",MessageType.Info);
            operation=(ReliefOperation)GUILayout.Toolbar((int)operation,new[]{"RAISE","LOWER","SMOOTH","FLATTEN"});brushRadius=EditorGUILayout.Slider("Brush size / radius",brushRadius,1,20);brushStrength=EditorGUILayout.Slider("Brush strength",brushStrength,.1f,15);flattenHeight=EditorGUILayout.FloatField("Flatten target world height",flattenHeight);
            if(GUILayout.Button("EDIT RELIEF IN SCENE VIEW"))StartMode(Mode.Terrain);
            if(l.ground){EditorGUILayout.LabelField("Geometry",l.ground.sharedMesh.vertexCount+" vertices / "+l.ground.sharedMesh.triangles.Length/3+" triangles");if(GUILayout.Button("Flatten All / Set Base Height")&&EditorUtility.DisplayDialog("Flatten terrain?","Set every ground vertex to target height? Undo is available.","Flatten","Cancel"))Act(()=>RoadGunnerTerrainBrush.All(l,ReliefOperation.Flatten,flattenHeight));if(GUILayout.Button("Smooth All"))Act(()=>RoadGunnerTerrainBrush.All(l,ReliefOperation.Smooth,flattenHeight));if(GUILayout.Button("Reset Terrain to level baseline")&&EditorUtility.DisplayDialog("Reset terrain?","Restore this level's captured starting relief? Undo is available.","Reset","Cancel"))Act(()=>RoadGunnerTerrainBrush.Reset(l));}
        }
        void SettingsUI(LevelLayout l)
        {
            showBounds=EditorGUILayout.Toggle("Show editable bounds",showBounds);var so=new SerializedObject(l);so.Update();EditorGUILayout.PropertyField(so.FindProperty("gameplayBounds"));EditorGUILayout.PropertyField(so.FindProperty("authoringNotes"));EditorGUILayout.PropertyField(so.FindProperty("recoveryPoints"),true);if(so.ApplyModifiedProperties())RoadGunnerLevelAuthoring.Dirty(l);
            var d=new SerializedObject(l.session.spawns);d.Update();EditorGUILayout.PropertyField(d.FindProperty("repeatAuthoredEnemies"),new GUIContent("Replenish individual placements"));EditorGUILayout.PropertyField(d.FindProperty("encounters"),true);EditorGUILayout.PropertyField(d.FindProperty("buggySpawnPoints"),true);if(d.ApplyModifiedProperties())RoadGunnerLevelAuthoring.Dirty(l.session.spawns);
            EditorGUILayout.HelpBox("Bounds guide placement and validation. Existing scene perimeter colliders enforce physical boundaries. Legacy group centers remain unchanged when editing existing levels.",MessageType.Info);
            if(GUILayout.Button("Bake Navigation"))Act(()=>RoadGunnerLevelAuthoring.Bake(l));
        }
        void ValidationUI(LevelLayout l)
        {if(GUILayout.Button("VALIDATE LEVEL"))validation=RoadGunnerLevelAuthoring.Validate(l);foreach(var v in validation)EditorGUILayout.HelpBox(v.text,v.type);if(GUILayout.Button("PLAY LEVEL",GUILayout.Height(32)))Play(l);}
        void Play(LevelLayout l){Cancel();Act(()=>{RoadGunnerLevelAuthoring.Save(l);validation=RoadGunnerLevelAuthoring.Validate(l);if(validation.Any(v=>v.type==MessageType.Error))throw new InvalidOperationException("Resolve validation errors before Play Level.");EditorApplication.isPlaying=true;});}
        void SceneGUI(SceneView view)
        {
            if(EditorApplication.isPlaying)return;var l=RoadGunnerLevelAuthoring.Current;if(!l||!l.authoredContentRoot||!l.session)return;var e=Event.current;
            DrawGuides(l);if(mode==Mode.None)return;
            int id=GUIUtility.GetControlID("RoadGunnerPlacement".GetHashCode(),FocusType.Passive);
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){Cancel();e.Use();return;}
            if(e.alt||e.button==1||e.button==2)return;if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(id);
            if(e.type==EventType.MouseUp&&e.button==0&&stroke){Undo.CollapseUndoOperations(undoGroup);stroke=false;GUIUtility.hotControl=0;e.Use();return;}
            if(!RoadGunnerTerrainBrush.GroundHit(l,HandleUtility.GUIPointToWorldRay(e.mousePosition),out var hit))return;
            Vector3 point=hit.point;if(gridSnap&&mode!=Mode.Terrain){point.x=Mathf.Round(point.x/grid)*grid;point.z=Mathf.Round(point.z/grid)*grid;if(RoadGunnerTerrainBrush.GroundHit(l,new Ray(point+Vector3.up*100,Vector3.down),out var snapped))point=snapped.point;}
            Handles.color=mode==Mode.Terrain?new Color(1,.65f,.15f,.85f):Color.cyan;Handles.DrawWireDisc(point,hit.normal,mode==Mode.Terrain?brushRadius:1);
            if(mode!=Mode.Terrain){Handles.DrawLine(point,point+Quaternion.Euler(0,yaw,0)*Vector3.forward*3);Handles.Label(point+Vector3.up*.5f,mode==Mode.Prefab&&selectedPrefab?selectedPrefab.name:mode.ToString());}
            if(e.type==EventType.MouseMove){view.Repaint();return;}
            if(e.type==EventType.MouseUp&&e.button==0){if(stroke){Undo.CollapseUndoOperations(undoGroup);stroke=false;GUIUtility.hotControl=0;e.Use();}return;}
            if((e.type!=EventType.MouseDown&&e.type!=EventType.MouseDrag)||e.button!=0)return;
            if(mode==Mode.Terrain){
                if(!stroke){Undo.IncrementCurrentGroup();undoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Sculpt terrain "+operation);Undo.RegisterCompleteObjectUndo(l.ground.sharedMesh,"Terrain stroke");Undo.RecordObject(l,"Terrain navigation dirty");stroke=true;lastDab=EditorApplication.timeSinceStartup-.08;GUIUtility.hotControl=id;}
                double now=EditorApplication.timeSinceStartup;float seconds=(float)Math.Min(.15,now-lastDab);if(seconds>.016f){RoadGunnerTerrainBrush.Apply(l,point,brushRadius,brushStrength,flattenHeight,operation,seconds,false);lastDab=now;}
            }else if(e.type==EventType.MouseDown){
                Act(()=>{if(mode==Mode.Player)RoadGunnerLevelAuthoring.PlacePlayer(l,point,yaw);else if(mode==Mode.Encounter)RoadGunnerLevelAuthoring.PlaceEncounter(l,point,yaw);else if(mode==Mode.VehicleSpawn)RoadGunnerLevelAuthoring.PlaceVehicleSpawn(l,point,yaw);else if(selectedPrefab){bool enemy=selectedPrefab.GetComponent<CombatTarget>();RoadGunnerLevelAuthoring.Place(l,selectedPrefab,point,hit.normal,enemy?yaw:randomYaw?UnityEngine.Random.Range(0,360):yaw,enemy?1:randomScale?UnityEngine.Random.Range(scaleRange.x,scaleRange.y):scale,!enemy&&alignGround,!enemy&&decoration);}});
            }
            e.Use();view.Repaint();
        }
        void DrawGuides(LevelLayout l)
        {
            if(l.playerSpawn){Handles.color=Color.cyan;Handles.DrawWireCube(l.playerSpawn.position+Vector3.up*.4f,new Vector3(2.2f,1,5.6f));Handles.ArrowHandleCap(0,l.playerSpawn.position,l.playerSpawn.rotation,4,EventType.Repaint);Handles.Label(l.playerSpawn.position+Vector3.up*2,"PLAYER SPAWN");}
            if(showBounds){Handles.color=new Color(.4f,.8f,1,.5f);boundsHandle.Draw(l);}
            foreach(var c in l.authoredContentRoot.GetComponentsInChildren<CombatTarget>(true)){
                bool selected=Selection.activeGameObject&&Selection.activeGameObject.transform.IsChildOf(c.transform);if(!showRanges&&!selected)continue;
                float detect,attack;
                if(c is Raider r){if(!r.tuning)continue;detect=r.tuning.aggroRadius;attack=r.tuning.meleeReach;}
                else if(c is EnemyBuggy b){if(!b.tuning)continue;detect=b.tuning.aggroRadius;attack=l.session.turret.Range;}
                else if(c is WastelandMutant mutant){detect=mutant.aggroRadius;attack=mutant.meleeReach;}
                else if(c is TruckArenaBoss truck){detect=truck.aggroRadius;attack=truck.playerRange;}
                else continue;
                if(c is Raider bossRaider&&bossRaider.arenaBoss){Handles.color=new Color(1,.65f,.1f,.8f);Handles.DrawWireDisc(c.transform.position+c.transform.forward*bossRaider.bossStrikeOffset,Vector3.up,bossRaider.bossStrikeRadius);}
                Handles.color=new Color(1,.5f,.1f,.7f);Handles.DrawWireDisc(c.transform.position,Vector3.up,detect);Handles.color=new Color(1,.2f,.2f,.7f);Handles.DrawWireDisc(c.transform.position,Vector3.up,attack);Handles.Label(c.transform.position+Vector3.up*2,c.name+" / detection "+detect+" / attack "+attack);
            }
        }
        sealed class BoxBoundsHandleWrapper
        {
            readonly UnityEditor.IMGUI.Controls.BoxBoundsHandle handle=new UnityEditor.IMGUI.Controls.BoxBoundsHandle();
            public void Draw(LevelLayout l){handle.center=l.gameplayBounds.center;handle.size=l.gameplayBounds.size;EditorGUI.BeginChangeCheck();handle.DrawHandle();if(EditorGUI.EndChangeCheck()){Undo.RecordObject(l,"Resize level bounds");l.gameplayBounds=new Bounds(handle.center,handle.size);RoadGunnerLevelAuthoring.Dirty(l);}}
        }
    }
}
