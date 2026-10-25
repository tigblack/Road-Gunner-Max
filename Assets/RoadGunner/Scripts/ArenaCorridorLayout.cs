using UnityEngine;

namespace RoadGunner
{
    /// <summary>Authored route/landmark data for the existing Arena; no runtime spawning or progression replacement.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaCorridorLayout : MonoBehaviour
    {
        public Vector3[] mainRoad, bossRoad, monsterRoad, workshopRoad;
        public Vector3[] startHealthPoints, mapFootprint;
        public Bounds authoredBounds;
        public Transform geometryRoot, roadsRoot, blockersRoot;
        public Transform workshop, bossArea, monsterArea, lockedGate, lockedApproach, lockedArena;
        public Collider[] blockers, gateColliders;
        public Transform[] preservedLegacyProps;
        public float roadWidth = 14, branchWidth = 16;
        [Tooltip("The existing project has no quest-based gate unlock. This north gate is visibly and physically closed.")]
        public bool hasRuntimeUnlockCondition;
    }
}
