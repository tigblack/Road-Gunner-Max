using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    public enum MinimapMarkerKind { Raider, Buggy, MiniBoss, Boss, Attack, Health, Workshop, Player }

    /// <summary>Friendly geometry glyphs: ordinary red dots, orange/red skulls, a green spanner and white/cyan player.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArenaMinimapIcon : MaskableGraphic
    {
        public MinimapMarkerKind kind;
        static readonly Color Outline = new Color(.035f, .045f, .04f, 1);
        static readonly Color PlayerCyan = new Color(.15f, .92f, 1, 1);
        static readonly Vector2[] Skull = {
            new Vector2(-.58f,.90f), new Vector2(.58f,.90f), new Vector2(.9f,.52f),
            new Vector2(.9f,-.20f), new Vector2(.47f,-.48f), new Vector2(.47f,-.88f),
            new Vector2(-.47f,-.88f), new Vector2(-.47f,-.48f), new Vector2(-.9f,-.20f), new Vector2(-.9f,.52f)
        };

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float x = rect.width * .5f, y = rect.height * .5f;
            Vector2 c = rect.center;
            if (kind == MinimapMarkerKind.Player)
            {
                Arrow(mesh, c, x, y, Outline);
                Arrow(mesh, c, x * .82f, y * .82f, color);
                Arrow(mesh, c, x * .52f, y * .59f, PlayerCyan);
            }
            else if (kind == MinimapMarkerKind.MiniBoss || kind == MinimapMarkerKind.Boss)
            {
                Polygon(mesh, c, x, y, Skull, Outline);
                Polygon(mesh, c, x * .83f, y * .83f, Skull, color);
                Quad(mesh, c + new Vector2(-x * .34f, y * .17f), x * .21f, y * .20f, Outline);
                Quad(mesh, c + new Vector2(x * .34f, y * .17f), x * .21f, y * .20f, Outline);
                Triangle(mesh, c + new Vector2(0, -y * .05f), c + new Vector2(-x * .13f, -y * .29f), c + new Vector2(x * .13f, -y * .29f), Outline);
                Quad(mesh, c + new Vector2(-x * .15f, -y * .61f), x * .035f, y * .15f, Outline);
                Quad(mesh, c + new Vector2(x * .15f, -y * .61f), x * .035f, y * .15f, Outline);
            }
            else if (kind == MinimapMarkerKind.Workshop)
            {
                Circle(mesh, c, x, y, new Color(.025f, .32f, .10f, 1));
                Circle(mesh, c, x * .88f, y * .88f, color);
                Wrench(mesh, c, Mathf.Min(x, y) * .88f);
            }
            else
            {
                // All regular enemies and reward objects intentionally share one simple red dot.
                Circle(mesh, c, x, y, new Color(.47f, .035f, .055f, 1));
                Circle(mesh, c, x * .84f, y * .84f, color);
            }
        }

        static Vector2 ToolPoint(Vector2 point, Vector2 center, float scale) =>
            center + new Vector2(point.x + point.y, point.y - point.x) * (.70710678f * scale);

        static void Wrench(VertexHelper mesh, Vector2 center, float scale)
        {
            // A real open spanner silhouette: broad diagonal handle and open U-shaped jaws.
            Vector2 a = ToolPoint(new Vector2(-.105f, -.64f), center, scale);
            Vector2 b = ToolPoint(new Vector2(-.105f, .10f), center, scale);
            Vector2 c = ToolPoint(new Vector2(.105f, .10f), center, scale);
            Vector2 d = ToolPoint(new Vector2(.105f, -.64f), center, scale);
            Triangle(mesh, a, b, c, Color.white); Triangle(mesh, a, c, d, Color.white);
            var baseCenter = ToolPoint(new Vector2(0, -.64f), center, scale);
            Circle(mesh, baseCenter, scale * .105f, scale * .105f, Color.white);
            const int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                float angleA = (135 + i * 270f / steps) * Mathf.Deg2Rad;
                float angleB = (135 + (i + 1) * 270f / steps) * Mathf.Deg2Rad;
                Vector2 outerA = new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)) * .34f + new Vector2(0, .27f);
                Vector2 outerB = new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)) * .34f + new Vector2(0, .27f);
                Vector2 innerA = new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)) * .16f + new Vector2(0, .27f);
                Vector2 innerB = new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)) * .16f + new Vector2(0, .27f);
                Triangle(mesh, ToolPoint(outerA, center, scale), ToolPoint(innerA, center, scale), ToolPoint(outerB, center, scale), Color.white);
                Triangle(mesh, ToolPoint(outerB, center, scale), ToolPoint(innerA, center, scale), ToolPoint(innerB, center, scale), Color.white);
            }
        }

        static void Arrow(VertexHelper mesh, Vector2 c, float x, float y, Color tint)
        {
            Triangle(mesh, c + new Vector2(0, y), c + new Vector2(-x, -y), c + new Vector2(0, -y * .45f), tint);
            Triangle(mesh, c + new Vector2(0, y), c + new Vector2(0, -y * .45f), c + new Vector2(x, -y), tint);
        }

        static void Circle(VertexHelper mesh, Vector2 c, float x, float y, Color tint)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6, b = (i + 1) * Mathf.PI / 6;
                Triangle(mesh, c, c + new Vector2(Mathf.Cos(a) * x, Mathf.Sin(a) * y), c + new Vector2(Mathf.Cos(b) * x, Mathf.Sin(b) * y), tint);
            }
        }

        static void Polygon(VertexHelper mesh, Vector2 c, float x, float y, Vector2[] points, Color tint)
        {
            for (int i = 0; i < points.Length; i++)
                Triangle(mesh, c, c + Vector2.Scale(points[i], new Vector2(x, y)), c + Vector2.Scale(points[(i + 1) % points.Length], new Vector2(x, y)), tint);
        }

        static void Quad(VertexHelper mesh, Vector2 center, float x, float y, Color tint)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(center + new Vector2(-x, -y), tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(-x, y), tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(x, y), tint, Vector2.zero);
            mesh.AddVert(center + new Vector2(x, -y), tint, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        }

        static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero); mesh.AddVert(c, tint, Vector2.zero);
            float winding = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (winding > 0) mesh.AddTriangle(i, i + 2, i + 1); else mesh.AddTriangle(i, i + 1, i + 2);
        }
    }
}
