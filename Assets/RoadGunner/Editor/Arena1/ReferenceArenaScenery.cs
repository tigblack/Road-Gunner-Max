using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reuses the saved finished wasteland models/materials. No art generator is rerun.</summary>
    public sealed class ReferenceArenaScenery
    {
        public sealed class Template { public Mesh mesh; public Material[] materials; }
        [Serializable] public sealed class Record
        {
            public string id,category,handling,meshPath;
            public Vector3 position,size;
            public int vertices,indices;
        }
        readonly Dictionary<string,Template> templates=new Dictionary<string,Template>();
        readonly Dictionary<Material,Material> environmentMaterials=new Dictionary<Material,Material>();
        readonly List<DecorationCrushChunk> chunks=new List<DecorationCrushChunk>();
        public readonly List<Record> records=new List<Record>();
        readonly string folder;
        public ReferenceArenaScenery(string assetFolder) { folder=assetFolder; }
        static readonly string[] Folders={"Assets/RoadGunner/Art/ConceptWasteland/Prefabs/","Assets/RoadGunner/Levels/WastelandRestored/Prefabs/"};

        public Template Model(string category)
        {
            if(templates.TryGetValue(category,out var found))return found;
            var prefab=Folders.Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p+category+".prefab")).FirstOrDefault(p=>p);
            if(!prefab)throw new InvalidOperationException("Missing saved wasteland model: "+category);
            var instance=Object.Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);instance.transform.localScale=Vector3.one;
                var combine=new List<CombineInstance>();var materials=new List<Material>();
                foreach(var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!filter.sharedMesh)continue;
                    var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||!renderer.enabled)continue;
                    if(!filter.sharedMesh.isReadable)throw new InvalidOperationException("Saved model must be readable: "+category+"/"+filter.name);
                    for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                    {
                        combine.Add(new CombineInstance { mesh=filter.sharedMesh,subMeshIndex=sub,transform=instance.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix });
                        var original=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];
                        if(!original)throw new InvalidOperationException("Missing material: "+category);
                        materials.Add(EnvironmentMaterial(original));
                    }
                }
                if(combine.Count==0)throw new InvalidOperationException("Saved model has no visible triangle mesh: "+category);
                var mesh=new Mesh { name="Reference "+category,indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(combine.ToArray(),false,true);mesh.RecalculateBounds();
                mesh=ReferenceArenaInstaller.SaveMesh(folder+"/Model "+category+".asset",mesh);
                found=new Template {mesh=mesh,materials=materials.ToArray()};templates.Add(category,found);return found;
            }
            finally{Object.DestroyImmediate(instance);}
        }

        Material EnvironmentMaterial(Material original)
        {
            if(original.shader&&original.shader.name==ArcadeVisualPalette.ToonShaderName)return original;
            if(environmentMaterials.TryGetValue(original,out var existing))return existing;
            if(!original.shader||original.shader.name!="Universal Render Pipeline/Lit")return original;
            var shader=Shader.Find(ArcadeVisualPalette.ToonShaderName);if(!shader)throw new InvalidOperationException("The existing Arcade Toon shader is required.");
            string suffix=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            if(string.IsNullOrEmpty(suffix))suffix=original.name.Replace('/','_');
            string path=folder+"/Environment material "+suffix+".mat";
            var converted=new Material(original);converted.name="Reference "+original.name;ArcadeVisualPalette.ToToon(converted,shader);
            var saved=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(saved){EditorUtility.CopySerialized(converted,saved);Object.DestroyImmediate(converted);EditorUtility.SetDirty(saved);}
            else{AssetDatabase.CreateAsset(converted,path);saved=converted;}
            environmentMaterials.Add(original,saved);return saved;
        }

        public GameObject Place(Transform parent,string id,string category,Vector2 w,Vector3 targetSize,float yaw,bool soft=false,DecorationCrushKind kind=DecorationCrushKind.Metal)
        {
            var model=Model(category);var root=new GameObject(id);root.transform.SetParent(parent,false);
            root.transform.SetPositionAndRotation(ReferenceArenaInstaller.World(w),Quaternion.Euler(0,45+yaw,0));
            var size=model.mesh.bounds.size;
            root.transform.localScale=new Vector3(targetSize.x/Mathf.Max(.01f,size.x),targetSize.y/Mathf.Max(.01f,size.y),targetSize.z/Mathf.Max(.01f,size.z));
            root.transform.position-=Vector3.up*(model.mesh.bounds.min.y*root.transform.localScale.y);
            var filter=root.AddComponent<MeshFilter>();filter.sharedMesh=model.mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterials=model.materials;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            int indices=0;for(int s=0;s<model.mesh.subMeshCount;s++)indices+=checked((int)model.mesh.GetIndexCount(s));
            if(soft)
            {
                GameObjectUtility.SetStaticEditorFlags(root,StaticEditorFlags.OccludeeStatic);
                var probe=new GameObject("Vehicle crush query (layer 12)");probe.transform.SetParent(root.transform,false);probe.layer=12;
                var box=probe.AddComponent<BoxCollider>();box.center=model.mesh.bounds.center;box.size=model.mesh.bounds.size;box.isTrigger=true;
                var chunk=root.AddComponent<DecorationCrushChunk>();chunk.source=filter;
                chunk.items=new[] {new DecorationCrushChunk.Item {id="reference/"+id,kind=kind,probe=box,localBounds=model.mesh.bounds,rangeStarts=new[]{0},rangeCounts=new[]{indices}}};
                chunks.Add(chunk);
            }
            else
            {
                var collision=root.AddComponent<MeshCollider>();collision.sharedMesh=model.mesh;collision.convex=false;
                root.layer=0;GameObjectUtility.SetStaticEditorFlags(root,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
            }
            records.Add(new Record {id="reference/"+id,category=category,handling=soft?"soft":"hard",meshPath=AssetDatabase.GetAssetPath(model.mesh),
                position=root.transform.position,size=renderer.bounds.size,vertices=model.mesh.vertexCount,indices=indices});
            return root;
        }

        public void Bind(ArenaDecorationCrush manager,GameSession session)
        {
            if(!manager)throw new InvalidOperationException("The existing scenery-crush manager is required.");
            manager.session=session;manager.chunks=chunks.ToArray();manager.probeMask=1<<12;
            manager.RebuildRegistry();EditorUtility.SetDirty(manager);
            if(manager.RegisteredCount!=chunks.Count)throw new InvalidOperationException("New scenery query registration is incomplete: "+manager.RegisteredCount+"/"+chunks.Count);
        }
        public int SoftCount=>chunks.Count;
    }
}
