using UnityEngine;
namespace RoadGunner
{
    public sealed class VehicleExplosion : MonoBehaviour
    {
        public ParticleSystem fire, smoke, dust, sparks, debris;
        public Transform shockwave, flash;
        public Collider ground;
        float started;
        Renderer waveRenderer;
        Renderer flashRenderer;
        MaterialPropertyBlock tint;
        public bool IsPlaying => gameObject.activeInHierarchy && Time.time - started < 2.4f;
        public int ParticleCount => fire.particleCount + smoke.particleCount + dust.particleCount + sparks.particleCount + debris.particleCount;
        public void PlayAt(Vector3 position)
        {
            Clear(); transform.position = position; gameObject.SetActive(true); started = Time.time;
            fire.Play(); smoke.Play(); dust.Play(); sparks.Play(); debris.Play();
            fire.Emit(18); smoke.Emit(20); dust.Emit(30); sparks.Emit(28); debris.Emit(12);
            Vector3 p = position - Vector3.up * .6f, normal = Vector3.up;
            if (ground && ground.Raycast(new Ray(position + Vector3.up * 10, Vector3.down), out var hit, 30)) { p = hit.point; normal = hit.normal; }
            shockwave.SetPositionAndRotation(p + normal * .05f, Quaternion.FromToRotation(Vector3.up, normal));
            shockwave.gameObject.SetActive(true); shockwave.localScale = Vector3.one * .35f;
            if (!waveRenderer) waveRenderer = shockwave.GetComponent<Renderer>();
            tint ??= new MaterialPropertyBlock(); waveRenderer.enabled = true;
            tint.SetColor("_BaseColor", new Color(1, .82f, .36f, .75f)); waveRenderer.SetPropertyBlock(tint);
            if (flash) {
                flash.gameObject.SetActive(true); flash.position = position + Vector3.up * 1.35f;
                if (Camera.main) flash.rotation = Quaternion.LookRotation(-Camera.main.transform.forward, Camera.main.transform.up);
                flash.localScale = Vector3.one * .2f; flashRenderer = flash.GetComponent<Renderer>();
                tint.SetColor("_BaseColor", Color.white); flashRenderer.SetPropertyBlock(tint);
            }
        }
        public void Clear()
        {
            Stop(fire); Stop(smoke); Stop(dust); Stop(sparks); Stop(debris);
            if (shockwave) shockwave.gameObject.SetActive(false);
            if (flash) flash.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
        static void Stop(ParticleSystem p) { if (p) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        void Update()
        {
            float age = Time.time - started;
            if (age >= 2.4f) { Clear(); return; }
            if (flash) {
                flash.gameObject.SetActive(age < .28f);
                if (age < .28f) {
                    flash.localScale = Vector3.one * (Mathf.Sin(age / .28f * Mathf.PI) * 2.2f);
                    tint.SetColor("_BaseColor", new Color(1, 1, 1, 1 - age / .28f)); flashRenderer.SetPropertyBlock(tint);
                }
            }
            if (age >= .45f) { shockwave.gameObject.SetActive(false); return; }
            shockwave.gameObject.SetActive(true); shockwave.localScale = Vector3.one * Mathf.Lerp(.35f, 3.2f, age / .45f);
            tint.SetColor("_BaseColor", new Color(1, .82f, .36f, Mathf.Lerp(.75f, 0, age / .45f))); waveRenderer.SetPropertyBlock(tint);
        }
    }
}
