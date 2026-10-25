#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator CorridorRouteChecks()
        {
            var corridor = Object.FindAnyObjectByType<ArenaCorridorLayout>();
            if (!corridor || corridor.mainRoad == null || corridor.mainRoad.Length < 3)
                throw new InvalidOperationException("The saved corridor layout/main-road metadata is missing.");
            Vector3 north = s.input.ScreenToWorldDirection(Vector2.up), right = s.input.ScreenToWorldDirection(Vector2.right);
            Vector3 mainDelta = corridor.mainRoad[corridor.mainRoad.Length - 1] - corridor.mainRoad[0]; mainDelta.y = 0;
            Vector3 workshopPoint = corridor.workshop.position;
            Vector3 workshopOffset = workshopPoint - NearestCorridorPoint(workshopPoint, corridor.mainRoad);
            Vector3 bossOffset = corridor.bossArea.position - NearestCorridorPoint(corridor.bossArea.position, corridor.mainRoad);
            Vector3 monsterOffset = corridor.monsterArea.position - NearestCorridorPoint(corridor.monsterArea.position, corridor.mainRoad);
            Check("corridor topology follows north with deliberate side branches", Vector3.Dot(mainDelta, north) > 120 &&
                Vector3.Dot((corridor.mainRoad[1] - corridor.mainRoad[0]).normalized, north) > .95f &&
                Vector3.Dot(workshopOffset, right) > 8 && Vector3.Dot(bossOffset, right) < -30 && Vector3.Dot(monsterOffset, right) > 50 &&
                corridor.bossRoad.Length >= 3 && corridor.monsterRoad.Length >= 3 && corridor.workshopRoad.Length >= 2,
                "Northward main-road extent=" + Vector3.Dot(mainDelta, north).ToString("F1") + "m; workshop right=" +
                Vector3.Dot(workshopOffset, right).ToString("F1") + "; boss left=" + Vector3.Dot(bossOffset, right).ToString("F1") +
                "; far-right Monster=" + Vector3.Dot(monsterOffset, right).ToString("F1"));
            bool gateSolid = corridor.lockedGate && corridor.lockedApproach && corridor.lockedArena &&
                corridor.gateColliders != null && corridor.gateColliders.Any(c => c && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy);
            bool gateVisible = corridor.lockedGate && corridor.lockedGate.GetComponentsInChildren<Renderer>(false).Any(r => r.enabled);
            bool gateBlocks = false;
            if (gateSolid)
            {
                Vector3 from = corridor.lockedApproach.position + Vector3.up * 1.1f;
                Vector3 towardGate = corridor.lockedGate.position - corridor.lockedApproach.position; towardGate.y = 0;
                Vector3 to = corridor.lockedGate.position + towardGate.normalized * 3 + Vector3.up * 1.1f;
                gateBlocks = Physics.Linecast(from, to, out var hit, 1, QueryTriggerInteraction.Ignore) &&
                    (corridor.gateColliders.Contains(hit.collider) || hit.transform.IsChildOf(corridor.lockedGate));
            }
            Check("north route ends at a visible physical closed gate", gateSolid && gateVisible && gateBlocks &&
                Vector3.Distance(corridor.mainRoad[corridor.mainRoad.Length - 1], corridor.lockedApproach.position) < .5f &&
                Vector3.Distance(corridor.lockedApproach.position, corridor.lockedGate.position) >= 5 &&
                Vector3.Dot(corridor.lockedArena.position - corridor.lockedGate.position, north) > 0,
                "Visible=" + gateVisible + "; solid=" + gateSolid + "; approach ray blocked=" + gateBlocks +
                "; approach-to-gate=" + Vector3.Distance(corridor.lockedApproach.position, corridor.lockedGate.position).ToString("F1") + "m");

            var map = s.minimap; Bounds view = map.WorldBounds;
            var landmarks = new[] { layout.playerSpawn.position, corridor.workshop.position, corridor.bossArea.position,
                corridor.monsterArea.position, corridor.lockedApproach.position, corridor.lockedGate.position };
            bool footprintFits = corridor.mapFootprint != null && corridor.mapFootprint.Length > 3 &&
                corridor.mapFootprint.All(point => CorridorContainsXZ(view, point) && map.IsInsideCircle(map.Project(point), 1));
            bool landmarksFit = landmarks.All(point => CorridorContainsXZ(view, point) && map.IsInsideCircle(map.Project(point), 18));
            bool routesFit = new[] { corridor.mainRoad, corridor.bossRoad, corridor.monsterRoad, corridor.workshopRoad }
                .All(route => route.All(point => CorridorContainsXZ(view, point) && map.IsInsideCircle(map.Project(point), 18)));
            Check("focused round map includes the full corridor and far landmarks", footprintFits && landmarksFit && routesFit &&
                map.BackgroundTexture && map.BackgroundTexture.width == 512 && map.contentScale > .99f,
                "Road/boundary footprint fits=" + footprintFits + "; route centers fit=" + routesFit + "; Monster/workshop/boss/gate fit=" + landmarksFit +
                "; texture=" + (map.BackgroundTexture ? map.BackgroundTexture.width.ToString() : "missing") + "; focused bounds=" + view);
            var path = new NavMeshPath();
            int connected = 0; var raiders = s.spawns.authoredRaiders.Where(r => r && !r.arenaBoss).ToArray();
            bool startOnNav = NavMesh.SamplePosition(layout.playerSpawn.position, out var startNav, 3, NavMesh.AllAreas);
            if (startOnNav) foreach (var raider in raiders)
                if (NavMesh.SamplePosition(raider.transform.position, out var target, 2, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(startNav.position, target.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) connected++;
            Check("all twelve corridor Raiders remain reachable", startOnNav && raiders.Length == 12 && connected == 12,
                "Complete actual baked NavMesh paths from spawn=" + connected + "/" + raiders.Length);
            yield return null;
            foreach (var route in new[] { new CorridorDriveCase("main-to-locked-approach", corridor.mainRoad, corridor.roadWidth),
                new CorridorDriveCase("right-workshop-spur", corridor.workshopRoad, corridor.roadWidth),
                new CorridorDriveCase("left-boss-branch", corridor.bossRoad, corridor.branchWidth),
                new CorridorDriveCase("far-right-Monster-branch", corridor.monsterRoad, corridor.branchWidth) })
            {
                IEnumerator drive = TraverseCorridorRoute(route);
                while (drive.MoveNext()) yield return drive.Current;
                (drive as IDisposable)?.Dispose();
            }
        }

        sealed class CorridorDriveCase
        {
            public readonly string name;
            public readonly Vector3[] points;
            public readonly float width;
            public CorridorDriveCase(string routeName, Vector3[] routePoints, float roadWidth)
            { name = routeName; points = routePoints; width = roadWidth; }
        }

        IEnumerator TraverseCorridorRoute(CorridorDriveCase route)
        {
            Isolate();
            var points = route.points;
            if (points == null || points.Length < 2) throw new InvalidOperationException("Missing route " + route.name);
            if (!NavMesh.SamplePosition(points[0], out var startNav, 2, NavMesh.AllAreas))
                throw new InvalidOperationException("Route start has no actual navigation: " + route.name);
            Vector3 initialDirection = points[1] - points[0]; initialDirection.y = 0;
            // This is the only pose reset in the traversal: every remaining waypoint is reached by driving.
            PlaceCar(startNav.position + Vector3.up * .7f, Quaternion.LookRotation(initialDirection));
            s.vehicle.Body.constraints = carConstraints;
            yield return new WaitForSecondsRealtime(.6f);
            float length = 0;
            for (int i = 1; i < points.Length; i++) length += FlatDistance(points[i - 1], points[i]);
            float began = Time.realtimeSinceStartup, deadline = began + length / 4.5f + 12;
            float maximumDeviation = 0, minimumUpright = 1, airborneTime = 0, drivenDistance = 0;
            int waypoint = 1; Vector3 lastPosition = s.vehicle.transform.position;
            var fixedStep = new WaitForFixedUpdate();
            while (waypoint < points.Length && Time.realtimeSinceStartup < deadline)
            {
                Vector3 position = s.vehicle.transform.position;
                while (waypoint < points.Length)
                {
                    Vector3 segment = points[waypoint] - points[waypoint - 1]; segment.y = 0;
                    Vector3 along = position - points[waypoint - 1]; along.y = 0;
                    if (FlatDistance(position, points[waypoint]) <= 3 || Vector3.Dot(along, segment.normalized) >= segment.magnitude) waypoint++;
                    else break;
                }
                if (waypoint >= points.Length) break;
                Vector3 direction = points[waypoint] - position; direction.y = 0;
                float heading = Vector3.Angle(Vector3.ProjectOnPlane(s.vehicle.transform.forward, Vector3.up), direction);
                float magnitude = heading > 50 ? .55f : .82f;
                if (waypoint == points.Length - 1 && direction.magnitude < 8) magnitude = Mathf.Min(magnitude, .5f);
                Drive(direction.normalized, magnitude);
                yield return fixedStep;
                Vector3 current = s.vehicle.transform.position;
                drivenDistance += FlatDistance(lastPosition, current); lastPosition = current;
                maximumDeviation = Mathf.Max(maximumDeviation, FlatDistance(current, NearestCorridorPoint(current, points)));
                minimumUpright = Mathf.Min(minimumUpright, Vector3.Dot(s.vehicle.transform.up, Vector3.up));
                if (s.vehicle.GroundedWheels < 2) airborneTime += Time.fixedDeltaTime;
            }
            Drive(Vector3.zero, 0);
            float stopBy = Time.realtimeSinceStartup + 1.5f;
            while (s.vehicle.Speed > .3f && Time.realtimeSinceStartup < stopBy) yield return fixedStep;
            float arrivalError = FlatDistance(s.vehicle.transform.position, points[points.Length - 1]);
            bool reached = waypoint >= points.Length;
            Check("actual vehicle continuously drives " + route.name, reached && arrivalError < 6 &&
                drivenDistance >= length * .65f && maximumDeviation <= route.width * .5f - 1 && minimumUpright > .75f && airborneTime < 1.5f && s.vehicle.Speed < .8f,
                "Reached=" + reached + "; waypoints=" + Math.Min(waypoint, points.Length) + "/" + points.Length +
                "; route length=" + length.ToString("F1") + "m; actual travel=" + drivenDistance.ToString("F1") +
                "m; arrival error=" + arrivalError.ToString("F1") + "m; max center deviation=" + maximumDeviation.ToString("F1") +
                "m; minimum upright=" + minimumUpright.ToString("F2") + "; airborne=" + airborneTime.ToString("F2") +
                "s; elapsed=" + (Time.realtimeSinceStartup - began).ToString("F1") + "s; pose resets=1 at route start only");
            Capture("corridor-route-" + route.name);
        }

        static bool CorridorContainsXZ(Bounds bounds, Vector3 point) => point.x >= bounds.min.x - .01f && point.x <= bounds.max.x + .01f &&
            point.z >= bounds.min.z - .01f && point.z <= bounds.max.z + .01f;
        static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static Vector3 NearestCorridorPoint(Vector3 point, Vector3[] road)
        {
            Vector3 best = road[0]; float distance = float.PositiveInfinity;
            for (int i = 1; i < road.Length; i++)
            {
                Vector3 delta = road[i] - road[i - 1]; delta.y = 0;
                Vector3 offset = point - road[i - 1]; offset.y = 0;
                float t = delta.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(offset, delta) / delta.sqrMagnitude) : 0;
                Vector3 candidate = road[i - 1] + delta * t;
                float current = FlatDistance(point, candidate);
                if (current < distance) { distance = current; best = candidate; }
            }
            return best;
        }
    }
}
#endif
