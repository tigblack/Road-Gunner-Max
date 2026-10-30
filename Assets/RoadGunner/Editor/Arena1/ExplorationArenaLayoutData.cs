using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Editable authoring source. W is screen east/north, 2.5 metres per unit, yaw 45.</summary>
    public sealed class ExplorationArenaLayoutData : ScriptableObject
    {
        [Serializable] public sealed class Route
        {
            public string id, loopId;
            [Min(8)] public float widthMeters = 14;
            public Vector2[] nodesW;
        }
        [Serializable] public sealed class Pocket
        {
            public string id;
            public Vector2 centerW;
            [Min(8)] public float radiusMeters = 18;
        }
        [Serializable] public sealed class Region
        {
            public string id, displayName;
            [TextArea] public string hint;
            public Vector2 centerW;
            public Vector2 landmarkW;
            public float radiusMeters = 80;
            public Color sandColor;
        }
        [Serializable] public sealed class Cache
        {
            public string id;
            public Vector2 positionW;
            public ArenaRewardKind reward;
            public bool overrideTuningAmount;
            public float amount = 5, repairAmount;
            [Min(.1f)] public float durability = 10.5f;
            public bool secret;
        }
        [Serializable] public sealed class BaselineMove
        {
            public string globalId, hierarchy, reason;
            public Vector3 position;
            public Quaternion rotation;
        }
        [Serializable] public sealed class BaselineSurface
        {
            public string globalId;
            public Material[] materials;
        }

        [Header("Arena source bounds, existing open perimeter (not new walls)")]
        public Vector2 minW = new Vector2(-46, -63), maxW = new Vector2(48, 74);
        [Min(.5f)] public float surfaceCellMeters = 2;
        [HideInInspector] public int geometryRevision;
        public Route[] routes;
        public Pocket[] pockets;
        public Region[] regions;
        public Cache[] caches;
        public Vector2[] recoveryLaybysW;
        [HideInInspector] public List<BaselineMove> baselineMoves = new List<BaselineMove>();
        [HideInInspector] public List<BaselineSurface> baselineSurfaces = new List<BaselineSurface>();

        static Vector2 P(float east, float north) => new Vector2(east, north);
        static Route Road(string id, string loop, float width, params Vector2[] nodes) =>
            new Route { id = id, loopId = loop, widthMeters = width, nodesW = nodes };
        static Pocket Clearing(string id, float e, float n, float radius) =>
            new Pocket { id = id, centerW = P(e, n), radiusMeters = radius };
        static Cache Reward(string id, float e, float n, ArenaRewardKind kind, float amount, bool secret = false, float repair = 0) =>
            new Cache { id = id, positionW = P(e, n), reward = kind, amount = amount, secret = secret, repairAmount = repair };

        public void SetDefaults()
        {
            routes = new[] {
                Road("main-spine", "spine", 20, P(0,-48),P(0,-34),P(1.5f,-25),P(0,-5),P(0,22),P(-1,42),P(0,60)),
                Road("workshop-drive-through", "service-loop", 8, P(0,-28),P(9,-24),P(12,-18),P(12,-12),P(12,-5),P(4,3),P(0,8)),
                Road("salvage-loop", "salvage-loop", 14, P(0,-30),P(-10,-31),P(-22,-23),P(-25,-14),P(-20,-3),P(-12,3),P(0,-5)),
                Road("salvage-north-return", "salvage-loop", 14, P(-12,3),P(-15,10),P(-10,18),P(0,22)),
                Road("warlord-lower-road", "warlord-loop", 18, P(0,22),P(-16,24),P(-29,26),P(-35,30),P(-33,35)),
                Road("warlord-upper-bypass", "warlord-loop", 14, P(0,22),P(-8,35),P(-16,44),P(-28,44),P(-33,35)),
                Road("closed-gate-approach", "gate-spur", 14, P(-30,24),P(-42,24)),
                Road("rustworks-lower-road", "rustworks-loop", 18, P(0,22),P(14,22),P(24,29),P(31,38),P(32,49)),
                Road("rustworks-upper-bypass", "rustworks-loop", 14, P(0,36.5f),P(16,36.5f),P(24,40),P(31,38)),
                Road("ridge-crosslink", "ridge-loop", 14, P(-16,44),P(-4,51),P(12,49),P(24,47),P(32,49)),
                Road("ashmaw-approach", "ashmaw-branch", 14, P(32,49),P(34,53),P(38,56)),
                Road("secret-scrap-gully", "secret-west", 10, P(-25,-14),P(-33,-10),P(-36,-5)),
                Road("secret-wreck-overlook", "secret-north", 10, P(-16,44),P(-20,50),P(-28,56)),
                Road("secret-dry-cistern", "secret-east", 10, P(24,29),P(29,20),P(35,16),P(38,10))
            };
            pockets = new[] {
                Clearing("start-turning-bowl",0,-47,16), Clearing("first-raider-pocket",2,-33,19),
                Clearing("salvage-camp",-21,-16,19), Clearing("salvage-north-pocket",-16,4,19),
                Clearing("main-crossroads",0,22,22), Clearing("rustworks-camp",20,34,23),
                Clearing("warlord-arena",-33,35,18), Clearing("ashmaw-arena",37.5f,56,11.5f),
                Clearing("ashmaw-approach-bowl",30.5f,52.5f,13),
                Clearing("secret-west-turnaround",-35,-5,10.5f), Clearing("secret-north-turnaround",-28,56,12),
                Clearing("secret-east-turnaround",37,10,10.5f)
            };
            regions = new[] {
                new Region { id="dust-flats",displayName="DUST FLATS",hint="SUPPLIES: auto-fire breaks marked crates",centerW=P(0,-37),landmarkW=P(-10,-39),radiusMeters=85,sandColor=new Color(.90f,.66f,.40f) },
                new Region { id="salvage-yard",displayName="SALVAGE YARD",hint="SIDE ROADS: secrets and free repairs",centerW=P(-24,-5),landmarkW=P(-33,-14),radiusMeters=90,sandColor=new Color(.68f,.56f,.36f) },
                new Region { id="rustworks",displayName="RUSTWORKS",hint="BUGGIES: RAM DAMAGE +{buggyRamBonus}%",centerW=P(24,25),landmarkW=P(35,29),radiusMeters=65,sandColor=new Color(.81f,.47f,.29f) },
                new Region { id="ash-ridge",displayName="ASH RIDGE",hint="WARLORD: AFTER STRIKE, BULLETS +{bossRecoveryBonus}%",centerW=P(7,52),landmarkW=P(-9,57),radiusMeters=130,sandColor=new Color(.49f,.47f,.43f) }
            };
            caches = new[] {
                Reward("crate-damage-south",-2,-27,ArenaRewardKind.Damage,5),
                Reward("crate-health-salvage",-18,-13,ArenaRewardKind.Health,5),
                Reward("crate-health-crossroads",7,28,ArenaRewardKind.Health,5),
                Reward("crate-ram-ridge",-7,37,ArenaRewardKind.Ram,10),
                Reward("crate-damage-rustworks",20,33,ArenaRewardKind.Damage,5),
                Reward("secret-scrap-gully",-36,-5,ArenaRewardKind.Scrap,3,true,15),
                Reward("secret-wreck-overlook",-28,56,ArenaRewardKind.Scrap,3,true,20),
                Reward("secret-dry-cistern",38,10,ArenaRewardKind.Scrap,3,true,15)
            };
            recoveryLaybysW = new[] { P(0,-48), P(12,-12), P(-12,-20), P(0,22), P(-23,36), P(22,44), P(34,55) };
            geometryRevision = 3;
        }

        /// <summary>Only the first installed geometry is migrated; rewards, UI, regions and tuning are retained.</summary>
        public bool UpgradeGeometry()
        {
            if (geometryRevision >= 3) return false;
            if (geometryRevision < 2)
            {
            foreach (var route in routes)
            {
                if (route.id == "warlord-lower-road") route.nodesW = new[] { P(0,22),P(-16,24),P(-29,26),P(-35,30),P(-33,35) };
                if (route.id == "ashmaw-approach") route.nodesW = new[] { P(32,49),P(34,53),P(38,56) };
            }
            foreach (var pocket in pockets)
            {
                if (pocket.id == "ashmaw-arena") { pocket.centerW = P(37.5f,56); pocket.radiusMeters = 11.5f; }
                if (pocket.id == "warlord-arena") pocket.radiusMeters = 18;
                if (pocket.id == "secret-west-turnaround") { pocket.centerW = P(-35,-5); pocket.radiusMeters = 10; }
                if (pocket.id == "secret-east-turnaround") { pocket.centerW = P(37,10); pocket.radiusMeters = 10; }
            }
            if (!Array.Exists(pockets, p => p.id == "ashmaw-approach-bowl"))
            {
                var expanded = new List<Pocket>(pockets) { Clearing("ashmaw-approach-bowl",30.5f,52.5f,13) };
                pockets = expanded.ToArray();
            }
            foreach (var region in regions) if (region.id == "rustworks") region.radiusMeters = 65;
            }
            foreach (var pocket in pockets)
                if (pocket.id == "secret-west-turnaround" || pocket.id == "secret-east-turnaround")
                    pocket.radiusMeters = Mathf.Max(10.5f, pocket.radiusMeters);
            geometryRevision = 3; return true;
        }
    }
}
