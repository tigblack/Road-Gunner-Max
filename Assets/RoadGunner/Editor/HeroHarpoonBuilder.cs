using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using RoadGunner;
namespace RoadGunner.Editor
{
    public static class HeroHarpoonBuilder
    {
        const string Root="Assets/RoadGunner", PrefabPath=Root+"/Prefabs/Dust Jackal.prefab";
        const float GroundOffset=.481f;
        public class Submesh { public int[] indices; }
        public class Part { public string name;public float[] origin;public float[][] vertices,normals,uvs;public Submesh[] submeshes;public string[] materials; }
        public class Source { public Part[] objects;public float[] harpoonAxis,harpoonMuzzle; }
        static Source source;
        static Dictionary<string,Mesh> meshes;
        static Dictionary<string,Part> parts;
        static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
        [MenuItem("Tools/Road Gunner/Player/Install Muscle Pickup And Companion Harpoon")]
        public static void Install() { Main(); }
        public static string Main()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before installing.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the open scene before installing: "+SceneManager.GetSceneAt(i).path);
            string previous=SceneManager.GetActiveScene().path;
            LoadArt();
            var contents=PrefabUtility.LoadPrefabContents(PrefabPath);
            try{UpdateVehicle(contents.GetComponent<VehicleController>());PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath);}
            finally{PrefabUtility.UnloadPrefabContents(contents);}
            var handling=AssetDatabase.LoadAssetAtPath<VehicleTuning>(Root+"/Settings/Vehicle handling.asset");handling.wheelRadius=GroundOffset;EditorUtility.SetDirty(handling);
            var installed=new List<string>();
            foreach(var path in new[]{Root+"/Scenes/RoadGunner_Prototype.unity",Root+"/Scenes/Level01_Wasteland.unity"})
            {
                var scene=EditorSceneManager.OpenScene(path);var session=Object.FindAnyObjectByType<GameSession>();
                if(!session)throw new InvalidOperationException("No GameSession in "+path);
                WireSession(session);EditorSceneManager.SaveScene(scene);installed.Add(path);
            }
            AssetDatabase.SaveAssets();
            if(!string.IsNullOrEmpty(previous))EditorSceneManager.OpenScene(previous);
            string report="Updated existing Dust Jackal prefab and both gameplay scenes. Existing turret, session, resource HUD, enemy pools, effects, input and vehicle physics retained. Nine custom low-poly meshes: "+source.objects.Sum(p=>p.submeshes.Sum(s=>s.indices.Length/3))+" triangles. Harpoon configured, pooled visuals connected.\n"+string.Join("\n",installed);
            Directory.CreateDirectory("Documentation/RoadGunner/Hero-Harpoon-2026-10-08");File.WriteAllText("Documentation/RoadGunner/Hero-Harpoon-2026-10-08/Install.txt",report);Debug.Log(report);return report;
        }
        static void LoadArt()
        {
            source=JsonConvert.DeserializeObject<Source>(File.ReadAllText(Root+"/Art/HeroVehicle_01/HeroVehicle_source.json"));
            parts=source.objects.ToDictionary(p=>p.name);meshes=new Dictionary<string,Mesh>();
            Directory.CreateDirectory(Root+"/Meshes/HeroVehicle_01");AssetDatabase.Refresh();
            foreach(var p in source.objects)
            {
                var mesh=new Mesh{name=p.name};mesh.vertices=p.vertices.Select(V).ToArray();mesh.normals=p.normals.Select(V).ToArray();mesh.uv=p.uvs.Select(v=>new Vector2(v[0],v[1])).ToArray();
                mesh.subMeshCount=p.submeshes.Length;for(int i=0;i<p.submeshes.Length;i++)mesh.SetTriangles(p.submeshes[i].indices,i);mesh.RecalculateBounds();
                string path=Root+"/Meshes/HeroVehicle_01/"+p.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
                meshes[p.name]=mesh;
            }
        }
        // Called by the existing prototype builder; current scenes are installed in place by Main.
        public static void ApplyToSession(GameSession session)
        {
            LoadArt();UpdateVehicle(session.vehicle);WireSession(session);
            session.vehicle.tuning.wheelRadius=GroundOffset;EditorUtility.SetDirty(session.vehicle.tuning);
        }
        static Material Material(string name)
        {
            var original=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
            if(!original)throw new InvalidOperationException("Missing shared material "+name);
            if(name!="Smoked teal glass")return original;
            string path=Root+"/Materials/Hero glazed teal glass.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(original);AssetDatabase.CreateAsset(mat,path);}
            Color c=original.GetColor("_BaseColor");c.a=.22f;mat.SetColor("_BaseColor",c);mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);
            mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.DisableKeyword("_ALPHATEST_ON");mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType","Transparent");mat.renderQueue=(int)RenderQueue.Transparent;mat.SetShaderPassEnabled("ShadowCaster",false);mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
        }
        static Transform Group(string name,Transform parent,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static Transform MeshPart(string name,Transform parent)
        {
            var p=parts[name];var t=Group(name,parent,V(p.origin));var filter=t.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=meshes[name];
            var renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=p.materials.Select(Material).ToArray();return t;
        }
        static void Layer(Transform t,int layer){t.gameObject.layer=layer;foreach(Transform child in t)Layer(child,layer);}
        static void UpdateVehicle(VehicleController car)
        {
            var turret=car.GetComponent<TurretController>();if(!turret||!turret.yaw||!turret.pitch||!turret.muzzle)throw new InvalidOperationException("Existing gun hierarchy is missing.");
            var visual=car.transform.Find("Replaceable visuals");if(!visual)throw new InvalidOperationException("Missing replaceable visual root.");
            // Preserve the functional gun objects if this installer is being repeated.
            var yaw=turret.yaw;yaw.SetParent(car.transform,true);
            var bearing=car.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Turret bearing");if(bearing)bearing.SetParent(car.transform,true);
            foreach(var child in visual.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            visual.localPosition=Vector3.down*GroundOffset;visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
            MeshPart("hero_vehicle_body",visual);MeshPart("hero_driver_body",visual);
            var rear=Group("RearCompanionArea",visual,Vector3.zero);MeshPart("hero_vehicle_rear_platform",rear);MeshPart("hero_companion_body",rear);
            var mount=Group("HarpoonMount",rear,V(parts["hero_companion_harpoon"].origin));var pivot=Group("HarpoonPivot",mount,Vector3.zero);
            var rest=Quaternion.LookRotation(V(source.harpoonAxis));pivot.localRotation=rest;
            var launcher=MeshPart("hero_companion_harpoon",pivot);launcher.localPosition=Vector3.zero;launcher.localRotation=Quaternion.Inverse(rest);
            var spawn=Group("HarpoonSpawnPoint",pivot,Quaternion.Inverse(rest)*V(source.harpoonMuzzle));
            var collect=Group("HarpoonCollectionPoint",mount,Vector3.down*.32f);
            var collector=car.GetComponent<HarpoonCollector>();if(!collector)collector=car.gameObject.AddComponent<HarpoonCollector>();
            collector.pivot=pivot;collector.spawnPoint=spawn;collector.collectionPoint=collect;
            var projectile=Group("HarpoonProjectile",rear,Vector3.zero);projectile.gameObject.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Spike.asset");
            projectile.gameObject.AddComponent<MeshRenderer>().sharedMaterial=Material("Bone armor");projectile.localScale=new Vector3(.24f,.60f,.24f);
            // Existing Spike mesh points +Y. Put the mesh under a +Z-facing moving root.
            var tipRoot=Group("HarpoonTip",rear,Vector3.zero);projectile.SetParent(tipRoot,false);projectile.localRotation=Quaternion.Euler(90,0,0);collector.projectile=tipRoot;tipRoot.gameObject.SetActive(false);
            var cableRoot=Group("HarpoonCable",rear,Vector3.zero);var line=cableRoot.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=Material("Bone armor");
            line.useWorldSpace=true;line.positionCount=2;line.widthMultiplier=.065f;line.numCapVertices=2;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;collector.cable=line;
            collector.collectionRadius=8;collector.harpoonLaunchSpeed=36;collector.harpoonRetractSpeed=30;collector.delayBetweenCollections=.08f;collector.searchInterval=.05f;collector.aimSpeed=960;collector.maxTetherLength=20;
            car.wheelVisuals=new Transform[4];string[] suffix={"fl","fr","rl","rr"};
            for(int i=0;i<4;i++)
            {
                car.wheelVisuals[i]=MeshPart("hero_vehicle_wheel_"+suffix[i],visual);
                var w=car.wheels[i];w.transform.localPosition=new Vector3(i%2==0?-1.035f:1.035f,.15f,i<2?1.71f:-1.34f);w.radius=GroundOffset;
            }
            var roof=Group("RoofMachineGun",visual,new Vector3(.53f,1.965f,-.15f));
            if(bearing){bearing.SetParent(roof,false);bearing.localPosition=new Vector3(0,.04f,0);bearing.localRotation=Quaternion.identity;bearing.localScale=new Vector3(.58f,.05f,.58f);}
            yaw.SetParent(roof,false);yaw.name="GunPivot";yaw.localPosition=new Vector3(0,.23f,0);yaw.localRotation=Quaternion.identity;
            turret.pitch.name="GunBarrel"; // Reference, receiver, barrel, muzzle, tuning and firing code remain the same.
            var boxes=car.GetComponents<BoxCollider>();boxes[0].center=new Vector3(0,.18f,.45f);boxes[0].size=new Vector3(1.88f,.65f,5.38f);
            if(boxes.Length>1){boxes[1].center=new Vector3(0,1.025f,.175f);boxes[1].size=new Vector3(1.55f,.90f,1.45f);}
            var ram=car.GetComponentInChildren<RamSensor>();if(ram)ram.transform.localPosition=new Vector3(0,.40f,3.20f);
            var health=car.GetComponent<VehicleHealth>();if(health)health.bodyPanels=new[]{visual.Find("hero_vehicle_body").GetComponent<Renderer>(),rear.Find("hero_vehicle_rear_platform").GetComponent<Renderer>()};
            Layer(visual,car.gameObject.layer);EditorUtility.SetDirty(car);EditorUtility.SetDirty(turret);EditorUtility.SetDirty(collector);
        }
        public static void WireSession(GameSession session)
        {
            var car=session.vehicle;var collector=car.GetComponent<HarpoonCollector>();
            if(!collector)throw new InvalidOperationException("Prefab update did not provide a HarpoonCollector.");
            collector.session=session;collector.pickups=session.pickups;if(PrefabUtility.IsPartOfPrefabInstance(collector))PrefabUtility.RecordPrefabInstancePropertyModifications(collector);EditorUtility.SetDirty(collector);
            string[] suffix={"fl","fr","rl","rr"};var visual=car.transform.Find("Replaceable visuals");
            for(int i=0;i<4;i++)car.wheelVisuals[i]=visual.Find("hero_vehicle_wheel_"+suffix[i]);
            session.health.bodyPanels=new[]{visual.Find("hero_vehicle_body").GetComponent<Renderer>(),visual.Find("RearCompanionArea/hero_vehicle_rear_platform").GetComponent<Renderer>()};
            if(PrefabUtility.IsPartOfPrefabInstance(car))PrefabUtility.RecordPrefabInstancePropertyModifications(car);
            if(PrefabUtility.IsPartOfPrefabInstance(session.health))PrefabUtility.RecordPrefabInstancePropertyModifications(session.health);
            EditorUtility.SetDirty(car);EditorUtility.SetDirty(session.health);
            if(session.pickups.gearVisuals!=null&&session.pickups.gearVisuals.Length!=session.pickups.items.Length)throw new InvalidOperationException("Existing Metal Gear pool is not wired.");
        }
    }
}
