using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadGunner
{
    /// <summary>
    /// Visual suspension-following track units. The four original WheelColliders remain the
    /// sole wheel contacts; this component never applies forces, changes inputs or adds colliders.
    /// Each side advances by its own measured chassis-point displacement, including pivot turns.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    [DisallowMultipleComponent]
    public sealed class PlayerTrackVisuals : MonoBehaviour
    {
        public VehicleController vehicle;
        public GameSession session;
        public WheelCollider frontLeft, rearLeft, frontRight, rearRight;
        public MeshFilter leftMesh, rightMesh;
        public Renderer[] hiddenWheelRenderers = Array.Empty<Renderer>();
        [Range(32, 56)] public int treadsPerSide = 44;
        [Range(.45f, .8f)] public float trackWidth = .64f;
        [Range(.045f, .1f)] public float shoeThickness = .074f;
        [Range(.02f, .07f)] public float grouserHeight = .045f;
        [Range(0, .12f)] public float returnSag = .045f;

        public float LeftPhase => left == null ? 0 : Mathf.Repeat(LeftDistanceMeters / left.LoopLength, 1);
        public float RightPhase => right == null ? 0 : Mathf.Repeat(RightDistanceMeters / right.LoopLength, 1);
        public float LeftDistanceMeters { get; private set; }
        public float RightDistanceMeters { get; private set; }
        public float LeftBeltSpeed { get; private set; }
        public float RightBeltSpeed { get; private set; }
        public int TreadsPerSide => treadsPerSide;
        public int SupportRollersPerSide => 6;
        public int SideAssemblyCount => (leftMesh ? 1 : 0) + (rightMesh ? 1 : 0);
        public float LeftLoopLength => left == null ? 0 : left.LoopLength;
        public float RightLoopLength => right == null ? 0 : right.LoopLength;

        Side left, right;
        Vector3 previousLeft, previousRight, previousForward;
        int runSerial;
        bool sampled;

        void Awake()
        {
            if (!HasValidRig()) { Debug.LogError("PLAYER TRACKS: incomplete player-only visual rig.", this); enabled = false; return; }
            left = BuildSide(true); right = BuildSide(false);
            left.Mesh.hideFlags = right.Mesh.hideFlags = HideFlags.DontSave;
            leftMesh.sharedMesh = left.Mesh; rightMesh.sharedMesh = right.Mesh;
            foreach (var renderer in hiddenWheelRenderers) if (renderer) renderer.enabled = false;
            ResetMotion();
        }

        bool HasValidRig() => vehicle && frontLeft && rearLeft && frontRight && rearRight && leftMesh && rightMesh;

        /// <summary>Visual phase reset only. Does not alter any physical body or wheel.</summary>
        public void ResetMotion()
        {
            LeftDistanceMeters = RightDistanceMeters = LeftBeltSpeed = RightBeltSpeed = 0;
            sampled = false;
            if (session) runSerial = session.RunSerial;
        }

        void LateUpdate()
        {
            if (left == null || right == null || !HasValidRig()) return;
            if (session && runSerial != session.RunSerial) ResetMotion();
            Vector3 l = SideAnchor(frontLeft, rearLeft), r = SideAnchor(frontRight, rearRight);
            Vector3 forward = vehicle.transform.forward;
            float dt = Time.deltaTime;
            // Recover/Restart can reposition the chassis. A teleport is not belt travel.
            float discontinuity = Mathf.Max(4, 80 * dt);
            if (sampled && ((l - previousLeft).sqrMagnitude > discontinuity * discontinuity ||
                            (r - previousRight).sqrMagnitude > discontinuity * discontinuity ||
                            Vector3.Dot(forward, previousForward) < .5f)) ResetMotion();
            if (sampled && dt > .00001f)
            {
                Vector3 direction = (previousForward + forward).normalized;
                float dl = Vector3.Dot(l - previousLeft, direction), dr = Vector3.Dot(r - previousRight, direction);
                LeftDistanceMeters += dl; RightDistanceMeters += dr;
                LeftBeltSpeed = dl / dt; RightBeltSpeed = dr / dt;
            }
            else LeftBeltSpeed = RightBeltSpeed = 0;
            previousLeft = l; previousRight = r; previousForward = forward; sampled = true;
            UpdateSide(left, true, LeftDistanceMeters);
            UpdateSide(right, false, RightDistanceMeters);
        }

        Vector3 SideAnchor(WheelCollider a, WheelCollider b) =>
            (a.transform.TransformPoint(a.center) + b.transform.TransformPoint(b.center)) * .5f;

        void OnDestroy()
        {
            if (left != null && left.Mesh) Destroy(left.Mesh);
            if (right != null && right.Mesh) Destroy(right.Mesh);
        }

        /// <summary>Used by the narrow editor installer to persist an Edit Mode preview mesh.</summary>
        public Mesh CreatePreviewMesh(bool isLeft)
        {
            if (!HasValidRig()) throw new InvalidOperationException("Player tracks need four original wheel contacts and two mesh filters.");
            return BuildSide(isLeft).Mesh;
        }

        Side BuildSide(bool isLeft)
        {
            ReadShape(isLeft, out Vector3 rear, out Vector3 front, out float radius);
            float span = Vector3.Distance(front, rear), width = trackWidth;
            float loopLength = 2 * span + 2 * Mathf.PI * radius;
            var builder = new Geometry();
            float pitch = loopLength / Mathf.Max(1, treadsPerSide);
            for (int i = 0; i < treadsPerSide; i++)
            {
                builder.Begin(PartKind.Shoe, i / (float)treadsPerSide, 0);
                builder.BevelBox(Vector3.zero, new Vector3(width, shoeThickness, pitch * .87f), .035f, Steel);
                // Replaceable rubber pads and a broad raised steel grouser, with exposed bright ends.
                builder.Box(new Vector3(-width * .235f, shoeThickness * .57f, 0), new Vector3(width * .40f, .022f, pitch * .63f), Rubber);
                builder.Box(new Vector3(width * .235f, shoeThickness * .57f, 0), new Vector3(width * .40f, .022f, pitch * .63f), Rubber);
                builder.Box(new Vector3(0, shoeThickness * .5f + grouserHeight * .5f, 0),
                    new Vector3(width * .96f, grouserHeight, pitch * .18f), BrightSteel);
                // A central guide tooth on the inside and visible hinge ends make an articulated belt silhouette.
                builder.Box(new Vector3(0, -shoeThickness * .5f - .037f, 0), new Vector3(.065f, .074f, pitch * .44f), DarkSteel);
                for (int sign = -1; sign <= 1; sign += 2)
                    builder.Cylinder(new Vector3(sign * (width * .5f - .015f), 0, -pitch * .31f), .036f, .055f, Brass, 6);
                builder.End();
            }

            // Rear drive sprocket, front idler, six independently positioned lower rollers.
            AddWheel(builder, PartKind.RearWheel, 0, radius * .86f, width, true, isLeft);
            AddWheel(builder, PartKind.FrontWheel, 1, radius * .86f, width, false, isLeft);
            float roadRadius = Mathf.Min(.225f, span * .073f);
            for (int i = 0; i < SupportRollersPerSide; i++)
                AddWheel(builder, PartKind.RoadWheel, .12f + i * .152f, roadRadius, width, false, isLeft);
            for (int i = 0; i < 3; i++)
                AddWheel(builder, PartKind.ReturnWheel, .25f + i * .25f, .105f, width * .72f, false, isLeft);

            builder.Begin(PartKind.Frame, 0, 0);
            float outside = (isLeft ? -1 : 1) * (width * .5f + .018f);
            builder.BevelBox(new Vector3(outside, .115f, 0), new Vector3(.065f, .19f, span * .83f), .035f, Teal);
            builder.Box(new Vector3(outside, .01f, 0), new Vector3(.08f, .065f, span * .93f), DarkSteel);
            builder.Box(new Vector3(outside * 1.01f, .185f, 0), new Vector3(.075f, .035f, span * .66f), BrightSteel);
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-span * .36f, span * .36f, i / 4f);
                builder.Cylinder(new Vector3(outside + Mathf.Sign(outside) * .047f, .11f, z), .033f, .025f, Bolt, 6);
            }
            // Ivory end wear strips and small brass inspection plates match the existing pickup palette.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                builder.BevelBox(new Vector3(outside, .14f, sign * span * .37f), new Vector3(.085f, .25f, .23f), .02f, Dust);
                builder.Box(new Vector3(outside + Mathf.Sign(outside) * .049f, .145f, sign * span * .37f), new Vector3(.018f, .055f, .13f), Brass);
            }
            builder.End();
            var side = builder.Finish(isLeft ? "Player left articulated track" : "Player right articulated track");
            UpdateSide(side, isLeft, 0);
            return side;
        }

        static void AddWheel(Geometry builder, PartKind kind, float fraction, float radius, float width, bool sprocket, bool isLeft)
        {
            builder.Begin(kind, fraction, radius);
            bool small = kind == PartKind.ReturnWheel;
            builder.Cylinder(Vector3.zero, radius, width * .67f, sprocket ? DarkSteel : Rubber, small ? 12 : 18);
            builder.Cylinder(Vector3.zero, radius * .82f, width * .74f, sprocket ? Steel : Teal, small ? 12 : 18);
            int outside = isLeft ? -1 : 1;
            float faceX = outside * width * .395f;
            builder.Cylinder(new Vector3(faceX, 0, 0), radius * .55f, .065f, sprocket ? Brass : Steel, small ? 10 : 14);
            builder.Cylinder(new Vector3(faceX + outside * .037f, 0, 0), radius * .25f, .055f, BrightSteel, 8);
            if (!small)
            {
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3;
                    builder.Cylinder(new Vector3(faceX + outside * .04f, Mathf.Cos(a) * radius * .66f, Mathf.Sin(a) * radius * .66f),
                        .021f, .027f, Bolt, 6);
                }
            }
            if (sprocket)
            {
                for (int i = 0; i < 12; i++)
                {
                    Quaternion q = Quaternion.AngleAxis(i * 30, Vector3.right);
                    builder.Box(q * new Vector3(0, radius + .012f, 0), new Vector3(width * .70f, .065f, .065f), BrightSteel, q);
                }
            }
            builder.End();
        }

        void ReadShape(bool isLeft, out Vector3 rear, out Vector3 front, out float radius)
        {
            var a = isLeft ? rearLeft : rearRight; var b = isLeft ? frontLeft : frontRight;
            rear = WheelCenter(a); front = WheelCenter(b);
            // The outer grouser reaches the same contact envelope as the retained physical wheel.
            radius = Mathf.Max(.25f, (a.radius + b.radius) * .5f - shoeThickness * .5f - grouserHeight);
            // Keep a coherent belt plane; actual suspension still changes the height at both ends.
            rear.x = front.x = (rear.x + front.x) * .5f;
        }

        Vector3 WheelCenter(WheelCollider wheel)
        {
            Vector3 position;
            if (Application.isPlaying) wheel.GetWorldPose(out position, out _);
            else position = wheel.transform.TransformPoint(wheel.center) - wheel.transform.up *
                wheel.suspensionDistance * (1 - wheel.suspensionSpring.targetPosition);
            return transform.InverseTransformPoint(position);
        }

        void UpdateSide(Side side, bool isLeft, float distance)
        {
            ReadShape(isLeft, out Vector3 rear, out Vector3 front, out float radius);
            float span = Mathf.Max(.1f, Vector3.Distance(front, rear));
            Vector3 axis = (front - rear) / span, up = Vector3.Cross(axis, Vector3.right).normalized;
            Quaternion frame = Quaternion.LookRotation(axis, up);
            side.LoopLength = 2 * span + 2 * Mathf.PI * radius;
            foreach (var group in side.Groups)
            {
                Vector3 position; Quaternion rotation;
                switch (group.Kind)
                {
                    case PartKind.Shoe:
                        SampleLoop(rear, front, axis, up, radius, span, group.Fraction * side.LoopLength + distance,
                            out position, out Vector3 tangent, out Vector3 normal);
                        rotation = Quaternion.LookRotation(tangent, normal);
                        break;
                    case PartKind.FrontWheel: position = front; rotation = WheelRotation(frame, distance, group.Radius); break;
                    case PartKind.RearWheel: position = rear; rotation = WheelRotation(frame, distance, group.Radius); break;
                    case PartKind.RoadWheel:
                        position = Vector3.Lerp(rear, front, group.Fraction) + up * (-radius + shoeThickness * .5f + group.Radius);
                        rotation = WheelRotation(frame, distance, group.Radius); break;
                    case PartKind.ReturnWheel:
                        float sag = returnSag * Mathf.Pow(Mathf.Sin(group.Fraction * Mathf.PI), 2);
                        position = Vector3.Lerp(rear, front, group.Fraction) + up * (radius - shoeThickness * .5f - group.Radius - sag);
                        rotation = WheelRotation(frame, distance, group.Radius); break;
                    default: position = (rear + front) * .5f; rotation = frame; break;
                }
                int end = group.Start + group.Count;
                for (int i = group.Start; i < end; i++)
                {
                    side.Vertices[i] = position + rotation * side.RestVertices[i];
                    side.Normals[i] = rotation * side.RestNormals[i];
                }
            }
            side.Mesh.SetVertices(side.Vertices); side.Mesh.SetNormals(side.Normals);
            side.Mesh.bounds = new Bounds((rear + front) * .5f,
                new Vector3(trackWidth + .3f, Mathf.Abs(front.y - rear.y) + radius * 2 + .45f, Mathf.Abs(front.z - rear.z) + radius * 2 + .4f));
        }

        static Quaternion WheelRotation(Quaternion frame, float distance, float radius) =>
            frame * Quaternion.AngleAxis(Mathf.Repeat(distance / Mathf.Max(.01f, radius), Mathf.PI * 2) * Mathf.Rad2Deg, Vector3.right);

        void SampleLoop(Vector3 rear, Vector3 front, Vector3 axis, Vector3 up, float radius, float span, float distance,
            out Vector3 position, out Vector3 tangent, out Vector3 normal)
        {
            float arc = Mathf.PI * radius;
            float s = Mathf.Repeat(distance, span * 2 + arc * 2);
            if (s < span)
            {
                float t = s / span, sin = Mathf.Sin(t * Mathf.PI);
                position = Vector3.Lerp(rear, front, t) + up * (radius - returnSag * sin * sin);
                tangent = (axis - up * (returnSag * Mathf.PI * Mathf.Sin(t * 2 * Mathf.PI) / span)).normalized;
                normal = Vector3.Cross(tangent, Vector3.right).normalized;
            }
            else if ((s -= span) < arc)
            {
                float a = s / radius;
                normal = up * Mathf.Cos(a) + axis * Mathf.Sin(a);
                tangent = -up * Mathf.Sin(a) + axis * Mathf.Cos(a);
                position = front + normal * radius;
            }
            else if ((s -= arc) < span)
            {
                position = Vector3.Lerp(front, rear, s / span) - up * radius; tangent = -axis; normal = -up;
            }
            else
            {
                float a = (s - span) / radius;
                normal = -up * Mathf.Cos(a) - axis * Mathf.Sin(a);
                tangent = up * Mathf.Sin(a) - axis * Mathf.Cos(a);
                position = rear + normal * radius;
            }
        }

        const int Rubber = 0, Steel = 1, BrightSteel = 2, Teal = 3, Brass = 4, Bolt = 5, DarkSteel = 6, Dust = 7;
        enum PartKind { Shoe, FrontWheel, RearWheel, RoadWheel, ReturnWheel, Frame }
        sealed class Group { public PartKind Kind; public float Fraction, Radius; public int Start, Count; }
        sealed class Side
        {
            public Mesh Mesh;
            public Vector3[] RestVertices, RestNormals, Vertices, Normals;
            public Group[] Groups;
            public float LoopLength = 1;
        }

        // One UV palette material and one combined dynamic mesh per side. There are no per-shoe
        // GameObjects, material instances, allocations or mesh topology rebuilds in LateUpdate.
        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> indices = new List<int>();
            readonly List<Group> groups = new List<Group>();
            Group group;
            public void Begin(PartKind kind, float fraction, float radius)
            { group = new Group { Kind = kind, Fraction = fraction, Radius = radius, Start = vertices.Count }; groups.Add(group); }
            public void End() { group.Count = vertices.Count - group.Start; }
            void Vertex(Vector3 p, Vector3 n, int color)
            { vertices.Add(p); normals.Add(n); uv.Add(new Vector2((color + .5f) / 8, .5f)); }
            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int color)
            {
                int k = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var swap = b; b = c; c = swap; }
                Vertex(a, n, color); Vertex(b, n, color); Vertex(c, n, color);
                indices.Add(k); indices.Add(k + 1); indices.Add(k + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int color)
            {
                int k = vertices.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) { var swap = b; b = d; d = swap; }
                Vertex(a, n, color); Vertex(b, n, color); Vertex(c, n, color); Vertex(d, n, color);
                indices.Add(k); indices.Add(k + 1); indices.Add(k + 2); indices.Add(k); indices.Add(k + 2); indices.Add(k + 3);
            }
            public void Box(Vector3 center, Vector3 size, int color, Quaternion? rotation = null)
            {
                Quaternion q = rotation ?? Quaternion.identity; Vector3 h = size * .5f;
                Face(Vector3.right, Vector3.up * h.y, Vector3.forward * h.z, h.x);
                Face(-Vector3.right, Vector3.up * h.y, -Vector3.forward * h.z, h.x);
                Face(Vector3.up, Vector3.forward * h.z, Vector3.right * h.x, h.y);
                Face(-Vector3.up, Vector3.forward * h.z, -Vector3.right * h.x, h.y);
                Face(Vector3.forward, Vector3.right * h.x, Vector3.up * h.y, h.z);
                Face(-Vector3.forward, Vector3.right * h.x, -Vector3.up * h.y, h.z);
                void Face(Vector3 n, Vector3 u, Vector3 v, float depth)
                {
                    Vector3 c = center + q * n * depth;
                    Quad(c - q * u - q * v, c + q * u - q * v, c + q * u + q * v, c - q * u + q * v, q * n, color);
                }
            }
            public void BevelBox(Vector3 center, Vector3 size, float bevel, int color)
            {
                float x = size.x * .5f, z = size.z * .5f, y = size.y * .5f;
                float b = Mathf.Min(bevel, Mathf.Min(x, z) * .45f);
                Vector3[] ring = { new Vector3(-x + b, 0, -z), new Vector3(x - b, 0, -z), new Vector3(x, 0, -z + b),
                    new Vector3(x, 0, z - b), new Vector3(x - b, 0, z), new Vector3(-x + b, 0, z),
                    new Vector3(-x, 0, z - b), new Vector3(-x, 0, -z + b) };
                for (int i = 0; i < 8; i++)
                {
                    Vector3 a = ring[i], d = ring[(i + 1) % 8];
                    Vector3 n = Vector3.Cross(Vector3.up, d - a).normalized;
                    Quad(center + a - Vector3.up * y, center + d - Vector3.up * y, center + d + Vector3.up * y, center + a + Vector3.up * y, n, color);
                    Triangle(center + Vector3.up * y, center + a + Vector3.up * y, center + d + Vector3.up * y, Vector3.up, color);
                    Triangle(center - Vector3.up * y, center + a - Vector3.up * y, center + d - Vector3.up * y, -Vector3.up, color);
                }
            }
            // Axle is local X, with flat end caps and readable low-poly radial facets.
            public void Cylinder(Vector3 center, float radius, float length, int color, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector3 r0 = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)), r1 = new Vector3(0, Mathf.Cos(b), Mathf.Sin(b));
                    Vector3 left = center - Vector3.right * length * .5f, right = center + Vector3.right * length * .5f;
                    Quad(left + r0 * radius, right + r0 * radius, right + r1 * radius, left + r1 * radius, (r0 + r1).normalized, color);
                    Triangle(left, left + r0 * radius, left + r1 * radius, -Vector3.right, color);
                    Triangle(right, right + r0 * radius, right + r1 * radius, Vector3.right, color);
                }
            }
            public Side Finish(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.MarkDynamic(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
                return new Side { Mesh = mesh, RestVertices = vertices.ToArray(), RestNormals = normals.ToArray(),
                    Vertices = new Vector3[vertices.Count], Normals = new Vector3[vertices.Count], Groups = groups.ToArray() };
            }
        }
    }
}
