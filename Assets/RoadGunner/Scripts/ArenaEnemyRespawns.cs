using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RoadGunner
{
    /// <summary>
    /// One death-site job per authored non-final enemy. Owns time and lifecycle; markers only
    /// present this state. An occupied site waits at zero instead of moving the enemy elsewhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaEnemyRespawns : MonoBehaviour
    {
        public GameSession session;
        [Min(.1f)] public float respawnDelaySeconds = 30;
        [Min(.1f)] public float retryInterval = .5f;
        public Camera view;
        public Material ringMaterial;
        public Font labelFont;
        [Tooltip("Existing authored terrain collider; roads and obstacles remain separate occupancy checks.")]
        public Collider ground;

        public sealed class Entry
        {
            public CombatTarget Target { get; internal set; }
            public bool Pending { get; internal set; }
            public Vector3 DeathPosition { get; internal set; }
            public Vector3 Site { get; internal set; }
            public float Remaining { get; internal set; }
            public float Total { get; internal set; }
            public bool Blocked { get; internal set; }
            public string BlockReason { get; internal set; } = "";
            public Collider BlockingCollider { get; internal set; }
            public string SupportReason { get; internal set; } = "";
            public int RespawnCount { get; internal set; }
            public EnemyRespawnMarker Marker { get; internal set; }
            internal Quaternion rotation;
            internal Vector3 spawnSite;
            internal int group, index, queuedLife = -1, runSerial;
            internal float retryAfter;
            internal Shape[] shapes;
        }
        internal sealed class Shape
        {
            public Collider collider;
            public Matrix4x4 relative;
        }

        public IReadOnlyList<Entry> Entries => entries;
        public int RegisteredCount => entries.Count;
        public int PendingCount { get; private set; }
        public bool OwnsAuthoredRoster { get; private set; }
        public event Action Changed;

        readonly List<Entry> entries = new List<Entry>(16);
        readonly Dictionary<CombatTarget, Entry> byTarget = new Dictionary<CombatTarget, Entry>();
        readonly Collider[] occupancy = new Collider[128];
        NavMeshPath path;
        Collider roadSupport;
        GameSession subscribed;
        int runSerial;

        public Entry Find(CombatTarget target) => target && byTarget.TryGetValue(target, out var entry) ? entry : null;

        public void Bind(GameSession value)
        {
            if (subscribed && subscribed != value) subscribed.Changed -= SessionChanged;
            session = value;
            if (!session) return;
            path ??= new NavMeshPath();
            if (subscribed != session) { subscribed = session; subscribed.Changed += SessionChanged; }
            if (!view) view = session.spawns && session.spawns.view ? session.spawns.view : session.input ? session.input.view : null;
            if (!ground && session.minimap && session.minimap.layout && session.minimap.layout.ground)
                ground = session.minimap.layout.ground.GetComponent<Collider>();
            if (!roadSupport && session.minimap && session.minimap.layout && session.minimap.layout.authoredContentRoot)
            {
                var surface = session.minimap.layout.authoredContentRoot.Find("Arena exploration/Physical connected roads and combat pockets");
                if (surface) roadSupport = surface.GetComponent<Collider>();
            }
            // Serialized fixed references only. No scene search occurs during countdowns or retries.
            if (entries.Count == 0)
            {
                var director = session.spawns;
                OwnsAuthoredRoster = director && ((director.authoredRaiders != null && director.authoredRaiders.Length > 0) ||
                    (director.authoredBuggies != null && director.authoredBuggies.Length > 0));
                if (director)
                {
                    if (director.authoredRaiders != null) foreach (var actor in director.authoredRaiders) Register(actor);
                    if (director.authoredBuggies != null) foreach (var actor in director.authoredBuggies) Register(actor);
                }
                if (session.zone1 && session.zone1.ExtraTargets != null)
                    foreach (var actor in session.zone1.ExtraTargets) if (actor is WastelandMutant) Register(actor);
            }
            runSerial = session.RunSerial;
        }

        void Register(CombatTarget actor)
        {
            if (!actor || byTarget.ContainsKey(actor) || actor is Raider boss && boss.arenaBoss) return;
            if (!(actor is Raider) && !(actor is EnemyBuggy) && !(actor is WastelandMutant)) return;
            var list = new List<Shape>();
            Collider hitbox = actor is Raider raider ? raider.hitbox : actor is WastelandMutant mutant ? mutant.hitbox : null;
            foreach (var collider in actor.GetComponentsInChildren<Collider>(true))
                // Human/Monster hitboxes are triggers, but their occupied body volume is still real.
                if ((!collider.isTrigger || collider == hitbox) && (collider is BoxCollider || collider is CapsuleCollider || collider is SphereCollider))
                    list.Add(new Shape { collider = collider, relative = actor.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix });
            var go = new GameObject(actor.name + " respawn countdown");
            go.transform.SetParent(transform, false);
            var marker = go.AddComponent<EnemyRespawnMarker>();
            marker.font = labelFont; marker.Configure(view, ringMaterial); marker.Hide();
            var entry = new Entry { Target = actor, Marker = marker, shapes = list.ToArray() };
            entries.Add(entry); byTarget.Add(actor, entry);
        }

        static int Life(CombatTarget actor) => actor is Raider r ? r.LifeSerial : actor is EnemyBuggy b ? b.LifeSerial : actor is WastelandMutant m ? m.LifeSerial : -1;
        static bool Rewarded(CombatTarget actor) => actor is Raider r ? r.RewardGranted : actor is EnemyBuggy b ? b.RewardGranted : actor is WastelandMutant m && m.RewardGranted;

        /// <summary>Called only by real Hit death branches, after the one reward for this life.</summary>
        public bool QueueDeath(CombatTarget actor)
        {
            var entry = Find(actor);
            if (!isActiveAndEnabled || !session || !session.Playing || entry == null || actor.Alive || !Rewarded(actor) ||
                entry.Pending || entry.queuedLife == Life(actor)) return false;
            entry.queuedLife = Life(actor); entry.runSerial = session.RunSerial;
            entry.DeathPosition = actor.transform.position;
            entry.Site = GroundSite(entry.DeathPosition, out var site) ? site : entry.DeathPosition;
            entry.spawnSite = entry.Site;
            entry.SupportReason = "";
            Vector3 forward = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up);
            entry.rotation = forward.sqrMagnitude > .001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
            if (actor is Raider raider) { entry.group = raider.Group; entry.index = raider.SpawnIndex; }
            else if (actor is EnemyBuggy buggy) entry.index = buggy.SpawnSlot;
            entry.Total = entry.Remaining = Mathf.Max(.1f, respawnDelaySeconds);
            entry.Pending = true; entry.Blocked = false; entry.BlockReason = ""; entry.BlockingCollider = null; entry.retryAfter = 0; PendingCount++;
            entry.Marker.Show(entry.Site, entry.Remaining, entry.Total);
            if (session.spawns) session.spawns.RefreshActiveCount();
            Changed?.Invoke(); return true;
        }

        bool GroundSite(Vector3 position, out Vector3 site)
        {
            if (ground && ground.enabled && ground.Raycast(new Ray(position + Vector3.up * 20, Vector3.down), out var hit, 100))
            { site = hit.point; return true; }
            site = position; return false;
        }

        public void ResetForRun()
        {
            if (session && entries.Count == 0) Bind(session);
            CancelAll(); runSerial = session ? session.RunSerial : 0;
            foreach (var entry in entries) { entry.queuedLife = -1; entry.RespawnCount = 0; entry.runSerial = runSerial; entry.SupportReason = ""; }
        }
        public void CancelAll()
        {
            bool changed = PendingCount > 0;
            foreach (var entry in entries)
            {
                entry.Pending = false; entry.Blocked = false; entry.BlockReason = ""; entry.BlockingCollider = null; entry.Remaining = 0; entry.retryAfter = 0;
                if (entry.Marker) entry.Marker.Hide();
            }
            PendingCount = 0; if (changed) Changed?.Invoke();
        }
        void SessionChanged()
        {
            if (!session) return;
            if (runSerial != session.RunSerial) ResetForRun();
            if (session.State == SessionState.Completed || session.State == SessionState.Defeated) CancelAll();
        }
        void OnEnable() { if (session) Bind(session); }
        void OnDisable() { CancelAll(); }
        void OnDestroy() { if (subscribed) subscribed.Changed -= SessionChanged; }

        void Update()
        {
            if (!session) return;
            if (runSerial != session.RunSerial) ResetForRun();
            if (!session.Playing) return;
            float delta = Time.deltaTime;
            foreach (var entry in entries)
            {
                if (!entry.Pending) continue;
                if (!entry.Target || entry.Target.Alive || entry.runSerial != session.RunSerial) { Cancel(entry); continue; }
                entry.Remaining = Mathf.Max(0, entry.Remaining - delta);
                entry.Marker.SetRemaining(entry.Remaining);
                if (entry.Remaining > 0) continue;
                entry.retryAfter -= delta;
                if (entry.retryAfter > 0) continue;
                entry.retryAfter = Mathf.Max(.1f, retryInterval);
                string previousReason = entry.BlockReason;
                if (!SafeSite(entry))
                {
                    if (!entry.Blocked || previousReason != entry.BlockReason)
                    {
                        entry.Blocked = true;
                        Debug.Log("ENEMY_RESPAWN_DEFERRED actor=" + entry.Target.name + "; reason=" + entry.BlockReason +
                            "; markerSite=" + entry.Site.ToString("F4") + "; standingSite=" + entry.spawnSite.ToString("F4") +
                            (entry.BlockingCollider ? "; blockerBounds=" + entry.BlockingCollider.bounds : ""), entry.Target);
                        Changed?.Invoke();
                    }
                    continue;
                }
                Respawn(entry);
                // A synchronous callback may end/restart the run; no later job can leak through it.
                if (!session.Playing || runSerial != session.RunSerial) return;
            }
        }
        void Cancel(Entry entry)
        {
            if (!entry.Pending) return;
            entry.Pending = false; entry.Blocked = false; entry.BlockReason = ""; entry.BlockingCollider = null; entry.Remaining = 0;
            entry.Marker.Hide(); PendingCount = Mathf.Max(0, PendingCount - 1); Changed?.Invoke();
        }

        bool SafeSite(Entry entry)
        {
            entry.BlockReason = ""; entry.BlockingCollider = null;
            if (!GroundSite(entry.Site, out var surface)) return Reject(entry, "terrain-missing");
            if ((surface - entry.Site).sqrMagnitude > .04f) return Reject(entry, "terrain-height-changed: " + surface.ToString("F4"));
            int mask = entry.Target is Raider r && r.agent ? r.agent.areaMask : entry.Target is WastelandMutant m && m.agent ? m.agent.areaMask : NavMesh.AllAreas;
            if (!NavMesh.SamplePosition(entry.Site, out var nav, .35f, mask)) return Reject(entry, "navigation-missing");
            if (Vector3.ProjectOnPlane(nav.position - entry.Site, Vector3.up).sqrMagnitude > .01f)
                return Reject(entry, "navigation-XZ-differs: " + nav.position.ToString("F4"));
            if (Mathf.Abs(nav.position.y - entry.Site.y) > .2f) return Reject(entry, "navigation-height-differs: " + nav.position.ToString("F4"));
            // NavMesh agents stand on the baked navigation surface, which is above the bare
            // terrain/road triangles (the saved arena has e.g. -.03 terrain and +.12 agent Y).
            // Predict the same standing pose that Warp/PlaceOnNavigation actually uses, while
            // keeping the marker and horizontal death-site immutable. Buggy suspension uses ground.
            entry.spawnSite = entry.Target is EnemyBuggy ? entry.Site : new Vector3(entry.Site.x, nav.position.y, entry.Site.z);
            if (!NavMesh.SamplePosition(session.startPosition, out var anchor, 3, mask) ||
                !NavMesh.CalculatePath(nav.position, anchor.position, mask, path) || path.status != NavMeshPathStatus.PathComplete)
                return Reject(entry, "navigation-path-incomplete");
            if (entry.shapes.Length == 0) return Reject(entry, "body-shapes-empty");
            var target = entry.Target.transform;
            Vector3 root = entry.spawnSite + (entry.Target is EnemyBuggy ? Vector3.up * .65f : Vector3.zero);
            Matrix4x4 pose = Matrix4x4.TRS(root, entry.rotation, target.lossyScale);
            foreach (var shape in entry.shapes)
            {
                if (!shape.collider) return Reject(entry, "body-collider-missing");
                Matrix4x4 world = pose * shape.relative;
                Vector3 scale = new Vector3(world.GetColumn(0).magnitude, world.GetColumn(1).magnitude, world.GetColumn(2).magnitude);
                int count; Vector3 centre; float bottom;
                if (shape.collider is BoxCollider box)
                {
                    centre = world.MultiplyPoint3x4(box.center);
                    bottom = centre.y - (Mathf.Abs(world.m10) * box.size.x + Mathf.Abs(world.m11) * box.size.y + Mathf.Abs(world.m12) * box.size.z) * .5f;
                    count = Physics.OverlapBoxNonAlloc(centre, Vector3.Scale(box.size * .5f, scale) + Vector3.one * .05f,
                        occupancy, world.rotation, ~0, QueryTriggerInteraction.Collide);
                }
                else if (shape.collider is CapsuleCollider capsule)
                {
                    int axis = capsule.direction; Vector3 axisVector = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                    float radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float shaft = Mathf.Max(0, capsule.height * scale[axis] * .5f - radius);
                    centre = world.MultiplyPoint3x4(capsule.center); Vector3 offset = world.rotation * axisVector * shaft;
                    bottom = centre.y - Mathf.Abs(offset.y) - radius;
                    count = Physics.OverlapCapsuleNonAlloc(centre - offset, centre + offset, radius + .05f, occupancy, ~0, QueryTriggerInteraction.Collide);
                }
                else
                {
                    var sphere = (SphereCollider)shape.collider;
                    centre = world.MultiplyPoint3x4(sphere.center); float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    bottom = centre.y - radius;
                    count = Physics.OverlapSphereNonAlloc(centre, radius + .05f,
                        occupancy, ~0, QueryTriggerInteraction.Collide);
                }
                if (count >= occupancy.Length) return Reject(entry, "occupancy-query-full");
                for (int i = 0; i < count; i++)
                {
                    var hit = occupancy[i];
                    if (!hit || hit == ground || hit.transform.IsChildOf(target)) continue;
                    // Ignore collectible/scenery/zone query triggers while retaining other live
                    // enemies' trigger hitboxes as occupancy, including simultaneous respawn jobs.
                    if (hit.isTrigger)
                    {
                        var occupant = hit.GetComponentInParent<CombatTarget>();
                        if (!occupant || !occupant.Alive) continue;
                    }
                    // The arena road is a single ground-conforming mesh whose global Y bounds
                    // include distant hills. Resolve only this known support using local geometry.
                    // Other colliders (including player and walls) retain the full occupancy guard.
                    if (hit == roadSupport && SupportingRoadContact(shape.collider, world, hit, centre, bottom, out string support))
                    {
                        if (entry.SupportReason == "")
                        {
                            entry.SupportReason = support;
                            Debug.Log("ENEMY_RESPAWN_SUPPORT actor=" + entry.Target.name + "; " + support +
                                "; standingSite=" + entry.spawnSite.ToString("F4"), entry.Target);
                        }
                        continue;
                    }
                    // The supporting road/terrain slab is beneath the actor's physical standing plane.
                    if (hit.bounds.max.y <= entry.Site.y + .09f) continue;
                    return Reject(entry, "occupied: " + ColliderPath(hit.transform), hit);
                }
            }
            return true;
        }
        static bool SupportingRoadContact(Collider body, Matrix4x4 pose, Collider surface, Vector3 centre, float bottom, out string reason)
        {
            reason = "";
            // A real upward-facing local floor must be immediately below the body. A broad-phase
            // skin hit is not a body penetration; a real penetration must be shallow and upward.
            if (!surface.Raycast(new Ray(centre + Vector3.up * .25f, Vector3.down), out var floor,
                    Mathf.Max(1, centre.y - bottom + .75f)) || floor.normal.y < .9f || floor.point.y > bottom + .05f) return false;
            bool penetrates = Physics.ComputePenetration(body, pose.MultiplyPoint3x4(Vector3.zero), pose.rotation,
                surface, surface.transform.position, surface.transform.rotation, out var direction, out float depth);
            if (penetrates && (direction.y < .9f || depth > .05f)) return false;
            reason = (penetrates ? "shallow-upward-floor" : "query-skin-only-floor") + "; localY=" + floor.point.y.ToString("F4") +
                "; bodyBottom=" + bottom.ToString("F4") + "; penetration=" + (penetrates ? depth : 0).ToString("F4");
            return true;
        }
        static bool Reject(Entry entry, string reason, Collider collider = null)
        { entry.BlockReason = reason; entry.BlockingCollider = collider; return false; }
        static string ColliderPath(Transform value) => value.parent ? ColliderPath(value.parent) + "/" + value.name : value.name;
        void Respawn(Entry entry)
        {
            if (!entry.Pending || !session.Playing || entry.runSerial != session.RunSerial) return;
            if (entry.Target is Raider raider) { raider.Spawn(entry.spawnSite, entry.group, entry.index); raider.transform.rotation = entry.rotation; }
            else if (entry.Target is EnemyBuggy buggy) buggy.Spawn(entry.spawnSite, entry.rotation, entry.index);
            else if (entry.Target is WastelandMutant mutant) mutant.RespawnAt(entry.spawnSite, entry.rotation);
            if (!entry.Target.Alive) return;
            entry.RespawnCount++; Cancel(entry); Physics.SyncTransforms();
            if (session.spawns) session.spawns.RefreshActiveCount();
        }
    }
}
