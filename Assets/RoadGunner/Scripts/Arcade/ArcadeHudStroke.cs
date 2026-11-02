using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>
    /// Thick round outline plus a hard drop shadow for chunky arcade labels. Presentation only: the
    /// Graphic's own colour and text are untouched; extra copies are added to the mesh it already builds.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Graphic))]
    public sealed class ArcadeHudStroke : BaseMeshEffect
    {
        public Color strokeColor = new Color(.106f, .141f, .251f, 1);
        [Range(0, 10)] public float width = 3.5f;
        public Color shadowColor = new Color(.106f, .141f, .251f, .9f);
        public Vector2 shadowOffset = new Vector2(0, -4);
        [Range(4, 12)] public int directions = 8;

        static readonly List<UIVertex> source = new List<UIVertex>(256), output = new List<UIVertex>(2048);
        static readonly Vector2[] unit = new Vector2[12];
        static int unitCount;

        public void Configure(Color stroke, float strokeWidth, Color shadow, Vector2 offset)
        {
            strokeColor = stroke; width = strokeWidth; shadowColor = shadow; shadowOffset = offset;
            if (graphic) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            source.Clear(); vh.GetUIVertexStream(source);
            int n = source.Count;
            if (n == 0) return;
            int dirs = Mathf.Clamp(directions, 4, 12);
            if (unitCount != dirs)
            {
                for (int i = 0; i < dirs; i++) { float a = i * Mathf.PI * 2 / dirs; unit[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
                unitCount = dirs;
            }
            bool stroke = width > .01f && strokeColor.a > .001f;
            bool shadow = shadowColor.a > .001f && shadowOffset.sqrMagnitude > .0001f;
            int copies = 1 + (stroke ? dirs : 0) + (shadow ? (stroke ? 4 : 1) : 0);
            output.Clear();
            if (output.Capacity < n * copies) output.Capacity = n * copies;
            if (shadow)
            {
                if (stroke)
                {
                    // The shadow is a thinner stroked silhouette so it reads below the outline, not inside it.
                    float w = width * .8f;
                    Append(n, shadowOffset + new Vector2(w, 0), shadowColor); Append(n, shadowOffset + new Vector2(-w, 0), shadowColor);
                    Append(n, shadowOffset + new Vector2(0, w), shadowColor); Append(n, shadowOffset + new Vector2(0, -w), shadowColor);
                }
                else Append(n, shadowOffset, shadowColor);
            }
            if (stroke) for (int i = 0; i < dirs; i++) Append(n, unit[i] * width, strokeColor);
            for (int i = 0; i < n; i++) output.Add(source[i]);
            vh.Clear();
            vh.AddUIVertexTriangleStream(output);
        }

        static void Append(int n, Vector2 offset, Color tint)
        {
            Color32 c = tint;
            for (int i = 0; i < n; i++)
            {
                var v = source[i];
                v.position.x += offset.x; v.position.y += offset.y;
                v.color = new Color32(c.r, c.g, c.b, (byte)(c.a * v.color.a / 255));
                output.Add(v);
            }
        }
    }
}
