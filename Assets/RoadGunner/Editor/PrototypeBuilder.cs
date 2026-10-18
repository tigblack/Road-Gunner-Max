using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine.AI;
using Object=UnityEngine.Object;
namespace RoadGunner.Editor
{
    public static partial class PrototypeBuilder
    {
        public const string Root="Assets/RoadGunner", ScenePath=Root+"/Scenes/RoadGunner_Prototype.unity";
        static Material sand,rust,dark,rubber,cream,accent,glass,rock,cloth,skin;
        static Mesh wedge,cone;
        static int vehicleLayer,enemyLayer;
        static Font font;
        [MenuItem("Tools/Road Gunner/Build Prototype")]
        public static void Build()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before rebuilding.");
            for(int i=0;i<SceneManager.sceneCount;i++) { var s=SceneManager.GetSceneAt(i); if(s.isDirty&&s.path!=ScenePath) throw new InvalidOperationException("Save unrelated scene before building: "+s.path); }
            foreach(string d in new[]{"Scenes","Scripts","Prefabs","Materials","Meshes","UI","Audio","Effects","Settings","Editor"}) System.IO.Directory.CreateDirectory(Root+"/"+d);
            AssetDatabase.Refresh();
            vehicleLayer=Layer("RG_Vehicle"); enemyLayer=Layer("RG_Enemy");
            Physics.IgnoreLayerCollision(vehicleLayer,enemyLayer,true); Physics.IgnoreLayerCollision(enemyLayer,enemyLayer,true);
            Time.fixedDeltaTime=.02f;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait=true; PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
            PlayerSettings.allowedAutorotateToLandscapeLeft=false; PlayerSettings.allowedAutorotateToLandscapeRight=false;
            PlayerSettings.defaultScreenWidth=540; PlayerSettings.defaultScreenHeight=960;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            sand=Mat("Dune sand",Hex("F48B45")); rust=Mat("Oxidized steel",Hex("AF4831"),.08f);
            dark=Mat("Gunmetal",Hex("303B3A"),.15f); rubber=Mat("Dusty tire",Hex("34312C"));
            cream=Mat("Bone armor",Hex("F4DFAC"),.03f); accent=Mat("Signal orange",Hex("E9683F"),.05f);
            glass=Mat("Smoked teal glass",Hex("224F50"),.2f); rock=Mat("Desert basalt",Hex("C18C70"));
            cloth=Mat("Raider crimson",Hex("BE352B")); skin=Mat("Sunburnt skin",Hex("B87542"));
            wedge=SaveMesh("Armor wedge",Wedge()); cone=SaveMesh("Spike",Cone());
            PrepareScrapRushArt();
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RenderSettings.ambientMode=AmbientMode.Trilight; RenderSettings.ambientSkyColor=Hex("CEDAE2"); RenderSettings.ambientEquatorColor=Hex("A9977B"); RenderSettings.ambientGroundColor=Hex("695547");
            RenderSettings.fog=true; RenderSettings.fogColor=Hex("F1BC73"); RenderSettings.fogMode=FogMode.Linear; RenderSettings.fogStartDistance=65; RenderSettings.fogEndDistance=135;
            var sun=new GameObject("Sun • late desert afternoon").AddComponent<Light>(); sun.type=LightType.Directional; sun.color=new Color(1,.91f,.77f); sun.intensity=1.8f; sun.shadows=LightShadows.Soft; sun.shadowStrength=.72f; sun.transform.rotation=Quaternion.Euler(52,-36,0);
            QualitySettings.shadowDistance=65;
            var arena=new GameObject("Arena • The Rust Basin").transform;
            BuildScrapRushArena(arena);
            var car=BuildVehicle(); car.transform.position=new Vector3(0,.65f,0); car.transform.rotation=Quaternion.Euler(0,165,0);StyleScrapRushVehicle(car);
            var cam=new GameObject("Main Camera").AddComponent<Camera>(); cam.tag="MainCamera"; cam.orthographic=true; cam.orthographicSize=11.5f; cam.nearClipPlane=.1f; cam.farClipPlane=150; cam.backgroundColor=Hex("F1BC73"); cam.clearFlags=CameraClearFlags.SolidColor; cam.allowHDR=false; cam.gameObject.AddComponent<AudioListener>();
            ScrapRushLighting(cam);
            var follow=cam.gameObject.AddComponent<FollowCamera>(); follow.vehicle=car; follow.Snap();
            var input=new GameObject("Input • camera relative direction").AddComponent<DriveInput>(); input.view=cam; car.input=input;
            var canvas=new GameObject("HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1080,1920); scaler.matchWidthOrHeight=.5f;
            var safe=Rect("Safe area",canvas.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)); ConfigureUIInput(es.GetComponent<InputSystemUIInputModule>());
            BuildJoystick(safe,input);
            CompletePrototype(car,input,follow,canvas,safe,arena);
            EditorSceneManager.SaveScene(scene,ScenePath);
            var builds=new List<EditorBuildSettingsScene>{new EditorBuildSettingsScene(ScenePath,true)};
            foreach(var b in EditorBuildSettings.scenes) if(b.path!=ScenePath) builds.Add(b);
            EditorBuildSettings.scenes=builds.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject=car.gameObject;
            Debug.Log("ROAD GUNNER: Saved playable prototype at "+ScenePath);
        }
        static partial void CompletePrototype(VehicleController car,DriveInput input,FollowCamera follow,Canvas canvas,Transform safe,Transform arena);
        static int Layer(string name) {
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]); var layers=tags.FindProperty("layers");
            for(int i=8;i<32;i++) if(layers.GetArrayElementAtIndex(i).stringValue==name) return i;
            for(int i=8;i<32;i++) if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { layers.GetArrayElementAtIndex(i).stringValue=name; tags.ApplyModifiedProperties(); return i; }
            throw new InvalidOperationException("No free user layer for "+name);
        }
        static Material Mat(string name,Color color,float metal=0) {
            string p=Root+"/Materials/"+name+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(p);
            if(!m) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,p); }
            m.color=color; m.SetFloat("_Metallic",metal); m.SetFloat("_Smoothness",.22f); EditorUtility.SetDirty(m); return m;
        }
        static T Setting<T>(string name) where T:ScriptableObject { string p=Root+"/Settings/"+name+".asset"; var a=AssetDatabase.LoadAssetAtPath<T>(p); if(!a) { a=ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a,p); } return a; }
        static Mesh SaveMesh(string name,Mesh m) { string p=Root+"/Meshes/"+name+".asset"; var old=AssetDatabase.LoadAssetAtPath<Mesh>(p); m.name=name; if(old) { EditorUtility.CopySerialized(m,old); Object.DestroyImmediate(m); return old; } AssetDatabase.CreateAsset(m,p); return m; }
        static GameObject Shape(string name,Transform parent,PrimitiveType type,Vector3 p,Vector3 size,Material mat,bool collision=false) {
            var g=GameObject.CreatePrimitive(type); g.name=name; g.transform.SetParent(parent,false); g.transform.localPosition=p; g.transform.localScale=size; g.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collision) {Object.DestroyImmediate(g.GetComponent<Collider>());if(type==PrimitiveType.Cube && softBox)g.GetComponent<MeshFilter>().sharedMesh=softBox;} return g;
        }
        static GameObject MeshShape(string name,Transform parent,Mesh mesh,Vector3 p,Vector3 size,Material mat) {
            var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); g.transform.SetParent(parent,false); g.transform.localPosition=p; g.transform.localScale=size; g.GetComponent<MeshFilter>().sharedMesh=mesh; g.GetComponent<Renderer>().sharedMaterial=mat; return g;
        }
        static void SetLayer(Transform t,int layer) { t.gameObject.layer=layer; foreach(Transform c in t) SetLayer(c,layer); }
        static Mesh Wedge() {
            var v=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(-.40f,.5f,-.3f),new Vector3(.40f,.5f,-.3f),new Vector3(.42f,.25f,.5f),new Vector3(-.42f,.25f,.5f)};
            return Faceted(v,new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5},true);
        }
        static Mesh Cone() { var v=new Vector3[7]; v[0]=Vector3.up; for(int i=0;i<6;i++) v[i+1]=new Vector3(Mathf.Cos(i*Mathf.PI/3)*.5f,0,Mathf.Sin(i*Mathf.PI/3)*.5f); var t=new List<int>(); for(int i=0;i<6;i++) { t.Add(0);t.Add((i+1)%6+1);t.Add(i+1); } return Faceted(v,t.ToArray()); }
        static Mesh Faceted(Vector3[] v,int[] t,bool flip=false) { if(flip)for(int i=0;i<t.Length;i+=3){int a=t[i+1];t[i+1]=t[i+2];t[i+2]=a;} var vv=new Vector3[t.Length]; var tt=new int[t.Length]; for(int i=0;i<t.Length;i++){vv[i]=v[t[i]];tt[i]=i;} var m=new Mesh(); m.vertices=vv;m.triangles=tt;m.RecalculateNormals();m.RecalculateBounds();return m; }
        public static float Height(float x,float z) {
            float flat=Mathf.SmoothStep(0,1,Mathf.InverseLerp(7,15,new Vector2(x,z).magnitude));
            float dunes=.8f+.60f*Mathf.Sin(x*.085f)*Mathf.Cos(z*.095f)+.45f*Mathf.Sin(z*.11f+x*.045f);
            float test=Mathf.Exp(-Mathf.Pow((x+13)/3.8f,2))*Mathf.Exp(-Mathf.Pow((z-2)/10,4))*.16f*Mathf.Sin(z*1.7f+x*.55f);
            return dunes*flat+test;
        }
        static void BuildArena(Transform root) {
            const int n=80; var v=new Vector3[(n+1)*(n+1)]; var t=new List<int>();
            for(int z=0;z<=n;z++) for(int x=0;x<=n;x++) {float px=-50+x*1.25f,pz=-50+z*1.25f;v[z*(n+1)+x]=new Vector3(px,Height(px,pz),pz);}
            for(int z=0;z<n;z++) for(int x=0;x<n;x++) {int a=z*(n+1)+x;t.Add(a);t.Add(a+n+1);t.Add(a+1);t.Add(a+1);t.Add(a+n+1);t.Add(a+n+2);}
            var ground=MeshShape("Rolling dunes • collision terrain",root,SaveMesh("Rust basin terrain",Faceted(v,t.ToArray())),Vector3.zero,Vector3.one,sand); ground.AddComponent<MeshCollider>().sharedMesh=ground.GetComponent<MeshFilter>().sharedMesh;
            ground.isStatic=true;
            var rand=new System.Random(9471);
            Vector2[] spots={new Vector2(-12,18),new Vector2(13,12),new Vector2(24,-7),new Vector2(-22,-13),new Vector2(0,27),new Vector2(-30,22),new Vector2(28,27),new Vector2(13,-26),new Vector2(-12,-30),new Vector2(36,-25)};
            for(int i=0;i<spots.Length;i++) {
                var s=spots[i]; float y=Height(s.x,s.y);
                var g=MeshShape(i%3==0?"Wreck barricade":"Wind-carved basalt",root,wedge,new Vector3(s.x,y+.65f,s.y),new Vector3(3+(float)rand.NextDouble()*2,1.3f,2+(float)rand.NextDouble()*2),i%3==0?rust:rock);
                g.transform.rotation=Quaternion.Euler(0,rand.Next(360),0); g.AddComponent<BoxCollider>();g.isStatic=true;
                if(i%3==0) {Shape("Wreck cabin",g.transform,PrimitiveType.Cube,new Vector3(0,.45f,0),new Vector3(.55f,.4f,.5f),dark);Shape("Salvage marker",root,PrimitiveType.Cylinder,new Vector3(s.x+2,y+1.2f,s.y),new Vector3(.08f,1.2f,.08f),accent);}
            }
            for(int i=0;i<60;i++) { float a=i*Mathf.PI*2/60;float x=Mathf.Cos(a)*49,z=Mathf.Sin(a)*49;var g=MeshShape("Perimeter dune rock",root,wedge,new Vector3(x,Height(x,z)+1.1f,z),new Vector3(4.8f,2.2f,3.8f),i%3==0?rust:rock);g.transform.rotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);g.AddComponent<BoxCollider>();g.isStatic=true; }
            for(int i=0;i<4;i++) { bool along=i<2; float sign=i%2==0?-1:1; var g=new GameObject("Invisible perimeter safety wall");g.transform.SetParent(root);g.transform.position=along?new Vector3(sign*51,3,0):new Vector3(0,3,sign*51);var b=g.AddComponent<BoxCollider>();b.size=along?new Vector3(2,9,104):new Vector3(104,9,2);g.isStatic=true; }
            // Sparse flat debris silhouettes add scale without loose physics objects.
            for(int i=0;i<90;i++) {float x=(float)rand.NextDouble()*94-47,z=(float)rand.NextDouble()*94-47;if(x*x+z*z<70)continue;var g=Shape("Buried metal",root,PrimitiveType.Cube,new Vector3(x,Height(x,z)+.025f,z),new Vector3(.35f+(float)rand.NextDouble(),.035f,.12f),i%3==0?cream:rust);g.transform.rotation=Quaternion.Euler(0,rand.Next(360),0);g.isStatic=true;}
            for(int i=0;i<5;i++) {float z=-7+i*4;Shape("Suspension test lane marker",root,PrimitiveType.Cube,new Vector3(-17,Height(-17,z)+.04f,z),new Vector3(.3f,.08f,1.2f),cream);}
        }
        static VehicleController BuildVehicle() {
            var root=new GameObject("DUST JACKAL • armored muscle car"); var car=root.AddComponent<VehicleController>();car.tuning=Setting<VehicleTuning>("Vehicle handling");
            var body=root.GetComponent<Rigidbody>();body.mass=1400;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0,.35f,0);collider.size=new Vector3(1.8f,.60f,4.15f);
            var cabinCollider=root.AddComponent<BoxCollider>();cabinCollider.center=new Vector3(0,1.05f,-.40f);cabinCollider.size=new Vector3(1.4f,.75f,1.4f);
            var visual=new GameObject("Replaceable visuals").transform;visual.SetParent(root.transform,false);
            MeshShape("Long sculpted muscle body",visual,wedge,new Vector3(0,.47f,0),new Vector3(2.05f,.65f,4.4f),rust);
            MeshShape("Armored hood",visual,wedge,new Vector3(0,.85f,1.15f),new Vector3(1.9f,.26f,1.7f),cream);
            MeshShape("Cabin",visual,wedge,new Vector3(0,1.03f,-.42f),new Vector3(1.65f,.86f,1.9f),dark);
            var wind=Shape("Windscreen",visual,PrimitiveType.Cube,new Vector3(0,1.15f,.39f),new Vector3(1.30f,.43f,.05f),glass);wind.transform.localRotation=Quaternion.Euler(24,0,0);
            Shape("Roof plate",visual,PrimitiveType.Cube,new Vector3(0,1.46f,-.5f),new Vector3(1.4f,.12f,1.24f),cream);
            for(int side=-1;side<=1;side+=2) {
                Shape("Side armor",visual,PrimitiveType.Cube,new Vector3(side*1.02f,.68f,-.3f),new Vector3(.11f,.5f,1.6f),cream);
                Shape("Orange door stripe",visual,PrimitiveType.Cube,new Vector3(side*1.087f,.7f,-.3f),new Vector3(.02f,.30f,.3f),accent);
                Shape("Side window",visual,PrimitiveType.Cube,new Vector3(side*.735f,1.12f,-.43f),new Vector3(.055f,.4f,.85f),glass);
                var exhaust=Shape("Exposed exhaust",visual,PrimitiveType.Cylinder,new Vector3(side*.9f,.94f,-1.63f),new Vector3(.16f,.65f,.16f),dark);exhaust.transform.localRotation=Quaternion.Euler(-20,0,0);
                Shape("Fuel can",visual,PrimitiveType.Cube,new Vector3(side*.52f,.92f,-1.75f),new Vector3(.43f,.65f,.40f),accent);
                for(int j=0;j<3;j++) {var spike=MeshShape("Armor spike",visual,cone,new Vector3(side*1.06f,.88f,-.85f+j*.55f),new Vector3(.23f,.56f,.23f),dark);spike.transform.localRotation=Quaternion.Euler(0,0,side*-40);}
                Shape("Headlamp",visual,PrimitiveType.Cube,new Vector3(side*.70f,.68f,2.12f),new Vector3(.43f,.22f,.06f),cream);
            }
            Shape("Battering ram",visual,PrimitiveType.Cube,new Vector3(0,.38f,2.24f),new Vector3(2.25f,.30f,.26f),dark);
            Shape("Grille",visual,PrimitiveType.Cube,new Vector3(0,.65f,2.11f),new Vector3(.8f,.28f,.1f),dark);
            for(int i=0;i<5;i++){var spike=MeshShape("Ram tooth",visual,cone,new Vector3(-.85f+i*.425f,.40f,2.37f),new Vector3(.2f,.4f,.2f),cream);spike.transform.localRotation=Quaternion.Euler(90,0,0);}
            Shape("Engine blower",visual,PrimitiveType.Cube,new Vector3(0,1.03f,1.1f),new Vector3(.6f,.3f,.55f),dark);
            car.wheels=new WheelCollider[4]; car.wheelVisuals=new Transform[4];car.dust=new ParticleSystem[4];
            for(int i=0;i<4;i++) {
                float x=i%2==0?-.91f:.91f,z=i<2?1.35f:-1.35f;
                var w=new GameObject(i<2?"Front WheelCollider":"Rear WheelCollider");w.transform.SetParent(root.transform,false);w.transform.localPosition=new Vector3(x,.15f,z);
                var wc=w.AddComponent<WheelCollider>(); wc.radius=.42f;wc.suspensionDistance=.30f;wc.mass=30;wc.forceAppPointDistance=.12f;var s=wc.suspensionSpring;s.spring=35000;s.damper=4500;s.targetPosition=.5f;wc.suspensionSpring=s;car.wheels[i]=wc;
                var pivot=new GameObject("Wheel pose "+i).transform;pivot.SetParent(visual,false);pivot.localPosition=new Vector3(x,0,z);car.wheelVisuals[i]=pivot;
                var tire=Shape("Rubber tire",pivot,PrimitiveType.Cylinder,Vector3.zero,new Vector3(.84f,.20f,.84f),rubber);tire.transform.localRotation=Quaternion.Euler(0,0,90);
                var hub=Shape("Steel hub",pivot,PrimitiveType.Cylinder,new Vector3(Mathf.Sign(x)*.21f,0,0),new Vector3(.43f,.025f,.43f),dark);hub.transform.localRotation=Quaternion.Euler(0,0,90);
                for(int j=0;j<6;j++) {float a=j*Mathf.PI/3; var lug=Shape("Tread cleat",pivot,PrimitiveType.Cube,new Vector3(0,Mathf.Cos(a)*.39f,Mathf.Sin(a)*.39f),new Vector3(.43f,.10f,.16f),rubber);lug.transform.localRotation=Quaternion.Euler(a*Mathf.Rad2Deg,0,0);}
                car.dust[i]=Dust(root.transform,i);
            }
            SetLayer(root.transform,vehicleLayer);
            return car;
        }
        static ParticleSystem Dust(Transform parent,int i) {
            var g=new GameObject("Wheel dust "+i);g.transform.SetParent(parent,false);var p=g.AddComponent<ParticleSystem>();p.Stop();var main=p.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.85f,1.35f);main.startSpeed=new ParticleSystem.MinMaxCurve(.65f,1.3f);main.startSize=new ParticleSystem.MinMaxCurve(.45f,.95f);main.startColor=Color.white;main.maxParticles=40;main.simulationSpace=ParticleSystemSimulationSpace.World;var em=p.emission;em.rateOverTime=0;var shape=p.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.18f;var col=p.colorOverLifetime;col.enabled=true;var grad=new Gradient();grad.SetKeys(new[]{new GradientColorKey(new Color(1,.83f,.55f),0),new GradientColorKey(new Color(1,.83f,.55f),1)},new[]{new GradientAlphaKey(.52f,0),new GradientAlphaKey(0,1)});col.color=grad;
            var velocity=p.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=.65f;
            string path=Root+"/Materials/Dust particles.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",5);mat.SetFloat("_DstBlend",10);mat.SetFloat("_ZWrite",0);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;AssetDatabase.CreateAsset(mat,path);}mat.shader=Shader.Find("RoadGunner/Sand Tracks");mat.SetColor("_BaseColor",Color.white);mat.renderQueue=3000;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/Soft golden dust.png"));EditorUtility.SetDirty(mat);p.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;return p;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 pos,Vector2 size) { var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.anchoredPosition=pos;r.sizeDelta=size;return r; }
        static Image Panel(string name,Transform parent,Vector2 min,Vector2 max,Vector2 pos,Vector2 size,Color color) {var r=Rect(name,parent,min,max,pos,size);var img=r.gameObject.AddComponent<Image>();img.color=color;if(color.a>0){img.sprite=RoundedPanel();img.type=Image.Type.Sliced;}return img;}
        static Text Label(string name,Transform parent,string text,int size,Color color,Vector2 min,Vector2 max,Vector2 pos,Vector2 dimensions,TextAnchor align=TextAnchor.MiddleLeft) {var r=Rect(name,parent,min,max,pos,dimensions);var t=r.gameObject.AddComponent<Text>();t.font=font;t.text=text;t.fontSize=size;t.color=color;t.alignment=align;t.raycastTarget=false;if(name=="Wordmark")t.fontStyle=FontStyle.BoldAndItalic;return t;}
        static void ConfigureUIInput(InputSystemUIInputModule module) {
            var asset=AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            module.actionsAsset=asset;
            module.point=UIAction(asset,"Point");module.leftClick=UIAction(asset,"Click");
            module.rightClick=UIAction(asset,"RightClick");module.middleClick=UIAction(asset,"MiddleClick");
            module.scrollWheel=UIAction(asset,"ScrollWheel");module.move=UIAction(asset,"Navigate");
            module.submit=UIAction(asset,"Submit");module.cancel=UIAction(asset,"Cancel");
            module.pointerBehavior=UIPointerBehavior.AllPointersAsIs;
        }
        static InputActionReference UIAction(InputActionAsset asset,string name) {
            string path=Root+"/Settings/UI "+name+".asset";var reference=AssetDatabase.LoadAssetAtPath<InputActionReference>(path);
            var action=asset.FindAction("UI/"+name,true);
            if(!reference){reference=InputActionReference.Create(action);AssetDatabase.CreateAsset(reference,path);}else {reference.Set(action);EditorUtility.SetDirty(reference);}
            return reference;
        }
        static void BuildJoystick(Transform safe,DriveInput input) {
            var zone=Panel("Floating joystick activation zone",safe,Vector2.zero,new Vector2(.56f,.32f),Vector2.zero,Vector2.zero,new Color(0,0,0,0));
            var joy=zone.gameObject.AddComponent<FloatingJoystick>();
            var ring=Panel("Joystick ring",zone.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(-20,-15),new Vector2(240,240),new Color(.1f,.14f,.14f,.34f));ring.raycastTarget=false;ring.sprite=CircleSprite();
            var inner=Panel("Joystick inner",ring.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(214,214),new Color(.74f,.72f,.61f,.12f));inner.sprite=CircleSprite();inner.raycastTarget=false;
            var thumb=Panel("Joystick thumb",ring.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(86,86),new Color(.96f,.8f,.53f,.7f));thumb.sprite=CircleSprite();thumb.raycastTarget=false;
            joy.ring=ring.rectTransform;joy.thumb=thumb.rectTransform;input.joystick=joy;
            Label("Joystick hint",zone.transform,"DRAG TO DRIVE",23,new Color(.95f,.9f,.77f,.8f),new Vector2(.5f,.12f),new Vector2(.5f,.12f),Vector2.zero,new Vector2(350,38),TextAnchor.MiddleCenter);
        }
        static Sprite CircleSprite() {
            string p=Root+"/UI/Circle.png";if(!System.IO.File.Exists(p)) { var tex=new Texture2D(128,128,TextureFormat.RGBA32,false);for(int y=0;y<128;y++)for(int x=0;x<128;x++){float d=Vector2.Distance(new Vector2(x,y),new Vector2(63.5f,63.5f));tex.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(64-d)));}tex.Apply();System.IO.File.WriteAllBytes(p,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(p);var importer=(TextureImporter)AssetImporter.GetAtPath(p);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.SaveAndReimport();}return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }
    }
}
