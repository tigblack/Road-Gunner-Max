using UnityEngine;

namespace RoadGunner
{
    /// <summary>Runtime-only translation of the existing barrel vertices and its real muzzle. Never edits a shared asset or physical body.</summary>
    [DisallowMultipleComponent]
    public sealed class CannonVisualRecoil : MonoBehaviour
    {
        public float RecoilMeters { get; private set; }
        public int MovingVertexCount { get; private set; }
        public Mesh RuntimeMesh => runtimeMesh;
        public Mesh OriginalMesh => originalMesh;
        public MeshFilter BarrelSource => source;
        TurretController turret;
        MeshFilter source;
        Mesh originalMesh, runtimeMesh;
        Vector3[] restVertices, vertices;
        int[] movingVertices;
        Vector3 barrelAxis, muzzleAxis, restMuzzlePosition;
        float sinceShot = float.PositiveInfinity;
        int runSerial, poseSerial;
        bool initialized;

        public void Bind(TurretController controller)
        {
            if (initialized || !controller || !controller.pitch || !controller.muzzle) return;
            turret = controller;
            // This is the saved player's combined cannon; receivers and mount vertices remain fixed.
            var node = controller.pitch.Find("Walker shoulder cannon");
            source = node ? node.GetComponent<MeshFilter>() : null;
            if (!source || !source.sharedMesh || !source.sharedMesh.isReadable) return;
            originalMesh = source.sharedMesh;
            restVertices = originalMesh.vertices; vertices = (Vector3[])restVertices.Clone();
            Vector3 tip = source.transform.InverseTransformPoint(controller.muzzle.position);
            barrelAxis = source.transform.InverseTransformDirection(controller.muzzle.forward).normalized;
            Vector3 rear = tip - barrelAxis * Mathf.Min(1.10f, Mathf.Max(.55f, tip.magnitude - .20f));
            float firstBarrel = Vector3.Dot(rear, barrelAxis) - .04f;
            int count = 0;
            for (int i = 0; i < restVertices.Length; i++) if (Vector3.Dot(restVertices[i], barrelAxis) >= firstBarrel) count++;
            if (count == 0 || count == restVertices.Length) { source = null; originalMesh = null; return; }
            movingVertices = new int[count]; count = 0;
            for (int i = 0; i < restVertices.Length; i++) if (Vector3.Dot(restVertices[i], barrelAxis) >= firstBarrel) movingVertices[count++] = i;
            MovingVertexCount = count;
            restMuzzlePosition = controller.muzzle.localPosition;
            muzzleAxis = controller.muzzle.parent.InverseTransformVector(controller.muzzle.forward).normalized;
            runtimeMesh = Instantiate(originalMesh); runtimeMesh.name = originalMesh.name + " (runtime barrel recoil)";
            runtimeMesh.hideFlags = HideFlags.DontSave; runtimeMesh.MarkDynamic(); source.sharedMesh = runtimeMesh;
            var bounds = originalMesh.bounds; bounds.Encapsulate(bounds.min - barrelAxis * .18f); bounds.Encapsulate(bounds.max - barrelAxis * .18f); runtimeMesh.bounds = bounds;
            initialized = true; CaptureSerials();
        }

        void CaptureSerials()
        {
            if (!turret) return;
            if (turret.session) runSerial = turret.session.RunSerial;
            if (turret.session && turret.session.vehicle) poseSerial = turret.session.vehicle.PoseResetSerial;
        }

        /// <summary>Called after the gait, before aiming and hitscan. A shot starts its kick on the next frame.</summary>
        public void PreparePose(float dt)
        {
            if (!initialized || !turret || !turret.tuning) return;
            var session = turret.session;
            if (session && (runSerial != session.RunSerial || (session.vehicle && poseSerial != session.vehicle.PoseResetSerial))) { ResetRecoil(); CaptureSerials(); }
            if (session && !session.Playing) { if (session.State != SessionState.Paused) ResetRecoil(); return; }
            if (dt <= 0) return;
            sinceShot += dt;
            var tuning = turret.tuning;
            float kick = Mathf.Max(.005f, tuning.cannonRecoilKickSeconds), recovery = Mathf.Max(.04f, tuning.cannonRecoilReturnSeconds);
            float envelope = sinceShot < kick ? Mathf.SmoothStep(0,1,sinceShot/kick) : 1 - Mathf.SmoothStep(0,1,(sinceShot-kick)/recovery);
            Apply(Mathf.Clamp(tuning.cannonRecoilMeters,0,.18f) * envelope);
        }

        public void Shot()
        {
            if (!initialized) return;
            // No pose change at this call: the rendered muzzle, flash, tracer and captured hitscan share the exact firing pose.
            // The next PreparePose begins one bounded impulse, never additive travel during a burst.
            sinceShot = 0;
        }

        void Apply(float amount)
        {
            if (Mathf.Abs(amount-RecoilMeters) < .000001f) return;
            RecoilMeters = amount;
            for (int i = 0; i < movingVertices.Length; i++) { int vertex = movingVertices[i]; vertices[vertex] = restVertices[vertex] - barrelAxis * amount; }
            if (runtimeMesh) runtimeMesh.SetVertices(vertices);
            if (turret && turret.muzzle) turret.muzzle.localPosition = restMuzzlePosition - muzzleAxis * amount;
        }

        public void ResetRecoil() { sinceShot = float.PositiveInfinity; if (initialized) Apply(0); }
        void OnDisable() { ResetRecoil(); }
        void OnDestroy()
        {
            ResetRecoil();
            if (source && source.sharedMesh == runtimeMesh) source.sharedMesh = originalMesh;
            if (runtimeMesh) Destroy(runtimeMesh);
        }
    }
}
