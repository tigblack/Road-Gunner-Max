#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in visual review fixture: stages real gameplay moments in the saved arena and renders portrait screenshots.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class VisualCaptureShots : MonoBehaviour
    {
        public const string OutputRoot = "outputs/visual-capture";
        public bool Completed { get; private set; }
        public int Captured { get; private set; }
        public int Failures { get; private set; }
        GameSession s;
        string folder, filter;
        int width, height, index;
        readonly List<string> manifest = new List<string>();

        public void Begin(string outputFolder, int w, int h, string shotFilter)
        {
            s = GetComponent<GameSession>(); folder = outputFolder; width = w; height = h; filter = shotFilter ?? "";
            Directory.CreateDirectory(folder);
            // WebGL and phones ship with the Mobile quality level (Mobile_RPAsset / Mobile_Renderer); preview that path.
            int mobile = Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);
            manifest.Add("Road Gunner visual capture • Unity " + Application.unityVersion + " • " + w + "x" + h + " • quality " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Write(); StartCoroutine(Run());
        }

        bool Wanted(string name) => filter.Length == 0 || filter.Split(',').Any(f => f.Length > 0 && name.StartsWith(f.Trim()));

        IEnumerator Run()
        {
            yield return null; yield return null;
            var steps = new (string name, Func<IEnumerator> body)[]
            {
                ("start", StartShot), ("drive", DriveShot), ("combat", CombatShot), ("station", StationShot),
                ("workshop", WorkshopShot), ("region", RegionShots), ("mutant", MutantShot), ("boss", BossShot),
                ("pause", PauseShot), ("defeat", DefeatShot), ("victory", VictoryShot),
            };
            foreach (var step in steps)
            {
                if (!Wanted(step.name)) continue;
                var stack = new Stack<IEnumerator>(); stack.Push(step.body());
                while (stack.Count > 0)
                {
                    IEnumerator part = stack.Peek(); bool more = false; object current = null;
                    try { more = part.MoveNext(); if (more) current = part.Current; }
                    catch (Exception error) { Failures++; manifest.Add("FAIL step " + step.name + ": " + error); Write(); stack.Clear(); break; }
                    if (!more) { stack.Pop(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested); else yield return current;
                }
                ResetWorld();
                yield return null;
            }
            ResetWorld();
            Completed = true;
        }

        // ---------- shots ----------

        IEnumerator StartShot()
        {
            s.Restart();
            yield return Settle(1.4f);
            Capture("start");
        }

        IEnumerator DriveShot()
        {
            s.Restart(); s.health.DamageEnabled = false;
            Vector3 forward = s.startRotation * Vector3.forward;
            s.input.testInput = true; s.input.testBrake = false; s.input.testDirection = ScreenDirection(forward);
            yield return new WaitForSecondsRealtime(1.2f);
            s.input.testDirection = ScreenDirection(Quaternion.Euler(0, 35, 0) * forward);
            yield return new WaitForSecondsRealtime(1.1f);
            Capture("drive-turn");
            s.input.testDirection = ScreenDirection(forward);
            yield return new WaitForSecondsRealtime(1.0f);
            Capture("drive-fast");
        }

        IEnumerator CombatShot()
        {
            Isolate();
            s.turret.enabled = true;
            Vector3 origin = s.startPosition; Quaternion heading = s.startRotation;
            var raiders = s.spawns.pool.Where(r => r).Take(3).ToArray();
            var buggy = s.spawns.buggyPool.FirstOrDefault(b => b);
            int placed = 0;
            for (int i = 0; i < 16 && placed < raiders.Length + 1; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 47f, 0) * (heading * Vector3.forward);
                if (!NavMesh.SamplePosition(origin + direction * (5.5f + (i % 3)), out var hit, 1.2f, NavMesh.AllAreas)) continue;
                if (placed < raiders.Length) { raiders[placed].Spawn(hit.position, -1, placed); raiders[placed].AIEnabled = true; }
                else if (buggy)
                {
                    buggy.Spawn(hit.position, Quaternion.LookRotation(-direction), -1); buggy.AIEnabled = false;
                    buggy.gun.CanShoot = true; buggy.Body.constraints = RigidbodyConstraints.FreezeAll;
                }
                placed++;
            }
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.9f);
            for (int i = 0; i < 3; i++) { yield return WaitForTracer(); Capture("combat-" + (char)('a' + i)); yield return new WaitForSecondsRealtime(.21f); }
            if (buggy && buggy.Alive)
            {
                buggy.Hit(buggy.MaxHealth * 10, heading * Vector3.forward);
                yield return new WaitForSecondsRealtime(.09f); Capture("explosion-early");
                yield return new WaitForSecondsRealtime(.3f); Capture("explosion-late");
            }
            foreach (var r in raiders) if (r && r.Alive) r.Hit(r.MaximumHealth + 1, (r.transform.position - origin).normalized);
            yield return new WaitForSecondsRealtime(.35f); Capture("raider-deaths");
            yield return new WaitForSecondsRealtime(.6f); Capture("loot");
        }

        IEnumerator StationShot()
        {
            Isolate();
            var station = s.runStats && s.runStats.stations.Length > 0 ? s.runStats.stations[0] : null;
            if (!station) yield break;
            Place(Ground(station.transform.position + Vector3.right * (station.radius + 3)) + Vector3.up * .7f, s.startRotation);
            yield return Settle(.5f);
            Capture("station");
            Place(Ground(station.transform.position) + Vector3.up * .7f, s.startRotation);
            yield return new WaitForSecondsRealtime(.35f);
            Capture("station-upgrade-toast");
        }

        IEnumerator WorkshopShot()
        {
            Isolate();
            var workshop = s.minimap ? s.minimap.workshops.FirstOrDefault(w => w) : null;
            if (!workshop) yield break;
            s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum * .45f, false); s.health.DamageEnabled = false;
            Place(Ground(workshop.transform.position) + Vector3.up * .7f, s.startRotation);
            yield return Settle(1.0f);
            Capture("workshop");
        }

        IEnumerator RegionShots()
        {
            if (!s.exploration || s.exploration.zones == null) yield break;
            foreach (var zone in s.exploration.zones)
            {
                Isolate();
                if (s.exploration) s.exploration.enabled = true;
                Vector3 p = zone.center;
                if (NavMesh.SamplePosition(p, out var hit, 12, NavMesh.AllAreas)) p = hit.position;
                Place(Ground(p) + Vector3.up * .7f, s.startRotation);
                yield return Settle(.8f);
                Capture("region-" + zone.id);
            }
        }

        IEnumerator MutantShot()
        {
            Isolate();
            var mutant = s.zone1 ? s.zone1.bosses.OfType<WastelandMutant>().FirstOrDefault() : null;
            if (!mutant) yield break;
            s.zone1.ResetEncounters();
            foreach (var t in s.zone1.bosses) if (t is Raider r) r.AIEnabled = false;
            Vector3 p = mutant.transform.position + mutant.transform.forward * 5.5f;
            if (NavMesh.SamplePosition(p, out var hit, 4, NavMesh.AllAreas)) p = hit.position;
            Place(Ground(p) + Vector3.up * .7f, Quaternion.LookRotation(Flat(mutant.transform.position - p)));
            mutant.AIEnabled = true; s.turret.enabled = true;
            float deadline = Time.realtimeSinceStartup + 3;
            while (mutant.State != MutantState.Telegraph && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.3f);
            Capture("mutant");
        }

        IEnumerator BossShot()
        {
            Isolate();
            var boss = s.zone1 ? s.zone1.bosses.OfType<Raider>().FirstOrDefault(r => r.arenaBoss) : null;
            if (!boss) yield break;
            s.zone1.ResetEncounters();
            foreach (var t in s.zone1.bosses) if (t is WastelandMutant m) m.AIEnabled = false;
            Vector3 attackPosition = Ground(boss.transform.position + boss.transform.forward * 4.6f) + Vector3.up * .7f;
            Place(attackPosition, Quaternion.LookRotation(-boss.transform.forward));
            boss.AIEnabled = true; s.turret.enabled = true;
            float deadline = Time.realtimeSinceStartup + 3;
            while (!boss.IsTelegraphing && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.35f);
            Capture("boss-telegraph");
        }

        IEnumerator PauseShot()
        {
            s.Restart();
            yield return Settle(.4f);
            s.SetPaused(true);
            yield return Frames();
            Capture("pause");
            s.SetPaused(false);
        }

        IEnumerator DefeatShot()
        {
            s.Restart(); yield return Settle(.4f);
            s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum + 1, false);
            yield return Frames(); yield return new WaitForSecondsRealtime(.4f);
            Capture("defeat");
        }

        IEnumerator VictoryShot()
        {
            Isolate();
            var boss = s.zone1 ? s.zone1.bosses.OfType<Raider>().FirstOrDefault(r => r.arenaBoss) : null;
            if (!boss) yield break;
            s.zone1.ResetEncounters();
            s.AddScrap(14);
            boss.Hit(boss.MaximumHealth + 1, Vector3.forward); boss.Hit(boss.MaximumHealth + 1, Vector3.forward);
            yield return Frames(); yield return new WaitForSecondsRealtime(.5f);
            Capture("victory");
        }

        // ---------- staging helpers ----------

        void Isolate()
        {
            Time.timeScale = 1;
            s.Restart(); s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
            foreach (var r in s.spawns.pool) if (r) r.Despawn();
            foreach (var b in s.spawns.buggyPool) if (b) b.Despawn();
            if (s.zone1) s.zone1.DespawnAll();
            s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
        }

        void ResetWorld()
        {
            if (!s) return;
            Time.timeScale = 1;
            foreach (var r in s.spawns.pool) if (r) r.AIEnabled = true;
            foreach (var b in s.spawns.buggyPool) if (b) { b.AIEnabled = true; if (b.Body) b.Body.constraints = RigidbodyConstraints.None; }
            if (s.zone1) foreach (var t in s.zone1.bosses) { if (t is Raider r) r.AIEnabled = true; else if (t is WastelandMutant m) m.AIEnabled = true; }
            if (s.exploration) s.exploration.enabled = true;
            s.spawns.enabled = true; s.turret.enabled = true; s.health.DamageEnabled = true;
            s.input.testInput = false; s.input.Clear();
            s.Restart();
        }

        void Place(Vector3 p, Quaternion rotation) { s.vehicle.ResetPose(p, rotation); Physics.SyncTransforms(); s.follow.Snap(); }

        Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 100, Vector3.down, out var hit, 250, 1, QueryTriggerInteraction.Ignore)) return hit.point;
            return p;
        }

        Vector2 ScreenDirection(Vector3 world)
        {
            var view = s.input.view;
            Vector3 right = Vector3.ProjectOnPlane(view.transform.right, Vector3.up).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            return new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)).normalized;
        }

        IEnumerator Settle(float seconds) { yield return Frames(); yield return new WaitForSecondsRealtime(seconds); }
        IEnumerator Frames() { yield return null; yield return null; yield return null; }

        IEnumerator WaitForTracer()
        {
            float deadline = Time.realtimeSinceStartup + .6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (s.effects && s.effects.tracers != null && s.effects.tracers.Any(t => t && t.enabled)) yield break;
                yield return null;
            }
        }

        static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up);

        sealed class CanvasState { public Canvas canvas; public RenderMode mode; public Camera camera; public float plane; }

        void Capture(string name)
        {
            var camera = s.input.view;
            string file = folder + "/" + (++index).ToString("00") + "-" + name + ".png";
            var msaa = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = true });
            var resolved = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = true });
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; float previousAspect = camera.aspect;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, plane = c.planeDistance }).ToArray();
            float began = Time.realtimeSinceStartup;
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = msaa;
                foreach (var state in canvases)
                {
                    state.canvas.renderMode = RenderMode.ScreenSpaceCamera; state.canvas.worldCamera = camera;
                    state.canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1);
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = msaa });
                Graphics.Blit(msaa, resolved);
                RenderTexture.active = resolved; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                byte[] png = texture.EncodeToPNG();
                File.WriteAllBytes(file, png);
                Captured++;
                if (png.Length < 5000) { Failures++; manifest.Add("FAIL tiny capture " + file); }
                manifest.Add("SHOT " + file + " | car=" + s.vehicle.transform.position.ToString("F1") + " | region=" + (s.exploration ? s.exploration.CurrentRegionId : "") +
                    " | live enemies=" + s.spawns.ActiveCount + " | shots=" + s.turret.ShotsFired + " | " + ((Time.realtimeSinceStartup - began) * 1000).ToString("F0") + " ms");
            }
            catch (Exception error) { Failures++; manifest.Add("FAIL capture " + name + ": " + error); }
            finally
            {
                foreach (var state in canvases) if (state.canvas)
                {
                    state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.plane;
                }
                camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(msaa); RenderTexture.ReleaseTemporary(resolved); Destroy(texture); Canvas.ForceUpdateCanvases();
                Write();
            }
        }

        void Write() { File.WriteAllLines(folder + "/CAPTURES.txt", manifest); }
    }
}
#endif
