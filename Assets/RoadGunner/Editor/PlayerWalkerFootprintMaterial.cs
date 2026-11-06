using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Flat vertex-alpha footprint material. The mechanical sole silhouette lives in the runtime mesh.</summary>
    public static class PlayerWalkerFootprintMaterial
    {
        public const string AssetPath="Assets/RoadGunner/Art/PlayerWalker/Walker footprints.mat";
        public static Material Ensure()
        {
            var shader=Shader.Find("RoadGunner/Sand Tracks");
            if(!shader)throw new InvalidOperationException("The existing RoadGunner/Sand Tracks alpha shader is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));AssetDatabase.Refresh();
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
            if(!material){material=new Material(shader){name="Walker discrete mechanical footprints"};AssetDatabase.CreateAsset(material,AssetPath);}
            material.shader=shader;material.SetTexture("_BaseMap",Texture2D.whiteTexture);
            material.SetTextureScale("_BaseMap",Vector2.one);material.SetTextureOffset("_BaseMap",Vector2.zero);
            material.SetColor("_BaseColor",new Color(.43f,.31f,.23f,1.2f));material.renderQueue=2990;
            material.enableInstancing=false;EditorUtility.SetDirty(material);return material;
        }
    }
}
