using System;
using UnityEngine;

namespace RoadGunner
{
    public enum DecorationCrushKind { Vegetation, Wood, Metal, Rubber, Bone }

    /// <summary>Authored soft scenery ranges in a combined mesh. Never a combat or reward target.</summary>
    [DisallowMultipleComponent]
    public sealed class DecorationCrushChunk : MonoBehaviour
    {
        [Serializable]
        public sealed class Item
        {
            public string id;
            public DecorationCrushKind kind;
            public Collider probe;
            [Tooltip("Offsets/counts in the index buffers concatenated in submesh order; multiples of three.")]
            public int[] rangeStarts = Array.Empty<int>();
            public int[] rangeCounts = Array.Empty<int>();
            public Bounds localBounds;
        }

        public MeshFilter source;
        public Item[] items = Array.Empty<Item>();
        public int BrokenCount { get; private set; }
        public Mesh RuntimeMesh => runtimeMesh;
        public Mesh OriginalMesh { get { Initialize(); return originalMesh; } }

        Mesh originalMesh, runtimeMesh;
        int[] originalIndices, workingIndices, submeshStarts, submeshCounts;
        bool[] broken, probeEnabled;
        Item[] boundItems;
        bool initialized;

        void Awake() { Initialize(); }

        void Initialize()
        {
            if (initialized && boundItems == items) return;
            if (!source) source = GetComponent<MeshFilter>();
            if (!source || !source.sharedMesh) return;
            if (!initialized) originalMesh = source.sharedMesh;
            if (runtimeMesh && originalIndices != null)
            {
                Array.Copy(originalIndices, workingIndices, originalIndices.Length);
                ApplyIndices();
            }
            int count = items != null ? items.Length : 0;
            broken = new bool[count]; probeEnabled = new bool[count];
            for (int i = 0; i < count; i++) probeEnabled[i] = items[i] != null && items[i].probe && items[i].probe.enabled;
            boundItems = items; BrokenCount = 0;
            initialized = true;
        }

        public bool IsBroken(int index)
        {
            Initialize();
            return initialized && index >= 0 && index < broken.Length && broken[index];
        }

        public bool ValidItem(int index)
        {
            Initialize();
            if (!initialized || index < 0 || index >= broken.Length || items == null || index >= items.Length) return false;
            var item = items[index];
            if (item == null || item.rangeStarts == null || item.rangeCounts == null || item.rangeStarts.Length == 0 || item.rangeStarts.Length != item.rangeCounts.Length) return false;
            long count = 0;
            for (int i = 0; i < originalMesh.subMeshCount; i++)
            {
                if (originalMesh.GetTopology(i) != MeshTopology.Triangles) return false;
                count += originalMesh.GetIndexCount(i);
            }
            for (int i = 0; i < item.rangeStarts.Length; i++)
            {
                int start = item.rangeStarts[i], length = item.rangeCounts[i];
                if (start < 0 || start % 3 != 0 || length < 3 || length % 3 != 0 || (long)start + length > count) return false;
            }
            return true;
        }

        bool PrepareMutableMesh()
        {
            if (runtimeMesh) return true;
            if (!originalMesh || !originalMesh.isReadable) return false;
            var renderer = source.GetComponent<Renderer>();
            if (renderer && renderer.isPartOfStaticBatch) return false;
            int submeshes = originalMesh.subMeshCount, total = 0;
            submeshStarts = new int[submeshes]; submeshCounts = new int[submeshes];
            for (int s = 0; s < submeshes; s++)
            {
                submeshStarts[s] = total;
                submeshCounts[s] = checked((int)originalMesh.GetIndexCount(s));
                total = checked(total + submeshCounts[s]);
            }
            originalIndices = new int[total];
            for (int s = 0; s < submeshes; s++) Array.Copy(originalMesh.GetIndices(s), 0, originalIndices, submeshStarts[s], submeshCounts[s]);
            workingIndices = (int[])originalIndices.Clone();
            runtimeMesh = Instantiate(originalMesh);
            runtimeMesh.name = originalMesh.name + " (run scenery copy)";
            runtimeMesh.hideFlags = HideFlags.DontSave;
            runtimeMesh.MarkDynamic();
            if (runtimeMesh.vertexCount > 65535 && runtimeMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
            {
                runtimeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                runtimeMesh.subMeshCount = submeshes;
                ApplyIndices();
            }
            source.sharedMesh = runtimeMesh;
            return true;
        }

        /// <summary>Atomic, idempotent visual break. Only ArenaDecorationCrush decides whether a vehicle may cause it.</summary>
        public bool Break(int index)
        {
            if (!ValidItem(index) || broken[index] || !PrepareMutableMesh()) return false;
            var item = items[index];
            broken[index] = true; BrokenCount++;
            for (int r = 0; r < item.rangeStarts.Length; r++)
            {
                int end = item.rangeStarts[r] + item.rangeCounts[r];
                for (int i = item.rangeStarts[r]; i < end; i += 3)
                    workingIndices[i + 1] = workingIndices[i + 2] = workingIndices[i];
            }
            ApplyIndices();
            if (item.probe) item.probe.enabled = false;
            return true;
        }

        void ApplyIndices()
        {
            // Keep material/submesh boundaries. The original mesh and its asset buffers are never edited.
            for (int s = 0; s < submeshCounts.Length; s++)
                runtimeMesh.SetTriangles(workingIndices, submeshStarts[s], submeshCounts[s], s, false, 0);
        }

        public Bounds GetWorldBounds(int index)
        {
            Initialize();
            if (!initialized || items == null || index < 0 || index >= items.Length || items[index] == null) return new Bounds(transform.position, Vector3.zero);
            Bounds local = items[index].localBounds;
            var matrix = source.transform.localToWorldMatrix;
            Vector3 e = local.extents;
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(e, new Vector3(x, y, z))));
            return result;
        }

        public void ResetChunk()
        {
            Initialize();
            if (!initialized) return;
            Array.Clear(broken, 0, broken.Length); BrokenCount = 0;
            if (runtimeMesh && originalIndices != null)
            {
                Array.Copy(originalIndices, workingIndices, originalIndices.Length);
                ApplyIndices();
                if (source) source.sharedMesh = runtimeMesh;
            }
            for (int i = 0; i < broken.Length && i < items.Length; i++)
                if (items[i] != null && items[i].probe) items[i].probe.enabled = probeEnabled[i];
        }

        void OnDestroy()
        {
            if (!runtimeMesh) return;
            if (source && source.sharedMesh == runtimeMesh) source.sharedMesh = originalMesh;
            Destroy(runtimeMesh);
        }
    }
}
