using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using RoadGunner;
public static class InspectInstalledHero
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first");
        var previous=SceneManager.GetActiveScene().path;var lines=new List<string>();
        string root="Assets/RoadGunner/Scenes/";
        foreach(string name in new[]{"RoadGunner_Prototype","Level01_Wasteland"})
        {
            var scene=EditorSceneManager.OpenScene(root+name+".unity");var s=UnityEngine.Object.FindAnyObjectByType<GameSession>();var h=s.vehicle.GetComponent<HarpoonCollector>();
            bool refs=h&&h.session==s&&h.pickups==s.pickups&&h.pivot&&h.projectile&&h.spawnPoint&&h.cable;
            int triangles=0,activeTriangles=0;
            foreach(var mf in s.vehicle.GetComponentsInChildren<MeshFilter>(true))
            {triangles+=mf.sharedMesh.triangles.Length/3;if(mf.gameObject.activeInHierarchy)activeTriangles+=mf.sharedMesh.triangles.Length/3;}
            var materials=s.vehicle.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Select(m=>m.name);
            lines.Add(name+": refs="+refs+", session count="+UnityEngine.Object.FindObjectsByType<GameSession>().Length+", vehicle triangles active/all="+activeTriangles+"/"+triangles+", shared materials="+string.Join(", ",materials));
            if(!refs||s.vehicle.wheelVisuals.Any(t=>!t)||s.vehicle.wheels.Length!=4||s.pickups.items.Length!=60)throw new InvalidOperationException("Unwired scene "+name);
            if(s.turret.tuning.shotsPerSecond!=5||s.turret.tuning.bulletDamage!=15||s.turret.Range!=10)throw new InvalidOperationException("Gun settings changed");
            var noColliders=h.pivot.GetComponentsInChildren<Collider>().Length==0;
            if(!noColliders)throw new InvalidOperationException("Harpoon has physical colliders");
            lines.Add("Prefab GUID retained: "+AssetDatabase.AssetPathToGUID("Assets/RoadGunner/Prefabs/Dust Jackal.prefab")+"; wheel radius="+s.vehicle.tuning.wheelRadius+"; pristine saved scene="+!scene.isDirty);
        }
        EditorSceneManager.OpenScene(previous);var result=string.Join("\n",lines);File.WriteAllText("Documentation/RoadGunner/Hero-Harpoon-2026-10-08/SavedScenes.txt",result);return result;
    }
}
