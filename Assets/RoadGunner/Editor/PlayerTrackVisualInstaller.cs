using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Narrow player-only visual authoring. Does not run a world/art installer, unpack the
    /// player prefab, rebake navigation/minimap, change physical contacts or save a prefab asset.
    /// Generated assets and the two child meshes are owned exclusively by this installer.
    /// </summary>
    public static class PlayerTrackVisualInstaller
    {
        public const string ScenePath = "Assets/RoadGunner/Scenes/Level01_Greenwood.unity";
        public const string AssetRoot = "Assets/RoadGunner/Art/PlayerTracks";
        public const string RootName = "Player tracked visuals";

        [MenuItem("Tools/Road Gunner/Player/Install Tracked Visuals Only")]
        public static void InstallBatch()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install tracks from compiled Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene before installing player tracks.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var session = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameSession>(true)).Single();
            Apply(session);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("PLAYER_TRACK_VISUALS_INSTALL_OK " + ScenePath + "; only player visual overrides and " + AssetRoot + " assets saved.");
        }

        /// <summary>For a coordinated installer: applies to the supplied saved-scene player; caller owns scene saving.</summary>
        public static PlayerTrackVisuals Apply(GameSession session)
        {
            if (!session || !session.vehicle) throw new InvalidOperationException("No existing player GameSession/VehicleController.");
            var vehicle = session.vehicle;
            if (vehicle.wheels == null || vehicle.wheels.Length != 4 || vehicle.wheels.Any(w => !w) || vehicle.wheels.Distinct().Count() != 4)
                throw new InvalidOperationException("Tracked visuals retain exactly four distinct existing WheelColliders.");
            if (vehicle.wheelVisuals == null || vehicle.wheelVisuals.Length != 4 || vehicle.wheelVisuals.Any(w => !w))
                throw new InvalidOperationException("The four existing wheel visual roots are required and will be retained.");
            if ((vehicle.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                throw new InvalidOperationException("Player track dimensions require the existing metre-scale player root.");

            var wheels = vehicle.wheels.ToArray();
            var colliders = vehicle.GetComponentsInChildren<Collider>(true);
            var oldRenderers = vehicle.GetComponentsInChildren<Renderer>(true);
            var oldVisibility = oldRenderers.Select(r => r.enabled).ToArray();
            var hidden = vehicle.wheelVisuals.SelectMany(t => t.GetComponentsInChildren<MeshRenderer>(true)).Distinct().Cast<Renderer>().ToArray();
            if (hidden.Length < 4) throw new InvalidOperationException("Cannot identify the player's four ordinary tire meshes.");

            EnsureFolder(AssetRoot);
            Material material = TrackMaterial();
            var root = Child(vehicle.transform, RootName);
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
            var visuals = root.GetComponent<PlayerTrackVisuals>();
            if (!visuals) visuals = Undo.AddComponent<PlayerTrackVisuals>(root.gameObject);
            visuals.enabled = true; visuals.vehicle = vehicle; visuals.session = session;
            visuals.treadsPerSide = 44; visuals.trackWidth = .64f; visuals.shoeThickness = .074f;
            visuals.grouserHeight = .045f; visuals.returnSag = .045f;

            // Classify by actual player-local coordinates, independent of legacy array ordering and world yaw.
            var bySide = wheels.OrderBy(w => vehicle.transform.InverseTransformPoint(w.transform.position).x).ToArray();
            var left = bySide.Take(2).OrderBy(w => vehicle.transform.InverseTransformPoint(w.transform.position).z).ToArray();
            var right = bySide.Skip(2).OrderBy(w => vehicle.transform.InverseTransformPoint(w.transform.position).z).ToArray();
            if (LocalX(left[1]) >= -.1f || LocalX(right[0]) <= .1f)
                throw new InvalidOperationException("Expected two original wheel contacts on each side of the player.");
            visuals.rearLeft = left[0]; visuals.frontLeft = left[1];
            visuals.rearRight = right[0]; visuals.frontRight = right[1];
            visuals.leftMesh = SideMesh(root, "Left track assembly", material, vehicle.gameObject.layer);
            visuals.rightMesh = SideMesh(root, "Right track assembly", material, vehicle.gameObject.layer);
            visuals.hiddenWheelRenderers = hidden;
            root.gameObject.layer = vehicle.gameObject.layer;

            visuals.leftMesh.sharedMesh = PersistMesh(visuals.CreatePreviewMesh(true), AssetRoot + "/Player left track.asset");
            visuals.rightMesh.sharedMesh = PersistMesh(visuals.CreatePreviewMesh(false), AssetRoot + "/Player right track.asset");
            // Keep all transforms and functional references. Hide rendering only, never deactivate a wheel root.
            foreach (var renderer in hidden)
            {
                renderer.enabled = false; EditorUtility.SetDirty(renderer);
                if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            visuals.ResetMotion();
            EditorUtility.SetDirty(visuals);
            EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);

            if (!vehicle.wheels.SequenceEqual(wheels) || !vehicle.GetComponentsInChildren<Collider>(true).SequenceEqual(colliders))
                throw new InvalidOperationException("Track authoring unexpectedly changed the original physical contacts.");
            for (int i = 0; i < oldRenderers.Length; i++)
                if (!hidden.Contains(oldRenderers[i]) && !oldRenderers[i].transform.IsChildOf(root) && oldRenderers[i].enabled != oldVisibility[i])
                    throw new InvalidOperationException("Track authoring unexpectedly changed body/crew/weapon visibility.");
            if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Visual track assemblies must have no decorative colliders.");
            Debug.Log("PLAYER_TRACK_VISUALS_OK sides=" + visuals.SideAssemblyCount + "; shoes=" + visuals.TreadsPerSide * 2 +
                      "; lower rollers=" + visuals.SupportRollersPerSide * 2 + "; shared materials=1; dynamic render meshes=2; preview vertices=" +
                      (visuals.leftMesh.sharedMesh.vertexCount + visuals.rightMesh.sharedMesh.vertexCount) +
                      "; original wheel contacts=4; added colliders=0; hidden tire renderers=" + hidden.Length);
            return visuals;

            float LocalX(WheelCollider w) => vehicle.transform.InverseTransformPoint(w.transform.position).x;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child) return child;
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Player tracked visuals");
            go.transform.SetParent(parent, false); return go.transform;
        }

        static MeshFilter SideMesh(Transform parent, string name, Material material, int layer)
        {
            var child = Child(parent, name);
            child.localPosition = Vector3.zero; child.localRotation = Quaternion.identity; child.localScale = Vector3.one;
            child.gameObject.layer = layer;
            var filter = child.GetComponent<MeshFilter>(); if (!filter) filter = Undo.AddComponent<MeshFilter>(child.gameObject);
            var renderer = child.GetComponent<MeshRenderer>(); if (!renderer) renderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
            renderer.enabled = true; renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer);
            return filter;
        }

        static Mesh PersistMesh(Mesh generated, string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) { EditorUtility.CopySerialized(generated, saved); Object.DestroyImmediate(generated); EditorUtility.SetDirty(saved); return saved; }
            AssetDatabase.CreateAsset(generated, path); return generated;
        }

        static Material TrackMaterial()
        {
            var shader = Shader.Find("RoadGunner/Arcade Toon");
            if (!shader) throw new InvalidOperationException("The saved Arcade Toon shader is required for player tracks.");
            var color = Palette(false); var response = Palette(true);
            string path = AssetRoot + "/Player tracks Arcade Toon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoadGunner/Materials/Gunmetal.mat");
                material = template ? new Material(template) : new Material(shader);
                material.name = "Player tracks Arcade Toon"; AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader; material.enableInstancing = true;
            material.SetTexture("_BaseMap", color); material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", response); material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION"); material.SetFloat("_WreckAmount", 0);
            EditorUtility.SetDirty(material); return material;
        }

        static Texture2D Palette(bool response)
        {
            string path = AssetRoot + (response ? "/Player track surface response.asset" : "/Player track palette.asset");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture)
            {
                texture = new Texture2D(8, 1, TextureFormat.RGBA32, false, response) { name = response ? "Track surface response" : "Track arcade palette" };
                AssetDatabase.CreateAsset(texture, path);
            }
            Color[] colors;
            if (response)
            {
                float[] metallic = { .02f, .65f, .8f, .2f, .5f, .65f, .5f, .1f };
                float[] smoothness = { .08f, .42f, .58f, .23f, .5f, .4f, .28f, .16f };
                colors = Enumerable.Range(0, 8).Select(i => new Color(metallic[i], 0, 0, smoothness[i])).ToArray();
            }
            else colors = new[] { Hex("27312F"), Hex("63756D"), Hex("B4C3AE"), Hex("347D72"), Hex("DAA852"), Hex("D4C8A0"), Hex("384A49"), Hex("9C7851") };
            texture.SetPixels(colors); texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, false); EditorUtility.SetDirty(texture); return texture;
        }

        static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out Color color); return color; }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
