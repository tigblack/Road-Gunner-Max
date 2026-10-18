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
            float broad = variant == 0 ? 1 : variant == 1 ? 1.08f : 1.16f;
            // The pelvis-to-head height stays inside the original 1.9 m capsule.
            ArtKit.Part(body, "Tailored ragged jacket", TorsoLoft(broad), Vector3.zero, Vector3.one, "Cloth");
            ArtKit.Part(body, "Sculpted trouser pelvis", OrganicLoft("Trouser pelvis", new[]{.665f,.735f,.84f,.90f},new[]{.45f*broad,.55f*broad,.55f*broad,.47f*broad},new[]{.34f,.40f,.40f,.34f},new[]{-.005f,-.005f,-.005f,-.005f},16,.72f), Vector3.zero, Vector3.one, "Cloth");
            ArtKit.Box(body, "Heavy leather waist belt", V(0, .86f, .015f), V(.57f * broad, .12f, .42f), "RustDark", .025f);
            ArtKit.Box(body, "Dull steel buckle", V(0, .862f, .239f), V(.12f, .10f, .04f), "SteelLight", .014f);
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Box(body, "Salvaged belt pouch", V(side * .245f * broad, .855f, .21f), V(.19f, .21f, .13f), side < 0 ? "IvoryShade" : "RustDark", .025f);
                ArtKit.Box(body, "Pouch flap", V(side * .245f * broad, .927f, .286f), V(.18f, .055f, .035f), "Cloth", .012f);
                ArtKit.Beam(body, "Broad scavenged harness", V(side * .24f * broad, 1.42f, .18f), V(side * .17f * broad, .94f, .252f), .075f, "RustDark");
                ArtKit.Box(body, "Harness clasp", V(side * .205f * broad, 1.14f, .273f), V(.10f, .06f, .025f), "SteelLight", .009f);
                var flap = ArtKit.Box(body, "Uneven jacket tail", V(side * .18f, .775f, .174f), V(.245f, .25f, .09f), "Cloth", .025f);
                flap.localRotation = Quaternion.Euler(0, side * 5, side * 6);
            }
            ArtKit.Part(body, "Curved tapered scavenged breastplate", SculptedPlate(.56f*broad,.425f,.115f), V(.012f,1.205f,.232f),Vector3.one,"Steel",Quaternion.Euler(-5,0,0));
            ArtKit.Beam(body, "Breastplate softened upper rim", V(-.18f*broad,1.403f,.264f), V(.18f*broad,1.403f,.264f), .042f, "SteelLight");
            ArtKit.Part(body,"Broad irregular oxidized breastplate patch",SculptedPlate(.205f,.12f,.008f),V(-.105f,1.18f,.309f),Vector3.one,"Rust",Quaternion.Euler(-5,0,-12));
            ArtKit.Part(body,"Chest softened edge wear",SculptedPlate(.095f,.031f,.007f),V(.15f*broad,1.33f,.303f),Vector3.one,"SteelLight",Quaternion.Euler(-5,0,12));
            if (variant == 2)
            {
                ArtKit.Part(body,"Heavy rounded abdominal plate",SculptedPlate(.49f,.185f,.08f),V(0,1.005f,.242f),Vector3.one,"IvoryShade");
                ArtKit.Part(body,"Heavy shaped back salvage plate",SculptedPlate(.63f,.43f,.11f),V(0,1.24f,-.22f),Vector3.one,"RustDark",Quaternion.Euler(0,180,0));
                ArtKit.Beam(body, "Heavy neck protection left", V(-.25f, 1.37f, -.18f), V(-.25f, 1.57f, -.10f), .10f, "Steel");
                ArtKit.Beam(body, "Heavy neck protection right", V(.25f, 1.37f, -.18f), V(.25f, 1.57f, -.10f), .10f, "Steel");
            }

            // A proper rounded skull and helmet replace the old oversized block head.
            ArtKit.Ellipsoid(body, "Sunburnt head", V(0, 1.67f, .022f), V(.355f, .38f, .34f), "Skin", 16, 10);
            ArtKit.Ellipsoid(body, "Organic pressed steel helmet dome", V(0, 1.782f, -.008f), V(.455f, .272f, .424f), "Steel", 20, 14);
            var helmetRim=ArtKit.Torus(body,"Helmet curved raised brow",V(0,1.752f,-.008f),.234f,.210f,.032f,"SteelLight",null,24);helmetRim.localScale=V(1,1,.92f);
            ArtKit.Ellipsoid(body,"Organic respirator face mask",V(0,1.625f,.187f),V(.285f,.228f,.164f),"Rubber",16,10);
            ArtKit.Ellipsoid(body,"Respirator nose bridge",V(0,1.69f,.236f),V(.078f,.12f,.095f),"Steel",12,8);
            ArtKit.Part(body,"Shaped mask breathing grille",SculptedPlate(.133f,.075f,.025f),V(0,1.603f,.264f),Vector3.one,"SteelLight");
            for (int i = -1; i <= 1; i++) ArtKit.Box(body, "Mask broad vent", V(i * .033f, 1.603f, .281f), V(.012f, .035f, .008f), "Steel", .003f);
            var goggleStrap=ArtKit.Torus(body,"Rounded leather goggle strap",V(0,1.713f,-.011f),.203f,.184f,.067f,"RustDark",null,20);goggleStrap.localScale=V(1,1,.90f);
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Cylinder(body, "Crude steel goggle rim", V(side * .091f, 1.715f, .208f), .083f, .043f, "Steel", AlongBarrel, 12);
                ArtKit.Cylinder(body, "Smoky goggle lens", V(side * .091f, 1.715f, .234f), .059f, .018f, "Glass", AlongBarrel, 12);
                ArtKit.Box(body, "Lens reflected daylight", V(side * .091f - .016f, 1.737f, .248f), V(.038f, .013f, .007f), "Ivory", .003f);
                ArtKit.Ellipsoid(body, "Helmet rounded side protection", V(side * .193f, 1.70f, -.025f), V(.092f, .173f, .183f), "Steel",12,8);
            }
            ArtKit.Ellipsoid(body, "Rolled red brown scarf", V(0, 1.486f, .036f), V(.49f, .176f, .395f), "Scarf", 14, 8);
            ArtKit.Box(body, "Scarf broad folded front", V(0, 1.484f, .228f), V(.355f, .14f, .095f), "Scarf", .03f, Quaternion.Euler(0, 0, -8));
            ArtKit.Part(body, "Hanging triangular scarf fold", ScarfPoint(), V(.025f, 1.40f, .266f), V(.25f, .24f, .07f), "Scarf", Quaternion.Euler(0, 0, -7));
            ArtKit.Box(body, "Scarf light catching fold", V(.09f, 1.486f, .28f), V(.12f, .026f, .017f), "Rust", .008f, Quaternion.Euler(0, 0, -8));
            if (variant == 1)
            {
                AddSpike(body, "Enforcer helmet spike", V(0, 1.93f, -.012f), V(.095f, .16f, .095f), Quaternion.identity, "SteelLight");
                ArtKit.Box(body, "Enforcer helmet red stripe", V(0, 1.887f, .06f), V(.075f, .026f, .21f), "Scarf", .007f);
            }
            else if (variant == 2)
            {
                ArtKit.Box(body, "Veteran helmet welded ridge", V(0, 1.913f, -.01f), V(.095f, .08f, .30f), "SteelLight", .022f);
                ArtKit.Part(body,"Veteran curved brow shield",SculptedPlate(.40f,.077f,.045f),V(0,1.768f,.198f),Vector3.one,"IvoryShade");
            }

            for (int side = -1; side <= 1; side += 2)
            {
                var leg = ArtKit.Node(side < 0 ? "Left leg" : "Right leg", rig.transform).transform;
                leg.localPosition = V(side * .184f * broad, .77f, 0);
                var thigh=ArtKit.Ellipsoid(leg,"Soft cloth trouser thigh",V(0,-.147f,.002f),V(.252f*broad,.365f,.282f),"Cloth",14,10);thigh.localRotation=Quaternion.Euler(side*3,0,side*2);
                ArtKit.Part(leg,"Shaped dusty trouser calf",OrganicLoft("Trouser calf",new[]{-.56f,-.50f,-.37f,-.285f},new[]{.18f,.215f,.235f,.205f},new[]{.205f,.24f,.255f,.22f},new[]{-.018f,-.018f,-.015f,-.008f},14,.85f),Vector3.zero,Vector3.one,"Cloth");
                ArtKit.Part(leg,"Curved steel knee guard",SculptedPlate(.237f,.207f,.074f),V(0,-.326f,.15f),Vector3.one,variant==2?"IvoryShade":"Steel",Quaternion.Euler(-7,0,side*5));
                ArtKit.Part(leg,"Broad knee oxidized patch",SculptedPlate(.092f,.051f,.006f),V(side*-.025f,-.31f,.199f),Vector3.one,"Rust",Quaternion.Euler(-7,0,-side*14));
                ArtKit.Part(leg,"Sculpted leather ankle and boot toe",OrganicLoft("Dusty shaped boot",new[]{-.735f,-.698f,-.63f,-.57f,-.49f},new[]{.27f,.283f,.276f,.232f,.215f},new[]{.393f,.415f,.373f,.293f,.24f},new[]{.067f,.071f,.063f,.013f,-.018f},18,.69f),Vector3.zero,Vector3.one,"RustDark");
                ArtKit.Part(leg,"Shaped heavy rubber boot sole",OrganicLoft("Boot sole",new[]{-.756f,-.739f,-.704f},new[]{.265f,.295f,.283f},new[]{.387f,.427f,.414f},new[]{.069f,.070f,.071f},18,.64f),Vector3.zero,Vector3.one,"Rubber");
                ArtKit.Ellipsoid(leg,"Dust polished rounded boot toe",V(0,-.647f,.203f),V(.242f,.104f,.118f),"RubberEdge",16,8);
                var cuff=ArtKit.Torus(leg,"Leather boot cuff",V(0,-.493f,-.018f),.118f,.096f,.045f,"RustDark",null,14);cuff.localScale=V(1,1,1.10f);
                BakeAtlas(leg, "Raider_" + label + (side < 0 ? "_LeftLeg" : "_RightLeg"));

                var arm = ArtKit.Node(side < 0 ? "Left arm" : "Right arm", rig.transform).transform;
                arm.localPosition = V(side * .39f * broad, 1.39f, -.005f);
                ArtKit.Ellipsoid(arm, "Expressive exposed upper arm", V(side * .025f, -.19f, .008f), V(.235f * broad, .355f, .254f), "Skin", 12, 8);
                ArtKit.Ellipsoid(arm, "Elbow", V(side * .018f, -.343f, .012f), V(.23f, .17f, .245f), "Skin", 10, 6);
                ArtKit.Part(arm,"Rounded tapered forearm bracer",OrganicLoft("Forearm armor",new[]{-.565f,-.505f,-.365f,-.325f},new[]{.195f,.23f,.25f,.215f},new[]{.223f,.26f,.269f,.232f},new[]{.015f,.015f,.014f,.010f},14,.77f),Vector3.zero,Vector3.one,side<0?"Steel":"RustDark");
                var binding=ArtKit.Torus(arm,"Leather wrist binding",V(0,-.53f,.015f),.126f,.107f,.052f,"RustDark",null,14);binding.localScale=V(1,1,1.10f);
                ArtKit.Ellipsoid(arm,"Rounded heavy work glove",V(0,-.607f,.043f),V(.239f,.195f,.272f),"Rubber",14,10);
                ArtKit.Ellipsoid(arm,"Curved glove knuckle plate",V(0,-.584f,.172f),V(.195f,.082f,.070f),"Steel",12,8);
                float padWidth = variant == 0 ? (side < 0 ? .33f : .27f) : variant == 1 ? .37f : .415f;
                ArtKit.Part(arm,"Domed shaped shoulder shell",OrganicLoft("Forged shoulder shell",new[]{-.09f,-.043f,.042f,.10f},new[]{padWidth*.88f,padWidth,padWidth*.85f,padWidth*.45f},new[]{.345f,.39f,.345f,.19f},new[]{.005f,.005f,.005f,.005f},18,.73f),V(side*.025f,0,0),Vector3.one,variant==0&&side>0?"RustDark":"Steel",Quaternion.Euler(0,0,side*-12));
                var rim=ArtKit.Torus(arm,"Curved shoulder edge reinforcement",V(side*.025f,-.049f,.005f),padWidth*.5f,padWidth*.5f-.025f,.037f,"SteelLight",Quaternion.Euler(0,0,side*-12),18);rim.localScale=V(1,1,.39f/padWidth);
                ArtKit.Part(arm,"Broad chipped shoulder paint",SculptedPlate(padWidth*.35f,.12f,.008f),V(side*.012f,.098f,.02f),Vector3.one,"Rust",Quaternion.Euler(-90,side*14,side*-12));
                int count = variant == 0 ? (side < 0 ? 1 : 0) : variant == 1 ? 3 : 2;
                for (int i = 0; i < count; i++) AddSpike(arm, "Scavenged shoulder spike", V(side * (.05f + i * .065f), .123f, -.07f + i * .09f), V(.09f, variant == 1 ? .18f : .14f, .09f), Quaternion.Euler(side * 9, 0, side * -18), "SteelLight");
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
                ArtKit.Cylinder(arm,"Crowbar long round shaft",V(.025f,-.225f,.17f),.033f,.91f,"Steel",null,12);
                ArtKit.Beam(arm, "Crowbar hooked upper neck", V(.025f, .23f, .17f), V(.09f, .325f, .17f), .075f, "SteelLight");
                ArtKit.Beam(arm, "Crowbar returned flattened tip", V(.09f, .325f, .17f), V(.19f, .325f, .17f), .07f, "SteelLight");
                ArtKit.Box(arm, "Crowbar worn grip", V(.025f, -.52f, .17f), V(.09f, .23f, .09f), "RustDark", .018f);
            }
            else if (variant == 1)
            {
                ArtKit.Cylinder(arm, "Salvaged club timber handle", V(.035f, -.325f, .17f), .053f, .79f, "Wood", null, 10);
                ArtKit.Part(arm,"Shaped salvaged timber club head",OrganicLoft("Timber club",new[]{-.065f,.005f,.20f,.385f,.405f},new[]{.155f,.215f,.228f,.203f,.15f},new[]{.175f,.235f,.246f,.225f,.165f},new[]{.17f,.17f,.17f,.17f,.17f},12,.78f),V(.035f,0,0),Vector3.one,"Wood");
                for (int j = 0; j < 3; j++)
                {
                    var band=ArtKit.Torus(arm,"Club curved steel band",V(.035f,.015f+j*.16f,.17f),.124f,.105f,.055f,"Steel",null,12);band.localScale=V(1,1,1.09f);
                    foreach (int side in new[] { -1, 1 }) AddSpike(arm, "Club welded spike", V(.035f + side * .16f, .015f + j * .16f, .17f), V(.075f, .16f, .075f), Quaternion.Euler(0, 0, -side * 90), "SteelLight");
                }
                ArtKit.Box(arm, "Club cloth grip", V(.035f, -.51f, .17f), V(.12f, .23f, .12f), "Scarf", .022f);
            }
            else
            {
                ArtKit.Cylinder(arm, "Salvage hammer handle", V(.035f, -.24f, .17f), .051f, .93f, "Steel", null, 12);
                ArtKit.Box(arm, "Heavy improvised hammer core", V(.035f, .29f, .17f), V(.48f, .235f, .28f), "RustDark", .05f);
                for (int side = -1; side <= 1; side += 2) ArtKit.Box(arm, "Hammer exposed steel striking face", V(.035f + side * .235f, .29f, .17f), V(.075f, .245f, .29f), "SteelLight", .025f);
                ArtKit.Box(arm, "Hammer worn grip", V(.035f, -.52f, .17f), V(.115f, .235f, .115f), "Wood", .023f);
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
            ArtKit.Box(body, "Heavy recessed chassis", V(0, .245f, 0), V(1.46f, .19f, 3.17f), "Steel", .055f);
            ArtKit.Box(body, "Tapered rust red hood", V(0, .64f, 1.01f), V(1.48f, .245f, 1.18f), "Red", .075f, Quaternion.Euler(-5, 0, 0));
            ArtKit.Box(body, "Rounded front nose", V(0, .48f, 1.53f), V(1.48f, .24f, .19f), "Rust", .06f);
            ArtKit.Box(body, "Sun faded hood identification band", V(-.32f, .779f, 1.01f), V(.22f, .015f, 1.08f), "IvoryShade", .006f, Quaternion.Euler(-5, 0, 0));
            ArtKit.Box(body, "Broad hood rust damage", V(.24f, .784f, .95f), V(.39f, .016f, .30f), "Rust", .013f, Quaternion.Euler(-5, 8, 0));
            ArtKit.Box(body, "Hood edge rubbed metal", V(.54f, .737f, 1.40f), V(.24f, .022f, .11f), "SteelLight", .009f, Quaternion.Euler(-5, 0, 0));
            ArtKit.Box(body, "Dark front intake", V(0, .48f, 1.636f), V(.73f, .12f, .025f), "Rubber", .014f);
            for (int j = 0; j < 4; j++) ArtKit.Box(body, "Broad intake steel slat", V(-.255f + j * .17f, .48f, 1.655f), V(.028f, .105f, .025f), "SteelLight", .007f);
            for (int side = -1; side <= 1; side += 2)
            {
                ArtKit.Beam(body, "Strong rusty chassis rail", V(side * .69f, .32f, -1.53f), V(side * .69f, .34f, 1.58f), .12f, "RustDark");
                ArtKit.Box(body, "Scavenged door armor", V(side * .756f, .63f, -.12f), V(.105f, .49f, 1.09f), side < 0 ? "Red" : "Rust", .038f, Quaternion.Euler(0, 0, side * -6));
                ArtKit.Box(body, "Door cream role marking", V(side * .82f, .68f, -.12f), V(.012f, .24f, .14f), "IvoryShade", .01f);
                ArtKit.Box(body, "Door broad worn lower edge", V(side * .82f, .43f, -.27f), V(.018f, .055f, .42f), "RustDark", .015f);
                ArtKit.Beam(body, "Roll cage front pillar", V(side * .63f, .57f, .45f), V(side * .59f, 1.48f, .17f), .115f, "Steel");
                ArtKit.Beam(body, "Roll cage rear pillar", V(side * .66f, .45f, -.88f), V(side * .59f, 1.48f, -.72f), .115f, "RustDark");
                ArtKit.Beam(body, "Roof frame side rail", V(side * .59f, 1.48f, .17f), V(side * .59f, 1.48f, -.72f), .115f, "Steel");
                ArtKit.Beam(body, "Exposed rear diagonal", V(side * .58f, 1.44f, -.73f), V(side * .64f, .42f, -1.51f), .095f, "Steel");
                ArtKit.Box(body, "Small lamp steel housing", V(side * .56f, .51f, 1.65f), V(.27f, .19f, .12f), "Steel", .035f);
                ArtKit.Box(body, "Dusty amber headlamp", V(side * .56f, .51f, 1.718f), V(.19f, .12f, .018f), "Yellow", .018f);
                ArtKit.Box(body, "Rear red lamp", V(side * .61f, .45f, -1.61f), V(.18f, .11f, .07f), "Scarf", .019f);
                ArtKit.Beam(body, "Raised rear exhaust", V(side * .64f, .37f, -1.25f), V(side * .70f, 1.02f, -1.55f), .115f, "Steel");
                ArtKit.Cylinder(body, "Black exhaust mouth", V(side * .70f, 1.03f, -1.555f), .043f, .015f, "Rubber", Quaternion.Euler(-25, 0, 0), 10);
                for (int axle = 0; axle < 2; axle++)
                {
                    int index = axle * 2 + (side < 0 ? 0 : 1);
                    if (b.wheels == null || index >= b.wheels.Length || !b.wheels[index]) continue;
                    var wc = b.wheels[index];
                    Vector3 p = b.transform.InverseTransformPoint(wc.transform.position);
                    ArtKit.Beam(body, "Exposed suspension lower link", V(side * .42f, .20f, p.z), V(p.x, p.y, p.z), .085f, "Steel");
                    ArtKit.Beam(body, "Dusty visible shock absorber", V(side * .64f, .60f, p.z), V(p.x, p.y, p.z), .075f, "IvoryShade");
                    ArtKit.Part(body, "Shaped armored wheel arch", FenderMesh(wc.radius + .075f, wc.radius + .165f, .325f), V(p.x, p.y, p.z), Vector3.one, axle == 0 ? "Red" : "Rust");
                }
            }
            ArtKit.Box(body, "Salvaged armored cabin roof", V(0, 1.52f, -.26f), V(1.40f, .11f, 1.08f), "Rust", .045f);
            ArtKit.Box(body, "Roof worn ivory band", V(-.32f, 1.582f, -.26f), V(.22f, .014f, .96f), "IvoryShade", .006f);
            ArtKit.Beam(body, "Roof front crossbar", V(-.59f, 1.48f, .17f), V(.59f, 1.48f, .17f), .115f, "Steel");
            ArtKit.Beam(body, "Rear crossbrace", V(-.61f, .49f, -.91f), V(.59f, 1.43f, -.74f), .085f, "Steel");
            ArtKit.Beam(body, "Rear crossbrace opposite", V(.61f, .49f, -.91f), V(-.59f, 1.43f, -.74f), .085f, "Steel");
            ArtKit.Box(body, "Heavy toothed ram", V(0, .31f, 1.76f), V(1.88f, .24f, .25f), "Steel", .04f);
            for (int j = -1; j <= 1; j++) AddSpike(body, "Front welded ram tooth", V(j * .59f, .32f, 1.995f), V(.18f, .34f, .19f), Quaternion.Euler(90, 0, 0), j == 0 ? "IvoryShade" : "Rust");
            ArtKit.Box(body, "Rear exposed engine block", V(0, .59f, -1.21f), V(.85f, .42f, .62f), "Steel", .055f);
            for (int j = 0; j < 5; j++) ArtKit.Box(body, "Engine broad cooling fin", V(-.31f + j * .155f, .83f, -1.21f), V(.07f, .075f, .53f), "SteelLight", .013f);
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
            ArtKit.Ellipsoid(parent, "Driver steel helmet", V(-.32f, 1.37f, -.16f), V(.35f, .18f, .32f), "Steel", 12, 8);
            ArtKit.Box(parent, "Driver red scarf", V(-.32f, 1.17f, -.02f), V(.32f, .14f, .12f), "Scarf", .035f);
            ArtKit.Box(parent, "Driver dark goggles", V(-.32f, 1.285f, .003f), V(.28f, .09f, .06f), "Rubber", .022f);
            ArtKit.Beam(parent, "Driver left forearm", V(-.51f, 1.01f, -.20f), V(-.48f, .91f, .20f), .115f, "Skin");
            ArtKit.Beam(parent, "Driver right forearm", V(-.13f, 1.01f, -.20f), V(-.16f, .91f, .20f), .115f, "Skin");
            ArtKit.Torus(parent, "Driver steering wheel", V(-.32f, .93f, .23f), .18f, .133f, .036f, "Rubber", Quaternion.Euler(65, 0, 0), 16);
        }

        static void BuildWheel(Transform parent, float radius, int side)
        {
            float s = radius / .46f;
            ArtKit.Torus(parent, "Rounded chunky offroad tire", Vector3.zero, radius * .96f, radius * .46f, .465f * s, "Rubber", AcrossAxle, 28);
            ArtKit.Cylinder(parent, "Dark recessed rim", V(side * .205f * s, 0, 0), radius * .51f, .04f * s, "Steel", AcrossAxle, 16);
            ArtKit.Cylinder(parent, "Rusted recessed wheel dish", V(side * .229f * s, 0, 0), radius * .365f, .026f * s, "Rust", AcrossAxle, 16);
            ArtKit.Cylinder(parent, "Heavy exposed wheel hub", V(side * .25f * s, 0, 0), radius * .145f, .05f * s, "SteelLight", AcrossAxle, 12);
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
                ArtKit.Cylinder(parent, "Five strong hub bolts", V(side * .258f * s, Mathf.Cos(a) * .118f * s, Mathf.Sin(a) * .118f * s), .025f * s, .026f * s, "SteelLight", AcrossAxle, 6);
            }
        }

        static void RestyleBuggyGun(BuggyGun gun)
        {
            if (!gun.yaw || !gun.pitch || !gun.muzzle) return;
            var mount = ArtKit.Node("Concept gun base skin", gun.yaw.parent).transform;
            ArtKit.Box(mount, "Armored roof gun mounting plate", Vector3.zero, V(.61f, .11f, .48f), "RustDark", .03f);
            BakeAtlas(mount, "Buggy_GunMount");
            var yaw = ArtKit.Node("Concept rotating bearing skin", gun.yaw).transform;
            ArtKit.Cylinder(yaw, "Steel rotating gun bearing", V(0, .125f, 0), .215f, .085f, "Steel", null, 16);
            ArtKit.Box(yaw, "Elevation armored support", V(0, .19f, -.04f), V(.32f, .15f, .31f), "Rust", .025f);
            BakeAtlas(yaw, "Buggy_GunYaw");
            var pitch = ArtKit.Node("Concept machine gun skin", gun.pitch).transform;
            ArtKit.Box(pitch, "Readable hostile gun receiver", V(0, 0, -.005f), V(.28f, .245f, .55f), "Steel", .035f);
            ArtKit.Box(pitch, "Receiver upper charging housing", V(0, .143f, -.08f), V(.20f, .07f, .29f), "SteelLight", .018f);
            ArtKit.Box(pitch, "Machine gun ammunition box", V(-.27f, -.045f, -.11f), V(.29f, .25f, .30f), "Rust", .035f);
            ArtKit.Box(pitch, "Hostile gun ivory identification", V(.151f, .005f, -.10f), V(.015f, .13f, .13f), "IvoryShade", .007f);
            float end = gun.muzzle.localPosition.z;
            float start = .24f;
            ArtKit.Cylinder(pitch, "Separate heavy machine gun barrel", V(0, 0, (start + end - .10f) * .5f), .047f, end - .10f - start, "Steel", AlongBarrel, 12);
            for (int j = 0; j < 3; j++) ArtKit.Cylinder(pitch, "Barrel cooling collar", V(0, 0, .34f + j * .12f), .063f, .055f, "SteelLight", AlongBarrel, 12);
            ArtKit.Box(pitch, "Heavy muzzle brake", V(0, 0, end - .09f), V(.155f, .145f, .17f), "Steel", .023f);
            ArtKit.Box(pitch, "Dark muzzle opening", V(0, 0, end - .001f), V(.080f, .070f, .006f), "Rubber", .008f);
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
            part.AddComponent<MeshRenderer>().sharedMaterials = new[] { ArtKit.Mat("SteelLight"), ArtKit.Mat("Steel"), ArtKit.Mat("Rust") };
            BakeAtlas(gearView, "Gear_SalvagedCog");
            ArtKit.Box(scrapView, "Curved salvaged armor plate", V(0, -.005f, -.03f), V(.53f, .085f, .34f), "Rust", .035f, Quaternion.Euler(-9, -13, 10));
            ArtKit.Box(scrapView, "Worn ivory paint on armor", V(-.08f, .046f, -.03f), V(.23f, .014f, .28f), "IvoryShade", .007f, Quaternion.Euler(-9, -13, 10));
            ArtKit.Box(scrapView, "Heavy salvaged bracket", V(.13f, .075f, .075f), V(.12f, .12f, .47f), "Steel", .025f, Quaternion.Euler(0, 32, -8));
            ArtKit.Cylinder(scrapView, "Short salvaged machine shaft", V(-.17f, .10f, .08f), .069f, .40f, "SteelLight", Quaternion.Euler(65, 0, -25), 10);
            ArtKit.Cylinder(scrapView, "Large useful hexagonal fastener", V(.09f, .16f, -.10f), .112f, .074f, "SteelLight", Quaternion.Euler(12, 20, 0), 6);
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
            ArtKit.Cylinder(t, "Jerrycan fuel cap", V(.14f, .32f, 0), .056f, .045f, "Steel", null, 10);
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
            return OrganicLoft("Tailored rounded jacket",new[]{.88f,.99f,1.15f,1.32f,1.40f},new[]{.49f*broad,.57f*broad,.66f*broad,.70f*broad,.60f*broad},new[]{.375f,.425f,.46f,.43f,.35f},new[]{0f,0f,.002f,.005f,.008f},18,.79f);
        }

        // Cross sections are rounded superellipses, with several intentional stations for
        // anatomical taper. This avoids the rectangular plank silhouettes of simple boxes.
        static Mesh OrganicLoft(string name,float[] levels,float[] widths,float[] depths,float[] zCenters,int segments=16,float power=.8f)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int row=0;row<levels.Length;row++)for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,c=Mathf.Cos(a),s=Mathf.Sin(a);
                vertices.Add(V(Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),power)*widths[row]*.5f,levels[row],Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),power)*depths[row]*.5f+zCenters[row]));
            }
            for(int row=0;row<levels.Length-1;row++)for(int i=0;i<segments;i++)
            {
                int a=row*segments+i,b=row*segments+(i+1)%segments,c=a+segments,d=b+segments;
                triangles.AddRange(new[]{a,c,b,b,c,d});
            }
            int bottom=vertices.Count;vertices.Add(V(0,levels[0],zCenters[0]));
            int top=vertices.Count;int last=(levels.Length-1)*segments;vertices.Add(V(0,levels[levels.Length-1],zCenters[zCenters.Length-1]));
            for(int i=0;i<segments;i++){int next=(i+1)%segments;triangles.AddRange(new[]{bottom,i,next,top,last+next,last+i});}
            return MeshFrom(name,vertices,triangles);
        }

        // A broad shaped plate with softened outline corners, true beveled perimeter and
        // a shallow convex front. Outline, rather than surface noise, carries the detail.
        static Mesh SculptedPlate(float width,float height,float depth)
        {
            Vector2[] corners={new Vector2(-.34f,-.5f),new Vector2(.34f,-.5f),new Vector2(.44f,-.28f),new Vector2(.5f,.28f),new Vector2(.35f,.5f),new Vector2(-.35f,.5f),new Vector2(-.5f,.28f),new Vector2(-.44f,-.28f)};
            var outline=new List<Vector2>();
            for(int i=0;i<corners.Length;i++)
            {
                var corner=corners[i];var from=Vector2.Lerp(corner,corners[(i+corners.Length-1)%corners.Length],.20f);var to=Vector2.Lerp(corner,corners[(i+1)%corners.Length],.20f);
                for(int j=0;j<3;j++){float t=j*.5f;outline.Add((1-t)*(1-t)*from+2*(1-t)*t*corner+t*t*to);}
            }
            int n=outline.Count;var vertices=new List<Vector3>();var triangles=new List<int>();
            float bevel=Mathf.Min(depth*.28f,.025f);float[] z={-depth*.5f,-depth*.5f+bevel,depth*.5f-bevel,depth*.5f};float[] scale={.91f,1f,1f,.875f};
            for(int row=0;row<4;row++)foreach(var p in outline)vertices.Add(V(p.x*width*scale[row],p.y*height*scale[row],z[row]));
            for(int row=0;row<3;row++)for(int i=0;i<n;i++)
            {int a=row*n+i,b=row*n+(i+1)%n,c=a+n,d=b+n;triangles.AddRange(new[]{a,b,c,b,d,c});}
            int back=vertices.Count;vertices.Add(V(0,0,-depth*.5f));int front=vertices.Count;vertices.Add(V(0,0,depth*.5f+Mathf.Min(depth*.16f,.018f)));
            for(int i=0;i<n;i++){int next=(i+1)%n;triangles.AddRange(new[]{back,next,i,front,n*3+i,n*3+next});}
            return MeshFrom("Shaped curved salvage armor",vertices,triangles);
        }

        static Mesh ScarfPoint()
        {
            var v = new List<Vector3> { V(-.5f,.5f,-.5f),V(.5f,.5f,-.5f),V(.08f,-.5f,-.3f),V(-.5f,.5f,.5f),V(.5f,.5f,.5f),V(.08f,-.5f,.25f) };
            var triangles=new List<int>{0,2,1,3,4,5,0,1,4,0,4,3,1,2,5,1,5,4,2,0,3,2,3,5};
            for(int i=0;i<triangles.Count;i+=3){int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;}
            return MeshFrom("Scarf folded point",v,triangles);
        }
        static void AddSpike(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation, string material)
        {
            if (!spike)
            {
                var v = new List<Vector3>(); var tr = new List<int>();
                const int n = 8;
                for (int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;int s=v.Count;
                    v.Add(V(Mathf.Cos(a)*.5f,-.5f,Mathf.Sin(a)*.5f));v.Add(V(0,.5f,0));v.Add(V(Mathf.Cos(b)*.5f,-.5f,Mathf.Sin(b)*.5f));
                    tr.AddRange(new[]{s,s+1,s+2});s=v.Count;
                    v.Add(Vector3.down*.5f);v.Add(V(Mathf.Cos(a)*.5f,-.5f,Mathf.Sin(a)*.5f));v.Add(V(Mathf.Cos(b)*.5f,-.5f,Mathf.Sin(b)*.5f));tr.AddRange(new[]{s,s+1,s+2});
                }
                spike=ArtKit.SaveMesh(MeshFrom("Welded scrap spike",v,tr),"Raider_Spike");
            }
            ArtKit.Part(parent,name,spike,position,size,material,rotation);
        }
        static Mesh FenderMesh(float inner, float outer, float width)
        {
            var v=new List<Vector3>();var tr=new List<int>();const int n=14;
            for(int s=-1;s<=1;s+=2)for(int row=0;row<2;row++)for(int j=0;j<=n;j++)
            {float a=(12+j*156f/n)*Mathf.Deg2Rad,r=row==0?inner:outer;v.Add(V(s*width*.5f,Mathf.Sin(a)*r,Mathf.Cos(a)*r));}
            int stride=n+1;
            for(int side=0;side<2;side++)for(int j=0;j<n;j++)
            {int a=side*stride*2+j,b=a+stride;if(side==0)tr.AddRange(new[]{a,a+1,b,a+1,b+1,b});else tr.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
            for(int row=0;row<2;row++)for(int j=0;j<n;j++)
            {int a=row*stride+j,b=a+stride*2;if(row==0)tr.AddRange(new[]{a,b,a+1,a+1,b,b+1});else tr.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
            tr.AddRange(new[]{0,stride,stride*2,stride,stride*3,stride*2,n,stride*2+n,stride+n,stride+n,stride*2+n,stride*3+n});
            for(int i=0;i<tr.Count;i+=3){int swap=tr[i+1];tr[i+1]=tr[i+2];tr[i+2]=swap;}
            return MeshFrom("Formed armored fender",v,tr);
        }
        static Mesh GearMesh()
        {
            const int n=64;var v=new List<Vector3>();var face=new List<int>();var edge=new List<int>();var rust=new List<int>();
            Vector3 Point(int ring,int i)
            {
                float outer=i%4==1||i%4==2?.44f:.365f;
                float r=ring==0||ring==5?.155f:ring==1||ring==4?outer-.018f:outer;
                float y=ring==0||ring==1?.055f:ring==2?.030f:ring==3?-.030f:-.055f;
                float a=i*Mathf.PI*2/n;return V(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
            }
            for(int ring=0;ring<6;ring++)for(int i=0;i<n;i++)
            {
                int s=v.Count;v.Add(Point(ring,i));v.Add(Point(ring,(i+1)%n));v.Add(Point((ring+1)%6,(i+1)%n));v.Add(Point((ring+1)%6,i));
                var indices=i/4==2||i/4==7||i/4==11?rust:ring==0||ring==4?face:edge;
                indices.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});
            }
            var m=new Mesh{name="Useful salvaged cog"};m.SetVertices(v);m.subMeshCount=3;m.SetTriangles(face,0);m.SetTriangles(edge,1);m.SetTriangles(rust,2);m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        static Mesh MeshFrom(string name,List<Vector3> vertices,List<int> triangles)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        // A palette atlas reduces each animated subgroup to one material and one renderer.
        // Metallic/smoothness data is sampled per material, so rubber stays matte and steel catches light.
        public static void RefreshAtlas() { atlas = null; EnsureAtlas(); }
        static void EnsureAtlas()
        {
            if(atlas)return;
            ArtKit.Ensure();
            const int cell=8,width=64,height=32;
            var color=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
            var response=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            for(int index=0;index<32;index++)
            {
                var source=ArtKit.Mat(Palette[Math.Min(index,Palette.Length-1)]);
                Color c=source.GetColor("_BaseColor");
                Color p=new Color(source.GetFloat("_Metallic"),0,0,source.GetFloat("_Smoothness"));
                for(int y=0;y<cell;y++)for(int x=0;x<cell;x++)
                {int px=(index%8)*cell+x,py=(index/8)*cell+y;color.SetPixel(px,py,c);response.SetPixel(px,py,p);}
            }
            color.Apply();response.Apply();
            string colorPath=ArtKit.Root+"/Textures/Enemy shared palette.png",responsePath=ArtKit.Root+"/Textures/Enemy shared surface response.png";
            File.WriteAllBytes(colorPath,color.EncodeToPNG());File.WriteAllBytes(responsePath,response.EncodeToPNG());Object.DestroyImmediate(color);Object.DestroyImmediate(response);
            AssetDatabase.ImportAsset(colorPath,ImportAssetOptions.ForceUpdate);AssetDatabase.ImportAsset(responsePath,ImportAssetOptions.ForceUpdate);
            ConfigureAtlasTexture(colorPath,true);ConfigureAtlasTexture(responsePath,false);
            string path=ArtKit.Root+"/Materials/Enemy shared palette.mat";
            atlas=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!atlas){atlas=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Enemy shared palette"};AssetDatabase.CreateAsset(atlas,path);}
            atlas.SetColor("_BaseColor",Color.white);atlas.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath));atlas.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(responsePath));
            atlas.SetFloat("_Metallic",1);atlas.SetFloat("_Smoothness",1);atlas.SetFloat("_WorkflowMode",1);atlas.SetFloat("_SmoothnessTextureChannel",0);atlas.SetFloat("_SpecularHighlights",1);atlas.EnableKeyword("_METALLICSPECGLOSSMAP");atlas.enableInstancing=true;EditorUtility.SetDirty(atlas);
        }
        static void ConfigureAtlasTexture(string path,bool srgb)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=srgb;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.SaveAndReimport();
        }
        static int PaletteIndex(Material mat)
        {
            for(int i=0;i<Palette.Length;i++)if(mat==ArtKit.Mat(Palette[i]))return i;
            return Array.IndexOf(Palette,"Steel");
        }
        public static void BakeAtlas(Transform root,string name)
        {
            EnsureAtlas();
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
            var filters=root.GetComponentsInChildren<MeshFilter>(true).Where(x=>x.sharedMesh).ToArray();
            foreach(var filter in filters)
            {
                var renderer=filter.GetComponent<MeshRenderer>();if(!renderer||renderer.sharedMaterials.Length==0)continue;
                var mesh=filter.sharedMesh;var points=mesh.vertices;var ns=mesh.normals;var sourceUvs=mesh.uv;
                Matrix4x4 matrix=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;Matrix4x4 normalMatrix=matrix.inverse.transpose;
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var material=renderer.sharedMaterials[Math.Min(sub,renderer.sharedMaterials.Length-1)];int colorIndex=PaletteIndex(material);
                    Vector2 uv=new Vector2((colorIndex%8+.5f)/8f,(colorIndex/8+.5f)/4f);
                    var source=mesh.GetTriangles(sub);var remap=new Dictionary<int,int>();
                    foreach(int index in source)
                    {
                        if(!remap.TryGetValue(index,out int dest))
                        {dest=vertices.Count;remap[index]=dest;vertices.Add(matrix.MultiplyPoint3x4(points[index]));normals.Add(ns.Length==points.Length?normalMatrix.MultiplyVector(ns[index]).normalized:Vector3.up);uvs.Add(material==atlas&&sourceUvs.Length==points.Length?sourceUvs[index]:uv);}
                        triangles.Add(dest);
                    }
                }
            }
            var result=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};result.SetVertices(vertices);result.SetNormals(normals);result.SetUVs(0,uvs);result.SetTriangles(triangles,0);result.RecalculateBounds();
            result=ArtKit.SaveMesh(result,name);
            foreach(Transform child in root.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
            var oldRenderer=root.GetComponent<MeshRenderer>();if(oldRenderer)Object.DestroyImmediate(oldRenderer);
            var oldFilter=root.GetComponent<MeshFilter>();if(oldFilter)Object.DestroyImmediate(oldFilter);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh=result;
            var finalRenderer=root.gameObject.AddComponent<MeshRenderer>();finalRenderer.sharedMaterial=atlas;finalRenderer.shadowCastingMode=ShadowCastingMode.On;finalRenderer.receiveShadows=true;
        }
    }
}
