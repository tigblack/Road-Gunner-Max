using System;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Our vehicle adaptation of the observed June reference topology. W is 2.5 m, yaw45.
    /// The 1.5 ratio applies to a normalized design, not to unknown reference-world metres.</summary>
    public sealed class ReferenceArenaLayoutData : ScriptableObject
    {
        public const int CurrentRevision = 1;
        public int revision;
        public float widthScale = 1.5f;
        public ExplorationArenaLayoutData geometry;
        public Vector2 crossW = new Vector2(0, 8), spawnW = new Vector2(0, -48);
        public Vector2 workshopW = new Vector2(22, -14), bunkerW = new Vector2(25, -38);
        public Vector2 southwestPenW = new Vector2(-34, -24), westFieldW = new Vector2(-28, -4);
        public Vector2 northeastCampW = new Vector2(29, 28);
        public Vector2 bossW = new Vector2(-40, 25), monsterW = new Vector2(40, 38);
        public Vector2[] raidersW = {
            new Vector2(-3,-30),new Vector2(3,-30),new Vector2(-23,-3),new Vector2(-32,-2),
            new Vector2(-27,-8),new Vector2(-33,-22),new Vector2(-36,-27),new Vector2(30,-3),
            new Vector2(35,0),new Vector2(19,22),new Vector2(28,29),new Vector2(36,20)
        };
        public Vector2[] buggiesW = { new Vector2(-29,-26),new Vector2(26,26) };
        /// <summary>West, East, North, in that stable order.</summary>
        public Vector2[] gatesW = { new Vector2(-62,8),new Vector2(62,8),new Vector2(0,58) };

        static Vector2 P(float x,float y) => new Vector2(x,y);
        static ExplorationArenaLayoutData.Route R(string id,string group,float width,params Vector2[] nodes) =>
            new ExplorationArenaLayoutData.Route { id=id,loopId=group,widthMeters=width,nodesW=nodes };
        static ExplorationArenaLayoutData.Pocket C(string id,float x,float y,float radius) =>
            new ExplorationArenaLayoutData.Pocket { id=id,centerW=P(x,y),radiusMeters=radius };
        static ExplorationArenaLayoutData.Cache Reward(string id,float x,float y,ArenaRewardKind reward,bool secret=false,float repair=0) =>
            new ExplorationArenaLayoutData.Cache { id=id,positionW=P(x,y),reward=reward,amount=reward==ArenaRewardKind.Ram?10:reward==ArenaRewardKind.Scrap?3:5,
                durability=10.5f,secret=secret,repairAmount=repair };

        public void InitializeGeometry()
        {
            if (!geometry) throw new InvalidOperationException("Assign the common Exploration geometry asset first.");
            geometry.minW=P(-70,-58);geometry.maxW=P(70,66);geometry.surfaceCellMeters=2;
            geometry.routes=new[] {
                R("main-south","crossroads",30,P(0,-48),P(0,-22),P(0,8)),
                R("main-north","crossroads",30,P(0,8),P(0,30),P(0,51)),
                R("cross-west","crossroads",27,P(0,8),P(-28,8),P(-55,8)),
                R("cross-east","crossroads",27,P(0,8),P(28,8),P(55,8)),
                R("workshop-south-access","service-loop",18,P(0,-26),P(12,-26),P(22,-25)),
                R("workshop-drive-through","service-loop",18,P(22,-25),P(22,-14),P(22,-3)),
                R("workshop-north-access","service-loop",18,P(22,-3),P(22,8)),
                R("southwest-guard-access","guard-access",18,P(0,-16),P(-17,-16),P(-34,-24)),
                R("west-field-access","guard-access",18,P(0,8),P(-16,-4),P(-28,-4)),
                R("northeast-camp-access","camp-access",18,P(0,30),P(15,30),P(29,28)),
                R("monster-camp-branch","camp-access",18,P(29,28),P(39,30),P(40,38)),
                R("northwest-guard-branch","guard-access",18,P(-30,8),P(-40,15),P(-40,25)),
                R("bunker-landmark-access","landmark-access",18,P(22,-25),P(25,-31),P(25,-38)),
                R("secret-west-spur","secret-access",18,P(-34,-24),P(-46,-18),P(-50,-15)),
                R("secret-northeast-spur","secret-access",18,P(40,38),P(44,43),P(45,47)),
                R("secret-southeast-spur","secret-access",18,P(25,-38),P(33,-39),P(38,-37))
            };
            geometry.pockets=new[] {
                C("start-turning-bowl",0,-47,18),C("first-raider-pocket",0,-30,23),
                C("main-crossroads",0,8,33),C("west-field",-28,-4,30),C("southwest-guard-pen",-34,-24,26),
                C("repair-hub",22,-14,25),C("bunker-courtyard",25,-38,22),
                C("northeast-camp",29,28,37),C("monster-arena",40,38,22),C("northwest-guard",-40,25,28),
                C("secret-west-turnaround",-50,-15,15),C("secret-east-turnaround",45,47,15),C("secret-south-turnaround",38,-37,15)
            };
            geometry.regions=new[] {
                new ExplorationArenaLayoutData.Region { id="dust-flats",displayName="DUST FLATS",hint="DEFEAT WARLORD · MONSTER OPTIONAL",centerW=P(0,-35),landmarkW=P(-13,-40),radiusMeters=110,sandColor=new Color(.90f,.66f,.40f) },
                new ExplorationArenaLayoutData.Region { id="salvage-yard",displayName="GUARD OUTPOST",hint="BREAK SUPPLIES · WATCH THE ROAD",centerW=P(-32,-15),landmarkW=P(-48,-26),radiusMeters=105,sandColor=new Color(.68f,.56f,.36f) },
                new ExplorationArenaLayoutData.Region { id="rustworks",displayName="RUSTWORKS CAMP",hint="MONSTER OPTIONAL · BUGGIES: RAM +{buggyRamBonus}%",centerW=P(31,27),landmarkW=P(46,25),radiusMeters=105,sandColor=new Color(.81f,.47f,.29f) },
                new ExplorationArenaLayoutData.Region { id="ash-ridge",displayName="WARLORD RIDGE",hint="WARLORD: AFTER STRIKE, BULLETS +{bossRecoveryBonus}%",centerW=P(-35,30),landmarkW=P(-51,33),radiusMeters=110,sandColor=new Color(.49f,.47f,.43f) }
            };
            geometry.caches=new[] {
                Reward("crate-damage-south",6,-24,ArenaRewardKind.Damage),Reward("crate-health-salvage",-24,-13,ArenaRewardKind.Health),
                Reward("crate-health-crossroads",11,0,ArenaRewardKind.Health),Reward("crate-ram-ridge",-29,23,ArenaRewardKind.Ram),
                Reward("crate-damage-rustworks",22,23,ArenaRewardKind.Damage),
                Reward("secret-scrap-gully",-50,-15,ArenaRewardKind.Scrap,true,15),Reward("secret-wreck-overlook",45,47,ArenaRewardKind.Scrap,true,20),
                Reward("secret-dry-cistern",38,-37,ArenaRewardKind.Scrap,true,15)
            };
            geometry.recoveryLaybysW=new[] {spawnW,P(14,-14),P(-20,-22),P(0,8),P(-30,24),P(20,34),P(35,39)};
            geometry.baselineMoves.Clear();geometry.baselineSurfaces.Clear();geometry.geometryRevision=100;
            revision=CurrentRevision;
        }
    }
}
