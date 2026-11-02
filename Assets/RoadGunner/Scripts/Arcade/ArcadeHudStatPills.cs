using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>
    /// Presentation-only mesh effect for a single stats label such as "ATK 1    HP 1": every run of visible
    /// characters separated by 2+ spaces is moved so it sits centred on its own decorative pill, and digits are
    /// tinted with the value colour. The Text component, its rect and its exact string are never touched. When the
    /// glyph stream cannot be mapped to the string (truncation, rich text) the label is left exactly as generated.
    /// Place it BEFORE any stroke effect on the same GameObject so the outline copies follow the moved glyphs.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Text))]
    public sealed class ArcadeHudStatPills : BaseMeshEffect
    {
        [Tooltip("Centre x of each group, measured from the left edge of the Text rect (reference units).")]
        public float[] groupCentres = new float[0];
        public Color valueColor = new Color(1, .79f, .24f, 1);
        public bool tintValues = true;

        static readonly List<UIVertex> stream = new List<UIVertex>(256);
        static readonly List<int> charGroup = new List<int>(64);
        static readonly List<bool> charValue = new List<bool>(64);

        Text label;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            if (!label) label = GetComponent<Text>();
            if (!label) return;
            string text = label.text;
            if (string.IsNullOrEmpty(text) || (label.supportRichText && text.IndexOf('<') >= 0)) return;
            stream.Clear(); vh.GetUIVertexStream(stream);
            int quads = stream.Count / 6;
            if (quads == 0 || stream.Count % 6 != 0) return;

            // Map visible characters to groups; whitespace runs of two or more start a new group.
            charGroup.Clear(); charValue.Clear();
            int group = 0, spaces = 0, visible = 0, all = 0;
            bool started = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || c == '\r') { spaces = 2; continue; }
                all++;
                if (char.IsWhiteSpace(c)) { spaces++; continue; }
                if (started && spaces >= 2) group++;
                spaces = 0; started = true; visible++;
                charGroup.Add(group);
                charValue.Add(char.IsDigit(c) || c == '+' || c == '%' || c == '/' || c == '.');
            }
            // Older text generators emit a (degenerate) quad for every character, newer ones only for visible glyphs.
            bool perVisible = quads == visible;
            bool perCharacter = !perVisible && quads == all;
            if (!perVisible && !perCharacter) return;
            if (perCharacter)
            {
                // Rebuild the mapping including whitespace quads (they inherit the group of the next visible glyph).
                charGroup.Clear(); charValue.Clear();
                group = 0; spaces = 0; started = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\n' || c == '\r') { spaces = 2; continue; }
                    if (char.IsWhiteSpace(c)) { spaces++; charGroup.Add(-1); charValue.Add(false); continue; }
                    if (started && spaces >= 2) group++;
                    spaces = 0; started = true;
                    charGroup.Add(group);
                    charValue.Add(char.IsDigit(c) || c == '+' || c == '%' || c == '/' || c == '.');
                }
                if (charGroup.Count != quads) return;
            }

            int groups = group + 1;
            Rect rect = label.rectTransform.rect;
            bool shift = groupCentres != null && groupCentres.Length >= groups && groups > 1;
            for (int g = 0; shift && g < groups; g++)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int q = 0; q < quads; q++)
                {
                    if (charGroup[q] != g) continue;
                    for (int v = 0; v < 6; v++) { float x = stream[q * 6 + v].position.x; if (x < min) min = x; if (x > max) max = x; }
                }
                if (min > max) continue;
                float delta = rect.xMin + groupCentres[g] - (min + max) * .5f;
                for (int q = 0; q < quads; q++)
                {
                    if (charGroup[q] != g) continue;
                    for (int v = 0; v < 6; v++)
                    {
                        var vertex = stream[q * 6 + v];
                        vertex.position.x += delta;
                        stream[q * 6 + v] = vertex;
                    }
                }
            }
            if (tintValues)
            {
                Color32 tint = valueColor;
                for (int q = 0; q < quads; q++)
                {
                    if (!charValue[q]) continue;
                    for (int v = 0; v < 6; v++)
                    {
                        var vertex = stream[q * 6 + v];
                        vertex.color = new Color32(tint.r, tint.g, tint.b, (byte)(tint.a * vertex.color.a / 255));
                        stream[q * 6 + v] = vertex;
                    }
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(stream);
        }
    }
}
