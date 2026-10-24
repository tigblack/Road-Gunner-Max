using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using RoadGunner.Editor;
using Object=UnityEngine.Object;
namespace RoadGunner.ConceptArt.Editor
{
 public static class Zone1EnemyInstaller
 {
  const string Folder=ArtKit.Root+"/Zone1";
  static readonly Quaternion Axis=Quaternion.Euler(0,45,0);
  static Vector3 W(float x,float z)=>Axis*new Vector3(x*2.5f,0,z*2.5f);
  static void Identity(GameObject g,EnemyRole role,string title){var i=g.GetComponent<EnemyIdentity>();if(!i)i=g.AddComponent<EnemyIdentity>();i.role=role;i.title=title;EditorUtility.SetDirty(i);}
  public static string Install()
  {
   if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
   Directory.CreateDirectory(Folder+"/Prefabs");Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
   var l=RoadGunnerLevelAuthoring.Current;var s=l.session;
   foreach(var old in s.spawns.authoredRaiders.Where(r=>r&&(r.name.Contains("West raider captain")||r.name.Contains("Armored scrapyard veteran"))).ToArray())Object.DestroyImmediate(old.gameObject);
   foreach(var r in s.spawns.pool.Where(r=>r))Identity(r.gameObject,EnemyRole.Raider,"WASTELAND RAIDER");
   // Update the reusable source as well as every existing pooled instance.
   string humanPath=AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(s.spawns.pool.First(r=>r).gameObject));
   if(!string.IsNullOrEmpty(humanPath)){var p=PrefabUtility.LoadPrefabContents(humanPath);Identity(p,EnemyRole.Raider,"WASTELAND RAIDER");PrefabUtility.SaveAsPrefabAsset(p,humanPath);PrefabUtility.UnloadPrefabContents(p);}
   string buggyPath="Assets/RoadGunner/Prefabs/Rust Vulture Enemy Buggy.prefab";
   {var p=PrefabUtility.LoadPrefabContents(buggyPath);Identity(p,EnemyRole.Buggy,"RUST VULTURE");PrefabUtility.SaveAsPrefabAsset(p,buggyPath);PrefabUtility.UnloadPrefabContents(p);}
   var placements=new[]{W(7,22),W(24,39)};
   var buggies=(s.spawns.authoredBuggies??Array.Empty<EnemyBuggy>()).Where(b=>b).Concat(s.spawns.buggyPool.Where(b=>b)).Distinct().Take(2).ToArray();
   foreach(var extra in (s.spawns.authoredBuggies??Array.Empty<EnemyBuggy>()).Where(b=>b&&!buggies.Contains(b))){extra.transform.SetParent(s.spawns.transform,true);extra.name="Rust Vulture reserve";extra.gameObject.SetActive(false);}
   s.spawns.authoredBuggies=buggies;
   for(int j=0;j<buggies.Length;j++){var b=buggies[j];b.transform.SetParent(l.enemyVehiclesRoot,true);if(!NavMesh.SamplePosition(placements[j],out var nh,3,NavMesh.AllAreas))throw new Exception("No buggy navigation "+j);b.transform.SetPositionAndRotation(nh.position+Vector3.up*.65f,Axis*Quaternion.Euler(0,j==0?180:220,0));b.name="Zone 1 · Rust Vulture "+(j+1);b.gameObject.SetActive(true);b.session=s;Identity(b.gameObject,EnemyRole.Buggy,"RUST VULTURE");EditorUtility.SetDirty(b);PrefabUtility.RecordPrefabInstancePropertyModifications(b.transform);}
   var oldRoot=l.authoredContentRoot.Find("Zone 1 boss encounters");if(oldRoot)Object.DestroyImmediate(oldRoot.gameObject);
   var root=ArtKit.Node("Zone 1 boss encounters",l.authoredContentRoot).transform;
   var mutantPrefab=BuildMutant();var truckPrefab=BuildTruck();
   var mutant=(GameObject)PrefabUtility.InstantiatePrefab(mutantPrefab,root);mutant.name="MINI BOSS · Ashmaw";mutant.transform.SetPositionAndRotation(W(41,58),Axis*Quaternion.Euler(0,210,0));
   var m=mutant.GetComponent<WastelandMutant>();m.session=s;
   var ms=ArtKit.Node("Ashmaw home",root).transform;ms.SetPositionAndRotation(mutant.transform.position,mutant.transform.rotation);m.spawnPoint=ms;
   var truck=(GameObject)PrefabUtility.InstantiatePrefab(truckPrefab,root);truck.name="ARENA BOSS · Iron Hauler";truck.transform.SetPositionAndRotation(W(-33,35),Axis*Quaternion.Euler(0,180,0));
   var t=truck.GetComponent<TruckArenaBoss>();t.session=s;
   var ts=ArtKit.Node("Iron Hauler home",root).transform;ts.SetPositionAndRotation(truck.transform.position,truck.transform.rotation);t.spawnPoint=ts;
   var center=ArtKit.Node("West arena center",root).transform;center.position=W(-33,35);t.arenaCenter=center;
   var roster=s.GetComponent<Zone1EnemyRoster>();if(!roster)roster=s.gameObject.AddComponent<Zone1EnemyRoster>();roster.session=s;roster.bosses=new CombatTarget[]{m,t};s.zone1=roster;
   Hud(s,roster);EditorUtility.SetDirty(s);EditorUtility.SetDirty(roster);PrefabUtility.RecordPrefabInstancePropertyModifications(m);PrefabUtility.RecordPrefabInstancePropertyModifications(t);
   RoadGunnerLevelAuthoring.Save(l,false);
   return $"Zone 1 installed: {s.spawns.authoredRaiders.Length} raiders, {s.spawns.authoredBuggies.Length} buggies, 1 mutant, 1 arena truck. Navigation and player physics retained.";
  }
  static GameObject BuildMutant()
  {
   var g=new GameObject("Ashmaw — mutant mini boss");g.layer=9;
   var nav=g.AddComponent<NavMeshAgent>();nav.enabled=false;nav.radius=.76f;nav.height=3.12f;nav.baseOffset=0;
   var c=g.AddComponent<CapsuleCollider>();c.radius=.78f;c.height=2.95f;c.center=new Vector3(0,1.49f,.05f);
   var m=g.AddComponent<WastelandMutant>();m.agent=nav;m.hitbox=c;m.attackMask=257;
   m.visual=Zone1MutantArt.Apply(g);m.body=m.visual.Find("Body");m.head=m.visual.Find("Head");m.leftArm=m.visual.Find("Left arm");m.rightArm=m.visual.Find("Right arm");m.leftLeg=m.visual.Find("Left leg");m.rightLeg=m.visual.Find("Right leg");
   m.skinRenderers=m.visual.GetComponentsInChildren<Renderer>(true);m.aimAnchor=ArtKit.Node("Target chest",g.transform).transform;m.aimAnchor.localPosition=new Vector3(0,1.6f,.08f);
   m.warningRadius=Marker(g.transform,"Smash danger radius",true);m.telegraph=m.warningRadius.gameObject;Identity(g,EnemyRole.MutantMiniBoss,"ASHMAW · MINI BOSS");
   var p=PrefabUtility.SaveAsPrefabAsset(g,Folder+"/Prefabs/Ashmaw Mutant.prefab");Object.DestroyImmediate(g);return p;
  }
  static GameObject BuildTruck()
  {
   var g=new GameObject("Iron Hauler — arena boss");g.layer=11;
   var body=g.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=2400;
   var nav=g.AddComponent<NavMeshAgent>();nav.enabled=false;nav.radius=1.5f;nav.height=3.1f;nav.baseOffset=0;
   var c=g.AddComponent<BoxCollider>();c.center=new Vector3(0,1.36f,.1f);c.size=new Vector3(2.85f,2.5f,6.65f);
   var ramNode=ArtKit.Node("Front ram contact",g.transform);ramNode.layer=11;ramNode.transform.localPosition=new Vector3(0,.85f,3.46f);var ram=ramNode.AddComponent<BoxCollider>();ram.size=new Vector3(2.95f,.5f,.4f);
   Zone1TruckArt.Apply(g);var t=g.AddComponent<TruckArenaBoss>();t.agent=nav;t.visual=g.transform.Find("Zone1 Truck visuals");t.bodyColliders=new Collider[]{c,ram};t.frontRam=ramNode.transform;t.frontRamCollider=ram;t.hitMask=257;t.wheelRadius=.65f;
   t.wheelVisuals=new[]{"Front left","Front right","Middle left","Middle right","Rear left","Rear right"}.Select(n=>t.visual.Find("Wheels/"+n)).ToArray();t.frontWheels=new[]{true,true,false,false,false,false};
   t.turretYaw=t.visual.Find("TurretYaw");t.turretPitch=t.turretYaw.Find("TurretPitch");t.muzzle=t.turretPitch.Find("Muzzle");t.armor=t.visual.GetComponentsInChildren<Renderer>(true);
   t.aimAnchor=ArtKit.Node("Target armored cab",g.transform).transform;t.aimAnchor.localPosition=new Vector3(0,1.65f,.4f);
   t.flash=ArtKit.Ellipsoid(t.muzzle,"Short autocannon flash",new Vector3(0,0,.1f),new Vector3(.18f,.18f,.42f),"Glow");ArtKit.Bake(t.flash,"Zone1_Truck_MuzzleFlash");t.flash.gameObject.SetActive(false);
   t.warningRadius=Marker(g.transform,"Ram danger lane",false);t.telegraph=t.warningRadius.gameObject;
   var warning=ArtKit.Torus(t.turretPitch,"Autocannon charging signal",new Vector3(0,0,.65f),.24f,.18f,.06f,"Yellow",Quaternion.Euler(90,0,0));ArtKit.Bake(warning,"Zone1_Truck_GunWarning");t.gunWarning=warning.gameObject;t.gunWarning.SetActive(false);
   // Reuse the existing low-cost dust/smoke treatment, never duplicate a gameplay root.
   var donor=RoadGunnerLevelAuthoring.Current.session.spawns.buggyPool.First(b=>b);
   if(donor.dust){t.dust=Object.Instantiate(donor.dust,g.transform);t.dust.name="Tandem wheel dust";t.dust.transform.localPosition=new Vector3(0,.15f,-2.5f);}
   if(donor.smoke){t.smoke=Object.Instantiate(donor.smoke,g.transform);t.smoke.name="Wreck smoke";t.smoke.transform.localPosition=new Vector3(0,2,.6f);}
   Identity(g,EnemyRole.TruckArenaBoss,"IRON HAULER · ARENA BOSS");
   var p=PrefabUtility.SaveAsPrefabAsset(g,Folder+"/Prefabs/Iron Hauler Truck.prefab");Object.DestroyImmediate(g);return p;
  }
  static Transform Marker(Transform parent,string name,bool circle)
  {
   string path=Folder+"/Materials/Threat amber.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",new Color(.85f,.24f,.065f,.55f));mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",5);mat.SetFloat("_DstBlend",10);mat.SetFloat("_ZWrite",0);mat.SetFloat("_Cull",0);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;EditorUtility.SetDirty(mat);
   var vertices=new System.Collections.Generic.List<Vector3>();var tris=new System.Collections.Generic.List<int>();
   Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>{int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});tris.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});};
   if(circle){for(int i=0;i<64;i++){float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;quad(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),new Vector3(Mathf.Cos(b),0,Mathf.Sin(b)),new Vector3(Mathf.Cos(b)*.90f,0,Mathf.Sin(b)*.90f),new Vector3(Mathf.Cos(a)*.90f,0,Mathf.Sin(a)*.90f));}}
   else {quad(new Vector3(-1,0,0),new Vector3(-.91f,0,0),new Vector3(-.91f,0,1),new Vector3(-1,0,1));quad(new Vector3(.91f,0,0),new Vector3(1,0,0),new Vector3(1,0,1),new Vector3(.91f,0,1));for(int i=0;i<5;i++){float z=.1f+i*.18f;quad(new Vector3(-.38f,0,z),new Vector3(0,0,z+.08f),new Vector3(.38f,0,z),new Vector3(0,0,z+.035f));}quad(new Vector3(-1,0,.98f),new Vector3(1,0,.98f),new Vector3(1,0,1),new Vector3(-1,0,1));}
   var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=ArtKit.SaveMesh(mesh,circle?"Zone1_SmashRing":"Zone1_RamLane");
   var marker=ArtKit.Node(name,parent).transform;marker.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var r=marker.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;marker.gameObject.SetActive(false);return marker;
  }
  static void Hud(GameSession s,Zone1EnemyRoster roster)
  {
   var canvas=s.hud.GetComponentInParent<Canvas>();var old=canvas.transform.Find("Zone 1 boss status");if(old)Object.DestroyImmediate(old.gameObject);
   var panel=new GameObject("Zone 1 boss status",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));panel.transform.SetParent(canvas.transform,false);var rect=(RectTransform)panel.transform;rect.anchorMin=new Vector2(.065f,.27f);rect.anchorMax=new Vector2(.935f,.34f);rect.offsetMin=rect.offsetMax=Vector2.zero;
   panel.GetComponent<Image>().color=new Color(.10f,.145f,.15f,.94f);panel.GetComponent<Image>().raycastTarget=false;panel.GetComponent<CanvasGroup>().blocksRaycasts=false;
   var font=s.hud.healthText.font;
   Func<string,Vector2,Vector2,int,Text> text=(name,min,max,size)=>{var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(panel.transform,false);var rt=(RectTransform)go.transform;rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=rt.offsetMax=Vector2.zero;var t=go.GetComponent<Text>();t.font=font;t.fontSize=size;t.color=ArtKit.Color("E4DCC5");t.raycastTarget=false;t.alignment=TextAnchor.MiddleLeft;return t;};
   var title=text("Boss name",new Vector2(.035f,.61f),new Vector2(.76f,.94f),25);title.fontStyle=FontStyle.Bold;
   var hp=text("Boss HP",new Vector2(.76f,.61f),new Vector2(.965f,.94f),23);hp.alignment=TextAnchor.MiddleRight;
   var phase=text("Attack warning",new Vector2(.035f,.02f),new Vector2(.965f,.29f),19);phase.color=ArtKit.Color("D7AE6B");
   var bar=new GameObject("Health track",typeof(RectTransform),typeof(Image));bar.transform.SetParent(panel.transform,false);var br=(RectTransform)bar.transform;br.anchorMin=new Vector2(.035f,.35f);br.anchorMax=new Vector2(.965f,.55f);br.offsetMin=br.offsetMax=Vector2.zero;bar.GetComponent<Image>().color=ArtKit.Color("303A3C");bar.GetComponent<Image>().raycastTarget=false;
   var fill=new GameObject("Health fill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(bar.transform,false);var fr=(RectTransform)fill.transform;fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=fr.offsetMax=Vector2.zero;var image=fill.GetComponent<Image>();image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;image.color=ArtKit.Color("AD603B");image.raycastTarget=false;
   var hud=panel.AddComponent<Zone1BossHud>();hud.session=s;hud.roster=roster;hud.panel=panel;hud.titleText=title;hud.healthText=hp;hud.phaseText=phase;hud.healthFill=image;
  }
 }
}
