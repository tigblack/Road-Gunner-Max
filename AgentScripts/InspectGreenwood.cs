using System;using System.Collections.Generic;using System.IO;using System.Linq;using UnityEngine;using UnityEngine.AI;using UnityEditor;using UnityEditor.SceneManagement;using RoadGunner;using RoadGunner.Editor;
public static class InspectGreenwood {
 static List<string> report=new List<string>();static int failures;static LevelLayout l;
 static void Check(string name,bool pass,string detail){report.Add((pass?"PASS ":"FAIL ")+name+": "+detail);if(!pass)failures++;}
 public static string Main(){
 l=RoadGunnerLevelAuthoring.Current;Physics.SyncTransforms();
 var points=new Dictionary<string,Vector2>{{"start",new Vector2(0,-48)},{"entry",new Vector2(0,-33)},{"southwest camp",new Vector2(-22,-16)},{"central clearing",new Vector2(-18,6)},{"pool recovery",new Vector2(6,-12)},{"arch approach",new Vector2(6,3)},{"crossroads",new Vector2(0,22)},{"west arena",new Vector2(-33,35)},{"west gate approach",new Vector2(-40,24)},{"north market",new Vector2(-12,47)},{"north gate approach",new Vector2(0,58)},{"northeast camp",new Vector2(24,40)},{"sealed entrance approach",new Vector2(38,37)},{"tree pocket",new Vector2(41,58)}};
 NavMesh.SamplePosition(l.playerSpawn.position,out var start,3,NavMesh.AllAreas);
 foreach(var pair in points){var p=AstraZone01Builder.World(pair.Value.x,pair.Value.y);bool sample=NavMesh.SamplePosition(p,out var end,.75f,NavMesh.AllAreas);var path=new NavMeshPath();bool reachable=sample&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;Check("walkable "+pair.Key,reachable,"sample="+sample+", path="+path.status+", corners="+path.corners.Length);
 var overlaps=Physics.OverlapBox(p+Vector3.up*1.3f,new Vector3(1.15f,.5f,2.85f),Quaternion.Euler(0,45,0),1,QueryTriggerInteraction.Ignore);Check("car clearance "+pair.Key,overlaps.Length==0,string.Join(",",overlaps.Select(c=>c.transform.parent?c.transform.parent.name:c.name))); }
 foreach(var pair in new[]{new KeyValuePair<string,Vector2>("snow",new Vector2(0,71)),new KeyValuePair<string,Vector2>("orange",new Vector2(-58,24))}){var p=AstraZone01Builder.World(pair.Value.x,pair.Value.y);var path=new NavMeshPath();bool end=NavMesh.SamplePosition(p,out var hit,2,NavMesh.AllAreas);bool connected=end&&NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;Check("locked "+pair.Key+" biome",!connected,"outside path="+path.status);}
 var validation=RoadGunnerLevelAuthoring.Validate(l);Check("editor validation",!validation.Any(a=>a.type!=MessageType.Info),string.Join("; ",validation.Select(a=>a.type+": "+a.text)));
 Check("13 authored enemies",l.session.spawns.authoredRaiders.Length==13&&l.session.spawns.authoredRaiders.All(r=>r.tuning&&r.agent&&r.visual&&r.torso&&PrefabUtility.IsPartOfPrefabInstance(r)),"Prefab variants and existing AI linked");
 Check("camera north",Vector3.Dot(Vector3.ProjectOnPlane(l.session.follow.transform.forward,Vector3.up).normalized,AstraZone01Builder.World(0,1))>.99f,"Existing 45 degree yaw respected");
 Check("private terrain",UnityEditor.AssetDatabase.GetAssetPath(l.ground.sharedMesh).Contains("Level01_Greenwood_Data"),UnityEditor.AssetDatabase.GetAssetPath(l.ground.sharedMesh));
 Check("saved scene",!UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,"dirty="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty);
 File.WriteAllLines(AstraZone01Builder.Report+"/StaticChecks.txt",report);return "Failures="+failures+"\n"+string.Join("\n",report);
 }
}