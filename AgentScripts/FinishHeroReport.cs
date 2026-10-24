using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;
public static class FinishHeroReport
{
    public static string Main()
    {
        const string source="Assets/Documentation/RoadGunner/Hero-Harpoon-2026-10-08";
        const string destination="Documentation/RoadGunner/Hero-Harpoon-2026-10-08/final-gameplay-HUD.png";
        File.Copy(source+"/final-gameplay-HUD.png",destination,true);
        AssetDatabase.DeleteAsset(source);
        foreach(string path in new[]{"Assets/Documentation/RoadGunner","Assets/Documentation"})
            if(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any())AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        var scene=SceneManager.GetActiveScene();return "HUD capture saved outside Assets. Open scene: "+scene.path+", dirty="+scene.isDirty;
    }
}
