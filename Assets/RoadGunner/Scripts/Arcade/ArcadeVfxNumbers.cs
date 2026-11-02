using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Floating combat numbers as ONE dynamic mesh (one draw call): every number is built from font-atlas glyph quads in
    /// three layers - a soft drop shadow, a real 8-direction dark outline and the face (white, gold→orange gradient for kills,
    /// orange for rams, mint "+N" for repairs). Rapid hits on the same target merge into one growing number.
    /// Presentation only, allocation-free after Awake, null-safe when the font or camera is missing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ArcadeVfxNumbers : MonoBehaviour
    {
        public enum Kind { Normal, Lethal, Ram, Heal }

        [Tooltip("Dynamic font used for the digits (Lilita One when present).")] public Font font;
        [Min(16)] public int fontSize = 96;
        [Range(4, 24)] public int capacity = 16;
        [Header("Glyph cap height in metres (camera shows ~40 px per metre at 720p)")]
        public float normalHeight = .82f;
        public float lethalHeight = 1.3f, ramHeight = 1.05f, healHeight = .9f;
        [Header("Timing")]
        [Min(.05f)] public float mergeWindow = .35f;
        [Min(.2f)] public float normalLife = .78f, lethalLife = 1.05f;
        [Min(0)] public float rise = 1.2f;
        [Range(0, .2f)] public float outline = .1f;
        [Range(0, .3f)] public float shadowDrop = .2f;

        const string Glyphs = "0123456789+";
        const int MaxChars = 4, Layers = 10;
        static readonly Color32 OutlineWarm = new Color32(0x3A, 0x1F, 0x14, 255), OutlineHeal = new Color32(0x0E, 0x4F, 0x48, 255);
        static readonly Color32 ShadowColor = new Color32(0x2A, 0x14, 0x14, 150);
        static readonly Color32 White = new Color32(255, 255, 255, 255), WhiteWarm = new Color32(255, 250, 236, 255);
        static readonly Color32 GoldTop = new Color32(0xFF, 0xE4, 0x5C, 255), GoldBottom = new Color32(0xFF, 0x7A, 0x1A, 255);
        static readonly Color32 RamTop = new Color32(0xFF, 0xB0, 0x40, 255), RamBottom = new Color32(0xFF, 0x5A, 0x1F, 255);
        static readonly Color32 HealTop = new Color32(0xE6, 0xFF, 0xF4, 255), HealBottom = new Color32(0x4E, 0xE0, 0xA4, 255);
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly Vector2[] Ring =
        {
            new Vector2(1, 0), new Vector2(.7071f, .7071f), new Vector2(0, 1), new Vector2(-.7071f, .7071f),
            new Vector2(-1, 0), new Vector2(-.7071f, -.7071f), new Vector2(0, -1), new Vector2(.7071f, -.7071f),
        };

        struct Pop
        {
            public CombatTarget target;
            public Vector3 origin, side;
            public float start, lastHit, punchAt, punch, value, life;
            public Kind kind;
            public bool active;
        }

        Pop[] pops;
        int[] order;
        readonly CharacterInfo[] glyph = new CharacterInfo[11];
        readonly int[] chars = new int[MaxChars];
        Vector3[] vertices;
        Vector2[] uvs;
        byte[] quadGlyph;
        int uploadedQuads;
        Color32[] colors;
        int[] triangles;
        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;
        Texture boundTexture;
        float glyphHeight = 1, glyphCenter;
        int vertexCount;
        bool glyphsReady, fontDirty = true, shown;

        public int ActiveCount { get { int n = 0; if (pops != null) for (int i = 0; i < pops.Length; i++) if (pops[i].active) n++; return n; } }

        void Awake()
        {
            int cap = Mathf.Max(4, capacity);
            pops = new Pop[cap]; order = new int[cap];
            int quads = cap * MaxChars * Layers;
            vertices = new Vector3[quads * 4]; uvs = new Vector2[quads * 4]; quadGlyph = new byte[quads]; colors = new Color32[quads * 4]; triangles = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, t = q * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2; triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh = new Mesh { name = "Arcade damage numbers (dynamic)" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            meshRenderer.enabled = false;
        }

        void OnEnable() { Font.textureRebuilt += OnFontRebuilt; fontDirty = true; }
        void OnDisable() { Font.textureRebuilt -= OnFontRebuilt; }
        void OnDestroy() { if (mesh) Destroy(mesh); }
        // The dynamic atlas is shared with the HUD and world labels and can be rebuilt after this frame's mesh upload:
        // re-fetch the glyphs right away and rewrite only the UVs of the uploaded quads (no allocation).
        void OnFontRebuilt(Font changed)
        {
            if (changed != font) return;
            fontDirty = true;
            if (!shown || uploadedQuads <= 0 || !mesh) return;
            font.RequestCharactersInTexture(Glyphs, fontSize, FontStyle.Normal);
            for (int i = 0; i < Glyphs.Length; i++) if (!font.GetCharacterInfo(Glyphs[i], out glyph[i], fontSize, FontStyle.Normal)) return;
            for (int q = 0; q < uploadedQuads; q++)
            {
                var g = glyph[quadGlyph[q]]; int v = q * 4;
                uvs[v] = g.uvBottomLeft; uvs[v + 1] = g.uvTopLeft; uvs[v + 2] = g.uvTopRight; uvs[v + 3] = g.uvBottomRight;
            }
            mesh.SetUVs(0, uvs, 0, uploadedQuads * 4);
            fontDirty = false;
        }

        public void Clear()
        {
            if (pops != null) for (int i = 0; i < pops.Length; i++) pops[i] = default;
            Hide();
        }

        /// <summary>Damage on an enemy. Hits on the same target within the merge window accumulate into one number.</summary>
        public void Hit(CombatTarget target, Vector3 point, float damage, bool ram, bool lethal, float clock, Transform view)
        {
            if (pops == null || damage <= 0) return;
            Kind kind = ram ? Kind.Ram : lethal ? Kind.Lethal : Kind.Normal;
            int stacked = 0;
            if (target)
                for (int i = 0; i < pops.Length; i++)
                {
                    if (!pops[i].active || pops[i].target != target || pops[i].kind == Kind.Heal) continue;
                    bool fresh = clock - pops[i].lastHit < mergeWindow && pops[i].kind != Kind.Lethal;
                    if (fresh && (kind == Kind.Normal || kind == Kind.Lethal || pops[i].kind == Kind.Ram))
                    {
                        ref var p = ref pops[i];
                        p.value += damage; p.lastHit = clock; p.punchAt = clock;
                        if (kind == Kind.Lethal) { p.kind = Kind.Lethal; p.punch = .5f; p.life = lethalLife; }
                        else { p.punch = kind == Kind.Ram ? .35f : .22f; p.life = Mathf.Max(p.life, normalLife); }
                        return;
                    }
                    stacked++;
                }
            Spawn(target, point + Vector3.up * (.55f + .38f * Mathf.Min(stacked, 3)), damage, kind, clock, view);
        }

        /// <summary>Repair tick: mint "+N" over the car.</summary>
        public void Heal(Vector3 point, float amount, float clock, Transform view)
        {
            if (pops == null || amount < .5f) return;
            Spawn(null, point, amount, Kind.Heal, clock, view);
        }

        void Spawn(CombatTarget target, Vector3 at, float value, Kind kind, float clock, Transform view)
        {
            int slot = 0; float oldest = float.MaxValue;
            for (int i = 0; i < pops.Length; i++)
            {
                if (!pops[i].active) { slot = i; break; }
                if (pops[i].start < oldest) { oldest = pops[i].start; slot = i; }
            }
            Vector3 right = view ? view.right : Vector3.right;
            float jitter = kind == Kind.Heal ? Random.Range(-.25f, .25f) : Random.Range(-.5f, .5f);
            pops[slot] = new Pop
            {
                target = target, origin = at + right * jitter, side = right * (jitter * .35f), start = clock, lastHit = clock, punchAt = clock,
                punch = kind == Kind.Lethal ? .5f : .25f, value = value, kind = kind, active = true,
                life = kind == Kind.Lethal ? lethalLife : normalLife,
            };
        }

        /// <summary>Animate and rebuild the combined mesh (call from LateUpdate with the presentation clock).</summary>
        public void Tick(float clock, Transform view)
        {
            if (pops == null || !mesh) return;
            int count = 0;
            for (int i = 0; i < pops.Length; i++)
            {
                if (!pops[i].active) continue;
                if (clock - pops[i].lastHit >= pops[i].life + .02f) { pops[i].active = false; continue; }
                // Insertion sort by spawn time: newer numbers draw on top of older ones.
                int j = count++;
                while (j > 0 && pops[order[j - 1]].start > pops[i].start) { order[j] = order[j - 1]; j--; }
                order[j] = i;
            }
            if (count == 0 || !font || !view) { Hide(); return; }
            if (!RefreshGlyphs()) { Hide(); return; }

            var toLocal = transform.worldToLocalMatrix;
            Vector3 right = toLocal.MultiplyVector(view.right), up = toLocal.MultiplyVector(view.up);
            int quads = 0;
            for (int n = 0; n < count; n++)
            {
                ref var p = ref pops[order[n]];
                // Keep merged numbers riding on their target while hits keep coming.
                if (p.target && p.target.Alive && p.kind != Kind.Lethal && clock - p.lastHit < .25f)
                {
                    Vector3 anchor = p.target.AimPoint + Vector3.up * .55f + p.side;
                    p.origin = Vector3.Lerp(p.origin, anchor, .35f);
                }
                float age = clock - p.start, sinceHit = clock - p.lastHit;
                float k = Mathf.Clamp01(age / Mathf.Max(.2f, p.life));
                float fade = Mathf.Clamp01((sinceHit / p.life - .76f) / .24f);
                float alpha = 1 - fade * fade;
                float scale = Scale(p, clock) * (1 - .25f * fade);
                if (alpha <= .01f || scale <= .001f) continue;
                Vector3 centre = toLocal.MultiplyPoint3x4(p.origin + Vector3.up * (rise * EaseOut(k)) + p.side * EaseOut(k));
                float height = p.kind == Kind.Lethal ? lethalHeight : p.kind == Kind.Ram ? ramHeight : p.kind == Kind.Heal ? healHeight : normalHeight;
                quads = Build(quads, centre, right, up, height * scale, p, alpha);
            }
            if (quads == 0) { Hide(); return; }
            Upload(quads);
            if (!shown) { meshRenderer.enabled = true; shown = true; }
        }

        float Scale(in Pop p, float clock)
        {
            float age = clock - p.start, sincePunch = clock - p.punchAt;
            float s;
            if (p.kind == Kind.Lethal && age < .2f)
            {
                // 0 -> 1.5 in 60 ms, settle 1.5 -> 1.0 by 200 ms.
                s = age < .06f ? 1.5f * EaseOut(age / .06f) : Mathf.Lerp(1.5f, 1, EaseOut((age - .06f) / .14f));
                return s;
            }
            if (age < .07f) return (1 + p.punch) * EaseOut(age / .07f);
            s = 1 + p.punch * Mathf.Clamp01(1 - sincePunch / .14f);
            if (sincePunch < .03f) s = 1 + p.punch * EaseOut(sincePunch / .03f);
            return s;
        }

        int Build(int quads, Vector3 centre, Vector3 right, Vector3 up, float height, in Pop p, float alpha)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(p.value), 1, 999);
            int n = 0;
            if (p.kind == Kind.Heal) chars[n++] = 10;
            if (value >= 100) chars[n++] = value / 100;
            if (value >= 10) chars[n++] = value / 10 % 10;
            chars[n++] = value % 10;
            if (quads + n * Layers > vertices.Length / 4) return quads;

            float s = height / glyphHeight, width = 0;
            for (int c = 0; c < n; c++) width += glyph[chars[c]].advance;
            float x0 = -width * .5f, y0 = -glyphCenter;
            byte a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha));
            // Outline and shadow fade faster than the face, so a fading number never turns into a muddy brown smear.
            float under = Mathf.Pow(Mathf.Clamp01(alpha), 2.6f);
            var outlineColor = p.kind == Kind.Heal ? OutlineHeal : OutlineWarm; outlineColor.a = (byte)Mathf.RoundToInt(255 * under);
            var shadow = ShadowColor; shadow.a = (byte)(ShadowColor.a * under);
            Color32 top, bottom;
            switch (p.kind)
            {
                case Kind.Lethal: top = GoldTop; bottom = GoldBottom; break;
                case Kind.Ram: top = RamTop; bottom = RamBottom; break;
                case Kind.Heal: top = HealTop; bottom = HealBottom; break;
                default: top = White; bottom = WhiteWarm; break;
            }
            top.a = a; bottom.a = a;
            float r = outline * height, drop = shadowDrop * height;
            // Layer 0: drop shadow, 1-8: outline ring, 9: face.
            for (int layer = 0; layer < Layers; layer++)
            {
                Vector3 offset;
                Color32 ct, cb;
                if (layer == 0) { offset = up * -drop + right * (r * .25f); ct = cb = shadow; }
                else if (layer < 9) { var d = Ring[layer - 1]; offset = right * (d.x * r) + up * (d.y * r); ct = cb = outlineColor; }
                else { offset = Vector3.zero; ct = top; cb = bottom; }
                float x = x0;
                for (int c = 0; c < n; c++)
                {
                    var g = glyph[chars[c]];
                    Vector3 o = centre + offset;
                    Vector3 bl = o + right * ((x + g.minX) * s) + up * ((y0 + g.minY) * s);
                    Vector3 tr = o + right * ((x + g.maxX) * s) + up * ((y0 + g.maxY) * s);
                    Vector3 br = o + right * ((x + g.maxX) * s) + up * ((y0 + g.minY) * s);
                    Vector3 tl = o + right * ((x + g.minX) * s) + up * ((y0 + g.maxY) * s);
                    int v = quads * 4;
                    vertices[v] = bl; vertices[v + 1] = tl; vertices[v + 2] = tr; vertices[v + 3] = br;
                    uvs[v] = g.uvBottomLeft; uvs[v + 1] = g.uvTopLeft; uvs[v + 2] = g.uvTopRight; uvs[v + 3] = g.uvBottomRight;
                    colors[v] = cb; colors[v + 1] = ct; colors[v + 2] = ct; colors[v + 3] = cb;
                    quadGlyph[quads] = (byte)chars[c];
                    quads++;
                    x += g.advance;
                }
            }
            return quads;
        }

        bool RefreshGlyphs()
        {
            font.RequestCharactersInTexture(Glyphs, fontSize, FontStyle.Normal);
            if (fontDirty || !glyphsReady)
            {
                glyphsReady = true;
                for (int i = 0; i < Glyphs.Length; i++)
                    if (!font.GetCharacterInfo(Glyphs[i], out glyph[i], fontSize, FontStyle.Normal)) glyphsReady = false;
                if (glyphsReady)
                {
                    var zero = glyph[0];
                    glyphHeight = Mathf.Max(1, zero.maxY - zero.minY);
                    glyphCenter = (zero.maxY + zero.minY) * .5f;
                }
                fontDirty = !glyphsReady;
            }
            var texture = font.material ? font.material.mainTexture : null;
            if (!texture) return false;
            if (texture != boundTexture)
            {
                boundTexture = texture;
                block.Clear(); block.SetTexture(BaseMapId, texture); meshRenderer.SetPropertyBlock(block);
            }
            return glyphsReady;
        }

        void Upload(int quads)
        {
            int verts = quads * 4, indices = quads * 6;
            // Shrinking: indices first (they stay inside the old vertex range); growing: vertices first.
            if (verts < vertexCount) { mesh.SetTriangles(triangles, 0, indices, 0, false); SetVertexData(verts); }
            else { SetVertexData(verts); mesh.SetTriangles(triangles, 0, indices, 0, false); }
            vertexCount = verts; uploadedQuads = quads;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000);
        }

        void SetVertexData(int verts)
        {
            mesh.SetVertices(vertices, 0, verts);
            mesh.SetUVs(0, uvs, 0, verts);
            mesh.SetColors(colors, 0, verts);
        }

        void Hide()
        {
            if (shown && meshRenderer) { meshRenderer.enabled = false; shown = false; }
        }

        static float EaseOut(float k) { k = 1 - Mathf.Clamp01(k); return 1 - k * k * k; }
    }
}
