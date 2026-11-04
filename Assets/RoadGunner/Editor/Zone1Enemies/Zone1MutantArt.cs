using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    /// <summary>Original Zone 1 brute. Authoring only: no collider, AI, health or root scale changes.</summary>
    public static class Zone1MutantArt
    {
        const string VisualName = "Zone1 Mutant visuals";
        const string Flesh = "Cloth";       // The shared matte gray-olive palette swatch.
        const string WarmFlesh = "Skin";    // Broad sun-worn tan anatomical planes.
        const string Bone = "Ivory";
        const string BoneShade = "IvoryShade";

        public static Transform Apply(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            ArtKit.Ensure();
            var previous = root.transform.Find(VisualName);
            if (previous) Object.DestroyImmediate(previous.gameObject);
            var rig = ArtKit.Node(VisualName, root.transform).transform;
            var body = Pivot(rig, "Body", V(0, 1.65f, -.055f));
            var head = Pivot(rig, "Head", V(0, 2.43f, .36f));
            var leftArm = Pivot(rig, "Left arm", V(-.78f, 2.19f, .005f));
            var rightArm = Pivot(rig, "Right arm", V(.86f, 2.18f, .035f));
            var leftLeg = Pivot(rig, "Left leg", V(-.345f, 1.22f, -.035f));
            var rightLeg = Pivot(rig, "Right leg", V(.355f, 1.22f, -.055f));

            Body(body);
            Head(head);
            // Fit the longer wedge to the original practical head-depth envelope. This
            // construction transform is baked away; the animation pivot keeps unit scale.
            var headForms = ArtKit.Node("Head sculpted anatomical forms", head).transform;
            foreach (Transform form in head.Cast<Transform>().ToArray()) if (form != headForms) form.SetParent(headForms, false);
            headForms.localScale = V(1, 1, .88f);
            Arm(leftArm, -1, false);
            Arm(rightArm, 1, true);
            Leg(leftLeg, -1);
            Leg(rightLeg, 1);

            // Six rigid anatomical groups retain named, deliberate animation pivots. Each
            // group is one shared-atlas renderer; no runtime mesh construction is required.
            foreach (var part in new[] { body, head, leftArm, rightArm, leftLeg, rightLeg })
                ConceptEnemies.BakeAtlas(part, "Zone1_Mutant_" + part.name.Replace(' ', '_'));
            Layer(rig, root.layer);
            return rig;
        }

        static void Body(Transform t)
        {
            Part(t, "Continuous hunched torso", LoftY("Hunched torso",
                new[] { -.58f, -.36f, -.08f, .20f, .43f, .64f, .78f },
                new[] { .72f, .93f, 1.28f, 1.60f, 1.65f, 1.43f, 1.06f },
                new[] { .60f, .75f, .92f, 1.04f, 1.02f, .90f, .68f },
                new[] { .05f, .025f, -.025f, -.10f, -.18f, -.24f, -.25f }, 20, .91f), Flesh);
            Part(t, "High wedged scapular hump", LoftY("Wedged shoulder hump", new[] { .25f, .48f, .72f, .91f, 1.025f }, new[] { 1.28f, 1.24f, .97f, .55f, .17f }, new[] { .72f, .79f, .70f, .46f, .16f }, new[] { -.29f, -.36f, -.40f, -.39f, -.33f }, 16, .64f), Flesh);
            SweepPart(t, "Forward inclined powerful neck", new[] { V(0, .43f, .16f), V(0, .66f, .25f), V(0, .79f, .35f) }, new[] { .67f, .63f, .49f }, new[] { .63f, .59f, .44f }, Flesh, 16, .72f);
            Ellipsoid(t, "Heavy lower ribcage", V(0, -.17f, .12f), V(.97f, .86f, .75f), Flesh, 18, 12);

            // Broad anatomical color planes
            Part(t, "Broad angular pectoral mantle", LoftY("Pectoral muscle planes", new[] { -.045f, .13f, .31f, .405f }, new[] { .93f, 1.27f, 1.23f, .85f }, new[] { .17f, .255f, .18f, .07f }, new[] { .445f, .425f, .372f, .32f }, 16, .65f), WarmFlesh);
            Part(t, "Tapered abdominal muscle plane", LoftY("Flat abdominal planes", new[] { -.49f, -.32f, -.11f, .075f }, new[] { .53f, .73f, .80f, .73f }, new[] { .13f, .20f, .215f, .12f }, new[] { .31f, .38f, .425f, .412f }, 14, .69f), WarmFlesh);
            // Glowing mutagen core fissure in sternum
            SweepPart(t, "Glowing sternum mutagen fissure", new[] { V(0, .35f, .47f), V(0, .18f, .565f), V(.015f, -.02f, .535f) }, new[] { .065f, .085f, .045f }, new[] { .032f, .035f, .025f }, "Mint", 8, .7f);
            Ellipsoid(t, "Left oblique muscle", V(-.42f, -.18f, .24f), V(.32f, .57f, .38f), Flesh, 14, 10, Quaternion.Euler(0, 0, -12));
            Ellipsoid(t, "Right oblique muscle", V(.43f, -.18f, .24f), V(.35f, .57f, .38f), Flesh, 14, 10, Quaternion.Euler(0, 0, 12));

            // Towering Dorsal Crest of Jagged Bone Spines & Erupting Bio-Luminescent Crystal Shards!
            for (int i = 0; i < 6; i++)
            {
                float tFrac = i / 5f;
                float y = .08f + i * .165f;
                float z = -.52f - Mathf.Sin(tFrac * Mathf.PI) * .12f;
                float h = .32f + Mathf.Sin(tFrac * Mathf.PI) * .24f;
                Vector3 basePos = V(0, y, z);
                Vector3 tipPos = basePos + V(0, h * .82f, -h * .65f);
                SweepPart(t, "Dorsal bone spine " + i, new[] { basePos, Vector3.Lerp(basePos, tipPos, .55f) + V(0, .03f, .02f), tipPos },
                    new[] { .22f, .14f, .012f }, new[] { .26f, .17f, .012f }, Bone, 10, .72f);

                if (i < 5)
                {
                    int side = i % 2 == 0 ? -1 : 1;
                    Vector3 cBase = V(side * .22f, y + .06f, z + .04f);
                    Vector3 cTip = cBase + V(side * .24f, .28f, -.22f);
                    SweepPart(t, "Bio-crystal dorsal shard " + i, new[] { cBase, Vector3.Lerp(cBase, cTip, .5f), cTip },
                        new[] { .15f, .11f, .008f }, new[] { .15f, .11f, .008f }, i % 2 == 0 ? "Mint" : "Glow", 6, .55f);
                }
            }

            // Lateral Scapular Bone Spurs & External Rib Spikes
            for (int side = -1; side <= 1; side += 2)
            {
                SweepPart(t, "Angular scapular ridge", new[] { V(side * .31f, .66f, -.61f), V(side * .54f, .40f, -.58f), V(side * .57f, .17f, -.48f) }, new[] { .19f, .28f, .10f }, new[] { .11f, .16f, .08f }, Flesh, 12, .65f);
                SweepPart(t, "Scapular bone horn", new[] { V(side * .46f, .62f, -.42f), V(side * .68f, .86f, -.58f), V(side * .78f, 1.04f, -.68f) }, new[] { .18f, .11f, .01f }, new[] { .18f, .11f, .01f }, Bone, 10, .75f);
                SweepPart(t, "Strong collar muscle ridge", new[] { V(side * .10f, .53f, .34f), V(side * .36f, .48f, .38f), V(side * .59f, .39f, .27f) }, new[] { .13f, .16f, .12f }, new[] { .11f, .14f, .10f }, WarmFlesh, 12, .72f);
                // Ossified external ribcage spurs
                for (int r = 0; r < 2; r++)
                {
                    float ry = -.05f - r * .18f;
                    SweepPart(t, "Ossified rib spur " + r, new[] { V(side * .52f, ry, .18f), V(side * .68f, ry + .05f, .26f), V(side * .74f, ry + .11f, .32f) },
                        new[] { .11f, .07f, .008f }, new[] { .09f, .06f, .008f }, BoneShade, 8, .8f);
                }
            }

            // Broken Iron Neck-Shackle & Heavy Chains across Torso
            var collar = ArtKit.Torus(t, "Broken iron neck shackle", V(0, .62f, .18f), .42f, .36f, .085f, "Steel", Quaternion.Euler(22, 0, 0), 18);
            collar.localScale = V(1.15f, 1, .95f);
            SweepPart(t, "Broad diagonal salvaged leather harness",
                new[] { V(-.58f, .62f, .20f), V(-.38f, .32f, .51f), V(-.07f, -.01f, .54f), V(.26f, -.35f, .42f), V(.40f, -.45f, .24f) },
                new[] { .13f, .14f, .14f, .14f, .13f }, new[] { .045f, .045f, .045f, .045f, .045f }, "RustDark", 10, .69f);
            Part(t, "Dull harness adjustment clasp", Plate(.18f, .14f, .05f), "Yellow", V(-.07f, -.015f, .575f), Quaternion.Euler(0, 0, -42));
            var belt = ArtKit.Torus(t, "Old heavy waist restraint", V(0, -.44f, .03f), .52f, .47f, .12f, "RustDark", null, 24);
            belt.localScale = V(1, 1, .74f);
            Part(t, "Salvaged hazard belt buckle", Plate(.24f, .18f, .06f), "Red", V(.05f, -.44f, .428f));
            Part(t, "Belt skull emblem", Plate(.12f, .11f, .03f), Bone, V(.05f, -.44f, .455f));
            Part(t, "Torn red brown waist cloth", TornCloth(), "Scarf");
            SweepPart(t, "Broad hanging front cloth fold", new[] { V(-.13f, -.47f, .354f), V(-.11f, -.67f, .372f), V(-.07f, -.91f, .325f) }, new[] { .11f, .105f, .035f }, new[] { .035f, .035f, .025f }, "RustDark", 8, .8f);
            SweepPart(t, "Worn cloth side fold", new[] { V(.38f, -.47f, .19f), V(.36f, -.64f, .245f), V(.32f, -.80f, .21f) }, new[] { .10f, .09f, .025f }, new[] { .025f, .03f, .02f }, "Rust", 8, .8f);
        }

        static void Head(Transform t)
        {
            // Predatory reptilian skull with quad horns and glowing bio-maw
            Part(t, "Long wedge shaped reptilian skull", LoftZ("Reptilian skull planes", new[] { -.285f, -.17f, .055f, .25f, .44f, .635f }, new[] { .22f, .52f, .65f, .53f, .37f, .27f }, new[] { .30f, .56f, .60f, .43f, .23f, .15f }, new[] { .125f, .105f, .072f, .015f, -.035f, -.065f }, 18, .63f), Flesh);
            Part(t, "Separate jutting lower mandible", LoftZ("Underbiting lower jaw", new[] { .045f, .22f, .44f, .64f }, new[] { .45f, .52f, .42f, .25f }, new[] { .23f, .24f, .17f, .10f }, new[] { -.225f, -.255f, -.26f, -.237f }, 16, .66f), Flesh);
            Part(t, "Visible shadow inside the open maw", LoftZ("Open maw shadow", new[] { .18f, .39f, .615f }, new[] { .435f, .36f, .215f }, new[] { .09f, .075f, .055f }, new[] { -.166f, -.171f, -.163f }, 14, .68f), "Rubber");
            // Glowing mutagenic throat/tongue inside the open jaws
            Ellipsoid(t, "Glowing mutagen tongue", V(0, -.165f, .42f), V(.22f, .055f, .28f), "Mint", 10, 6);
            Part(t, "Angular chin underside", LoftZ("Chin keel", new[] { .18f, .40f, .58f }, new[] { .32f, .30f, .18f }, new[] { .12f, .14f, .085f }, new[] { -.34f, -.33f, -.285f }, 12, .69f), Bone);

            for (int side = -1; side <= 1; side += 2)
            {
                SweepPart(t, "Heavy slanted scowling brow", new[] { V(side * .075f, .125f, .34f), V(side * .185f, .215f, .323f), V(side * .315f, .255f, .205f) }, new[] { .135f, .175f, .125f }, new[] { .13f, .155f, .115f }, BoneShade, 12, .63f);
                Part(t, "Narrow angular eye socket", Plate(.19f, .065f, .028f), "Rubber", V(side * .215f, .13f, .316f), Quaternion.Euler(0, side * 19, side * 16));
                Ellipsoid(t, "Blazing watchful mutant eye", V(side * .213f, .132f, .338f), V(.092f, .034f, .022f), "Glow", 12, 6, Quaternion.Euler(0, side * 19, side * 16));
                SweepPart(t, "Angular cheek and jaw hinge", new[] { V(side * .28f, .10f, .175f), V(side * .278f, -.071f, .32f), V(side * .19f, -.17f, .447f) }, new[] { .195f, .18f, .10f }, new[] { .13f, .115f, .07f }, Flesh, 12, .62f);
                Part(t, "Predatory nostril slit", Plate(.064f, .021f, .007f), "Rubber", V(side * .074f, -.032f, .642f), Quaternion.Euler(0, 0, side * 12));
                SweepPart(t, "Horn root ridge", new[] { V(side * .235f, .245f, -.08f), V(side * .275f, .335f, -.12f), V(side * .315f, .355f, -.17f) }, new[] { .25f, .21f, .15f }, new[] { .20f, .19f, .13f }, WarmFlesh, 12, .72f);

                // Swept-back primary bull-ram horns + forward cheek gore-horns
                SweepPart(t, "Massive swept temple horn", new[] { V(side * .267f, .29f, -.09f), V(side * .48f, .56f, -.26f), V(side * .62f, .74f, -.52f), V(side * .52f, .82f, -.82f) },
                    new[] { .22f, .17f, .10f, .008f }, new[] { .20f, .15f, .09f, .008f }, Bone, 14, .82f);
                SweepPart(t, "Forward cheek gore horn", new[] { V(side * .29f, -.04f, .22f), V(side * .42f, -.02f, .36f), V(side * .46f, .05f, .52f) },
                    new[] { .11f, .075f, .006f }, new[] { .10f, .07f, .006f }, BoneShade, 10, .8f);
            }

            SweepPart(t, "Central plated cranial ridge", new[] { V(0, .44f, -.12f), V(0, .38f, .10f), V(0, .23f, .32f) }, new[] { .13f, .14f, .07f }, new[] { .09f, .10f, .05f }, Bone, 10, .65f);
            for (int side = -1; side <= 1; side += 2)
            {
                SweepPart(t, "Long curved lower jaw fang", new[] { V(side * .215f, -.25f, .36f), V(side * .29f, -.21f, .50f), V(side * .32f, -.06f, .63f), V(side * .285f, .06f, .65f) }, new[] { .115f, .098f, .058f, .004f }, new[] { .10f, .088f, .052f, .004f }, Bone, 14, .9f);
                for (int i = 0; i < 3; i++)
                {
                    float z = .30f + i * .105f, x = side * (.22f - i * .035f);
                    SweepPart(t, "Exposed upper tooth " + i, new[] { V(x, -.11f, z), V(x * .98f, -.20f, z + .01f) }, new[] { .052f, .004f }, new[] { .048f, .004f }, Bone, 8);
                }
            }
        }

        static void Arm(Transform t, int side, bool giant)
        {
            float s = giant ? 1.25f : 1f;
            Vector3[] points = giant
                ? new[] { V(0, .04f, 0), V(.16f, -.25f, .01f), V(.24f, -.51f, .07f), V(.25f, -.65f, .11f), V(.27f, -.88f, .21f), V(.20f, -1.12f, .30f), V(.18f, -1.24f, .32f) }
                : new[] { V(0, .04f, 0), V(-.12f, -.22f, .01f), V(-.18f, -.42f, .05f), V(-.18f, -.52f, .08f), V(-.16f, -.72f, .17f), V(-.08f, -.90f, .25f), V(-.05f, -1.01f, .28f) };
            SweepPart(t, "Angular bicep elbow and forearm anatomy", points, new[] { .55f * s, .62f * s, .455f * s, .335f * s, .48f * s, .37f * s, .295f * s }, new[] { .585f * s, .59f * s, .425f * s, .345f * s, .445f * s, .35f * s, .285f * s }, Flesh, 16, .67f);
            SweepPart(t, "Shaped powerful deltoid", new[] { V(0, .17f, -.03f), V(side * .10f, -.03f, -.005f), V(side * .16f, -.31f, .055f) }, new[] { .48f * s, .65f * s, .44f * s }, new[] { .48f * s, .63f * s, .42f * s }, Flesh, 14, .64f);
            SweepPart(t, "Raised angular bicep plane", new[] { V(side * .08f, -.075f, .16f), V(side * .16f, -.25f, .22f), V(side * .20f, -.43f, .15f) }, new[] { .22f * s, .37f * s, .15f * s }, new[] { .10f * s, .18f * s, .07f * s }, Flesh, 12, .65f);
            SweepPart(t, "Forearm extensor muscle plane", new[] { points[3] + V(side * .055f, -.035f, .11f), points[4] + V(side * .035f, .015f, .19f), points[5] + V(side * .012f, .015f, .13f) }, new[] { .15f * s, .27f * s, .12f * s }, new[] { .07f * s, .13f * s, .06f * s }, Flesh, 12, .65f);
            Ellipsoid(t, "Prominent angular elbow knot", points[3] + V(side * .065f, .01f, -.075f), V(.27f * s, .25f * s, .27f * s), Bone, 10, 7);
            Vector3 palm = points[points.Length - 1];
            Ellipsoid(t, "Broad powerful claw palm", palm + V(0, -.045f, .025f), V(giant ? .55f : .38f, giant ? .49f : .38f, giant ? .45f : .33f), Flesh, 18, 12, Quaternion.Euler(-8, 0, side * -6));

            int fingers = 3;
            for (int i = 0; i < fingers; i++)
            {
                float x = (i - 1) * (giant ? .175f : .125f);
                float stagger = i == 1 ? .045f : 0;
                Vector3 a = palm + V(x, -(giant ? .17f : .13f), .09f);
                Vector3 b = a + V(x * .04f, -(giant ? .22f : .17f) - stagger, .06f);
                Vector3 c = b + V(-x * .10f, -.065f, .12f);
                SweepPart(t, "Separate thick claw finger " + i, new[] { a, b, c }, new[] { giant ? .16f : .115f, giant ? .135f : .095f, .085f }, new[] { giant ? .15f : .11f, giant ? .12f : .09f, .075f }, Flesh, 12);
                SweepPart(t, "Curved keratin claw " + i, new[] { c, c + V(0, -.065f, .13f), c + V(-x * .08f, -.025f, .25f) }, new[] { .09f, .065f, .004f }, new[] { .08f, .06f, .004f }, Bone, 12);
                Ellipsoid(t, "Finger knuckle " + i, a + V(0, .02f, -.04f), V(.14f, .14f, .12f), giant ? "Mint" : WarmFlesh, 10, 7);
            }
            Vector3 thumb = palm + V(-side * (giant ? .23f : .17f), .04f, .10f);
            SweepPart(t, "Opposable heavy claw thumb", new[] { thumb, thumb + V(-side * .11f, -.13f, .10f), thumb + V(-side * .07f, -.25f, .16f) }, new[] { giant ? .19f : .14f, giant ? .15f : .11f, .09f }, new[] { giant ? .18f : .13f, giant ? .14f : .10f, .08f }, Flesh, 12);
            SweepPart(t, "Hooked thumb talon", new[] { thumb + V(-side * .07f, -.25f, .16f), thumb + V(side * .005f, -.30f, .26f), thumb + V(side * .075f, -.255f, .31f) }, new[] { .095f, .065f, .004f }, new[] { .085f, .055f, .004f }, Bone, 12);

            if (!giant)
            {
                // Salvaged Highway Stop-Sign / Bulldozer Plough Pauldron on Left Shoulder
                Part(t, "Scavenged stop-sign shoulder guard", Plate(.78f, .52f, .14f), "Red", V(-.08f, -.045f, .275f), Quaternion.Euler(-20, -12, 18));
                Part(t, "Shoulder guard ivory border", Plate(.62f, .38f, .02f), Bone, V(-.08f, -.045f, .348f), Quaternion.Euler(-20, -12, 18));
                Part(t, "Shoulder exposed metal edge", Plate(.22f, .06f, .018f), "SteelLight", V(.02f, .14f, .33f), Quaternion.Euler(-20, -12, 18));
                var strap = ArtKit.Torus(t, "Leather shoulder restraint", V(-.095f, -.25f, .025f), .32f, .28f, .11f, "RustDark", Quaternion.Euler(0, 0, -13), 20);
                strap.localScale = V(1, 1, .93f);
                Part(t, "Forearm short salvaged guard", Plate(.34f, .39f, .10f), "Steel", V(-.13f, -.73f, .397f), Quaternion.Euler(-8, 0, -12));
                Part(t, "Forearm hazard stripe", Plate(.14f, .25f, .016f), "Yellow", V(-.14f, -.73f, .455f), Quaternion.Euler(-8, 0, -12));
            }
            else
            {
                // Colossal Right Crusher Arm: Erupting Forearm Bone-Blades, Bio-Crystal Clusters & Wrapped Anchor Chain!
                for (int i = 0; i < 3; i++)
                {
                    Vector3 bStart = V(.30f + i * .03f, -.44f - i * .21f, -.06f + i * .04f);
                    Vector3 bEnd = bStart + V(.26f, .14f - i * .04f, -.18f);
                    SweepPart(t, "Forearm bone cleaver blade " + i, new[] { bStart, Vector3.Lerp(bStart, bEnd, .5f), bEnd },
                        new[] { .22f - i * .02f, .14f, .01f }, new[] { .14f, .09f, .008f }, Bone, 10, .65f);
                }
                // Wrapped heavy iron anchor chains around forearm and fist
                for (int c = 0; c < 3; c++)
                {
                    var chain = ArtKit.Torus(t, "Crusher arm anchor chain " + c, V(.23f, -.78f - c * .15f, .22f + c * .04f), .28f - c * .02f, .22f - c * .02f, .065f, c == 1 ? "SteelLight" : "Steel", Quaternion.Euler(18 + c * 8, 12, -15), 14);
                    chain.localScale = V(1.1f, 1, .95f);
                }
                // Glowing crystal eruption on outer deltoid
                SweepPart(t, "Deltoid crystal cluster", new[] { V(.26f, .08f, -.02f), V(.48f, .26f, -.08f), V(.60f, .40f, -.12f) },
                    new[] { .24f, .16f, .01f }, new[] { .22f, .14f, .01f }, "Glow", 6, .55f);
                Ellipsoid(t, "Outer deltoid bony crest", V(.265f, .105f, -.025f), V(.29f, .23f, .33f), Bone, 14, 10, Quaternion.Euler(0, 0, -20));
            }
        }

        static void Leg(Transform t, int side)
        {
            SweepPart(t, "Heavy bowed muscular thigh", new[] { V(0, .04f, 0), V(side * .07f, -.26f, .025f), V(side * .10f, -.53f, .07f) }, new[] { .52f, .55f, .41f }, new[] { .54f, .53f, .39f }, Flesh, 18, .94f);
            Ellipsoid(t, "Heavy outer thigh muscle", V(side * .08f, -.24f, .13f), V(.40f, .53f, .33f), WarmFlesh, 16, 12, Quaternion.Euler(0, 0, side * 12));
            SweepPart(t, "Bent weight bearing shin", new[] { V(side * .10f, -.51f, .06f), V(side * .08f, -.74f, .10f), V(side * .045f, -.98f, .14f) }, new[] { .39f, .34f, .265f }, new[] { .40f, .33f, .27f }, Flesh, 16, .91f);
            // Jagged knee bone horn
            SweepPart(t, "Bony knee horn", new[] { V(side * .105f, -.55f, .22f), V(side * .12f, -.46f, .34f), V(side * .13f, -.36f, .42f) },
                new[] { .26f, .16f, .01f }, new[] { .22f, .14f, .01f }, Bone, 10, .75f);
            Ellipsoid(t, "Rounded heavy heel and planted foot", V(side * .04f, -1.098f, .205f), V(.46f, .24f, .66f), Flesh, 20, 12);
            Ellipsoid(t, "Dust worn upper foot plane", V(side * .04f, -1.056f, .35f), V(.39f, .14f, .41f), WarmFlesh, 16, 10);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * .145f + side * .04f;
                var start = V(x, -1.10f, .407f);
                SweepPart(t, "Separate thick planted toe " + i, new[] { start, start + V(0, -.015f, .12f), start + V((i - 1) * .012f, -.025f, .215f) }, new[] { .17f, .16f, .115f }, new[] { .15f, .14f, .095f }, Flesh, 12);
                SweepPart(t, "Short weight bearing toe claw " + i, new[] { start + V((i - 1) * .012f, -.025f, .205f), start + V((i - 1) * .017f, -.028f, .30f), start + V((i - 1) * .017f, -.005f, .38f) }, new[] { .12f, .09f, .004f }, new[] { .09f, .065f, .004f }, Bone, 12);
            }
            if (side < 0)
            {
                Part(t, "Old knee salvaged protection", Plate(.30f, .27f, .075f), "Red", V(-.11f, -.52f, .313f), Quaternion.Euler(-10, 0, -8));
                Part(t, "Knee restrained yellow patch", Plate(.14f, .10f, .012f), "Yellow", V(-.155f, -.55f, .357f), Quaternion.Euler(-10, 0, -14));
            }
        }

        static Transform Pivot(Transform parent, string name, Vector3 position)
        { var t = ArtKit.Node(name, parent).transform; t.localPosition = position; return t; }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static void Layer(Transform t, int layer) { t.gameObject.layer = layer; foreach (Transform c in t) Layer(c, layer); }
        static void Ellipsoid(Transform parent, string name, Vector3 position, Vector3 size, string material, int segments = 16, int rings = 10, Quaternion? rotation = null)
        { var t = ArtKit.Ellipsoid(parent, name, position, size, material, segments, rings); if (rotation.HasValue) t.localRotation = rotation.Value; }
        static Transform Part(Transform parent, string name, Mesh mesh, string material, Vector3? position = null, Quaternion? rotation = null)
        { return ArtKit.Part(parent, name, mesh, position ?? Vector3.zero, Vector3.one, material, rotation); }
        static void SweepPart(Transform parent, string name, Vector3[] points, float[] widths, float[] depths, string material, int segments = 14, float power = 1)
        { Part(parent, name, Sweep(name, points, widths, depths, segments, power), material); }

        // Smooth station-based anatomy with elliptical cross sections. The start/end caps
        // are closed and all triangle winding is outward. No capsule primitives are used.
        static Mesh Sweep(string name, Vector3[] points, float[] widths, float[] depths, int segments = 14, float power = 1)
        {
            var v = new List<Vector3>(); var tr = new List<int>(); Vector3 lastU = Vector3.right;
            for (int row = 0; row < points.Length; row++)
            {
                Vector3 tangent = (points[Math.Min(row + 1, points.Length - 1)] - points[Math.Max(0, row - 1)]).normalized;
                Vector3 u = Vector3.Cross(tangent, Vector3.forward);
                if (u.sqrMagnitude < .015f) u = Vector3.ProjectOnPlane(lastU, tangent);
                if (u.sqrMagnitude < .001f) u = Vector3.Cross(tangent, Vector3.up);
                u.Normalize(); if (row > 0 && Vector3.Dot(u, lastU) < 0) u = -u; lastU = u;
                Vector3 b = Vector3.Cross(tangent, u).normalized;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    v.Add(points[row] + u * (Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power) * widths[row] * .5f) + b * (Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power) * depths[row] * .5f));
                }
            }
            for (int row = 0; row < points.Length - 1; row++) for (int i = 0; i < segments; i++)
                { int a = row * segments + i, b = row * segments + (i + 1) % segments, c = a + segments, d = b + segments; tr.AddRange(new[] { a, b, c, b, d, c }); }
            int start = v.Count; v.Add(points[0]); int end = v.Count; v.Add(points[points.Length - 1]); int last = (points.Length - 1) * segments;
            for (int i = 0; i < segments; i++) { int next = (i + 1) % segments; tr.AddRange(new[] { start, next, i, end, last + i, last + next }); }
            return Mesh(name, v, tr);
        }
        static Mesh LoftY(string name, float[] y, float[] widths, float[] depths, float[] z, int segments = 18, float power = .9f)
        { return Sweep(name, y.Select((value, i) => V(0, value, z[i])).ToArray(), widths, depths, segments, power); }
        static Mesh LoftZ(string name, float[] z, float[] widths, float[] heights, float[] y, int segments = 16, float power = .7f)
        { return Sweep(name, z.Select((value, i) => V(0, y[i], value)).ToArray(), widths, heights, segments, power); }

        static Mesh Plate(float width, float height, float depth)
        {
            Vector2[] corners = { new Vector2(-.32f, -.5f), new Vector2(.35f, -.47f), new Vector2(.49f, -.13f), new Vector2(.44f, .30f), new Vector2(.29f, .5f), new Vector2(-.35f, .45f), new Vector2(-.5f, .20f), new Vector2(-.46f, -.23f) };
            var outline = new List<Vector2>();
            for (int i = 0; i < corners.Length; i++)
            {
                var p = corners[i]; var from = Vector2.Lerp(p, corners[(i + corners.Length - 1) % corners.Length], .20f); var to = Vector2.Lerp(p, corners[(i + 1) % corners.Length], .20f);
                for (int j = 0; j < 3; j++) { float t = j * .5f; outline.Add((1 - t) * (1 - t) * from + 2 * (1 - t) * t * p + t * t * to); }
            }
            int n = outline.Count; var v = new List<Vector3>(); var tr = new List<int>(); float bevel = Mathf.Min(.025f, depth * .28f);
            float[] z = { -depth * .5f, -depth * .5f + bevel, depth * .5f - bevel, depth * .5f }; float[] scales = { .91f, 1f, 1f, .875f };
            for (int row = 0; row < 4; row++) foreach (var p in outline) v.Add(V(p.x * width * scales[row], p.y * height * scales[row], z[row]));
            for (int row = 0; row < 3; row++) for (int i = 0; i < n; i++) { int a = row * n + i, b = row * n + (i + 1) % n, c = a + n, d = b + n; tr.AddRange(new[] { a, b, c, b, d, c }); }
            int back = v.Count; v.Add(V(0, 0, -depth * .5f)); int front = v.Count; v.Add(V(0, 0, depth * .5f + Mathf.Min(.018f, depth * .16f)));
            for (int i = 0; i < n; i++) { int next = (i + 1) % n; tr.AddRange(new[] { back, next, i, front, n * 3 + i, n * 3 + next }); }
            return Mesh("Beveled asymmetric salvage plate", v, tr);
        }

        static Mesh TornCloth()
        {
            const int n = 24; var v = new List<Vector3>(); var tr = new List<int>();
            for (int shell = 0; shell < 2; shell++) for (int row = 0; row < 3; row++) for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2 / n;
                        float fold = 1 + Mathf.Cos(a * 6) * .035f;
                        float width = (row == 0 ? .98f : row == 1 ? .91f : .78f) * fold - shell * .018f;
                        float depth = (row == 0 ? .67f : row == 1 ? .69f : .58f) * fold - shell * .018f;
                        float y = row == 0 ? -.44f : row == 1 ? -.66f : -.86f - (i % 4 == 0 ? .105f : i % 4 == 2 ? .045f : 0);
                        v.Add(V(Mathf.Cos(a) * width * .5f, y, Mathf.Sin(a) * depth * .5f + .025f));
                    }
            for (int shell = 0; shell < 2; shell++) for (int row = 0; row < 2; row++) for (int i = 0; i < n; i++)
                    {
                        int a = shell * n * 3 + row * n + i, b = shell * n * 3 + row * n + (i + 1) % n, c = a + n, d = b + n;
                        // Stations descend in Y, so the outer face winds angle-to-next-station.
                        if (shell == 0) tr.AddRange(new[] { a, b, c, b, d, c }); else tr.AddRange(new[] { a, c, b, b, c, d });
                    }
            for (int row = 0; row < 3; row += 2) for (int i = 0; i < n; i++)
                {
                    int a = row * n + i, b = row * n + (i + 1) % n, c = a + n * 3, d = b + n * 3;
                    if (row == 0) tr.AddRange(new[] { a, c, b, b, c, d }); else tr.AddRange(new[] { a, b, c, b, d, c });
                }
            return Mesh("Torn thick folded waist cloth", v, tr);
        }

        static Mesh Mesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
