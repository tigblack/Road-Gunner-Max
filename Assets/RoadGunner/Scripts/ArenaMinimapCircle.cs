using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>Low-cost circular UI geometry for the stencil mask, chunky frame and soft shadow.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArenaMinimapCircle : MaskableGraphic
    {
        [Range(0, .99f)] public float innerRadius;
        [Range(0, .3f)] public float softness;
        const int Segments = 128;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect(); Vector2 center = rect.center;
            Vector2 radius = rect.size * .5f;
            float solidOuter = 1 - softness;
            if (innerRadius <= 0)
            {
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2 / Segments, b = (i + 1) * Mathf.PI * 2 / Segments;
                    AddTriangle(mesh, center, Point(center, radius, a, solidOuter), Point(center, radius, b, solidOuter), color);
                }
            }
            else AddRing(mesh, center, radius, innerRadius, solidOuter, color, color);
            if (softness > 0)
            {
                Color transparent = color; transparent.a = 0;
                AddRing(mesh, center, radius, Mathf.Max(innerRadius, solidOuter), 1, color, transparent);
            }
        }

        static Vector2 Point(Vector2 center, Vector2 radius, float angle, float scale) =>
            center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y) * scale;

        static void AddRing(VertexHelper mesh, Vector2 center, Vector2 radius, float inner, float outer, Color innerColor, Color outerColor)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2 / Segments, b = (i + 1) * Mathf.PI * 2 / Segments;
                int start = mesh.currentVertCount;
                mesh.AddVert(Point(center, radius, a, inner), innerColor, Vector2.zero);
                mesh.AddVert(Point(center, radius, b, inner), innerColor, Vector2.zero);
                mesh.AddVert(Point(center, radius, b, outer), outerColor, Vector2.zero);
                mesh.AddVert(Point(center, radius, a, outer), outerColor, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }

        static void AddTriangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero); mesh.AddVert(c, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 2, start + 1);
        }
    }
}
