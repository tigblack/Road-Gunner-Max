using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    // Visual authoring only. Gameplay roots, colliders, wheel colliders, tuning and pivots are retained.
    public static class ConceptEnemies
    {
        static readonly string[] Palette = { "Sand", "SandLight", "SandShade", "Track", "Rock", "RockLight", "RockShade", "Teal", "TealLight", "TealDark", "Ivory", "IvoryShade", "Rust", "RustDark", "Steel", "SteelLight", "Rubber", "RubberEdge", "Cloth", "Scarf", "Skin", "Glass", "Wood", "WoodLight", "Mint", "Glow", "Red", "Yellow", "Cactus", "DryLeaves" };
        static readonly GameObject[] raiderModels = new GameObject[3];
        static Material atlas;
        static Mesh spike, gear;
        static readonly Quaternion AcrossAxle = Quaternion.Euler(0, 0, 90);
        static readonly Quaternion AlongBarrel = Quaternion.Euler(90, 0, 0);

        public static void ApplyRaider(Raider r, int variant = 0)
        {
            if (!r) return;
            variant = Mathf.Clamp(variant, 0, 2);
            EnsureAtlas();
            if (!raiderModels[variant]) raiderModels[variant] = BuildRaiderModel(variant);
            if (r.visual) Object.DestroyImmediate(r.visual.gameObject);
            var model = Object.Instantiate(raiderModels[variant], r.transform);
            model.name = "Concept raider rig — " + new[] { "Scavenger", "Enforcer", "Veteran" }[variant];
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            r.visual = model.transform;
            r.leftLeg = model.transform.Find("Left leg");
            r.rightLeg = model.transform.Find("Right leg");
            r.leftArm = model.transform.Find("Left arm");
            r.rightArm = model.transform.Find("Right arm");
            r.torso = model.transform.Find("Body").GetComponent<MeshRenderer>();
            Layer(model.transform, r.gameObject.layer);
            EditorUtility.SetDirty(r);
        }

        static GameObject BuildRaiderModel(int variant)
        {
            string label = new[] { "Scavenger", "Enforcer", "Veteran" }[variant];
            var rig = ArtKit.Node("Raider_" + label + "_Model");
            var body = ArtKit.Node("Body", rig.transform).transform;
            float broad = variant == 0 ? 1f : variant == 1 ? 1.10f : 1.18f;

            // Core torso and pelvis loft
            ArtKit.Part(body, "Tailored ragged jacket", TorsoLoft(broad), Vector3.zero, Vector3.one, variant == 2 ? "RustDark" : "Cloth");
            ArtKit.Part(body, "Sculpted trouser pelvis", OrganicLoft("Trouser pelvis", new[] { .665f, .735f, .84f, .90f }, new[] { .45f * broad, .55f * broad, .55f * broad, .47f * broad }, new[] { .34f, .40f, .40f, .34f }, new[] { -.005f, -.005f, -.005f, -.005f }, 16, .72f), Vector3.zero, Vector3.one, "Cloth");
            ArtKit.Box(body, "Heavy leather waist belt", V(0, .86f, .015f), V(.59f * broad, .13f, .44f), "RustDark", .025f);
            ArtKit.Box(body, "Skull steel buckle", V(0, .865f, .245f), V(.15f, .12f, .048f), variant == 2 ? "Yellow" : "Ivory", .015f);
            ArtKit.Box(body, "Buckle center", V(0, .865f, .272f), V(.07f, .06f, .018f), variant == 2 ? "Glow" : "Red", .006f);

            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Box(body, "Salvaged belt pouch", V(side * .25f * broad, .855f, .21f), V(.19f, .21f, .13f), side < 0 ? "IvoryShade" : "RustDark", .025f);
                ArtKit.Box(body, "Pouch flap", V(side * .25f * broad, .927f, .286f), V(.18f, .055f, .035f), "Red", .012f);
                ArtKit.Beam(body, "Broad scavenged harness", V(side * .24f * broad, 1.42f, .18f), V(side * .17f * broad, .94f, .252f), .078f, "RustDark");
                ArtKit.Box(body, "Harness clasp", V(side * .205f * broad, 1.14f, .278f), V(.10f, .06f, .025f), "Yellow", .009f);
                var flap = ArtKit.Box(body, "Uneven jacket tail", V(side * .185f * broad, .775f, .174f), V(.25f, .26f, .095f), variant == 2 ? "Scarf" : "Cloth", .025f);
                flap.localRotation = Quaternion.Euler(0, side * 5, side * 6);
            }

            // Sculpted breastplate with archetype markings
            ArtKit.Part(body, "Curved tapered scavenged breastplate", SculptedPlate(.58f * broad, .435f, .12f), V(.012f, 1.205f, .235f), Vector3.one, variant == 2 ? "Red" : "Steel", Quaternion.Euler(-5, 0, 0));
            ArtKit.Beam(body, "Breastplate softened upper rim", V(-.19f * broad, 1.408f, .268f), V(.19f * broad, 1.408f, .268f), .045f, variant == 2 ? "Yellow" : "SteelLight");
            ArtKit.Part(body, "Breastplate warpaint chevron", SculptedPlate(.32f * broad, .16f, .012f), V(0, 1.22f, .308f), Vector3.one, variant == 0 ? "Red" : "Ivory", Quaternion.Euler(-5, 0, 0));
            ArtKit.Part(body, "Chest softened edge wear", SculptedPlate(.11f, .04f, .009f), V(.16f * broad, 1.33f, .305f), Vector3.one, "SteelLight", Quaternion.Euler(-5, 0, 12));

            // Variant-specific torso & back silhouettes
            if (variant == 0)
            {
                // Scavenger: diagonal power-cell bandolier + twin-pipe exhaust & radio antenna backpack
                for (int c = 0; c < 3; c++)
                {
                    Vector3 cp = V(-.14f + c * .13f, 1.28f - c * .09f, .315f);
                    ArtKit.Cylinder(body, "Bandolier cell " + c, cp, .032f, .09f, c == 1 ? "Glow" : "Yellow", Quaternion.Euler(0, 0, -35), 8);
                }
                ArtKit.Box(body, "Scavenger backpack frame", V(0, 1.20f, -.25f), V(.36f, .34f, .16f), "RustDark", .03f);
                ArtKit.Cylinder(body, "Scavenger power canister", V(-.09f, 1.18f, -.32f), .065f, .28f, "Red", null, 10);
                ArtKit.Cylinder(body, "Scavenger exhaust pipe", V(.12f, 1.34f, -.29f), .038f, .46f, "SteelLight", Quaternion.Euler(-8, 0, -6), 10);
                ArtKit.Cylinder(body, "Scavenger whip antenna", V(-.13f, 1.54f, -.26f), .014f, .52f, "SteelLight", Quaternion.Euler(-10, 0, 8), 6);
                ArtKit.Box(body, "Antenna signal pennant", V(-.17f, 1.72f, -.32f), V(.016f, .10f, .16f), "Glow", .005f, Quaternion.Euler(0, 15, 0));
            }
            else if (variant == 1)
            {
                // Enforcer: heavy abdominal armor + pressurized dual chem-tanks on back
                ArtKit.Part(body, "Enforcer abdominal plate", SculptedPlate(.46f, .17f, .075f), V(0, 1.01f, .242f), Vector3.one, "Red");
                for (int side = -1; side <= 1; side += 2)
                {
                    ArtKit.Cylinder(body, "Back chem tank", V(side * .13f, 1.24f, -.27f), .085f, .38f, "Yellow", null, 12);
                    ArtKit.Cylinder(body, "Tank top valve", V(side * .13f, 1.45f, -.27f), .045f, .06f, "Red", null, 8);
                    ArtKit.Beam(body, "Tank harness strap", V(side * .13f, 1.24f, -.36f), V(side * .24f, 1.24f, -.16f), .035f, "Steel");
                }
                ArtKit.Cylinder(body, "Back pressure gauge", V(0, 1.32f, -.33f), .048f, .03f, "Glow", AlongBarrel, 10);
            }
            else
            {
                // Veteran / Warlord Boss: Ivory rib armor, cape mantle, and towering Trophy Banner Rack!
                ArtKit.Part(body, "Heavy rounded abdominal plate", SculptedPlate(.52f, .195f, .085f), V(0, 1.005f, .248f), Vector3.one, "Ivory");
                ArtKit.Part(body, "Heavy shaped back salvage plate", SculptedPlate(.65f, .44f, .115f), V(0, 1.24f, -.22f), Vector3.one, "RustDark", Quaternion.Euler(0, 180, 0));
                ArtKit.Box(body, "Warlord tattered back mantle", V(0, .92f, -.25f), V(.58f, .52f, .05f), "Scarf", .02f, Quaternion.Euler(8, 0, 0));
                for (int side = -1; side <= 1; side += 2)
                {
                    ArtKit.Beam(body, "Heavy neck protection", V(side * .25f, 1.37f, -.18f), V(side * .25f, 1.58f, -.10f), .10f, "Steel");
                    // Towering Warlord Trophy Poles & Pennants
                    ArtKit.Beam(body, "Warlord trophy pole", V(side * .18f, 1.28f, -.26f), V(side * .28f, 2.08f, -.36f), .042f, "SteelLight");
                    ArtKit.Box(body, "Warlord trophy crossbar", V(side * .26f, 1.96f, -.34f), V(.22f, .038f, .038f), "Wood", .01f);
                    ArtKit.Box(body, "Warlord crimson banner", V(side * .26f, 1.76f, -.37f), V(.18f, .34f, .022f), "Scarf", .008f, Quaternion.Euler(6, side * 12, 0));
                    ArtKit.Box(body, "Banner ivory stripe", V(side * .26f, 1.76f, -.385f), V(.06f, .28f, .012f), "Ivory", .004f, Quaternion.Euler(6, side * 12, 0));
                    ArtKit.Ellipsoid(body, "Trophy rack skull", V(side * .28f, 2.12f, -.36f), V(.13f, .15f, .14f), "Ivory", 10, 8);
                }
            }

            // Head, helmet, goggles, respirator, and archetype headgear
            ArtKit.Ellipsoid(body, "Sunburnt head", V(0, 1.67f, .022f), V(.355f, .38f, .34f), "Skin", 16, 10);
            ArtKit.Ellipsoid(body, "Organic pressed steel helmet dome", V(0, 1.782f, -.008f), V(.46f, .28f, .43f), variant == 2 ? "Red" : "Steel", 20, 14);
            var helmetRim = ArtKit.Torus(body, "Helmet curved raised brow", V(0, 1.752f, -.008f), .238f, .212f, .034f, variant == 2 ? "Yellow" : "SteelLight", null, 24);
            helmetRim.localScale = V(1, 1, .92f);
            ArtKit.Ellipsoid(body, "Organic respirator face mask", V(0, 1.625f, .187f), V(.29f, .23f, .17f), variant == 2 ? "Ivory" : "Rubber", 16, 10);
            ArtKit.Ellipsoid(body, "Respirator nose bridge", V(0, 1.69f, .236f), V(.08f, .12f, .095f), variant == 2 ? "IvoryShade" : "Steel", 12, 8);
            ArtKit.Part(body, "Shaped mask breathing grille", SculptedPlate(.14f, .08f, .028f), V(0, 1.603f, .268f), Vector3.one, variant == 2 ? "Steel" : "SteelLight");
            for (int i = -1; i <= 1; i++) ArtKit.Box(body, "Mask broad vent", V(i * .034f, 1.603f, .285f), V(.013f, .038f, .009f), "Rubber", .003f);

            var goggleStrap = ArtKit.Torus(body, "Rounded leather goggle strap", V(0, 1.713f, -.011f), .205f, .184f, .068f, "RustDark", null, 20);
            goggleStrap.localScale = V(1, 1, .90f);
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Cylinder(body, "Crude steel goggle rim", V(side * .092f, 1.715f, .210f), .086f, .046f, variant == 0 ? "Yellow" : "Steel", AlongBarrel, 12);
                // Bright glowing lenses make raiders look fierce and alive!
                ArtKit.Cylinder(body, "Glowing goggle lens", V(side * .092f, 1.715f, .236f), .062f, .020f, "Glow", AlongBarrel, 12);
                ArtKit.Box(body, "Lens reflected daylight", V(side * .092f - .016f, 1.737f, .250f), V(.038f, .013f, .007f), "Ivory", .003f);
                ArtKit.Ellipsoid(body, "Helmet rounded side protection", V(side * .195f, 1.70f, -.025f), V(.095f, .175f, .185f), "Steel", 12, 8);
                if (variant == 0)
                    ArtKit.Cylinder(body, "Respirator side filter", V(side * .135f, 1.59f, .21f), .048f, .065f, "Red", Quaternion.Euler(20, side * 35, 0), 10);
            }

            ArtKit.Ellipsoid(body, "Rolled red brown scarf", V(0, 1.486f, .036f), V(.50f, .18f, .40f), "Scarf", 14, 8);
            ArtKit.Box(body, "Scarf broad folded front", V(0, 1.484f, .228f), V(.36f, .14f, .095f), "Scarf", .03f, Quaternion.Euler(0, 0, -8));
            ArtKit.Part(body, "Hanging triangular scarf fold", ScarfPoint(), V(.025f, 1.40f, .266f), V(.26f, .25f, .075f), "Scarf", Quaternion.Euler(0, 0, -7));
            ArtKit.Box(body, "Scarf light catching fold", V(.09f, 1.486f, .28f), V(.12f, .026f, .017f), "Rust", .008f, Quaternion.Euler(0, 0, -8));

            if (variant == 0)
            {
                // Scavenger: Welded Scrap Saw-Fin Crest on helmet
                ArtKit.Box(body, "Scavenger helmet fin", V(0, 1.93f, .01f), V(.035f, .12f, .28f), "Red", .01f, Quaternion.Euler(-6, 0, 0));
                for (int t = 0; t < 3; t++)
                    AddSpike(body, "Fin tooth " + t, V(0, 2.00f, -.08f + t * .09f), V(.04f, .07f, .06f), Quaternion.Euler(-15, 0, 0), "SteelLight");
            }
            else if (variant == 1)
            {
                // Enforcer: Tall Bladed Mohawk + Twin Curved Side Horns + Visor Bar
                for (int m = 0; m < 4; m++)
                {
                    float mz = -.12f + m * .085f;
                    float mh = .18f + (m == 1 || m == 2 ? .04f : 0f);
                    ArtKit.Box(body, "Enforcer mohawk blade " + m, V(0, 1.94f, mz), V(.042f, mh, .068f), m % 2 == 0 ? "Scarf" : "Yellow", .01f, Quaternion.Euler(-10 + m * 6, 0, 0));
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    AddSpike(body, "Enforcer side horn", V(side * .26f, 1.82f, .02f), V(.09f, .22f, .09f), Quaternion.Euler(15, 0, -side * 68), "Ivory");
                }
            }
            else if (variant == 2)
            {
                // Veteran / Warlord: Swept Giant Ivory Bull Horns + Crown Ridge + Brow Shield
                ArtKit.Box(body, "Veteran helmet welded ridge", V(0, 1.93f, -.01f), V(.11f, .10f, .32f), "Yellow", .022f);
                ArtKit.Part(body, "Veteran curved brow shield", SculptedPlate(.42f, .085f, .05f), V(0, 1.772f, .205f), Vector3.one, "Ivory");
                for (int side = -1; side <= 1; side += 2)
                {
                    ArtKit.Beam(body, "Warlord bull horn base", V(side * .20f, 1.82f, .01f), V(side * .38f, 1.94f, .08f), .095f, "IvoryShade");
                    AddSpike(body, "Warlord bull horn tip", V(side * .43f, 2.04f, .14f), V(.095f, .26f, .095f), Quaternion.Euler(28, 0, -side * 28), "Ivory");
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                var leg = ArtKit.Node(side < 0 ? "Left leg" : "Right leg", rig.transform).transform;
                leg.localPosition = V(side * .184f * broad, .77f, 0);
                var thigh = ArtKit.Ellipsoid(leg, "Soft cloth trouser thigh", V(0, -.147f, .002f), V(.255f * broad, .365f, .285f), "Cloth", 14, 10);
                thigh.localRotation = Quaternion.Euler(side * 3, 0, side * 2);
                ArtKit.Part(leg, "Shaped dusty trouser calf", OrganicLoft("Trouser calf", new[] { -.56f, -.50f, -.37f, -.285f }, new[] { .18f, .215f, .235f, .205f }, new[] { .205f, .24f, .255f, .22f }, new[] { -.018f, -.018f, -.015f, -.008f }, 14, .85f), Vector3.zero, Vector3.one, "Cloth");
                ArtKit.Part(leg, "Curved steel knee guard", SculptedPlate(.245f, .215f, .078f), V(0, -.326f, .152f), Vector3.one, variant == 2 ? "Ivory" : (variant == 1 ? "Red" : "Steel"), Quaternion.Euler(-7, 0, side * 5));
                if (variant >= 1)
                    AddSpike(leg, "Knee spike", V(0, -.32f, .22f), V(.06f, .11f, .06f), Quaternion.Euler(85, 0, 0), "SteelLight");
                ArtKit.Part(leg, "Sculpted leather ankle and boot toe", OrganicLoft("Dusty shaped boot", new[] { -.735f, -.698f, -.63f, -.57f, -.49f }, new[] { .27f, .283f, .276f, .232f, .215f }, new[] { .393f, .415f, .373f, .293f, .24f }, new[] { .067f, .071f, .063f, .013f, -.018f }, 18, .69f), Vector3.zero, Vector3.one, "RustDark");
                ArtKit.Part(leg, "Shaped heavy rubber boot sole", OrganicLoft("Boot sole", new[] { -.756f, -.739f, -.704f }, new[] { .265f, .295f, .283f }, new[] { .387f, .427f, .414f }, new[] { .069f, .070f, .071f }, 18, .64f), Vector3.zero, Vector3.one, "Rubber");
                ArtKit.Ellipsoid(leg, "Armored boot toe cap", V(0, -.647f, .203f), V(.245f, .108f, .122f), "Steel", 16, 8);
                var cuff = ArtKit.Torus(leg, "Leather boot cuff", V(0, -.493f, -.018f), .118f, .096f, .045f, "RustDark", null, 14);
                cuff.localScale = V(1, 1, 1.10f);
                BakeAtlas(leg, "Raider_" + label + (side < 0 ? "_LeftLeg" : "_RightLeg"));

                var arm = ArtKit.Node(side < 0 ? "Left arm" : "Right arm", rig.transform).transform;
                arm.localPosition = V(side * .39f * broad, 1.39f, -.005f);
                ArtKit.Ellipsoid(arm, "Expressive exposed upper arm", V(side * .025f, -.19f, .008f), V(.235f * broad, .355f, .254f), "Skin", 12, 8);
                ArtKit.Ellipsoid(arm, "Elbow", V(side * .018f, -.343f, .012f), V(.23f, .17f, .245f), "Skin", 10, 6);
                ArtKit.Part(arm, "Rounded tapered forearm bracer", OrganicLoft("Forearm armor", new[] { -.565f, -.505f, -.365f, -.325f }, new[] { .195f, .235f, .255f, .215f }, new[] { .223f, .265f, .272f, .232f }, new[] { .015f, .015f, .014f, .010f }, 14, .77f), Vector3.zero, Vector3.one, side < 0 ? "Steel" : "Red");
                var binding = ArtKit.Torus(arm, "Leather wrist binding", V(0, -.53f, .015f), .126f, .107f, .052f, "RustDark", null, 14);
                binding.localScale = V(1, 1, 1.10f);
                ArtKit.Ellipsoid(arm, "Rounded heavy work glove", V(0, -.607f, .043f), V(.239f, .195f, .272f), "Rubber", 14, 10);
                ArtKit.Ellipsoid(arm, "Curved glove knuckle plate", V(0, -.584f, .172f), V(.195f, .085f, .075f), "SteelLight", 12, 8);

                float padWidth = variant == 0 ? (side < 0 ? .35f : .28f) : variant == 1 ? .39f : .44f;
                string padMat = variant == 2 ? "Red" : (variant == 1 ? (side < 0 ? "Red" : "Steel") : (side < 0 ? "Steel" : "RustDark"));
                ArtKit.Part(arm, "Domed shaped shoulder shell", OrganicLoft("Forged shoulder shell", new[] { -.09f, -.043f, .042f, .10f }, new[] { padWidth * .88f, padWidth, padWidth * .85f, padWidth * .45f }, new[] { .345f, .39f, .345f, .19f }, new[] { .005f, .005f, .005f, .005f }, 18, .73f), V(side * .025f, 0, 0), Vector3.one, padMat, Quaternion.Euler(0, 0, side * -12));
                var rim = ArtKit.Torus(arm, "Curved shoulder edge reinforcement", V(side * .025f, -.049f, .005f), padWidth * .5f, padWidth * .5f - .025f, .038f, variant == 2 ? "Yellow" : "SteelLight", Quaternion.Euler(0, 0, side * -12), 18);
                rim.localScale = V(1, 1, .39f / padWidth);
                ArtKit.Part(arm, "Broad chipped shoulder paint", SculptedPlate(padWidth * .38f, .13f, .010f), V(side * .012f, .098f, .02f), Vector3.one, variant == 2 ? "Ivory" : "Rust", Quaternion.Euler(-90, side * 14, side * -12));

                if (variant == 0 && side < 0)
                {
                    // Scavenger left shoulder: Mounted Buzzsaw Blade Pauldron!
                    ArtKit.Cylinder(arm, "Shoulder saw disc", V(-.08f, .13f, 0), .18f, .022f, "SteelLight", Quaternion.Euler(0, 0, 28), 16);
                    ArtKit.Cylinder(arm, "Shoulder saw hub", V(-.08f, .14f, 0), .07f, .035f, "Red", Quaternion.Euler(0, 0, 28), 10);
                }
                int count = variant == 0 ? (side < 0 ? 2 : 1) : variant == 1 ? 3 : 3;
                for (int i = 0; i < count; i++)
                {
                    string spMat = variant == 2 && i == 1 ? "Ivory" : "SteelLight";
                    float spH = variant == 2 ? .22f : (variant == 1 ? .19f : .14f);
                    AddSpike(arm, "Scavenged shoulder spike", V(side * (.05f + i * .065f), .125f, -.075f + i * .085f), V(.095f, spH, .095f), Quaternion.Euler(side * 9, 0, side * -18), spMat);
                }
                if (side > 0) BuildWeapon(arm, variant);
                BakeAtlas(arm, "Raider_" + label + (side < 0 ? "_LeftArm" : "_RightArm"));
                arm.localRotation = Quaternion.Euler(0, 0, side < 0 ? 12 : -12);
            }
            BakeAtlas(body, "Raider_" + label + "_Body");
            string path = ArtKit.Root + "/Prefabs/Raider_" + label + "_Model.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(rig, path);
            Object.DestroyImmediate(rig);
            return prefab;
        }

        static void BuildWeapon(Transform arm, int variant)
        {
            if (variant == 0)
            {
                // Scavenger: Motorized Ripper Buzzsaw-Cleaver!
                ArtKit.Cylinder(arm, "Ripper long steel shaft", V(.025f, -.22f, .17f), .036f, .92f, "Steel", null, 12);
                ArtKit.Box(arm, "Ripper motor housing", V(.025f, .18f, .17f), V(.13f, .20f, .18f), "Red", .022f);
                ArtKit.Cylinder(arm, "Ripper buzzsaw disc", V(.025f, .35f, .22f), .22f, .028f, "SteelLight", AcrossAxle, 18);
                ArtKit.Cylinder(arm, "Ripper glowing hub", V(.025f, .35f, .22f), .075f, .048f, "Glow", AcrossAxle, 10);
                for (int t = 0; t < 8; t++)
                {
                    float a = t * 45f;
                    Quaternion q = Quaternion.Euler(a, 0, 0);
                    ArtKit.Box(arm, "Saw tooth " + t, V(.025f, .35f, .22f) + q * V(0, .22f, 0), V(.024f, .065f, .045f), "SteelLight", .005f, q);
                }
                ArtKit.Box(arm, "Cleaver back spine", V(.025f, .32f, .09f), V(.045f, .26f, .11f), "Steel", .01f);
                ArtKit.Box(arm, "Ripper worn grip", V(.025f, -.52f, .17f), V(.095f, .24f, .095f), "RustDark", .018f);
            }
            else if (variant == 1)
            {
                // Enforcer: Spiked Kanabo War-Club with Glowing Ember Core!
                ArtKit.Cylinder(arm, "Kanabo timber handle", V(.035f, -.32f, .17f), .055f, .80f, "Wood", null, 10);
                ArtKit.Part(arm, "Shaped iron kanabo head", OrganicLoft("Kanabo head", new[] { -.06f, .01f, .22f, .42f, .46f }, new[] { .16f, .23f, .25f, .22f, .16f }, new[] { .18f, .25f, .27f, .24f, .18f }, new[] { .17f, .17f, .17f, .17f, .17f }, 12, .75f), V(.035f, 0, 0), Vector3.one, "Steel");
                ArtKit.Cylinder(arm, "Kanabo glowing tip core", V(.035f, .45f, .17f), .095f, .06f, "Glow", null, 10);
                for (int j = 0; j < 3; j++)
                {
                    float y = .03f + j * .16f;
                    var band = ArtKit.Torus(arm, "Club steel band", V(.035f, y, .17f), .135f, .112f, .055f, j == 1 ? "Red" : "SteelLight", null, 12);
                    band.localScale = V(1, 1, 1.09f);
                    foreach (int s in new[] { -1, 1 })
                    {
                        AddSpike(arm, "Club side spike", V(.035f + s * .175f, y, .17f), V(.08f, .18f, .08f), Quaternion.Euler(0, 0, -s * 90), "Ivory");
                        AddSpike(arm, "Club fb spike", V(.035f, y, .17f + s * .185f), V(.075f, .16f, .075f), Quaternion.Euler(s * 90, 0, 0), "SteelLight");
                    }
                }
                ArtKit.Box(arm, "Club cloth grip", V(.035f, -.51f, .17f), V(.12f, .24f, .12f), "Scarf", .022f);
            }
            else
            {
                // Veteran / Warlord Boss: Rocket-Powered Executioner Greataxe / Thunder-Hammer!
                ArtKit.Cylinder(arm, "Warlord greataxe haft", V(.035f, -.22f, .17f), .054f, .98f, "Steel", null, 12);
                ArtKit.Box(arm, "Hammer reactor block", V(.035f, .31f, .17f), V(.26f, .25f, .34f), "Red", .04f);
                ArtKit.Box(arm, "Hammer yellow hazard trim", V(.035f, .31f, .17f), V(.28f, .08f, .22f), "Yellow", .015f);
                // Forward sweeping executioner axe blade
                ArtKit.Box(arm, "Executioner axe cheek", V(.035f, .31f, .36f), V(.065f, .38f, .16f), "Steel", .018f);
                ArtKit.Box(arm, "Executioner razor edge", V(.035f, .31f, .44f), V(.032f, .44f, .08f), "SteelLight", .008f);
                // Rear Rocket Thruster Nozzle with glowing plasma core
                ArtKit.Cylinder(arm, "Rocket thruster bell", V(.035f, .31f, -.04f), .115f, .14f, "Steel", AlongBarrel, 12);
                ArtKit.Cylinder(arm, "Rocket plasma core", V(.035f, .31f, -.115f), .085f, .03f, "Glow", AlongBarrel, 10);
                // Top armor-piercing spike
                AddSpike(arm, "Hammer top spike", V(.035f, .51f, .17f), V(.095f, .18f, .095f), Quaternion.identity, "Ivory");
                ArtKit.Box(arm, "Hammer worn grip", V(.035f, -.52f, .17f), V(.12f, .24f, .12f), "Wood", .023f);
            }
        }

        public static void ApplyBuggy(EnemyBuggy b)
        {
            if (!b) return;
            EnsureAtlas();
            var visual = b.transform.Find("Replaceable buggy visuals");
            if (!visual) visual = ArtKit.Node("Replaceable buggy visuals", b.transform).transform;
            var protectedTransforms = new HashSet<Transform>();
            if (b.wheelVisuals != null) foreach (var t in b.wheelVisuals) if (t) KeepAncestors(t, visual, protectedTransforms);
            if (b.gun)
            {
                foreach (var t in new[] { b.gun.yaw, b.gun.pitch, b.gun.muzzle, b.gun.flash }) if (t) KeepAncestors(t, visual, protectedTransforms);
            }
            StripOldVisuals(visual, protectedTransforms, b.gun ? b.gun.flash : null);
            var body = ArtKit.Node("Concept armored scout body", visual).transform;

            // Predatory "Rust Vulture" Shark-Nose Raider Buggy
            ArtKit.Box(body, "Heavy recessed chassis", V(0, .245f, 0), V(1.48f, .20f, 3.20f), "Steel", .055f);
            ArtKit.Box(body, "Tapered crimson shark hood", V(0, .65f, 1.02f), V(1.50f, .25f, 1.22f), "Red", .075f, Quaternion.Euler(-6, 0, 0));
            ArtKit.Box(body, "Sculpted hood air scoop", V(0, .79f, .92f), V(.58f, .12f, .68f), "RustDark", .03f, Quaternion.Euler(-4, 0, 0));
            ArtKit.Box(body, "Scoop glowing intake grille", V(0, .81f, 1.26f), V(.46f, .065f, .03f), "Glow", .008f);
            ArtKit.Box(body, "Rounded predatory front nose", V(0, .49f, 1.55f), V(1.52f, .25f, .22f), "Rust", .06f);
            // Ivory war-paint stripes on hood
            ArtKit.Box(body, "Hood warpaint left stripe", V(-.36f, .782f, 1.02f), V(.18f, .016f, 1.12f), "Ivory", .006f, Quaternion.Euler(-6, 0, 0));
            ArtKit.Box(body, "Hood warpaint right stripe", V(.36f, .782f, 1.02f), V(.18f, .016f, 1.12f), "Ivory", .006f, Quaternion.Euler(-6, 0, 0));
            ArtKit.Box(body, "Dark front radiator intake", V(0, .48f, 1.66f), V(.78f, .14f, .03f), "Rubber", .014f);
            for (int j = 0; j < 5; j++) ArtKit.Box(body, "Intake steel fang slat", V(-.30f + j * .15f, .48f, 1.68f), V(.032f, .12f, .028f), "Ivory", .007f);

            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Beam(body, "Strong rusty chassis rail", V(side * .70f, .32f, -1.55f), V(side * .70f, .34f, 1.60f), .125f, "RustDark");
                ArtKit.Box(body, "Scavenged spiked door armor", V(side * .77f, .64f, -.12f), V(.115f, .51f, 1.14f), side < 0 ? "Red" : "Rust", .038f, Quaternion.Euler(0, 0, side * -7));
                ArtKit.Box(body, "Door ivory war chevron", V(side * .835f, .68f, -.10f), V(.016f, .26f, .38f), "Ivory", .008f, Quaternion.Euler(0, 0, side * -7));
                ArtKit.Box(body, "Side rock slider bar", V(side * .82f, .36f, -.12f), V(.09f, .08f, 1.24f), "Steel", .02f);
                for (int sp = 0; sp < 2; sp++)
                    AddSpike(body, "Door side spike", V(side * .88f, .62f, -.32f + sp * .42f), V(.075f, .16f, .075f), Quaternion.Euler(0, 0, -side * 90), "SteelLight");

                ArtKit.Beam(body, "Roll cage front pillar", V(side * .63f, .57f, .45f), V(side * .59f, 1.48f, .17f), .115f, "Steel");
                ArtKit.Beam(body, "Roll cage rear pillar", V(side * .66f, .45f, -.88f), V(side * .59f, 1.48f, -.72f), .115f, "RustDark");
                ArtKit.Beam(body, "Roof frame side rail", V(side * .59f, 1.48f, .17f), V(side * .59f, 1.48f, -.72f), .115f, "Steel");
                ArtKit.Beam(body, "Exposed rear diagonal", V(side * .58f, 1.44f, -.73f), V(side * .64f, .42f, -1.51f), .098f, "Red");

                // Angry glowing slit headlamps
                ArtKit.Box(body, "Headlamp armored cowl", V(side * .56f, .52f, 1.66f), V(.30f, .20f, .14f), "Steel", .035f, Quaternion.Euler(0, 0, side * -10));
                ArtKit.Box(body, "Glowing predatory headlamp", V(side * .56f, .52f, 1.735f), V(.22f, .11f, .022f), "Glow", .012f, Quaternion.Euler(0, 0, side * -10));
                ArtKit.Box(body, "Rear glowing tail lamp", V(side * .61f, .46f, -1.62f), V(.20f, .12f, .07f), "Red", .019f);

                // Twin sweeping multi-pipe rear exhaust stacks
                ArtKit.Beam(body, "Raised rear exhaust primary", V(side * .64f, .38f, -1.22f), V(side * .74f, 1.18f, -1.58f), .11f, "SteelLight");
                ArtKit.Beam(body, "Raised rear exhaust secondary", V(side * .58f, .38f, -1.32f), V(side * .66f, 1.12f, -1.64f), .095f, "Steel");
                ArtKit.Cylinder(body, "Exhaust ember mouth", V(side * .74f, 1.20f, -1.59f), .048f, .025f, "Glow", Quaternion.Euler(-25, 0, 0), 10);

                for (int axle = 0; axle < 2; axle++)
                {
                    int index = axle * 2 + (side < 0 ? 0 : 1);
                    if (b.wheels == null || index >= b.wheels.Length || !b.wheels[index]) continue;
                    var wc = b.wheels[index];
                    Vector3 p = b.transform.InverseTransformPoint(wc.transform.position);
                    ArtKit.Beam(body, "Exposed suspension lower link", V(side * .42f, .20f, p.z), V(p.x, p.y, p.z), .085f, "Steel");
                    ArtKit.Beam(body, "Coilover shock absorber", V(side * .64f, .62f, p.z), V(p.x, p.y, p.z), .078f, "Yellow");
                    ArtKit.Part(body, "Shaped armored wheel arch", FenderMesh(wc.radius + .075f, wc.radius + .175f, .34f), V(p.x, p.y, p.z), Vector3.one, axle == 0 ? "Red" : "Rust");
                }
            }

            // Armored roof with Dorsal Spine Spikes & High Rear Wing Spoiler
            ArtKit.Box(body, "Salvaged armored cabin roof", V(0, 1.52f, -.26f), V(1.42f, .115f, 1.10f), "Rust", .045f);
            ArtKit.Box(body, "Roof ivory war stripe", V(-.32f, 1.585f, -.26f), V(.24f, .016f, .98f), "Ivory", .006f);
            for (int rs = 0; rs < 3; rs++)
                AddSpike(body, "Roof spine spike " + rs, V(.32f, 1.64f, .08f - rs * .32f), V(.08f, .15f, .10f), Quaternion.Euler(-12, 0, 0), "SteelLight");

            ArtKit.Beam(body, "Roof front crossbar", V(-.59f, 1.48f, .17f), V(.59f, 1.48f, .17f), .115f, "Steel");
            ArtKit.Beam(body, "Rear crossbrace", V(-.61f, .49f, -.91f), V(.59f, 1.43f, -.74f), .085f, "Steel");
            ArtKit.Beam(body, "Rear crossbrace opposite", V(.61f, .49f, -.91f), V(-.59f, 1.43f, -.74f), .085f, "Steel");

            // High-Mount Rear Wing Spoiler
            ArtKit.Box(body, "Rear wing spoiler foil", V(0, 1.36f, -1.52f), V(1.56f, .065f, .32f), "Red", .02f, Quaternion.Euler(14, 0, 0));
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Box(body, "Wing endplate", V(side * .78f, 1.36f, -1.52f), V(.045f, .24f, .38f), "Ivory", .01f);
                ArtKit.Beam(body, "Wing strut", V(side * .52f, .82f, -1.42f), V(side * .52f, 1.34f, -1.52f), .065f, "Steel");
            }

            // V-Wedge "Bone-Crusher" Jaw Ram with 5 Ivory/Steel Tusks
            ArtKit.Box(body, "Heavy toothed jaw ram", V(0, .31f, 1.80f), V(1.94f, .26f, .28f), "Steel", .045f);
            ArtKit.Box(body, "Ram crimson hazard bar", V(0, .31f, 1.945f), V(1.76f, .11f, .025f), "Red", .008f);
            for (int j = -2; j <= 2; j++)
            {
                bool outer = Mathf.Abs(j) == 2;
                AddSpike(body, "Front welded ram tusk " + j, V(j * .42f, .33f, 2.04f), V(.16f, outer ? .42f : .35f, .18f), Quaternion.Euler(82, 0, 0), j % 2 == 0 ? "Ivory" : "SteelLight");
            }

            // Exposed Supercharged Rear V8 Engine
            ArtKit.Box(body, "Rear exposed engine block", V(0, .60f, -1.21f), V(.88f, .44f, .64f), "Steel", .055f);
            ArtKit.Box(body, "Engine supercharger blower", V(0, .92f, -1.18f), V(.44f, .22f, .46f), "Red", .03f);
            for (int j = 0; j < 5; j++) ArtKit.Box(body, "Engine broad cooling fin", V(-.32f + j * .16f, .84f, -1.21f), V(.07f, .08f, .55f), "SteelLight", .013f);
            BuildJerrycan(body, V(.53f, .69f, -.94f), .66f);
            BuildBuggyDriver(body);
            BakeAtlas(body, "Buggy_ArmoredScout_Body");

            if (b.wheelVisuals != null)
            {
                for (int i = 0; i < b.wheelVisuals.Length; i++)
                {
                    var p = b.wheelVisuals[i]; if (!p) continue;
                    foreach (Transform child in p.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                    float radius = b.wheels != null && i < b.wheels.Length && b.wheels[i] ? b.wheels[i].radius : .46f;
                    BuildWheel(p, radius, i % 2 == 0 ? -1 : 1);
                    BakeAtlas(p, "Buggy_" + (i % 2 == 0 ? "Left" : "Right") + "Wheel");
                }
            }
            if (b.gun) RestyleBuggyGun(b.gun);
            b.armor = body.GetComponentsInChildren<Renderer>(true);
            Layer(visual, b.gameObject.layer);
            EditorUtility.SetDirty(b);
        }

        static void BuildBuggyDriver(Transform parent)
        {
            ArtKit.Box(parent, "Visible driver seat", V(-.32f, .52f, -.27f), V(.48f, .15f, .57f), "Rubber", .05f);
            ArtKit.Box(parent, "Visible driver seat back", V(-.32f, .81f, -.49f), V(.46f, .57f, .14f), "Rubber", .05f);
            ArtKit.Box(parent, "Driver worn jacket", V(-.32f, .92f, -.24f), V(.36f, .43f, .29f), "Cloth", .06f);
            ArtKit.Ellipsoid(parent, "Driver rounded head", V(-.32f, 1.26f, -.15f), V(.30f, .32f, .29f), "Skin", 12, 8);
            ArtKit.Ellipsoid(parent, "Driver steel helmet", V(-.32f, 1.37f, -.16f), V(.35f, .18f, .32f), "Red", 12, 8);
            ArtKit.Box(parent, "Driver helmet mohawk", V(-.32f, 1.48f, -.16f), V(.04f, .10f, .22f), "Yellow", .008f);
            ArtKit.Box(parent, "Driver red scarf", V(-.32f, 1.17f, -.02f), V(.32f, .14f, .12f), "Scarf", .035f);
            ArtKit.Box(parent, "Driver glowing goggles", V(-.32f, 1.285f, .003f), V(.28f, .09f, .06f), "Glow", .022f);
            ArtKit.Beam(parent, "Driver left forearm", V(-.51f, 1.01f, -.20f), V(-.48f, .91f, .20f), .115f, "Skin");
            ArtKit.Beam(parent, "Driver right forearm", V(-.13f, 1.01f, -.20f), V(-.16f, .91f, .20f), .115f, "Skin");
            ArtKit.Torus(parent, "Driver steering wheel", V(-.32f, .93f, .23f), .18f, .133f, .036f, "Rubber", Quaternion.Euler(65, 0, 0), 16);
        }

        static void BuildWheel(Transform parent, float radius, int side)
        {
            float s = radius / .46f;
            ArtKit.Torus(parent, "Rounded chunky offroad tire", Vector3.zero, radius * .96f, radius * .46f, .465f * s, "Rubber", AcrossAxle, 28);
            ArtKit.Cylinder(parent, "Dark recessed rim", V(side * .205f * s, 0, 0), radius * .52f, .045f * s, "Steel", AcrossAxle, 16);
            ArtKit.Cylinder(parent, "Crimson beadlock wheel dish", V(side * .229f * s, 0, 0), radius * .38f, .030f * s, "Red", AcrossAxle, 16);
            ArtKit.Cylinder(parent, "Heavy exposed wheel hub", V(side * .25f * s, 0, 0), radius * .155f, .06f * s, "SteelLight", AcrossAxle, 12);
            // Chariot hub spike protruding outward
            AddSpike(parent, "Wheel hub spike", V(side * .33f * s, 0, 0), V(.11f * s, .14f * s, .11f * s), Quaternion.Euler(0, 0, -side * 90), "SteelLight");
            for (int j = 0; j < 14; j++)
            {
                float a = j * Mathf.PI * 2 / 14;
                for (int row = -1; row <= 1; row += 2)
                {
                    ArtKit.Box(parent, "Large chevron tire tread", V(row * .106f * s, Mathf.Cos(a) * radius * .933f, Mathf.Sin(a) * radius * .933f), V(.235f * s, .067f * s, .15f * s), "Rubber", .015f * s, Quaternion.Euler(a * Mathf.Rad2Deg, 0, row * 12));
                }
            }
            for (int j = 0; j < 5; j++)
            {
                float a = j * Mathf.PI * 2 / 5;
                ArtKit.Cylinder(parent, "Five strong hub bolts", V(side * .258f * s, Mathf.Cos(a) * .122f * s, Mathf.Sin(a) * .122f * s), .028f * s, .028f * s, "Yellow", AcrossAxle, 6);
            }
        }

        static void RestyleBuggyGun(BuggyGun gun)
        {
            if (!gun.yaw || !gun.pitch || !gun.muzzle) return;
            var mount = ArtKit.Node("Concept gun base skin", gun.yaw.parent).transform;
            ArtKit.Box(mount, "Armored roof gun mounting plate", Vector3.zero, V(.64f, .12f, .52f), "RustDark", .03f);
            BakeAtlas(mount, "Buggy_GunMount");

            var yaw = ArtKit.Node("Concept rotating bearing skin", gun.yaw).transform;
            ArtKit.Cylinder(yaw, "Steel rotating gun bearing", V(0, .125f, 0), .23f, .09f, "Steel", null, 16);
            ArtKit.Box(yaw, "Elevation armored support", V(0, .19f, -.04f), V(.34f, .16f, .33f), "Rust", .025f);
            // Angled V-Sloped Gunner Shield on Yaw
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Box(yaw, "Gunner V-shield wing", V(side * .24f, .28f, .16f), V(.24f, .28f, .045f), "Red", .015f, Quaternion.Euler(-12, side * 18, 0));
                ArtKit.Box(yaw, "Shield ivory stripe", V(side * .25f, .28f, .185f), V(.08f, .22f, .015f), "Ivory", .005f, Quaternion.Euler(-12, side * 18, 0));
            }
            BakeAtlas(yaw, "Buggy_GunYaw");

            var pitch = ArtKit.Node("Concept machine gun skin", gun.pitch).transform;
            ArtKit.Box(pitch, "Readable hostile gun receiver", V(0, 0, -.005f), V(.32f, .25f, .56f), "Steel", .035f);
            ArtKit.Box(pitch, "Receiver upper crimson cover", V(0, .145f, -.06f), V(.24f, .075f, .34f), "Red", .018f);
            ArtKit.Box(pitch, "Target holo sight", V(0, .21f, .02f), V(.09f, .07f, .06f), "Glow", .008f);
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Cylinder(pitch, "Side ammo drum", V(side * .24f, -.03f, -.08f), .13f, .16f, side < 0 ? "Rust" : "Red", AcrossAxle, 12);
                ArtKit.Cylinder(pitch, "Ammo drum cap", V(side * .33f, -.03f, -.08f), .07f, .02f, "Yellow", AcrossAxle, 10);
            }
            float end = gun.muzzle.localPosition.z;
            float start = .24f;
            for (int side = -1; side <= 1; side += 2)
            {
                float bx = side * .065f;
                ArtKit.Cylinder(pitch, "Twin heavy machine gun barrel", V(bx, 0, (start + end - .10f) * .5f), .040f, end - .10f - start, "Steel", AlongBarrel, 12);
                for (int j = 0; j < 3; j++) ArtKit.Cylinder(pitch, "Barrel cooling collar", V(bx, 0, .34f + j * .12f), .054f, .05f, "SteelLight", AlongBarrel, 12);
            }
            ArtKit.Box(pitch, "Twin heavy muzzle brake", V(0, 0, end - .09f), V(.24f, .145f, .17f), "Steel", .023f);
            ArtKit.Box(pitch, "Dark muzzle opening", V(0, 0, end - .001f), V(.18f, .070f, .006f), "Rubber", .008f);
            BakeAtlas(pitch, "Buggy_GunPitch");

            if (gun.flash)
            {
                var renderer = gun.flash.GetComponent<Renderer>();
                if (renderer) { renderer.sharedMaterial = ArtKit.Mat("Glow"); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
                gun.flash.localScale = V(.19f, .35f, .19f);
            }
            EditorUtility.SetDirty(gun);
        }

        public static void ApplyPickup(GameObject pickup)
        {
            if (!pickup) return;
            EnsureAtlas();
            var gearView = pickup.transform.Find("Metal gear");
            var scrapView = pickup.transform.Find("Scrap plates");
            if (!gearView) gearView = ArtKit.Node("Metal gear", pickup.transform).transform;
            if (!scrapView) scrapView = ArtKit.Node("Scrap plates", pickup.transform).transform;
            ClearMeshGroup(gearView);
            ClearMeshGroup(scrapView);
            if (!gear) gear = ArtKit.SaveMesh(GearMesh(), "Gear_SalvagedCog_Source");
            var part = ArtKit.Node("Recovered machine cog", gearView);
            part.AddComponent<MeshFilter>().sharedMesh = gear;
            part.AddComponent<MeshRenderer>().sharedMaterials = new[] { ArtKit.Mat("Yellow"), ArtKit.Mat("SteelLight"), ArtKit.Mat("Red") };
            ArtKit.Cylinder(gearView, "Cog glowing power core", V(0, 0, 0), .14f, .13f, "Glow", null, 12);
            BakeAtlas(gearView, "Gear_SalvagedCog");

            ArtKit.Box(scrapView, "Curved salvaged armor plate", V(0, -.005f, -.03f), V(.55f, .09f, .36f), "Teal", .035f, Quaternion.Euler(-9, -13, 10));
            ArtKit.Box(scrapView, "Worn ivory paint on armor", V(-.08f, .048f, -.03f), V(.25f, .016f, .30f), "Ivory", .007f, Quaternion.Euler(-9, -13, 10));
            ArtKit.Box(scrapView, "Heavy salvaged bracket", V(.13f, .08f, .075f), V(.13f, .13f, .48f), "Red", .025f, Quaternion.Euler(0, 32, -8));
            ArtKit.Cylinder(scrapView, "Salvaged plasma cell", V(-.17f, .11f, .08f), .075f, .38f, "Glow", Quaternion.Euler(65, 0, -25), 10);
            ArtKit.Cylinder(scrapView, "Large useful hexagonal fastener", V(.09f, .17f, -.10f), .12f, .08f, "Yellow", Quaternion.Euler(12, 20, 0), 6);
            BakeAtlas(scrapView, "Gear_SalvageCluster");
            Layer(pickup.transform, pickup.layer);
            EditorUtility.SetDirty(pickup);
        }

        static void BuildJerrycan(Transform parent, Vector3 position, float scale)
        {
            var t = ArtKit.Node("Strapped salvaged red fuel can", parent).transform;
            t.localPosition = position; t.localScale = Vector3.one * scale;
            ArtKit.Box(t, "Jerrycan body", Vector3.zero, V(.40f, .54f, .24f), "Red", .055f);
            ArtKit.Box(t, "Can top shoulder", V(0, .27f, 0), V(.34f, .10f, .215f), "Rust", .025f);
            ArtKit.Beam(t, "Strong metal carrying handle", V(-.09f, .36f, 0), V(.09f, .36f, 0), .055f, "Steel");
            ArtKit.Beam(t, "Handle left support", V(-.09f, .29f, 0), V(-.09f, .36f, 0), .055f, "Steel");
            ArtKit.Beam(t, "Handle right support", V(.09f, .29f, 0), V(.09f, .36f, 0), .055f, "Steel");
            ArtKit.Cylinder(t, "Jerrycan fuel cap", V(.14f, .32f, 0), .056f, .045f, "Yellow", null, 10);
            ArtKit.Beam(t, "Pressed can diagonal", V(-.12f, -.17f, .128f), V(.12f, .17f, .128f), .025f, "RustDark");
            ArtKit.Beam(t, "Pressed can opposite diagonal", V(.12f, -.17f, .128f), V(-.12f, .17f, .128f), .025f, "RustDark");
            ArtKit.Box(t, "Can restraining strap", V(0, -.01f, .138f), V(.06f, .49f, .035f), "Steel", .01f);
        }

        static void ClearMeshGroup(Transform t)
        {
            foreach (Transform child in t.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var renderer = t.GetComponent<MeshRenderer>(); if (renderer) Object.DestroyImmediate(renderer);
            var filter = t.GetComponent<MeshFilter>(); if (filter) Object.DestroyImmediate(filter);
        }
        static void KeepAncestors(Transform t, Transform limit, HashSet<Transform> keep)
        {
            while (t && t != limit) { keep.Add(t); t = t.parent; }
        }
        static void StripOldVisuals(Transform t, HashSet<Transform> keep, Transform flash)
        {
            foreach (Transform child in t.Cast<Transform>().ToArray())
            {
                if (!keep.Contains(child)) { Object.DestroyImmediate(child.gameObject); continue; }
                if (child != flash)
                {
                    var r = child.GetComponent<MeshRenderer>(); if (r) Object.DestroyImmediate(r);
                    var f = child.GetComponent<MeshFilter>(); if (f) Object.DestroyImmediate(f);
                }
                StripOldVisuals(child, keep, flash);
            }
        }
        static void Layer(Transform t, int layer) { t.gameObject.layer = layer; foreach (Transform c in t) Layer(c, layer); }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static Mesh TorsoLoft(float broad)
        {
            return OrganicLoft("Tailored rounded jacket", new[] { .88f, .99f, 1.15f, 1.32f, 1.40f }, new[] { .49f * broad, .57f * broad, .66f * broad, .70f * broad, .60f * broad }, new[] { .375f, .425f, .46f, .43f, .35f }, new[] { 0f, 0f, .002f, .005f, .008f }, 18, .79f);
        }

        // Cross sections are rounded superellipses, with several intentional stations for
        // anatomical taper. This avoids the rectangular plank silhouettes of simple boxes.
        static Mesh OrganicLoft(string name, float[] levels, float[] widths, float[] depths, float[] zCenters, int segments = 16, float power = .8f)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int row = 0; row < levels.Length; row++) for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    vertices.Add(V(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power) * widths[row] * .5f, levels[row], Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power) * depths[row] * .5f + zCenters[row]));
                }
            for (int row = 0; row < levels.Length - 1; row++) for (int i = 0; i < segments; i++)
                {
                    int a = row * segments + i, b = row * segments + (i + 1) % segments, c = a + segments, d = b + segments;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            int bottom = vertices.Count; vertices.Add(V(0, levels[0], zCenters[0]));
            int top = vertices.Count; int last = (levels.Length - 1) * segments; vertices.Add(V(0, levels[levels.Length - 1], zCenters[zCenters.Length - 1]));
            for (int i = 0; i < segments; i++) { int next = (i + 1) % segments; triangles.AddRange(new[] { bottom, i, next, top, last + next, last + i }); }
            return MeshFrom(name, vertices, triangles);
        }

        // A broad shaped plate with softened outline corners, true beveled perimeter and
        // a shallow convex front. Outline, rather than surface noise, carries the detail.
        static Mesh SculptedPlate(float width, float height, float depth)
        {
            Vector2[] corners = { new Vector2(-.34f, -.5f), new Vector2(.34f, -.5f), new Vector2(.44f, -.28f), new Vector2(.5f, .28f), new Vector2(.35f, .5f), new Vector2(-.35f, .5f), new Vector2(-.5f, .28f), new Vector2(-.44f, -.28f) };
            var outline = new List<Vector2>();
            for (int i = 0; i < corners.Length; i++)
            {
                var corner = corners[i]; var from = Vector2.Lerp(corner, corners[(i + corners.Length - 1) % corners.Length], .20f); var to = Vector2.Lerp(corner, corners[(i + 1) % corners.Length], .20f);
                for (int j = 0; j < 3; j++) { float t = j * .5f; outline.Add((1 - t) * (1 - t) * from + 2 * (1 - t) * t * corner + t * t * to); }
            }
            int n = outline.Count; var vertices = new List<Vector3>(); var triangles = new List<int>();
            float bevel = Mathf.Min(depth * .28f, .025f); float[] z = { -depth * .5f, -depth * .5f + bevel, depth * .5f - bevel, depth * .5f }; float[] scale = { .91f, 1f, 1f, .875f };
            for (int row = 0; row < 4; row++) foreach (var p in outline) vertices.Add(V(p.x * width * scale[row], p.y * height * scale[row], z[row]));
            for (int row = 0; row < 3; row++) for (int i = 0; i < n; i++)
                { int a = row * n + i, b = row * n + (i + 1) % n, c = a + n, d = b + n; triangles.AddRange(new[] { a, b, c, b, d, c }); }
            int back = vertices.Count; vertices.Add(V(0, 0, -depth * .5f)); int front = vertices.Count; vertices.Add(V(0, 0, depth * .5f + Mathf.Min(depth * .16f, .018f)));
            for (int i = 0; i < n; i++) { int next = (i + 1) % n; triangles.AddRange(new[] { back, next, i, front, n * 3 + i, n * 3 + next }); }
            return MeshFrom("Shaped curved salvage armor", vertices, triangles);
        }

        static Mesh ScarfPoint()
        {
            var v = new List<Vector3> { V(-.5f, .5f, -.5f), V(.5f, .5f, -.5f), V(.08f, -.5f, -.3f), V(-.5f, .5f, .5f), V(.5f, .5f, .5f), V(.08f, -.5f, .25f) };
            var triangles = new List<int> { 0, 2, 1, 3, 4, 5, 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4, 2, 0, 3, 2, 3, 5 };
            for (int i = 0; i < triangles.Count; i += 3) { int swap = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = swap; }
            return MeshFrom("Scarf folded point", v, triangles);
        }
        static void AddSpike(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation, string material)
        {
            if (!spike)
            {
                var v = new List<Vector3>(); var tr = new List<int>();
                const int n = 8;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n, b = (i + 1) * Mathf.PI * 2 / n; int s = v.Count;
                    v.Add(V(Mathf.Cos(a) * .5f, -.5f, Mathf.Sin(a) * .5f)); v.Add(V(0, .5f, 0)); v.Add(V(Mathf.Cos(b) * .5f, -.5f, Mathf.Sin(b) * .5f));
                    tr.AddRange(new[] { s, s + 1, s + 2 }); s = v.Count;
                    v.Add(Vector3.down * .5f); v.Add(V(Mathf.Cos(a) * .5f, -.5f, Mathf.Sin(a) * .5f)); v.Add(V(Mathf.Cos(b) * .5f, -.5f, Mathf.Sin(b) * .5f)); tr.AddRange(new[] { s, s + 1, s + 2 });
                }
                spike = ArtKit.SaveMesh(MeshFrom("Welded scrap spike", v, tr), "Raider_Spike");
            }
            ArtKit.Part(parent, name, spike, position, size, material, rotation);
        }
        static Mesh FenderMesh(float inner, float outer, float width)
        {
            var v = new List<Vector3>(); var tr = new List<int>(); const int n = 14;
            for (int s = -1; s <= 1; s += 2) for (int row = 0; row < 2; row++) for (int j = 0; j <= n; j++)
                    { float a = (12 + j * 156f / n) * Mathf.Deg2Rad, r = row == 0 ? inner : outer; v.Add(V(s * width * .5f, Mathf.Sin(a) * r, Mathf.Cos(a) * r)); }
            int stride = n + 1;
            for (int side = 0; side < 2; side++) for (int j = 0; j < n; j++)
                { int a = side * stride * 2 + j, b = a + stride; if (side == 0) tr.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); else tr.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
            for (int row = 0; row < 2; row++) for (int j = 0; j < n; j++)
                { int a = row * stride + j, b = a + stride * 2; if (row == 0) tr.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); else tr.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); }
            tr.AddRange(new[] { 0, stride, stride * 2, stride, stride * 3, stride * 2, n, stride * 2 + n, stride + n, stride + n, stride * 2 + n, stride * 3 + n });
            for (int i = 0; i < tr.Count; i += 3) { int swap = tr[i + 1]; tr[i + 1] = tr[i + 2]; tr[i + 2] = swap; }
            return MeshFrom("Formed armored fender", v, tr);
        }
        static Mesh GearMesh()
        {
            const int n = 64; var v = new List<Vector3>(); var face = new List<int>(); var edge = new List<int>(); var rust = new List<int>();
            Vector3 Point(int ring, int i)
            {
                float outer = i % 4 == 1 || i % 4 == 2 ? .44f : .365f;
                float r = ring == 0 || ring == 5 ? .155f : ring == 1 || ring == 4 ? outer - .018f : outer;
                float y = ring == 0 || ring == 1 ? .055f : ring == 2 ? .030f : ring == 3 ? -.030f : -.055f;
                float a = i * Mathf.PI * 2 / n; return V(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            for (int ring = 0; ring < 6; ring++) for (int i = 0; i < n; i++)
                {
                    int s = v.Count; v.Add(Point(ring, i)); v.Add(Point(ring, (i + 1) % n)); v.Add(Point((ring + 1) % 6, (i + 1) % n)); v.Add(Point((ring + 1) % 6, i));
                    var indices = i / 4 == 2 || i / 4 == 7 || i / 4 == 11 ? rust : ring == 0 || ring == 4 ? face : edge;
                    indices.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }
            var m = new Mesh { name = "Useful salvaged cog" }; m.SetVertices(v); m.subMeshCount = 3; m.SetTriangles(face, 0); m.SetTriangles(edge, 1); m.SetTriangles(rust, 2); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static Mesh MeshFrom(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        // A palette atlas reduces each animated subgroup to one material and one renderer.
        // Metallic/smoothness data is sampled per material, so rubber stays matte and steel catches light.
        public static void RefreshAtlas() { atlas = null; EnsureAtlas(); }
        static void EnsureAtlas()
        {
            if (atlas) return;
            ArtKit.Ensure();
            const int cell = 8, width = 64, height = 32;
            var color = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var response = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            for (int index = 0; index < 32; index++)
            {
                var source = ArtKit.Mat(Palette[Math.Min(index, Palette.Length - 1)]);
                Color c = source.GetColor("_BaseColor");
                Color p = new Color(source.GetFloat("_Metallic"), 0, 0, source.GetFloat("_Smoothness"));
                for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++)
                    { int px = (index % 8) * cell + x, py = (index / 8) * cell + y; color.SetPixel(px, py, c); response.SetPixel(px, py, p); }
            }
            color.Apply(); response.Apply();
            string colorPath = ArtKit.Root + "/Textures/Enemy shared palette.png", responsePath = ArtKit.Root + "/Textures/Enemy shared surface response.png";
            File.WriteAllBytes(colorPath, color.EncodeToPNG()); File.WriteAllBytes(responsePath, response.EncodeToPNG()); Object.DestroyImmediate(color); Object.DestroyImmediate(response);
            AssetDatabase.ImportAsset(colorPath, ImportAssetOptions.ForceUpdate); AssetDatabase.ImportAsset(responsePath, ImportAssetOptions.ForceUpdate);
            ConfigureAtlasTexture(colorPath, true); ConfigureAtlasTexture(responsePath, false);
            string path = ArtKit.Root + "/Materials/Enemy shared palette.mat";
            atlas = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!atlas) { atlas = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Enemy shared palette" }; AssetDatabase.CreateAsset(atlas, path); }
            atlas.SetColor("_BaseColor", Color.white); atlas.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath)); atlas.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(responsePath));
            atlas.SetFloat("_Metallic", 1); atlas.SetFloat("_Smoothness", 1); atlas.SetFloat("_WorkflowMode", 1); atlas.SetFloat("_SmoothnessTextureChannel", 0); atlas.SetFloat("_SpecularHighlights", 1); atlas.EnableKeyword("_METALLICSPECGLOSSMAP"); atlas.enableInstancing = true; EditorUtility.SetDirty(atlas);
        }
        static void ConfigureAtlasTexture(string path, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.sRGBTexture = srgb; importer.mipmapEnabled = false; importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.SaveAndReimport();
        }
        static int PaletteIndex(Material mat)
        {
            for (int i = 0; i < Palette.Length; i++) if (mat == ArtKit.Mat(Palette[i])) return i;
            return Array.IndexOf(Palette, "Steel");
        }
        public static void BakeAtlas(Transform root, string name)
        {
            EnsureAtlas();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(x => x.sharedMesh).ToArray();
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>(); if (!renderer || renderer.sharedMaterials.Length == 0) continue;
                var mesh = filter.sharedMesh; var points = mesh.vertices; var ns = mesh.normals; var sourceUvs = mesh.uv;
                Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix; Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var material = renderer.sharedMaterials[Math.Min(sub, renderer.sharedMaterials.Length - 1)]; int colorIndex = PaletteIndex(material);
                    Vector2 uv = new Vector2((colorIndex % 8 + .5f) / 8f, (colorIndex / 8 + .5f) / 4f);
                    var source = mesh.GetTriangles(sub); var remap = new Dictionary<int, int>();
                    foreach (int index in source)
                    {
                        if (!remap.TryGetValue(index, out int dest))
                        { dest = vertices.Count; remap[index] = dest; vertices.Add(matrix.MultiplyPoint3x4(points[index])); normals.Add(ns.Length == points.Length ? normalMatrix.MultiplyVector(ns[index]).normalized : Vector3.up); uvs.Add(material == atlas && sourceUvs.Length == points.Length ? sourceUvs[index] : uv); }
                        triangles.Add(dest);
                    }
                }
            }
            var result = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 }; result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0, uvs); result.SetTriangles(triangles, 0); result.RecalculateBounds();
            result = ArtKit.SaveMesh(result, name);
            foreach (Transform child in root.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var oldRenderer = root.GetComponent<MeshRenderer>(); if (oldRenderer) Object.DestroyImmediate(oldRenderer);
            var oldFilter = root.GetComponent<MeshFilter>(); if (oldFilter) Object.DestroyImmediate(oldFilter);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = result;
            var finalRenderer = root.gameObject.AddComponent<MeshRenderer>(); finalRenderer.sharedMaterial = atlas; finalRenderer.shadowCastingMode = ShadowCastingMode.On; finalRenderer.receiveShadows = true;
        }
    }
}
