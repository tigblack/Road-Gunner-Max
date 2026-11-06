using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>Moves the existing gameplay objects onto the reference roads; never rebuilds or rebalances them.</summary>
    public static class ReferenceArenaGameplayAuthoring
    {
        public const string PreservedRootName = "Reference arena preserved gameplay";
        public static string LastConfigurationSnapshotJson { get; private set; }
        public static string LastReport { get; private set; }
        static readonly Quaternion Axis = Quaternion.Euler(0, 45, 0);
        static readonly Vector2 HealthW = new Vector2(9, -40), AttackW = new Vector2(-26, 17);
        static readonly Dictionary<LevelLayout, Context> contexts = new Dictionary<LevelLayout, Context>();

        sealed class Context
        {
            public Transform root;
            public RepairWorkshop workshop;
            public ArenaUpgradeStation[] stations;
            public Raider boss;
            public WastelandMutant monster;
            public float playerLift;
            public readonly List<Guard> guards = new List<Guard>();
            public Snapshot before;
        }
        sealed class Guard { public Object target; public string mode, id; }
        [Serializable] sealed class Row { public string id, type, mode, json; }
        [Serializable] sealed class Snapshot { public Row[] settings; }
        [Serializable] sealed class Comparison { public string scope; public bool unchanged; public Snapshot before, after; }
        [Serializable] sealed class PoseValue { public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] sealed class OwnerValue { public Vector3 scale; public string name; public int layer; public bool active; }
        [Serializable] sealed class BehaviourValue { public bool enabled; public string fields; }
        [Serializable] sealed class RespawnValue { public float delay, retry; public string material, font; }

        /// <summary>Call before deleting old scenery. Component roots contain the real workshop/station art and colliders.</summary>
        public static Transform Preserve(LevelLayout level)
        {
            Require(level);
            var session = level.session;
            var workshop = InScene<RepairWorkshop>(level).Single(w => w.session == session);
            var stations = session.runStats.stations;
            if (stations == null || stations.Length != 2 || stations.Any(s => !s) ||
                stations.Count(s => s.kind == ArenaUpgradeKind.Health) != 1 || stations.Count(s => s.kind == ArenaUpgradeKind.Attack) != 1)
                throw new InvalidOperationException("The two existing Attack/Health station objects are required.");
            var root = level.transform.Find(PreservedRootName);
            if (!root) { root = new GameObject(PreservedRootName).transform; root.SetParent(level.transform, false); }
            if (level.authoredContentRoot && root.IsChildOf(level.authoredContentRoot))
                throw new InvalidOperationException("Preserved gameplay must be outside the old authored-content root.");
            var context = new Context {
                root = root, workshop = workshop, stations = (ArenaUpgradeStation[])stations.Clone(),
                boss = session.zone1.bosses.OfType<Raider>().Single(r => r && r.arenaBoss),
                monster = session.zone1.bosses.OfType<WastelandMutant>().Single(m => m),
                playerLift = ExistingPlayerLift(level)
            };
            MoveParent(workshop.transform, root);
            foreach (var station in stations) MoveParent(station.transform, root);
            // The old exploration signs are siblings, rather than children, of the real workshop.
            var signs = level.authoredContentRoot ? level.authoredContentRoot.Find("Arena exploration/Repair hub wayfinding") : null;
            if (signs) MoveParent(signs, workshop.transform);
            PreserveExternal(level, workshop.transform, workshop.radiusMarking);
            PreserveExternal(level, workshop.transform, workshop.activeRepairEffect ? workshop.activeRepairEffect.transform : null);
            foreach (var station in stations)
            {
                PreserveExternal(level, station.transform, station.availableVisual ? station.availableVisual.transform : null);
                PreserveExternal(level, station.transform, station.consumedVisual ? station.consumedVisual.transform : null);
                PreserveExternal(level, station.transform, station.highlight ? station.highlight.transform : null);
                PreserveExternal(level, station.transform, station.displayText ? station.displayText.transform : null);
            }
            foreach (var actor in session.spawns.authoredRaiders.Concat(new[] { context.boss }))
                if (actor.spawnPoint && !actor.spawnPoint.IsChildOf(actor.transform)) MoveParent(actor.spawnPoint, root);
            if (context.monster.spawnPoint && !context.monster.spawnPoint.IsChildOf(context.monster.transform)) MoveParent(context.monster.spawnPoint, root);
            if (level.recoveryPoints != null) foreach (var marker in level.recoveryPoints) if (marker) MoveParent(marker, root);
            if (level.playerSpawn && level.authoredContentRoot && level.playerSpawn.IsChildOf(level.authoredContentRoot)) MoveParent(level.playerSpawn, root);
            CaptureGuards(level, context);
            context.before = ReadSnapshot(context);
            contexts[level] = context;
            return root;
        }

        public static void Apply(LevelLayout level, ReferenceArenaLayoutData data)
        {
            Require(level);
            if (!data || !data.geometry || data.raidersW == null || data.raidersW.Length != 12 || data.buggiesW == null || data.buggiesW.Length != 2 ||
                data.gatesW == null || data.gatesW.Length != 3 || data.geometry.recoveryLaybysW == null || data.geometry.recoveryLaybysW.Length == 0)
                throw new InvalidOperationException("Reference gameplay requires 12 Raider sites, two Buggy sites, three gates and recovery laybys.");
            if (!contexts.TryGetValue(level, out var context))
                throw new InvalidOperationException("Call ReferenceArenaGameplayAuthoring.Preserve before removing the old world.");
            var session = level.session;
            if (!context.workshop || context.stations.Any(s => !s) || !context.boss || !context.monster)
                throw new InvalidOperationException("A preserved gameplay owner was deleted while rebuilding the environment.");
            var terrain = level.ground.GetComponent<Collider>();
            if (!terrain || !terrain.enabled) throw new InvalidOperationException("The new actual ground collider is required before Apply.");
            Physics.SyncTransforms();
            if (!level.playerSpawn)
            {
                level.playerSpawn = new GameObject("Player Spawn").transform;
                level.playerSpawn.SetParent(context.root, false);
            }
            SetPose(level.playerSpawn, Ground(terrain, data.spawnW, context.playerLift), Axis);
            session.startPosition = level.playerSpawn.position; session.startRotation = level.playerSpawn.rotation;
            SetPose(session.vehicle.transform, session.startPosition, session.startRotation);
            for (int i = 0; i < session.spawns.authoredRaiders.Length; i++)
            {
                var actor = session.spawns.authoredRaiders[i]; var position = Ground(terrain, data.raidersW[i]);
                SetPose(actor.transform, position, Facing(data.raidersW[i], data.crossW));
                SyncHome(actor.transform, actor.spawnPoint, position, actor.transform.rotation);
            }
            for (int i = 0; i < session.spawns.authoredBuggies.Length; i++)
                SetPose(session.spawns.authoredBuggies[i].transform, Ground(terrain, data.buggiesW[i], .65f), Facing(data.buggiesW[i], data.crossW));
            SetPose(context.boss.transform, Ground(terrain, data.bossW), Facing(data.bossW, data.crossW));
            SyncHome(context.boss.transform, context.boss.spawnPoint, context.boss.transform.position, context.boss.transform.rotation);
            SetPose(context.monster.transform, Ground(terrain, data.monsterW), Facing(data.monsterW, data.crossW));
            SyncHome(context.monster.transform, context.monster.spawnPoint, context.monster.transform.position, context.monster.transform.rotation);
            SetPose(context.workshop.transform, Ground(terrain, data.workshopW), Axis);
            foreach (var station in context.stations)
                SetPose(station.transform, Ground(terrain, station.kind == ArenaUpgradeKind.Health ? HealthW : AttackW), Axis);
            var previous = level.recoveryPoints ?? Array.Empty<Transform>();
            var recoveries = new Transform[data.geometry.recoveryLaybysW.Length];
            for (int i = 0; i < recoveries.Length; i++)
            {
                var marker = i < previous.Length && previous[i] ? previous[i] : new GameObject("Recovery " + i.ToString("00")).transform;
                MoveParent(marker, context.root); recoveries[i] = marker;
                SetPose(marker, Ground(terrain, data.geometry.recoveryLaybysW[i], context.playerLift), Axis);
            }
            level.recoveryPoints = recoveries; session.recoveryPoints = recoveries.Select(p => p.position).ToArray();
            if (session.respawns) { session.respawns.ground = terrain; Dirty(session.respawns); }
            if (session.minimap)
            {
                session.minimap.workshops = new[] { context.workshop };
                session.minimap.upgrades = (ArenaUpgradeStation[])context.stations.Clone();
                Dirty(session.minimap);
            }
            Dirty(level); Dirty(session); Physics.SyncTransforms();
            var after = ReadSnapshot(context);
            bool unchanged = JsonUtility.ToJson(context.before) == JsonUtility.ToJson(after);
            LastConfigurationSnapshotJson = JsonUtility.ToJson(new Comparison {
                scope = "Same existing actors, vehicle/tracks, combat assets, component settings, meshes/materials and local presentation poses. Only owner placement, spawn/recovery markers and explicit scene bindings may change.",
                unchanged = unchanged, before = context.before, after = after
            }, true);
            if (!unchanged)
            {
                var changed = context.before.settings.Zip(after.settings, (a, b) => a.json != b.json ? a.id + " (" + a.type + ")" : null).Where(x => x != null);
                throw new InvalidOperationException("Reference layout changed protected gameplay/presentation settings: " + string.Join("; ", changed));
            }
            LastReport = "Preserved exactly12 authored Raiders,2 authored Buggies,1 Monster and1 final Boss Raider; legacy spawn arrays remain empty. " +
                "Actual-ground spawn/recovery/home markers updated; Workshop radius/rate unchanged; Health W=" + HealthW + "; Attack W=" + AttackW + ". " +
                "Protected configuration objects=" + after.settings.Length + "; before/after JSON identical. Existing workshop/station/minimap object references retained. Navigation and minimap bake remain the installer's next step.";
        }

        static void Require(LevelLayout level)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Reference authoring requires Edit Mode.");
            if (!level || !level.session || !level.ground || !level.session.vehicle || !level.session.spawns || !level.session.zone1 || !level.session.runStats)
                throw new InvalidOperationException("Existing saved level, player, terrain, authored roster and run stats are required.");
            var director = level.session.spawns;
            if (director.authoredRaiders == null || director.authoredRaiders.Length != 12 || director.authoredRaiders.Distinct().Count() != 12 || director.authoredRaiders.Any(r => !r || r.arenaBoss) ||
                director.authoredBuggies == null || director.authoredBuggies.Length != 2 || director.authoredBuggies.Distinct().Count() != 2 || director.authoredBuggies.Any(b => !b) ||
                director.GroupCount != 0 || (director.buggySpawnPoints != null && director.buggySpawnPoints.Length != 0))
                throw new InvalidOperationException("Retain exactly12 existing normal Raiders and2 Buggies, with no legacy encounter/vehicle spawners.");
        }
        static IEnumerable<T> InScene<T>(LevelLayout level) where T : Component =>
            level.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static Vector3 Ground(Collider terrain, Vector2 w, float lift = 0)
        {
            var position = ExplorationArenaInstaller.World(w);
            var origin = new Vector3(position.x, terrain.bounds.max.y + 100, position.z);
            if (!terrain.Raycast(new Ray(origin, Vector3.down), out var hit, terrain.bounds.size.y + 200))
                throw new InvalidOperationException("No actual new ground at W=" + w);
            return hit.point + Vector3.up * lift;
        }
        static float ExistingPlayerLift(LevelLayout level)
        {
            var point = level.playerSpawn ? level.playerSpawn.position : level.session.startPosition;
            var terrain = level.ground.GetComponent<Collider>();
            if (terrain && terrain.Raycast(new Ray(new Vector3(point.x, terrain.bounds.max.y + 100, point.z), Vector3.down), out var hit, terrain.bounds.size.y + 200))
                return Mathf.Clamp(point.y - hit.point.y, .3f, 1.5f);
            return .7f;
        }
        static Quaternion Facing(Vector2 from, Vector2 to)
        { var direction = Axis * new Vector3(to.x - from.x, 0, to.y - from.y); return direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : Axis; }
        static void SyncHome(Transform actor, Transform marker, Vector3 position, Quaternion rotation)
        { if (marker && !marker.IsChildOf(actor)) SetPose(marker, position, rotation); }
        static void SetPose(Transform target, Vector3 position, Quaternion rotation)
        { target.SetPositionAndRotation(position, rotation); Dirty(target); }
        static void MoveParent(Transform target, Transform parent)
        { if (target.parent != parent) { target.SetParent(parent, true); Dirty(target); } }
        static void PreserveExternal(LevelLayout level, Transform owner, Transform reference)
        {
            if (!reference || reference.IsChildOf(owner) || !level.authoredContentRoot || !reference.IsChildOf(level.authoredContentRoot)) return;
            if (owner.IsChildOf(reference)) throw new InvalidOperationException("A presentation reference cannot be an ancestor of its owner: " + reference.name);
            MoveParent(reference, owner);
        }
        static void Dirty(Object value)
        { if (!value) return; EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }

        static void CaptureGuards(LevelLayout level, Context context)
        {
            var session = level.session; var seen = new HashSet<string>();
            void Add(Object target, string mode = "settings")
            {
                if (!target || !seen.Add(target.GetEntityId().ToString())) return;
                context.guards.Add(new Guard { target = target, mode = mode, id = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() });
            }
            foreach (var component in new MonoBehaviour[] { session.vehicle, session.health, session.turret, session.spawns, session.zone1, session.runStats, session.input, session.follow, session.effects }) Add(component);
            Add(session.vehicle.tuning); Add(session.health.tuning); Add(session.turret.tuning); Add(session.spawns.tuning); Add(session.runStats.tuning);
            if (session.exploration) Add(session.exploration.tuning);
            if (session.respawns) Add(session.respawns, "respawn-tuning");
            var actors = session.spawns.authoredRaiders.Select(r => r.transform).Concat(session.spawns.authoredBuggies.Select(b => b.transform))
                .Concat(new[] { context.boss.transform, context.monster.transform, session.vehicle.transform, context.workshop.transform })
                .Concat(context.stations.Select(s => s.transform));
            foreach (var owner in actors)
            {
                Add(owner, "owner-scale");
                foreach (var component in owner.GetComponentsInChildren<Component>(true))
                {
                    if (!component) throw new InvalidOperationException("Missing script on protected owner: " + owner.name);
                    if (component is Transform child) { if (child != owner) Add(child, "local-pose"); }
                    else if (component is MonoBehaviour) Add(component);
                    else if (component is Collider || component is Rigidbody || component is NavMeshAgent || component is Renderer ||
                        component is MeshFilter || component is ParticleSystem || component is AudioSource) Add(component, "native-settings");
                }
            }
            foreach (var actor in session.spawns.authoredRaiders) Add(actor.tuning);
            foreach (var actor in session.spawns.authoredBuggies) Add(actor.tuning);
            Add(context.boss.tuning);
        }
        static Snapshot ReadSnapshot(Context context)
        {
            var rows = new List<Row>(context.guards.Count);
            foreach (var guard in context.guards)
            {
                if (!guard.target) throw new InvalidOperationException("Protected gameplay/presentation object was deleted: " + guard.id);
                string json;
                if (guard.mode == "owner-scale")
                {
                    var t = (Transform)guard.target;
                    json = JsonUtility.ToJson(new OwnerValue { scale = t.localScale, name = t.name, layer = t.gameObject.layer, active = t.gameObject.activeSelf });
                }
                else if (guard.mode == "local-pose")
                {
                    var t = (Transform)guard.target;
                    json = JsonUtility.ToJson(new PoseValue { position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                }
                else if (guard.mode == "respawn-tuning")
                {
                    var value = (ArenaEnemyRespawns)guard.target;
                    json = JsonUtility.ToJson(new RespawnValue { delay = value.respawnDelaySeconds, retry = value.retryInterval,
                        material = value.ringMaterial ? value.ringMaterial.GetEntityId().ToString() : "", font = value.labelFont ? value.labelFont.GetEntityId().ToString() : "" });
                }
                else if (guard.mode == "native-settings") json = EditorJsonUtility.ToJson(guard.target);
                else if (guard.target is Behaviour behaviour) json = JsonUtility.ToJson(new BehaviourValue { enabled = behaviour.enabled, fields = JsonUtility.ToJson(behaviour) });
                else json = JsonUtility.ToJson(guard.target);
                rows.Add(new Row { id = guard.id, type = guard.target.GetType().FullName, mode = guard.mode, json = json });
            }
            return new Snapshot { settings = rows.ToArray() };
        }
    }
}
