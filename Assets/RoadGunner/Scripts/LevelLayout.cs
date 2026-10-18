using UnityEngine;

namespace RoadGunner
{
    public sealed class LevelLayout : MonoBehaviour
    {
        [Header("Reference status (editor documentation)")]
        public string referenceUrl = "https://www.youtube.com/watch?v=i2-6e8ymC_8";
        public bool referenceInspected;
        public float requestedFootprintScale = 2;
        public float appliedFootprintScale = 1;
        [TextArea(2, 5)] public string authoringNotes =
            "Reference video inaccessible (YouTube CAPTCHA). Existing prototype layout retained. " +
            "Reference areas, routes, encounters and doubled footprint still require inspected frames.";

        [Header("Level authoring")]
        public string levelName;
        public MeshFilter ground;
        public Mesh baselineTerrain;
        public Bounds gameplayBounds = new Bounds(Vector3.zero, new Vector3(100, 20, 100));
        public bool navigationNeedsBake;
        public Transform authoredContentRoot, humanEnemiesRoot, enemyVehiclesRoot, obstaclesRoot, decorationRoot;

        [Header("Editable spawn and recovery markers")]
        public GameSession session;
        public Transform playerSpawn;
        public Transform[] recoveryPoints;

        void Awake()
        {
            if (!session) return;
            if (playerSpawn)
            {
                session.startPosition = playerSpawn.position;
                session.startRotation = playerSpawn.rotation;
            }
            if (recoveryPoints == null || recoveryPoints.Length == 0) return;
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var point in recoveryPoints) if (point) points.Add(point.position);
            if (points.Count > 0) session.recoveryPoints = points.ToArray();
        }

        void OnDrawGizmosSelected()
        {
            if (!playerSpawn) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(playerSpawn.position + Vector3.up * .4f, new Vector3(2.25f, 1, 4.8f));
            Gizmos.DrawLine(playerSpawn.position, playerSpawn.position + playerSpawn.forward * 5);
        }
    }
}
