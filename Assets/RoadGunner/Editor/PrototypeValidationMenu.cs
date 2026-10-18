using UnityEngine;
using UnityEditor;
namespace RoadGunner.Editor
{
    public static class PrototypeValidationMenu
    {
        [MenuItem("Tools/Road Gunner/Validate/Run Ranged Tactics Explosion And Loot Checks")]
        public static void DeathLoot(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session)return;
            var check=session.GetComponent<RangedDeathLootChecks>();if(!check)check=session.gameObject.AddComponent<RangedDeathLootChecks>();check.Begin();
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Shared Range And Buggy Gun Checks")]
        public static void RangeCombat(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session)return;
            var check=session.GetComponent<CombatRangeChecks>();if(!check)check=session.gameObject.AddComponent<CombatRangeChecks>();check.Begin();
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Enemy Buggy Checks")]
        public static void Buggy(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session||session.spawns.buggyPool==null||session.spawns.buggyPool.Length==0){Debug.LogWarning("No enemy buggies in this scene.");return;}
            var check=session.GetComponent<EnemyBuggyChecks>();if(!check)check=session.gameObject.AddComponent<EnemyBuggyChecks>();check.Begin();
        }
        [MenuItem("Tools/Road Gunner/Level 01/Validate Prepared Encounter Markers")]
        public static void Level01Markers(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode in Level01_Wasteland first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();
            if(!session || !Object.FindAnyObjectByType<LevelLayout>()){Debug.LogWarning("Open the prepared Level01_Wasteland scene first.");return;}
            var check=session.GetComponent<Level01PreparationChecks>();if(!check)check=session.gameObject.AddComponent<Level01PreparationChecks>();check.Begin();
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Play Mode Checks")]
        public static void Checks(){Run(false);}
        [MenuItem("Tools/Road Gunner/Validate/Run Five Minute Session")]
        public static void Endurance(){Run(true);}
        [MenuItem("Tools/Road Gunner/Validate/Run Joystick Heading Checks")]
        public static void JoystickHeading(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session)return;
            Application.runInBackground=true;
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            var check=session.GetComponent<JoystickHeadingChecks>();if(!check)check=session.gameObject.AddComponent<JoystickHeadingChecks>();check.Begin();
        }
        [MenuItem("Tools/Road Gunner/Validate/Run Melee and Touch Checks")]
        public static void Supplemental(){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session)return;
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            var check=session.GetComponent<SupplementalChecks>();if(!check)check=session.gameObject.AddComponent<SupplementalChecks>();check.Begin();
        }
        static void Run(bool endurance){
            if(!EditorApplication.isPlaying){Debug.LogWarning("Open RoadGunner_Prototype and enter Play mode first.");return;}
            var session=Object.FindAnyObjectByType<GameSession>();if(!session){Debug.LogWarning("No Road Gunner session in the current scene.");return;}
            Application.runInBackground=true;
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            var checks=session.GetComponent<PrototypeChecks>();if(!checks)checks=session.gameObject.AddComponent<PrototypeChecks>();checks.Begin(endurance);
        }
        [MenuItem("Tools/Road Gunner/Game View/Portrait 9 by 16")]
        public static void Portrait(){PrototypeBuilder.SetPortraitGameView(540,960);}
        [MenuItem("Tools/Road Gunner/Game View/Tall Portrait 9 by 19.5")]
        public static void TallPortrait(){PrototypeBuilder.SetPortraitGameView(540,1170);}
    }
}
