using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoadGunner.Editor
{
    /// <summary>Save presentation settings on the existing player/audio; never rebuilds world or art.</summary>
    public static class WalkerFeelInstaller
    {
        public const string Folder="Documentation/RoadGunner/WalkerFeel-2026-11-09";
        [Serializable] sealed class Balance { public string vehicle,combat,buggy,raider,boss,exploration,layout; }
        [Serializable] sealed class Report { public string scene,scope;public bool balanceUnchanged;public Balance before,after;public string[] reusedAudio;public float footfallVolume,winchVolume; }
        static Balance Snapshot(GameSession s)=>new Balance {
            vehicle=JsonUtility.ToJson(s.vehicle.tuning),combat=JsonUtility.ToJson(s.turret.tuning),
            buggy=JsonUtility.ToJson(s.spawns.buggyPool.First(b=>b).tuning),
            raider=JsonUtility.ToJson(s.spawns.pool.First(r=>r).tuning),
            boss=JsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<CombatTuning>("Assets/RoadGunner/Levels/Greenwood/Arena1/Arena 1 Boss Raider.asset")),
            exploration=JsonUtility.ToJson(s.exploration.tuning),
            layout=File.ReadAllText("Assets/RoadGunner/Levels/Greenwood/Exploration/Exploration layout.asset")
        };
        [MenuItem("Tools/Road Gunner/Player/Install Walker Weapon Feel")]
        public static void InstallBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Run from compiled Edit Mode.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scenes first.");
            var scene=EditorSceneManager.OpenScene(PlayerWalkerVisualInstaller.ScenePath);
            var s=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GameSession>(true)).Single();
            var before=Snapshot(s);var audio=s.Feedback.GetComponentInChildren<ArcadeAudio>(true);
            if(!audio||!s.vehicle.tuning.walkerDrive)throw new InvalidOperationException("Saved walker/audio are required.");
            var impact=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/RoadGunner/Audio/Impact.wav");
            var impactVariant=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/RoadGunner/Audio/Arcade/Impact_Bullet_02.wav");
            var pickup=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/RoadGunner/Audio/Pickup.wav");
            var motor=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/RoadGunner/Audio/Engine.wav");
            if(!impact||!impactVariant||!pickup||!motor)throw new InvalidOperationException("Existing project audio is missing.");
            audio.footfall.clips=new[]{impact,impactVariant};audio.footfall.Mix(.32f,.04f,.45f,0,3,7);
            audio.landing.clips=new[]{impact};audio.landing.Mix(.38f,0,.2f,.1f,1,7);
            audio.harpoonFire.clips=new[]{impactVariant};audio.harpoonFire.Mix(.30f,.02f,.3f,0,1,5);
            audio.harpoonLatch.clips=new[]{impact};audio.harpoonLatch.Mix(.24f,.02f,.2f,0,1,5);
            audio.harpoonDock.clips=new[]{pickup};audio.harpoonDock.Mix(.20f,.02f,.25f,0,1,5);
            if(!audio.winch){var go=new GameObject("Walker winch · retrieval only");go.transform.SetParent(audio.transform,false);audio.winch=go.AddComponent<AudioSource>();}
            ArcadeAudio.ConfigureLoop(audio.winch,160);audio.winch.clip=motor;audio.winchVolume=.075f;
            EditorUtility.SetDirty(audio);EditorUtility.SetDirty(audio.winch);
            var after=Snapshot(s);bool unchanged=JsonUtility.ToJson(before)==JsonUtility.ToJson(after);
            if(!unchanged)throw new InvalidOperationException("Feel authoring changed protected gameplay/layout values.");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/Authoring.json",JsonUtility.ToJson(new Report { scene=scene.path,
                scope="Existing saved walker and weapon feel; reuses original project audio and the SFX voice pool. No world/art installer and no game-balance edits.",
                before=before,after=after,balanceUnchanged=unchanged,footfallVolume=audio.footfall.volume,winchVolume=audio.winchVolume,
                reusedAudio=new[]{AssetDatabase.GetAssetPath(impact),AssetDatabase.GetAssetPath(impactVariant),AssetDatabase.GetAssetPath(pickup),AssetDatabase.GetAssetPath(motor)} },true));
            Debug.Log("WALKER_FEEL_INSTALL_OK balance/layout unchanged; foot plants + event cannon + anchored winch.");
        }
    }
}
