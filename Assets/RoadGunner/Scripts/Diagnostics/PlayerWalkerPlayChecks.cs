#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace RoadGunner
{
    /// <summary>Actual saved walker, input-driven gait and footprint acceptance; no saved assets modified.</summary>
    public sealed class PlayerWalkerPlayChecks : MonoBehaviour
    {
        public const string Folder = "Documentation/RoadGunner/PlayerWalkerInstant-2026-11-06";
        public const string ReportPath = Folder + "/PlayMode.txt";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        GameSession s; VehicleController car; PlayerTrackVisuals walker; TireTracks prints;
        bool restored, saved; float originalTime; int fps, vsync; bool originalDamage;
        readonly Dictionary<Behaviour,bool> enabledStates = new Dictionary<Behaviour,bool>();
        readonly List<string> lines = new List<string>();
        readonly List<PlayerTrackVisuals.FootContact> contacts = new List<PlayerTrackVisuals.FootContact>();
        int invalidContacts;
        public void Begin() { Directory.CreateDirectory(Folder + "/Screenshots"); StartCoroutine(Guarded()); }
        IEnumerator Guarded()
        {
            lines.Add("Actual saved walker, Unity " + Application.unityVersion);
            lines.Add("Setup ResetPose and administrative enemy despawn only; all walking uses production input/Rigidbody/WheelCollider physics. Airborne fixture is explicitly elevated without changing a running velocity. No scene/asset writes.");
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                var r = stack.Peek(); bool more = false; object value = null;
                try { more = r.MoveNext(); if (more) value = r.Current; }
                catch (Exception e) { Check("fixture exception", false, e.ToString()); break; }
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (value is IEnumerator child) stack.Push(child); else yield return value;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            Restore(); lines.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; saved scene not modified.");
            File.WriteAllLines(ReportPath, lines); Completed = true;
        }
        IEnumerator Run()
        {
            yield return null; yield return null;
            s = GetComponent<GameSession>(); car = s.vehicle; walker = car.GetComponentInChildren<PlayerTrackVisuals>(true); prints = car.GetComponent<TireTracks>();
            originalTime = Time.timeScale; fps = Application.targetFrameRate; vsync = QualitySettings.vSyncCount; originalDamage = s.health.DamageEnabled; saved = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; s.Restart();
            Check("walker keeps baseline HP and real damage", s.Playing && s.health.Maximum == 100 && Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 5.25f) < .001f, "HP=" + s.health.Maximum + "; bullet=" + s.PlayerDamage(s.turret.tuning.bulletDamage));
            var visual = car.transform.Find("Replaceable visuals");
            Check("complete articulated biped is active", walker && walker.enabled && walker.IsWalkingRig && walker.SideAssemblyCount == 2, "two actual articulated leg meshes");
            Check("new torso pelvis arm and harpoon meshes exist", visual && LiveMesh(visual.Find("Walker torso")) && LiveMesh(visual.Find("Walker pelvis")) && LiveMesh(visual.Find("Walker right arm")) && LiveMesh(visual.Find("RearCompanionArea/HarpoonMount/HarpoonPivot/Walker harpoon arm")), "saved walker wrappers with enabled combined meshes");
            var legacy = car.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && (f.sharedMesh.name.StartsWith("Hero_Body") || f.sharedMesh.name.StartsWith("Hero_Driver") || f.sharedMesh.name.StartsWith("Hero_Mechanic") || f.sharedMesh.name.StartsWith("Hero_Wheel"))).ToArray();
            Check("obsolete automobile and crew meshes are detached", legacy.Length == 0, "legacy car/driver/mechanic/wheel mesh references=" + legacy.Length);
            Check("all ordinary wheel renderers are removed or disabled", car.wheelVisuals.All(w => w && w.GetComponentsInChildren<MeshRenderer>(true).All(r => !r.enabled || !r.GetComponent<MeshFilter>() || !r.GetComponent<MeshFilter>().sharedMesh)), "four original empty visual anchors retained");
            Check("physical chassis and wheel contacts remain", car.wheels.Length == 4 && car.wheels.All(w => w && w.enabled) && !car.Body.isKinematic && walker.GetComponentsInChildren<Collider>(true).Length == 0, "physical wheels=" + car.wheels.Length + "; mass=" + car.Body.mass);
            Check("footprint mode replaces wheel stamping", prints && prints.IsFootprintMode && prints.WheelStampCount == 0, "mode=" + (prints && prints.IsFootprintMode));
            Check("real weapon and collector anchors survive", s.turret.yaw && s.turret.pitch && s.turret.muzzle && car.GetComponent<HarpoonCollector>().spawnPoint, "turret and harpoon production references valid");
            Disable(s.spawns); Disable(s.turret); Disable(s.respawns);
            foreach (var w in s.minimap.workshops) Disable(w);
            foreach (var r in s.spawns.pool) if (r) r.Despawn(); foreach (var b in s.spawns.buggyPool) if (b) b.Despawn(); s.zone1.DespawnAll();
            s.health.DamageEnabled = false; s.input.Enabled = true; s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            car.ResetPose(s.startPosition, s.startRotation); prints.Clear(); walker.ResetMotion(); walker.FootPlanted += OnPlant;
            yield return new WaitForSeconds(1);
            Check("idle walker settles without painting tread strips", car.GroundedWheels >= 3 && prints.StampCount == 0 && walker.PlantedFootCount == 0, "grounded=" + car.GroundedWheels + "; stamps=" + prints.StampCount);
            Capture("game-idle"); CaptureDetail("walker-three-quarter", false);
            Vector3 start = car.transform.position; var mesh = walker.leftMesh.sharedMesh; var vertices = mesh.vertices;
            s.input.testBrake = false; s.input.testDirection = Vector2.up;
            yield return new WaitForSeconds(1.25f);
            Check("leg joints actually change during travel", vertices.Zip(mesh.vertices, (a,b) => (a-b).sqrMagnitude).Any(d => d > .01f), "actual dynamic vertex motion, not a static leg replacement");
            Capture("game-walking");
            yield return new WaitForSeconds(3.25f);
            s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            yield return new WaitForSeconds(1.4f);
            Check("production direction input moves the walker", Vector3.ProjectOnPlane(car.transform.position - start, Vector3.up).magnitude > 8 && car.GroundedWheels >= 3, "travel=" + Vector3.ProjectOnPlane(car.transform.position - start, Vector3.up).magnitude + "m");
            Check("both feet make discrete grounded impressions", prints.LeftFootprintCount >= 2 && prints.RightFootprintCount >= 2 && prints.WheelStampCount == 0, "left=" + prints.LeftFootprintCount + "; right=" + prints.RightFootprintCount + "; wheel=" + prints.WheelStampCount);
            Check("each printed step comes from a verified sole landing", contacts.Count >= 4 && invalidContacts == 0 && prints.StampCount == contacts.Count && walker.PlantedFootCount == contacts.Count, "events=" + contacts.Count + "; prints=" + prints.StampCount + "; invalid=" + invalidContacts);
            Check("landing cadence contains separate left right placements", contacts.Any(c => c.IsLeft) && contacts.Any(c => !c.IsLeft) && contacts.Zip(contacts.Skip(1), (a,b) => Vector3.Distance(a.Position,b.Position)).Any(d => d > .5f), "distinct world-space foot placements=" + contacts.Count);
            Check("footprints use a bounded actual render mesh", prints.ImprintMesh && prints.ImprintMesh.vertexCount > 0 && prints.ImprintMesh.vertexCount < 30000 && prints.StampCount < 640, "vertices=" + (prints.ImprintMesh ? prints.ImprintMesh.vertexCount : 0) + "; stamps=" + prints.StampCount);
            Capture("game-footsteps"); CaptureDetail("walker-and-footsteps", true);
            int parked = prints.StampCount; yield return new WaitForSeconds(.75f);
            Check("standing feet do not continuously repaint the ground", car.HorizontalSpeed < .12f && prints.StampCount == parked, "speed=" + car.HorizontalSpeed + "; stamps=" + prints.StampCount);
            s.SetPaused(true); int paused = prints.StampCount; yield return new WaitForSecondsRealtime(.35f);
            Check("pause cannot produce footprints", prints.StampCount == paused, "stamps=" + prints.StampCount); s.SetPaused(false);
            s.input.testInput = true; s.input.testBrake = false; s.input.testDirection = Vector2.up;
            car.ResetPose(car.transform.position + Vector3.up * 12, car.transform.rotation); yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            int air = prints.StampCount; yield return new WaitForSeconds(.6f);
            Check("airborne gait creates no ground impressions", car.GroundedWheels == 0 && prints.StampCount == air && walker.PlantedFootCount == 0, "grounded=" + car.GroundedWheels + "; added=" + (prints.StampCount - air));
            s.Restart(); s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            yield return new WaitForSeconds(.6f);
            Check("restart clears footprints and step phase", prints.StampCount == 0 && prints.ActiveStampCount == 0 && walker.PlantedFootCount == 0 && s.Kills == 0 && s.Scrap == 0 && s.health.Current == 100, "stamps=" + prints.StampCount + "; active=" + prints.ActiveStampCount + "; steps=" + walker.PlantedFootCount);
        }
        bool LiveMesh(Transform t) => t && t.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh);
        void OnPlant(PlayerTrackVisuals.FootContact value)
        {
            contacts.Add(value);
            if (Mathf.Abs(value.Normal.magnitude - 1) > .02f || Mathf.Abs(Vector3.Dot(value.Normal,value.Forward)) > .03f || !Physics.Raycast(value.Position + value.Normal * .15f, -value.Normal, out var hit, .3f, 1, QueryTriggerInteraction.Ignore) || Vector3.Distance(hit.point,value.Position) > .04f) invalidContacts++;
        }
        void Disable(Behaviour b) { if (b && !enabledStates.ContainsKey(b)) { enabledStates[b] = b.enabled; b.enabled = false; } }
        void Check(string title, bool success, string detail) { Checks++; if (!success) Failures++; lines.Add((success ? "PASS " : "FAIL ") + title + ": " + detail); File.WriteAllLines(ReportPath, lines); Debug.Log("WALKER " + lines[lines.Count-1]); }
        sealed class CanvasState { public Canvas Canvas; public RenderMode Mode; public Camera Camera; public float Plane; }
        void Capture(string name)
        {
            var camera = s.input.view; const int width = 540, height = 960; var target = RenderTexture.GetTemporary(width,height,24);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; float oldAspect = camera.aspect; var image = new Texture2D(width,height,TextureFormat.RGB24,false);
            var ui = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).Select(c => new CanvasState { Canvas=c,Mode=c.renderMode,Camera=c.worldCamera,Plane=c.planeDistance }).ToArray();
            try
            {
                camera.aspect = (float)width/height; camera.targetTexture = target;
                foreach (var c in ui) { c.Canvas.renderMode=RenderMode.ScreenSpaceCamera;c.Canvas.worldCamera=camera;c.Canvas.planeDistance=Mathf.Max(camera.nearClipPlane+.5f,1); }
                Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Folder+"/Screenshots/"+name+".png",image.EncodeToPNG());
            }
            finally {foreach(var c in ui)if(c.Canvas){c.Canvas.renderMode=c.Mode;c.Canvas.worldCamera=c.Camera;c.Canvas.planeDistance=c.Plane;}camera.targetTexture=oldTarget;camera.aspect=oldAspect;RenderTexture.active=oldActive;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
        }
        void CaptureDetail(string name, bool showTrail)
        {
            var go=new GameObject("Temporary walker inspection camera");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.7f,.55f,.35f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var centre=car.transform.position+Vector3.up*.6f;camera.orthographicSize=showTrail?5:2.6f;
            camera.transform.position=centre+car.transform.forward*(showTrail?-7:5)+car.transform.right*4+Vector3.up*(showTrail?9:4);
            camera.transform.LookAt(centre-(showTrail?car.transform.forward*2:Vector3.zero));
            const int width=900,height=1000;camera.aspect=(float)width/height;var target=RenderTexture.GetTemporary(width,height,24);var old=RenderTexture.active;var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            var ui=Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Select(c=>(canvas:c,enabled:c.enabled)).ToArray();
            try {foreach(var c in ui)c.canvas.enabled=false;camera.targetTexture=target;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Folder+"/Screenshots/"+name+".png",image.EncodeToPNG());}
            finally {foreach(var c in ui)if(c.canvas)c.canvas.enabled=c.enabled;RenderTexture.active=old;camera.targetTexture=null;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
        }
        void Restore()
        {
            if(restored)return;restored=true;if(walker)walker.FootPlanted-=OnPlant;
            if(!saved)return;
            Time.timeScale=1;
            foreach(var p in enabledStates)if(p.Key)p.Key.enabled=p.Value;
            if(s){s.input.testInput=false;s.input.testDirection=Vector2.zero;s.input.testBrake=false;s.health.DamageEnabled=originalDamage;s.Restart();}
            Time.timeScale=originalTime;Application.targetFrameRate=fps;QualitySettings.vSyncCount=vsync;
        }
        void OnDestroy(){Restore();}
    }
}
#endif
