using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace RoadGunner.Editor
{
    public static class RangeCombatBuilder
    {
        const string Root = PrototypeBuilder.Root;
        const string PrefabPath = Root + "/Prefabs/Rust Vulture Enemy Buggy.prefab";
        [MenuItem("Tools/Road Gunner/Combat/Install Shared 10m Range And Buggy Guns")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene first.");
            var combat = AssetDatabase.LoadAssetAtPath<CombatTuning>(Root + "/Settings/Combat and population.asset");
            combat.range = 10; EditorUtility.SetDirty(combat);
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            AddGun(contents.GetComponent<EnemyBuggy>());
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath); PrefabUtility.UnloadPrefabContents(contents);
            foreach (string path in new[] { PrototypeBuilder.ScenePath, Level01Builder.ScenePath })
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path);
                AddToSession(Object.FindAnyObjectByType<GameSession>());
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets(); PrototypeBuilder.SetPortraitGameView(540, 960);
            Debug.Log("ROAD GUNNER: shared 10m weapons, rusty roof guns and ground range circle saved in both scenes.");
        }
        public static void AddToSession(GameSession session)
        {
            var indicator = session.vehicle.GetComponent<AttackRangeIndicator>();
            if (!indicator) indicator = session.vehicle.gameObject.AddComponent<AttackRangeIndicator>();
            indicator.turret = session.turret;
            foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
                if (c.name.StartsWith("Golden dunes") || c.name.StartsWith("Rolling dunes")) { indicator.ground = c; break; }
            if (!indicator.ground) throw new InvalidOperationException("Could not find the drivable terrain collider.");
            string path = Root + "/Materials/Player attack range.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("RoadGunner/Sand Tracks")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", Color.white); mat.renderQueue = 3001; EditorUtility.SetDirty(mat);
            indicator.material = mat;
            if (session.spawns.buggyPool != null) foreach (var buggy in session.spawns.buggyPool) if (buggy)
            {
                AddGun(buggy);
                PrefabUtility.RecordPrefabInstancePropertyModifications(buggy);
            }
            EditorUtility.SetDirty(indicator);
            DeathLootBuilder.AddToSession(session);
        }
        public static void AddGun(EnemyBuggy buggy)
        {
            var gun = buggy.GetComponent<BuggyGun>();
            if (gun) { buggy.gun = gun; gun.buggy = buggy; return; }
            gun = buggy.gameObject.AddComponent<BuggyGun>(); buggy.gun = gun; gun.buggy = buggy;
            gun.hitMask = 1 | (1 << LayerMask.NameToLayer("RG_Vehicle"));
            var rust = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Buggy deep rust.mat");
            var steel = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Buggy scratched tube steel.mat");
            var cream = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Bone armor.mat");
            var flashMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Hot brass unlit.mat");
            var mounting = new GameObject("Rust Vulture roof gun").transform;
            mounting.SetParent(buggy.transform.Find("Replaceable buggy visuals"), false);
            mounting.localPosition = new Vector3(0, 1.67f, -.30f);
            Part("Bolted gun mount", mounting, Vector3.zero, new Vector3(.66f, .12f, .52f), rust);
            gun.yaw = new GameObject("360 degree gun yaw").transform; gun.yaw.SetParent(mounting, false);
            Part("Scratched rotating bearing", gun.yaw, new Vector3(0, .13f, 0), new Vector3(.44f, .065f, .44f), steel, PrimitiveType.Cylinder);
            gun.pitch = new GameObject("Gun elevation").transform; gun.pitch.SetParent(gun.yaw, false); gun.pitch.localPosition = new Vector3(0, .24f, 0);
            Part("Rusted machine gun receiver", gun.pitch, Vector3.zero, new Vector3(.26f, .22f, .55f), steel);
            Part("Cream receiver stripe", gun.pitch, new Vector3(.135f, 0, -.12f), new Vector3(.016f, .16f, .11f), cream);
            Part("Rusty ammunition box", gun.pitch, new Vector3(-.28f, -.04f, -.11f), new Vector3(.30f, .24f, .29f), rust);
            var barrel = Part("Heavy gun barrel", gun.pitch, new Vector3(0, 0, .53f), new Vector3(.105f, .35f, .105f), steel, PrimitiveType.Cylinder);
            barrel.localRotation = Quaternion.Euler(90, 0, 0);
            Part("Muzzle brake", gun.pitch, new Vector3(0, 0, .91f), new Vector3(.15f, .14f, .16f), steel);
            gun.muzzle = new GameObject("Hostile muzzle").transform; gun.muzzle.SetParent(gun.pitch, false); gun.muzzle.localPosition = new Vector3(0, 0, 1.02f);
            var flash = new GameObject("Hostile muzzle flash", typeof(MeshFilter), typeof(MeshRenderer)); flash.transform.SetParent(gun.muzzle, false);
            flash.transform.localRotation = Quaternion.Euler(90, 0, 0); flash.transform.localScale = new Vector3(.28f, .52f, .28f);
            flash.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Spike.asset");
            var renderer = flash.GetComponent<MeshRenderer>(); renderer.sharedMaterial = flashMat; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gun.flash = flash.transform; flash.SetActive(false);
            SetLayer(mounting, buggy.gameObject.layer);
            EditorUtility.SetDirty(buggy); EditorUtility.SetDirty(gun); EditorUtility.SetDirty(buggy.tuning);
        }
        static Transform Part(string name, Transform parent, Vector3 position, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false); obj.transform.localPosition = position; obj.transform.localScale = size;
            Object.DestroyImmediate(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial = material;
            if (type == PrimitiveType.Cube) obj.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Scrap Rush beveled block.asset");
            return obj.transform;
        }
        static void SetLayer(Transform parent, int layer) { parent.gameObject.layer = layer; foreach (Transform child in parent) SetLayer(child, layer); }
    }
}
