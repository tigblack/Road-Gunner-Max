using UnityEngine;
using UnityEngine.Rendering;
namespace RoadGunner
{
    // One reusable mesh: no colliders, per-frame objects, textures or material copies.
    public sealed class AttackRangeIndicator : MonoBehaviour
    {
        public TurretController turret;
        public Collider ground;
        public Material material;
        [Header("Readable weapon boundary (actual weapon range is unchanged)")]
        public Color fillColor = new Color(.3f, .86f, 1, .07f);
        public Color outlineColor = new Color(.65f, .97f, 1, .98f);
        public Color contrastColor = new Color(.025f, .11f, .14f, .65f);
        [Min(.02f)] public float outlineWidth = .16f;
        [Min(.02f)] public float contrastWidth = .14f;
        const int Segments = 128, FillRings = 4, EdgeRows = 6;
        const float Lift = .045f;
        readonly Vector3[] vertices = new Vector3[1 + Segments * (FillRings + EdgeRows)];
        Mesh mesh; GameObject holder; MeshRenderer ringRenderer;
        Vector3 centre = Vector3.positiveInfinity;
        float builtRadius;
        public float Radius => turret ? turret.Range : 0;
        public Mesh Mesh => mesh;
        public Vector3 Centre => centre;
        public bool Visible => ringRenderer && ringRenderer.enabled;
        void Awake()
        {
            holder = new GameObject("Player attack radius • 10 metres");
            mesh = new Mesh { name = "Ground-conforming attack circle" }; mesh.MarkDynamic();
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            ringRenderer = holder.AddComponent<MeshRenderer>(); ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off; ringRenderer.receiveShadows = false;
            var colors = new Color[vertices.Length]; var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * (3 + (FillRings - 1 + EdgeRows - 1) * 6)]; int t = 0;
            var fill = fillColor; colors[0] = fill;
            for (int row = 0; row < FillRings + EdgeRows; row++) for (int i = 0; i < Segments; i++)
            {
                int index = 1 + row * Segments + i, next = 1 + row * Segments + (i + 1) % Segments;
                Color c = fill;
                if (row >= FillRings)
                {
                    int edge = row - FillRings;
                    c = edge == 2 || edge == 3 ? outlineColor : contrastColor;
                    if (edge == 0 || edge == 5) c.a = 0;
                }
                colors[index] = c;
                if (row == 0) { triangles[t++] = 0; triangles[t++] = next; triangles[t++] = index; }
                else if (row != FillRings)
                {
                    int prev = index - Segments, prevNext = next - Segments;
                    triangles[t++] = prev; triangles[t++] = next; triangles[t++] = index;
                    triangles[t++] = prev; triangles[t++] = prevNext; triangles[t++] = next;
                }
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = triangles;
            Refresh();
        }
        public float GroundHeight(float x, float z)
        {
            if (ground && ground.Raycast(new Ray(new Vector3(x, transform.position.y + 30, z), Vector3.down), out var hit, 100)) return hit.point.y;
            return ground ? ground.bounds.min.y : transform.position.y - .6f;
        }
        Vector3 Point(float x, float z) { return new Vector3(x, GroundHeight(x, z) + Lift, z); }
        public void Refresh()
        {
            if (!mesh || !turret) return;
            centre = transform.position; centre.y = GroundHeight(centre.x, centre.z);
            float radius = Radius; builtRadius = radius; vertices[0] = Point(centre.x, centre.z);
            for (int row = 0; row < FillRings + EdgeRows; row++)
            {
                int edge = row - FillRings;
                float half = Mathf.Max(.02f, outlineWidth) * .5f;
                float contrast = Mathf.Max(.02f, contrastWidth);
                float offset = edge == 0 ? -half - contrast - .06f : edge == 1 ? -half - contrast :
                    edge == 2 ? -half : edge == 3 ? half : edge == 4 ? half + contrast : half + contrast + .06f;
                float r = row < FillRings ? radius * (row + 1) / FillRings : radius + offset;
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * Mathf.PI * 2 / Segments;
                    vertices[1 + row * Segments + i] = Point(centre.x + Mathf.Cos(a) * r, centre.z + Mathf.Sin(a) * r);
                }
            }
            mesh.vertices = vertices; mesh.RecalculateBounds();
        }
        void LateUpdate()
        {
            if (!ringRenderer || !turret) return;
            ringRenderer.enabled = isActiveAndEnabled && turret.session.State != SessionState.Defeated;
            if (AttackRange.SquaredDistance(centre, transform.position) > .0004f || Mathf.Abs(builtRadius - Radius) > .001f) Refresh();
        }
        void OnDisable() { if (ringRenderer) ringRenderer.enabled = false; }
        void OnDestroy() { if (holder) Destroy(holder); if (mesh) Destroy(mesh); }
    }
}
