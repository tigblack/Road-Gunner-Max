using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Collider-free, static, chunk-combined dressing under Decoration/Arcade dressing, region-themed:
    /// Dust Flats dunes, tufts, cacti, pebbles, bones; Salvage Yard scrap piles, tyres, hull bits, sage tufts;
    /// Rustworks barrels, pipes, girders, rust plates; Ash Ridge lilac rock spires, dead white trees, ash drifts.
    /// Densest on pocket/road rims (~1 per 20 m²), sparse small flat items inside pockets, clustered in open sand;
    /// never on the travelled lanes (route distance raster), clear of the spawn, workshop, stations and caches, one
    /// shared palette-atlas material. Deterministic: re-running rebuilds the identical result.
    /// </summary>
    public static class ArcadeVisualDressing
    {
        public const string RootName = "Arcade dressing";
        const string MeshFolder = ArcadeVisualInstaller.Root + "/Meshes";
        const string MeshPrefix = "Arcade dressing ";
        const float Spacing = 2.5f, ChunkSize = 64f;

        // Atlas cells (ConceptEnemies.Palette order + arcade spares).
        const int SandCell = 0, SandLightCell = 1, SandShadeCell = 2, RockCell = 4, RockLightCell = 5, RockShadeCell = 6, TealCell = 7, TealDarkCell = 9,
            IvoryCell = 10, IvoryShadeCell = 11, RustCell = 12, RustDarkCell = 13, SteelCell = 14, SteelLightCell = 15, RubberCell = 16, RubberEdgeCell = 17,
            WoodCell = 22, RedCell = 26, YellowCell = 27, CactusCell = 28, DryLeavesCell = 29, AshStoneCell = 31;

        enum Zone { Rim, Pocket, Open }

        public sealed class ReplayItem
        {
            public string id, category;
            public int start, count;
            public bool primary;
            public Bounds worldBounds;
        }
        public sealed class ReplayChunk
        {
            public Vector2Int key;
            public Vector3[] vertices, normals;
            public Vector2[] uvs;
            public int[] triangles;
            public ReplayItem[] items;
        }
        public sealed class ReplayResult { public ReplayChunk[] chunks; public int primaryItems; }
        static bool recordingOnly;
        static ReplayResult recorded;

        /// <summary>Replays only the original deterministic recipe; never writes a mesh, material, prefab or scene.</summary>
        public static ReplayResult ReplayRecords(GameSession session)
        {
            if (recordingOnly) throw new InvalidOperationException("Nested dressing replay is unsupported.");
            recordingOnly = true; recorded = null;
            try { Apply(session, new List<string>()); return recorded ?? throw new InvalidOperationException("No saved dressing/layout to replay."); }
            finally { recordingOnly = false; recorded = null; }
        }

        sealed class Builder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
            public int items;
            public string parentId, category;
            public readonly List<ReplayItem> records = new List<ReplayItem>();
            public void Record(string name, int start, string suffix, bool primary)
            {
                int count = triangles.Count - start;
                if (count == 0) throw new InvalidOperationException("Empty dressing item " + parentId);
                var bounds = new Bounds(vertices[triangles[start]], Vector3.zero);
                for (int i = start; i < triangles.Count; i++) bounds.Encapsulate(vertices[triangles[i]]);
                records.Add(new ReplayItem { id = parentId + suffix, category = name, start = start, count = count, primary = primary, worldBounds = bounds });
            }

            // Flat-shaded triangle, wound so its normal faces away from 'inside' (works for every convex part).
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, int cell)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0) { var swap = b; b = c; c = swap; normal = -normal; }
                normal.Normalize();
                var uv = ArcadeVisualPalette.CellUv(cell);
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, int cell) { Tri(a, b, c, inside, cell); Tri(a, c, d, inside, cell); }
        }

        static readonly Vector3[] Ico = BuildIco();
        static readonly int[] IcoFaces =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        static Vector3[] BuildIco()
        {
            float t = (1 + Mathf.Sqrt(5)) * .5f;
            return new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            }.Select(v => v.normalized).ToArray();
        }

        public static void Apply(GameSession session, List<string> log)
        {
            var layout = RoadGunnerLevelAuthoring.Current;
            if (!layout || !layout.decorationRoot || !layout.ground) { log.Add("Arcade dressing skipped (no layout/decoration root)."); return; }
            var ground = layout.ground.GetComponent<Collider>();
            var material = AssetDatabase.LoadAssetAtPath<Material>(ArcadeVisualPalette.SharedPalettePath);
            if (!ground || !material) { log.Add("Arcade dressing skipped (no ground collider or palette material)."); return; }
            var data = ArcadeVisualGround.LoadLayout();
            var lanes = ArcadeVisualGround.Lanes(data);
            var union = ArcadeVisualGround.RasterSurfaceDistance(lanes, data, true);
            var roads = ArcadeVisualGround.RasterSurfaceDistance(lanes, data, false);

            var root = layout.decorationRoot.Find(RootName);
            if (recordingOnly)
            {
                if (!root) throw new InvalidOperationException("Saved Arcade dressing root is required for a non-mutating replay.");
            }
            else
            {
                if (root) foreach (Transform child in root.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                else { root = new GameObject(RootName).transform; root.SetParent(layout.decorationRoot, false); }
                root.gameObject.SetActive(true);
                root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
                GameObjectUtility.SetStaticEditorFlags(root.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }

            Physics.SyncTransforms();
            var keepOut = new List<(Vector3 p, float r)>();
            foreach (var w in RoadGunnerLevelAuthoring.InScene<RepairWorkshop>()) if (w) keepOut.Add((w.transform.position, 16f));
            foreach (var s in RoadGunnerLevelAuthoring.InScene<ArenaUpgradeStation>()) if (s) keepOut.Add((s.transform.position, 6f));
            foreach (var c in RoadGunnerLevelAuthoring.InScene<ArenaRewardCache>()) if (c) keepOut.Add((c.transform.position, 4f));
            if (session) keepOut.Add((session.startPosition, 5f));

            var corners = new[] { data.minW, data.maxW, new Vector2(data.minW.x, data.maxW.y), new Vector2(data.maxW.x, data.minW.y) }
                .Select(w => ExplorationArenaInstaller.World(w)).ToArray();
            float minX = corners.Min(p => p.x), maxX = corners.Max(p => p.x), minZ = corners.Min(p => p.z), maxZ = corners.Max(p => p.z);
            var rng = new System.Random(20261102);
            var chunks = new Dictionary<Vector2Int, Builder>();
            int placed = 0, blockedSphere = 0, blockedAbove = 0, candidates = 0;
            var zoneCounts = new Dictionary<Zone, int> { { Zone.Rim, 0 }, { Zone.Pocket, 0 }, { Zone.Open, 0 } };
            var regionCounts = new Dictionary<string, int>();
            for (float x = minX; x <= maxX; x += Spacing)
                for (float z = minZ; z <= maxZ; z += Spacing)
                {
                    float jx = (float)(rng.NextDouble() - .5) * Spacing * .9f, jz = (float)(rng.NextDouble() - .5) * Spacing * .9f;
                    double roll = rng.NextDouble(), pick = rng.NextDouble(); int seed = rng.Next();
                    var at = new Vector3(x + jx, 0, z + jz);
                    var w = ExplorationArenaInstaller.ToW(at);
                    if (w.x < data.minW.x || w.y < data.minW.y || w.x > data.maxW.x || w.y > data.maxW.y) continue;
                    float du = ArcadeVisualGround.SampleRaster(union, at), dl = ArcadeVisualGround.SampleRaster(roads, at);
                    if (dl < 1.8f) continue; // never on (or hugging) the travelled lanes
                    Zone zone = du < -2.5f ? Zone.Pocket : du < 6f ? Zone.Rim : Zone.Open;
                    float cluster = Mathf.PerlinNoise(at.x * .055f + 11, at.z * .055f + 3);
                    float density = zone == Zone.Rim ? .42f * Mathf.Lerp(.6f, 1.35f, cluster)
                        : zone == Zone.Pocket ? .2f
                        : (du < 28 ? .2f : .08f) * Mathf.Lerp(.25f, 1.9f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .72f, cluster)));
                    if (roll > density) continue;
                    candidates++;
                    if (keepOut.Any(k => (new Vector2(at.x - k.p.x, at.z - k.p.z)).sqrMagnitude < k.r * k.r)) continue;
                    if (!ground.Raycast(new Ray(new Vector3(at.x, 60, at.z), Vector3.down), out var hit, 140)) continue;
                    // Solid props, cliffs, buildings and actors keep a clear margin; ground and road meshes are fine.
                    if (Physics.OverlapSphere(hit.point + Vector3.up * 1.35f, 1f, ~0, QueryTriggerInteraction.Ignore).Length > 0) { blockedSphere++; continue; }
                    if (Physics.Raycast(new Vector3(at.x, 40, at.z), Vector3.down, out var top, 80, ~0, QueryTriggerInteraction.Ignore) && top.point.y > hit.point.y + .3f) { blockedAbove++; continue; }

                    string region = NearestRegion(data, w);
                    var key = new Vector2Int(Mathf.FloorToInt(at.x / ChunkSize), Mathf.FloorToInt(at.z / ChunkSize));
                    if (!chunks.TryGetValue(key, out var builder)) chunks.Add(key, builder = new Builder());
                    builder.parentId = "arcade/" + key.x + "," + key.y + "/" + builder.items;
                    int start = builder.triangles.Count;
                    Item(builder, region, zone, du, dl, (float)pick, hit.point, new System.Random(seed));
                    builder.Record(builder.category, start, "", true);
                    builder.items++; placed++; zoneCounts[zone]++;
                    regionCounts[region] = regionCounts.TryGetValue(region, out int rc) ? rc + 1 : 1;
                }

            if (recordingOnly)
            {
                recorded = new ReplayResult { primaryItems = placed, chunks = chunks.OrderBy(p => p.Key.x).ThenBy(p => p.Key.y).Select(p => new ReplayChunk {
                    key = p.Key, vertices = p.Value.vertices.ToArray(), normals = p.Value.normals.ToArray(), uvs = p.Value.uvs.ToArray(),
                    triangles = p.Value.triangles.ToArray(), items = p.Value.records.ToArray() }).ToArray() };
                return;
            }

            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(ArcadeVisualInstaller.Root, "Meshes");
            var used = new HashSet<string>();
            var toLocal = root.worldToLocalMatrix;
            foreach (var pair in chunks.OrderBy(p => p.Key.x).ThenBy(p => p.Key.y))
            {
                var b = pair.Value;
                string name = MeshPrefix + pair.Key.x + "_" + pair.Key.y;
                var mesh = new Mesh { name = name, indexFormat = b.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(b.vertices.Select(v => toLocal.MultiplyPoint3x4(v)).ToList());
                mesh.SetNormals(b.normals.Select(n => toLocal.MultiplyVector(n).normalized).ToList());
                mesh.SetUVs(0, b.uvs); mesh.SetTriangles(b.triangles, 0); mesh.RecalculateBounds();
                string path = MeshFolder + "/" + name + ".asset"; used.Add(path);
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
                else { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                var go = new GameObject("Dressing chunk " + pair.Key.x + "," + pair.Key.y + " (" + b.items + ")");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = saved;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(path).StartsWith(MeshPrefix) && !used.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            EditorUtility.SetDirty(root.gameObject);
            log.Add("Arcade dressing: " + placed + " collider-free items (rim " + zoneCounts[Zone.Rim] + ", pocket " + zoneCounts[Zone.Pocket] + ", open " + zoneCounts[Zone.Open] + "; " +
                string.Join(", ", regionCounts.OrderBy(r => r.Key).Select(r => r.Key + " " + r.Value)) + ") in " + chunks.Count + " static combined chunks (" +
                chunks.Values.Sum(c => c.vertices.Count) + " verts); candidates " + candidates + ", blocked by solids " + blockedSphere + " / overhead " + blockedAbove + ".");
        }

        static string NearestRegion(ExplorationArenaLayoutData data, Vector2 w)
        {
            string best = ""; float bestDistance = float.PositiveInfinity;
            foreach (var region in data.regions)
            {
                if (region == null) continue;
                float d = Vector2.Distance(w, region.centerW);
                if (d < bestDistance) { bestDistance = d; best = region.id; }
            }
            return best;
        }

        // Region + zone themed pick. Big items (dunes, spires, hulls, girders) stay away from the lanes and pocket floors.
        static void Item(Builder b, string region, Zone zone, float du, float dl, float pick, Vector3 at, System.Random rng)
        {
            float yaw = (float)rng.NextDouble() * 360f;
            bool roomy = zone != Zone.Pocket && du > 2.5f && dl > 4f;
            switch (region)
            {
                case "salvage-yard":
                    if (zone == Zone.Pocket) { if (pick < .35f) Pick(b, "Plate", () => Plate(b, at, yaw, rng)); else if (pick < .7f) Pick(b, "Tuft", () => Tuft(b, at, rng, DryLeavesCell, 1.1f)); else if (pick < .85f) Pick(b, "Tyre", () => Tyre(b, at, yaw, rng, false)); else Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockCell, RockLightCell)); }
                    else if (pick < .24f) Pick(b, "ScrapPile", () => ScrapPile(b, at, yaw, rng));
                    else if (pick < .46f) { if (rng.NextDouble() < .55) Pick(b, "TyreStack", () => TyreStack(b, at, yaw, rng)); else Pick(b, "Tyre", () => Tyre(b, at, yaw, rng, true)); }
                    else if (pick < .72f) Pick(b, "Tuft", () => Tuft(b, at, rng, DryLeavesCell, 1.25f));
                    else if (pick < .86f && roomy) Pick(b, "Hull", () => Hull(b, at, yaw, rng));
                    else Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockCell, RockLightCell));
                    break;
                case "rustworks":
                    if (zone == Zone.Pocket) { if (pick < .4f) Pick(b, "Plate", () => Plate(b, at, yaw, rng)); else if (pick < .8f) Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockShadeCell, RockCell)); else Pick(b, "Pipe", () => Pipe(b, at, yaw, rng, .7f)); }
                    else if (pick < .24f) Pick(b, "Barrels", () => Barrels(b, at, yaw, rng));
                    else if (pick < .44f) Pick(b, "Pipe", () => Pipe(b, at, yaw, rng, 1f));
                    else if (pick < .6f && roomy) Pick(b, "Girder", () => Girder(b, at, yaw, rng));
                    else if (pick < .78f) Pick(b, "Plate", () => Plate(b, at, yaw, rng));
                    else Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockShadeCell, RockCell));
                    break;
                case "ash-ridge":
                    if (zone == Zone.Pocket) { if (pick < .5f) Pick(b, "Pebbles", () => Pebbles(b, at, rng, AshStoneCell, IvoryShadeCell)); else if (pick < .8f) Pick(b, "Drift", () => Drift(b, at, yaw, rng, AshStoneCell, .8f)); else Pick(b, "Bones", () => Bones(b, at, yaw, rng)); }
                    else if (pick < .28f && roomy) Pick(b, "Spire", () => Spire(b, at, yaw, rng));
                    else if (pick < .48f) Pick(b, "DeadTree", () => DeadTree(b, at, yaw, rng));
                    else if (pick < .68f) Pick(b, "Drift", () => Drift(b, at, yaw, rng, AshStoneCell, 1.2f));
                    else if (pick < .9f) Pick(b, "Pebbles", () => Pebbles(b, at, rng, AshStoneCell, IvoryShadeCell));
                    else Pick(b, "Tuft", () => Tuft(b, at, rng, DryLeavesCell, 1f));
                    break;
                default:
                    if (zone == Zone.Pocket) { if (pick < .5f) Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockLightCell, RockCell)); else if (pick < .8f) Pick(b, "Tuft", () => Tuft(b, at, rng, DryLeavesCell, 1f)); else Pick(b, "Bones", () => Bones(b, at, yaw, rng)); }
                    else if (pick < .2f && roomy) Pick(b, "Dune", () => Dune(b, at, yaw, rng));
                    else if (pick < .42f) Pick(b, "Cactus", () => Cactus(b, at, yaw, rng));
                    else if (pick < .66f) Pick(b, "Tuft", () => Tuft(b, at, rng, rng.NextDouble() < .3 ? CactusCell : DryLeavesCell, 1.2f));
                    else if (pick < .9f) Pick(b, "Pebbles", () => Pebbles(b, at, rng, RockLightCell, RockCell));
                    else Pick(b, "Bones", () => Bones(b, at, yaw, rng));
                    break;
            }
        }

        static void Pick(Builder builder, string category, Action draw) { builder.category = category; draw(); }

        static float R(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static readonly Vector3[] Octa = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly int[] OctaFaces = { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0, 3 };

        // Icosahedron for readable rocks, octahedron for small chips (vertex budget).
        static void Rock(Builder b, Vector3 center, Vector3 size, float yaw, System.Random rng, int cell)
        {
            var rotation = Quaternion.Euler(R(rng, -8, 8), yaw, R(rng, -8, 8));
            bool small = size.x < .36f;
            var shape = small ? Octa : Ico; var faces = small ? OctaFaces : IcoFaces;
            var points = shape.Select(v => center + rotation * Vector3.Scale(v * R(rng, .82f, 1.12f), size * .5f)).ToArray();
            for (int i = 0; i < faces.Length; i += 3) b.Tri(points[faces[i]], points[faces[i + 1]], points[faces[i + 2]], center, cell);
        }

        static void Pebbles(Builder b, Vector3 at, System.Random rng, int mainCell, int altCell)
        {
            int count = rng.Next(3, 6);
            for (int i = 0; i < count; i++)
            {
                float width = i == 0 ? R(rng, .45f, .72f) : R(rng, .18f, .38f);
                var size = new Vector3(width, width * R(rng, .5f, .75f), width * R(rng, .7f, 1f));
                var offset = i == 0 ? Vector3.zero : Quaternion.Euler(0, R(rng, 0, 360), 0) * Vector3.forward * R(rng, .4f, .95f);
                Rock(b, at + offset + Vector3.up * size.y * .18f, size, R(rng, 0, 360), rng, i % 2 == 0 ? mainCell : altCell);
            }
        }

        static void Prism(Builder b, Vector3 start, Vector3 end, float r0, float r1, int sides, int cell, bool capStart, bool capEnd, float twist = 0)
        {
            var axis = (end - start).normalized;
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            var a = new Vector3[sides]; var c = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides + twist;
                var dir = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                a[i] = start + dir * r0; c[i] = end + dir * r1;
            }
            var mid = (start + end) * .5f;
            for (int i = 0; i < sides; i++) { int j = (i + 1) % sides; b.Quad(a[i], a[j], c[j], c[i], mid, cell); }
            for (int i = 1; i < sides - 1; i++)
            {
                if (capStart) b.Tri(a[0], a[i], a[i + 1], mid, cell);
                if (capEnd && r1 > 1e-4f) b.Tri(c[0], c[i], c[i + 1], mid, cell);
            }
        }

        static void Cactus(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            float r = R(rng, .13f, .19f), h = R(rng, .65f, 1.25f);
            var top = at + Vector3.up * h;
            Prism(b, at - Vector3.up * .05f, top, r, r, 8, CactusCell, false, false);
            Prism(b, top, top + Vector3.up * r * .45f, r, r * .62f, 8, CactusCell, false, true);
            int arms = h > .85f ? rng.Next(1, 3) : rng.Next(0, 2);
            for (int i = 0; i < arms; i++)
            {
                var side = Quaternion.Euler(0, yaw + i * 180 + R(rng, -25, 25), 0) * Vector3.right;
                float ar = r * .62f, y = h * R(rng, .38f, .55f), reach = r + ar * 1.4f, rise = R(rng, .22f, .4f);
                var elbow = at + Vector3.up * y + side * reach;
                Prism(b, at + Vector3.up * y, elbow, ar * .9f, ar, 6, CactusCell, false, false);
                Prism(b, elbow - Vector3.up * ar * .5f, elbow + Vector3.up * rise, ar, ar, 6, CactusCell, true, false);
                Prism(b, elbow + Vector3.up * rise, elbow + Vector3.up * (rise + ar * .45f), ar, ar * .55f, 6, CactusCell, false, true);
            }
            if (rng.NextDouble() < .4) Rock(b, top + Vector3.up * r * .5f, Vector3.one * r * .75f, yaw, rng, rng.NextDouble() < .5 ? RedCell : YellowCell);
            if (rng.NextDouble() < .5) Rock(b, at + Quaternion.Euler(0, yaw, 0) * Vector3.forward * (r + .2f), new Vector3(.26f, .16f, .22f), yaw, rng, RockLightCell);
        }

        static void Tuft(Builder b, Vector3 at, System.Random rng, int cell, float scale)
        {
            int blades = rng.Next(7, 11);
            for (int i = 0; i < blades; i++)
            {
                var offset = Quaternion.Euler(0, R(rng, 0, 360), 0) * Vector3.forward * R(rng, 0, .22f) * scale;
                var baseCenter = at + offset;
                float height = R(rng, .32f, .58f) * scale, width = R(rng, .035f, .055f) * scale;
                var outward = offset.sqrMagnitude > 1e-4f ? offset.normalized : Vector3.forward;
                var tip = baseCenter + Vector3.up * height + outward * height * R(rng, .2f, .5f);
                float spin = R(rng, 0, 120);
                var p0 = baseCenter + Quaternion.Euler(0, spin, 0) * Vector3.forward * width;
                var p1 = baseCenter + Quaternion.Euler(0, spin + 120, 0) * Vector3.forward * width;
                var p2 = baseCenter + Quaternion.Euler(0, spin + 240, 0) * Vector3.forward * width;
                var inside = (p0 + p1 + p2 + tip) * .25f;
                int c = i % 4 == 0 && cell == DryLeavesCell ? CactusCell : cell;
                b.Tri(p0, p1, tip, inside, c); b.Tri(p1, p2, tip, inside, c); b.Tri(p2, p0, tip, inside, c);
            }
        }

        // Half-ellipsoid mound (flat-shaded faces catch the toon terminator), optionally ridged.
        static void Mound(Builder b, Vector3 at, float yaw, Vector3 size, int cell, int segments = 9, int rings = 3)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            Vector3 P(int ring, int seg)
            {
                float phi = ring / (float)rings * Mathf.PI * .5f, theta = seg / (float)segments * Mathf.PI * 2;
                var local = new Vector3(Mathf.Cos(theta) * Mathf.Cos(phi) * size.x * .5f, Mathf.Sin(phi) * size.y, Mathf.Sin(theta) * Mathf.Cos(phi) * size.z * .5f);
                return at + rotation * local - Vector3.up * .04f;
            }
            var inside = at;
            for (int ring = 0; ring < rings; ring++)
                for (int seg = 0; seg < segments; seg++)
                {
                    if (ring == rings - 1) b.Tri(P(ring, seg), P(ring, seg + 1), P(rings, 0), inside, cell);
                    else b.Quad(P(ring, seg), P(ring, seg + 1), P(ring + 1, seg + 1), P(ring + 1, seg), inside, cell);
                }
        }

        static void Dune(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            var size = new Vector3(R(rng, 3.2f, 5.2f), R(rng, .32f, .52f), R(rng, 1.6f, 2.4f));
            Mound(b, at, 25 + R(rng, -20, 20), size, SandLightCell, 10, 3);
            if (rng.NextDouble() < .5) Mound(b, at + Quaternion.Euler(0, yaw, 0) * Vector3.forward * size.x * .45f, yaw, size * .55f, SandLightCell, 8, 2);
            if (rng.NextDouble() < .6) Tuft(b, at + Quaternion.Euler(0, yaw, 0) * Vector3.right * size.x * .4f, rng, DryLeavesCell, 1f);
        }

        static void Drift(Builder b, Vector3 at, float yaw, System.Random rng, int cell, float scale)
        {
            var size = new Vector3(R(rng, 1.8f, 3.2f), R(rng, .16f, .28f), R(rng, 1f, 1.7f)) * scale;
            Mound(b, at, yaw, size, cell, 8, 2);
            if (rng.NextDouble() < .5) Rock(b, at + Vector3.up * size.y * .6f, new Vector3(.3f, .2f, .25f), yaw, rng, IvoryShadeCell);
        }

        static void Spire(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            // Chunky leaning lilac rock spires (1-3 per cluster) with a pale strata band and base rubble.
            int count = rng.Next(1, 4);
            for (int k = 0; k < count; k++)
            {
                float h = R(rng, 1.6f, 3.0f) * (k == 0 ? 1 : .65f), r = R(rng, .55f, .85f) * (k == 0 ? 1 : .7f);
                var basePoint = at + (k == 0 ? Vector3.zero : Quaternion.Euler(0, yaw + k * 140, 0) * Vector3.forward * R(rng, .9f, 1.4f));
                var lean = Quaternion.Euler(R(rng, -9, 9), yaw + k * 50, R(rng, -9, 9));
                float twist = (yaw + k * 31) * Mathf.Deg2Rad;
                var mid = basePoint + lean * Vector3.up * h * .5f;
                Prism(b, basePoint - Vector3.up * .1f, mid, r, r * .72f, 6, AshStoneCell, false, false, twist);
                Prism(b, mid, mid + lean * Vector3.up * h * .08f, r * .76f, r * .7f, 6, IvoryShadeCell, false, false, twist);
                Prism(b, mid + lean * Vector3.up * h * .08f, basePoint + lean * Vector3.up * h, r * .7f, r * .3f, 6, AshStoneCell, false, true, twist + .2f);
            }
            for (int i = 0; i < 3; i++)
                Rock(b, at + Quaternion.Euler(0, yaw + i * 120 + R(rng, -30, 30), 0) * Vector3.forward * R(rng, .8f, 1.2f), new Vector3(.42f, .3f, .38f) * R(rng, .8f, 1.3f), yaw, rng, AshStoneCell);
        }

        static void DeadTree(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            float h = R(rng, 1.7f, 2.6f), r = R(rng, .11f, .15f);
            var lean = Quaternion.Euler(R(rng, -8, 8), yaw, R(rng, -8, 8));
            var top = at + lean * Vector3.up * h;
            Prism(b, at - Vector3.up * .05f, top, r, r * .4f, 6, IvoryCell, false, true);
            int branches = rng.Next(3, 6);
            for (int i = 0; i < branches; i++)
            {
                float t = R(rng, .35f, .85f);
                var start = at + lean * Vector3.up * h * t;
                var dir = Quaternion.Euler(0, yaw + i * (360f / branches) + R(rng, -25, 25), 0) * new Vector3(1, R(rng, .6f, 1.1f), 0).normalized;
                float length = R(rng, .55f, 1f) * (1.25f - t);
                var end = start + dir * length;
                Prism(b, start, end, r * .6f, r * .22f, 5, IvoryCell, false, true);
                if (rng.NextDouble() < .6) Prism(b, end - dir * length * .35f, end - dir * length * .35f + (Quaternion.Euler(0, 55, 0) * dir + Vector3.up * .4f).normalized * length * .55f, r * .32f, r * .12f, 4, IvoryCell, false, true);
            }
            Rock(b, at + Vector3.up * .04f, new Vector3(.55f, .16f, .5f), yaw, rng, AshStoneCell);
        }

        static void Tyre(Builder b, Vector3 at, float yaw, System.Random rng, bool allowLean)
        {
            float major = R(rng, .38f, .48f), minor = R(rng, .12f, .16f);
            bool leaning = allowLean && rng.NextDouble() < .45;
            Torus(b, at + Vector3.up * (leaning ? major * .55f : minor * .55f), Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(leaning ? R(rng, 25, 65) : R(rng, -6, 6), 0, 0), major, minor, RubberCell);
        }

        static void Torus(Builder b, Vector3 center, Quaternion rotation, float major, float minor, int cell)
        {
            const int segments = 8, cross = 4;
            Vector3 P(int s, int c)
            {
                float a = s * Mathf.PI * 2 / segments, t = c * Mathf.PI * 2 / cross;
                var ring = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                return center + rotation * (ring * (major + minor * Mathf.Cos(t)) + Vector3.up * minor * Mathf.Sin(t));
            }
            for (int s = 0; s < segments; s++) for (int c = 0; c < cross; c++)
            {
                float a = (s + .5f) * Mathf.PI * 2 / segments;
                var tube = center + rotation * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * major;
                b.Quad(P(s, c), P(s + 1, c), P(s + 1, c + 1), P(s, c + 1), tube, cell);
            }
        }

        static void TyreStack(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            int count = rng.Next(2, 4); float major = R(rng, .4f, .47f), minor = .14f;
            for (int i = 0; i < count; i++)
                Torus(b, at + Vector3.up * (minor * .9f + i * minor * 1.9f) + new Vector3(R(rng, -.06f, .06f), 0, R(rng, -.06f, .06f)), Quaternion.Euler(R(rng, -4, 4), yaw + i * 17, R(rng, -4, 4)), major, minor, i == count - 1 && rng.NextDouble() < .4 ? RubberEdgeCell : RubberCell);
        }

        static void Bones(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            int count = rng.Next(1, 3);
            for (int i = 0; i < count; i++)
            {
                var dir = Quaternion.Euler(0, yaw + i * R(rng, 40, 120), 0) * Vector3.forward;
                float length = R(rng, .5f, .75f);
                var a = at + Vector3.up * .05f - dir * length * .5f + Vector3.right * i * .25f;
                var c = a + dir * length;
                Prism(b, a, c, .045f, .045f, 6, IvoryCell, false, false);
                Rock(b, a, Vector3.one * .14f, yaw, rng, IvoryCell);
                Rock(b, c, Vector3.one * .14f, yaw, rng, IvoryCell);
            }
            if (rng.NextDouble() < .35) Rock(b, at + Quaternion.Euler(0, yaw + 90, 0) * Vector3.forward * .45f + Vector3.up * .12f, new Vector3(.3f, .24f, .34f), yaw, rng, IvoryCell); // skull
        }

        static void Box(Builder b, Vector3 center, Vector3 size, Quaternion rotation, int cell)
        {
            var h = size * .5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            b.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), center, cell);
            b.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), center, cell);
            b.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), center, cell);
            b.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), center, cell);
            b.Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), center, cell);
        }

        static void Plate(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            var plate = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(R(rng, 6, 24), 0, R(rng, -12, 12));
            Box(b, at + Vector3.up * .1f, new Vector3(R(rng, .6f, .95f), .05f, R(rng, .4f, .6f)), plate, rng.NextDouble() < .6 ? RustCell : RustDarkCell);
            if (rng.NextDouble() < .55)
            {
                var dir = Quaternion.Euler(0, yaw + R(rng, 50, 130), 0) * Vector3.forward;
                var start = at + Vector3.up * .06f + dir * .25f;
                Prism(b, start, start + dir * R(rng, .5f, .85f), .055f, .055f, 6, SteelLightCell, true, true);
            }
        }

        static void ScrapPile(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            int[] cells = { RustCell, RustDarkCell, SteelLightCell, RustCell, TealDarkCell, YellowCell };
            int count = rng.Next(3, 6);
            for (int i = 0; i < count; i++)
            {
                var size = new Vector3(R(rng, .45f, .9f), R(rng, .12f, .3f), R(rng, .35f, .7f));
                var offset = Quaternion.Euler(0, R(rng, 0, 360), 0) * Vector3.forward * R(rng, 0, .45f);
                var rotation = Quaternion.Euler(R(rng, -25, 25), yaw + R(rng, -60, 60), R(rng, -25, 25));
                Box(b, at + offset + Vector3.up * (size.y * .4f + i * .12f), size, rotation, cells[rng.Next(cells.Length)]);
            }
            Prism(b, at + Vector3.up * .3f, at + Quaternion.Euler(0, yaw, 0) * new Vector3(.7f, .55f, .1f), .05f, .05f, 6, SteelCell, true, true);
        }

        static void Hull(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            // Half-buried car shell: tilted body, cabin, wheel arch hole hint.
            int paint = new[] { RustCell, TealDarkCell, RustDarkCell, YellowCell }[rng.Next(4)];
            var rotation = Quaternion.Euler(R(rng, -6, 6), yaw, R(rng, 8, 18));
            Box(b, at + Vector3.up * .25f, new Vector3(1.9f, .55f, 1.05f), rotation, paint);
            Box(b, at + rotation * new Vector3(-.15f, .68f, 0), new Vector3(.95f, .38f, .9f), rotation, paint == YellowCell ? RustCell : paint);
            Box(b, at + rotation * new Vector3(.25f, .7f, 0), new Vector3(.08f, .3f, .82f), rotation * Quaternion.Euler(0, 0, -25), SteelCell);
            Torus(b, at + rotation * new Vector3(.62f, .2f, .56f), rotation * Quaternion.Euler(90, 0, 0), .2f, .08f, RubberCell);
            Pebbles(b, at + rotation * new Vector3(-1.15f, 0, .3f), rng, SandShadeCell, RockCell);
        }

        static void Barrels(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            int count = rng.Next(1, 4);
            for (int i = 0; i < count; i++)
            {
                int firstIndex = b.triangles.Count;
                int cell = new[] { RustCell, YellowCell, RustDarkCell, TealCell }[rng.Next(4)];
                var offset = Quaternion.Euler(0, yaw + i * 120, 0) * Vector3.forward * (i == 0 ? 0 : .5f);
                bool tipped = rng.NextDouble() < .3;
                float r = .26f, h = .82f;
                Vector3 start, end;
                if (tipped) { var dir = Quaternion.Euler(0, yaw + i * 70, 0) * Vector3.forward; start = at + offset + Vector3.up * r - dir * h * .5f; end = start + dir * h; }
                else { start = at + offset; end = start + Vector3.up * h; }
                Prism(b, start, end, r, r, 8, cell, true, true);
                var axis = (end - start).normalized;
                Prism(b, start + axis * h * .3f, start + axis * h * .36f, r * 1.04f, r * 1.04f, 8, RustDarkCell, false, false);
                Prism(b, start + axis * h * .64f, start + axis * h * .7f, r * 1.04f, r * 1.04f, 8, RustDarkCell, false, false);
                b.Record("Barrel", firstIndex, "/barrel/" + i, false);
            }
        }

        static void Pipe(Builder b, Vector3 at, float yaw, System.Random rng, float scale)
        {
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            float length = R(rng, 1.4f, 2.6f) * scale, r = R(rng, .14f, .22f);
            var start = at + Vector3.up * r * .8f - dir * length * .5f;
            Prism(b, start, start + dir * length, r, r, 8, rng.NextDouble() < .6 ? RustCell : SteelLightCell, true, true);
            Prism(b, start - dir * .06f, start + dir * .1f, r * 1.25f, r * 1.25f, 8, RustDarkCell, true, true);
            if (rng.NextDouble() < .5) Prism(b, start + dir * length * .6f, start + dir * length * .6f + Quaternion.Euler(0, 90, 0) * dir * .2f + Vector3.up * .5f, r * .6f, r * .6f, 6, RustDarkCell, false, true);
        }

        static void Girder(Builder b, Vector3 at, float yaw, System.Random rng)
        {
            float length = R(rng, 2f, 3.2f);
            var rotation = Quaternion.Euler(R(rng, -4, 10), yaw, R(rng, -6, 6));
            int cell = rng.NextDouble() < .6 ? RustCell : YellowCell;
            Box(b, at + rotation * new Vector3(0, .05f, 0), new Vector3(.36f, .06f, length), rotation, cell);
            Box(b, at + rotation * new Vector3(0, .2f, 0), new Vector3(.06f, .26f, length), rotation, cell);
            Box(b, at + rotation * new Vector3(0, .35f, 0), new Vector3(.36f, .06f, length), rotation, cell);
            if (rng.NextDouble() < .5) Pebbles(b, at + rotation * new Vector3(.5f, 0, length * .3f), rng, RockShadeCell, RockCell);
        }
    }
}
