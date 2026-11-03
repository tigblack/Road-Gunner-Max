using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Vehicle-only soft scenery contact, run state and bounded cosmetic debris. No combat/reward APIs.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaDecorationCrush : MonoBehaviour
    {
        public GameSession session;
        public DecorationCrushChunk[] chunks = Array.Empty<DecorationCrushChunk>();
        [Tooltip("Shared untextured white material; fragment colours use a property block.")]
        public Material debrisMaterial;
        public LayerMask probeMask = 1 << 12;
        [Min(0)] public float minimumImpactSpeed = .25f;
        [Range(16, 256)] public int debrisCapacity = 96;
        public int RegisteredCount => entries.Count;
        public int TrackableCount => cars.Count;
        public int BrokenCount { get; private set; }
        public int ActiveDebrisCount { get; private set; }
        public int DebrisCapacity => pieces != null ? pieces.Length : Mathf.Clamp(debrisCapacity, 16, 256);
        public int DebrisSpawned { get; private set; }

        struct Entry { public DecorationCrushChunk chunk; public int index; }
        struct Pose { public Vector3 centre, half; public Quaternion rotation; }
        sealed class Car
        {
            public Rigidbody body;
            public VehicleController player;
            public EnemyBuggy buggy;
            public Bounds localBounds;
            public WheelCollider[] wheels;
            public Pose previous;
            public Vector3 position;
            public bool valid;
            public int poseSerial;
        }
        struct Piece
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Vector3 velocity, spin, scale;
            public float age, life, ground;
            public bool active;
        }

        readonly Dictionary<Collider, Entry> entries = new Dictionary<Collider, Entry>();
        readonly Dictionary<Rigidbody, Car> carLookup = new Dictionary<Rigidbody, Car>();
        readonly List<Car> cars = new List<Car>();
        readonly HashSet<DecorationCrushChunk> uniqueChunks = new HashSet<DecorationCrushChunk>();
        Collider[] overlap = new Collider[64];
        RaycastHit[] cast = new RaycastHit[64];
        Piece[] pieces;
        Mesh pieceMesh;
        Transform poolRoot;
        MaterialPropertyBlock tint;
        ArcadeVfx vfx;
        GameSession subscribed;
        int runSerial = int.MinValue, nextPiece;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        const int SceneryLayer = 12, MaximumQueryHits = 2048;
        const float SweepSpacing = .08f, QuerySkin = .02f;

        void Awake() { RebuildRegistry(); }
        void OnEnable() { BindSession(); }
        void Start() { RebuildRegistry(); SynchronizeRun(); EnsurePool(); }

        void BindSession()
        {
            if (!session) session = GetComponentInParent<GameSession>();
            if (subscribed == session) return;
            if (subscribed) subscribed.Changed -= SessionChanged;
            subscribed = session;
            if (subscribed) subscribed.Changed += SessionChanged;
        }

        void SessionChanged()
        {
            SynchronizeRun();
            if (!session || !session.Playing) ResetSweepHistory();
        }

        void SynchronizeRun()
        {
            if (session && runSerial != session.RunSerial) ResetRun();
        }

        /// <summary>Rebind authored data or an isolated fixture. No scene search and no probe creation.</summary>
        public void RebuildRegistry()
        {
            BindSession(); entries.Clear(); uniqueChunks.Clear(); cars.Clear(); carLookup.Clear();
            BrokenCount = 0;
            if (chunks != null) foreach (var chunk in chunks)
            {
                if (!chunk || uniqueChunks.Contains(chunk) || HasGameplayOwner(chunk.transform) || chunk.items == null) continue;
                for (int i = 0; i < chunk.items.Length; i++)
                {
                    var item = chunk.items[i]; var probe = item != null ? item.probe : null;
                    if (!probe || !probe.isTrigger || probe.gameObject.layer != SceneryLayer || HasGameplayOwner(probe.transform) ||
                        (probeMask.value & (1 << probe.gameObject.layer)) == 0 || !chunk.ValidItem(i) || entries.ContainsKey(probe)) continue;
                    entries.Add(probe, new Entry { chunk = chunk, index = i });
                    uniqueChunks.Add(chunk);
                    if (chunk.IsBroken(i)) BrokenCount++;
                }
            }
            if (session)
            {
                if (session.vehicle) AddCar(session.vehicle, null);
                var pool = session.spawns ? session.spawns.buggyPool : null;
                if (pool != null) foreach (var buggy in pool) if (buggy) AddCar(null, buggy);
                var authored = session.spawns ? session.spawns.authoredBuggies : null;
                if (authored != null) foreach (var buggy in authored) if (buggy) AddCar(null, buggy);
            }
            vfx = null;
            var hub = session ? session.Feedback : null;
            if (hub && hub.sinks != null) foreach (var sink in hub.sinks) if (sink is ArcadeVfx found) { vfx = found; break; }
            ResetSweepHistory();
        }

        static bool HasGameplayOwner(Transform node)
        {
            // Pure scenery never inherits the targeting, collection or actor semantics of a nearby parent.
            return node.GetComponentInParent<CombatTarget>() || node.GetComponentInParent<ArenaUpgradeStation>() ||
                node.GetComponentInParent<RepairWorkshop>() || node.GetComponentInParent<PickupPool>() ||
                node.GetComponentInParent<VehicleController>() || node.GetComponentInParent<EnemyBuggy>() ||
                node.GetComponentInChildren<CombatTarget>(true) || node.GetComponentInChildren<ArenaUpgradeStation>(true) ||
                node.GetComponentInChildren<RepairWorkshop>(true) || node.GetComponentInChildren<PickupPool>(true) ||
                node.GetComponentInChildren<VehicleController>(true) || node.GetComponentInChildren<EnemyBuggy>(true);
        }

        void AddCar(VehicleController player, EnemyBuggy buggy)
        {
            var owner = player ? player.gameObject : buggy.gameObject;
            var body = owner.GetComponent<Rigidbody>();
            if (!body || carLookup.ContainsKey(body)) return;
            var box = owner.GetComponent<BoxCollider>();
            if (!box || box.isTrigger) return;
            var car = new Car { body = body, player = player, buggy = buggy, wheels = player ? player.wheels : buggy.wheels };
            Vector3 e = box.size * .5f;
            var bounds = new Bounds(body.transform.InverseTransformPoint(box.transform.TransformPoint(box.center)), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(body.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(e, new Vector3(x, y, z)))));
            // Include the actual suspension/track footprint, which extends below and sideways of the chassis box.
            var tracks = player ? player.GetComponentInChildren<PlayerTrackVisuals>(true) : null;
            float trackHalfWidth = tracks && tracks.vehicle == player ? tracks.trackWidth * .5f + .06f : 0;
            if (car.wheels != null) foreach (var wheel in car.wheels)
            {
                if (!wheel) continue;
                Vector3 c = body.transform.InverseTransformPoint(wheel.transform.TransformPoint(wheel.center));
                Vector3 we = new Vector3(Mathf.Max(wheel.radius * .32f, trackHalfWidth), wheel.radius, wheel.radius);
                bounds.Encapsulate(c - we); bounds.Encapsulate(c + we);
            }
            car.localBounds = bounds; cars.Add(car); carLookup.Add(body, car);
        }

        bool Eligible(Car car)
        {
            if (!car.body || !car.body.gameObject.activeInHierarchy || car.body.isKinematic) return false;
            if (car.player) return car.player == session.vehicle && car.player.isActiveAndEnabled && car.player.CanDrive && car.player.GroundedWheels > 0;
            return car.buggy && car.buggy.isActiveAndEnabled && car.buggy.session == session && car.buggy.Alive && car.buggy.GroundedWheels > 0;
        }

        Pose CurrentPose(Car car)
        {
            var body = car.body; Vector3 scale = body.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Bounds bounds = car.localBounds;
            if (car.wheels != null) foreach (var wheel in car.wheels)
            {
                if (wheel && wheel.GetGroundHit(out var hit))
                {
                    Vector3 point = Quaternion.Inverse(body.rotation) * (hit.point - body.position);
                    bounds.Encapsulate(new Vector3(point.x / Mathf.Max(.001f, scale.x), point.y / Mathf.Max(.001f, scale.y), point.z / Mathf.Max(.001f, scale.z)));
                }
            }
            return new Pose {
                centre = body.position + body.rotation * Vector3.Scale(bounds.center, scale),
                half = Vector3.Scale(bounds.extents, scale), rotation = body.rotation
            };
        }

        public void ResetSweepHistory()
        {
            foreach (var car in cars) car.valid = false;
        }

        public void ResetRun()
        {
            runSerial = session ? session.RunSerial : int.MinValue;
            BrokenCount = 0; DebrisSpawned = 0;
            foreach (var chunk in uniqueChunks) if (chunk) chunk.ResetChunk();
            ClearDebris(); ResetSweepHistory();
        }

        void FixedUpdate()
        {
            SynchronizeRun();
            if (!session || !session.Playing) { ResetSweepHistory(); return; }
            if (entries.Count == 0) { ResetSweepHistory(); return; }
            int serial = session.RunSerial;
            foreach (var car in cars)
            {
                if (!Eligible(car)) { car.valid = false; continue; }
                Pose current = CurrentPose(car);
                int poseSerial = car.player ? car.player.PoseResetSerial : 0;
                float moved = car.valid ? Vector3.Distance(car.position, car.body.position) : 0;
                // Recover/Restart have an explicit serial. The distance check also protects pooled buggy respawns.
                if (!car.valid || car.poseSerial != poseSerial || moved > Mathf.Max(1, Time.fixedDeltaTime * 40))
                {
                    car.valid = true; car.previous = current; car.position = car.body.position; car.poseSerial = poseSerial;
                    continue;
                }
                float cornerTravel = Quaternion.Angle(car.previous.rotation, current.rotation) * Mathf.Deg2Rad * Mathf.Max(car.previous.half.magnitude, current.half.magnitude);
                int steps = Mathf.CeilToInt((Vector3.Distance(car.previous.centre, current.centre) + cornerTravel) / SweepSpacing);
                if (steps > 24) { car.previous = current; car.position = car.body.position; continue; }
                steps = Mathf.Max(1, steps);
                QueryOverlap(car, car.previous, serial);
                for (int i = 1; i <= steps && session && session.Playing && session.RunSerial == serial; i++)
                {
                    float a = (i - 1f) / steps, b = (float)i / steps;
                    Vector3 from = Vector3.Lerp(car.previous.centre, current.centre, a), to = Vector3.Lerp(car.previous.centre, current.centre, b);
                    Vector3 half = Vector3.Max(car.previous.half, current.half) + Vector3.one * QuerySkin;
                    QueryCast(car, from, to, half, Quaternion.Slerp(car.previous.rotation, current.rotation, (a + b) * .5f), serial);
                    QueryOverlap(car, new Pose { centre = to, half = half - Vector3.one * QuerySkin, rotation = Quaternion.Slerp(car.previous.rotation, current.rotation, b) }, serial);
                }
                car.previous = current; car.position = car.body.position; car.poseSerial = poseSerial;
                if (!session || session.RunSerial != serial) return;
            }
        }

        void QueryOverlap(Car car, Pose pose, int serial)
        {
            int count;
            for (;;)
            {
                count = Physics.OverlapBoxNonAlloc(pose.centre, pose.half + Vector3.one * QuerySkin, overlap, pose.rotation, probeMask, QueryTriggerInteraction.Collide);
                if (count < overlap.Length || overlap.Length >= MaximumQueryHits) break;
                Array.Resize(ref overlap, overlap.Length * 2);
            }
            for (int i = 0; i < count && session && session.Playing && session.RunSerial == serial; i++) Contact(car, overlap[i]);
        }

        void QueryCast(Car car, Vector3 from, Vector3 to, Vector3 half, Quaternion rotation, int serial)
        {
            Vector3 delta = to - from; float length = delta.magnitude;
            if (length < .00001f) return;
            int count;
            for (;;)
            {
                count = Physics.BoxCastNonAlloc(from, half, delta / length, cast, rotation, length, probeMask, QueryTriggerInteraction.Collide);
                if (count < cast.Length || cast.Length >= MaximumQueryHits) break;
                Array.Resize(ref cast, cast.Length * 2);
            }
            for (int i = 0; i < count && session && session.Playing && session.RunSerial == serial; i++) Contact(car, cast[i].collider);
        }

        void Contact(Car car, Collider probe)
        {
            if (!probe || !entries.TryGetValue(probe, out var entry) || entry.chunk.IsBroken(entry.index)) return;
            Vector3 point = probe.ClosestPoint(car.body.worldCenterOfMass);
            Vector3 velocity = car.body.GetPointVelocity(point);
            TryCrush(entry.chunk, entry.index, car.body, point, velocity, velocity.magnitude);
        }

        /// <summary>Uses a registered, currently alive vehicle and its measured point velocity, not caller-supplied damage.</summary>
        public bool TryCrush(DecorationCrushChunk chunk, int index, Rigidbody vehicle, Vector3 point, Vector3 direction, float speed)
        {
            if (!isActiveAndEnabled || !session || !session.Playing || !vehicle || !carLookup.TryGetValue(vehicle, out var car) || !Eligible(car) ||
                !chunk || !chunk.isActiveAndEnabled || chunk.items == null || index < 0 || index >= chunk.items.Length) return false;
            var item = chunk.items[index]; var probe = item != null ? item.probe : null;
            if (!probe || !probe.enabled || !probe.gameObject.activeInHierarchy || !entries.TryGetValue(probe, out var entry) || entry.chunk != chunk || entry.index != index) return false;
            Vector3 velocity = vehicle.GetPointVelocity(point);
            float actualSpeed = velocity.magnitude;
            if (float.IsNaN(actualSpeed) || float.IsInfinity(actualSpeed) || actualSpeed < Mathf.Max(.01f, minimumImpactSpeed)) return false;
            int serial = session.RunSerial;
            Bounds bounds = chunk.GetWorldBounds(index);
            // Public callers cannot turn the measured-car API into a remote scenery hit.
            if ((bounds.ClosestPoint(vehicle.worldCenterOfMass) - vehicle.worldCenterOfMass).sqrMagnitude >
                Mathf.Pow(CurrentPose(car).half.magnitude + Mathf.Max(.2f, actualSpeed * Time.fixedDeltaTime * 2), 2)) return false;
            if (!chunk.Break(index)) return false;
            BrokenCount++;
            if (!session || session.RunSerial != serial || !session.Playing) return true;
            SpawnDebris(bounds, item.kind, velocity, actualSpeed);
            PlayImpact(item.kind, bounds.center);
            return true;
        }

        void EnsurePool()
        {
            if (pieces != null) return;
            Material material = debrisMaterial;
            if (!material && chunks != null) foreach (var chunk in chunks)
            {
                var renderer = chunk && chunk.source ? chunk.source.GetComponent<Renderer>() : null;
                if (renderer && renderer.sharedMaterial) { material = renderer.sharedMaterial; break; }
            }
            pieceMesh = CubeMesh(); tint = new MaterialPropertyBlock();
            poolRoot = new GameObject("Scenery crumble pieces (pooled cosmetics)").transform;
            poolRoot.gameObject.hideFlags = HideFlags.DontSave; poolRoot.SetParent(transform, false);
            pieces = new Piece[Mathf.Clamp(debrisCapacity, 16, 256)];
            for (int i = 0; i < pieces.Length; i++)
            {
                var go = new GameObject("Fragment " + i); go.transform.SetParent(poolRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.enabled = false; pieces[i] = new Piece { transform = go.transform, renderer = renderer };
            }
        }

        void SpawnDebris(Bounds bounds, DecorationCrushKind kind, Vector3 velocity, float speed)
        {
            EnsurePool();
            int count = kind == DecorationCrushKind.Vegetation || kind == DecorationCrushKind.Bone ? 6 : 8;
            Vector3 direction = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized;
            Color colour = KindColour(kind);
            float size = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), .2f, 3);
            for (int j = 0; j < count; j++)
            {
                int index = nextPiece++ % pieces.Length; var p = pieces[index];
                if (!p.active) ActiveDebrisCount++;
                float side = (j & 1) == 0 ? -1 : 1, row = (j & 2) == 0 ? -1 : 1;
                Vector3 offset = new Vector3(side * bounds.extents.x * .45f, (j & 4) == 0 ? 0 : bounds.extents.y * .55f, row * bounds.extents.z * .45f);
                p.transform.position = bounds.center + offset;
                p.transform.rotation = Quaternion.Euler(j * 31, j * 67, j * 19);
                Vector3 scale = Vector3.Max(bounds.size * .32f, Vector3.one * .08f);
                scale = Vector3.Min(scale, Vector3.one * .65f);
                if (kind == DecorationCrushKind.Metal) scale.y *= .22f;
                else if (kind == DecorationCrushKind.Wood || kind == DecorationCrushKind.Bone) { scale.x *= .4f; scale.y *= 1.1f; }
                else if (kind == DecorationCrushKind.Vegetation) { scale.x *= .45f; scale.z *= .45f; }
                else scale.y *= .6f;
                p.scale = scale; p.transform.localScale = scale;
                Vector3 outward = new Vector3(side, .8f + (j % 3) * .2f, row).normalized;
                p.velocity = direction * Mathf.Min(speed * .2f, 1.7f) + outward * (.7f + size * .5f);
                p.spin = new Vector3(side * 140, row * 180, (j % 3 - 1) * 160);
                p.age = 0; p.life = 1.05f + (j % 3) * .15f; p.ground = bounds.min.y + scale.y * .4f;
                p.active = true; p.renderer.enabled = true;
                tint.SetColor(BaseColor, colour * (j % 2 == 0 ? 1 : .82f)); p.renderer.SetPropertyBlock(tint);
                pieces[index] = p; DebrisSpawned++;
            }
            if (vfx && vfx.isActiveAndEnabled && vfx.dustPuffs)
            {
                var emission = new ParticleSystem.EmitParams { position = bounds.center, startSize = Mathf.Clamp(size * .5f, .35f, 1.3f),
                    startLifetime = .45f, startColor = kind == DecorationCrushKind.Vegetation ? new Color(.58f, .68f, .39f) : new Color(.78f, .65f, .48f) };
                vfx.dustPuffs.Emit(emission, 4);
            }
        }

        void PlayImpact(DecorationCrushKind kind, Vector3 point)
        {
            // A low contact sound, not the cache/kill/explosion feedback paths that announce rewards.
            var effects = session ? session.effects : null;
            if (!effects || !effects.isActiveAndEnabled || !effects.source || !effects.impact) return;
            float volume = kind == DecorationCrushKind.Metal ? .08f : kind == DecorationCrushKind.Wood ? .045f : .025f;
            effects.source.PlayOneShot(effects.impact, volume);
        }

        void Update()
        {
            SynchronizeRun();
            if (!session || !session.Playing || Time.deltaTime <= 0 || pieces == null || ActiveDebrisCount == 0) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i]; if (!p.active) continue;
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.active = false; p.renderer.enabled = false; ActiveDebrisCount--; pieces[i] = p; continue;
                }
                float crumble = Mathf.SmoothStep(0, 1, p.age / .1f);
                p.velocity += Vector3.down * (7 * dt * crumble);
                p.transform.position += p.velocity * (dt * crumble);
                p.transform.Rotate(p.spin * (dt * crumble), Space.Self);
                Vector3 position = p.transform.position;
                if (position.y < p.ground)
                {
                    position.y = p.ground; p.transform.position = position;
                    p.velocity = new Vector3(p.velocity.x * .72f, 0, p.velocity.z * .72f); p.spin *= .55f;
                }
                p.transform.localScale = p.scale * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(p.life - .3f, p.life, p.age)));
                pieces[i] = p;
            }
        }

        void ClearDebris()
        {
            if (pieces != null) for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i].active = false;
                if (pieces[i].renderer) pieces[i].renderer.enabled = false;
            }
            ActiveDebrisCount = 0; nextPiece = 0;
        }

        static Color KindColour(DecorationCrushKind kind)
        {
            switch (kind)
            {
                case DecorationCrushKind.Vegetation: return new Color(.39f, .59f, .35f);
                case DecorationCrushKind.Wood: return new Color(.67f, .43f, .25f);
                case DecorationCrushKind.Metal: return new Color(.62f, .48f, .37f);
                case DecorationCrushKind.Rubber: return new Color(.19f, .22f, .22f);
                default: return new Color(.87f, .81f, .65f);
            }
        }

        static Mesh CubeMesh()
        {
            var mesh = new Mesh { name = "Shared cosmetic scenery fragment", hideFlags = HideFlags.DontSave };
            Vector3[] v = {
                new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),
                new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),
                new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,.5f,.5f),
                new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f),
                new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f),
                new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,-.5f) };
            int[] indices = new int[36];
            for (int f = 0; f < 6; f++) { int k = f * 6, a = f * 4; indices[k] = a; indices[k + 1] = a + 2; indices[k + 2] = a + 1; indices[k + 3] = a; indices[k + 4] = a + 3; indices[k + 5] = a + 2; }
            mesh.vertices = v; mesh.triangles = indices; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void OnDisable()
        {
            if (subscribed) subscribed.Changed -= SessionChanged;
            subscribed = null; ClearDebris(); ResetSweepHistory();
        }

        void OnDestroy()
        {
            if (pieceMesh) Destroy(pieceMesh);
            if (poolRoot) Destroy(poolRoot.gameObject);
        }
    }
}
