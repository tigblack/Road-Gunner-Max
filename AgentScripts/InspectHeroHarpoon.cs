using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using RoadGunner;
public static class InspectHeroHarpoon
{
    public static string Main()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<GameSession>();
        var lines=new System.Collections.Generic.List<string>();
        for(int i=0;i<SceneManager.sceneCount;i++) {var sc=SceneManager.GetSceneAt(i);lines.Add("Scene: "+sc.path+" dirty="+sc.isDirty);}
        if(!s)return string.Join("\n",lines)+"\nNo session";
        lines.Add("Vehicle prefab: "+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.vehicle));
        lines.Add("Vehicle root: "+s.vehicle.transform.position+" rotation "+s.vehicle.transform.eulerAngles);
        lines.Add("Tuning: "+AssetDatabase.GetAssetPath(s.vehicle.tuning));
        lines.Add("Turret: yaw "+s.turret.yaw.localPosition+" pitch "+s.turret.pitch.localPosition+" muzzle "+s.turret.muzzle.localPosition);
        lines.Add("Combat tuning: "+EditorJsonUtility.ToJson(s.turret.tuning));
        lines.Add("Camera: "+s.follow.GetComponent<Camera>().orthographicSize+" pitch "+s.follow.transform.eulerAngles);
        lines.Add("Pools: raiders="+s.spawns.pool.Length+" buggies="+s.spawns.buggyPool.Length+" pickups="+s.pickups.items.Length+" gear="+s.pickups.gearVisuals.Length);
        foreach(var c in s.vehicle.GetComponentsInChildren<Collider>())lines.Add("Collider: "+c.name+" "+c.GetType().Name+" pos="+c.transform.localPosition+" trigger="+c.isTrigger);
        foreach(Transform t in s.vehicle.transform)lines.Add("Child: "+t.name);
        Directory.CreateDirectory("Documentation/RoadGunner/Hero-Harpoon-2026-10-08");
        var result=string.Join("\n",lines);File.WriteAllText("Documentation/RoadGunner/Hero-Harpoon-2026-10-08/Before.txt",result);return result;
    }
}
