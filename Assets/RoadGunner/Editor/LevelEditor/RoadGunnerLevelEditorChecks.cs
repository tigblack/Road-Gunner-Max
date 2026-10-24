using System;
using System.Collections;
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
    [InitializeOnLoad]
    public static class RoadGunnerLevelEditorChecks
    {
        public const string Folder="Documentation/RoadGunner/LevelEditor-2026-10-09";
        public const string Demo="Assets/RoadGunner/Scenes/LevelEditor_Demo.unity";
        static RoadGunnerLevelEditorChecks(){EditorApplication.playModeStateChanged+=OnPlay;}
        static void OnPlay(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode&&UnityEditor.SessionState.GetBool("RGLevelChecks",false)){UnityEditor.SessionState.SetBool("RGLevelChecks",false);EditorApplication.delayCall+=()=>{var s=Object.FindAnyObjectByType<GameSession>();if(s)new RoadGunnerLevelPlayChecks().Begin(s);};}}
        [MenuItem("Tools/Road Gunner/Validate/Run Level Editor Demo Checks")]
        public static void RunPlay(){if(EditorApplication.isPlaying){var s=Object.FindAnyObjectByType<GameSession>();new RoadGunnerLevelPlayChecks().Begin(s);}else{UnityEditor.SessionState.SetBool("RGLevelChecks",true);EditorApplication.isPlaying=true;}}
        public static Vector3 Ground(LevelLayout l,float x,float z){if(!RoadGunnerTerrainBrush.GroundHit(l,new Ray(new Vector3(x,100,z),Vector3.down),out var hit))throw new InvalidOperationException("No ground");return hit.point;}
        public static string SetupDemo()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
            Directory.CreateDirectory(Folder);var lines=new List<string>();
            void Check(string n,bool pass,string detail){string line=(pass?"PASS ":"FAIL ")+n+": "+detail;lines.Add(line);File.WriteAllLines(Folder+"/AuthoringChecks.txt",lines);if(!pass)throw new InvalidOperationException(line);}
            RoadGunnerLevelEditorWindow.Open();Check("1 window opens",EditorWindow.HasOpenInstances<RoadGunnerLevelEditorWindow>(),"Tools > Road Gunner > Level Editor");
            RoadGunnerLevelAuthoring.ExtractEnvironmentPalette(null);
            // Do not persist preparation in the template; the demo alone is authored.
            EditorSceneManager.OpenScene(RoadGunnerLevelAuthoring.Template);
            var l=RoadGunnerLevelAuthoring.CreateLevel(Demo);Check("1 new level",l.session&&l.session.spawns.GroupCount==0&&l.session.spawns.buggySpawnPoints.Length==0,"Existing gameplay copied; no legacy encounters");
            float baseHeight=Ground(l,0,0).y;var mesh=l.ground.sharedMesh;int count=mesh.vertexCount;float old=Ground(l,17,20).y;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();RoadGunnerTerrainBrush.Apply(l,Ground(l,17,20),7,8,0,ReliefOperation.Raise,.15f);Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);float raised=Ground(l,17,20).y;Undo.PerformUndo();float undone=Ground(l,17,20).y;Check("6 terrain Undo",raised>old+.5f&&Mathf.Abs(old-undone)<.001f,"Raised="+raised+", restored="+undone);Undo.PerformRedo();Check("6 terrain Redo",Mathf.Abs(Ground(l,17,20).y-raised)<.001f,"MeshCollider follows restored mesh");Undo.PerformUndo();
            for(int i=0;i<18;i++)RoadGunnerTerrainBrush.Apply(l,Ground(l,0,0),18,10,baseHeight,ReliefOperation.Flatten,.15f,false);
            old=Ground(l,17,20).y;for(int i=0;i<12;i++)RoadGunnerTerrainBrush.Apply(l,Ground(l,17,20),14,2,0,ReliefOperation.Raise,.15f,false);float hill=Ground(l,17,20).y;
            float beforeLow=Ground(l,-20,-15).y;for(int i=0;i<12;i++)RoadGunnerTerrainBrush.Apply(l,Ground(l,-20,-15),14,2,0,ReliefOperation.Lower,.15f,false);float low=Ground(l,-20,-15).y;
            var beforeSmooth=mesh.vertices;RoadGunnerTerrainBrush.Apply(l,Ground(l,17,20),10,4,0,ReliefOperation.Smooth,.15f);hill=Ground(l,17,20).y;Check("6 four relief operations",hill>old+3&&low<beforeLow-3&&count==mesh.vertexCount&&!beforeSmooth.SequenceEqual(mesh.vertices),"Hill +3.6m; depression -3.6m; smooth and flatten; "+count+" vertices unchanged");
            var humanPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(RoadGunnerLevelAuthoring.Root+"/Prefabs/Wasteland Raider.prefab");var buggyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(RoadGunnerLevelAuthoring.Root+"/Prefabs/Rust Vulture Enemy Buggy.prefab");
            foreach(var p in new[]{new Vector2(0,7),new Vector2(10,-6),new Vector2(-12,12),new Vector2(26,-20)})RoadGunnerLevelAuthoring.Place(l,humanPrefab,Ground(l,p.x,p.y),Vector3.up,180,1,false,false);
            foreach(var p in new[]{new Vector2(-9,-7),new Vector2(15,9)})RoadGunnerLevelAuthoring.Place(l,buggyPrefab,Ground(l,p.x,p.y),Vector3.up,0,1,false,false);
            Check("2-3 actual enemy prefabs",l.session.spawns.authoredRaiders.Length==4&&l.session.spawns.authoredBuggies.Length==2&&l.session.spawns.authoredRaiders.All(r=>PrefabUtility.IsPartOfPrefabInstance(r)),"Four humans / two vehicles in existing target pools");
            var human=l.session.spawns.authoredRaiders[0];var buggy=l.session.spawns.authoredBuggies[0];var sharedHuman=human.tuning;var sharedBuggy=buggy.tuning;var h=(CombatTuning)RoadGunnerLevelAuthoring.MakeOverride(human);h.enemyHealth=60;h.moveSpeed=2;h.meleeDamage=12;h.attackCooldown=2;h.aggroRadius=30;EditorUtility.SetDirty(h);
            var b=(BuggyTuning)RoadGunnerLevelAuthoring.MakeOverride(buggy);b.health=160;b.maxSpeed=8;b.bulletDamage=7;b.shotsPerSecond=3;b.scrapReward=5;EditorUtility.SetDirty(b);
            Check("4 existing stat overrides",sharedHuman.enemyHealth==45&&sharedHuman.moveSpeed==3.5f&&sharedBuggy.health==105&&sharedBuggy.scrapReward==3&&human.tuning!=sharedHuman,"Shared defaults intact; independent native tuning assets");
            var rock=AssetDatabase.LoadAssetAtPath<GameObject>(RoadGunnerLevelAuthoring.Root+"/Prefabs/Environment/Rounded sandstone.prefab");var fence=AssetDatabase.LoadAssetAtPath<GameObject>(RoadGunnerLevelAuthoring.Root+"/Prefabs/Environment/Rusted corrugated barricade.prefab");var pebble=AssetDatabase.LoadAssetAtPath<GameObject>(RoadGunnerLevelAuthoring.Root+"/Prefabs/Environment/Sandstone pebble (decoration).prefab");
            RoadGunnerLevelAuthoring.Place(l,rock,Ground(l,0,15),Vector3.up,0,1,false,false);RoadGunnerLevelAuthoring.Place(l,rock,Ground(l,22,-5),Vector3.up,37,1.2f,true,false);RoadGunnerLevelAuthoring.Place(l,fence,Ground(l,-18,4),Vector3.up,15,1,false,false);RoadGunnerLevelAuthoring.Place(l,pebble,Ground(l,5,-3),Vector3.up,127,1,false,true);
            Check("5 obstacles and decoration",l.obstaclesRoot.childCount==3&&l.decorationRoot.childCount==1&&l.obstaclesRoot.GetComponentsInChildren<Collider>().All(c=>c.enabled&&c.gameObject.layer==0)&&l.decorationRoot.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Existing collider conventions and prefab connections");
            RoadGunnerLevelAuthoring.PlacePlayer(l,Ground(l,0,0),0);Check("7 player spawn",l.session.startPosition==l.playerSpawn.position&&l.playerSpawn.forward==Vector3.forward,"Existing LevelLayout -> GameSession start");
            RoadGunnerLevelAuthoring.Save(l);Check("8 scene save",File.Exists(Demo)&&!SceneManager.GetActiveScene().isDirty,"Scene / terrain / navigation / overrides saved");
            var originalTerrain=AssetDatabase.LoadAssetAtPath<Mesh>(RoadGunnerLevelAuthoring.Root+"/Meshes/Rust basin terrain.asset");Check("terrain isolation",mesh!=originalTerrain&&AssetDatabase.GetAssetPath(mesh).Contains("LevelEditor_Demo_Data"),"Prototype and Level01 mesh unchanged");
            EditorSceneManager.OpenScene(RoadGunnerLevelAuthoring.Template);EditorSceneManager.OpenScene(Demo);l=RoadGunnerLevelAuthoring.Current;RoadGunnerLevelAuthoring.Wire(l);
            Check("9 reload",l.session.spawns.authoredRaiders.Length==4&&l.session.spawns.authoredBuggies.Length==2&&l.obstaclesRoot.childCount==3&&l.session.spawns.authoredRaiders[0].tuning.enemyHealth==60&&Mathf.Abs(Ground(l,17,20).y-hill)<.2f,"All placed content, overrides and relief survive reopening");
            var validation=RoadGunnerLevelAuthoring.Validate(l);File.WriteAllLines(Folder+"/Validation.txt",validation.Select(v=>v.type+": "+v.text));Check("level validation",!validation.Any(v=>v.type==MessageType.Error),string.Join("; ",validation.Select(v=>v.type+" "+v.text)));
            var sv=SceneView.lastActiveSceneView;if(!sv)sv=EditorWindow.GetWindow<SceneView>();sv.LookAt(new Vector3(0,0,3),Quaternion.Euler(50,45,0),32,true);Selection.activeGameObject=l.gameObject;SceneView.RepaintAll();
            return string.Join("\n",lines);
        }
    }
}
