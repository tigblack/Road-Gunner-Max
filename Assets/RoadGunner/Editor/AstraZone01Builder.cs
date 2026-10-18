using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    // Scene coordinates use screen east / screen north. The existing camera yaw is 45 degrees.
    public static class AstraZone01Builder
    {
        public const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";
        public const string Root = "Assets/RoadGunner/Levels/Greenwood";
        public const string Report = "Documentation/RoadGunner/Astra-Zone01-2026-10-10";
        static readonly Quaternion Axis = Quaternion.Euler(0, 45, 0);
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, GameObject> props = new Dictionary<string, GameObject>();
        static System.Random random;
        static LevelLayout level;
        static int variant;
        public static Vector3 World(float east, float north, float height = 0) => Axis * new Vector3(east, height, north);

        [MenuItem("Tools/Road Gunner/Create Greenwood First Level")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Greenwood already exists. Open it in the Level Editor; the builder never overwrites a level.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before building Greenwood.");
            Directory.CreateDirectory(Root + "/Materials"); Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Prefabs"); Directory.CreateDirectory(Root + "/Tuning");
            Directory.CreateDirectory(Report); AssetDatabase.Refresh();
            random = new System.Random(100926); variant = 0; mats.Clear(); meshes.Clear(); props.Clear();
            PreparePalette(); PrepareMeshes(); PrepareProps();
            level = RoadGunnerLevelAuthoring.CreateLevel(ScenePath);
            level.levelName = "01 · Greenwood / Зелёная граница";
            level.referenceUrl = ""; level.referenceInspected = true;
            level.authoringNotes = "Authored from astra-zone-01-level-design.txt (9 October 2026). Screen north follows the existing 45° camera. Road Gunner vehicle/combat retained; roads and camp gaps adapted to its 2.2 m width and turning clearance. Future biome gates and NPC are visual landmarks, not quest systems.";
            level.requestedFootprintScale = level.appliedFootprintScale = 1;
            ClearTemplateArena(); Ground(); Paths(); Boundary();
            SouthernApproach(); SouthwestCamp(); CentralSanctuary(); Crossroads(); WestDanger(); NorthMarket(); NortheastCamp(); BiomePreviews();
            Populate(); ConfigureGameplay();
            PolishForVehicle();
            RoadGunnerLevelAuthoring.Save(level);
            var view = SceneView.lastActiveSceneView;
            if (view) { view.orthographic = true; view.LookAtDirect(World(-4, 8), Quaternion.Euler(57, 45, 0), 81); view.Repaint(); }
            Selection.activeObject = level.gameObject;
            Inspect(); Debug.Log("GREENWOOD BUILT: " + ScenePath);
        }

        static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
        static void Mat(string name, string hex, bool emission = false)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", Hex(hex)); m.SetFloat("_Smoothness", .08f); m.enableInstancing = true;
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Hex(hex) * .55f); }
            EditorUtility.SetDirty(m); mats[name] = m;
        }
        static void PreparePalette()
        {
            Mat("Grass", "658749"); Mat("GrassLight", "789653"); Mat("Road", "AA8150"); Mat("Paver", "C0A071"); Mat("PaverShade", "B6966B");
            Mat("Stone", "77818C"); Mat("StoneLight", "969CAE"); Mat("StonePurple", "76758D");
            Mat("Wood", "AE683C"); Mat("WoodDark", "674630"); Mat("Metal", "4D5862");
            Mat("Canvas", "E8DABC"); Mat("Blue", "3D83BA"); Mat("Purple", "AB66D3", true);
            Mat("Fir", "557B3D"); Mat("FirBright", "75B94F"); Mat("FirDark", "3D613C"); Mat("Yellow", "D8BB59");
            Mat("Water", "50EB74", true); Mat("Cyan", "62CADC", true); Mat("Teal", "438F9B");
            Mat("Snow", "DAE9E8"); Mat("OrangeGround", "C79052"); Mat("RedRock", "B56B4B");
            Mat("Skin", "BDA37B"); Mat("Cloth", "666E59"); Mat("Red", "B85649");
            Mat("Bone", "C9BC99"); Mat("Dark", "303C37"); Mat("Lantern", "FFE19B", true);
        }
        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name;
            string path = Root + "/Meshes/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
        static Mesh Flat(List<Vector3> points, List<int> indices)
        {
            var v = new Vector3[indices.Count]; var t = new int[indices.Count]; var uv = new Vector2[indices.Count];
            for (int i = 0; i < indices.Count; i++) { v[i] = points[indices[i]]; t[i] = i; uv[i] = new Vector2(v[i].x, v[i].y + v[i].z); }
            var m = new Mesh(); m.vertices = v; m.uv = uv; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Mesh Radial(int sides, float top, bool irregular = false)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int ring = 0; ring < 2; ring++) for (int i = 0; i < sides; i++) {
                float a = i * Mathf.PI * 2 / sides, r = (ring == 0 ? .5f : top) * (irregular ? 1 + .1f * Mathf.Sin(i * 7 + ring) : 1);
                v.Add(new Vector3(Mathf.Cos(a) * r, ring, Mathf.Sin(a) * r));
            }
            for (int i = 0; i < sides; i++) { int j = (i + 1) % sides; t.AddRange(new[] { i, i + sides, j, j, i + sides, j + sides }); }
            for (int i = 1; i < sides - 1; i++) { t.AddRange(new[] { 0, i, i + 1, sides, sides + i + 1, sides + i }); }
            return Flat(v, t);
        }
        static Mesh RockMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>(); float[] y = { 0, .16f, .76f, 1 }, r = { .38f, .5f, .43f, .32f };
            for (int k = 0; k < 4; k++) for (int i = 0; i < 7; i++) { float a = i * Mathf.PI * 2 / 7 + k % 2 * .12f; float f = 1 + .13f * Mathf.Sin(i * 7.3f + k * 1.8f); v.Add(new Vector3(Mathf.Cos(a) * r[k] * f, y[k], Mathf.Sin(a) * r[k] * f)); }
            for (int k = 0; k < 3; k++) for (int i = 0; i < 7; i++) { int a = k * 7 + i, b = k * 7 + (i + 1) % 7; t.AddRange(new[] { a, a + 7, b, b, a + 7, b + 7 }); }
            for (int i = 1; i < 6; i++) t.AddRange(new[] { 21, 21 + i + 1, 21 + i, 0, i, i + 1 });
            return Flat(v, t);
        }
        static void PrepareMeshes()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); meshes["Box"] = cube.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cube);
            meshes["Cylinder"] = SaveMesh("Eight sided cylinder", Radial(8, .5f));
            meshes["Cone"] = SaveMesh("Six sided crown", Radial(6, 0));
            meshes["Rock"] = SaveMesh("Faceted stone", RockMesh());
        }
        static Transform New(Transform parent, string name)
        { var g = new GameObject(name); g.transform.SetParent(parent, false); return g.transform; }
        static GameObject Shape(Transform p, string name, string mesh, string mat, Vector3 at, Vector3 size, Vector3 angles = default)
        {
            var g = New(p, name).gameObject; g.transform.localPosition = at; g.transform.localScale = size; g.transform.localRotation = Quaternion.Euler(angles);
            g.AddComponent<MeshFilter>().sharedMesh = meshes[mesh]; g.AddComponent<MeshRenderer>().sharedMaterial = mats[mat]; return g;
        }
        static void Box(Transform p, string mat, Vector3 at, Vector3 size, Vector3 angles = default) => Shape(p, mat, "Box", mat, at, size, angles);
        static void Beam(Transform p, string mat, Vector3 a, Vector3 b, float width)
        { var g = Shape(p, mat + " beam", "Box", mat, (a + b) / 2, new Vector3(width, (b - a).magnitude, width)); g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); }
        static void Collider(Transform p, Vector3 center, Vector3 size)
        { var g = New(p, "Collision").gameObject; var c = g.AddComponent<BoxCollider>(); c.center = center; c.size = size; }
        static void Merge(Transform root, string name)
        {
            var sources = root.GetComponentsInChildren<MeshFilter>(); var grouped = sources.GroupBy(f => f.GetComponent<Renderer>().sharedMaterial).ToArray();
            var sub = new List<CombineInstance>(); var materials = new List<Material>(); var temp = new List<Mesh>();
            foreach (var group in grouped) {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(group.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = root.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                sub.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity }); materials.Add(group.Key); temp.Add(m);
            }
            var combined = new Mesh { indexFormat = IndexFormat.UInt32 }; combined.CombineMeshes(sub.ToArray(), false, false); combined.RecalculateBounds();
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(name, combined);
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            foreach (var f in sources) Object.DestroyImmediate(f.gameObject); foreach (var m in temp) Object.DestroyImmediate(m);
        }
        static void Prefab(string name, Action<Transform> action)
        {
            var root = New(null, name); action(root); Merge(root, name);
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            props[name] = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Root + "/Prefabs/" + name + ".prefab"); Object.DestroyImmediate(root.gameObject);
        }
        static void Tree(Transform p, string leaf, float h = 5, bool snowy = false)
        {
            Shape(p, "Trunk", "Cylinder", "WoodDark", Vector3.zero, new Vector3(.46f, h * .7f, .46f));
            for (int i = 0; i < 3; i++) Shape(p, "Crown", "Cone", leaf, new Vector3(0, h * (.18f + .21f * i), 0), new Vector3(h * (.7f - .14f * i), h * .5f, h * (.7f - .14f * i)), new Vector3(0, i * 23, 0));
            if (snowy) for (int i = 0; i < 3; i++) Shape(p, "Snow crown", "Cone", "Snow", new Vector3(0, h * (.29f + .21f * i), 0), new Vector3(h * (.52f - .11f * i), h * .38f, h * (.52f - .11f * i)));
        }
        static void Bush(Transform p, string mat)
        { for (int i = 0; i < 4; i++) Shape(p, "Leaf clump", "Rock", mat, new Vector3((i % 2 - .5f) * .8f, i % 2 * .15f, (i / 2 - .5f) * .65f), new Vector3(1.5f, 1 + i % 2 * .3f, 1.4f), new Vector3(0, i * 61, 0)); }
        static void Palisade(Transform p)
        {
            for (int i = 0; i < 8; i++) { float x = -2.6f + i * .74f, h = 2 + (i % 3) * .22f; Box(p, i % 3 == 0 ? "WoodDark" : "Wood", new Vector3(x, h / 2, 0), new Vector3(.65f, h, .3f)); Shape(p, "Point", "Cone", "Wood", new Vector3(x, h, 0), new Vector3(.65f, .35f, .4f)); }
            for (int s = -1; s <= 1; s += 2) { Box(p, "WoodDark", new Vector3(s * 3, 1.35f, 0), new Vector3(.45f, 2.7f, .5f)); Shape(p, "Cap", "Rock", "StoneLight", new Vector3(s * 3, 2.6f, 0), new Vector3(.75f, .45f, .7f)); }
            Box(p, "WoodDark", new Vector3(0, .85f, -.18f), new Vector3(6, .18f, .2f)); Collider(p, new Vector3(0, 1.1f, 0), new Vector3(6.4f, 2.5f, .6f));
        }
        static void Tent(Transform p)
        {
            Box(p, "WoodDark", new Vector3(0, .13f, 0), new Vector3(5.6f, .26f, 6));
            for (int s = -1; s <= 1; s += 2) { Box(p, "Canvas", new Vector3(s * 1.28f, 1.9f, 0), new Vector3(.2f, 4.8f, 6), new Vector3(0, 0, s * 33)); Box(p, "Blue", new Vector3(s * 1.28f, 1.9f, -2.9f), new Vector3(.25f, 4.8f, .22f), new Vector3(0, 0, s * 33)); }
            Box(p, "Canvas", new Vector3(0, 1.3f, 2.8f), new Vector3(2.7f, 2.4f, .16f));
            Box(p, "Dark", new Vector3(0, .9f, -2.84f), new Vector3(1.5f, 1.8f, .15f));
            Beam(p, "Wood", new Vector3(0, 0, -3.1f), new Vector3(0, 3.9f, -3.1f), .15f);
            Collider(p, new Vector3(0, 1.3f, 0), new Vector3(5.5f, 2.6f, 6));
        }
        static void Tower(Transform p)
        {
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2) Box(p, "WoodDark", new Vector3(x * 1.65f, 2.35f, z * 1.65f), new Vector3(.4f, 4.7f, .4f));
            Box(p, "Wood", new Vector3(0, 3.5f, 0), new Vector3(3.9f, .3f, 3.9f));
            for (int s = -1; s <= 1; s += 2) { Box(p, "Wood", new Vector3(0, 4.25f, s * 1.8f), new Vector3(3.9f, .26f, .2f)); Box(p, "Wood", new Vector3(s * 1.8f, 4.25f, 0), new Vector3(.2f, .26f, 3.9f)); Box(p, "WoodDark", new Vector3(s * 1.02f, 5.3f, 0), new Vector3(2.8f, .25f, 4.7f), new Vector3(0, 0, s * -26)); }
            for (int i = 0; i < 6; i++) Box(p, "Wood", new Vector3(1.1f, .4f + i * .5f, -1.9f), new Vector3(.8f, .15f, .2f));
            Beam(p, "Wood", new Vector3(-1.7f, .6f, -1.7f), new Vector3(1.7f, 3.4f, -1.7f), .22f);
            Collider(p, new Vector3(0, 2, 0), new Vector3(3.8f, 4, 3.8f));
        }
        static void Cart(Transform p)
        {
            Box(p, "Wood", new Vector3(0, 1.1f, 0), new Vector3(3, .5f, 5));
            Box(p, "Wood", new Vector3(0, 1.65f, 2.3f), new Vector3(3, 1, .25f));
            for (int s = -1; s <= 1; s += 2) { Box(p, "Wood", new Vector3(s * 1.4f, 1.65f, 0), new Vector3(.2f, 1, 5)); Box(p, "Canvas", new Vector3(s * .9f, 3.1f, 0), new Vector3(.18f, 2.6f, 4.8f), new Vector3(0, 0, s * 40));
                for (int z = -1; z <= 1; z += 2) Shape(p, "Wheel", "Cylinder", "WoodDark", new Vector3(s * 1.7f, .7f, z * 1.7f), new Vector3(1.5f, .25f, 1.5f), new Vector3(0, 0, s * 90));
                Beam(p, "Wood", new Vector3(s * .7f, 1, -2.3f), new Vector3(s * .7f, .65f, -4.2f), .18f); }
            Collider(p, new Vector3(0, 1.2f, 0), new Vector3(3.6f, 2.4f, 5.2f));
        }
        static void Gate(Transform p, bool crystal, bool open)
        {
            string leaf = crystal ? "Teal" : "Wood";
            for (int s = -1; s <= 1; s += 2) {
                Shape(p, "Pylon", "Rock", "Stone", new Vector3(s * 5.8f, 0, 0), new Vector3(3.2f, 7.5f, 3));
                Shape(p, "Cap", "Rock", crystal ? "Snow" : "StoneLight", new Vector3(s * 5.8f, 7.1f, 0), new Vector3(3.8f, .8f, 3.5f));
                Collider(p, new Vector3(s * 5.8f, 3.5f, 0), new Vector3(2.7f, 7, 2.8f));
                var door = New(p, "Gate leaf"); door.localPosition = new Vector3(s * (open ? 5.3f : 2.5f), 0, open ? -2.4f : 0); door.localRotation = Quaternion.Euler(0, open ? s * 76 : 0, 0);
                for (int i = 0; i < 6; i++) Box(door, leaf, new Vector3(-2.1f + i * .84f, 2.8f, 0), new Vector3(.8f, 5.6f + (i % 2) * .35f, .45f));
                Box(door, "Metal", new Vector3(0, 1.1f, -.3f), new Vector3(5, .35f, .2f)); Box(door, "Metal", new Vector3(0, 4.4f, -.3f), new Vector3(5, .35f, .2f));
                if (crystal) { Shape(door, "Purple crystal", "Rock", "Purple", new Vector3(0, 1.8f, -.55f), new Vector3(1.8f, 3, .6f)); Box(door, "Snow", new Vector3(0, 5.85f, 0), new Vector3(4.9f, .45f, .7f)); }
                else Beam(door, "Metal", new Vector3(-2, .6f, -.38f), new Vector3(2, 4.8f, -.38f), .22f);
                Collider(door, new Vector3(0, 2.7f, 0), new Vector3(5, 5.4f, .7f));
            }
            if (!open) Box(p, "Metal", new Vector3(0, 2.6f, -.5f), new Vector3(.3f, 1.5f, .2f));
        }
        static void Disc(Transform p, string mat, float radius, float height, float y = 0)
        { Shape(p, mat + " disc", "Cylinder", mat, new Vector3(0, y, 0), new Vector3(radius * 2, height, radius * 2)); }
        static void Ring(Transform p, string mat, float radius, float thickness, float height, bool vertical)
        {
            int n = vertical ? 14 : 18;
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; Vector3 at = vertical ? new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(a) * radius, 0) : new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
                Box(p, mat, at, new Vector3(radius * 2 * Mathf.Sin(Mathf.PI / n) * .97f, thickness, thickness * 1.8f), vertical ? new Vector3(0, 0, a * Mathf.Rad2Deg + 90) : new Vector3(0, -a * Mathf.Rad2Deg + 90, 0)); }
        }
        static void PrepareProps()
        {
            Prefab("Cliff", p => { Shape(p, "Rock face", "Rock", "Stone", Vector3.zero, new Vector3(9, 6, 8)); Shape(p, "Green shelf", "Rock", "Grass", new Vector3(0, 5.3f, 0), new Vector3(8.3f, 1, 7.4f)); Collider(p, new Vector3(0, 2.8f, 0), new Vector3(7.4f, 5.6f, 6.8f)); });
            Prefab("Fir", p => Tree(p, "Fir")); Prefab("Bright Fir", p => Tree(p, "FirBright", 3.6f)); Prefab("Yellow Fir", p => Tree(p, "Yellow", 4.5f)); Prefab("Snow Fir", p => Tree(p, "FirDark", 5, true));
            Prefab("Bush", p => Bush(p, "FirBright")); Prefab("Yellow Bush", p => Bush(p, "Yellow"));
            Prefab("Grass Tuft", p => { for(int i=0;i<5;i++) Shape(p,"Blade","Cone",i%2==0?"GrassLight":"FirBright",new Vector3((i%3-1)*.16f,0,(i/3)*.2f),new Vector3(.12f,.35f+i%3*.13f,.12f),new Vector3(i*7, i*31, i*8-16)); });
            Prefab("Palisade", Palisade); Prefab("Watchtower", Tower); Prefab("Tent", Tent); Prefab("Covered Cart", Cart);
            Prefab("Barrel", p => { Shape(p, "Staves", "Cylinder", "Wood", Vector3.zero, new Vector3(1.2f, 1.5f, 1.2f)); for (int i = 0; i < 2; i++) Shape(p, "Hoop", "Cylinder", "Metal", new Vector3(0, .25f + i * .8f, 0), new Vector3(1.24f, .15f, 1.24f)); });
            Prefab("Crate", p => { Box(p, "Wood", new Vector3(0, .7f, 0), new Vector3(1.7f, 1.4f, 1.5f)); for (int s = -1; s <= 1; s += 2) { Box(p, "Stone", new Vector3(s * .7f, .7f, -.78f), new Vector3(.2f, 1.5f, .16f)); Box(p, "Stone", new Vector3(0, .7f + s * .6f, -.78f), new Vector3(1.7f, .2f, .16f)); } });
            Prefab("Lantern", p => { Box(p, "WoodDark", new Vector3(0, 1.6f, 0), new Vector3(.28f, 3.2f, .3f)); Box(p, "Wood", new Vector3(.6f, 3.1f, 0), new Vector3(1.5f, .24f, .28f)); Beam(p, "Metal", new Vector3(1.1f, 3, 0), new Vector3(1.1f, 2.55f, 0), .08f); Box(p, "Lantern", new Vector3(1.1f, 2.35f, 0), new Vector3(.45f, .55f, .45f)); Box(p, "Metal", new Vector3(1.1f, 2.68f, 0), new Vector3(.65f, .15f, .6f)); });
            Prefab("Signpost", p => { Box(p, "WoodDark", new Vector3(0, 1.25f, 0), new Vector3(.3f, 2.5f, .3f)); for (int i = 0; i < 3; i++) { Box(p, i == 1 ? "Blue" : "Wood", new Vector3(i % 2 * .3f, 1.3f + i * .4f, -.2f), new Vector3(1.8f, .32f, .22f), new Vector3(0, i * 30, 0)); Shape(p, "Arrowhead", "Cone", "Wood", new Vector3(i == 1 ? -.95f : 1.15f, 1.3f + i * .4f, -.2f), new Vector3(.5f, .55f, .24f), new Vector3(0, 0, i == 1 ? 90 : -90)); } });
            Prefab("Open South Gate", p => Gate(p, false, true)); Prefab("Closed West Gate", p => Gate(p, false, false)); Prefab("Crystal North Gate", p => Gate(p, true, false));
            Prefab("Stone Arch", p => { Disc(p, "StonePurple", 4.8f, .22f); Disc(p, "StoneLight", 4.1f, .28f, .22f); Ring(p, "Stone", 3.5f, .7f, 4.05f, true); Disc(p, "Cyan", 1.4f, .08f, .53f); Shape(p,"Cyan core","Rock","Cyan",Vector3.up*.53f,new Vector3(1.2f,1.8f,1.2f)); Collider(p, new Vector3(0, .25f, 0), new Vector3(8.3f, .5f, 7.4f)); for (int s = -1; s <= 1; s += 2) Collider(p, new Vector3(s * 3.15f, 2.5f, 0), new Vector3(1.8f, 5, 1.7f)); });
            Prefab("Green Pool", p => { Disc(p, "StonePurple", 4.6f, .12f); Disc(p, "Water", 3.8f, .04f, .17f); Ring(p, "StoneLight", 4.05f, .38f, .28f, false); });
            Prefab("Windmill", p => { Shape(p, "Body", "Cylinder", "WoodDark", Vector3.zero, new Vector3(3.2f, 5.3f, 3.2f)); Shape(p, "Roof", "Cone", "Wood", new Vector3(0, 5.3f, 0), new Vector3(4.5f, 1.4f, 4.5f)); for (int i = 0; i < 4; i++) { float a = i * 90 + 25; var blade = New(p, "Sail"); blade.localPosition = new Vector3(0, 4.8f, -2); blade.localRotation = Quaternion.Euler(0, 0, a); Box(blade, "Wood", new Vector3(0, 1.8f, 0), new Vector3(.22f, 4, .18f)); Box(blade, "Blue", new Vector3(.45f, 2.15f, 0), new Vector3(1, 2.4f, .15f)); } Shape(p, "Hub", "Rock", "Wood", new Vector3(0, 4.8f, -2.15f), Vector3.one * .75f); Collider(p, new Vector3(0, 2.6f, 0), new Vector3(3.2f, 5.2f, 3.2f)); });
            Prefab("Sealed Cliff Door", p => { for (int s = -1; s <= 1; s += 2) { Shape(p, "Pylon", "Rock", "StoneLight", new Vector3(s * 2.6f, 0, 0), new Vector3(2.1f, 5.8f, 2.5f)); Collider(p, new Vector3(s * 2.6f, 2.8f, 0), new Vector3(2, 5.6f, 2.4f)); } Box(p, "Stone", new Vector3(0, 5.3f, 0), new Vector3(7, 1.2f, 2.5f)); for (int i = 0; i < 6; i++) Box(p, "WoodDark", new Vector3(-1.75f + i * .7f, 2.3f, -.25f), new Vector3(.64f, 4.6f, .45f)); Beam(p, "Purple", new Vector3(-2.2f, .6f, -.55f), new Vector3(2.2f, 4.5f, -.55f), .24f); Beam(p, "Purple", new Vector3(2.2f, .6f, -.55f), new Vector3(-2.2f, 4.5f, -.55f), .24f); var seal = New(p, "Rune seal"); seal.localPosition = new Vector3(0, 2.6f, -.8f); seal.localRotation = Quaternion.Euler(90, 0, 0); Disc(seal, "Purple", 1, .18f); Ring(p, "Cyan", .72f, .13f, 2.6f, true); Collider(p, new Vector3(0, 2.4f, 0), new Vector3(5, 4.8f, 1)); });
            Prefab("Blue Wreck", p => { for (int i = 0; i < 6; i++) { Beam(p, i % 2 == 0 ? "Wood" : "WoodDark", new Vector3(-4 + i * 1.5f, 0, -1.7f), new Vector3(-3 + i * 1.2f, 3 + i % 3, 1.8f), .55f); Box(p, "Blue", new Vector3(-3 + i * 1.1f, 2.6f + i % 2 * .7f, .3f), new Vector3(1.4f, 2.1f, .18f), new Vector3(i * 9, 0, i * 13 - 25)); } Box(p, "Stone", new Vector3(0, .5f, 0), new Vector3(9, 1, 3)); Collider(p, new Vector3(0, 1.8f, 0), new Vector3(9, 3.6f, 3.8f)); });
            Prefab("Ruin", p => { Shape(p, "Wall", "Rock", "StonePurple", Vector3.zero, new Vector3(3.6f, 3, 1.1f)); Shape(p, "Broken pillar", "Rock", "StoneLight", new Vector3(1.3f, 0, .5f), new Vector3(1, 4.4f, 1.1f)); Collider(p, new Vector3(0, 1.5f, 0), new Vector3(3.1f, 3, 1.3f)); });
            Prefab("Mushroom Cluster", p => { for (int i = 0; i < 4; i++) { Vector3 v = new Vector3((i % 2) * .7f, 0, (i / 2) * .65f); Shape(p, "Stem", "Cylinder", "Canvas", v, new Vector3(.17f, .6f + i % 2 * .2f, .17f)); Shape(p, "Purple cap", "Cone", "Purple", v + Vector3.up * (.6f + i % 2 * .2f), new Vector3(.75f, .35f, .75f)); } });
            Prefab("Planks", p => { for (int i = 0; i < 5; i++) Box(p, i % 2 == 0 ? "Wood" : "WoodDark", new Vector3(i * .4f - .8f, .07f + i * .02f, (i % 2 - .5f) * .8f), new Vector3(.24f, .14f, 2.4f), new Vector3(0, i * 34, 0)); });
            Prefab("Bones", p => { for (int i = 0; i < 3; i++) { Beam(p, "Bone", new Vector3(i * .6f, .13f, 0), new Vector3(i * .6f + .4f, .15f, 1), .13f); Shape(p, "Skull", "Rock", "Bone", new Vector3(i * .6f - .2f, .1f, .7f), Vector3.one * .5f); } });
            Prefab("Skull Sign", p => { Box(p, "WoodDark", new Vector3(0, 1, 0), new Vector3(.23f, 2, .23f)); Box(p, "Wood", new Vector3(0, 1.7f, 0), new Vector3(1.5f, 1.2f, .2f)); Shape(p, "Skull", "Rock", "Bone", new Vector3(0, 1.45f, -.2f), new Vector3(.7f, .7f, .2f)); for (int s = -1; s <= 1; s += 2) Box(p, "Dark", new Vector3(s * .15f, 1.83f, -.31f), new Vector3(.14f, .14f, .04f)); });
            Prefab("Stakes", p => { for (int i = 0; i < 4; i++) { var g = Shape(p, "Stake", "Cone", "WoodDark", new Vector3((i - 1.5f) * .65f, 0, 0), new Vector3(.35f, 1.7f + i % 2 * .3f, .35f), new Vector3(12, 0, 8)); } });
            Prefab("Cactus", p => { Shape(p, "Stem", "Cylinder", "FirDark", Vector3.zero, new Vector3(.65f, 3.3f, .65f)); for (int s = -1; s <= 1; s += 2) { Beam(p, "FirDark", new Vector3(0, 1.4f + s * .4f, 0), new Vector3(s, 1.4f + s * .4f, 0), .35f); Beam(p, "FirDark", new Vector3(s, 1.4f + s * .4f, 0), new Vector3(s, 2.4f + s * .4f, 0), .35f); } });
            Prefab("Green Haired Guide", p => { Box(p, "Cloth", new Vector3(0, 1.6f, 0), new Vector3(1.4f, 1.5f, .9f)); Shape(p, "Head", "Rock", "Skin", new Vector3(0, 2.3f, 0), new Vector3(.95f, 1, .85f)); Shape(p, "Hair", "Rock", "FirBright", new Vector3(0, 3, 0), new Vector3(1.1f, .5f, 1)); for (int s = -1; s <= 1; s += 2) { Box(p, "Dark", new Vector3(s * .4f, .5f, 0), new Vector3(.5f, 1, .65f)); Box(p, "Skin", new Vector3(s * .92f, 1.65f, 0), new Vector3(.4f, 1.3f, .5f)); Box(p, "Red", new Vector3(s * .65f, 2.3f, 0), new Vector3(.7f, .5f, 1.05f)); } Collider(p, new Vector3(0, 1.3f, 0), new Vector3(1.8f, 2.6f, 1.2f)); });
            AssetDatabase.SaveAssets();
        }
        static Transform Section(bool obstacle, string name) => New(obstacle ? level.obstaclesRoot : level.decorationRoot, name);
        static GameObject Place(string name, Transform section, float x, float z, float yaw = 0, float scale = 1, float height = 0)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(props[name], section); g.name = name;
            g.transform.SetPositionAndRotation(World(x, z, height), Axis * Quaternion.Euler(0, yaw, 0)); g.transform.localScale = Vector3.one * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform); return g;
        }
        static void Boulder(Transform parent, float x, float z, float size = 1.2f, string mat = "Stone", float h = 0)
        {
            var g = Shape(parent, "Boulder", "Rock", mat, Vector3.zero, new Vector3(size, size * .7f, size * .85f)); g.transform.SetPositionAndRotation(World(x, z, h), Axis * Quaternion.Euler(0, x * 31, 0));
        }
        static void Cluster(Transform p, float x, float z, bool yellow = false)
        { Place(yellow ? "Yellow Bush" : "Bush", p, x, z, x * 13); Place("Grass Tuft",p,x-.8f,z-1.2f); Place("Mushroom Cluster", p, x + 1.5f, z - .8f, x * 9, .9f); for (int i = 0; i < 4; i++) Boulder(p, x - 1.5f + i * .7f, z - 1.8f + i % 2 * .5f, .5f + i * .15f, "StonePurple"); }
        static void ClearTemplateArena()
        {
            var arena = level.ground.transform.parent;
            foreach (Transform child in arena.Cast<Transform>().ToArray()) if (child != level.ground.transform) Object.DestroyImmediate(child.gameObject);
        }
        static void Ground()
        {
            var mesh = level.ground.sharedMesh; int nx = 62, nz = 73; var vertices = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[vertices.Length]; var tri = new List<int>();
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++) { int i = z * (nx + 1) + x; vertices[i] = new Vector3(-70 + x * 2, 0, -66 + z * 2); uv[i] = new Vector2(x / (float)nx, z / (float)nz); }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) { int a = z * (nx + 1) + x; tri.AddRange(new[] { a, a + nx + 1, a + 1, a + 1, a + nx + 1, a + nx + 2 }); }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            level.ground.name = "Greenwood · flat grass / drivable ground"; level.ground.transform.SetPositionAndRotation(Vector3.zero, Axis);
            level.ground.transform.localScale = Vector3.one; level.ground.GetComponent<Renderer>().sharedMaterial = mats["Grass"];
            level.ground.GetComponent<MeshCollider>().sharedMesh = null; level.ground.GetComponent<MeshCollider>().sharedMesh = mesh;
            var baseline = level.baselineTerrain; EditorUtility.CopySerialized(mesh, baseline); baseline.name = "Terrain baseline"; EditorUtility.SetDirty(baseline);
            level.gameplayBounds = new Bounds(World(-6, 7), new Vector3(196, 24, 196));
        }
        static float Rand(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        static void Path(Transform parent, string name, Vector2[] points, float width)
        {
            var root = New(parent, name); var v = new List<Vector3>(); var t = new List<int>();
            var samples = new List<Vector2>();
            for (int k = 0; k < points.Length - 1; k++) { int n = Mathf.CeilToInt(Vector2.Distance(points[k], points[k + 1]) / 2); for (int j = 0; j < n; j++) samples.Add(Vector2.Lerp(points[k], points[k + 1], j / (float)n)); }
            samples.Add(points.Last());
            for (int i = 0; i < samples.Count; i++) { Vector2 dir = (samples[Math.Min(i + 1, samples.Count - 1)] - samples[Math.Max(0, i - 1)]).normalized; Vector2 side = new Vector2(dir.y, -dir.x); float edge = width / 2 + Rand(-.35f, .35f);
                v.Add(World(samples[i].x - side.x * edge, samples[i].y - side.y * edge, .015f)); v.Add(World(samples[i].x + side.x * edge, samples[i].y + side.y * edge, .015f)); if (i > 0) { int a = i * 2; t.AddRange(new[] { a - 2, a, a - 1, a - 1, a, a + 1 }); }
                for (int j = -1; j <= 1; j++) { Vector2 at = samples[i] + side * (j * width * .28f + Rand(-.2f, .2f)); var g = Shape(root, "Irregular road stone", "Rock", (i+j)%3==0 ? "PaverShade" : "Paver", Vector3.zero, new Vector3(Rand(1.9f, 2.5f), .04f, Rand(1.6f, 2.05f))); g.transform.SetPositionAndRotation(World(at.x, at.y, .026f), Axis * Quaternion.Euler(0, Rand(-15, 15), 0)); }
            }
            var mesh = Flat(v, t); var strip = New(root, "Warm path bed").gameObject; strip.AddComponent<MeshFilter>().sharedMesh = mesh; strip.AddComponent<MeshRenderer>().sharedMaterial = mats["Road"];
            Merge(root, "Road " + name); Object.DestroyImmediate(mesh);
        }
        static void Paths()
        {
            var p = Section(false, "00 · Routes and clear ground");
            Path(p, "South to north", new[] { new Vector2(0, -62), new Vector2(0, -43), new Vector2(1.5f, -25), new Vector2(0, -5), new Vector2(0, 22), new Vector2(-1, 42), new Vector2(0, 70) }, 7.5f);
            Path(p, "West biome road", new[] { new Vector2(0, 22), new Vector2(-17, 22), new Vector2(-30, 24), new Vector2(-58, 24) }, 7.5f);
            Path(p, "East camp approach", new[] { new Vector2(0, 22), new Vector2(15, 22), new Vector2(35, 23), new Vector2(39, 33) }, 7);
            Path(p, "Northeast camp entrance",new[]{new Vector2(0,36.5f),new Vector2(16,36.5f),new Vector2(24,40)},4.5f);
            Path(p, "Southwest camp spur", new[] { new Vector2(0, -9), new Vector2(-11, -9), new Vector2(-22, -16) }, 5);
            Path(p, "Sanctuary fragments", new[] { new Vector2(3, -1), new Vector2(6.5f, -1) }, 3.5f);
            for (int i = 0; i < 32; i++) { float x = Rand(-37, 37), z = Rand(-36, 58); if (Mathf.Abs(x) < 7 || Mathf.Abs(z - 22) < 6 || (x > 8 && x < 27 && z > -20 && z < 13)) continue; var g = Shape(p, "Quiet grass patch", "Cylinder", "GrassLight", Vector3.zero, new Vector3(Rand(2.5f, 6), .005f, Rand(2, 4))); g.transform.position = World(x, z, .006f); }
        }
        static void Cliff(Transform p, float x, float z, float yaw = 0, float scale = 1, bool tree = true)
        {
            Place("Cliff", p, x, z, yaw, scale); if (tree) { var d = Section(false, "Cliff crown " + (++variant)); Place("Fir", d, x - .8f, z + .5f, yaw, .9f * scale, 6 * scale); }
        }
        static void Boundary()
        {
            var p = Section(true, "01 · Rock boundaries and islands");
            for (int i = 0; i < 13; i++) { float z = -33 + i * 7.4f; if (z > 16 && z < 32) continue; Cliff(p, -46, z, i * 37, 1.16f); }
            Cliff(p,-47,16,45,.55f,false);
            for (int i = 0; i < 13; i++) Cliff(p, 48, -31 + i * 7.6f, i * 31, 1.15f);
            for (int i = 0; i < 5; i++) { Cliff(p, -16 - i * 4.3f, -53 + i * 5.3f, 20 + i * 31); Cliff(p, 16 + i * 6.4f, -53 + i * 5.3f, i * 43); }
            for (int i = 0; i < 6; i++) { Cliff(p, -44 + i * 7, 65, i * 37); Cliff(p, 10 + i * 7.2f, 66, i * 29); }
            Cliff(p, -26, 14, 31, 1.1f); Cliff(p, -35, 8, 90, .9f); Cliff(p, 37, 47, 12, .85f); Cliff(p, 43, 49, 41, .85f); Cliff(p, 43, 63, 71, .8f);
            // Last-resort walls outside visible cliffs keep physics on the finite ground mesh.
            var safety = New(p, "Outer safety collision"); safety.SetPositionAndRotation(World(-8, 7), Axis);
            Collider(safety, new Vector3(-61, 5, 0), new Vector3(1, 16, 144)); Collider(safety, new Vector3(61, 5, 0), new Vector3(1, 16, 144)); Collider(safety, new Vector3(0, 5, -72), new Vector3(124, 16, 1)); Collider(safety, new Vector3(0, 5, 72), new Vector3(124, 16, 1));
            SealBoundary();
        }
        static void SealBoundary()
        {
            var old=level.obstaclesRoot.Find("01b · Continuous cliff foundations");if(old)Object.DestroyImmediate(old.gameObject);
            var root=Section(true,"01b · Continuous cliff foundations");root.SetPositionAndRotation(Vector3.zero,Axis);
            Action<Vector2,Vector2> wall=(a,b)=>{
                var part=New(root,"Cliff foundation");part.localPosition=new Vector3((a.x+b.x)/2,2.5f,(a.y+b.y)/2);
                part.localRotation=Quaternion.LookRotation(new Vector3(b.x-a.x,0,b.y-a.y));
                var c=part.gameObject.AddComponent<BoxCollider>();c.size=new Vector3(1.6f,5,Vector2.Distance(a,b)+.65f);
                Shape(part,"Stone joining ledge","Box","Stone",Vector3.zero,c.size);
            };
            Vector2[] west={new Vector2(-8,-63),new Vector2(-15,-57),new Vector2(-27,-39),new Vector2(-39,-32),new Vector2(-46,-31),new Vector2(-46,18.5f)};
            Vector2[] east={new Vector2(8,-63),new Vector2(15,-57),new Vector2(27,-45),new Vector2(39,-34),new Vector2(48,-31),new Vector2(48,74),new Vector2(30,74),new Vector2(20,66),new Vector2(6,66)};
            for(int i=0;i<west.Length-1;i++)wall(west[i],west[i+1]);for(int i=0;i<east.Length-1;i++)wall(east[i],east[i+1]);
            wall(new Vector2(-46,29.5f),new Vector2(-46,65));wall(new Vector2(-46,65),new Vector2(-6,65));
            wall(new Vector2(-8,-63),new Vector2(8,-63));
            foreach(var t in root.GetComponentsInChildren<Transform>())t.gameObject.isStatic=true;
        }
        static void SouthernApproach()
        {
            var o = Section(true, "A · South gate and entry"); var d = Section(false, "A · Entry dressing");
            Place("Open South Gate", o, 0, -58); Place("Lantern", d, -6.5f, -49, 15); Place("Bright Fir", d, -8, -43);
            Place("Palisade", o, -13, -39, 90); Cluster(d, -12, -47); Place("Fir", d, 12, -43, 37); Cluster(d, 14, -35);
        }
        static void SouthwestCamp()
        {
            var o = Section(true, "B · Southwest camp"); var d = Section(false, "B · Camp supplies");
            Place("Watchtower", o, -11, -27); Place("Tent", o, -30, -26, -18); Place("Palisade", o, -25, -32); Place("Palisade", o, -35, -18, 90);
            Place("Palisade", o, -21, -5); Place("Palisade", o, -8, -17, 90);
            Place("Crate", d, -25, -26, 12); Place("Barrel", d, -23, -7); Cluster(d, -34, -11, true); Cluster(d, -15, -30);
        }
        static void CentralSanctuary()
        {
            var o = Section(true, "C · Ancient sanctuary"); var d = Section(false, "C · Sanctuary garden");
            Place("Stone Arch", o, 18, 5); var pool = Place("Green Pool", d, 18, -12);
            Place("Ruin", o, 29, 4, 25); Place("Ruin", o, 29, -12, 170); Place("Lantern", d, 8, -6, 165);
            Cluster(d, 26, -4); Cluster(d, 28, 11); Cluster(d, 26, -19, true); Cluster(d, 11, 12);
            for (int i = 0; i < 12; i++) Boulder(d, 8 + i % 3 * 2.2f, -15 + i / 3 * 5, .9f + i % 3 * .2f, "StonePurple");
            var recovery = New(level.transform, "Sanctuary recovery · clear grass west of pool"); recovery.SetPositionAndRotation(World(10, -12, .7f), Axis);
            level.recoveryPoints = new[] { level.playerSpawn, recovery };
            var sanctuary=pool.AddComponent<GreenwoodSanctuary>();sanctuary.session=level.session;
            Place("Fir", d, -29, -1, 33); Place("Bright Fir", d, -34, -10); Cluster(d, -33, 4);
        }
        static void Crossroads()
        {
            var o = Section(true, "D · Crossroads landmarks"); var d = Section(false, "D · Crossroads dressing");
            Place("Signpost", d, -5.8f, 27); Place("Ruin", o, 10, 16, 15); Place("Yellow Fir", d, 14.5f, 16); Cluster(d, 12, 13);
            Cluster(d, -27, 9); Place("Fir", d, -36, 15, 12); Place("Barrel", d, -36, 11);
        }
        static void WestDanger()
        {
            var o = Section(true, "E · West closed gate and danger arena"); var d = Section(false, "E · Bones and wreck dressing");
            Place("Closed West Gate", o, -48, 24, 90); Place("Blue Wreck", o, -32, 49, -8); Place("Blue Wreck", o, -20, 57, 35, .8f);
            Place("Skull Sign", d, -25, 32, 15); Place("Stakes", d, -39, 36, 0); Place("Stakes", d, -30, 42, 90);
            Place("Bones", d, -36, 29, 35); Place("Bones", d, -26, 36, -31); Place("Lantern", d, -42, 29, 170);
            var arena = New(d, "Warm champion clearing"); arena.position = World(-33, 35, .008f); Shape(arena, "Dusty arena", "Cylinder", "Road", Vector3.zero, new Vector3(11, .005f, 10)); Ring(arena, "Paver", 4.3f, .12f, .025f, false); Merge(arena, "Champion arena floor");
            Cluster(d, -40, 44); Place("Fir", d, -39, 54, 20, .9f); Place("Barrel", d, -33, 16);
        }
        static void NorthMarket()
        {
            var o = Section(true, "F · North gate and guide"); var d = Section(false, "F · Market supplies");
            Place("Crystal North Gate", o, 0, 65); Place("Covered Cart", o, -21, 54, 35); Place("Green Haired Guide", o, -12, 52);
            Place("Palisade", o, -18, 62); Place("Lantern", d, -6.5f, 58, 20); Place("Crate", d, -26, 54, 14); Place("Barrel", d, -16, 57); Place("Barrel", d, -14, 58); Place("Planks", d, -20, 49, 33); Place("Planks", d, -25, 58, 81); Cluster(d, -29, 61);
            // Large punctuation constructed from chunky geometry, no text texture or micro details.
            var q = New(d, "Guide question marker"); q.SetPositionAndRotation(World(-12, 52, 4.2f), Axis);
            for (int i = 0; i < 4; i++) { float a = i * 50 - 30; Box(q, "Lantern", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * .4f, .45f + Mathf.Cos(a * Mathf.Deg2Rad) * .4f, 0), new Vector3(.25f, .3f, .15f), new Vector3(0, 0, -a)); }
            Box(q, "Lantern", new Vector3(0, .05f, 0), new Vector3(.18f, .3f, .15f)); Box(q, "Lantern", new Vector3(0, -.4f, 0), Vector3.one * .23f); Merge(q, "Guide question marker");
        }
        static void NortheastCamp()
        {
            var o = Section(true, "G · Northeast camp and sealed entrance"); var d = Section(false, "G · Camp garden and supplies");
            Place("Watchtower", o, 13, 31); Place("Tent", o, 30, 30, 8); Place("Windmill", o, 15, 55); Place("Covered Cart", o, 28, 53, -20);
            Place("Palisade", o, 9, 42, 90); Place("Palisade", o, 22, 26); Place("Palisade", o, 9, 52, 90);
            Place("Sealed Cliff Door", o, 44, 37, 45); Place("Palisade", o, 38, 63);
            Place("Barrel", d, 20, 53); Place("Crate", d, 31, 49, 15); Cluster(d, 34, 29); Cluster(d, 9, 35, true); Cluster(d, 43, 43); Place("Lantern", d, 36, 34, 135);
            // The island leaves a 7+ m bypass between it and the divider cliffs.
            Place("Cliff", o, 36, 58, 41, .55f); Place("Fir", d, 36, 58, 22, .6f, 3.3f);
            Place("Fir", d, 46, 57, 10, .9f, 6.2f); Cluster(d, 41, 61);
        }
        static void BiomePreviews()
        {
            var d = Section(false, "Future biomes · visible beyond locked gates");
            var snow = New(d, "Snow biome glimpse"); snow.SetPositionAndRotation(World(0, 72, .008f), Axis); Box(snow, "Snow", Vector3.zero, new Vector3(25, .014f, 16)); Merge(snow, "Snow biome ground");
            for (int i = 0; i < 5; i++) Place("Snow Fir", d, -9 + i * 4.5f, 73 + i % 2 * 3, i * 29, .85f);
            Place("Green Haired Guide",d,-4,70,160,.8f); Place("Green Haired Guide",d,5,73,210,.8f);
            Place("Cactus",d,-52.5f,24,15,.7f);
            var orange = New(d, "Orange biome glimpse"); orange.SetPositionAndRotation(World(-58, 25, .008f), Axis); Box(orange, "OrangeGround", Vector3.zero, new Vector3(22, .014f, 28)); Merge(orange, "Orange biome ground");
            for (int i = 0; i < 6; i++) { Place("Cactus", d, -57 - i % 2 * 6, 12 + i * 4, i * 61); Boulder(d, -62 + i % 2 * 2, 15 + i * 4, 3.5f, "RedRock"); }
            // Outer cliff trees and occasional grass tufts are grouped, leaving arena centers clean.
            for (int i = 0; i < 10; i++) { float x = i % 2 == 0 ? -40 : 42, z = -23 + i * 8; Place("Fir", d, x, z, i * 31, Rand(.65f, 1)); Cluster(d, x - 1.2f, z - 2.8f, i % 4 == 0); }
            foreach (Transform section in level.decorationRoot) foreach (var t in section.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            foreach (Transform section in level.obstaclesRoot) foreach (var t in section.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        }
        static CombatTuning Tuning(string name, float hp, float speed, float aggro, float damage)
        {
            var t = Object.Instantiate(level.session.spawns.tuning); t.name = name; t.enemyHealth = hp; t.moveSpeed = speed; t.aggroRadius = aggro; t.meleeDamage = damage;
            AssetDatabase.CreateAsset(t, Root + "/Tuning/" + name + ".asset"); return t;
        }
        static GameObject RaiderVariant(string name, CombatTuning tuning, bool champion = false, bool sentinel = false)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoadGunner/Prefabs/Wasteland Raider.prefab");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(source); g.name = name; g.SetActive(true); var r = g.GetComponent<Raider>(); r.tuning = tuning;
            if (!sentinel) {
                foreach (var renderer in r.visual.GetComponentsInChildren<Renderer>()) {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0 ? mats["Skin"] : m.name.IndexOf("boot", StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("rubber", StringComparison.OrdinalIgnoreCase) >= 0 ? mats["Dark"] : mats["Cloth"]).ToArray();
                    string n=renderer.name.ToLowerInvariant();Material accent=n.Contains("crowbar")||n.Contains("scrap plate")?mats["Metal"]:n.Contains("head wrap")?mats["Red"]:n=="head"?mats["Skin"]:n.Contains("boot")?mats["Dark"]:null;
                    if(accent)renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>accent).ToArray();if(champion&&n.Contains("crowbar"))renderer.enabled=false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                if (champion) {
                    Box(r.leftArm, "Blue", new Vector3(0, -.35f, .15f), new Vector3(.9f, 1.1f, .24f));
                    Box(r.rightArm, "Metal", new Vector3(.05f, -.9f, .1f), new Vector3(.22f, 1.6f, .18f));
                    Box(r.visual, "Red", new Vector3(0, 1.35f, -.24f), new Vector3(.8f, .6f, .12f));
                }
            } else {
                var old = r.visual.gameObject; var visual = New(g.transform, "Tree sentinel · animated block silhouette"); r.visual = visual;
                r.torso = Shape(visual, "Broad bark trunk", "Rock", "WoodDark", new Vector3(0, 1, 0), new Vector3(2.2f, 2.6f, 1.5f)).GetComponent<Renderer>();
                r.leftLeg = New(visual, "Root left"); r.leftLeg.localPosition = new Vector3(-.6f, 1, 0); Box(r.leftLeg, "Wood", new Vector3(0, -.5f, 0), new Vector3(.6f, 1, .8f));
                r.rightLeg = New(visual, "Root right"); r.rightLeg.localPosition = new Vector3(.6f, 1, 0); Box(r.rightLeg, "Wood", new Vector3(0, -.5f, 0), new Vector3(.6f, 1, .8f));
                r.leftArm = New(visual, "Branch left"); r.leftArm.localPosition = new Vector3(-1.1f, 2.8f, 0); Box(r.leftArm, "Wood", new Vector3(-.3f, -.6f, 0), new Vector3(.55f, 1.6f, .55f), new Vector3(0, 0, -25));
                r.rightArm = New(visual, "Branch right"); r.rightArm.localPosition = new Vector3(1.1f, 2.8f, 0); Box(r.rightArm, "Wood", new Vector3(.3f, -.6f, 0), new Vector3(.55f, 1.6f, .55f), new Vector3(0, 0, 25));
                Shape(visual, "Tree crown", "Cone", "Fir", new Vector3(0, 3, 0), new Vector3(3.6f, 2.3f, 3));
                for (int s = -1; s <= 1; s += 2) Box(visual, "Cyan", new Vector3(s * .33f, 2.8f, .71f), new Vector3(.23f, .3f, .08f));
                Object.DestroyImmediate(old); r.hitbox.radius = .95f; r.hitbox.height = 4; r.hitbox.center = Vector3.up * 2; r.agent.radius = .85f; r.agent.height = 4;
            }
            foreach (var component in g.GetComponentsInChildren<Component>(true)) if (component) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            var prefab = PrefabUtility.SaveAsPrefabAsset(g, Root + "/Prefabs/" + name + ".prefab"); Object.DestroyImmediate(g); return prefab;
        }
        static void Enemy(GameObject prefab, string label, float x, float z, float yaw = 180)
        {
            var g = RoadGunnerLevelAuthoring.Place(level, prefab, World(x, z), Vector3.up, 45 + yaw, 1, false, false); g.name = label; PrefabUtility.RecordPrefabInstancePropertyModifications(g);
        }
        static void Populate()
        {
            var scout = RaiderVariant("Forest Scavenger", Tuning("Forest Scavenger", 45, 3.2f, 12, 6));
            var champion = RaiderVariant("West Champion", Tuning("West Champion", 180, 2.6f, 10, 14), true);
            var tree = RaiderVariant("Tree Sentinel", Tuning("Tree Sentinel", 240, 1.9f, 9, 18), false, true);
            Enemy(scout, "01 · Entry scout", 1, -34); Enemy(scout, "02 · Southwest guard", -23, -16); Enemy(scout, "03 · Southwest scavenger", -17, -18, 120);
            Enemy(scout, "04 · Clearing patrol", -18, 3); Enemy(scout, "05 · Clearing patrol", -14, 7); Enemy(scout, "06 · Clearing patrol", -22, 8);
            Enemy(champion, "07 · West sword and shield champion", -33, 35); Enemy(scout, "08 · West lookout", -24, 35);
            Enemy(scout, "09 · Northeast camp guard", 24, 40); Enemy(scout, "10 · Northeast scavenger", 29, 42); Enemy(scout, "11 · Northeast scavenger", 21, 45); Enemy(scout, "12 · Northeast scout", 20, 36);
            Enemy(tree, "13 · Tree sentinel in cliff pocket", 41, 56.5f);
            level.session.spawns.repeatAuthoredEnemies = false;
        }
        static void ConfigureGameplay()
        {
            RoadGunnerLevelAuthoring.PlacePlayer(level, World(0, -48), 45); var s = level.session; s.vehicle.transform.SetPositionAndRotation(level.playerSpawn.position, level.playerSpawn.rotation);
            var cam = s.follow.GetComponent<Camera>(); cam.orthographicSize = 13; cam.backgroundColor = Hex("A4BBA4");
            s.follow.Snap(); RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = Hex("D0E2E7"); RenderSettings.ambientEquatorColor = Hex("A9BC93"); RenderSettings.ambientGroundColor = Hex("718267"); RenderSettings.ambientIntensity = 1;
            foreach (var light in RoadGunnerLevelAuthoring.InScene<Light>()) if (light.type == LightType.Directional) { light.transform.rotation = Quaternion.Euler(48, -25, 0); light.color = Hex("FFF2D7"); light.intensity = 1.15f; light.shadows = LightShadows.Soft; }
            // Old desert grading is not shared with this scene: disable its scene-local volume.
            foreach (var volume in RoadGunnerLevelAuthoring.InScene<Volume>()) volume.enabled = false;
            RoadGunnerLevelAuthoring.Wire(level); level.navigationNeedsBake = true; RoadGunnerLevelAuthoring.Dirty(level);
        }
        public static string Inspect()
        {
            var l = RoadGunnerLevelAuthoring.Current; var notes = new List<string> { "Greenwood inspection · 2026-10-10", "Scene: " + ScenePath, "Screen north: world yaw 45°. Road 7.5 m; vehicle width approximately 2.2 m.", "Authored enemies: " + l.session.spawns.authoredRaiders.Length };
            int tris = 0; foreach (var f in l.authoredContentRoot.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh) tris += f.sharedMesh.triangles.Length / 3;
            notes.Add("Authored scene geometry: " + tris + " triangles, excluding ground / player / dormant resource pools.");
            notes.Add("Ground: " + l.ground.sharedMesh.triangles.Length / 3 + " triangles.");
            var prefabs=AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}).Select(id=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            notes.Add("Reusable environment prefabs: " + prefabs.Count(p=>!p.GetComponent<Raider>())+"; enemy variants: "+prefabs.Count(p=>p.GetComponent<Raider>()));
            foreach (var result in RoadGunnerLevelAuthoring.Validate(l)) notes.Add(result.type + ": " + result.text);
            File.WriteAllLines(Report + "/Inspection.txt", notes); return string.Join("\n", notes);
        }
        public static void Refine()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath) throw new InvalidOperationException("Open Greenwood in Edit Mode first.");
            level=RoadGunnerLevelAuthoring.Current; random=new System.Random(100926); mats.Clear();meshes.Clear();props.Clear();
            PreparePalette();PrepareMeshes();PrepareProps();
            var route=level.decorationRoot.Find("00 · Routes and clear ground");if(route)Object.DestroyImmediate(route.gameObject);Paths();
            foreach(var t in level.decorationRoot.GetComponentsInChildren<Transform>().Where(t=>t.name=="Bush"||t.name=="Yellow Bush").ToArray()){
                Vector3 local=Quaternion.Inverse(Axis)*t.position;Place("Grass Tuft",t.parent,local.x-.8f,local.z-1.2f);
            }
            var cliffs=level.obstaclesRoot.Find("01 · Rock boundaries and islands");Cliff(cliffs,-47,16,45,.55f,false);
            var future=level.decorationRoot.Find("Future biomes · visible beyond locked gates");Place("Green Haired Guide",future,-4,70,160,.8f);Place("Green Haired Guide",future,5,73,210,.8f);
            level.baselineTerrain.name="Terrain baseline";EditorUtility.SetDirty(level.baselineTerrain);
            RoadGunnerLevelAuthoring.Save(level);Inspect();
        }
        public static void FixPassability()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open Greenwood in Edit Mode first.");
            level=RoadGunnerLevelAuthoring.Current;mats.Clear();meshes.Clear();PreparePalette();PrepareMeshes();SealBoundary();
            var tree=level.session.spawns.authoredRaiders.First(r=>r.tuning.name=="Tree Sentinel");tree.transform.position=World(41,56.5f);PrefabUtility.RecordPrefabInstancePropertyModifications(tree.transform);
            RoadGunnerLevelAuthoring.Save(level);
        }
        public static void PolishForVehicle()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open Greenwood in Edit Mode first.");
            level=RoadGunnerLevelAuthoring.Current;random=new System.Random(100926);mats.Clear();meshes.Clear();props.Clear();PreparePalette();PrepareMeshes();
            foreach(var id in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"})){var g=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id));props[g.name]=g;}
            Action<Transform,float,float> move=(t,x,z)=>{t.position=World(x,z,t.position.y);PrefabUtility.RecordPrefabInstancePropertyModifications(t);};
            var boundaries=level.obstaclesRoot.Find("01 · Rock boundaries and islands");
            foreach(Transform t in boundaries){if(t.name!="Cliff")continue;var p=Quaternion.Inverse(Axis)*t.position;if(p.z>=64&&p.x>30)move(t,p.x,73);if(Mathf.Abs(p.x-43)<.2f&&Mathf.Abs(p.z-63)<.2f)move(t,43,70);}
            foreach(Transform t in boundaries){var p=Quaternion.Inverse(Axis)*t.position;if(t.name=="Cliff"&&((Mathf.Abs(p.x-37)<.2f&&Mathf.Abs(p.z-47)<.2f)||(Mathf.Abs(p.x-43)<.2f&&Mathf.Abs(p.z-49)<.2f))){if(p.z<48)move(t,40.5f,47);var scale=t.localScale;scale.y=.527f;t.localScale=scale;PrefabUtility.RecordPrefabInstancePropertyModifications(t);}}
            if(!boundaries.Cast<Transform>().Any(t=>Vector3.Distance(t.position,World(48,68))<.2f))Cliff(boundaries,48,68,28,1.15f);
            foreach(Transform section in level.decorationRoot)if(section.name.StartsWith("Cliff crown")){foreach(Transform t in section){var p=Quaternion.Inverse(Axis)*t.position;if(p.z>=64&&p.x>30)move(t,p.x,73.5f);if(Mathf.Abs(p.x-42.2f)<.2f&&Mathf.Abs(p.z-63.5f)<.2f)move(t,42.2f,70.5f);}}
            foreach(Transform section in level.decorationRoot)if(section.name.StartsWith("Cliff crown"))foreach(Transform t in section){var p=Quaternion.Inverse(Axis)*t.position;if((Mathf.Abs(p.x-36.2f)<.2f&&Mathf.Abs(p.z-47.5f)<.2f)||(Mathf.Abs(p.x-42.2f)<.2f&&Mathf.Abs(p.z-49.5f)<.2f)){t.position=World(p.z<48?39.7f:p.x,p.z,3.162f);t.localScale=Vector3.one*.6f;PrefabUtility.RecordPrefabInstancePropertyModifications(t);}}
            var camp=level.obstaclesRoot.Find("G · Northeast camp and sealed entrance");move(camp.Find("Cliff"),33,62);move(camp.Find("Watchtower"),12,31);
            move(camp.Find("Covered Cart"),23,56);
            foreach(Transform t in camp){var p=Quaternion.Inverse(Axis)*t.position;if(t.name=="Palisade"&&Mathf.Abs(p.x-38)<.2f)move(t,38,70);}
            var garden=level.decorationRoot.Find("G · Camp garden and supplies");foreach(Transform t in garden)if(t.name=="Fir"&&t.position.y<4)move(t,33,62);
            var sentinel=level.session.spawns.authoredRaiders.First(r=>r.tuning.name=="Tree Sentinel");move(sentinel.transform,41,58);
            move(level.obstaclesRoot.Find("B · Southwest camp").Find("Watchtower"),-9.5f,-26);
            move(level.decorationRoot.Find("A · Entry dressing").Find("Bright Fir"),-5.8f,-42);
            move(level.obstaclesRoot.Find("C · Ancient sanctuary").Find("Stone Arch"),12,5);
            move(level.decorationRoot.Find("C · Sanctuary garden").Find("Green Pool"),12,-12);
            var gardenDressing=level.decorationRoot.Find("C · Sanctuary garden");foreach(Transform t in gardenDressing.Cast<Transform>().ToArray()){var p=Quaternion.Inverse(Axis)*t.position;if(t.name=="Boulder"&&p.x>=7.5f&&p.x<=13&&p.z>=-17&&p.z<=1){if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(12,-12))<4.9f){Object.DestroyImmediate(t.gameObject);continue;}var scale=t.localScale;scale.y=.075f;t.localScale=scale;}}
            var pool=level.decorationRoot.Find("C · Sanctuary garden").Find("Green Pool");var sanctuary=pool.GetComponent<GreenwoodSanctuary>();if(!sanctuary)sanctuary=pool.gameObject.AddComponent<GreenwoodSanctuary>();sanctuary.session=level.session;
            move(level.recoveryPoints[1],6,-12);
            level.session.follow.GetComponent<Camera>().orthographicSize=16;
            foreach(var text in RoadGunnerLevelAuthoring.InScene<UnityEngine.UI.Text>()){
                if(text.text=="ROAD GUNNER  /  THE DUSTLANDS")text.text="LEVEL 01  /  GREENWOOD";
                if(text.text=="SCRAP RUSH  /  ROAD GUNNER")text.text="SCRAP RUSH  /  GREENWOOD";
            }
            var route=level.decorationRoot.Find("00 · Routes and clear ground");if(route)Object.DestroyImmediate(route.gameObject);Paths();SealBoundary();
            RoadGunnerLevelAuthoring.Save(level);Inspect();
        }
    }
}
