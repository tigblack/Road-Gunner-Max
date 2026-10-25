using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>One collision policy for scene migration and future Level Editor placements.</summary>
    public static class GameplayObstacleAuthoring
    {
        static readonly string[] BlockingNames = {
            "boulder", "rock", "sandstone", "cliff", "wall", "fence", "barricade", "barrier",
            "wreck", "pickup", "container", "concrete", "scrap pile", "tire stack", "fuel drum",
            "supply crate", "storage crate", "closed scrap gate", "open scrap gate", "bunker", "lookout"
        };
        static readonly string[] DressingNames = {
            "scrub", "grass", "vegetation", "cactus", "pebble", "route sign", "danger sign",
            "utility pole", "scrap stakes", "shoulder composition", "shoulder ·"
        };

        public static bool ShouldBlock(GameObject obj, bool decoration = false)
        {
            if (!obj || IsGameplayObject(obj)) return false;
            if (ShoulderIndex(obj, out int composition)) return ShoulderPieces(composition).Any(p => Substantial(obj, p));
            var source = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            string name = (obj.name + " " + (source ? source.name : "")).ToLowerInvariant();
            if (DressingNames.Any(name.Contains)) return false;
            bool recognized = BlockingNames.Any(name.Contains);
            if (!TryVisualBounds(obj, out var local)) return !decoration;
            Vector3 world = Vector3.Scale(local.size, Abs(obj.transform.lossyScale));
            // A pebble-sized rock remains dressing, even when it shares the large boulder's prefab.
            bool substantial = world.y >= .5f && Mathf.Max(world.x, world.z) >= .8f &&
                Mathf.Min(world.x, world.z) >= .16f;
            return substantial && (recognized || !decoration);
        }

        static bool IsGameplayObject(GameObject obj) => obj.GetComponentInChildren<CombatTarget>(true) ||
            obj.GetComponentInChildren<VehicleController>(true) || obj.GetComponentInChildren<ArenaUpgradeStation>(true) ||
            obj.GetComponentInChildren<RepairWorkshop>(true);

        /// <returns>Whether this prop is a physical gameplay blocker.</returns>
        public static bool Configure(GameObject obj, bool decoration = false)
        {
            if (!obj || IsGameplayObject(obj)) return false;
            if (ShoulderIndex(obj, out int composition)) return ConfigureShoulder(obj, composition);
            bool blocking = ShouldBlock(obj, decoration);
            var colliders = obj.GetComponentsInChildren<Collider>(true);
            if (!blocking)
            {
                foreach (var collider in colliders)
                {
                    Undo.RecordObject(collider, "Keep small dressing nonblocking");
                    collider.enabled = false; Record(collider);
                }
                return false;
            }
            foreach (var child in obj.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(child.gameObject, "Use solid environment collision layer");
                child.gameObject.layer = 0; child.gameObject.isStatic = true; Record(child.gameObject);
            }
            // Existing compound shapes are intentional: never fill the opening of an open gate/gantry.
            var solids = colliders.Where(c => !c.isTrigger && !(c is WheelCollider)).ToArray();
            if (solids.Length == 0)
            {
                if (!TryVisualBounds(obj, out var bounds))
                    throw new InvalidOperationException("Gameplay obstacle has no visual bounds: " + obj.name);
                var box = Undo.AddComponent<BoxCollider>(obj);
                box.center = bounds.center;
                box.size = new Vector3(Mathf.Max(.16f, bounds.size.x), Mathf.Max(.5f, bounds.size.y), Mathf.Max(.16f, bounds.size.z));
                solids = new Collider[] { box };
            }
            foreach (var collider in solids)
            {
                Undo.RecordObject(collider, "Enable gameplay obstacle collision");
                collider.enabled = true; collider.isTrigger = false;
                collider.includeLayers = 0; collider.excludeLayers = 0;
                Record(collider);
            }
            return true;
        }

        /// <summary>Upgrade existing direct prop instances without modifying their source prefabs.</summary>
        public static string ConfigureLevel(LevelLayout layout)
        {
            if (!layout || !layout.obstaclesRoot || !layout.decorationRoot)
                throw new InvalidOperationException("Gameplay obstacle and decoration roots are required.");
            int blocking = 0, decoration = 0, added = 0;
            var instances = new[] { layout.obstaclesRoot, layout.decorationRoot }
                .SelectMany(PropInstances).Distinct().Select(child => (child, dressing: child.IsChildOf(layout.decorationRoot))).ToArray();
            foreach (var instance in instances)
            {
                var child = instance.child;
                var obj = child.gameObject;
                if (IsGameplayObject(obj)) continue;
                int before = obj.GetComponentsInChildren<Collider>(true).Length;
                bool solid = Configure(obj, instance.dressing);
                added += obj.GetComponentsInChildren<Collider>(true).Length - before;
                var parent = solid ? layout.obstaclesRoot : layout.decorationRoot;
                if (!child.IsChildOf(parent)) Undo.SetTransformParent(child, parent, "Classify gameplay blocker");
                if (solid) blocking++; else decoration++;
            }
            layout.navigationNeedsBake = true; RoadGunnerLevelAuthoring.Dirty(layout);
            return "Obstacle collision policy: " + blocking + " solid props, " + added +
                " fitted primitive colliders added; " + decoration + " small/decorative props remain nonblocking.";
        }

        /// <summary>Recurse organizational folders, but keep each compound prefab as one authored prop.</summary>
        public static IEnumerable<Transform> PropInstances(Transform root)
        {
            if (!root) yield break;
            foreach (Transform child in root)
            {
                if (child.GetComponent<CombatTarget>() || child.GetComponent<VehicleController>() || child.GetComponent<RepairWorkshop>()) continue;
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) || child.GetComponent<MeshFilter>() || child.GetComponent<Collider>())
                    yield return child;
                else foreach (var prop in PropInstances(child)) yield return prop;
            }
        }

        struct Piece
        {
            public string path, name; public Vector3 position; public float scale, yaw;
        }
        static bool ShoulderIndex(GameObject obj, out int index)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(obj);
            string name = source ? source.name : obj.name;
            const string prefix = "Shoulder composition "; index = -1;
            return name.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(name.Substring(prefix.Length), out index) && index >= 0 && index < 8;
        }
        static IEnumerable<Piece> ShoulderPieces(int index)
        {
            // These are the exact substantial pieces in ConceptDressing.Groups, before its meshes were
            // merged and source colliders stripped. Never fit one box around empty space in the whole group.
            const string art = "Assets/RoadGunner/Art/ConceptWasteland/Prefabs/";
            const string old = "Assets/RoadGunner/Levels/WastelandRestored/Prefabs/";
            if (index < 5)
            {
                yield return new Piece { name = "Boulder", path = art + "Boulder.prefab", position = new Vector3(.63f, 0, .30f), scale = .38f + index % 3 * .10f, yaw = index * 37 };
                // The remaining .18-.28-scale stones and scrub/cactus are decorative debris.
            }
            else if (index == 5)
                yield return new Piece { name = "Tire stack", path = old + "Tire stack.prefab", position = new Vector3(.52f, 0, .13f), scale = .80f, yaw = 12 };
            else if (index == 6)
                yield return new Piece { name = "Fuel drums", path = old + "Fuel drums.prefab", position = new Vector3(.68f, 0, .17f), scale = .72f, yaw = 98 };
            else
                yield return new Piece { name = "Supply crate", path = art + "Wooden supply crate.prefab", position = new Vector3(.62f, 0, .10f), scale = .85f, yaw = 17 };
        }
        static bool Substantial(GameObject obj, Piece piece)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(piece.path);
            if (!source || !TryVisualBounds(source, out var bounds)) return false;
            var size = Vector3.Scale(bounds.size * piece.scale, Abs(obj.transform.lossyScale));
            return size.y >= .5f && Mathf.Max(size.x, size.z) >= .8f && Mathf.Min(size.x, size.z) >= .16f;
        }
        static bool ConfigureShoulder(GameObject obj, int index)
        {
            int count = 0;
            var generated = obj.transform.Find("Gameplay solid pieces");
            if (!generated)
            {
                var group = new GameObject("Gameplay solid pieces"); Undo.RegisterCreatedObjectUndo(group, "Add shoulder-piece collision");
                generated = group.transform; generated.SetParent(obj.transform, false);
            }
            foreach (var collider in generated.GetComponentsInChildren<Collider>(true))
            { Undo.RecordObject(collider, "Refresh shoulder-piece collision"); collider.enabled = false; Record(collider); }
            foreach (var piece in ShoulderPieces(index))
            {
                if (!Substantial(obj, piece)) continue;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(piece.path);
                TryVisualBounds(source, out var bounds);
                var child = generated.Find(piece.name);
                if (!child)
                {
                    var go = new GameObject(piece.name); Undo.RegisterCreatedObjectUndo(go, "Add fitted shoulder collider");
                    child = go.transform; child.SetParent(generated, false);
                }
                Undo.RecordObject(child, "Fit original shoulder piece");
                child.localPosition = piece.position; child.localRotation = Quaternion.Euler(0, piece.yaw, 0); child.localScale = Vector3.one;
                var box = child.GetComponent<BoxCollider>(); if (!box) box = Undo.AddComponent<BoxCollider>(child.gameObject);
                Undo.RecordObject(box, "Fit substantial shoulder piece");
                box.center = bounds.center * piece.scale; box.size = bounds.size * piece.scale;
                box.enabled = true; box.isTrigger = false; box.includeLayers = 0; box.excludeLayers = 0;
                child.gameObject.layer = 0; child.gameObject.isStatic = true;
                Record(child); Record(box); Record(child.gameObject); count++;
            }
            generated.gameObject.layer = 0; generated.gameObject.isStatic = true; Record(generated.gameObject);
            return count > 0;
        }

        public static bool TryVisualBounds(GameObject obj, out Bounds bounds)
        {
            bool found = false; bounds = default;
            foreach (var mesh in obj.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || !mesh.gameObject.activeSelf) continue;
                var renderer = mesh.GetComponent<Renderer>();
                if (renderer && !renderer.enabled) continue;
                var matrix = obj.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                var b = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
                }
            }
            return found;
        }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static void Record(UnityEngine.Object obj)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            EditorUtility.SetDirty(obj);
        }
    }
}
