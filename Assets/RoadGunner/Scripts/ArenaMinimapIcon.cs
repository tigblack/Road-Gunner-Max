using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    public enum MinimapMarkerKind { Raider, Buggy, MiniBoss, Boss, Attack, Health, Workshop, Player }

    /// <summary>Small procedural UI glyphs. Geometry changes only when built, never per frame.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArenaMinimapIcon : MaskableGraphic
    {
        public MinimapMarkerKind kind;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float x = rect.width * .5f, y = rect.height * .5f;
            Vector2 c = rect.center;
            if (kind == MinimapMarkerKind.Player)
            {
                Triangle(mesh, c + new Vector2(0, y), c + new Vector2(-x, -y), c + new Vector2(0, -y * .45f), color);
                Triangle(mesh, c + new Vector2(0, y), c + new Vector2(0, -y * .45f), c + new Vector2(x, -y), color);
            }
            else if (kind == MinimapMarkerKind.Attack)
            {
                Triangle(mesh, c + new Vector2(x * .65f, y), c + new Vector2(-x, -y * .18f), c + new Vector2(x * .22f, -y * .18f), color);
                Triangle(mesh, c + new Vector2(-x * .22f, y * .18f), c + new Vector2(-x * .65f, -y), c + new Vector2(x, y * .18f), color);
            }
            else if (kind == MinimapMarkerKind.Health)
            {
                Quad(mesh, c, x * .3f, y, color);
                Quad(mesh, c, x, y * .3f, color);
            }
            else if (kind == MinimapMarkerKind.MiniBoss || kind == MinimapMarkerKind.Boss)
            {
                Triangle(mesh, c + new Vector2(0, y), c + new Vector2(-x, 0), c + new Vector2(0, -y), color);
                Triangle(mesh, c + new Vector2(0, y), c + new Vector2(0, -y), c + new Vector2(x, 0), color);
                if (kind == MinimapMarkerKind.Boss) Quad(mesh, c, x * .25f, y * .25f, Color.white);
            }
            else if (kind == MinimapMarkerKind.Raider)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
                    Triangle(mesh, c, c + new Vector2(Mathf.Cos(a) * x, Mathf.Sin(a) * y), c + new Vector2(Mathf.Cos(b) * x, Mathf.Sin(b) * y), color);
                }
            }
            else
            {
                Quad(mesh, c, x, y, color);
                if (kind == MinimapMarkerKind.Workshop) Quad(mesh, c, x * .55f, y * .55f, new Color(.08f, .15f, .15f));
            }
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
            mesh.AddTriangle(i, i + 2, i + 1);
        }
    }
}
