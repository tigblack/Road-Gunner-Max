using UnityEditor;
using UnityEngine;
namespace RoadGunner.Editor
{
    public static class HarpoonValidationMenu
    {
        [MenuItem("Tools/Road Gunner/Validate/Run Companion Harpoon Checks")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play mode in a Road Gunner gameplay scene first.");return;}
            var s=Object.FindAnyObjectByType<GameSession>();if(!s)return;
            var check=s.GetComponent<HarpoonCollectionChecks>();if(!check)check=s.gameObject.AddComponent<HarpoonCollectionChecks>();check.Begin();
        }
    }
}
