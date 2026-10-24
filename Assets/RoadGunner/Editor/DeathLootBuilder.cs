using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace RoadGunner.Editor
{
    public static class DeathLootBuilder
    {
        const string Root=PrototypeBuilder.Root, LootPath=Root+"/Prefabs/Scrap pickup.prefab";
        [MenuItem("Tools/Road Gunner/Combat/Install Ranged Buggies Explosion And Gear Loot")]
        public static void Install()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the current scene first.");
            EnsurePickupPrefab();
            var tuning=AssetDatabase.LoadAssetAtPath<BuggyTuning>(Root+"/Settings/Enemy buggy.asset");EditorUtility.SetDirty(tuning);
            foreach(string path in new[]{PrototypeBuilder.ScenePath,Level01Builder.ScenePath})
            {
                if(!File.Exists(path))continue;var scene=EditorSceneManager.OpenScene(path);
                RangeCombatBuilder.AddToSession(Object.FindAnyObjectByType<GameSession>());EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();PrototypeBuilder.SetPortraitGameView(540,960);
            Debug.Log("ROAD GUNNER: ranged buggy tactics, pooled explosions and flying metal gears saved in both scenes.");
        }
        static Material Mat(string name,Color color,float metallic=0)
        {
            string path=Root+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",.24f);mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
        }
        static Mesh SaveMesh(string name,Mesh mesh)
        {
            string path=Root+"/Meshes/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);mesh.name=name;
            if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Mesh GearMesh()
        {
            const int n=48;var vertices=new List<Vector3>();var steel=new List<int>();var rust=new List<int>();
            Vector3 Point(int ring,int i)
            {
                float outer=i%4==1||i%4==2?.54f:.43f;float r=ring==0||ring==5?.17f:ring==1||ring==4?outer-.025f:outer;
                float y=ring==0||ring==1?.065f:ring==2?.035f:ring==3?-.035f:-.065f;
                float a=i*Mathf.PI*2/n;return new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
            }
            for(int row=0;row<6;row++)for(int i=0;i<n;i++)
            {
                int next=(i+1)%n,other=(row+1)%6,index=vertices.Count;
                vertices.Add(Point(row,i));vertices.Add(Point(row,next));vertices.Add(Point(other,next));vertices.Add(Point(other,i));
                var triangles=i/4==2||i/4==7?rust:steel;
                triangles.Add(index);triangles.Add(index+1);triangles.Add(index+2);triangles.Add(index);triangles.Add(index+2);triangles.Add(index+3);
            }
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.subMeshCount=2;mesh.SetTriangles(steel,0);mesh.SetTriangles(rust,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static void PreparePickupPrefab(GameObject root)
        {
            if(root.transform.Find("Metal gear"))return;
            var plates=new GameObject("Scrap plates").transform;plates.SetParent(root.transform,false);
            for(int i=root.transform.childCount-1;i>=0;i--){var c=root.transform.GetChild(i);if(c!=plates)c.SetParent(plates,false);}
            var gear=new GameObject("Metal gear",typeof(MeshFilter),typeof(MeshRenderer));gear.transform.SetParent(root.transform,false);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Metal gear pickup.asset");if(!mesh)mesh=SaveMesh("Metal gear pickup",GearMesh());gear.GetComponent<MeshFilter>().sharedMesh=mesh;
            var steel=Mat("Gear brushed steel",new Color(.49f,.55f,.53f),.45f);
            gear.GetComponent<MeshRenderer>().sharedMaterials=new[]{steel,Mat("Gear oxidized edges",new Color(.42f,.25f,.14f),.08f)};gear.SetActive(false);
        }
        static void EnsurePickupPrefab()
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(LootPath);if(asset&&asset.transform.Find("Metal gear"))return;
            var root=PrefabUtility.LoadPrefabContents(LootPath);PreparePickupPrefab(root);PrefabUtility.SaveAsPrefabAsset(root,LootPath);PrefabUtility.UnloadPrefabContents(root);
        }
        static Mesh RingMesh()
        {
            const int n=64;var v=new Vector3[n*2];var c=new Color[n*2];var t=new int[n*6];
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));v[i*2]=d*.95f;v[i*2+1]=d*1.05f;c[i*2]=c[i*2+1]=Color.white;
                int j=i*2,k=(i+1)%n*2,b=i*6;t[b]=j;t[b+1]=k+1;t[b+2]=j+1;t[b+3]=j;t[b+4]=k;t[b+5]=k+1;
            }
            var mesh=new Mesh();mesh.vertices=v;mesh.colors=c;mesh.uv=new Vector2[v.Length];mesh.triangles=t;mesh.RecalculateBounds();return mesh;
        }
        static ParticleSystem Particle(Transform parent,string name,int count,Color color,float lifeMin,float lifeMax,float speedMin,float speedMax,float sizeMin,float sizeMax)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=Vector3.up*.35f;
            var p=obj.AddComponent<ParticleSystem>();p.Stop();var main=p.main;main.loop=false;main.playOnAwake=false;main.duration=2.4f;main.maxParticles=count;
            main.startLifetime=new ParticleSystem.MinMaxCurve(lifeMin,lifeMax);main.startSpeed=new ParticleSystem.MinMaxCurve(speedMin,speedMax);main.startSize=new ParticleSystem.MinMaxCurve(sizeMin,sizeMax);main.startColor=color;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=p.emission;emission.rateOverTime=0;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.65f;
            var fade=p.colorOverLifetime;fade.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.65f),new GradientAlphaKey(0,1)});fade.color=g;
            var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Dust particles.mat");renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return p;
        }
        static VehicleExplosion BuildExplosion(Transform parent,Collider ground,int index)
        {
            var obj=new GameObject("Vehicle explosion "+index);obj.transform.SetParent(parent,false);var e=obj.AddComponent<VehicleExplosion>();e.ground=ground;
            e.fire=Particle(obj.transform,"Golden orange fireball",24,new Color(1,.48f,.075f,.95f),.32f,.7f,.25f,1.5f,1.2f,2.1f);
            var fireSize=e.fire.sizeOverLifetime;fireSize.enabled=true;fireSize.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.25f,1,1.25f));
            var fireColor=e.fire.colorOverLifetime;var fg=new Gradient();fg.SetKeys(new[]{new GradientColorKey(new Color(1,1,.55f),0),new GradientColorKey(new Color(1,.7f,.3f),.35f),new GradientColorKey(new Color(.6f,.22f,.07f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fireColor.color=fg;
            e.smoke=Particle(obj.transform,"Soot plume",24,new Color(.22f,.17f,.13f,.8f),1.2f,2.3f,.25f,.8f,.9f,1.5f);
            var smokeVelocity=e.smoke.velocityOverLifetime;smokeVelocity.enabled=true;smokeVelocity.space=ParticleSystemSimulationSpace.World;smokeVelocity.y=1.7f;
            var smokeSize=e.smoke.sizeOverLifetime;smokeSize.enabled=true;smokeSize.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.65f,1,2));
            e.dust=Particle(obj.transform,"Sand pressure burst",36,new Color(1,.74f,.38f,.4f),.45f,1.2f,2,4.5f,.5f,1.1f);
            var dustShape=e.dust.shape;dustShape.shapeType=ParticleSystemShapeType.Circle;dustShape.rotation=new Vector3(90,0,0);
            e.sparks=Particle(obj.transform,"Flying hot sparks",32,new Color(1,.79f,.24f,1),.35f,.9f,4,8,.05f,.11f);var sm=e.sparks.main;sm.gravityModifier=.8f;
            var sr=e.sparks.GetComponent<ParticleSystemRenderer>();sr.renderMode=ParticleSystemRenderMode.Stretch;sr.lengthScale=2;sr.velocityScale=.06f;
            e.debris=Particle(obj.transform,"Scattered rusty metal",16,Color.white,.7f,1.4f,2,5,.10f,.23f);var dm=e.debris.main;dm.gravityModifier=1;dm.startRotation3D=true;dm.startRotationX=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);dm.startRotationY=new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
            var spin=e.debris.rotationOverLifetime;spin.enabled=true;spin.separateAxes=true;spin.x=3;spin.y=2;spin.z=4;
            var dr=e.debris.GetComponent<ParticleSystemRenderer>();dr.renderMode=ParticleSystemRenderMode.Mesh;dr.mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Scrap Rush beveled block.asset");dr.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Buggy deep rust.mat");
            string matPath=Root+"/Materials/Explosion ground ring.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!mat){mat=new Material(Shader.Find("RoadGunner/Sand Tracks"));mat.SetColor("_BaseColor",Color.white);mat.renderQueue=3002;AssetDatabase.CreateAsset(mat,matPath);}
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Explosion ground ring.asset");if(!mesh)mesh=SaveMesh("Explosion ground ring",RingMesh());
            var ring=new GameObject("Expanding ground shockwave",typeof(MeshFilter),typeof(MeshRenderer));ring.transform.SetParent(obj.transform,false);ring.GetComponent<MeshFilter>().sharedMesh=mesh;ring.GetComponent<MeshRenderer>().sharedMaterial=mat;ring.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;e.shockwave=ring.transform;
            obj.SetActive(false);return e;
        }
        static void StrengthenExplosion(VehicleExplosion e)
        {
            e.fire.transform.localPosition=Vector3.up*1.1f;
            var main=e.fire.main;main.startSize=new ParticleSystem.MinMaxCurve(1.6f,3);main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,2);
            var shape=e.fire.shape;shape.radius=.95f;
            var size=e.fire.sizeOverLifetime;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.55f,1,1.25f));
            var velocity=e.fire.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=.8f;
            e.smoke.transform.localPosition=Vector3.up*.85f;
            if(e.flash)return;
            string meshPath=Root+"/Meshes/Explosion cartoon flash.asset",matPath=Root+"/Materials/Explosion cartoon flash.mat";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!mesh){
                var v=new Vector3[17];var c=new Color[17];var t=new int[48];c[0]=new Color(1,.98f,.82f);
                for(int i=0;i<16;i++){float a=i*Mathf.PI*2/16,r=i%2==0?1:.42f;v[i+1]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);c[i+1]=i%2==0?new Color(1,.65f,.12f):new Color(1,.86f,.35f);t[i*3]=0;t[i*3+1]=i+1;t[i*3+2]=(i+1)%16+1;}
                mesh=new Mesh();mesh.vertices=v;mesh.colors=c;mesh.uv=new Vector2[v.Length];mesh.triangles=t;mesh.RecalculateBounds();mesh=SaveMesh("Explosion cartoon flash",mesh);
            }
            var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!mat){mat=new Material(Shader.Find("RoadGunner/Sand Tracks"));mat.SetColor("_BaseColor",Color.white);mat.renderQueue=3003;AssetDatabase.CreateAsset(mat,matPath);}
            var flash=new GameObject("Cartoon explosion flash",typeof(MeshFilter),typeof(MeshRenderer));flash.transform.SetParent(e.transform,false);
            flash.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=flash.GetComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;e.flash=flash.transform;flash.SetActive(false);EditorUtility.SetDirty(e);
        }
        static AudioClip ExplosionAudio()
        {
            string path=Root+"/Audio/Vehicle explosion.wav";if(!File.Exists(path))
            {
                const int sampleRate=44100;int count=(int)(sampleRate*.85f);var random=new System.Random(771);float smooth=0;
                using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(sampleRate);writer.Write(sampleRate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
                    for(int i=0;i<count;i++){float t=(float)i/sampleRate,noise=(float)random.NextDouble()*2-1;smooth=Mathf.Lerp(smooth,noise,.18f);float sample=Mathf.Sin(2*Mathf.PI*(62*t-18*t*t))*Mathf.Exp(-t*7)*.38f+smooth*Mathf.Exp(-t*5)*.65f+noise*Mathf.Exp(-t*18)*.22f;sample*=Mathf.Min(1,t*500);writer.Write((short)(Mathf.Clamp(sample,-1,1)*26000));}
                }
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        public static void AddToSession(GameSession session)
        {
            EnsurePickupPrefab();var ground=session.vehicle.GetComponent<AttackRangeIndicator>().ground;
            var pool=session.pickups;pool.ground=ground;pool.gearVisuals=new Transform[pool.items.Length];pool.scrapVisuals=new Transform[pool.items.Length];
            for(int i=0;i<pool.items.Length;i++)
            {
                pool.gearVisuals[i]=pool.items[i].Find("Metal gear");pool.scrapVisuals[i]=pool.items[i].Find("Scrap plates");
                if(!pool.gearVisuals[i]||!pool.scrapVisuals[i])throw new InvalidOperationException("Pickup prefab views missing at index "+i);
            }
            var effects=session.effects;
            if(effects.explosions==null||effects.explosions.Length==0)
            {
                var parent=new GameObject("Vehicle explosions • fixed pool of 3").transform;parent.SetParent(effects.transform,false);effects.explosions=new VehicleExplosion[3];
                for(int i=0;i<effects.explosions.Length;i++)effects.explosions[i]=BuildExplosion(parent,ground,i);
            }
            foreach(var e in effects.explosions)StrengthenExplosion(e);
            effects.explosion=ExplosionAudio();EditorUtility.SetDirty(pool);EditorUtility.SetDirty(effects);
        }
    }
}
