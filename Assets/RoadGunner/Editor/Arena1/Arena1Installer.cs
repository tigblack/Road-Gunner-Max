using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Reauthors the existing first level with the Level Editor; terrain, art and gameplay roots are retained.</summary>
    public static class Arena1Installer
    {
        public const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";
        public const string AssetRoot = "Assets/RoadGunner/Levels/Greenwood/Arena1";
        public const string ReportPath = "Documentation/RoadGunner/Arena1-2026-10-24/Installation.txt";
        static readonly Quaternion Axis = Quaternion.Euler(0,45,0);
        public static Vector3 World(float east,float north) => Axis * new Vector3(east*2.5f,0,north*2.5f);
        static readonly Vector2[] RaiderPoints = {
            new Vector2(1,-34),new Vector2(3,-31),
            new Vector2(-23,-16),new Vector2(-17,-18),
            new Vector2(-18,3),new Vector2(-14,7),
            new Vector2(12,27),new Vector2(16,29),new Vector2(13,32),
            new Vector2(20,36),new Vector2(24,40),new Vector2(29,42)
        };
        static readonly int[] Pockets = {1,1,2,2,3,3,4,4,4,5,5,5};
        static void Record(Object value) {if(!value)return;EditorUtility.SetDirty(value);if(PrefabUtility.IsPartOfPrefabInstance(value))PrefabUtility.RecordPrefabInstancePropertyModifications(value);}
        static T Setting<T>(string name,T original) where T:ScriptableObject {
            string path=AssetRoot+"/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<T>(path);
            if(!asset){asset=Object.Instantiate(original);asset.name=name;AssetDatabase.CreateAsset(asset,path);}return asset;
        }
        static void Identity(GameObject go,EnemyRole role,string title) {var value=go.GetComponent<EnemyIdentity>();if(!value)value=go.AddComponent<EnemyIdentity>();value.role=role;value.title=title;Record(value);}
        static Vector3 Ground(Vector3 desired,float radius=8) {
            if(!NavMesh.SamplePosition(desired,out var hit,radius,NavMesh.AllAreas))throw new InvalidOperationException("No existing walkable ground near "+desired);
            return hit.position;
        }
        public static void InstallBatch() {
            if(SceneManager.GetActiveScene().path!=ScenePath)EditorSceneManager.OpenScene(ScenePath);
            Debug.Log(Install());
        }
        static Vector3 StationGround(Vector3 desired,Vector3 playerStart) {
            Physics.SyncTransforms();var path=new NavMeshPath();
            if(!NavMesh.SamplePosition(playerStart,out var start,3,NavMesh.AllAreas))throw new InvalidOperationException("Player start is outside existing navigation.");
            for(int ring=0;ring<=4;ring++)for(int step=0;step<(ring==0?1:12);step++) {
                float angle=step*Mathf.PI/6;var candidate=desired+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*2.5f;
                if(!NavMesh.SamplePosition(candidate,out var hit,2,NavMesh.AllAreas))continue;
                if(Physics.CheckBox(hit.position+Vector3.up*.9f,new Vector3(1.4f,.55f,1.4f),Axis,1,QueryTriggerInteraction.Ignore))continue;
                if(!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                return hit.position;
            }
            throw new InvalidOperationException("No clear, reachable station site near "+desired);
        }
        [MenuItem("Tools/Road Gunner/Arena 1/Apply First Arena Rebalance")]
        public static void ApplyFromMenu() {Debug.Log(Install());}
        public static string Install() {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before authoring Arena 1.");
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open the existing Greenwood level first.");
            var layout=RoadGunnerLevelAuthoring.PrepareCurrent();var s=layout.session;var d=s.spawns;
            Directory.CreateDirectory(AssetRoot);Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));AssetDatabase.Refresh();
            var lines=new List<string>{"Arena 1 fallback layout; these placements are authored Road Gunner values, not verified The War reference data.","Scene: "+ScenePath};
            var oldVehicle=s.vehicle.tuning;
            var handling=Setting("Arena 1 vehicle handling",oldVehicle);
            handling.maxSpeed=9;handling.maxReverseSpeed=7f/3f;handling.totalDriveTorque=2700;
            // Original steering curves/rate, brakes, wheel suspension and friction remain responsive.
            s.vehicle.tuning=handling;Record(handling);Record(s.vehicle);
            lines.Add("Vehicle: current playable maxSpeed6 ->9 m/s (1.5x), half the original18; reverse configuration="+handling.maxReverseSpeed+"; drive torque2400 ->2700. Steering rate125 and brake torques retained; directional drive uses forward only.");
            var playerCombat=Setting("Arena 1 player combat",s.turret.tuning);
            playerCombat.playerDamageMultiplier=.35f;playerCombat.attackLevelBonus=.2f;playerCombat.healthLevelBonus=.2f;playerCombat.ramMinimumSpeed=2;playerCombat.activeCap=30;
            s.turret.tuning=playerCombat;s.health.tuning=playerCombat;d.tuning=playerCombat;Record(playerCombat);Record(s.turret);Record(s.health);
            var normal=Setting("Arena 1 basic Raider",playerCombat);normal.enemyHealth=45;normal.moveSpeed=2.8f;normal.aggroRadius=12;normal.meleeDamage=6;normal.attackCooldown=1.2f;Record(normal);
            var buggySource=(d.buggyPool??Array.Empty<EnemyBuggy>()).First(b=>b).tuning;
            var buggyTuning=Setting("Arena 1 Buggy",buggySource);buggyTuning.health=105;buggyTuning.maxSpeed=6.5f;buggyTuning.aggroRadius=18;Record(buggyTuning);
            var bossTuning=Setting("Arena 1 Boss Raider",normal);bossTuning.enemyHealth=520;bossTuning.moveSpeed=2.5f;bossTuning.aggroRadius=21;bossTuning.meleeReach=2.7f;bossTuning.meleeDamage=16;bossTuning.attackCooldown=1.8f;Record(bossTuning);
            lines.Add("Player base bullet15 -> effective "+playerCombat.ScalePlayerDamage(playerCombat.bulletDamage)+" (x0.35). HP: Raider45 / Buggy105 / Monster330 / Boss Raider520.");
            // Retire only the old authored human placements; the existing reserve pools remain available to other tooling.
            var oldHumans=layout.humanEnemiesRoot.GetComponentsInChildren<Raider>(true);
            d.pool=(d.pool??Array.Empty<Raider>()).Where(r=>r&&!oldHumans.Contains(r)).ToArray();d.authoredRaiders=Array.Empty<Raider>();
            foreach(var old in oldHumans)Object.DestroyImmediate(old.gameObject);
            d.encounters=Array.Empty<EncounterSpawn>();d.groupCenters=Array.Empty<Vector3>();d.buggySpawnPoints=Array.Empty<Transform>();d.repeatAuthoredEnemies=false;
            foreach(var reserve in d.pool){reserve.tuning=normal;reserve.gameObject.SetActive(false);Identity(reserve.gameObject,EnemyRole.Raider,"RAIDER");Record(reserve);}
            var raiderPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Prefabs/Wasteland Raider.prefab");
            for(int i=0;i<RaiderPoints.Length;i++) {
                var p=RaiderPoints[i];var position=Ground(World(p.x,p.y));
                var go=RoadGunnerLevelAuthoring.Place(layout,raiderPrefab,position,Vector3.up,45+i*47,1,false,false);
                go.name="Arena 1 · Encounter "+Pockets[i]+" · Raider "+(i+1).ToString("00");var raider=go.GetComponent<Raider>();raider.tuning=normal;raider.session=s;raider.arenaBoss=false;
                Identity(go,EnemyRole.Raider,"RAIDER · BASIC");Record(raider);Record(go.transform);lines.Add(go.name+" world="+position);
            }
            // Two existing physical buggies, both in open navigation spaces.
            var existingBuggies=(d.authoredBuggies??Array.Empty<EnemyBuggy>()).Concat(d.buggyPool??Array.Empty<EnemyBuggy>()).Where(b=>b).Distinct().ToArray();
            var buggyPoints=new[]{new Vector2(7,22),new Vector2(24,39)};
            if(existingBuggies.Length<2)throw new InvalidOperationException("The existing level must retain at least two buggy instances.");
            for(int i=0;i<existingBuggies.Length;i++) {
                var b=existingBuggies[i];b.tuning=buggyTuning;b.session=s;
                if(i<2){var at=Ground(World(buggyPoints[i].x,buggyPoints[i].y));b.transform.SetParent(layout.enemyVehiclesRoot,true);b.transform.SetPositionAndRotation(at+Vector3.up*.65f,Axis*Quaternion.Euler(0,180+i*40,0));b.name="Arena 1 · Buggy "+(i+1);b.gameObject.SetActive(true);lines.Add(b.name+" world="+b.transform.position);}
                else {b.transform.SetParent(d.transform,true);b.gameObject.SetActive(false);}
                Identity(b.gameObject,EnemyRole.Buggy,"BUGGY · VEHICLE ELITE");Record(b);Record(b.transform);
            }
            d.buggyPool=existingBuggies;d.authoredBuggies=existingBuggies.Take(2).ToArray();
            // Keep Ashmaw, its existing articulated art and actual telegraphed smash implementation.
            var monsters=RoadGunnerLevelAuthoring.InScene<WastelandMutant>();if(monsters.Length!=1)throw new InvalidOperationException("Expected exactly one existing Monster mini-boss; found "+monsters.Length);
            var monster=monsters[0];monster.session=s;monster.maximumHealth=330;monster.transform.position=Ground(World(41,58));if(monster.spawnPoint)monster.spawnPoint.position=monster.transform.position;monster.name="MINI BOSS · Monster Ashmaw";Identity(monster.gameObject,EnemyRole.MutantMiniBoss,"MONSTER · MINI BOSS");Record(monster);Record(monster.transform);Record(monster.spawnPoint);
            // Retire the truck only in this Arena. Its reusable art/prefab/scripts remain intact.
            foreach(var truck in RoadGunnerLevelAuthoring.InScene<TruckArenaBoss>())Object.DestroyImmediate(truck.gameObject);
            var prior=layout.authoredContentRoot.Find("Arena 1 final boss");if(prior)Object.DestroyImmediate(prior.gameObject);
            var bossRoot=RoadGunnerLevelAuthoring.Section(layout.authoredContentRoot,"Arena 1 final boss");
            var bossPrefab=BuildBossPrefab(raiderPrefab,bossTuning);
            var bossGO=(GameObject)PrefabUtility.InstantiatePrefab(bossPrefab,bossRoot);bossGO.name="FINAL BOSS · Raider Warlord";
            bossGO.transform.SetPositionAndRotation(Ground(World(-33,35)),Axis*Quaternion.Euler(0,180,0));var boss=bossGO.GetComponent<Raider>();boss.session=s;
            var home=RoadGunnerLevelAuthoring.Section(bossRoot,"Boss Raider home");home.SetPositionAndRotation(boss.transform.position,boss.transform.rotation);boss.spawnPoint=home;Record(boss);Record(boss.transform);
            var roster=s.zone1?s.zone1:s.GetComponent<Zone1EnemyRoster>();if(!roster)roster=s.gameObject.AddComponent<Zone1EnemyRoster>();roster.session=s;roster.bosses=new CombatTarget[]{monster,boss};s.zone1=roster;Record(roster);
            foreach(var bossHud in RoadGunnerLevelAuthoring.InScene<Zone1BossHud>()){bossHud.session=s;bossHud.roster=roster;Record(bossHud);}
            lines.Add("Monster mini-boss world="+monster.transform.position+"; final Boss Raider world="+boss.transform.position);
            ConfigureUpgradesAndHud(layout,lines);
            MobileRevisionInstaller.ConfigureScene(layout);
            layout.referenceInspected=false;layout.authoringNotes="Arena 1 / 24 October 2026. Exact latest The War first-arena count/placement unavailable: explicit fallback, 12 basic Raiders in five pockets2/2/2/3/3,2 elite Buggies,1 Monster mini-boss,1 Boss Raider final encounter. Existing terrain, navigation, environment, vehicle, Metal Gear and harpoon systems retained. Attack/Health station objects are scene-authored and reusable.";
            Record(layout);Record(d);Record(s);RoadGunnerLevelAuthoring.Save(layout,false);
            foreach(var issue in RoadGunnerLevelAuthoring.Validate(layout))lines.Add(issue.type+": "+issue.text);
            File.WriteAllLines(ReportPath,lines);return string.Join("\n",lines);
        }
        static GameObject BuildBossPrefab(GameObject raiderPrefab,CombatTuning tuning) {
            var donor=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Art/ConceptWasteland/Prefabs/Wasteland Veteran.prefab");if(!donor)donor=raiderPrefab;
            var go=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(donor));
            try {
                go.name="Boss Raider";go.transform.localScale=Vector3.one*1.6f;
                var r=go.GetComponent<Raider>();r.arenaBoss=true;r.tuning=tuning;r.bossWindup=.85f;r.bossStrikeRadius=2.3f;r.bossStrikeOffset=2.6f;r.bossRecovery=.6f;r.bossGearReward=10;
                Identity(go,EnemyRole.BossRaider,"RAIDER WARLORD · FINAL BOSS");
                var old=go.transform.Find("Committed strike warning");if(old)Object.DestroyImmediate(old.gameObject);
                var marker=new GameObject("Committed strike warning",typeof(MeshFilter),typeof(MeshRenderer));marker.transform.SetParent(go.transform,false);marker.GetComponent<MeshFilter>().sharedMesh=RingMesh();marker.GetComponent<Renderer>().sharedMaterial=Material("Boss strike amber",new Color(.95f,.36f,.10f),true);marker.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;marker.GetComponent<Renderer>().receiveShadows=false;r.bossWarning=marker.transform;marker.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(go,AssetRoot+"/Boss Raider.prefab");
            } finally {PrefabUtility.UnloadPrefabContents(go);}
        }
        static Material Material(string name,Color color,bool unlit=false) {
            var path=AssetRoot+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}mat.SetColor("_BaseColor",color);if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",.12f);Record(mat);return mat;
        }
        static Mesh RingMesh() {
            string path=AssetRoot+"/Arena warning ring.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
            mesh=new Mesh{name="Arena warning ring"};var vertices=new Vector3[96];var indices=new int[48*6];
            for(int i=0;i<48;i++){float angle=i*Mathf.PI*2/48;vertices[i*2]=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));vertices[i*2+1]=vertices[i*2]*.88f;int next=(i+1)%48;int a=i*6;indices[a]=i*2;indices[a+1]=i*2+1;indices[a+2]=next*2;indices[a+3]=next*2;indices[a+4]=i*2+1;indices[a+5]=next*2+1;}
            mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static GameObject Shape(Transform parent,string name,PrimitiveType primitive,Vector3 at,Vector3 size,Material mat) {
            var go=GameObject.CreatePrimitive(primitive);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=size;Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go;
        }
        static ArenaUpgradeStation Station(Transform root,GameSession s,ArenaRunStats stats,ArenaUpgradeKind kind,Vector3 at) {
            var go=new GameObject(kind==ArenaUpgradeKind.Attack?"ATTACK +1 · Scrap weapon bench":"HEALTH +1 · Field repair terminal");go.transform.SetParent(root,false);go.transform.SetPositionAndRotation(StationGround(at,s.startPosition),Axis);
            var station=go.AddComponent<ArenaUpgradeStation>();station.session=s;station.stats=stats;station.kind=kind;station.radius=3.5f;
            var steel=Material("Station worn steel",new Color(.18f,.23f,.23f));var color=kind==ArenaUpgradeKind.Attack?new Color(.97f,.65f,.23f):new Color(.37f,.85f,.66f);var accent=Material(kind==ArenaUpgradeKind.Attack?"Attack amber":"Health mint",color);
            Shape(go.transform,"Low scrap plinth",PrimitiveType.Cylinder,new Vector3(0,.16f,0),new Vector3(2.5f,.16f,2.5f),steel);
            var available=new GameObject("Available upgrade");available.transform.SetParent(go.transform,false);station.availableVisual=available;
            Shape(available.transform,"Rugged terminal",PrimitiveType.Cube,new Vector3(0,.85f,0),new Vector3(1.7f,1.25f,1),steel);Shape(available.transform,"Bright stat panel",PrimitiveType.Cube,new Vector3(0,1.54f,0),new Vector3(1.85f,.18f,1.3f),accent);
            Shape(available.transform,"Stat cross horizontal",PrimitiveType.Cube,new Vector3(0,1.68f,0),new Vector3(.85f,.08f,.22f),accent);Shape(available.transform,"Stat cross vertical",PrimitiveType.Cube,new Vector3(0,1.68f,0),new Vector3(.22f,.08f,.85f),accent);
            var highlight=new GameObject("Proximity highlight",typeof(MeshFilter),typeof(MeshRenderer));highlight.transform.SetParent(go.transform,false);highlight.transform.localPosition=Vector3.up*.05f;highlight.transform.localScale=Vector3.one*3.5f;highlight.GetComponent<MeshFilter>().sharedMesh=RingMesh();highlight.GetComponent<Renderer>().sharedMaterial=accent;station.highlight=highlight;highlight.SetActive(false);
            var label=new GameObject("Readable stat label",typeof(TextMesh));label.transform.SetParent(go.transform,false);label.transform.localPosition=new Vector3(0,2.5f,0);label.transform.rotation=Quaternion.Euler(57,45,0);var text=label.GetComponent<TextMesh>();text.text=kind==ArenaUpgradeKind.Attack?"ATTACK\n+1":"HEALTH\n+1";text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.fontSize=48;text.characterSize=.10f;text.color=color;station.displayText=text;Record(station);return station;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size) {var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;}
        static Text Label(string name,Transform parent,Font font,string value,Vector2 at,Vector2 size,int fontSize) {var rect=Rect(name,parent,Vector2.one,at,size);var label=rect.gameObject.AddComponent<Text>();label.font=font;label.text=value;label.fontSize=fontSize;label.color=new Color(.91f,.88f,.78f);label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;return label;}
        static void LegendEntry(Transform panel,Font font,string label,MinimapMarkerKind kind,int column,int row,Color tint) {
            float x=column==0?-430:-130,y=-112-row*58;
            var text=Label("Map key "+label,panel,font,label,new Vector2(x,y),new Vector2(220,40),24);text.alignment=TextAnchor.MiddleLeft;text.color=new Color(.94f,.92f,.84f);text.horizontalOverflow=HorizontalWrapMode.Overflow;
            var rect=Rect(label+" symbol",panel,Vector2.one,new Vector2(x-142,y),new Vector2(24,24));
            var icon=rect.gameObject.AddComponent<ArenaMinimapIcon>();icon.kind=kind;icon.color=tint;icon.raycastTarget=false;
        }
        static void ConfigureUpgradesAndHud(LevelLayout layout,List<string> lines) {
            var s=layout.session;var old=layout.authoredContentRoot.Find("Arena 1 stat stations");if(old)Object.DestroyImmediate(old.gameObject);var root=RoadGunnerLevelAuthoring.Section(layout.authoredContentRoot,"Arena 1 stat stations");
            var stats=s.GetComponent<ArenaRunStats>();if(!stats)stats=s.gameObject.AddComponent<ArenaRunStats>();stats.session=s;stats.tuning=s.turret.tuning;s.runStats=stats;s.health.runStats=stats;
            stats.stations=new[]{Station(root,s,stats,ArenaUpgradeKind.Attack,World(-13,-8)),Station(root,s,stats,ArenaUpgradeKind.Health,World(19,30))};Record(stats);Record(s.health);
            foreach(var station in stats.stations)lines.Add(station.kind+" +1 station world="+station.transform.position+", radius="+station.radius);
            var safe=s.hud.transform;var previous=safe.Find("Arena 1 HUD");if(previous)Object.DestroyImmediate(previous.gameObject);var ui=Rect("Arena 1 HUD",safe,Vector2.zero,Vector2.zero,Vector2.zero);ui.anchorMin=Vector2.zero;ui.anchorMax=Vector2.one;ui.offsetMin=ui.offsetMax=Vector2.zero;
            var font=s.hud.healthText.font;
            var statPill=Rect("Compact run stats",ui,new Vector2(0,1),new Vector2(285,-355),new Vector2(500,70));
            statPill.anchorMin=new Vector2(0,1);statPill.anchorMax=Vector2.one;statPill.offsetMin=new Vector2(35,-390);statPill.offsetMax=new Vector2(-434,-320);
            var statBackground=statPill.gameObject.AddComponent<Image>();statBackground.color=new Color(.035f,.07f,.075f,.94f);statBackground.raycastTarget=false;
            s.hud.statsText=Label("Arena stats",statPill,font,"ATK 1   HP 1",new Vector2(-250,-35),new Vector2(480,58),34);s.hud.statsText.fontStyle=FontStyle.Bold;
            var statTextRect=(RectTransform)s.hud.statsText.transform;statTextRect.anchorMin=Vector2.zero;statTextRect.anchorMax=Vector2.one;statTextRect.offsetMin=new Vector2(10,5);statTextRect.offsetMax=new Vector2(-10,-5);
            var keyButtonRect=Rect("Map key button",ui,new Vector2(0,1),new Vector2(165,-445),new Vector2(260,86));
            var keyImage=keyButtonRect.gameObject.AddComponent<Image>();keyImage.color=new Color(.15f,.28f,.28f,.96f);
            var keyButton=keyButtonRect.gameObject.AddComponent<Button>();keyButton.targetGraphic=keyImage;
            Label("Map key button label",keyButtonRect,font,"MAP KEY",new Vector2(-130,-43),new Vector2(240,65),28);
            var statsPanel=Rect("Map key panel",ui,new Vector2(0,1),new Vector2(345,-685),new Vector2(620,360));
            var statsBackground=statsPanel.gameObject.AddComponent<Image>();statsBackground.color=new Color(.035f,.07f,.075f,.97f);statsBackground.raycastTarget=false;
            Label("Map key heading",statsPanel,font,"MAP KEY",new Vector2(-310,-39),new Vector2(580,58),28);
            LegendEntry(statsPanel,font,"PLAYER",MinimapMarkerKind.Player,0,0,Color.white);
            LegendEntry(statsPanel,font,"RAIDER",MinimapMarkerKind.Raider,1,0,new Color(1,.75f,.48f));
            LegendEntry(statsPanel,font,"BUGGY",MinimapMarkerKind.Buggy,0,1,new Color(1,.43f,.16f));
            LegendEntry(statsPanel,font,"MINI-BOSS",MinimapMarkerKind.MiniBoss,1,1,new Color(.83f,.43f,1));
            LegendEntry(statsPanel,font,"BOSS",MinimapMarkerKind.Boss,0,2,new Color(1,.22f,.28f));
            LegendEntry(statsPanel,font,"ATTACK",MinimapMarkerKind.Attack,1,2,new Color(1,.8f,.27f));
            LegendEntry(statsPanel,font,"HEALTH",MinimapMarkerKind.Health,0,3,new Color(.35f,1,.51f));
            LegendEntry(statsPanel,font,"WORKSHOP",MinimapMarkerKind.Workshop,1,3,new Color(.29f,.88f,.91f));
            s.hud.upgradeFeedbackText=Label("Upgrade feedback",ui,font,"",new Vector2(-540,-750),new Vector2(850,65),38);
            var feedbackRect=(RectTransform)s.hud.upgradeFeedbackText.transform;feedbackRect.anchorMin=new Vector2(0,1);feedbackRect.anchorMax=Vector2.one;feedbackRect.offsetMin=new Vector2(35,-785);feedbackRect.offsetMax=new Vector2(-35,-715);
            var feedbackOutline=s.hud.upgradeFeedbackText.gameObject.AddComponent<Outline>();feedbackOutline.effectColor=new Color(.025f,.04f,.04f,.95f);feedbackOutline.effectDistance=new Vector2(2,-2);
            var killLabelTransform=s.hud.killsText.transform.parent.Find("Kill label");var killLabel=killLabelTransform?killLabelTransform.GetComponent<Text>():null;if(killLabel){killLabel.text="ENEMIES";Record(killLabel);}
            var panel=Rect("Arena minimap frame",ui,Vector2.one,new Vector2(-217,-515),new Vector2(362,390));var background=panel.gameObject.AddComponent<Image>();background.color=new Color(.035f,.07f,.075f,.94f);background.raycastTarget=false;
            Label("Minimap title",panel,font,"ARENA 01",new Vector2(-181,-24),new Vector2(338,35),24);
            var map=Rect("Actual arena map",panel,new Vector2(.5f,.5f),new Vector2(0,-16),new Vector2(338,338));var mapBackground=map.gameObject.AddComponent<Image>();mapBackground.color=new Color(.13f,.18f,.18f,.96f);mapBackground.raycastTarget=false;
            var minimap=ui.gameObject.AddComponent<ArenaMinimap>();minimap.session=s;minimap.layout=layout;minimap.mapRect=map;minimap.legendPanel=statsPanel.gameObject;minimap.legendVisibleDuration=8;statsPanel.gameObject.SetActive(false);UnityEventTools.AddPersistentListener(keyButton.onClick,minimap.ToggleLegend);minimap.upgrades=stats.stations;minimap.workshops=RoadGunnerLevelAuthoring.InScene<RepairWorkshop>();s.minimap=minimap;Record(minimap);Record(s.hud);
            s.hud.overlay.transform.SetAsLastSibling();
        }
    }
}
