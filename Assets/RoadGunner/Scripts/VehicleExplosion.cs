using UnityEngine;
namespace RoadGunner
{
    public sealed class VehicleExplosion : MonoBehaviour
    {
        public const float Lifetime = 2.4f;
        public ParticleSystem fire, smoke, dust, sparks, debris;
        [Tooltip("Optional HDR additive core glow; counted with the other systems.")] public ParticleSystem core;
        public Transform shockwave, flash;
        [Tooltip("Optional flat ground decals: a short additive flash and a fading scorch.")] public Transform groundFlash, scorch;
        public Collider ground;
        [Header("Burst sizes (the slot must stay >= 80 and <= 132 particles)")]
        [Min(0)] public int fireCount = 18, smokeCount = 20, dustCount = 30, sparkCount = 28, debrisCount = 12, coreCount;
        [Header("Arcade timing / colours (HDR values bloom)")]
        [Min(.05f)] public float flashTime = .28f, flashScale = 2.2f, waveTime = .45f, waveFrom = .35f, waveTo = 3.2f, groundFlashTime = .3f, groundFlashScale = 5f;
        [ColorUsage(true, true)] public Color waveColor = new Color(1, .82f, .36f, .75f), flashColor = Color.white, groundFlashColor = new Color(2, 1.3f, .5f, 1), scorchColor = new Color(.3f, .19f, .16f, .55f);
        float started;
        Renderer waveRenderer, flashRenderer, groundFlashRenderer, scorchRenderer;
        MaterialPropertyBlock tint;
        Transform view;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public bool IsPlaying => gameObject.activeInHierarchy && Time.time - started < Lifetime;
        public int ParticleCount => Count(fire) + Count(smoke) + Count(dust) + Count(sparks) + Count(debris) + Count(core);
        static int Count(ParticleSystem p) => p ? p.particleCount : 0;
        public void PlayAt(Vector3 position)
        {
            Clear(); transform.position = position; gameObject.SetActive(true); started = Time.time;
            Burst(fire, fireCount); Burst(smoke, smokeCount); Burst(dust, dustCount); Burst(sparks, sparkCount); Burst(debris, debrisCount); Burst(core, coreCount);
            Vector3 p = position - Vector3.up * .6f, normal = Vector3.up;
            if (ground && ground.Raycast(new Ray(position + Vector3.up * 10, Vector3.down), out var hit, 30)) { p = hit.point; normal = hit.normal; }
            var flat = Quaternion.FromToRotation(Vector3.up, normal);
            tint ??= new MaterialPropertyBlock();
            if (shockwave)
            {
                shockwave.SetPositionAndRotation(p + normal * .05f, flat); shockwave.gameObject.SetActive(true); shockwave.localScale = Vector3.one * waveFrom;
                if (!waveRenderer) waveRenderer = shockwave.GetComponent<Renderer>();
                if (waveRenderer) { waveRenderer.enabled = true; Tint(waveRenderer, waveColor); }
            }
            Decal(groundFlash, ref groundFlashRenderer, p + normal * .07f, flat, groundFlashColor, groundFlashScale * .7f);
            Decal(scorch, ref scorchRenderer, p + normal * .03f, flat, scorchColor, groundFlashScale * .55f);
            if (flash)
            {
                if (!view) { var cam = Camera.main; if (cam) view = cam.transform; }
                flash.gameObject.SetActive(true); flash.position = position + Vector3.up * 1.35f;
                if (view) flash.rotation = Quaternion.LookRotation(-view.forward, view.up);
                flash.localScale = Vector3.one * .2f;
                if (!flashRenderer) flashRenderer = flash.GetComponent<Renderer>();
                if (flashRenderer) Tint(flashRenderer, flashColor);
            }
        }
        void Burst(ParticleSystem p, int count) { if (!p || count <= 0) return; p.Play(); p.Emit(count); }
        void Decal(Transform t, ref Renderer r, Vector3 p, Quaternion rotation, Color color, float scale)
        {
            if (!t) return;
            t.SetPositionAndRotation(p, rotation); t.localScale = Vector3.one * scale; t.gameObject.SetActive(true);
            if (!r) r = t.GetComponent<Renderer>();
            if (r) Tint(r, color);
        }
        void Tint(Renderer r, Color c) { tint.SetColor(BaseColor, c); r.SetPropertyBlock(tint); }
        public void Clear()
        {
            Stop(fire); Stop(smoke); Stop(dust); Stop(sparks); Stop(debris); Stop(core);
            if (shockwave) shockwave.gameObject.SetActive(false);
            if (flash) flash.gameObject.SetActive(false);
            if (groundFlash) groundFlash.gameObject.SetActive(false);
            if (scorch) scorch.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
        static void Stop(ParticleSystem p) { if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        static float EaseOut(float k) { k = 1 - Mathf.Clamp01(k); return 1 - k * k * k; }
        void Update()
        {
            float age = Time.time - started;
            if (age >= Lifetime) { Clear(); return; }
            if (tint == null) return;
            if (flash)
            {
                bool on = age < flashTime; if (flash.gameObject.activeSelf != on) flash.gameObject.SetActive(on);
                if (on && flashRenderer)
                {
                    float k = age / flashTime;
                    flash.localScale = Vector3.one * flashScale * (k < .3f ? EaseOut(k / .3f) : Mathf.Lerp(1, .7f, (k - .3f) / .7f));
                    var c = flashColor; c.a *= 1 - k * k; Tint(flashRenderer, c);
                }
            }
            if (groundFlash && groundFlash.gameObject.activeSelf)
            {
                if (age >= groundFlashTime) groundFlash.gameObject.SetActive(false);
                else if (groundFlashRenderer)
                {
                    float k = age / groundFlashTime; groundFlash.localScale = Vector3.one * groundFlashScale * Mathf.Lerp(.7f, 1.1f, EaseOut(k));
                    // Warm fire-light glow on the ground: holds its strength, then fades out quickly.
                    var c = groundFlashColor; c.a *= 1 - k * k * k; Tint(groundFlashRenderer, c);
                }
            }
            if (scorch && scorchRenderer && scorch.gameObject.activeSelf)
            {
                var c = scorchColor; c.a *= Mathf.Clamp01(age / .12f) * (1 - Mathf.Clamp01((age - 1.3f) / (Lifetime - .25f - 1.3f))); Tint(scorchRenderer, c);
            }
            if (!shockwave) return;
            if (age >= waveTime) { if (shockwave.gameObject.activeSelf) shockwave.gameObject.SetActive(false); return; }
            float w = age / waveTime;
            shockwave.gameObject.SetActive(true); shockwave.localScale = Vector3.one * Mathf.Lerp(waveFrom, waveTo, EaseOut(w));
            // Bright crisp band that holds most of its strength while it races outward, then fades in the last third.
            if (waveRenderer) { var c = waveColor; c.a *= Mathf.Clamp01(age / .03f) * (1 - w * w * w); Tint(waveRenderer, c); }
        }
    }
}
