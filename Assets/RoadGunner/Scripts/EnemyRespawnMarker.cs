using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Reusable presentation for a scheduler-owned respawn. Never advances time or changes gameplay.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyRespawnMarker : MonoBehaviour
    {
        [Min(.25f)] public float worldRadius = .9f;
        [Min(.025f)] public float ringWidth = .12f;
        [Min(.01f)] public float groundOffset = .08f;
        [Min(.05f)] public float labelHeight = .28f;
        [Tooltip("Use the existing HUD font so its atlas is included in the player build.")]
        public Font font;
        public Color backgroundColor = new Color(.22f, .26f, .28f, .95f);
        public Color shadowColor = new Color(.045f, .06f, .075f, .65f);
        public Color goldColor = new Color(1, .79f, .24f, 1);
        public Color readyColor = new Color(.31f, .88f, .64f, 1);

        public bool Visible => shown && isActiveAndEnabled && visuals && visuals.activeInHierarchy;
        /// <summary>The exact queued ground site; the small drawing offset is applied only to the child visuals.</summary>
        public Vector3 WorldSite { get; private set; }
        public float Remaining { get; private set; }
        public float Total { get; private set; }
        public float RemainingFraction => Total > 0 ? Mathf.Clamp01(Remaining / Total) : 0;
        /// <summary>Elapsed fraction, from zero at a newly queued respawn to one when ready.</summary>
        public float Progress => 1 - RemainingFraction;
        public MeshRenderer BackgroundRenderer { get; private set; }
        public MeshRenderer ProgressRenderer { get; private set; }
        public Text SecondsLabel { get; private set; }

        const int Segments = 64;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly string[] SecondLabels = BuildSecondLabels();
        readonly Vector3[] arcVertices = new Vector3[Segments * 4];
        GameObject visuals;
        Transform labelAnchor;
        Canvas labelCanvas;
        MeshRenderer shadowRenderer;
        Mesh shadowMesh, backgroundMesh, arcMesh;
        Material surfaceMaterial, ownedMaterial;
        MaterialPropertyBlock tint;
        Camera gameplayCamera;
        bool shown;
        int displayedSeconds = int.MinValue;
        float builtRadius, builtWidth, renderedFraction = -1;

        /// <summary>Call once when creating the cached marker. The shared material is never modified or owned here.</summary>
        public void Configure(Camera view, Material sharedMaterial = null)
        {
            gameplayCamera = view;
            if (sharedMaterial && surfaceMaterial != sharedMaterial)
            {
                if (ownedMaterial) Destroy(ownedMaterial);
                ownedMaterial = null; surfaceMaterial = sharedMaterial;
                if (shadowRenderer) shadowRenderer.sharedMaterial = surfaceMaterial;
                if (BackgroundRenderer) BackgroundRenderer.sharedMaterial = surfaceMaterial;
                if (ProgressRenderer) ProgressRenderer.sharedMaterial = surfaceMaterial;
            }
            EnsureVisuals();
            if (labelCanvas) labelCanvas.worldCamera = gameplayCamera;
            FaceCamera();
        }

        public void Show(Vector3 worldSite, float remaining, float total)
        {
            EnsureVisuals();
            WorldSite = worldSite;
            Total = SafeTime(total);
            transform.SetPositionAndRotation(worldSite, Quaternion.identity);
            visuals.transform.localPosition = Vector3.up * Mathf.Max(.01f, groundOffset);
            if (!Mathf.Approximately(builtRadius, Mathf.Max(.25f, worldRadius)) ||
                !Mathf.Approximately(builtWidth, Mathf.Clamp(ringWidth, .025f, Mathf.Max(.25f, worldRadius) * .7f)))
                BuildMeshes();
            shown = true; visuals.SetActive(true);
            displayedSeconds = int.MinValue; renderedFraction = -1;
            SetRemaining(remaining);
            FaceCamera();
        }

        /// <summary>Displays the authoritative remaining time. Pause/terminal/restart policy belongs to the scheduler.</summary>
        public void SetRemaining(float remaining)
        {
            Remaining = Mathf.Clamp(SafeTime(remaining), 0, Total);
            if (!visuals) return;
            float fraction = RemainingFraction;
            if (renderedFraction != fraction)
            {
                renderedFraction = fraction;
                FillArc(arcVertices, builtRadius, builtRadius - builtWidth, fraction);
                arcMesh.SetVertices(arcVertices);
                ProgressRenderer.enabled = fraction > 0;
                SetTint(ProgressRenderer, Color.Lerp(goldColor, readyColor, Progress));
            }
            int seconds = Remaining > 0 ? Mathf.CeilToInt(Remaining) : 0;
            if (displayedSeconds == seconds) return;
            displayedSeconds = seconds;
            SecondsLabel.fontSize = seconds == 0 ? 36 : 56;
            SecondsLabel.color = seconds == 0 ? readyColor : new Color(1, .99f, .95f);
            SecondsLabel.text = seconds == 0 ? "READY" : seconds < SecondLabels.Length ? SecondLabels[seconds] : seconds.ToString() + "s";
        }

        /// <summary>Retains the last site/time for inspection; the same object can be shown for the next death.</summary>
        public void Hide()
        {
            shown = false;
            if (visuals) visuals.SetActive(false);
        }

        void EnsureVisuals()
        {
            if (visuals) return;
            if (!surfaceMaterial)
            {
                // Normal saved-scene use supplies Attack Range.mat, whose shader is already included in WebGL.
                var shader = Shader.Find("RoadGunner/Sand Tracks");
                if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
                ownedMaterial = new Material(shader) { name = "Respawn marker fallback", hideFlags = HideFlags.DontSave };
                surfaceMaterial = ownedMaterial;
            }
            tint = new MaterialPropertyBlock();
            visuals = new GameObject("Respawn countdown visuals");
            visuals.transform.SetParent(transform, false); visuals.SetActive(false);
            visuals.transform.localPosition = Vector3.up * Mathf.Max(.01f, groundOffset);
            shadowRenderer = NewRing("Ground shadow and outline", 0);
            BackgroundRenderer = NewRing("Dark countdown ring", .012f);
            ProgressRenderer = NewRing("Remaining time arc", .025f);
            BuildMeshes();
            SetTint(shadowRenderer, shadowColor); SetTint(BackgroundRenderer, backgroundColor);
            BuildLabel();
        }

        MeshRenderer NewRing(string name, float height)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(visuals.transform, false); go.transform.localPosition = Vector3.up * height;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = surfaceMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        void BuildMeshes()
        {
            if (shadowMesh) Destroy(shadowMesh);
            if (backgroundMesh) Destroy(backgroundMesh);
            if (arcMesh) Destroy(arcMesh);
            builtRadius = Mathf.Max(.25f, worldRadius);
            builtWidth = Mathf.Clamp(ringWidth, .025f, builtRadius * .7f);
            shadowMesh = RingMesh("Respawn ground outline", builtRadius + .045f, builtRadius - builtWidth - .045f);
            backgroundMesh = RingMesh("Respawn full background ring", builtRadius, builtRadius - builtWidth);
            arcMesh = RingMesh("Respawn reusable remaining arc", builtRadius, builtRadius - builtWidth);
            arcMesh.MarkDynamic();
            shadowRenderer.GetComponent<MeshFilter>().sharedMesh = shadowMesh;
            BackgroundRenderer.GetComponent<MeshFilter>().sharedMesh = backgroundMesh;
            ProgressRenderer.GetComponent<MeshFilter>().sharedMesh = arcMesh;
            if (labelAnchor) labelAnchor.localScale = Vector3.one * (.011f * builtRadius / .9f);
            renderedFraction = -1;
        }

        static Mesh RingMesh(string name, float outer, float inner)
        {
            var vertices = new Vector3[Segments * 4]; var normals = new Vector3[vertices.Length];
            var colors = new Color32[vertices.Length]; var uv = new Vector2[vertices.Length];
            var indices = new int[Segments * 6];
            FillArc(vertices, outer, Mathf.Max(.01f, inner), 1);
            for (int i = 0; i < vertices.Length; i++) { normals[i] = Vector3.up; colors[i] = new Color32(255, 255, 255, 255); uv[i] = new Vector2(.5f, .5f); }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 2; indices[t + 4] = v + 3; indices[t + 5] = v + 1;
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors32 = colors; mesh.uv = uv; mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(outer * 2, .05f, outer * 2));
            return mesh;
        }

        static void FillArc(Vector3[] vertices, float outer, float inner, float fraction)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = Mathf.Min((float)i / Segments, fraction) * Mathf.PI * 2;
                float b = Mathf.Min((float)(i + 1) / Segments, fraction) * Mathf.PI * 2;
                Vector3 from = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), to = new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b));
                int v = i * 4;
                vertices[v] = from * outer; vertices[v + 1] = from * inner;
                vertices[v + 2] = to * outer; vertices[v + 3] = to * inner;
            }
        }

        void SetTint(MeshRenderer renderer, Color color)
        {
            tint.Clear(); tint.SetColor(BaseColorId, color); tint.SetTexture(BaseMapId, Texture2D.whiteTexture);
            renderer.SetPropertyBlock(tint);
        }

        void BuildLabel()
        {
            var go = new GameObject("Billboard seconds", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(visuals.transform, false); labelAnchor = go.transform;
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(128, 80); rect.localScale = Vector3.one * (.011f * builtRadius / .9f);
            labelCanvas = go.GetComponent<Canvas>(); labelCanvas.renderMode = RenderMode.WorldSpace; labelCanvas.worldCamera = gameplayCamera;
            var group = go.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            var text = new GameObject("Seconds or READY", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            text.transform.SetParent(go.transform, false);
            var textRect = text.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            SecondsLabel = text.GetComponent<Text>();
            SecondsLabel.font = font ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            SecondsLabel.fontSize = 56; SecondsLabel.alignment = TextAnchor.MiddleCenter;
            SecondsLabel.horizontalOverflow = HorizontalWrapMode.Overflow; SecondsLabel.verticalOverflow = VerticalWrapMode.Overflow;
            SecondsLabel.raycastTarget = false; SecondsLabel.supportRichText = false;
            var outline = text.GetComponent<Outline>(); outline.effectColor = new Color(.035f, .05f, .06f, 1); outline.effectDistance = new Vector2(2.5f, -2.5f);
        }

        void LateUpdate() { if (Visible) FaceCamera(); }
        void FaceCamera()
        {
            if (!labelAnchor) return;
            // Start the ground arc at screen twelve o'clock, clockwise, without tilting it out of the ground plane.
            visuals.transform.rotation = Quaternion.Euler(0, gameplayCamera ? gameplayCamera.transform.eulerAngles.y : 45, 0);
            labelAnchor.SetPositionAndRotation(WorldSite + Vector3.up * (Mathf.Max(.01f, groundOffset) + Mathf.Max(.05f, labelHeight)),
                gameplayCamera ? gameplayCamera.transform.rotation : Quaternion.Euler(50, 45, 0));
        }
        void OnDisable() { Hide(); }
        void OnDestroy()
        {
            if (shadowMesh) Destroy(shadowMesh); if (backgroundMesh) Destroy(backgroundMesh); if (arcMesh) Destroy(arcMesh);
            if (ownedMaterial) Destroy(ownedMaterial); if (visuals) Destroy(visuals);
        }
        static float SafeTime(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
        static string[] BuildSecondLabels()
        {
            var result = new string[601];
            for (int i = 0; i < result.Length; i++) result[i] = i.ToString() + "s";
            return result;
        }
    }
}
