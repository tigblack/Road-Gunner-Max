using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace RoadGunner.Editor
{
    public static class EnemyBuggyBuilder
    {
        const string Root = PrototypeBuilder.Root;
        const string PrefabPath = Root + "/Prefabs/Rust Vulture Enemy Buggy.prefab";
        static Material rust, faded, steel, rubber, cream, red, skin;
        static Mesh box, spike;
        [MenuItem("Tools/Road Gunner/Enemies/Add Rust Vulture To Gameplay Scenes")]
        public static void Install()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the current scene first.");
            foreach(var path in new[]{PrototypeBuilder.ScenePath,Level01Builder.ScenePath})
            {
                if(!File.Exists(path))continue;
                var scene=EditorSceneManager.OpenScene(path);
                var session=Object.FindAnyObjectByType<GameSession>();
                if(!session)throw new InvalidOperationException("No GameSession in "+path);
                AddToSession(session);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            PrototypeBuilder.SetPortraitGameView(540,960);
            Debug.Log("ROAD GUNNER: Rust Vulture enemy buggies saved in both gameplay scenes.");
        }
        public static void AddToSession(GameSession session)
        {
            int layer=BuggyLayer();
            Physics.IgnoreLayerCollision(layer,LayerMask.NameToLayer("RG_Enemy"),true);
            session.turret.hitMask|=1<<layer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(session.turret);
            if(session.spawns.buggyPool!=null&&session.spawns.buggyPool.Length>0){RangeCombatBuilder.AddToSession(session);return;}
            var tuning=AssetDatabase.LoadAssetAtPath<BuggyTuning>(Root+"/Settings/Enemy buggy.asset");
            if(!tuning){tuning=ScriptableObject.CreateInstance<BuggyTuning>();AssetDatabase.CreateAsset(tuning,Root+"/Settings/Enemy buggy.asset");}
            var prefab=BuildPrefab(tuning,layer);
            var parent=new GameObject("Enemy buggies • pooled 3").transform;parent.SetParent(session.spawns.transform,false);
            var markers=new GameObject("Buggy spawn markers • editable").transform;markers.SetParent(session.spawns.transform,false);
            Vector3[] points={new Vector3(-7,0,-12),new Vector3(24,0,18),new Vector3(-26,0,18)};
            session.spawns.buggySpawnPoints=new Transform[points.Length];session.spawns.buggyPool=new EnemyBuggy[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                var marker=new GameObject("Buggy encounter "+(i+1).ToString("00")).transform;marker.SetParent(markers,false);
                marker.position=points[i]+Vector3.up*PrototypeBuilder.Height(points[i].x,points[i].z);
                Vector3 facing=session.startPosition-marker.position;facing.y=0;
                marker.rotation=Quaternion.LookRotation(facing);
                session.spawns.buggySpawnPoints[i]=marker;
                var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);obj.name="Rust Vulture "+(i+1).ToString("00");
                var buggy=obj.GetComponent<EnemyBuggy>();buggy.session=session;
                PrefabUtility.RecordPrefabInstancePropertyModifications(buggy);
                session.spawns.buggyPool[i]=buggy;obj.SetActive(false);
            }
            RangeCombatBuilder.AddToSession(session);
        }
        static int BuggyLayer()
        {
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var layers=tags.FindProperty("layers");
            for(int i=8;i<32;i++)if(layers.GetArrayElementAtIndex(i).stringValue=="RG_Buggy")return i;
            for(int i=8;i<32;i++)if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)){layers.GetArrayElementAtIndex(i).stringValue="RG_Buggy";tags.ApplyModifiedProperties();return i;}
            throw new InvalidOperationException("No free layer for the buggy.");
        }
        static Color ColorOf(string hex){ColorUtility.TryParseHtmlString("#"+hex,out var c);return c;}
        static Material Material(string name,string color,float metal=0)
        {
            string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",ColorOf(color));m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",.13f);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
        }
        static GameObject Part(string name,Transform parent,Vector3 p,Vector3 size,Material material,PrimitiveType type=PrimitiveType.Cube)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=p;obj.transform.localScale=size;
            Object.DestroyImmediate(obj.GetComponent<Collider>());obj.GetComponent<Renderer>().sharedMaterial=material;
            if(type==PrimitiveType.Cube&&box)obj.GetComponent<MeshFilter>().sharedMesh=box;
            return obj;
        }
        static void Tube(string name,Transform parent,Vector3 a,Vector3 b,float width,Material material)
        {
            var obj=Part(name,parent,(a+b)*.5f,new Vector3(width,(b-a).magnitude*.5f,width),material,PrimitiveType.Cylinder);
            obj.transform.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
        }
        static GameObject BuildPrefab(BuggyTuning tuning,int layer)
        {
            rust=Material("Buggy deep rust","99523D",.08f);faded=Material("Buggy sun faded orange","BF7049");steel=Material("Buggy scratched tube steel","434C49",.18f);
            rubber=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Dusty tire.mat");cream=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Bone armor.mat");
            red=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Raider crimson.mat");skin=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Sunburnt skin.mat");
            box=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Scrap Rush beveled block.asset");spike=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/Spike.asset");
            var root=new GameObject("RUST VULTURE • rusty raider buggy",typeof(Rigidbody));var buggy=root.AddComponent<EnemyBuggy>();buggy.tuning=tuning;
            var body=root.GetComponent<Rigidbody>();body.mass=620;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.25f,0);collider.size=new Vector3(1.65f,.65f,3.25f);buggy.bodyColliders=new Collider[]{collider};
            var visual=new GameObject("Replaceable buggy visuals").transform;visual.SetParent(root.transform,false);
            var panels=new List<Renderer>();
            Part("Exposed belly pan",visual,new Vector3(0,.26f,0),new Vector3(1.45f,.16f,3.1f),steel);
            panels.Add(Part("Chipped short nose armor",visual,new Vector3(0,.62f,1.06f),new Vector3(1.42f,.18f,1.1f),faded).GetComponent<Renderer>());
            for(int side=-1;side<=1;side+=2)
            {
                Tube("Rusty chassis rail",visual,new Vector3(side*.72f,.25f,-1.55f),new Vector3(side*.72f,.35f,1.60f),.13f,rust);
                Tube("Roll cage front pillar",visual,new Vector3(side*.66f,.35f,.53f),new Vector3(side*.64f,1.60f,.25f),.14f,steel);
                Tube("Roll cage rear pillar",visual,new Vector3(side*.71f,.33f,-.94f),new Vector3(side*.64f,1.60f,-.68f),.14f,rust);
                Tube("Roll cage roof rail",visual,new Vector3(side*.64f,1.60f,.25f),new Vector3(side*.64f,1.60f,-.68f),.14f,steel);
                Tube("Rear cage diagonal",visual,new Vector3(side*.64f,1.50f,-.68f),new Vector3(side*.65f,.35f,-1.57f),.11f,steel);
                var plate=Part("Asymmetric bolted scrap side",visual,new Vector3(side*.78f,.53f,-.10f),new Vector3(.08f,.42f,1.04f),side<0?rust:faded);
                panels.Add(plate.GetComponent<Renderer>());
                for(int j=0;j<4;j++)Part("Side panel rivet",visual,new Vector3(side*.83f,.66f,-.5f+j*.25f),Vector3.one*.07f,cream,PrimitiveType.Sphere);
                Part("Worn cream identification stripe",visual,new Vector3(side*.827f,.51f,-.03f),new Vector3(.012f,.28f,.12f),cream);
                for(int axle=-1;axle<=1;axle+=2)
                {
                    Tube("Exposed suspension wishbone",visual,new Vector3(side*.45f,.18f,axle*1.10f),new Vector3(side*.98f,.08f,axle*1.10f),.09f,steel);
                    Tube("Gold suspension damper",visual,new Vector3(side*.67f,.58f,axle*1.1f),new Vector3(side*.98f,.10f,axle*1.1f),.11f,cream);
                    var fender=Part("Small rusted fender",visual,new Vector3(side*.91f,.64f,axle*1.12f),new Vector3(.5f,.09f,.86f),rust);panels.Add(fender.GetComponent<Renderer>());
                }
                Tube("Exposed rear exhaust",visual,new Vector3(side*.68f,.40f,-1.23f),new Vector3(side*.73f,1.02f,-1.61f),.12f,steel);
                Part("Small amber headlamp",visual,new Vector3(side*.50f,.48f,1.62f),new Vector3(.26f,.17f,.10f),cream);
            }
            Tube("Roll cage crossbar",visual,new Vector3(-.64f,1.60f,.25f),new Vector3(.64f,1.60f,.25f),.14f,steel);
            Tube("Rear roll cage crossbar",visual,new Vector3(-.64f,1.60f,-.68f),new Vector3(.64f,1.60f,-.68f),.14f,rust);
            Tube("Rear X brace left",visual,new Vector3(-.68f,.4f,-.95f),new Vector3(.64f,1.52f,-.70f),.09f,steel);
            Tube("Rear X brace right",visual,new Vector3(.68f,.4f,-.95f),new Vector3(-.64f,1.52f,-.70f),.09f,steel);
            Part("Toothed ram bumper",visual,new Vector3(0,.30f,1.73f),new Vector3(1.9f,.22f,.22f),steel);
            for(int j=0;j<5;j++)
            {
                var tooth=new GameObject("Scrap ram tooth",typeof(MeshFilter),typeof(MeshRenderer));tooth.transform.SetParent(visual,false);
                tooth.transform.localPosition=new Vector3(-.72f+j*.36f,.31f,1.87f);tooth.transform.localScale=new Vector3(.23f,.36f,.23f);tooth.transform.localRotation=Quaternion.Euler(90,0,0);
                tooth.GetComponent<MeshFilter>().sharedMesh=spike;tooth.GetComponent<MeshRenderer>().sharedMaterial=j%2==0?cream:rust;
            }
            Part("Rear exposed engine block",visual,new Vector3(0,.56f,-1.22f),new Vector3(.95f,.43f,.63f),steel);
            for(int j=0;j<7;j++)Part("Cooling fins",visual,new Vector3(-.38f+j*.13f,.80f,-1.22f),new Vector3(.055f,.06f,.51f),rust);
            Part("Strapped fuel tank",visual,new Vector3(.48f,.66f,-.93f),new Vector3(.38f,.47f,.32f),red);
            Part("Tank steel strap",visual,new Vector3(.48f,.66f,-1.10f),new Vector3(.06f,.40f,.018f),steel);
            for(int j=0;j<13;j++)
            {
                var chip=Part("Broken rust paint edge",visual,new Vector3(Mathf.Sin(j*2.4f)*.62f,.716f,.61f+(j%5)*.20f),new Vector3(.065f,.006f,.035f+j%3*.03f),j%3==0?steel:rust);
                chip.transform.localRotation=Quaternion.Euler(0,j*31,0);
            }
            for(int side=-1;side<=1;side+=2)
            {
                Part("Bucket seat",visual,new Vector3(side*.35f,.48f,-.22f),new Vector3(.47f,.13f,.52f),rubber);
                Part("Seat back",visual,new Vector3(side*.35f,.78f,-.43f),new Vector3(.45f,.61f,.14f),rubber);
            }
            Part("Driver red jacket",visual,new Vector3(-.35f,.91f,-.22f),new Vector3(.35f,.44f,.29f),red);
            Part("Driver cream shoulder",visual,new Vector3(-.55f,1.04f,-.21f),new Vector3(.18f,.16f,.25f),cream);
            Part("Driver head",visual,new Vector3(-.35f,1.32f,-.17f),new Vector3(.32f,.34f,.30f),skin);
            Part("Driver bandanna",visual,new Vector3(-.35f,1.42f,-.17f),new Vector3(.35f,.15f,.33f),red);
            Part("Driver scarf",visual,new Vector3(-.35f,1.18f,-.02f),new Vector3(.31f,.15f,.07f),red);
            Part("Driver goggles",visual,new Vector3(-.35f,1.33f,-.005f),new Vector3(.32f,.10f,.05f),steel);
            for(int side=-1;side<=1;side+=2)Part("Cream goggle lens",visual,new Vector3(-.35f+side*.08f,1.34f,.025f),new Vector3(.09f,.045f,.012f),cream);
            Tube("Driver left arm",visual,new Vector3(-.55f,1.02f,-.2f),new Vector3(-.50f,.92f,.21f),.12f,skin);
            Tube("Driver right arm",visual,new Vector3(-.15f,1.02f,-.2f),new Vector3(-.19f,.92f,.21f),.12f,skin);
            var steering=Part("Driver steering wheel",visual,new Vector3(-.35f,.94f,.22f),new Vector3(.30f,.025f,.30f),rubber,PrimitiveType.Cylinder);steering.transform.localRotation=Quaternion.Euler(65,0,0);
            buggy.wheels=new WheelCollider[4];buggy.wheelVisuals=new Transform[4];
            for(int i=0;i<4;i++)
            {
                float side=i%2==0?-1:1;float z=i<2?1.10f:-1.10f;
                var wheel=new GameObject("Buggy WheelCollider "+i).transform;wheel.SetParent(root.transform,false);wheel.localPosition=new Vector3(side*.96f,.10f,z);
                var wc=wheel.gameObject.AddComponent<WheelCollider>();wc.radius=.46f;wc.suspensionDistance=.28f;wc.mass=22;wc.forceAppPointDistance=.08f;
                var spring=wc.suspensionSpring;spring.spring=21000;spring.damper=2800;spring.targetPosition=.5f;wc.suspensionSpring=spring;
                var forward=wc.forwardFriction;forward.stiffness=1.25f;wc.forwardFriction=forward;var sideways=wc.sidewaysFriction;sideways.stiffness=1.15f;wc.sidewaysFriction=sideways;
                buggy.wheels[i]=wc;
                var pivot=new GameObject("Buggy wheel pose "+i).transform;pivot.SetParent(visual,false);pivot.localPosition=wheel.localPosition;buggy.wheelVisuals[i]=pivot;
                var tire=Part("Exposed knobby tire",pivot,Vector3.zero,new Vector3(.92f,.25f,.92f),rubber,PrimitiveType.Cylinder);tire.transform.localRotation=Quaternion.Euler(0,0,90);
                var hub=Part("Rusted wheel hub",pivot,new Vector3(side*.25f,0,0),new Vector3(.46f,.027f,.46f),rust,PrimitiveType.Cylinder);hub.transform.localRotation=Quaternion.Euler(0,0,90);
                for(int j=0;j<14;j++){float a=j*Mathf.PI*2/14;for(int row=-1;row<=1;row+=2){var tread=Part("Buggy thick tire tread",pivot,new Vector3(row*.12f,Mathf.Cos(a)*.45f,Mathf.Sin(a)*.45f),new Vector3(.23f,.08f,.15f),rubber);tread.transform.localRotation=Quaternion.Euler(a*Mathf.Rad2Deg,0,row*9);}}
                for(int j=0;j<5;j++){float a=j*Mathf.PI*2/5;Part("Hub bolt",pivot,new Vector3(side*.28f,Mathf.Cos(a)*.12f,Mathf.Sin(a)*.12f),Vector3.one*.07f,cream,PrimitiveType.Sphere);}
            }
            buggy.armor=panels.ToArray();buggy.dust=Particles(root.transform,"Buggy driving dust",new Color(1,.76f,.42f,.28f),40,true);
            buggy.smoke=Particles(root.transform,"Buggy wreck smoke",new Color(.19f,.16f,.13f,.65f),24,false);
            RangeCombatBuilder.AddGun(buggy);
            SetLayer(root.transform,layer);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);Object.DestroyImmediate(root);return prefab;
        }
        static ParticleSystem Particles(Transform parent,string name,Color color,int cap,bool loop)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.localPosition=new Vector3(0,.13f,-1.50f);var p=obj.AddComponent<ParticleSystem>();p.Stop();
            var main=p.main;main.loop=loop;main.playOnAwake=loop;main.duration=1.5f;main.startLifetime=1.5f;main.startSpeed=.6f;main.startSize=new ParticleSystem.MinMaxCurve(.4f,.85f);main.startColor=color;main.maxParticles=cap;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=p.emission;emission.rateOverTime=0;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.30f;
            var velocity=p.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=loop?.5f:1.3f;
            var fade=p.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=gradient;
            p.GetComponent<ParticleSystemRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Dust particles.mat");return p;
        }
        static void SetLayer(Transform parent,int layer){parent.gameObject.layer=layer;foreach(Transform child in parent)SetLayer(child,layer);}
    }
}
