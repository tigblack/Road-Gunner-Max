using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Idempotent addition to the current OPEN arena. Never calls the historical rebalance installers.</summary>
    public static class ExplorationArenaInstaller
    {
        public const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";
        public const string AssetRoot = "Assets/RoadGunner/Levels/Greenwood/Exploration";
        public const string LayoutAssetPath = AssetRoot + "/Exploration layout.asset";
        public const string SceneRootName = "Arena exploration";
        static readonly Quaternion Axis = Quaternion.Euler(0, 45, 0);
        static readonly Quaternion InverseAxis = Quaternion.Inverse(Axis);
        sealed class Lane { public ExplorationArenaLayoutData.Route source; public Vector2[] points; }
        sealed class RelocationReport
        {
            public int attempts;
            readonly Dictionary<string,int> counts=new Dictionary<string,int>();
            readonly Dictionary<string,List<string>> examples=new Dictionary<string,List<string>>();
            readonly Dictionary<string,string> last=new Dictionary<string,string>();
            readonly Dictionary<string,int> blockers=new Dictionary<string,int>();
            readonly Dictionary<string,int> groundContacts=new Dictionary<string,int>();
            public void SupportingContact(Collider surface)
            {string path=Hierarchy(surface.transform);groundContacts[path]=groundContacts.TryGetValue(path,out var count)?count+1:1;}
            public void Reject(string reason,Vector2 at,IEnumerable<Collider> clashes=null)
            {
                counts[reason]=counts.TryGetValue(reason,out var count)?count+1:1;
                var paths=clashes==null?Array.Empty<string>():clashes.Select(c=>Hierarchy(c.transform)).Distinct().ToArray();
                string sample="W="+at+ (paths.Length>0?" clashes="+string.Join("; ",paths):"");
                if(!examples.TryGetValue(reason,out var list)){list=new List<string>();examples.Add(reason,list);}
                if(list.Count<3)list.Add(sample);last[reason]=sample;
                foreach(var path in paths)blockers[path]=blockers.TryGetValue(path,out var n)?n+1:1;
            }
            public string Describe()
            {
                var lines=new List<string>{"Candidate attempts="+attempts+"; local search radius87.5m, then whole-arena6.25m grid with navigation reachability."};
                foreach(var pair in counts)
                {lines.Add(pair.Key+" rejected="+pair.Value);lines.AddRange(examples[pair.Key].Select(s=>"  "+s));if(!examples[pair.Key].Contains(last[pair.Key]))lines.Add("  last "+last[pair.Key]);}
                lines.Add("All actual clashing solids (candidate count):");
                lines.AddRange(blockers.OrderByDescending(p=>p.Value).Select(p=>"  "+p.Value+" × "+p.Key));
                lines.Add("Allowed shallow upward contacts with flat ground surfaces:");
                lines.AddRange(groundContacts.OrderByDescending(p=>p.Value).Select(p=>"  "+p.Value+" × "+p.Key));return string.Join("\n",lines);
            }
        }
        static Lane[] lanes;
        static ExplorationArenaLayoutData data;
        static LevelLayout layout;
        static Collider terrain;
        static Transform ownedRoot;
        static Font font;
        static RepairWorkshop[] workshops;
        static readonly List<string> log = new List<string>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public static Vector3 World(Vector2 w, float height = 0) => Axis * new Vector3(w.x * 2.5f, height, w.y * 2.5f);
        public static Vector2 ToW(Vector3 world) { var p = InverseAxis * world; return new Vector2(p.x, p.z) / 2.5f; }

        [MenuItem("Tools/Road Gunner/Arena 1/Install Exploration Arena")]
        public static void InstallBatch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            Debug.Log(Install());
        }

        public static string Install(bool bakeNavigationAndMinimap = true)
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open the current saved Greenwood arena in Edit Mode.");
            log.Clear(); materials.Clear();
            layout = RoadGunnerLevelAuthoring.PrepareCurrent();
            if (!layout.ground || !layout.ground.sharedMesh) throw new InvalidOperationException("The existing ground is required.");
            terrain = layout.ground.GetComponent<Collider>();
            if (!terrain) throw new InvalidOperationException("The original ground collider is required.");
            Directory.CreateDirectory(AssetRoot); AssetDatabase.Refresh();
            data = AssetDatabase.LoadAssetAtPath<ExplorationArenaLayoutData>(LayoutAssetPath);
            if (!data) { data = ScriptableObject.CreateInstance<ExplorationArenaLayoutData>(); data.SetDefaults(); AssetDatabase.CreateAsset(data, LayoutAssetPath); }
            if (data.UpgradeGeometry()) { Dirty(data); log.Add("Geometry revision"+data.geometryRevision+": immutable-cliff routes retained; secret turnarounds reserve >=10.5m; reward, region and HUD authoring retained."); }
            ValidateDefinition(); RestoreBaseline();
            var old = layout.authoredContentRoot.Find(SceneRootName); if (old) Object.DestroyImmediate(old.gameObject);
            ownedRoot = Node(layout.authoredContentRoot, SceneRootName);
            lanes = data.routes.Select(r => new Lane { source = r, points = Sample(r.nodesW) }).ToArray();
            var session = layout.session;
            workshops = RoadGunnerLevelAuthoring.InScene<RepairWorkshop>();
            workshopLocations = workshops.Select(w => ToW(w.transform.position)).ToArray();
            var oldCombat = CombatSnapshot(session);
            font = session.hud ? session.hud.GetComponentInParent<Canvas>().GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.font).FirstOrDefault(f => f) : null;
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ClearPhysicalRoutesUsingBaselineNavigation();
            var surface = BuildWorldSurface();
            ApplyContinuousSurface(surface);
            BuildRoadUnion(surface);
            BuildRouteMarkers();
            var exploration = session.exploration ? session.exploration : session.GetComponent<ArenaExploration>();
            if (!exploration) exploration = session.gameObject.AddComponent<ArenaExploration>();
            exploration.session = session; session.exploration = exploration;
            var tuning = AssetDatabase.LoadAssetAtPath<ArenaExplorationTuning>(AssetRoot + "/Exploration tuning.asset");
            if (!tuning) { tuning = ScriptableObject.CreateInstance<ArenaExplorationTuning>(); AssetDatabase.CreateAsset(tuning, AssetRoot + "/Exploration tuning.asset"); }
            exploration.tuning = tuning;
            exploration.zones = data.regions.Select(r => new ArenaExplorationZone { id = r.id, displayName = r.displayName, hint = r.hint, center = Ground(r.centerW), radius = r.radiusMeters }).ToArray();
            BuildRegionLandmarks();
            BuildRepairHub();
            exploration.caches = data.caches.Select(c => BuildCache(c, exploration)).ToArray();
            ConfigureAffinities(session);
            ExplorationHudInstaller.Install(session);
            BuildRecoveryLaybys();
            ConfigureBounds();
            Physics.SyncTransforms();
            ValidatePhysicalCenterlines();
            if (CombatSnapshot(session) != oldCombat) throw new InvalidOperationException("Exploration installation changed an existing combat placement or vehicle/combat configuration.");
            Dirty(data); Dirty(exploration); Dirty(session); Dirty(layout);
            layout.navigationNeedsBake = true;
            if (bakeNavigationAndMinimap)
            {
                RoadGunnerLevelAuthoring.Bake(layout);
                if (!session.minimap) throw new InvalidOperationException("The existing arena minimap is required for a matching bake.");
                ArenaMinimapBaker.BakeAndConfigure(layout, session.minimap);
            }
            RoadGunnerLevelAuthoring.Save(layout, false);
            log.Insert(0, "Exploration installed over the current open arena: 4 regions; " + data.routes.Length + " routes; " + data.pockets.Length + " open pockets; 5 breakable crates; 3 secret chests.");
            log.Add("Original terrain/perimeter, 16 combat targets and their coordinates, base HP, vehicle tuning and inputs retained. No historical Arena1 installer was invoked.");
            log.Add("Authoring source: " + LayoutAssetPath + "; W=screen east/north, yaw45, 2.5m/W. Road mesh, texture, clearance and markers derive from the same sampled routes.");
            log.Add("Navigation/minimap baked=" + bakeNavigationAndMinimap + "; physical route driving still requires the separate play checks.");
            return string.Join("\n", log);
        }

        static void ValidateDefinition()
        {
            if (data.routes == null || data.regions == null || data.regions.Length != 4 || data.caches == null)
                throw new InvalidOperationException("Exploration data requires routes, four regions and reward definitions.");
            var ids = new HashSet<string>();
            foreach (var route in data.routes)
                if (route == null || string.IsNullOrEmpty(route.id) || !ids.Add(route.id) || route.nodesW == null || route.nodesW.Length < 2 || route.widthMeters < 8)
                    throw new InvalidOperationException("Each exploration route needs a unique ID, at least two nodes and width >=8m.");
            ids.Clear();
            foreach (var cache in data.caches)
                if (cache == null || string.IsNullOrEmpty(cache.id) || !ids.Add(cache.id) || cache.amount <= 0)
                    throw new InvalidOperationException("Each exploration cache needs a unique ID and positive reward.");
        }
        static void Dirty(Object value)
        {
            if (!value) return; EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        static string Id(Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static T Resolve<T>(string id) where T : Object => GlobalObjectId.TryParse(id, out var parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as T : null;
        static string Hierarchy(Transform t) { var names = new List<string>(); while (t) { names.Add(t.name); t = t.parent; } names.Reverse(); return string.Join("/", names); }
        static void RestoreBaseline()
        {
            foreach (var item in data.baselineMoves)
            {
                var target = Resolve<Transform>(item.globalId); if (!target) throw new InvalidOperationException("Moved baseline prop cannot be resolved: " + item.hierarchy);
                target.SetPositionAndRotation(item.position, item.rotation); Dirty(target);
            }
            foreach (var item in data.baselineSurfaces)
            {
                var target = Resolve<Renderer>(item.globalId); if (target) { target.sharedMaterials = item.materials; Dirty(target); }
            }
            data.baselineMoves.Clear(); data.baselineSurfaces.Clear(); Physics.SyncTransforms();
        }
        static Transform Node(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
        static Vector3 Ground(Vector2 w, float offset = 0)
        {
            var p = World(w); if (!terrain.Raycast(new Ray(p + Vector3.up * 40, Vector3.down), out var hit, 100))
                throw new InvalidOperationException("No actual ground at W=" + w);
            return hit.point + Vector3.up * offset;
        }
        static Vector2[] Sample(Vector2[] nodes)
        {
            var result = new List<Vector2>();
            for (int i = 0; i < nodes.Length - 1; i++)
            {
                var a = nodes[Mathf.Max(0, i - 1)]; var b = nodes[i]; var c = nodes[i + 1]; var d = nodes[Mathf.Min(nodes.Length - 1, i + 2)];
                int count = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(b, c) * 2.5f / 2));
                for (int k = 0; k < count; k++)
                {
                    float t = k / (float)count, t2 = t * t, t3 = t2 * t;
                    result.Add(.5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3));
                }
            }
            result.Add(nodes[nodes.Length - 1]); return result.ToArray();
        }
        static float Distance(Vector2 p, Vector2[] path)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < path.Length; i++)
            { var delta = path[i] - path[i - 1]; float t = delta.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector2.Dot(p - path[i - 1], delta) / delta.sqrMagnitude); best = Mathf.Min(best, Vector2.Distance(p, path[i - 1] + t * delta)); }
            return best * 2.5f;
        }
        static float SurfaceDistance(Vector2 p)
        {
            float d = float.PositiveInfinity;
            foreach (var lane in lanes) d = Mathf.Min(d, Distance(p, lane.points) - lane.source.widthMeters * .5f);
            foreach (var pocket in data.pockets) d = Mathf.Min(d, Vector2.Distance(p, pocket.centerW) * 2.5f - pocket.radiusMeters);
            foreach (var layby in data.recoveryLaybysW) d = Mathf.Min(d, Vector2.Distance(p, layby) * 2.5f - 6);
            return d;
        }

        static Collider[] BaselineSolids() => layout.obstaclesRoot.GetComponentsInChildren<Collider>(false)
            .Concat(layout.decorationRoot.GetComponentsInChildren<Collider>(false))
            .Where(c => c && c.enabled && !c.isTrigger && !c.GetComponentInParent<CombatTarget>() && !c.GetComponentInParent<RepairWorkshop>()).ToArray();
        static bool Protected(Transform owner)
        {
            if (!owner) return false;
            for (var category = owner; category; category = category.parent)
                if (category.name == "01b · Continuous cliff foundations" || category.name == "01 · Rock boundaries and islands") return true;
            if (owner.GetComponentInChildren<RepairWorkshop>()) return true;
            // Classify the prefab/root itself, not nearby props in a section named "North gate".
            // In particular, "Corrugated" contains the substring "gate" but is an ordinary movable fence.
            return Regex.Split(owner.name, "[^A-Za-z]+").Any(word =>
                string.Equals(word, "cliff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(word, "gate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(word, "bunker", StringComparison.OrdinalIgnoreCase));
        }
        static Transform Owner(Collider collider)
        {
            var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(collider.gameObject); if (prefab) return prefab.transform;
            var t = collider.transform;
            if (t.name == "Smooth driving collision" || t.name == "Collision") t = t.parent;
            return t;
        }
        static void ClearPhysicalRoutesUsingBaselineNavigation()
        {
            var surface=RoadGunnerLevelAuthoring.InScene<NavMeshSurface>().FirstOrDefault();
            if(!surface)throw new InvalidOperationException("The existing level navigation surface is required for deterministic shoulder reachability.");
            var previousData=surface.navMeshData;
            var previousCollect=surface.collectObjects;var previousGeometry=surface.useGeometry;var previousMask=surface.layerMask;
            bool previousOverrideVoxel=surface.overrideVoxelSize;float previousVoxel=surface.voxelSize;float previousMinRegion=surface.minRegionArea;
            NavMeshData temporary=null;
            try
            {
                // All previous exploration geometry has already been destroyed and baseline props restored.
                // Never use the previous final bake, whose obstacle positions depend on the preceding install.
                surface.collectObjects=CollectObjects.All;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask=1;surface.overrideVoxelSize=true;surface.voxelSize=.18f;surface.minRegionArea=3;
                Physics.SyncTransforms();var timer=System.Diagnostics.Stopwatch.StartNew();surface.BuildNavMesh();timer.Stop();
                temporary=surface.navMeshData;
                if(!temporary||temporary==previousData)throw new InvalidOperationException("Restored-baseline navigation produced no temporary data.");
                temporary.hideFlags=HideFlags.DontSave;surface.AddData();
                log.Add("Baseline-only navigation rebuilt in memory for shoulder reachability: "+timer.Elapsed.TotalSeconds.ToString("F3")+"s; previous saved Navigation.asset retained.");
                ClearPhysicalRoutes();
            }
            finally
            {
                surface.RemoveData();surface.navMeshData=previousData;
                surface.collectObjects=previousCollect;surface.useGeometry=previousGeometry;surface.layerMask=previousMask;
                surface.overrideVoxelSize=previousOverrideVoxel;surface.voxelSize=previousVoxel;surface.minRegionArea=previousMinRegion;
                if(previousData)surface.AddData();
                if(temporary&&temporary!=previousData&&!AssetDatabase.Contains(temporary))Object.DestroyImmediate(temporary);
            }
        }
        static void ClearPhysicalRoutes()
        {
            var solids = BaselineSolids();
            var immutableOverlaps = solids.Where(c => c.bounds.min.y < 2.8f && Protected(Owner(c)))
                .Where(SurfaceOverlapsCollider).Select(c => Hierarchy(c.transform) + " at W=" + ToW(c.bounds.center)).ToArray();
            if (immutableOverlaps.Length > 0)
                throw new InvalidOperationException("Authored roads/pockets intersect " + immutableOverlaps.Length + " immutable solids (first20):\n" +
                    string.Join("\n", immutableOverlaps.Take(20)) + "\nAdjust the editable routes instead of moving these landmarks.");
            var owners = solids.Select(Owner).Where(t => t && !Protected(t)).Distinct().ToArray();
            int moved = 0;
            foreach (var owner in owners)
            {
                var own = owner.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger && c.bounds.min.y < 2.8f).ToArray();
                if (own.Length == 0) continue;
                bool obstruction = own.Any(SurfaceOverlapsCollider);
                if (!obstruction) continue;
                Bounds b = own[0].bounds; foreach (var c in own) b.Encapsulate(c.bounds);
                float footprint = new Vector2(b.extents.x, b.extents.z).magnitude;
                var original = owner.position; bool found = false; Vector3 destination = original;
                var report = new RelocationReport();
                for (int ring = 1; ring <= 14 && !found; ring++) for (int step = 0; step < 24 && !found; step++)
                {
                    float angle = (step * 15 + moved * 29) * Mathf.Deg2Rad;
                    var candidateW = ToW(original) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring * 2.5f;
                    found=TryShoulder(owner,own,solids,original,candidateW,footprint,true,report,out destination);
                }
                if(!found)
                {
                    var candidates=new List<Vector2>();
                    for(float n=data.minW.y+2;n<data.maxW.y-2;n+=2.5f)for(float e=data.minW.x+2;e<data.maxW.x-2;e+=2.5f)candidates.Add(new Vector2(e,n));
                    foreach(var candidateW in candidates.OrderBy(p=>(p-ToW(original)).sqrMagnitude))
                    {if(TryShoulder(owner,own,solids,original,candidateW,footprint,true,report,out destination)){found=true;break;}}
                }
                if (!found) throw new InvalidOperationException("No safe shoulder relocation for " + Hierarchy(owner) + " at W=" + ToW(original) + ", footprint radius=" + footprint + "m.\n" + report.Describe());
                data.baselineMoves.Add(new ExplorationArenaLayoutData.BaselineMove { globalId = Id(owner), hierarchy = Hierarchy(owner), position = original, rotation = owner.rotation, reason = "Overlapped exploration road/pocket clearance; relocated intact to an unused shoulder." });
                owner.position = destination; Dirty(owner); Physics.SyncTransforms(); moved++;
                log.Add("Relocated "+Hierarchy(owner)+" fromW="+ToW(original)+" toW="+ToW(destination)+"; candidates="+report.attempts+"; oriented solid/road checks passed.");
            }
            log.Add("Relocated " + moved + " complete baseline prop instances; original transforms retained in the authoring asset. Perimeter, sealed gates and workshop protected.");
        }
        static bool TryShoulder(Transform owner,Collider[] own,Collider[] solids,Vector3 original,Vector2 candidateW,float footprint,bool requireNavigation,RelocationReport report,out Vector3 candidate)
        {
            report.attempts++;candidate=World(candidateW);
            if(!terrain.Raycast(new Ray(candidate+Vector3.up*40,Vector3.down),out var groundHit,100))
            {report.Reject("no ground",candidateW);return false;}
            candidate=groundHit.point;candidate.y+=original.y-Ground(ToW(original)).y;var delta=candidate-original;
            if(!ColliderCornersInsideBounds(own,delta)){report.Reject("outside authored bounds",candidateW);return false;}
            if(own.Any(c=>SurfaceOverlapsCollider(c,delta))){report.Reject("actual road/pocket reserve",candidateW);return false;}
            var clashes=new HashSet<Collider>();
            foreach(var part in own)
            {
                var shifted=new Bounds(part.bounds.center+delta,part.bounds.size);
                foreach(var solid in solids)
                {
                    if(!solid||!solid.enabled||solid.transform.IsChildOf(owner)||solid.bounds.min.y>=2.8f||!shifted.Intersects(solid.bounds))continue;
                    if(Physics.ComputePenetration(part,part.transform.position+delta,part.transform.rotation,solid,solid.transform.position,solid.transform.rotation,out var direction,out var penetration)&&penetration>.005f)
                    {
                        // Existing prop feet may be embedded a few centimetres in a thin terrain/road pad.
                        // A full-height obstacle or a lateral contact is still rejected without exception.
                        bool support=solid.bounds.size.y<=.25f&&solid.bounds.max.y<=groundHit.point.y+.25f&&direction.y>.85f&&penetration<=.25f;
                        if(support)report.SupportingContact(solid);else clashes.Add(solid);
                    }
                }
            }
            if(clashes.Count>0){report.Reject("actual solid overlap",candidateW,clashes);return false;}
            var candidatePosition=candidate;
            if(workshops.Any(w=>AttackRange.SquaredDistance(candidatePosition,w.transform.position)<Mathf.Pow(w.healingRadius+footprint+5,2)))
            {report.Reject("workshop entrance/service reserve",candidateW);return false;}
            if(requireNavigation)
            {
                var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(layout.session.startPosition,out var start,3,NavMesh.AllAreas)||!NavMesh.SamplePosition(candidate,out var end,1,NavMesh.AllAreas)||
                    !NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                {report.Reject("fallback outside connected existing navigation",candidateW);return false;}
            }
            return true;
        }
        static bool ColliderCornersInsideBounds(Collider[] colliders,Vector3 delta)
        {
            const float insetW=.4f;
            foreach(var collider in colliders)
            {
                if(collider is BoxCollider box)
                {
                    for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                    {
                        var corner=box.transform.TransformPoint(box.center+new Vector3(x*box.size.x*.5f,0,z*box.size.z*.5f))+delta;
                        var w=ToW(corner);if(w.x<data.minW.x+insetW||w.x>data.maxW.x-insetW||w.y<data.minW.y+insetW||w.y>data.maxW.y-insetW)return false;
                    }
                }
                else
                {
                    var bounds=collider.bounds;
                    for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                    {
                        var w=ToW(bounds.center+new Vector3(x*bounds.extents.x,0,z*bounds.extents.z)+delta);
                        if(w.x<data.minW.x+insetW||w.x>data.maxW.x-insetW||w.y<data.minW.y+insetW||w.y>data.maxW.y-insetW)return false;
                    }
                }
            }
            return true;
        }
        static bool SurfaceOverlapsCollider(Collider obstacle)
        {return SurfaceOverlapsCollider(obstacle,Vector3.zero);}
        static bool SurfaceOverlapsCollider(Collider obstacle,Vector3 translation)
        {
            // Use the actual oriented solid, rather than its large AABB's circumscribed circle.
            // Samples are <=2m apart; the 1.25m reserve also covers the interval between samples.
            foreach (var lane in lanes)
                foreach (var p in lane.points)
                    if (DistanceToSolid(obstacle,p,translation) < lane.source.widthMeters * .5f + 1.25f) return true;
            foreach (var pocket in data.pockets)
                if (DistanceToSolid(obstacle,pocket.centerW,translation) < pocket.radiusMeters + 1.25f) return true;
            foreach (var layby in data.recoveryLaybysW)
                if (DistanceToSolid(obstacle,layby,translation) < 7.25f) return true;
            return false;
        }
        static float DistanceToSolid(Collider obstacle,Vector2 w,Vector3 translation)
        {
            var position=World(w)-translation;position.y=Mathf.Clamp(1.1f-translation.y,obstacle.bounds.min.y+.01f,obstacle.bounds.max.y-.01f);
            var nearest=obstacle is BoxCollider || obstacle is SphereCollider || obstacle is CapsuleCollider ? obstacle.ClosestPoint(position) : obstacle.bounds.ClosestPoint(position);
            nearest.y=position.y;return Vector3.Distance(position,nearest);
        }
        static bool HorizontalOverlap(Bounds a, Bounds b, float padding) => a.min.x - padding <= b.max.x && a.max.x + padding >= b.min.x && a.min.z - padding <= b.max.z && a.max.z + padding >= b.min.z;

        static Material Mat(string name, Color tint)
        {
            if (materials.TryGetValue(name, out var cached)) return cached;
            string path = AssetRoot + "/" + name + ".mat"; var value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!value) { value = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(value, path); }
            value.SetColor("_BaseColor", tint); value.SetFloat("_Smoothness", .12f); value.enableInstancing = true; Dirty(value); materials.Add(name, value); return value;
        }
        static Material BuildWorldSurface()
        {
            const int size = 1024; const float span = 512;
            var distances = RasterSurfaceDistance(size, span, false);
            var clearings = RasterSurfaceDistance(size, span, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var world = new Vector3((x + .5f) / size * span - span * .5f, 0, (y + .5f) / size * span - span * .5f);
                var w = ToW(world); float noise = Mathf.PerlinNoise(world.x * .035f + 74, world.z * .035f + 96);
                Color color = Color.black; float total = 0;
                foreach (var region in data.regions)
                {
                    float d = Mathf.Max(8, Vector2.Distance(w, region.centerW) * 2.5f);
                    float weight = 1 / Mathf.Pow(d, 3); color += region.sandColor * weight; total += weight;
                }
                color /= Mathf.Max(.000000000001f, total); color *= .92f + noise * .16f;
                // Keep the travelled lane visible through broad combat clearings, using the same real routes.
                float clearing = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-2, 3, clearings[y * size + x]));
                color = Color.Lerp(color, color * 1.045f, clearing * .5f);
                float roadDistance = distances[y * size + x];
                float worn = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-1.5f, 2.7f, roadDistance + (noise - .5f) * 1.1f));
                color = Color.Lerp(color, new Color(.85f,.55f,.22f) * (.96f + noise * .08f), worn * .88f);
                float shoulder = Mathf.Exp(-Mathf.Pow((roadDistance - .25f) / .65f, 2));
                color = Color.Lerp(color, new Color(.57f,.38f,.21f), shoulder * .30f);
                float hub = float.PositiveInfinity; for (int i = 0; i < workshopLocations.Length; i++) hub = Mathf.Min(hub, Vector2.Distance(w, workshopLocations[i]) * 2.5f);
                float hubRing = Mathf.Exp(-Mathf.Pow((hub - 10.5f) / .30f, 2));
                color = Color.Lerp(color, new Color(.36f,.77f,.61f), hubRing * .8f);
                color.a = 1; pixels[y * size + x] = color;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            string texturePath = AssetRoot + "/Exploration world surface.png";
            try { texture.SetPixels(pixels); texture.Apply(false, false); File.WriteAllBytes(texturePath, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.maxTextureSize = size; importer.SaveAndReimport();
            string path = AssetRoot + "/Exploration continuous ground.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path); var shader = Shader.Find("RoadGunner/Concept Desert");
            if (!shader) throw new InvalidOperationException("The existing Concept Desert shader is missing.");
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader; mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_WorldScale", span); mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath)); Dirty(mat); return mat;
        }
        // Rasterize bounded segment rectangles once instead of searching every road for every texture pixel.
        static float[] RasterSurfaceDistance(int size, float span, bool includePockets)
        {
            var result = Enumerable.Repeat(float.PositiveInfinity, size * size).ToArray();
            Action<Vector2,Vector2,float> stroke = (a,b,radius) => {
                float extent = radius + 4;
                int minX = Mathf.Clamp(Mathf.FloorToInt(((Mathf.Min(a.x,b.x)-extent)/span+.5f)*size),0,size-1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(((Mathf.Max(a.x,b.x)+extent)/span+.5f)*size),0,size-1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(((Mathf.Min(a.y,b.y)-extent)/span+.5f)*size),0,size-1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(((Mathf.Max(a.y,b.y)+extent)/span+.5f)*size),0,size-1);
                var delta=b-a;
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++) {
                    var p=new Vector2((x+.5f)/size*span-span*.5f,(y+.5f)/size*span-span*.5f);
                    float t=delta.sqrMagnitude<.00001f?0:Mathf.Clamp01(Vector2.Dot(p-a,delta)/delta.sqrMagnitude);
                    int index=y*size+x;result[index]=Mathf.Min(result[index],Vector2.Distance(p,a+t*delta)-radius);
                }
            };
            foreach(var lane in lanes)for(int i=1;i<lane.points.Length;i++) {
                var a=World(lane.points[i-1]);var b=World(lane.points[i]);stroke(new Vector2(a.x,a.z),new Vector2(b.x,b.z),lane.source.widthMeters*.5f);
            }
            if(includePockets)
            {
                foreach(var pocket in data.pockets){var p=World(pocket.centerW);stroke(new Vector2(p.x,p.z),new Vector2(p.x,p.z),pocket.radiusMeters);}
                foreach(var layby in data.recoveryLaybysW){var p=World(layby);stroke(new Vector2(p.x,p.z),new Vector2(p.x,p.z),6);}
            }
            return result;
        }
        static Vector2[] workshopLocations = Array.Empty<Vector2>();
        static void ApplyContinuousSurface(Material surface)
        {
            var groundRenderer = layout.ground.GetComponent<Renderer>();
            var renderers = new List<Renderer> { groundRenderer };
            var oldRoutes = layout.decorationRoot.Find("00 · Routes and clear ground");
            if (oldRoutes) renderers.AddRange(oldRoutes.GetComponentsInChildren<MeshRenderer>(true));
            foreach (var renderer in renderers.Where(r => r).Distinct())
            {
                data.baselineSurfaces.Add(new ExplorationArenaLayoutData.BaselineSurface { globalId = Id(renderer), materials = renderer.sharedMaterials });
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => surface).ToArray(); Dirty(renderer);
            }
        }
        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name; string path = AssetRoot + "/" + name + ".asset"; var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!saved) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); Dirty(saved); return saved;
        }
        static void BuildRoadUnion(Material material)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var lookup = new Dictionary<Vector2Int, int>();
            float step = Mathf.Clamp(data.surfaceCellMeters, 1, 3) / 2.5f;
            int nx = Mathf.CeilToInt((data.maxW.x - data.minW.x) / step), nz = Mathf.CeilToInt((data.maxW.y - data.minW.y) / step);
            Func<int, int, int> vertex = (x,z) => {
                var key = new Vector2Int(x,z); if (lookup.TryGetValue(key,out var found)) return found;
                var w = data.minW + new Vector2(x * step, z * step); int index = vertices.Count;
                vertices.Add(Ground(w, .035f)); lookup.Add(key,index); return index;
            };
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
            {
                var w = data.minW + new Vector2((x + .5f) * step, (z + .5f) * step);
                if (SurfaceDistance(w) > 1.5f) continue;
                int a = vertex(x,z), b = vertex(x,z+1), c = vertex(x+1,z), d = vertex(x+1,z+1);
                triangles.AddRange(new[] { a,b,c,c,b,d });
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = SaveMesh("Road exploration connected surface", mesh);
            var surface = Node(ownedRoot, "Physical connected roads and combat pockets").gameObject;
            surface.isStatic = true; surface.AddComponent<MeshFilter>().sharedMesh = mesh; surface.AddComponent<MeshRenderer>().sharedMaterial = material; surface.AddComponent<MeshCollider>().sharedMesh = mesh;
            log.Add("One non-overlapping connected road surface: " + vertices.Count + " indexed vertices, " + triangles.Count / 3 + " triangles; ground-conforming +3.5cm.");
        }
        static void BuildRouteMarkers()
        {
            var routesRoot = Node(ownedRoot,"Routes");
            foreach (var lane in lanes)
            {
                var route = Node(routesRoot,lane.source.id);
                for (int i=0;i<lane.points.Length;i++) { var point=Node(route,i.ToString("000")); point.position=Ground(lane.points[i],.06f); }
            }
            var pocketsRoot=Node(ownedRoot,"Combat pockets");
            foreach(var pocket in data.pockets) Node(pocketsRoot,pocket.id).position=Ground(pocket.centerW,.06f);
        }
        static GameObject Shape(Transform parent,string name,PrimitiveType type,Vector3 localPosition,Vector3 scale,Material material,bool collision=false)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=localPosition;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        static TextMesh Label(Transform parent,string name,string text,Vector3 localPosition,float characterSize,Color tint)
        {
            var node=Node(parent,name);node.localPosition=localPosition;node.rotation=layout.session.input.view?layout.session.input.view.transform.rotation:Quaternion.Euler(57,45,0);
            var label=node.gameObject.AddComponent<TextMesh>();label.font=font;label.text=text;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.fontSize=64;label.characterSize=characterSize;label.color=tint;
            var renderer=node.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return label;
        }
        static void BuildRegionLandmarks()
        {
            var root=Node(ownedRoot,"Region landmarks");
            for(int i=0;i<data.regions.Length;i++)
            {
                var region=data.regions[i];var marker=Node(root,region.id);marker.position=Ground(region.centerW);marker.rotation=Axis;
                // Landmark compositions sit outside the route union; no large overhead billboards.
                var visual=Node(marker,"Low zone landmark");visual.position=Ground(region.landmarkW);visual.rotation=Axis;
                var accent=Mat("Region "+region.id,Color.Lerp(region.sandColor,Color.white,.15f));var steel=Mat("Salvage dark steel",new Color(.18f,.22f,.22f));
                Shape(visual,"Wide marker footing",PrimitiveType.Cube,new Vector3(0,.10f,0),new Vector3(3.6f,.20f,1.1f),steel);
                Shape(visual,"Scrap route marker",PrimitiveType.Cube,new Vector3(0,1.05f,0),new Vector3(3.3f,1.5f,.28f),accent);
                Label(visual,"Region name",region.displayName,new Vector3(0,2.2f,0),.12f,new Color(.96f,.92f,.78f));
                for(int s=-1;s<=1;s+=2) Shape(visual,"Real marker post",PrimitiveType.Cube,new Vector3(s*1.5f,.70f,0),new Vector3(.18f,1.4f,.25f),steel,true);
                BuildRegionalProps(marker,region,i);
            }
        }
        static void BuildRegionalProps(Transform parent,ExplorationArenaLayoutData.Region region,int index)
        {
            string[][] families={new[]{"Cactus","Boulder","Cactus"},new[]{"Tire stack","Wooden supply crate","Tire and scrap pile"},new[]{"Fuel drums","Metal storage crate","Fuel drums"},new[]{"Boulder","Boulder","Weathered concrete"}};
            int added=0;
            for(int candidate=0;candidate<18&&added<3;candidate++)
            {
                string kind=families[index][added];
                string folder=kind=="Boulder"||kind=="Cactus"||kind=="Wooden supply crate"?"Assets/RoadGunner/Art/ConceptWasteland/Prefabs/":"Assets/RoadGunner/Levels/WastelandRestored/Prefabs/";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+kind+".prefab");if(!prefab)throw new InvalidOperationException("Missing existing regional prop: "+kind);
                float angle=(candidate*137.5f+index*23)*Mathf.Deg2Rad;
                var w=region.landmarkW+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(2.2f+candidate%3*.8f);
                if(SurfaceDistance(w)<4.5f)continue;
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=region.id+" shoulder · "+kind;
                go.transform.SetPositionAndRotation(Ground(w),Axis*Quaternion.Euler(0,candidate*47,0));go.transform.localScale*=.72f;
                var renderers=go.GetComponentsInChildren<Renderer>();if(renderers.Length==0){Object.DestroyImmediate(go);continue;}
                var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                float scale=Mathf.Min(1,1.9f/Mathf.Max(.1f,bounds.size.y),3.3f/Mathf.Max(.1f,Mathf.Max(bounds.size.x,bounds.size.z)));
                go.transform.localScale*=scale;Physics.SyncTransforms();bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                go.transform.position+=Vector3.up*(Ground(w).y-bounds.min.y);Physics.SyncTransforms();
                float radius=new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
                if(SurfaceDistance(w)<radius+2||Physics.OverlapBox(bounds.center,bounds.extents+Vector3.one*.3f,Quaternion.identity,1,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(go.transform)&&c!=terrain))
                {Object.DestroyImmediate(go);continue;}
                if(!go.GetComponentsInChildren<Collider>().Any(c=>c.enabled&&!c.isTrigger))
                {var solid=go.AddComponent<BoxCollider>();solid.center=go.transform.InverseTransformPoint(bounds.center);solid.size=new Vector3(bounds.size.x/go.transform.lossyScale.x,bounds.size.y/go.transform.lossyScale.y,bounds.size.z/go.transform.lossyScale.z);}
                if(index==3)foreach(var renderer in renderers)renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Mat("Ash ridge dark shale",new Color(.34f,.35f,.34f))).ToArray();
                foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.isStatic=true;
                Dirty(go.transform);added++;
            }
        }
        static void BuildRepairHub()
        {
            var workshop=workshops.FirstOrDefault();
            if(!workshop)throw new InvalidOperationException("The existing repair workshop is required.");
            var root=Node(ownedRoot,"Repair hub wayfinding");root.position=workshop.transform.position;root.rotation=Axis;
            var mint=Mat("Repair mint",new Color(.31f,.82f,.63f));var ink=Mat("Repair sign ink",new Color(.12f,.22f,.20f));
            for(int side=-1;side<=1;side+=2)
            {
                // Entrance signs are well outside the central 8m drive-through lane.
                var sign=Node(root,"Repair entrance "+(side<0?"south":"north"));sign.localPosition=new Vector3(-7.0f,0,side*11f);
                Shape(sign,"Sign foot",PrimitiveType.Cube,new Vector3(0,.15f,0),new Vector3(2.6f,.30f,1),ink);
                Shape(sign,"Mint sign",PrimitiveType.Cube,new Vector3(0,1.4f,0),new Vector3(3,1.1f,.18f),mint);
                Label(sign,"Repair direction","REPAIR\nDRIVE THROUGH",new Vector3(0,2.4f,0),.105f,new Color(.84f,1,.89f));
                Shape(sign,"Solid sign post",PrimitiveType.Cube,new Vector3(0,.75f,0),new Vector3(.15f,1.5f,.15f),ink,true);
            }
            Label(root,"Repair hub title","AUTO REPAIR",new Vector3(0,3.5f,-.5f),.17f,new Color(.63f,1,.79f));
        }
        static ArenaRewardCache BuildCache(ExplorationArenaLayoutData.Cache spec,ArenaExploration exploration)
        {
            var rewardsRoot=ownedRoot.Find("Rewards");if(!rewardsRoot)rewardsRoot=Node(ownedRoot,"Rewards");
            var root=Node(rewardsRoot,spec.id);root.position=Ground(spec.positionW,.04f);root.rotation=Axis;
            root.gameObject.layer=9;var cache=root.gameObject.AddComponent<ArenaRewardCache>();cache.id=spec.id;cache.session=layout.session;cache.exploration=exploration;cache.reward=spec.reward;cache.amount=spec.overrideTuningAmount?spec.amount:exploration.tuning.DefaultAmount(spec.reward);cache.secret=spec.secret;cache.proximityOnly=spec.secret;cache.repairAmount=spec.repairAmount;cache.durability=Mathf.Max(.1f,spec.durability);cache.radius=exploration.tuning.collectionRadius;
            // Proximity chests poll their real distance/LOS. A layer9 trigger would intercept turret rays.
            if(!spec.secret){var collider=root.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.65f,0);collider.size=new Vector3(1.9f,1.3f,1.5f);}
            var available=Node(root,"Available reward");cache.availableVisual=available.gameObject;
            var dark=Mat("Cache dark timber",new Color(.28f,.23f,.17f));var metal=Mat("Cache worn steel",new Color(.43f,.46f,.43f));
            Color tint=spec.secret?new Color(.41f,.82f,.85f):spec.reward==ArenaRewardKind.Health?new Color(.38f,.88f,.60f):spec.reward==ArenaRewardKind.Ram?new Color(.96f,.63f,.23f):new Color(.98f,.84f,.37f);
            var accent=Mat("Cache "+(spec.secret?"secret":spec.reward.ToString()),tint);
            Shape(available,"Armored reward crate",PrimitiveType.Cube,new Vector3(0,.63f,0),new Vector3(1.85f,1.22f,1.45f),dark);
            for(int s=-1;s<=1;s+=2)Shape(available,"Bright binding strap",PrimitiveType.Cube,new Vector3(s*.56f,.65f,0),new Vector3(.13f,1.34f,1.52f),metal);
            Shape(available,"Reward cap",PrimitiveType.Cube,new Vector3(0,1.27f,0),new Vector3(2.0f,.16f,1.57f),accent);
            if(spec.reward==ArenaRewardKind.Health)
            {
                Shape(available,"Health cross horizontal",PrimitiveType.Cube,new Vector3(0,.72f,-.76f),new Vector3(.85f,.20f,.06f),accent);
                Shape(available,"Health cross vertical",PrimitiveType.Cube,new Vector3(0,.72f,-.76f),new Vector3(.20f,.85f,.06f),accent);
            }
            else if(spec.secret) Shape(available,"Secret chest latch",PrimitiveType.Cube,new Vector3(0,.82f,-.78f),new Vector3(.28f,.42f,.12f),accent);
            else for(int s=-1;s<=1;s+=2)
            {var stripe=Shape(available,"Damage chevron",PrimitiveType.Cube,new Vector3(s*.2f,.75f,-.76f),new Vector3(.13f,.65f,.06f),accent);stripe.transform.localRotation=Quaternion.Euler(0,0,s*-35);}
            var consumed=Node(root,"Opened empty cache");cache.consumedVisual=consumed.gameObject;
            Shape(consumed,"Empty dark crate base",PrimitiveType.Cube,new Vector3(0,.10f,0),new Vector3(1.85f,.20f,1.45f),dark);consumed.gameObject.SetActive(false);
            cache.displayText=Label(root,"Exact reward label",cache.RewardLabel,new Vector3(0,3,0),.105f,tint);
            if(!spec.secret){var bar=EnemyHealthBarAuthoring.ConfigureTarget(cache,layout.session.input.view);bar.height=1.7f;bar.anchor.localPosition=Vector3.up*1.7f;Dirty(bar);Dirty(bar.anchor);}
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=9;
            Dirty(cache);return cache;
        }
        static void ConfigureAffinities(GameSession session)
        {
            foreach(var buggy in (session.spawns.authoredBuggies??Array.Empty<EnemyBuggy>()).Where(b=>b))
            {var affinity=buggy.GetComponent<ArenaCombatAffinity>();if(!affinity)affinity=buggy.gameObject.AddComponent<ArenaCombatAffinity>();affinity.session=session;affinity.profile=ArenaAffinityProfile.Buggy;Dirty(affinity);}
            foreach(var raider in RoadGunnerLevelAuthoring.InScene<Raider>().Where(r=>r.arenaBoss))
            {var affinity=raider.GetComponent<ArenaCombatAffinity>();if(!affinity)affinity=raider.gameObject.AddComponent<ArenaCombatAffinity>();affinity.session=session;affinity.profile=ArenaAffinityProfile.Warlord;Dirty(affinity);}
        }
        static void BuildRecoveryLaybys()
        {
            var root=Node(ownedRoot,"Safe recovery laybys");var points=new List<Transform>();
            for(int i=0;i<data.recoveryLaybysW.Length;i++)
            {
                var marker=Node(root,"Recovery "+i.ToString("00"));marker.position=Ground(data.recoveryLaybysW[i],.70f);marker.rotation=layout.session.startRotation;points.Add(marker);
            }
            layout.recoveryPoints=points.ToArray();layout.session.recoveryPoints=points.Select(p=>p.position).ToArray();
        }
        static void ConfigureBounds()
        {
            var bounds=new Bounds(World(data.minW),Vector3.zero);
            bounds.Encapsulate(World(new Vector2(data.minW.x,data.maxW.y)));bounds.Encapsulate(World(new Vector2(data.maxW.x,data.minW.y)));bounds.Encapsulate(World(data.maxW));
            bounds.Expand(new Vector3(12,20,12));layout.gameplayBounds=bounds;
        }
        static void ValidatePhysicalCenterlines()
        {
            var obstacles=BaselineSolids().Concat(workshops.SelectMany(w=>w.GetComponentsInChildren<Collider>(false)).Where(c=>c.enabled&&!c.isTrigger)).ToArray();int samples=0;var problems=new List<string>();
            foreach(var lane in lanes)for(int i=0;i<lane.points.Length;i+=2)
            {
                var position=Ground(lane.points[i]);var next=Ground(lane.points[Mathf.Min(i+1,lane.points.Length-1)]);var tangent=Vector3.ProjectOnPlane(next-position,Vector3.up);
                var rotation=tangent.sqrMagnitude>.001f?Quaternion.LookRotation(tangent):Axis;
                var overlaps=Physics.OverlapBox(position+Vector3.up*1.1f,new Vector3(1.45f,.65f,2.7f),rotation,1,QueryTriggerInteraction.Ignore);
                if(overlaps.Any(c=>obstacles.Contains(c)))problems.Add(lane.source.id+" W="+lane.points[i]+" intersects "+string.Join(",",overlaps.Where(c=>obstacles.Contains(c)).Select(c=>Hierarchy(c.transform))));
                samples++;
            }
            if(problems.Count>0)throw new InvalidOperationException("Exploration lane chassis clearance failed:\n"+string.Join("\n",problems.Take(15)));
            log.Add("Static route centerline chassis clearance: "+samples+" sampled oriented boxes 2.9m wide ×5.4m long; no baseline solid overlaps. This is not a dynamic driving claim.");
        }
        static string CombatSnapshot(GameSession session)
        {
            var items=RoadGunnerLevelAuthoring.InScene<CombatTarget>().Where(t=>!(t is ArenaRewardCache)).OrderBy(t=>Id(t),StringComparer.Ordinal).Select(t=>Id(t)+":"+t.transform.position.ToString("R")+":"+t.transform.rotation.ToString("R"));
            return string.Join("|",items)+"|vehicle="+JsonUtility.ToJson(session.vehicle.tuning)+"|combat="+JsonUtility.ToJson(session.turret.tuning)+"|health="+JsonUtility.ToJson(session.health.tuning);
        }
    }
}
