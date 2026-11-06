using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadGunner.Editor
{
    /// <summary>Explicit player-only character handling and detailed walker presentation.</summary>
    public static class PlayerWalkerInstantInstaller
    {
        [Serializable] sealed class Report
        {
            public string scene, tuningAsset, scope, before, after;
            public bool onlyWalkerFieldsChanged;
        }
        [MenuItem("Tools/Road Gunner/Player/Install Instant Walker")]
        public static void InstallBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install the walker from compiled Edit Mode.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the open scene before authoring.");
            var scene=EditorSceneManager.OpenScene(PlayerWalkerVisualInstaller.ScenePath);
            var session=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GameSession>(true)).Single();
            var tuning=session.vehicle.tuning;
            string path=AssetDatabase.GetAssetPath(tuning);
            if(path!="Assets/RoadGunner/Levels/Greenwood/Arena1/Arena 1 vehicle handling.asset")
                throw new InvalidOperationException("Unexpected player tuning asset: "+path);
            var baseline=ScriptableObject.Instantiate(tuning);
            string before=JsonUtility.ToJson(tuning,true);
            tuning.walkerDrive=true;tuning.walkerCollisionSkin=.02f;
            baseline.walkerDrive=tuning.walkerDrive;baseline.walkerCollisionSkin=tuning.walkerCollisionSkin;
            bool unchanged=JsonUtility.ToJson(baseline)==JsonUtility.ToJson(tuning);
            UnityEngine.Object.DestroyImmediate(baseline);
            if(!unchanged)throw new InvalidOperationException("An unrelated tuning value changed.");
            EditorUtility.SetDirty(tuning);
            PlayerWalkerVisualInstaller.Apply(session);
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory(PlayerWalkerVisualInstaller.ReportFolder);
            File.WriteAllText(PlayerWalkerVisualInstaller.ReportFolder+"/Handling.json",JsonUtility.ToJson(new Report {
                scene=scene.path,tuningAsset=path,scope="Only the saved player enables instant grounded character movement. Existing maximum speed, input shaping, health, combat, rewards, world, navigation and enemy profiles remain unchanged.",
                before=before,after=JsonUtility.ToJson(tuning,true),onlyWalkerFieldsChanged=unchanged
            },true));
            Debug.Log("PLAYER_WALKER_INSTANT_INSTALL_OK "+path+"; maxSpeed="+tuning.maxSpeed+"; player-only profile.");
        }
    }
}
