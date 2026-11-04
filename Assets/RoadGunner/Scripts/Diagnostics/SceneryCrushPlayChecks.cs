#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in scenery regression using real source meshes and normal production driving.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SceneryCrushPlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/SceneryCrush-2026-11-03/Props.txt";
        public const string ArtifactFolder = "outputs/scenery-crush-2026-11-03";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        readonly List<string> lines = new List<string>();
        readonly Dictionary<Mesh, int[]> originalIndices = new Dictionary<Mesh, int[]>();
        readonly Dictionary<Mesh, Vector3[]> originalVertices = new Dictionary<Mesh, Vector3[]>();
        readonly Vector3 course = new Vector3(900, 800, 900);
        GameSession s;
        ArenaDecorationCrush manager;
        VehicleController car;
        DecorationCrushChunk[] originalChunks;
        Vector3[] originalRecovery;
        GameObject fixtures, propRoot, hardRoot;
        DecorationCrushChunk fixtureChunk;
        Material floorMaterial;
        RigidbodyConstraints originalConstraints;
        float originalStep;
        int originalFps, originalVsync;
        bool originalSpawns, originalTurret, originalManagerEnabled, running, restored, setupCaptured;
        int[] fixtureIndices;
        int[] selectedStarts, selectedCounts;
        Bounds fixtureBounds;
        string fixtureId;
        int peakDebris;
        bool observedDebris;
        readonly string[] prefixes = { "arcade/", "shoulder/", "standalone/concept-roadside/", "standalone/legacy/" };
        sealed class Sample { public DecorationCrushChunk chunk; public DecorationCrushChunk.Item item; }
        struct Ledger
        {
            public float hp, maxHp, bonusDamage, bonusRam, bonusHp;
            public int kills, scrap, earned, pickups, value, rewardSerial, attack, health;
        }
        Ledger Snapshot() => new Ledger { hp=s.health.Current, maxHp=s.health.Maximum, kills=s.Kills, scrap=s.Scrap, earned=s.ScrapEarned,
            pickups=s.pickups.ActiveCount, value=s.pickups.StoredValue, rewardSerial=s.exploration.RewardSerial,
            attack=s.runStats.AttackLevel, health=s.runStats.HealthLevel, bonusDamage=s.runStats.BonusDamagePercent,
            bonusRam=s.runStats.BonusRamPercent, bonusHp=s.runStats.BonusMaxHP };
        bool SameLedger(Ledger a) => a.Equals(Snapshot());

        public void Begin()
        {
            if (running) return; running=true;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); Directory.CreateDirectory(ArtifactFolder);
            lines.Add("Scenery crush: saved scene source geometry; Unity " + Application.unityVersion);
            lines.Add("Disclosure: actual player is positioned on a temporary flat course. Props use copied original chunks with one selected item enabled, not a natural arena playthrough. Driving uses normal input and physics; no moving-case body velocity/rotation assignments.");
            lines.Add("Pause/terminal rejection calls TryCrush explicitly; external collisions use copied actual hard BoxCollider dimensions. Captures use the actual camera and HUD.");
            Write(); StartCoroutine(Guarded());
        }
        IEnumerator Guarded()
        {
            yield return null; yield return null;
            foreach (var section in new Func<IEnumerator>[] { Setup, SourceDrives, NearMissAndRecover, StateGuardsAndRestart, ExactMeshImpact, HardImpacts, FinalContracts })
            {
                var stack=new Stack<IEnumerator>(); stack.Push(section()); bool failed=false;
                while (stack.Count>0 && !failed)
                {
                    var part=stack.Peek(); bool more=false; object value=null;
                    try { more=part.MoveNext(); if(more)value=part.Current; }
                    catch(Exception error) { Check(section.Method.Name+" exception",false,error.ToString()); failed=true; }
                    if(failed)break;
                    if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(value is IEnumerator nested)stack.Push(nested);else yield return value;
                }
                while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
                if(!setupCaptured)break;
            }
            try { Restore(); } catch(Exception error) { Check("fixture restore",false,error.ToString()); }
            running=false; Completed=true;
            lines.Add("RESULT checks="+Checks+"; failures="+Failures+"; runtime fixtures removed; saved scene/assets not written.");Write();
        }
        IEnumerator Setup()
        {
            s=GetComponent<GameSession>(); if(!s)throw new InvalidOperationException("Attach to the saved GameSession.");
            car=s.vehicle; manager=Object.FindAnyObjectByType<ArenaDecorationCrush>();
            if(!manager)throw new InvalidOperationException("Saved scene needs ArenaDecorationCrush authoring.");
            originalChunks=manager.chunks; originalRecovery=s.recoveryPoints; originalConstraints=car.Body.constraints;
            originalStep=Time.fixedDeltaTime; originalFps=Application.targetFrameRate; originalVsync=QualitySettings.vSyncCount;
            originalSpawns=s.spawns.enabled; originalTurret=s.turret.enabled; originalManagerEnabled=manager.enabled; setupCaptured=true;
            Time.fixedDeltaTime=1f/60; Application.targetFrameRate=60; QualitySettings.vSyncCount=0;
            FreshRun();
            Check("production manager covers every existing scenery layer", manager.session==s && manager.isActiveAndEnabled &&
                prefixes.All(p=>Samples().Any(x=>x.item.id.StartsWith(p,StringComparison.Ordinal))) && originalChunks.All(c=>c && c.source) &&
                new[]{DecorationCrushKind.Vegetation,DecorationCrushKind.Wood,DecorationCrushKind.Metal,DecorationCrushKind.Rubber}.All(k=>Samples().Any(x=>x.item.kind==k)) &&
                manager.RegisteredCount==originalChunks.Sum(c=>c.items.Length),
                "Required prefixes="+string.Join(", ",prefixes)+"; authored chunks="+originalChunks.Length+"; registered="+manager.RegisteredCount+"; authored items="+originalChunks.Sum(c=>c.items.Length));
            bool safeOwners=originalChunks.All(c=>!c.GetComponentInParent<CombatTarget>() &&
                !c.GetComponentInParent<VehicleController>() && !c.GetComponentInParent<ArenaUpgradeStation>() &&
                !c.GetComponentInParent<RepairWorkshop>() && !c.GetComponentInChildren<CombatTarget>(true));
            Check("decoration excludes reward systems and actors", safeOwners && s.exploration.caches.Length==8 && s.runStats.stations.Length==2,
                "No CombatTarget/reward/station/workshop/player ancestry="+safeOwners+"; actual caches="+s.exploration.caches.Length+
                "; actual stations="+s.runStats.stations.Length+" (one Attack, one Health in the unchanged saved scene)");
            foreach(var chunk in originalChunks)
            {
                var mesh=chunk.OriginalMesh;
                if(!mesh || !mesh.isReadable)throw new InvalidOperationException("Source must be readable before first break: "+chunk.name);
                if(!originalIndices.ContainsKey(mesh)){originalIndices.Add(mesh,Flatten(mesh));originalVertices.Add(mesh,mesh.vertices);}
            }
            fixtures=new GameObject("Runtime-only scenery acceptance fixtures"){hideFlags=HideFlags.DontSave};
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Flat scenery course";floor.transform.SetParent(fixtures.transform);
            floor.transform.position=course-Vector3.up;floor.transform.localScale=new Vector3(400,2,400);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.72f,.53f,.31f));
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            yield return null;
        }
        IEnumerable<Sample> Samples() => originalChunks.Where(c=>c && c.items!=null).SelectMany(c=>c.items.Where(i=>i!=null).Select(i=>new Sample{chunk=c,item=i}));
        Sample Representative(string prefix)
        {
            var desired=prefix=="standalone/legacy/"?DecorationCrushKind.Metal:prefix=="shoulder/"||prefix=="standalone/exploration/"?DecorationCrushKind.Wood:DecorationCrushKind.Vegetation;
            return Samples().Where(x=>x.item.id.StartsWith(prefix,StringComparison.Ordinal)).OrderBy(x=>x.item.kind==desired?0:1)
                .ThenByDescending(x=>x.item.localBounds.size.y).First();
        }
        void FreshRun()
        {
            Time.timeScale=1; s.Restart(); s.spawns.enabled=false;s.turret.enabled=false;
            foreach(var r in s.spawns.pool)if(r)r.Despawn();foreach(var b in s.spawns.buggyPool)if(b)b.Despawn();if(s.zone1)s.zone1.DespawnAll();
            // Keep damage enabled: otherwise an accidental damaging barrel would be hidden by the fixture.
            s.health.DamageEnabled=true;
        }
        void MakeFixture(Sample sample)
        {
            if(propRoot){propRoot.SetActive(false);Destroy(propRoot);}
            propRoot=new GameObject("Actual source copy: "+sample.item.id);propRoot.SetActive(false);propRoot.transform.SetParent(fixtures.transform);
            var source=sample.chunk.source;var bounds=sample.chunk.GetWorldBounds(Array.IndexOf(sample.chunk.items,sample.item));
            propRoot.transform.SetPositionAndRotation(source.transform.position + course - bounds.center + Vector3.up*(bounds.extents.y+.02f),source.transform.rotation);
            propRoot.transform.localScale=source.transform.lossyScale;
            var filter=propRoot.AddComponent<MeshFilter>();filter.sharedMesh=sample.chunk.OriginalMesh;
            propRoot.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<Renderer>().sharedMaterials;
            fixtureChunk=propRoot.AddComponent<DecorationCrushChunk>();fixtureChunk.source=filter;
            var probe=propRoot.AddComponent<BoxCollider>();probe.isTrigger=true;probe.center=sample.item.localBounds.center;probe.size=sample.item.localBounds.size;propRoot.layer=12;
            selectedStarts=(int[])sample.item.rangeStarts.Clone();selectedCounts=(int[])sample.item.rangeCounts.Clone();
            fixtureId=sample.item.id;
            fixtureChunk.items=new[]{new DecorationCrushChunk.Item {id="fixture/"+fixtureId,kind=sample.item.kind,probe=probe,
                rangeStarts=(int[])selectedStarts.Clone(),rangeCounts=(int[])selectedCounts.Clone(),localBounds=sample.item.localBounds}};
            fixtureIndices=Flatten(filter.sharedMesh);propRoot.SetActive(true);
            fixtureBounds=fixtureChunk.GetWorldBounds(0);manager.chunks=new[]{fixtureChunk};manager.RebuildRegistry();manager.ResetSweepHistory();Physics.SyncTransforms();
        }
        IEnumerator PlaceCar(float x,float z)
        {
            Command(Vector3.zero,true);car.Body.constraints=originalConstraints;car.CanDrive=true;car.input.Enabled=true;
            car.ResetPose(course+new Vector3(x,.8f,z),Quaternion.identity);manager.ResetSweepHistory();Physics.SyncTransforms();s.follow.Snap();
            float began=Time.fixedTime;while(Time.fixedTime-began<1)yield return new WaitForFixedUpdate();
            car.input.testBrake=false;
        }
        void Command(Vector3 direction,bool brake=false)
        {
            var right=Vector3.ProjectOnPlane(s.input.view.transform.right,Vector3.up).normalized;
            var up=Vector3.ProjectOnPlane(s.input.view.transform.forward,Vector3.up).normalized;
            s.input.testInput=true;s.input.testDirection=new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,up));s.input.testBrake=brake;
        }
        IEnumerator DriveThrough(bool capture)
        {
            Command(Vector3.forward);float began=Time.fixedTime, brokeAt=-1;bool captured=false, beforeCaptured=false;
            while(car.Body.position.z<fixtureBounds.max.z+3 && Time.fixedTime-began<12)
            {
                yield return new WaitForFixedUpdate();
                peakDebris=Mathf.Max(peakDebris,manager.ActiveDebrisCount);observedDebris|=manager.ActiveDebrisCount>0 && manager.DebrisSpawned>0;
                if(capture && !beforeCaptured && !fixtureChunk.IsBroken(0) && car.Body.position.z>=fixtureBounds.min.z-5)
                {Capture("concept-shoulder-before");beforeCaptured=true;}
                if(fixtureChunk.IsBroken(0) && brokeAt<0)brokeAt=Time.fixedTime;
                if(capture && !captured && brokeAt>=0 && Time.fixedTime-brokeAt>=.12f){Capture("concept-shoulder-early-debris");captured=true;}
            }
            if(capture && !captured)Capture("concept-shoulder-no-break-observed");
            Command(Vector3.zero,true);
        }
        IEnumerator SourceDrives()
        {
            foreach(var prefix in prefixes.Concat(new[]{"material/rubber"}))
            {
                var sample=prefix=="material/rubber"?Samples().First(x=>x.item.kind==DecorationCrushKind.Rubber):Representative(prefix);
                MakeFixture(sample);yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);
                var ledger=Snapshot();bool capture=prefix=="shoulder/";
                yield return DriveThrough(capture);
                Check(prefix+" real drive crushes once and passes its source geometry", fixtureChunk.IsBroken(0) && fixtureChunk.BrokenCount==1 &&
                    manager.BrokenCount==1 && car.Body.position.z>=fixtureBounds.max.z+3 && car.GroundedWheels>=3 && SameLedger(ledger) && s.health.DamageEnabled,
                    "Actual source="+fixtureId+"; broken="+manager.BrokenCount+"; carZ="+car.Body.position.z+"; targetEnd="+(fixtureBounds.max.z+3)+"; no reward/HP changes="+SameLedger(ledger));
                var after=Flatten(fixtureChunk.RuntimeMesh?fixtureChunk.RuntimeMesh:fixtureChunk.source.sharedMesh);
                var selected=new HashSet<int>();for(int r=0;r<selectedStarts.Length;r++)for(int i=selectedStarts[r];i<selectedStarts[r]+selectedCounts[r];i++)selected.Add(i);
                bool sameLength=after.Length==fixtureIndices.Length;
                bool outsideUnchanged=sameLength && Enumerable.Range(0,after.Length).Where(i=>!selected.Contains(i)).All(i=>after[i]==fixtureIndices[i]);
                bool changed=sameLength && selected.Any(i=>after[i]!=fixtureIndices[i]);
                yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);yield return DriveThrough(false);
                Check(prefix+" only selected geometry changes and a second pass cannot break or pay twice", outsideUnchanged && changed &&
                    manager.BrokenCount==1 && fixtureChunk.BrokenCount==1 && SameLedger(ledger),
                    "Selected index slots="+selected.Count+"; total="+fixtureIndices.Length+"; all neighbours/hard slots unchanged="+outsideUnchanged+
                    "; broken after second pass="+manager.BrokenCount+"; immutable gameplay ledger="+SameLedger(ledger));
            }
        }
        IEnumerator NearMissAndRecover()
        {
            MakeFixture(Representative("shoulder/"));
            float carHalf=car.GetComponents<BoxCollider>().Max(c=>c.bounds.extents.x);
            var tracks=car.GetComponentInChildren<PlayerTrackVisuals>(true);
            float halfTrack=tracks?tracks.trackWidth*.5f+.06f:0;
            foreach(var wheel in car.wheels)
                carHalf=Mathf.Max(carHalf,Mathf.Abs(car.transform.InverseTransformPoint(wheel.transform.TransformPoint(wheel.center)).x)+Mathf.Max(wheel.radius*.32f,halfTrack));
            yield return PlaceCar(fixtureBounds.extents.x+carHalf+1,fixtureBounds.min.z-course.z-10);
            yield return DriveThrough(false);
            Check("near miss outside the actual chassis footprint preserves scenery", manager.BrokenCount==0 && !fixtureChunk.IsBroken(0),
                "Lateral clearance exceeds prop half-width plus chassis half-width by1m; car passed alongside");
            MakeFixture(Representative("shoulder/"));yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);
            Command(Vector3.zero,true);Vector3 destination=course+new Vector3(0,car.Body.position.y-course.y+.15f,fixtureBounds.max.z-course.z+10);
            s.recoveryPoints=new[]{destination};s.Recover();
            yield return null;yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Check("real Recover cannot sweep a destruction bridge across scenery", Vector3.Distance(car.Body.position,destination)<1 &&
                manager.BrokenCount==0 && !fixtureChunk.IsBroken(0),"Recovered to="+car.Body.position+"; expected="+destination+"; broken="+manager.BrokenCount);
            s.recoveryPoints=originalRecovery;
        }
        IEnumerator StateGuardsAndRestart()
        {
            MakeFixture(Representative("shoulder/"));yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);
            manager.enabled=false;yield return WarmUp();float pausedSpeed=car.Body.GetPointVelocity(fixtureBounds.center).magnitude;
            s.SetPaused(true);manager.enabled=true;bool paused=manager.TryCrush(fixtureChunk,0,car.Body,fixtureBounds.center,Vector3.forward,3);
            Check("paused run rejects an explicit valid-body crush attempt", !paused && !fixtureChunk.IsBroken(0) && pausedSpeed>manager.minimumImpactSpeed &&
                Vector3.Distance(fixtureBounds.ClosestPoint(car.Body.worldCenterOfMass),car.Body.worldCenterOfMass)<=car.GetComponent<BoxCollider>().bounds.extents.magnitude,
                "Direct API guard test; actual point speed="+pausedSpeed+"; normal paused physics cannot advance");
            s.SetPaused(false);s.CompleteArena();bool terminal=manager.TryCrush(fixtureChunk,0,car.Body,fixtureBounds.center,Vector3.forward,3);
            Check("completed run rejects scenery mutation", !terminal && !fixtureChunk.IsBroken(0),"Completion invoked as isolated terminal fixture, not as a natural win");
            FreshRun();yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);manager.enabled=false;yield return WarmUp();s.health.Damage(1000,false);manager.enabled=true;
            bool defeated=manager.TryCrush(fixtureChunk,0,car.Body,fixtureBounds.center,Vector3.forward,3);
            Check("defeated run rejects scenery mutation", !defeated && !fixtureChunk.IsBroken(0),"Lethal HP change is explicit terminal setup");
            FreshRun();yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);yield return DriveThrough(false);
            int serial=s.RunSerial;FreshRun();yield return null;
            Check("real Restart restores broken source indices and probe", s.RunSerial==serial+1 && !fixtureChunk.IsBroken(0) && manager.BrokenCount==0 &&
                fixtureChunk.items[0].probe.enabled && Flatten(fixtureChunk.source.sharedMesh).SequenceEqual(fixtureIndices) && manager.ActiveDebrisCount==0,
                "RunSerial="+serial+" -> "+s.RunSerial+"; broken="+manager.BrokenCount+"; active debris="+manager.ActiveDebrisCount);
            yield return PlaceCar(0,fixtureBounds.min.z-course.z-10);Capture("concept-shoulder-after-restart");
            int registered=manager.RegisteredCount;FreshRun();yield return null;
            Check("second Restart neither duplicates props nor retains debris", manager.RegisteredCount==registered && registered==1 && manager.BrokenCount==0 &&
                manager.ActiveDebrisCount==0 && s.health.Current==100 && s.Scrap==0 && s.Kills==0,
                "Registered="+registered+" -> "+manager.RegisteredCount+"; debris="+manager.ActiveDebrisCount);
        }
        IEnumerator WarmUp()
        {
            Command(Vector3.forward);float began=Time.fixedTime;
            while(car.Body.position.z<fixtureBounds.min.z-1.8f && Time.fixedTime-began<6)yield return new WaitForFixedUpdate();
            // Stop requesting forward travel while retaining the measured momentum for direct guard tests.
            Command(Vector3.zero);
        }
        [Serializable] sealed class AuthoredCatalog { public AuthoredItem[] items; }
        [Serializable] sealed class AuthoredItem { public string id, category, handling; }
        IEnumerator ExactMeshImpact()
        {
            // The manifest supplies semantic identity; the new compact collider supplies the real shape.
            // A renderer AABB cannot establish collision against the authored rock triangles.
            var catalog=JsonUtility.FromJson<AuthoredCatalog>(File.ReadAllText("Documentation/RoadGunner/SceneryCrush-2026-11-03/Authoring.json"));
            var root=Object.FindAnyObjectByType<LevelLayout>().decorationRoot.Find("Scenery crush collisions/Hard exact collision (Default)");
            MeshCollider original=null;string sourceId="";
            foreach(var item in catalog.items.Where(i=>i.handling=="hard" && i.category=="Boulder"))
            {
                string key;using(var hash=SHA256.Create())key=string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(item.id)).Select(b=>b.ToString("x2"))).Substring(0,20);
                var candidate=root?root.Find("Hard "+key):null;
                var collider=candidate?candidate.GetComponent<MeshCollider>():null;
                if(collider && collider.enabled && !collider.isTrigger && collider.sharedMesh && collider.bounds.size.y>=1.4f && collider.bounds.size.x>=1.5f)
                { original=collider;sourceId=item.id;break; }
            }
            if(!original){Check("new authored Boulder mesh blocks an actual physical drive",false,"No tall authored Boulder compact MeshCollider resolved from the installed catalog");yield break;}
            if(propRoot)propRoot.SetActive(false);manager.chunks=Array.Empty<DecorationCrushChunk>();manager.RebuildRegistry();
            if(hardRoot){hardRoot.SetActive(false);Destroy(hardRoot);}
            var asset=original.sharedMesh;var indices=Flatten(asset);var vertices=asset.vertices;
            hardRoot=new GameObject("Exact authored Boulder mesh fixture");hardRoot.transform.SetParent(fixtures.transform);
            hardRoot.transform.SetPositionAndRotation(original.transform.position+course-original.bounds.center+Vector3.up*original.bounds.extents.y,original.transform.rotation);
            hardRoot.transform.localScale=original.transform.lossyScale;
            var solid=hardRoot.AddComponent<MeshCollider>();solid.sharedMesh=asset;solid.convex=original.convex;solid.isTrigger=false;Physics.SyncTransforms();
            // Prove this approach crosses a real face before driving; bounds centre alone could fall in a mesh hole.
            float faceHeight=Mathf.Min(.75f,solid.bounds.size.y*.5f);
            bool hasFace=solid.Raycast(new Ray(new Vector3(course.x,solid.bounds.min.y+faceHeight,solid.bounds.min.z-3),Vector3.forward),out var face,solid.bounds.size.z+6);
            yield return PlaceCar(0,solid.bounds.min.z-course.z-10);
            var collision=car.gameObject.AddComponent<SceneryCrushCollisionProbe>();collision.Target=solid;
            var ledger=Snapshot();float began=Time.fixedTime,peak=0,furthest=car.Body.position.z;
            Command(Vector3.forward);
            while(Time.fixedTime-began<10 && collision.Contacts==0){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,car.HorizontalSpeed);furthest=Mathf.Max(furthest,car.Body.position.z);}
            Command(Vector3.zero,true);began=Time.fixedTime;
            while(Time.fixedTime-began<.4f){yield return new WaitForFixedUpdate();furthest=Mathf.Max(furthest,car.Body.position.z);}
            Check("new authored Boulder mesh blocks an actual physical drive", hasFace && collision.Contacts>0 && collision.Impulse>car.Body.mass*.2f && peak>2 &&
                furthest<solid.bounds.center.z && original.enabled && solid.enabled && solid.sharedMesh==asset &&
                Flatten(asset).SequenceEqual(indices) && asset.vertices.SequenceEqual(vertices) && SameLedger(ledger) && manager.BrokenCount==0,
                "Catalog source="+sourceId+"; exact shared mesh="+asset.name+"; vertices="+asset.vertexCount+"; real face="+hasFace+
                "; faceZ="+face.point.z+"; contacts="+collision.Contacts+"; impulse="+collision.Impulse+"; peak="+peak+"; furthestZ="+furthest+"; no asset/HP/reward mutation="+SameLedger(ledger));
            Destroy(collision);hardRoot.SetActive(false);
        }
        IEnumerator HardImpacts()
        {
            foreach(string category in new[]{"Boulder","Cliff","bunker"})
            {
                var hard=Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                    .FirstOrDefault(c=>c.enabled&&!c.isTrigger&&!c.transform.IsChildOf(fixtures.transform)&&c.bounds.size.y>1 &&
                        Hierarchy(c.transform).IndexOf(category,StringComparison.OrdinalIgnoreCase)>=0);
                if(!hard){Check(category+" hard collision source exists",false,"No authored solid BoxCollider found");continue;}
                if(propRoot)propRoot.SetActive(false);manager.chunks=Array.Empty<DecorationCrushChunk>();manager.RebuildRegistry();
                if(hardRoot){hardRoot.SetActive(false);Destroy(hardRoot);}
                hardRoot=new GameObject("Copy of actual hard collider: "+Hierarchy(hard.transform));hardRoot.transform.SetParent(fixtures.transform);
                hardRoot.transform.SetPositionAndRotation(hard.transform.position+course-hard.bounds.center+Vector3.up*hard.bounds.extents.y,hard.transform.rotation);
                hardRoot.transform.localScale=hard.transform.lossyScale;
                var solid=hardRoot.AddComponent<BoxCollider>();solid.center=hard.center;solid.size=hard.size;Physics.SyncTransforms();
                // A long rotated foundation's AABB corner can be far ahead of its actual centreline face.
                // Keep the exact authored box, but start ten metres before the surface this drive can hit.
                float faceHeight=Mathf.Min(.75f,solid.bounds.size.y*.5f);
                bool hasFace=solid.Raycast(new Ray(new Vector3(course.x,solid.bounds.min.y+faceHeight,solid.bounds.min.z-3),Vector3.forward),out var face,solid.bounds.size.z+6);
                float approachZ=(hasFace?face.point.z:solid.bounds.min.z)-course.z-10;
                yield return PlaceCar(0,approachZ);
                var collision=car.gameObject.AddComponent<SceneryCrushCollisionProbe>();collision.Target=solid;
                var ledger=Snapshot();float began=Time.fixedTime,peak=0,furthest=car.Body.position.z;
                Command(Vector3.forward);
                while(Time.fixedTime-began<10 && collision.Contacts==0){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,car.HorizontalSpeed);furthest=Mathf.Max(furthest,car.Body.position.z);}
                Command(Vector3.zero,true);began=Time.fixedTime;
                while(Time.fixedTime-began<.4f){yield return new WaitForFixedUpdate();furthest=Mathf.Max(furthest,car.Body.position.z);}
                bool ray=solid.Raycast(new Ray(solid.bounds.center-Vector3.forward*(solid.bounds.extents.z+3),Vector3.forward),out _,solid.bounds.size.z+6);
                Check(category+" remains a hard physical obstacle at real driving speed", hasFace && collision.Contacts>0 && peak>2 && collision.Impulse>car.Body.mass*.2f &&
                    furthest<solid.bounds.center.z && solid.enabled && hard.enabled && ray && SameLedger(ledger) && manager.BrokenCount==0,
                    "Actual source="+Hierarchy(hard.transform)+"; realFace="+hasFace+"; faceZ="+face.point.z+"; approachZ="+(course.z+approachZ)+
                    "; contact="+collision.Contacts+"; impulse="+collision.Impulse+"; peak="+peak+"; furthestZ="+furthest+"; solidCentreZ="+solid.bounds.center.z);
                Destroy(collision);hardRoot.SetActive(false);
            }
        }
        IEnumerator FinalContracts()
        {
            Check("original shared source assets retain every vertex and index", originalIndices.All(p=>p.Key && Flatten(p.Key).SequenceEqual(p.Value) && p.Key.vertices.SequenceEqual(originalVertices[p.Key])),
                "Original shared meshes checked="+originalIndices.Count+"; runtime copies cannot mutate other source instances");
            int mask=1<<12;
            Check("scenery probes cannot intercept player or Buggy LOS", (s.turret.hitMask.value&mask)==0 &&
                s.spawns.authoredBuggies.All(b=>b && b.gun && (b.gun.hitMask.value&mask)==0),
                "Layer12 excluded from actual saved player/Buggy masks; reward-cache/upgrade contracts are exercised separately by the unchanged gameplay/exploration suites");
            Check("debris pool remains bounded and cosmetic", observedDebris && manager.DebrisCapacity>0 && peakDebris<=manager.DebrisCapacity && manager.ActiveDebrisCount<=manager.DebrisCapacity &&
                s.pickups.StoredValue==0 && s.pickups.ActiveCount==0 && s.Kills==0 && s.Scrap==0,
                "Observed debris="+observedDebris+"; peak/capacity="+peakDebris+"/"+manager.DebrisCapacity+"; final active="+manager.ActiveDebrisCount+"; spawned="+manager.DebrisSpawned+"; gameplay loot remains empty");
            yield return null;
        }
        static string Hierarchy(Transform t){var names=new List<string>();for(;t;t=t.parent)names.Add(t.name);names.Reverse();return string.Join("/",names);}
        static int[] Flatten(Mesh mesh)=>Enumerable.Range(0,mesh.subMeshCount).SelectMany(i=>mesh.GetIndices(i)).ToArray();
        void Check(string name,bool pass,string detail){Checks++;if(!pass)Failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);Debug.Log("SCENERY CHECK "+lines[lines.Count-1]);Write();}
        void Write()=>File.WriteAllLines(ReportPath,lines);
        sealed class CanvasState {public Canvas canvas;public RenderMode mode;public Camera camera;public float plane;}
        void Capture(string name)
        {
            var camera=s.input.view;var target=RenderTexture.GetTemporary(540,960,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float aspect=camera.aspect;
            var texture=new Texture2D(540,960,TextureFormat.RGB24,false);
            var canvases=Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>new CanvasState{canvas=c,mode=c.renderMode,camera=c.worldCamera,plane=c.planeDistance}).ToArray();
            try{camera.aspect=540f/960;camera.targetTexture=target;foreach(var c in canvases){c.canvas.renderMode=RenderMode.ScreenSpaceCamera;c.canvas.worldCamera=camera;c.canvas.planeDistance=Mathf.Max(camera.nearClipPlane+.5f,1);}
                Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,540,960),0,0);texture.Apply();string path=ArtifactFolder+"/"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());
                lines.Add("CAPTURE "+path+"; actual camera/HUD on disclosed source-copy course; prop="+fixtureId);Write();}
            finally{foreach(var c in canvases)if(c.canvas){c.canvas.renderMode=c.mode;c.canvas.worldCamera=c.camera;c.canvas.planeDistance=c.plane;}camera.targetTexture=oldTarget;camera.aspect=aspect;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Destroy(texture);Canvas.ForceUpdateCanvases();}
        }
        void Restore()
        {
            if(restored)return;restored=true;
            if(fixtures){fixtures.SetActive(false);Destroy(fixtures);}if(floorMaterial)Destroy(floorMaterial);
            if(!setupCaptured)return;
            Time.timeScale=1;Time.fixedDeltaTime=originalStep;Application.targetFrameRate=originalFps;QualitySettings.vSyncCount=originalVsync;
            manager.chunks=originalChunks;manager.enabled=originalManagerEnabled;manager.RebuildRegistry();s.recoveryPoints=originalRecovery;car.Body.constraints=originalConstraints;
            s.input.testInput=false;s.input.testBrake=false;s.input.testDirection=Vector2.zero;s.input.Clear();
            s.spawns.enabled=originalSpawns;s.turret.enabled=originalTurret;s.Restart();
        }
        void OnDestroy(){if(running)Restore();}
    }
    public sealed class SceneryCrushCollisionProbe : MonoBehaviour
    {
        public Collider Target;public int Contacts;public float Impulse;
        void OnCollisionEnter(Collision hit){if(hit.collider==Target){Contacts++;Impulse=Mathf.Max(Impulse,hit.impulse.magnitude);}}
    }
}
#endif
