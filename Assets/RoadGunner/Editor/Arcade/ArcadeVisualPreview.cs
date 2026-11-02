using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Edit-mode look-dev renders for the RENDER stream (world only, through the real camera post stack):
    /// -executeMethod RoadGunner.Editor.ArcadeVisualPreview.RenderBatch [-rgPreviewLabel name] [-rgPreviewSize 720x1280].
    /// Never saves the scene; moves the car/camera only in memory. Writes outputs/visual-capture/&lt;label&gt;/*.png.
    /// </summary>
    public static class ArcadeVisualPreview
    {
        static readonly Quaternion View = Quaternion.Euler(50, 45, 0);

        /// <summary>One Unity launch: apply + save the RENDER installer, then render the look-dev frames.</summary>
        public static void ApplyAndRenderBatch()
        {
            ArcadeVisualInstaller.ApplyBatch();
            RenderBatch();
        }

        public static void RenderBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            string label = Argument("-rgPreviewLabel", "render-preview-" + DateTime.UtcNow.ToString("HHmmss"));
            var size = Argument("-rgPreviewSize", "720x1280").Split('x').Select(int.Parse).ToArray();
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "outputs", "visual-capture", label);
            Directory.CreateDirectory(folder);
            var camera = session.input && session.input.view ? session.input.view : Camera.main;
            if (!camera || !session.vehicle) throw new InvalidOperationException("Preview needs the game camera and vehicle.");
            var car = session.vehicle.transform;
            var shots = new (string name, Vector3 at)[]
            {
                ("start", session.startPosition), ("dust-flats", new Vector3(-65.4f, .5f, -65.4f)), ("workshop", new Vector3(0, .5f, -42.4f)),
                ("salvage-yard", new Vector3(-51.3f, .5f, 33.6f)), ("rustworks", new Vector3(86.6f, .5f, 1.8f)),
                ("ash-ridge", new Vector3(104.3f, .5f, 79.5f)), ("boss", new Vector3(.3f, .5f, 117f)),
            }.ToList();
            var station = RoadGunnerLevelAuthoring.InScene<ArenaUpgradeStation>().FirstOrDefault(x => x && x.kind == ArenaUpgradeKind.Attack);
            if (station) shots.Add(("station", station.transform.position + new Vector3(3, .5f, -3)));
            var raider = RoadGunnerLevelAuthoring.InScene<Raider>().FirstOrDefault(x => x && x.gameObject.activeInHierarchy && !x.arenaBoss);
            if (raider) shots.Add(("raiders", raider.transform.position + new Vector3(-3, .5f, -3)));
            var buggy = RoadGunnerLevelAuthoring.InScene<EnemyBuggy>().FirstOrDefault(x => x && x.gameObject.activeInHierarchy);
            if (buggy) shots.Add(("buggy", buggy.transform.position + new Vector3(-3.5f, .5f, -3.5f)));
            var mutant = RoadGunnerLevelAuthoring.InScene<WastelandMutant>().FirstOrDefault(x => x && x.gameObject.activeInHierarchy);
            if (mutant) shots.Add(("mutant", mutant.transform.position + new Vector3(-4, .5f, -4)));
            int index = 1;
            foreach (var shot in shots)
            {
                car.position = shot.at;
                Physics.SyncTransforms();
                camera.transform.SetPositionAndRotation(shot.at - View * Vector3.forward * 35 + Vector3.up * .7f, View);
                Shoot(camera, size[0], size[1], Path.Combine(folder, index.ToString("00") + "-" + shot.name + ".png"));
                index++;
            }
            // Isolation test: the same frame with an empty MaterialPropertyBlock on the hero body panels.
            car.position = session.startPosition;
            camera.transform.SetPositionAndRotation(session.startPosition - View * Vector3.forward * 35 + Vector3.up * .7f, View);
            var panels = session.health ? session.health.bodyPanels.Where(r => r).ToArray() : Array.Empty<Renderer>();
            var block = new MaterialPropertyBlock();
            foreach (var r in panels) r.SetPropertyBlock(block);
            Shoot(camera, size[0], size[1], Path.Combine(folder, (index++).ToString("00") + "-start-empty-mpb.png"));
            var palette = panels.Select(r => r.sharedMaterial).FirstOrDefault(m => m);
            if (palette && palette.enableInstancing)
            {
                palette.enableInstancing = false;
                Shoot(camera, size[0], size[1], Path.Combine(folder, (index++).ToString("00") + "-start-empty-mpb-no-instancing.png"));
                palette.enableInstancing = true;
            }
            block.SetColor("_BaseColor", Color.white);
            foreach (var r in panels) r.SetPropertyBlock(block);
            Shoot(camera, size[0], size[1], Path.Combine(folder, (index++).ToString("00") + "-start-white-mpb.png"));
            foreach (var r in panels) r.SetPropertyBlock(null);
            Debug.Log("ARCADE PREVIEW OK " + folder);
        }

        static void Shoot(Camera camera, int width, int height, string path)
        {
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 2, sRGB = true };
            var target = RenderTexture.GetTemporary(descriptor);
            var readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect;
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
                File.WriteAllBytes(path, readback.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(readback);
            }
        }

        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
