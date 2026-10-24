using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using Object=UnityEngine.Object;
namespace RoadGunner.Editor
{
    public static partial class PrototypeBuilder
    {
        static partial void CompletePrototype(VehicleController car,DriveInput input,FollowCamera follow,Canvas canvas,Transform safe,Transform arena)
        {
            var tuning=Setting<CombatTuning>("Combat and population");
            var session=new GameObject("Session • Road Gunner").AddComponent<GameSession>();session.vehicle=car;session.input=input;session.follow=follow;session.startPosition=car.transform.position;session.startRotation=car.transform.rotation;
            var health=car.gameObject.AddComponent<VehicleHealth>();health.tuning=tuning;health.bodyPanels=new[]{car.transform.GetChild(0).GetChild(0).GetComponent<Renderer>(),car.transform.GetChild(0).GetChild(1).GetComponent<Renderer>()};session.health=health;
            // Navigation includes only static world colliders; no car or trigger hitboxes in the bake.
            var surface=arena.gameObject.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Children;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=1;surface.overrideVoxelSize=true;surface.voxelSize=.18f;surface.minRegionArea=3;
            surface.BuildNavMesh();
            string navPath=Root+"/Meshes/Rust basin navigation.asset";var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            if(old){EditorUtility.CopySerialized(surface.navMeshData,old);surface.RemoveData();Object.DestroyImmediate(surface.navMeshData);surface.navMeshData=old;surface.AddData();}else AssetDatabase.CreateAsset(surface.navMeshData,navPath);
            var recovery=new List<Vector3>();recovery.Add(session.startPosition);
            for(int z=-36;z<=36;z+=12)for(int x=-36;x<=36;x+=12){var p=new Vector3(x,Height(x,z)+.7f,z);if(!Physics.CheckBox(p+Vector3.up*.4f,new Vector3(1.2f,.4f,2.6f),session.startRotation,1,QueryTriggerInteraction.Ignore)&&NavMesh.SamplePosition(p,out NavMeshHit h,2,NavMesh.AllAreas))recovery.Add(new Vector3(h.position.x,Height(h.position.x,h.position.z)+.7f,h.position.z));}session.recoveryPoints=recovery.ToArray();
            var effects=BuildEffects();effects.transform.SetParent(session.transform,false);session.effects=effects;
            var turret=BuildTurret(car.transform);turret.session=session;turret.tuning=tuning;turret.hitMask=(1<<enemyLayer)|1;session.turret=turret;
            var ram=new GameObject("Front ram sensor");ram.transform.SetParent(car.transform,false);ram.transform.localPosition=new Vector3(0,.4f,2.45f);ram.layer=Layer("RG_Ram");var trigger=ram.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(2.2f,.9f,.9f);ram.AddComponent<RamSensor>().session=session;
            var engine=car.gameObject.AddComponent<AudioSource>();engine.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Engine.wav");engine.loop=true;engine.playOnAwake=true;engine.volume=.12f;engine.spatialBlend=0;car.engine=engine;
            var spawns=new GameObject("Raider groups • pooled 30").AddComponent<SpawnDirector>();session.spawns=spawns;spawns.session=session;spawns.tuning=tuning;spawns.view=input.view;
            Vector2[] groups={new Vector2(4,17),new Vector2(-22,7),new Vector2(24,22),new Vector2(-27,-24),new Vector2(21,-23),new Vector2(-21,31)};
            spawns.groupCenters=new Vector3[groups.Length];for(int i=0;i<groups.Length;i++)spawns.groupCenters[i]=new Vector3(groups[i].x,Height(groups[i].x,groups[i].y),groups[i].y);
            var raider=BuildRaider();var prefab=PrefabUtility.SaveAsPrefabAsset(raider.gameObject,Root+"/Prefabs/Wasteland Raider.prefab");Object.DestroyImmediate(raider.gameObject);
            spawns.pool=new Raider[tuning.activeCap];
            for(int i=0;i<spawns.pool.Length;i++){var g=(GameObject)PrefabUtility.InstantiatePrefab(prefab);g.name="Raider "+(i+1).ToString("00");g.transform.SetParent(spawns.transform,false);var r=g.GetComponent<Raider>();r.session=session;r.tuning=tuning;spawns.pool[i]=r;g.SetActive(false);}
            var pickups=new GameObject("Scrap pool • capped 60").AddComponent<PickupPool>();session.pickups=pickups;pickups.session=session;pickups.tuning=tuning;pickups.items=new Transform[tuning.pickupCap];
            var scrap=new GameObject("Scrap pickup");Shape("Scrap plate",scrap.transform,PrimitiveType.Cube,Vector3.zero,new Vector3(.55f,.15f,.36f),accent);var metal=Shape("Salvage metal",scrap.transform,PrimitiveType.Cube,new Vector3(0,.14f,0),new Vector3(.36f,.12f,.27f),cream);metal.transform.localRotation=Quaternion.Euler(0,35,0);DeathLootBuilder.PreparePickupPrefab(scrap);var pp=PrefabUtility.SaveAsPrefabAsset(scrap,Root+"/Prefabs/Scrap pickup.prefab");Object.DestroyImmediate(scrap);
            for(int i=0;i<pickups.items.Length;i++){var g=(GameObject)PrefabUtility.InstantiatePrefab(pp);g.transform.SetParent(pickups.transform,false);pickups.items[i]=g.transform;g.SetActive(false);}
            safe.gameObject.AddComponent<SafeArea>();session.hud=BuildHud(safe,session);
            // Prefab stores replaceable vehicle configuration; scene-only dependencies are assigned explicitly on the scene instance.
            HeroHarpoonBuilder.ApplyToSession(session);
            PrefabUtility.SaveAsPrefabAssetAndConnect(car.gameObject,Root+"/Prefabs/Dust Jackal.prefab",InteractionMode.AutomatedAction);
            // Reassign external references as prefab instance overrides (cross-scene references cannot live in a prefab).
            car.input=input;car.GetComponent<TurretController>().session=session;ram.GetComponent<RamSensor>().session=session;
            PrefabUtility.RecordPrefabInstancePropertyModifications(car);PrefabUtility.RecordPrefabInstancePropertyModifications(car.GetComponent<TurretController>());PrefabUtility.RecordPrefabInstancePropertyModifications(ram.GetComponent<RamSensor>());
            SetPortraitGameView(540,960);
            EnemyBuggyBuilder.AddToSession(session);
        }
        static TurretController BuildTurret(Transform car) {
            var turret=car.gameObject.AddComponent<TurretController>();
            Shape("Turret bearing",car,PrimitiveType.Cylinder,new Vector3(0,1.58f,-.35f),new Vector3(.92f,.10f,.92f),dark);
            var yaw=new GameObject("Independent turret yaw").transform;yaw.SetParent(car,false);yaw.localPosition=new Vector3(0,1.76f,-.35f);turret.yaw=yaw;
            MeshShape("Turret armored shield",yaw,wedge,new Vector3(0,0,0),new Vector3(.65f,.25f,.65f),rust);
            var pitch=new GameObject("Barrel elevation").transform;pitch.SetParent(yaw,false);pitch.localPosition=new Vector3(0,.1f,.34f);turret.pitch=pitch;
            Shape("Machine gun receiver",pitch,PrimitiveType.Cube,new Vector3(0,0,.25f),new Vector3(.38f,.28f,.72f),dark);
            var barrel=Shape("Long machine gun barrel",pitch,PrimitiveType.Cylinder,new Vector3(0,0,.94f),new Vector3(.135f,.60f,.135f),dark);barrel.transform.localRotation=Quaternion.Euler(90,0,0);
            var tip=Shape("Muzzle brake",pitch,PrimitiveType.Cube,new Vector3(0,0,1.52f),new Vector3(.23f,.23f,.18f),cream);
            turret.muzzle=new GameObject("Muzzle • hitscan origin").transform;turret.muzzle.SetParent(pitch,false);turret.muzzle.localPosition=new Vector3(0,0,1.64f);
            SetLayer(yaw,vehicleLayer);return turret;
        }
        static Raider BuildRaider() {
            var root=new GameObject("Wasteland Raider");var r=root.AddComponent<Raider>();var agent=root.AddComponent<NavMeshAgent>();agent.radius=.38f;agent.height=1.9f;agent.speed=3.5f;agent.angularSpeed=420;agent.acceleration=18;agent.stoppingDistance=.25f;agent.obstacleAvoidanceType=ObstacleAvoidanceType.MedQualityObstacleAvoidance;agent.enabled=false;r.agent=agent;
            var col=root.AddComponent<CapsuleCollider>();col.center=new Vector3(0,.95f,0);col.radius=.4f;col.height=1.9f;col.isTrigger=true;r.hitbox=col;
            var visual=new GameObject("Procedural raider rig").transform;visual.SetParent(root.transform,false);r.visual=visual;
            var torso=MeshShape("Ragged torso",visual,wedge,new Vector3(0,1.1f,0),new Vector3(.73f,.77f,.46f),cloth);r.torso=torso.GetComponent<Renderer>();
            Shape("Chest scrap plate",visual,PrimitiveType.Cube,new Vector3(.06f,1.25f,.245f),new Vector3(.45f,.40f,.06f),dark);
            Shape("Belt",visual,PrimitiveType.Cube,new Vector3(0,.82f,0),new Vector3(.62f,.15f,.47f),rust);
            MeshShape("Head",visual,wedge,new Vector3(0,1.72f,0),new Vector3(.45f,.48f,.43f),skin);
            Shape("Goggles",visual,PrimitiveType.Cube,new Vector3(0,1.81f,.215f),new Vector3(.46f,.12f,.07f),dark);
            Shape("Orange lenses",visual,PrimitiveType.Cube,new Vector3(0,1.82f,.26f),new Vector3(.3f,.06f,.03f),accent);
            Shape("Scarf",visual,PrimitiveType.Cube,new Vector3(0,1.53f,.12f),new Vector3(.48f,.2f,.39f),cloth);
            r.leftLeg=Limb("Left leg",visual,new Vector3(-.2f,.74f,0),new Vector3(.23f,.61f,.26f),dark);r.rightLeg=Limb("Right leg",visual,new Vector3(.2f,.74f,0),new Vector3(.23f,.61f,.26f),dark);
            Shape("Left boot",r.leftLeg,PrimitiveType.Cube,new Vector3(0,-.63f,.09f),new Vector3(.27f,.19f,.45f),rubber);Shape("Right boot",r.rightLeg,PrimitiveType.Cube,new Vector3(0,-.63f,.09f),new Vector3(.27f,.19f,.45f),rubber);
            r.leftArm=Limb("Left arm",visual,new Vector3(-.45f,1.42f,0),new Vector3(.21f,.63f,.23f),skin);r.rightArm=Limb("Weapon arm",visual,new Vector3(.45f,1.42f,0),new Vector3(.21f,.63f,.23f),skin);
            Shape("Shoulder pad",r.leftArm,PrimitiveType.Cube,new Vector3(0,-.04f,0),new Vector3(.36f,.24f,.4f),cream);
            var spike=MeshShape("Shoulder spike",r.leftArm,cone,new Vector3(0,.09f,0),new Vector3(.15f,.28f,.15f),dark);
            Shape("Crowbar grip",r.rightArm,PrimitiveType.Cube,new Vector3(0,-.58f,.19f),new Vector3(.09f,.09f,.75f),dark);Shape("Crowbar hook",r.rightArm,PrimitiveType.Cube,new Vector3(0,-.48f,.53f),new Vector3(.09f,.28f,.09f),cream);
            StyleScrapRushRaider(r);SetLayer(root.transform,enemyLayer);return r;
        }
        static Transform Limb(string name,Transform parent,Vector3 p,Vector3 size,Material m){var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=p;Shape("Limb mesh",t,PrimitiveType.Cube,new Vector3(0,-size.y*.5f,0),size,m);return t;}
        static CombatEffects BuildEffects() {
            var e=new GameObject("Combat effects • fixed pools").AddComponent<CombatEffects>();
            string p=Root+"/Materials/Hot brass unlit.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(p);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=new Color(1,.72f,.2f);AssetDatabase.CreateAsset(mat,p);}
            e.tracers=new LineRenderer[12];for(int i=0;i<e.tracers.Length;i++){var g=new GameObject("Tracer "+i);g.transform.SetParent(e.transform,false);var l=g.AddComponent<LineRenderer>();l.sharedMaterial=mat;l.positionCount=2;l.startWidth=.065f;l.endWidth=.025f;l.useWorldSpace=true;l.enabled=false;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;e.tracers[i]=l;}
            e.sparks=new Transform[16];for(int i=0;i<e.sparks.Length;i++){var g=MeshShape("Impact "+i,e.transform,cone,Vector3.zero,Vector3.one*.22f,mat);g.SetActive(false);e.sparks[i]=g.transform;}
            var flash=MeshShape("Muzzle flash",e.transform,cone,Vector3.zero,new Vector3(.25f,.66f,.25f),mat);flash.SetActive(false);e.flash=flash.transform;
            e.source=e.gameObject.AddComponent<AudioSource>();e.source.playOnAwake=false;e.source.spatialBlend=0;e.gun=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Gun.wav");e.impact=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Impact.wav");e.pickup=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/Pickup.wav");return e;
        }
        static readonly Color Ink=new Color(.19f,.16f,.12f,.92f),Paper=new Color(1,.94f,.79f),Orange=new Color(1,.65f,.22f);
        static HudView BuildHud(Transform safe,GameSession session) {
            var hud=safe.gameObject.AddComponent<HudView>();hud.session=session;
            var header=Panel("Top instrument panel",safe,new Vector2(0,1),new Vector2(1,1),new Vector2(0,-111),new Vector2(-60,164),Ink);header.raycastTarget=false;
            Panel("Signal stripe",header.transform,new Vector2(0,0),new Vector2(0,1),new Vector2(4,0),new Vector2(8,0),Orange).raycastTarget=false;
            Label("Wordmark",header.transform,"SCRAP RUSH",46,Paper,new Vector2(0,1),new Vector2(0,1),new Vector2(250,-43),new Vector2(420,58));
            Label("Sector",header.transform,"ROAD GUNNER  /  THE DUSTLANDS",19,new Color(.84f,.76f,.58f),new Vector2(0,1),new Vector2(0,1),new Vector2(250,-88),new Vector2(420,30));
            var healthBack=Panel("Health track",header.transform,new Vector2(0,0),new Vector2(1,0),new Vector2(0,17),new Vector2(-60,12),new Color(.23f,.25f,.21f));healthBack.raycastTarget=false;
            var fill=Panel("Health",healthBack.transform,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Color(.96f,.67f,.25f));fill.sprite=CircleSprite();fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.raycastTarget=false;hud.healthFill=fill;
            hud.healthText=Label("Health number",header.transform,"100 / 100",23,Paper,new Vector2(1,0),new Vector2(1,0),new Vector2(-120,43),new Vector2(180,36),TextAnchor.MiddleRight);
            hud.pause=Button("Pause",header.transform,"II",new Vector2(1,1),new Vector2(-80,-57),new Vector2(100,90),new Color(.17f,.21f,.20f),Paper,36);
            hud.pause.GetComponentInChildren<Text>().text="";
            Panel("Pause left bar",hud.pause.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(-9,0),new Vector2(7,31),Paper).raycastTarget=false;
            Panel("Pause right bar",hud.pause.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(9,0),new Vector2(7,31),Paper).raycastTarget=false;
            var counts=Rect("Salvage counters",safe,new Vector2(0,1),new Vector2(1,1),new Vector2(0,-251),new Vector2(-62,86));
            var left=Panel("Scrap panel",counts,new Vector2(0,0),new Vector2(.48f,1),Vector2.zero,Vector2.zero,Ink);left.raycastTarget=false;
            Label("Scrap label",left.transform,"SCRAP",20,Orange,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(82,0),new Vector2(110,38));hud.scrapText=Label("Scrap value",left.transform,"000",38,Paper,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-104,0),new Vector2(140,54),TextAnchor.MiddleRight);
            var right=Panel("Raiders panel",counts,new Vector2(.52f,0),new Vector2(1,1),Vector2.zero,Vector2.zero,Ink);right.raycastTarget=false;
            Label("Kill label",right.transform,"RAIDERS",20,new Color(.84f,.76f,.58f),new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(92,0),new Vector2(135,38));hud.killsText=Label("Kill value",right.transform,"000",38,Paper,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-104,0),new Vector2(140,54),TextAnchor.MiddleRight);
            hud.recover=Button("Recover",safe,"RECOVER",new Vector2(1,0),new Vector2(-175,125),new Vector2(280,100),Ink,Paper,27);
            hud.speedText=Label("Speed",safe,"00 <size=20>KM/H</size>",56,Paper,new Vector2(1,0),new Vector2(1,0),new Vector2(-183,243),new Vector2(290,95),TextAnchor.MiddleRight);
            Label("Automatic fire hint",safe,"AUTO FIRE",20,new Color(.93f,.87f,.71f,.75f),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,32),new Vector2(600,32),TextAnchor.MiddleCenter);
            hud.debugText=Label("Debug overlay",safe,"",22,Color.white,new Vector2(.5f,.70f),new Vector2(.5f,.70f),Vector2.zero,new Vector2(850,110),TextAnchor.MiddleCenter);hud.debugText.gameObject.SetActive(false);
            var overlay=Panel("Pause and defeat veil",safe,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Color(.025f,.04f,.035f,.84f));hud.overlay=overlay.gameObject;
            Label("Overlay kicker",overlay.transform,"SCRAP RUSH  /  ROAD GUNNER",22,Orange,new Vector2(.5f,.64f),new Vector2(.5f,.64f),Vector2.zero,new Vector2(880,45),TextAnchor.MiddleCenter);
            hud.overlayTitle=Label("Overlay title",overlay.transform,"PAUSED",88,Paper,new Vector2(.5f,.57f),new Vector2(.5f,.57f),Vector2.zero,new Vector2(900,140),TextAnchor.MiddleCenter);
            hud.overlayScore=Label("Overlay score",overlay.transform,"",28,Paper,new Vector2(.5f,.48f),new Vector2(.5f,.48f),Vector2.zero,new Vector2(900,65),TextAnchor.MiddleCenter);
            hud.resume=Button("Resume",overlay.transform,"RESUME",new Vector2(.5f,.36f),Vector2.zero,new Vector2(640,115),Orange,Ink,32);
            hud.restart=Button("Restart",overlay.transform,"RESTART",new Vector2(.5f,.26f),Vector2.zero,new Vector2(640,115),new Color(.20f,.24f,.21f),Paper,32);
            Label("Controls hint",overlay.transform,"WASD / ARROWS  •  SPACE: BRAKE\nR: RECOVER  •  ESC: PAUSE  •  F3: TELEMETRY",22,new Color(.65f,.68f,.6f),new Vector2(.5f,.13f),new Vector2(.5f,.13f),Vector2.zero,new Vector2(900,100),TextAnchor.MiddleCenter);
            overlay.gameObject.SetActive(false);return hud;
        }
        static Button Button(string name,Transform parent,string label,Vector2 anchor,Vector2 pos,Vector2 size,Color bg,Color fg,int fontSize) {var p=Panel(name,parent,anchor,anchor,pos,size,bg);var b=p.gameObject.AddComponent<Button>();b.targetGraphic=p;var colors=b.colors;colors.highlightedColor=new Color(1.15f,1.15f,1.15f);colors.pressedColor=new Color(.65f,.65f,.65f);b.colors=colors;Label("Label",p.transform,label,fontSize,fg,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,TextAnchor.MiddleCenter);return b;}
        public static void SetPortraitGameView(int width,int height) {
            // Unity's Game View size API is editor-internal; reflection is isolated to this convenience action.
            var assembly=typeof(UnityEditor.Editor).Assembly;var sizes=assembly.GetType("UnityEditor.GameViewSizes");var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizes);var instance=singleton.GetProperty("instance").GetValue(null,null);
            var getGroup=sizes.GetMethod("GetGroup");var groupType=assembly.GetType("UnityEditor.GameViewSizeGroupType");var group=getGroup.Invoke(instance,new[]{System.Enum.Parse(groupType,"Standalone")});
            var sizeType=assembly.GetType("UnityEditor.GameViewSize");var enumType=assembly.GetType("UnityEditor.GameViewSizeType");string label="Road Gunner "+width+"x"+height;
            int builtin=(int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group,null),custom=(int)group.GetType().GetMethod("GetCustomCount").Invoke(group,null),index=-1;
            for(int i=0;i<builtin+custom;i++){var s=group.GetType().GetMethod("GetGameViewSize").Invoke(group,new object[]{i});if((int)sizeType.GetProperty("width").GetValue(s)==width&&(int)sizeType.GetProperty("height").GetValue(s)==height)index=i;}
            if(index<0){var size=System.Activator.CreateInstance(sizeType,new object[]{System.Enum.Parse(enumType,"FixedResolution"),width,height,label});group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size});index=builtin+custom;}
            var viewType=assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(viewType);viewType.GetProperty("selectedSizeIndex",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(window,index);window.Repaint();
        }
    }
}
