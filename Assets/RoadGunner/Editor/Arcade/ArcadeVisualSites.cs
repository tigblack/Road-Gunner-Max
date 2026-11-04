using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// RENDER pass on the gameplay sites (visual only; every collider, name and gameplay reference is kept):
    /// Workshop: the baked workshop mesh is re-derived from its legacy source into an arcade copy without the old
    /// apron/footing/joint slabs that z-fought with the packed-road mesh, with the cow-print rust blooms painted out
    /// (teal cladding, cream end walls, navy uprights, yellow knee braces, rust roofs with cream seams and teal trim) and
    /// the high side canopies split into a no-shadow renderer (no ghost slabs on the floor), plus a 3D rooftop wrench-and-gear
    /// landmark totem and glowing service beacons. A new opaque concrete floor slab (tile grid, mint repair lane with chevrons,
    /// hazard corner brackets, wrench badge) sits just above the measured road/terrain height.
    /// Upgrade stations: navy pedestal, gold rim, corner pylons, 3D attack/health totem crest, and emissive inner disc.
    /// Reward caches: armored wasteland loot chest trim with gold corner bumpers and glowing energy core bars.
    /// </summary>
    public static class ArcadeVisualSites
    {
        const string WorkshopSourcePath = "Assets/RoadGunner/Art/ConceptWasteland/Meshes/Env_AutoRepairWorkshop_Palette.asset";
        const string ShellPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade workshop shell.asset";
        const string CanopyPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade workshop canopy.asset";
        const string RooftopPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade workshop rooftop.asset";
        const string FloorMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade workshop floor.asset";
        const string FloorTexturePath = ArcadeVisualInstaller.Root + "/Textures/Arcade workshop floor.png";
        const string FloorMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade workshop floor.mat";
        const string RimMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade station rim.asset";
        const string DiscMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade station disc.asset";
        const string AttackTotemMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade station attack totem.asset";
        const string HealthTotemMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade station health totem.asset";
        const string CacheTrimMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade cache trim.asset";
        const string CacheGlowMeshPath = ArcadeVisualInstaller.Root + "/Meshes/Arcade cache glow.asset";
        const string GoldMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade station gold.mat";
        const string GlowGoldMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade station glow gold.mat";
        const string GlowMintMaterialPath = ArcadeVisualInstaller.Root + "/Materials/Arcade station glow mint.mat";
        public const string FloorName = "Arcade workshop floor";
        public const string CanopyName = "Arcade workshop canopy (no shadows)";
        public const string RooftopName = "Arcade workshop rooftop landmarks";
        const float FloorX = 23.4f, FloorZ = 13.4f, FloorDepth = .14f;

        public static void Apply(GameSession session, Shader toon, List<string> log)
        {
            foreach (var workshop in RoadGunnerLevelAuthoring.InScene<RepairWorkshop>().Where(w => w))
                log.Add(Workshop(workshop, toon));
            int stations = 0;
            foreach (var station in RoadGunnerLevelAuthoring.InScene<ArenaUpgradeStation>().Where(s => s)) { Station(station, toon); stations++; }
            log.Add("Upgrade stations: " + stations + " restyled (navy pedestal, gold rim, 3D totem crest, emissive gold/mint inner disc).");
            int caches = 0;
            foreach (var cache in RoadGunnerLevelAuthoring.InScene<ArenaRewardCache>().Where(c => c)) { Cache(cache, toon); caches++; }
            log.Add("Reward caches: " + caches + " restyled with armored chest trim and glowing core bars.");
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); return saved; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        static Material ToonMaterial(string path, Shader toon, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(toon) { name = name }; AssetDatabase.CreateAsset(m, path); }
            if (m.shader != toon) m.shader = toon;
            return m;
        }

        static void Configure(Material m, Color baseColor, Texture texture, float metallic, float smoothness, Color emission)
        {
            m.SetColor("_BaseColor", baseColor); m.SetTexture("_BaseMap", texture);
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_MetallicGlossMap", null); m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", emission);
            m.enableInstancing = true;
            ArcadeVisualPalette.ApplyToonLook(m);
        }

        static GameObject Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            return t.gameObject;
        }

        static void Visual(GameObject go, Mesh mesh, Material material, ShadowCastingMode shadows)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (!filter) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            if (!renderer) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = shadows; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            foreach (var collider in go.GetComponents<Collider>()) Object.DestroyImmediate(collider);
            EditorUtility.SetDirty(go);
        }

        // ---------- small procedural mesh builder for site landmarks ----------

        sealed class SiteMeshBuilder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 cellUv)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(cellUv); uv.Add(cellUv); uv.Add(cellUv); uv.Add(cellUv);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Box(Vector3 center, Vector3 size, Quaternion rot, string cellName = null)
            {
                Vector2 u = string.IsNullOrEmpty(cellName) ? Vector2.zero : ArcadeVisualPalette.CellUv(ArcadeVisualPalette.CellOf(cellName));
                Vector3 h = size * .5f;
                Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
                Vector3 N(Vector3 dir) => (rot * dir).normalized;
                Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), N(Vector3.up), u);
                Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), N(Vector3.down), u);
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), N(Vector3.forward), u);
                Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), N(Vector3.back), u);
                Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), N(Vector3.right), u);
                Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), N(Vector3.left), u);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------- workshop ----------

        static string Workshop(RepairWorkshop workshop, Shader toon)
        {
            var root = workshop.transform;
            var visuals = root.Find("Concept environment visuals");
            var baked = visuals ? visuals.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh && f.sharedMesh.vertexCount > 20000) : null;
            if (!baked) baked = visuals ? visuals.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "Finished shared visuals") : null;
            if (!baked) return "Workshop '" + root.name + "': baked workshop visuals not found, skipped.";
            var current = baked.sharedMesh;
            string currentPath = current ? AssetDatabase.GetAssetPath(current) : "";
            var source = current && !currentPath.StartsWith(ArcadeVisualInstaller.Root) ? current : AssetDatabase.LoadAssetAtPath<Mesh>(WorkshopSourcePath);
            if (!source)
            {
                string guid = AssetDatabase.FindAssets("Env_AutoRepairWorkshop_Palette t:Mesh").FirstOrDefault();
                if (guid != null) source = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (!source || !source.isReadable) return "Workshop '" + root.name + "': legacy source mesh missing or unreadable, skipped.";

            float floorTop = MeasureFloor(workshop);
            var toRoot = root.worldToLocalMatrix * baked.transform.localToWorldMatrix;
            var (shell, canopy, stats) = Derive(source, toRoot);
            shell = SaveMesh(shell, ShellPath); canopy = SaveMesh(canopy, CanopyPath);
            baked.sharedMesh = shell; EditorUtility.SetDirty(baked);
            var bakedRenderer = baked.GetComponent<MeshRenderer>();
            var palette = bakedRenderer ? bakedRenderer.sharedMaterial : AssetDatabase.LoadAssetAtPath<Material>(ArcadeVisualPalette.SharedPalettePath);
            if (bakedRenderer) { bakedRenderer.shadowCastingMode = ShadowCastingMode.On; EditorUtility.SetDirty(bakedRenderer); }
            var canopyGo = Child(baked.transform, CanopyName);
            canopyGo.transform.localPosition = Vector3.zero; canopyGo.transform.localRotation = Quaternion.identity; canopyGo.transform.localScale = Vector3.one;
            Visual(canopyGo, canopy, palette, ShadowCastingMode.Off);

            // 3D Rooftop Landmarks (crossed wrench & gear totems + glowing mint service beacons + awning headers)
            var rooftopGo = Child(canopyGo.transform, RooftopName);
            rooftopGo.transform.localPosition = Vector3.zero; rooftopGo.transform.localRotation = Quaternion.identity; rooftopGo.transform.localScale = Vector3.one;
            Visual(rooftopGo, SaveMesh(WorkshopRooftopMesh(), RooftopPath), palette, ShadowCastingMode.Off);

            // Opaque concrete floor slab (queue 2000: never washes over world labels or VFX text).
            var texture = WriteFloorTexture();
            var floorMaterial = ToonMaterial(FloorMaterialPath, toon, "Arcade workshop floor");
            Configure(floorMaterial, Color.white, texture, 0, .18f, Color.black);
            var floorGo = Child(root, FloorName);
            floorGo.transform.localPosition = new Vector3(0, floorTop, 0); floorGo.transform.localRotation = Quaternion.identity; floorGo.transform.localScale = Vector3.one;
            Visual(floorGo, SaveMesh(FloorMesh(), FloorMeshPath), floorMaterial, ShadowCastingMode.Off);

            // Keep the mint dashed service ring and the repair pulses just above the new floor; the painted wrench
            // is now part of the floor texture.
            if (workshop.radiusMarking)
            {
                var p = workshop.radiusMarking.localPosition; p.y = Mathf.Max(0, floorTop + .014f - .058f);
                workshop.radiusMarking.localPosition = p; EditorUtility.SetDirty(workshop.radiusMarking);
            }
            if (workshop.activeRepairEffect)
            {
                var t = workshop.activeRepairEffect.transform; var p = t.localPosition; p.y = Mathf.Max(.075f, floorTop + .03f);
                t.localPosition = p; EditorUtility.SetDirty(t);
            }
            int hidden = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh && f.sharedMesh.name.StartsWith("Env_Repair_wrench"))
                {
                    var r = f.GetComponent<MeshRenderer>();
                    if (r) { if (r.enabled) { r.enabled = false; EditorUtility.SetDirty(r); } hidden++; }
                }
            return "Workshop '" + root.name + "': shell " + shell.vertexCount + " verts (" + stats + "), canopy " + canopy.vertexCount + " verts + rooftop landmarks without shadows; concrete floor top at y=" +
                floorTop.ToString("F3") + " (" + FloorX + "x" + FloorZ + " m); painted wrench renderers hidden " + hidden + ".";
        }

        static Mesh WorkshopRooftopMesh()
        {
            var b = new SiteMeshBuilder();
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 9.75f;
                // Rooftop totem pedestal + giant gear & crossed wrenches
                b.Box(new Vector3(x, 5.95f, 0), new Vector3(1.1f, .42f, 2.6f), Quaternion.identity, "Steel");
                b.Box(new Vector3(x, 6.22f, 0), new Vector3(1.22f, .14f, 2.75f), Quaternion.identity, "Yellow");
                // Giant gear ring & teeth
                b.Box(new Vector3(x, 7.15f, 0), new Vector3(.28f, 1.55f, 1.55f), Quaternion.identity, "Teal");
                b.Box(new Vector3(x, 7.15f, 0), new Vector3(.34f, 1.10f, 1.10f), Quaternion.Euler(45, 0, 0), "Yellow");
                b.Box(new Vector3(x, 7.15f, 0), new Vector3(.42f, .58f, .58f), Quaternion.identity, "Mint");
                // Crossed giant spanner shafts above the gear
                for (int k = -1; k <= 1; k += 2)
                {
                    b.Box(new Vector3(x, 7.35f, 0), new Vector3(.22f, 2.65f, .28f), Quaternion.Euler(k * 38f, 0, 0), "Ivory");
                    b.Box(new Vector3(x, 8.35f, k * .82f), new Vector3(.26f, .48f, .56f), Quaternion.Euler(k * 20f, 0, 0), "Yellow");
                }
                // 2 glowing Mint service beacons per canopy end
                for (int z = -1; z <= 1; z += 2)
                {
                    b.Box(new Vector3(x, 5.92f, z * 5.8f), new Vector3(.68f, .28f, .68f), Quaternion.identity, "Steel");
                    b.Box(new Vector3(x, 6.26f, z * 5.8f), new Vector3(.52f, .48f, .52f), Quaternion.identity, "Mint");
                    b.Box(new Vector3(x, 5.85f, z * 6.32f), new Vector3(3.2f, .52f, .14f), Quaternion.identity, "Yellow");
                    b.Box(new Vector3(x, 5.85f, z * 6.40f), new Vector3(2.6f, .26f, .08f), Quaternion.identity, "Mint");
                }
            }
            return b.Build("Arcade workshop rooftop");
        }

        static float MeasureFloor(RepairWorkshop workshop)
        {
            Physics.SyncTransforms();
            float top = float.NegativeInfinity;
            for (int i = 0; i <= 6; i++) for (int j = 0; j <= 4; j++)
            {
                var local = new Vector3((i / 6f - .5f) * FloorX, 6, (j / 4f - .5f) * FloorZ);
                var world = workshop.transform.TransformPoint(local);
                foreach (var hit in Physics.RaycastAll(world, Vector3.down, 12, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider is MeshCollider) top = Mathf.Max(top, workshop.transform.InverseTransformPoint(hit.point).y);
            }
            if (float.IsNegativeInfinity(top)) top = .04f;
            return Mathf.Clamp(top + .03f, .06f, .3f);
        }

        static (Mesh shell, Mesh canopy, string stats) Derive(Mesh source, Matrix4x4 toRoot)
        {
            var vertices = source.vertices; var normals = source.normals; var uvs = source.uv;
            var triangles = source.triangles;
            var local = vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
            var uses = new int[vertices.Length];
            foreach (int index in triangles) uses[index]++;
            int Cell(Vector2 uv) => Mathf.Clamp(Mathf.FloorToInt(uv.x * 8), 0, 7) + 8 * Mathf.Clamp(Mathf.FloorToInt(uv.y * 4), 0, 3);
            int ivory = ArcadeVisualPalette.CellOf("Ivory"), ivoryShade = ArcadeVisualPalette.CellOf("IvoryShade"), rust = ArcadeVisualPalette.CellOf("Rust"),
                rustDark = ArcadeVisualPalette.CellOf("RustDark"), teal = ArcadeVisualPalette.CellOf("Teal"), tealDark = ArcadeVisualPalette.CellOf("TealDark"),
                steel = ArcadeVisualPalette.CellOf("Steel"), yellow = ArcadeVisualPalette.CellOf("Yellow");
            var shellParts = (v: new List<Vector3>(), n: new List<Vector3>(), uv: new List<Vector2>(), t: new List<int>(), map: new Dictionary<int, int>());
            var canopyParts = (v: new List<Vector3>(), n: new List<Vector3>(), uv: new List<Vector2>(), t: new List<int>(), map: new Dictionary<int, int>());
            int dropped = 0, marks = 0, recoloured = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 pa = local[a], pb = local[b], pc = local[c];
                if (pa.y < .062f && pb.y < .062f && pc.y < .062f) { dropped++; continue; } // old apron/footings/joints: replaced by the floor slab
                var centre = (pa + pb + pc) / 3f;
                int cell = Cell(uvs.Length == vertices.Length ? uvs[a] : Vector2.zero), target = cell;
                // Rust paint blooms are 14-triangle fans around one shared centre vertex (ConceptEnvironment.Mark).
                bool mark = (cell == rust || cell == rustDark) && (uses[a] == 14 || uses[b] == 14 || uses[c] == 14);
                if (mark) marks++;
                float ax = Mathf.Abs(centre.x), az = Mathf.Abs(centre.z);
                bool canopy = centre.y > 5.2f && az < 6.8f;
                if (canopy)
                {
                    if (cell == ivory || mark) target = rust;               // rust corrugated roof, blooms painted out
                    else if (cell == rustDark) target = teal;               // edge channels + awning girders: turquoise trim
                }
                else if (ax > 11.3f && centre.y < 2.45f)
                {
                    if (cell == ivory || cell == rust || mark) target = teal; // side cladding: clean turquoise sheets
                    else if (cell == rustDark) target = tealDark;
                }
                else if (Mathf.Abs(az - 5.98f) < .22f && ax > 8f && ax < 11.6f && centre.y < 4.15f)
                {
                    if (cell == ivory || mark) target = ivoryShade;          // cream end walls
                    else if (cell == rustDark) target = teal;
                }
                else if (cell == rustDark && ax > 10.4f && ax < 11.2f && centre.y < 5.2f) target = steel;  // canopy uprights: navy steel
                else if (cell == rust && centre.y > 3.9f && centre.y < 5.7f && ax > 8.6f && ax < 11.05f) target = yellow; // knee braces
                else if (mark) target = ivoryShade;
                if (target != cell) recoloured++;
                var uv = ArcadeVisualPalette.CellUv(target);
                var parts = canopy ? canopyParts : shellParts;
                foreach (int index in new[] { a, b, c })
                {
                    // Re-coloured triangles get their own vertices so shared corners keep their original cell.
                    int key = target == cell ? index : -1 - (i + Array.IndexOf(new[] { a, b, c }, index));
                    if (!parts.map.TryGetValue(key, out int dest))
                    {
                        dest = parts.v.Count; parts.map[key] = dest;
                        parts.v.Add(vertices[index]); parts.n.Add(normals.Length == vertices.Length ? normals[index] : Vector3.up);
                        parts.uv.Add(target == cell && uvs.Length == vertices.Length ? uvs[index] : uv);
                    }
                    parts.t.Add(dest);
                }
            }
            Mesh Build(string name, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Dictionary<int, int> map) p)
            {
                var mesh = new Mesh { name = name, indexFormat = p.v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(p.v); mesh.SetNormals(p.n); mesh.SetUVs(0, p.uv); mesh.SetTriangles(p.t, 0); mesh.RecalculateBounds();
                return mesh;
            }
            return (Build("Arcade workshop shell", shellParts), Build("Arcade workshop canopy", canopyParts),
                dropped + " old floor tris dropped, " + marks + " bloom tris painted out, " + recoloured + " recoloured");
        }

        static Mesh FloorMesh()
        {
            float hx = FloorX * .5f, hz = FloorZ * .5f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count; v.AddRange(new[] { a, b, c, d }); n.AddRange(new[] { normal, normal, normal, normal }); uv.AddRange(new[] { ua, ub, uc, ud });
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Vector2 U(float x, float z) => new Vector2(x / FloorX + .5f, z / FloorZ + .5f);
            Quad(new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz), Vector3.up, U(-hx, -hz), U(-hx, hz), U(hx, hz), U(hx, -hz));
            float d = -FloorDepth; Vector2 e = U(-hx + .1f, -hz + .1f);
            Quad(new Vector3(-hx, d, -hz), new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, d, -hz), Vector3.back, e, e, e, e);
            Quad(new Vector3(hx, d, hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), new Vector3(-hx, d, hz), Vector3.forward, e, e, e, e);
            Quad(new Vector3(-hx, d, hz), new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), new Vector3(-hx, d, -hz), Vector3.left, e, e, e, e);
            Quad(new Vector3(hx, d, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(hx, d, hz), Vector3.right, e, e, e, e);
            var mesh = new Mesh { name = "Arcade workshop floor" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            return mesh;
        }

        static float Coverage(float sdf, float aa) => Mathf.Clamp01(.5f - sdf / aa);
        static float Box(float x, float z, float hx, float hz) { float dx = Mathf.Abs(x) - hx, dz = Mathf.Abs(z) - hz; return new Vector2(Mathf.Max(dx, 0), Mathf.Max(dz, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dz), 0); }

        static Texture2D WriteFloorTexture()
        {
            const int width = 1024, height = 512;
            Color concrete = ArcadeVisualInstaller.Hex("E6D6B8"), seam = ArcadeVisualInstaller.Hex("CDBB9C"), border = ArcadeVisualInstaller.Hex("C3A47C"),
                mint = ArcadeVisualInstaller.Hex("4EE0A4"), mintInk = ArcadeVisualInstaller.Hex("249E74"), gold = ArcadeVisualInstaller.Hex("FCC946"), stain = ArcadeVisualInstaller.Hex("CDBD9F");
            float aa = FloorX / width * 1.2f;
            var pixels = new Color32[width * height];
            for (int py = 0; py < height; py++) for (int px = 0; px < width; px++)
            {
                float x = ((px + .5f) / width - .5f) * FloorX, z = ((py + .5f) / height - .5f) * FloorZ;
                float noise = Mathf.PerlinNoise(x * .33f + 7, z * .33f + 3) * .6f + Mathf.PerlinNoise(x * 1.7f + 2, z * 1.7f + 9) * .4f;
                var c = concrete * (.975f + noise * .05f);
                for (int s = 0; s < 4; s++)
                {
                    float sx = (Mathf.PerlinNoise(s * 3.1f, 1.7f) - .5f) * FloorX * .8f, sz = (Mathf.PerlinNoise(2.3f, s * 2.9f) - .5f) * FloorZ * .7f;
                    float blob = new Vector2((x - sx) * .8f, z - sz).magnitude - (.7f + .5f * Mathf.PerlinNoise(x * .9f + s, z * .9f));
                    c = Color.Lerp(c, stain, Coverage(blob, .6f) * .35f);
                }
                // 2 m tile grid with thin seams.
                float gx = Mathf.Abs(Mathf.Repeat(x + 1, 2f) - 1f), gz = Mathf.Abs(Mathf.Repeat(z + 1, 2f) - 1f);
                float grid = Mathf.Min(1 - gx, 1 - gz);
                c = Color.Lerp(c, seam, Coverage(grid - .035f, aa) * .9f);
                // Mint repair lane along the drive-through axis (local z).
                float laneEdge = Mathf.Abs(Mathf.Abs(x) - 3.5f) - .11f;
                if (Mathf.Abs(x) < 3.5f) c = Color.Lerp(c, mint, .08f);
                c = Color.Lerp(c, mint, Coverage(laneEdge, aa));
                // Gold hazard corner brackets framing the service bay
                float cornerBracket = Mathf.Max(Mathf.Abs(Mathf.Abs(x) - 5.2f) - .14f, Mathf.Abs(Mathf.Abs(z) - 4.8f) - .9f);
                c = Color.Lerp(c, gold, Coverage(cornerBracket, aa) * .88f);
                for (int k = 0; k < 2; k++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float zc = side * (4.3f + k * .95f);
                        float chevron = Mathf.Abs((z - zc) - Mathf.Abs(x) * .62f + .25f) - .17f;
                        chevron = Mathf.Max(chevron, Mathf.Abs(x) - 1.9f);
                        c = Color.Lerp(c, mint, Coverage(chevron, aa) * .92f);
                    }
                // Wrench badge: mint ring + rotated wrench with a darker ink rim.
                float r = new Vector2(x, z).magnitude;
                float ring = Mathf.Abs(r - 2.15f) - .13f;
                c = Color.Lerp(c, mint, Coverage(ring, aa));
                float ca = Mathf.Cos(.785f), sa = Mathf.Sin(.785f);
                float wx = x * ca - z * sa, wz = x * sa + z * ca;
                float shaft = Box(wx, wz, .2f, 1.05f);
                float head = new Vector2(wx, wz - 1.05f).magnitude - .5f;
                float jaw = Box(wx, wz - 1.35f, .2f, .32f);
                float eye = new Vector2(wx, wz + 1.15f).magnitude - .32f;
                float hole = new Vector2(wx, wz + 1.15f).magnitude - .13f;
                float wrench = Mathf.Max(Mathf.Min(Mathf.Min(shaft, head), eye), -Mathf.Min(jaw, hole));
                c = Color.Lerp(c, mintInk, Coverage(wrench - .07f, aa));
                c = Color.Lerp(c, mint, Coverage(wrench, aa));
                // Slab border.
                float edge = Mathf.Min(FloorX * .5f - Mathf.Abs(x), FloorZ * .5f - Mathf.Abs(z));
                c = Color.Lerp(c, border, Coverage(edge - .32f, aa));
                c = Color.Lerp(c, border * .9f, Coverage(Mathf.Abs(edge - .32f) - .03f, aa) * .6f);
                c.a = 1;
                pixels[py * width + px] = (Color32)c;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { texture.SetPixels32(pixels); texture.Apply(false, false); File.WriteAllBytes(FloorTexturePath, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(FloorTexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(FloorTexturePath);
            if (!importer) throw new InvalidOperationException("Workshop floor texture import failed.");
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 4;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.maxTextureSize = 1024;
            importer.isReadable = false; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
        }

        // ---------- upgrade stations ----------

        static void Station(ArenaUpgradeStation station, Shader toon)
        {
            var root = station.transform;
            var plinth = root.Find("Low scrap plinth");
            var plinthRenderer = plinth ? plinth.GetComponent<MeshRenderer>() : null;
            if (plinthRenderer && plinthRenderer.sharedMaterial && plinthRenderer.sharedMaterial.shader == toon)
            {
                var m = plinthRenderer.sharedMaterial; // "Station worn steel": pedestal + terminal body
                m.SetColor("_BaseColor", ArcadeVisualInstaller.Hex("323E51")); m.SetFloat("_Metallic", .3f); m.SetFloat("_Smoothness", .5f);
                ArcadeVisualPalette.ApplyToonLook(m);
            }
            var available = station.availableVisual ? station.availableVisual.transform : root.Find("Available upgrade");
            var panel = available ? available.Find("Bright stat panel") : null;
            var panelRenderer = panel ? panel.GetComponent<MeshRenderer>() : null;
            bool health = station.kind == ArenaUpgradeKind.Health;
            Color accent = ArcadeVisualInstaller.Hex(health ? "4EE0A4" : "FFC93C");
            if (panelRenderer && panelRenderer.sharedMaterial && panelRenderer.sharedMaterial.shader == toon)
            {
                var m = panelRenderer.sharedMaterial; // "Attack amber" / "Health mint": panel, cross, proximity ring
                m.SetColor("_BaseColor", ArcadeVisualInstaller.Hex(health ? "4EE0A4" : "FCC946")); m.SetFloat("_Smoothness", .3f); m.SetFloat("_Metallic", .05f);
                m.SetColor("_EmissionColor", ArcadeVisualInstaller.Hex(health ? "1E9C6C" : "C8641A") * .5f);
                ArcadeVisualPalette.ApplyToonLook(m);
            }
            float top = plinthRenderer ? root.InverseTransformPoint(plinthRenderer.bounds.max).y : .32f;
            float radius = plinthRenderer ? plinthRenderer.bounds.extents.x / Mathf.Max(.0001f, root.lossyScale.x) : 1.77f;

            var gold = ToonMaterial(GoldMaterialPath, toon, "Arcade station gold");
            Configure(gold, ArcadeVisualInstaller.Hex("FCC946"), null, .1f, .32f, Color.black);
            var rim = Child(root, "Arcade station gold rim");
            rim.transform.localPosition = new Vector3(0, top - .01f, 0); rim.transform.localRotation = Quaternion.identity; rim.transform.localScale = Vector3.one;
            Visual(rim, SaveMesh(Ring(radius - .06f, .1f, 40), RimMeshPath), gold, ShadowCastingMode.On);

            var glow = ToonMaterial(health ? GlowMintMaterialPath : GlowGoldMaterialPath, toon, health ? "Arcade station glow mint" : "Arcade station glow gold");
            // Emission hue is shifted so the clipped HDR surface stays gold/mint (not yellow-white) and the bloom halo is warm.
            Configure(glow, accent, null, 0, .3f, ArcadeVisualInstaller.Hex(health ? "2EC48A" : "FF9A1A") * .8f);
            var discParent = available ? available : root;
            var disc = Child(discParent, "Arcade station glow disc");
            disc.transform.position = root.TransformPoint(new Vector3(0, top + .006f, 0)); disc.transform.rotation = root.rotation; disc.transform.localScale = Vector3.one;
            Visual(disc, SaveMesh(Annulus(Mathf.Max(.4f, radius * .62f), radius - .2f, 40), DiscMeshPath), glow, ShadowCastingMode.Off);

            // 3D Totem Crest + Corner Energy Pylons on availableVisual so each upgrade station has an unmistakable 3D silhouette
            var totem = Child(discParent, "Arcade station totem crest");
            totem.transform.position = root.TransformPoint(new Vector3(0, top, 0)); totem.transform.rotation = root.rotation; totem.transform.localScale = Vector3.one;
            Visual(totem, SaveMesh(StationTotemMesh(health, radius), health ? HealthTotemMeshPath : AttackTotemMeshPath), glow, ShadowCastingMode.On);
        }

        static Mesh StationTotemMesh(bool health, float radius)
        {
            var b = new SiteMeshBuilder();
            // 4 glowing corner pylons around the station rim
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(a) * (radius - .18f), .28f, Mathf.Sin(a) * (radius - .18f));
                b.Box(pos, new Vector3(.22f, .56f, .22f), Quaternion.Euler(0, -i * 90f - 45f, 0));
                b.Box(pos + Vector3.up * .34f, new Vector3(.14f, .18f, .14f), Quaternion.Euler(45, i * 90f, 45));
            }
            // Overhead holographic/emissive crest above the terminal
            if (health)
            {
                // Bold 3D Medical Shield & Cross Aegis Crest
                b.Box(new Vector3(0, 2.55f, 0), new Vector3(.88f, .26f, .22f), Quaternion.identity);
                b.Box(new Vector3(0, 2.55f, 0), new Vector3(.26f, .88f, .22f), Quaternion.identity);
                for (int s = -1; s <= 1; s += 2)
                    b.Box(new Vector3(s * .58f, 2.55f, 0), new Vector3(.14f, .64f, .18f), Quaternion.Euler(0, 0, s * -16f));
            }
            else
            {
                // Bold 3D Twin-Autocannon & Chevron Attack Crest
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Box(new Vector3(s * .24f, 2.62f, 0), new Vector3(.18f, .86f, .18f), Quaternion.Euler(0, 0, s * -18f));
                    b.Box(new Vector3(s * .42f, 2.45f, 0), new Vector3(.38f, .16f, .20f), Quaternion.Euler(0, 0, s * 32f));
                }
                b.Box(new Vector3(0, 2.88f, 0), new Vector3(.32f, .32f, .22f), Quaternion.Euler(0, 0, 45f));
            }
            return b.Build(health ? "Arcade station health totem" : "Arcade station attack totem");
        }

        // ---------- reward caches ----------

        static void Cache(ArenaRewardCache cache, Shader toon)
        {
            if (!cache.availableVisual) return;
            var gold = ToonMaterial(GoldMaterialPath, toon, "Arcade station gold");
            bool mintAccent = cache.reward == ArenaRewardKind.Health || cache.reward == ArenaRewardKind.Repair;
            var glow = ToonMaterial(mintAccent ? GlowMintMaterialPath : GlowGoldMaterialPath, toon, mintAccent ? "Arcade station glow mint" : "Arcade station glow gold");

            var trimGo = Child(cache.availableVisual.transform, "Arcade cache gold trim");
            trimGo.layer = cache.gameObject.layer;
            trimGo.transform.localPosition = Vector3.zero; trimGo.transform.localRotation = Quaternion.identity; trimGo.transform.localScale = Vector3.one;
            Visual(trimGo, SaveMesh(CacheTrimMesh(), CacheTrimMeshPath), gold, ShadowCastingMode.On);

            var glowGo = Child(cache.availableVisual.transform, "Arcade cache glow core");
            glowGo.layer = cache.gameObject.layer;
            glowGo.transform.localPosition = Vector3.zero; glowGo.transform.localRotation = Quaternion.identity; glowGo.transform.localScale = Vector3.one;
            Visual(glowGo, SaveMesh(CacheGlowMesh(), CacheGlowMeshPath), glow, ShadowCastingMode.Off);
        }

        static Mesh CacheTrimMesh()
        {
            var b = new SiteMeshBuilder();
            // Armored golden corner bumpers + heavy front/back lock clasps on the 1.7 x 0.92 x 1.18 chest
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    b.Box(new Vector3(sx * .84f, .52f, sz * .58f), new Vector3(.18f, .96f, .18f), Quaternion.identity);
                    b.Box(new Vector3(sx * .84f, 1.02f, sz * .58f), new Vector3(.24f, .12f, .24f), Quaternion.identity);
                }
            for (int sz = -1; sz <= 1; sz += 2)
            {
                b.Box(new Vector3(0, .74f, sz * .62f), new Vector3(.34f, .38f, .10f), Quaternion.identity);
            }
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.Box(new Vector3(sx * .90f, .62f, 0), new Vector3(.08f, .14f, .46f), Quaternion.identity);
            }
            return b.Build("Arcade cache trim");
        }

        static Mesh CacheGlowMesh()
        {
            var b = new SiteMeshBuilder();
            // Glowing energy core strips on the chest lid and front/back lock faces
            for (int sx = -1; sx <= 1; sx += 2)
                b.Box(new Vector3(sx * .46f, 1.07f, 0), new Vector3(.18f, .06f, .92f), Quaternion.identity);
            for (int sz = -1; sz <= 1; sz += 2)
                b.Box(new Vector3(0, .74f, sz * .66f), new Vector3(.18f, .18f, .05f), Quaternion.Euler(0, 0, 45f));
            return b.Build("Arcade cache glow");
        }

        static Mesh Ring(float major, float minor, int segments)
        {
            const int cross = 8;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s <= segments; s++) for (int c = 0; c <= cross; c++)
            {
                float a = s * Mathf.PI * 2 / segments, b = c * Mathf.PI * 2 / cross;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var normal = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                v.Add(dir * major + normal * minor); n.Add(normal);
            }
            for (int s = 0; s < segments; s++) for (int c = 0; c < cross; c++)
            {
                int i = s * (cross + 1) + c, j = i + cross + 1;
                t.AddRange(new[] { i, i + 1, j + 1, i, j + 1, j });
            }
            var mesh = new Mesh { name = "Arcade station rim" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, v.Select(_ => Vector2.zero).ToList()); mesh.SetTriangles(t, 0);
            if (Vector3.Dot(Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]), n[t[0]]) < 0) { t.Reverse(); mesh.SetTriangles(t, 0); }
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Annulus(float inner, float outer, int segments)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2 / segments; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(dir * inner); v.Add(dir * outer);
            }
            for (int s = 0; s < segments; s++) { int i = s * 2; t.AddRange(new[] { i, i + 2, i + 3, i, i + 3, i + 1 }); }
            if (Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]).y < 0) t.Reverse();
            var mesh = new Mesh { name = "Arcade station disc" };
            mesh.SetVertices(v); mesh.SetNormals(v.Select(_ => Vector3.up).ToList()); mesh.SetUVs(0, v.Select(_ => Vector2.zero).ToList()); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
