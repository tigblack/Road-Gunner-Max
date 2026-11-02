using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Hue-keeping hit flash shared by Raider, EnemyBuggy, WastelandMutant and VehicleHealth.
    /// Each renderer's own material _BaseColor is lerped toward a warm tint (never replaced by flat HDR white), the flash eases out
    /// over a few frames, a per-target cooldown keeps the real colours readable under continuous fire, and heavy targets can get a
    /// short scale punch on their visual root. Writes _BaseColor through a MaterialPropertyBlock only; no allocation after Bind.
    /// </summary>
    public sealed class ArcadeVfxFlash
    {
        /// <summary>Warm white used by enemy flashes (HDR, kept modest so outline and colour zones survive bloom).</summary>
        public static readonly Color WarmWhite = new Color(1.45f, 1.4f, 1.3f);
        /// <summary>Reddish-warm tint for the player's damage flash.</summary>
        public static readonly Color PlayerWarm = new Color(1.5f, .92f, .78f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        const float PunchTime = .1f;

        Renderer[] renderers;
        Color[][] baseColors;
        MaterialPropertyBlock block;
        Transform punchTarget;
        Vector3 punchBase;
        Color tint;
        float start = -10, lastTrigger = -10, duration = .055f, strength, punchAmount;
        bool colouring, punching;

        public bool Bound => renderers != null;
        /// <summary>True while either the colour flash or the scale punch is still running.</summary>
        public bool Active => colouring || punching;

        /// <summary>Caches renderers and their material base colours once (call again only if the rig changes).</summary>
        public void Bind(Renderer[] list, Transform punch = null)
        {
            if (renderers != null && Active) Clear();
            renderers = list ?? new Renderer[0];
            punchTarget = punch;
            block ??= new MaterialPropertyBlock();
            baseColors = new Color[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var materials = r ? r.sharedMaterials : null;
                int count = materials != null ? Mathf.Max(1, materials.Length) : 1;
                var colors = new Color[count];
                for (int m = 0; m < count; m++)
                {
                    var material = materials != null && m < materials.Length ? materials[m] : null;
                    colors[m] = material && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
                }
                baseColors[i] = colors;
            }
        }

        /// <summary>
        /// Starts a flash unless the target flashed less than <paramref name="cooldown"/> seconds ago.
        /// <paramref name="amount"/> is the lerp weight toward <paramref name="warm"/> at the peak; <paramref name="punch"/> &gt; 0 scales
        /// the visual root 1 → 1+punch → 1 over 0.1 s.
        /// </summary>
        public bool Trigger(Color warm, float amount, float time, float cooldown, float punch = 0)
        {
            if (renderers == null) return false;
            // Presentation clock: a flash that starts on a killing blow must still finish while timeScale is 0.
            float now = Time.unscaledTime;
            if (now - lastTrigger < cooldown) return false;
            lastTrigger = now; start = now; tint = warm; strength = Mathf.Clamp01(amount); duration = Mathf.Max(.02f, time);
            colouring = true;
            if (punch > 0 && punchTarget)
            {
                if (!punching) punchBase = punchTarget.localScale;
                punching = true; punchAmount = punch;
            }
            Apply(strength);
            return true;
        }

        /// <summary>Advance the ease-out; returns <see cref="Active"/>. Call every frame while active.</summary>
        public bool Tick()
        {
            if (!Active) return false;
            float age = Time.unscaledTime - start;
            if (colouring)
            {
                float k = age / duration;
                if (k >= 1) { Restore(); colouring = false; }
                else { float e = 1 - k; Apply(strength * e * e); }
            }
            if (punching)
            {
                if (!punchTarget || age >= PunchTime) { EndPunch(); }
                else
                {
                    float k = age / PunchTime;
                    // Fast rise (first 35%), soft settle: a squashy toy "thump".
                    float bump = k < .35f ? Mathf.Sin(k / .35f * Mathf.PI * .5f) : Mathf.Cos((k - .35f) / .65f * Mathf.PI * .5f);
                    punchTarget.localScale = punchBase * (1 + punchAmount * bump);
                }
            }
            return Active;
        }

        /// <summary>Immediately removes the tint and restores the punched scale.</summary>
        public void Clear()
        {
            if (colouring) Restore();
            colouring = false;
            if (punching) EndPunch();
        }

        /// <summary>Forget the running flash without touching renderers (the owner has already written its own block, e.g. a wreck tint).</summary>
        public void Cancel()
        {
            colouring = false;
            if (punching) EndPunch();
        }

        /// <summary>Allow the next Trigger regardless of the cooldown (new spawn / run restart).</summary>
        public void ResetCooldown() { lastTrigger = -10; }

        void EndPunch()
        {
            if (punchTarget) punchTarget.localScale = punchBase;
            punching = false;
        }

        void Apply(float k)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i]; if (!r) continue;
                var colors = baseColors[i];
                if (colors.Length == 1)
                {
                    block.Clear(); block.SetColor(BaseColorId, Mix(colors[0], k)); r.SetPropertyBlock(block);
                    continue;
                }
                for (int m = 0; m < colors.Length; m++) { block.Clear(); block.SetColor(BaseColorId, Mix(colors[m], k)); r.SetPropertyBlock(block, m); }
            }
        }

        Color Mix(Color c, float k)
        {
            var o = new Color(c.r + (tint.r - c.r) * k, c.g + (tint.g - c.g) * k, c.b + (tint.b - c.b) * k, c.a);
            return o;
        }

        void Restore()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i]; if (!r) continue;
                var colors = baseColors[i];
                if (colors.Length == 1) { r.SetPropertyBlock(null); continue; }
                block.Clear();
                for (int m = 0; m < colors.Length; m++) r.SetPropertyBlock(block, m);
            }
        }
    }
}
