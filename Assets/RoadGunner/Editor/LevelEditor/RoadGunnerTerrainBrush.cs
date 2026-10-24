using System;
using UnityEditor;
using UnityEngine;
namespace RoadGunner.Editor
{
    public enum ReliefOperation { Raise, Lower, Smooth, Flatten }
    public static class RoadGunnerTerrainBrush
    {
        public static bool GroundHit(LevelLayout l,Ray ray,out RaycastHit hit)
        {hit=default;return l&&l.ground&&l.ground.GetComponent<MeshCollider>()&&l.ground.GetComponent<MeshCollider>().Raycast(ray,out hit,10000);}
        public static Vector3 GroundPoint(LevelLayout l,float x,float z)
        {if(!GroundHit(l,new Ray(new Vector3(x,1000,z),Vector3.down),out var hit))throw new InvalidOperationException("No level ground below this position.");return hit.point;}
        public static void Refresh(LevelLayout l)
        {if(!l||!l.ground)return;var mesh=l.ground.sharedMesh;mesh.RecalculateNormals();mesh.RecalculateBounds();var c=l.ground.GetComponent<MeshCollider>();c.sharedMesh=null;c.sharedMesh=mesh;EditorUtility.SetDirty(mesh);l.navigationNeedsBake=true;RoadGunnerLevelAuthoring.Dirty(l);Physics.SyncTransforms();SceneView.RepaintAll();}
        public static void Apply(LevelLayout l,Vector3 world,float radius,float strength,float target,ReliefOperation op,float seconds=.1f,bool undo=true)
        {
            if(!l||!l.ground)return;var mesh=l.ground.sharedMesh;if(!mesh||!mesh.isReadable)throw new InvalidOperationException("Ground mesh must be readable.");
            if(undo){Undo.RegisterCompleteObjectUndo(mesh,"Terrain "+op);Undo.RecordObject(l,"Relief changes navigation");}
            var vertices=mesh.vertices;var original=(Vector3[])vertices.Clone();int width=Mathf.RoundToInt(Mathf.Sqrt(vertices.Length));bool grid=width*width==vertices.Length;
            if(op==ReliefOperation.Smooth&&!grid)throw new InvalidOperationException("Smooth requires the Road Gunner square grid mesh.");
            for(int i=0;i<vertices.Length;i++){
                var p=l.ground.transform.TransformPoint(original[i]);float distance=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(world.x,world.z));if(distance>=radius)continue;
                float falloff=Mathf.SmoothStep(0,1,1-distance/radius);float amount=Mathf.Max(0,strength)*Mathf.Clamp(seconds,.001f,.15f)*falloff;
                if(op==ReliefOperation.Raise)p.y+=amount;else if(op==ReliefOperation.Lower)p.y-=amount;
                else if(op==ReliefOperation.Flatten)p.y=Mathf.MoveTowards(p.y,target,amount);
                else{int x=i%width,z=i/width;float sum=0;int n=0;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,zz=z+dz;if(xx<0||zz<0||xx>=width||zz>=width)continue;sum+=l.ground.transform.TransformPoint(original[zz*width+xx]).y;n++;}p.y=Mathf.Lerp(p.y,sum/n,Mathf.Clamp01(amount));}
                vertices[i]=l.ground.transform.InverseTransformPoint(p);
            }
            mesh.vertices=vertices;Refresh(l);
        }
        public static void All(LevelLayout l,ReliefOperation op,float height)
        {
            var mesh=l.ground.sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Terrain "+op+" all");Undo.RecordObject(l,"Relief changes navigation");
            if(op==ReliefOperation.Smooth){var b=l.ground.GetComponent<MeshCollider>().bounds;for(int i=0;i<4;i++)Apply(l,b.center,Mathf.Max(b.size.x,b.size.z)*2,4,height,op,.15f,false);}
            else {var v=mesh.vertices;for(int i=0;i<v.Length;i++){var p=l.ground.transform.TransformPoint(v[i]);p.y=height;v[i]=l.ground.transform.InverseTransformPoint(p);}mesh.vertices=v;Refresh(l);}
        }
        public static void Reset(LevelLayout l)
        {if(!l.baselineTerrain)return;Undo.RegisterCompleteObjectUndo(l.ground.sharedMesh,"Reset terrain to level baseline");Undo.RecordObject(l,"Reset relief");EditorUtility.CopySerialized(l.baselineTerrain,l.ground.sharedMesh);Refresh(l);}
    }
}
