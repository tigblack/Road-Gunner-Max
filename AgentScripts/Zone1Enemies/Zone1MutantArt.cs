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
        const string Bone = "IvoryShade";

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
            var headForms=ArtKit.Node("Head sculpted anatomical forms",head).transform;
            foreach(Transform form in head.Cast<Transform>().ToArray())if(form!=headForms)form.SetParent(headForms,false);
            headForms.localScale=V(1,1,.88f);
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
            Part(t,"High wedged scapular hump",LoftY("Wedged shoulder hump",new[]{.25f,.48f,.72f,.91f,1.025f},new[]{1.28f,1.24f,.97f,.55f,.17f},new[]{.72f,.79f,.70f,.46f,.16f},new[]{-.29f,-.36f,-.40f,-.39f,-.33f},16,.64f),Flesh);
            SweepPart(t,"Forward inclined powerful neck",new[]{V(0,.43f,.16f),V(0,.66f,.25f),V(0,.79f,.35f)},new[]{.67f,.63f,.49f},new[]{.63f,.59f,.44f},Flesh,16,.72f);
            Ellipsoid(t, "Heavy lower ribcage", V(0, -.17f, .12f), V(.97f, .86f, .75f), Flesh, 18, 12);

            // These are broad anatomical color planes, rather than a layer of noisy detail.
            Part(t,"Broad angular pectoral mantle",LoftY("Pectoral muscle planes",new[]{-.045f,.13f,.31f,.405f},new[]{.93f,1.27f,1.23f,.85f},new[]{.17f,.255f,.18f,.07f},new[]{.445f,.425f,.372f,.32f},16,.65f),WarmFlesh);
            Part(t,"Tapered abdominal muscle plane",LoftY("Flat abdominal planes",new[]{-.49f,-.32f,-.11f,.075f},new[]{.53f,.73f,.80f,.73f},new[]{.13f,.20f,.215f,.12f},new[]{.31f,.38f,.425f,.412f},14,.69f),WarmFlesh);
            SweepPart(t,"Shallow sternum division",new[]{V(0,.33f,.461f),V(0,.18f,.558f),V(.015f,-.01f,.527f)},new[]{.038f,.045f,.03f},new[]{.018f,.018f,.015f},Flesh,8,.7f);
            Ellipsoid(t, "Left oblique muscle", V(-.42f, -.18f, .24f), V(.32f, .57f, .38f), Flesh, 14, 10, Quaternion.Euler(0, 0, -12));
            Ellipsoid(t, "Right oblique muscle", V(.43f, -.18f, .24f), V(.35f, .57f, .38f), Flesh, 14, 10, Quaternion.Euler(0, 0, 12));

            // A deliberate short dorsal crest makes the back identifiable in portrait play.
            for (int i = 0; i < 5; i++)
            {
                float y = .19f + i * .145f;
                float z = -.57f - Mathf.Sin(i * .48f) * .095f;
                Ellipsoid(t, "Dorsal bony knot " + i, V(i % 2 == 0 ? -.018f : .018f, y, z), V(.18f - i * .009f, .20f, .12f), Bone, 10, 7, Quaternion.Euler(-25, 0, 0));
            }
            for (int side = -1; side <= 1; side += 2)
            {
                SweepPart(t,"Angular scapular ridge",new[]{V(side*.31f,.66f,-.61f),V(side*.54f,.40f,-.58f),V(side*.57f,.17f,-.48f)},new[]{.17f,.26f,.09f},new[]{.09f,.14f,.07f},Flesh,12,.65f);
                SweepPart(t,"Strong collar muscle ridge",new[]{V(side*.10f,.53f,.34f),V(side*.36f,.48f,.38f),V(side*.59f,.39f,.27f)},new[]{.12f,.15f,.11f},new[]{.10f,.13f,.09f},WarmFlesh,12,.72f);
            }

            // One salvaged restraint crosses the torso. The body remains mostly organic.
            SweepPart(t, "Broad diagonal salvaged leather harness",
                new[] { V(-.58f, .62f, .20f), V(-.38f, .32f, .51f), V(-.07f, -.01f, .54f), V(.26f, -.35f, .42f), V(.40f, -.45f, .24f) },
                new[] { .12f, .13f, .13f, .13f, .12f }, new[] { .04f, .04f, .04f, .04f, .04f }, "RustDark", 10, .69f);
            Part(t, "Dull harness adjustment clasp", Plate(.145f, .105f, .04f), "SteelLight", V(-.07f, -.015f, .57f), Quaternion.Euler(0, 0, -42));
            var belt = ArtKit.Torus(t, "Old heavy waist restraint", V(0, -.44f, .03f), .515f, .471f, .115f, "RustDark", null, 24);
            belt.localScale = V(1, 1, .74f);
            Part(t, "Salvaged belt buckle", Plate(.19f, .145f, .05f), "Steel", V(.05f, -.44f, .423f));
            Part(t, "Torn red brown waist cloth", TornCloth(), "Scarf");
            // Two large folded planes enrich the cloth without turning it into a tassel fringe.
            SweepPart(t, "Broad hanging front cloth fold", new[] { V(-.13f, -.47f, .354f), V(-.11f, -.67f, .372f), V(-.07f, -.91f, .325f) }, new[] { .11f, .105f, .035f }, new[] { .035f, .035f, .025f }, "RustDark", 8, .8f);
            SweepPart(t, "Worn cloth side fold", new[] { V(.38f, -.47f, .19f), V(.36f, -.64f, .245f), V(.32f, -.80f, .21f) }, new[] { .10f, .09f, .025f }, new[] { .025f, .03f, .02f }, "Rust", 8, .8f);
        }

        static void Head(Transform t)
        {
            // A long low wedge, rather than a spherical mammalian head. The snout and
            // lower mandible are separate volumes with a visible dark mouth between them.
            Part(t,"Long wedge shaped reptilian skull",LoftZ("Reptilian skull planes",new[]{-.285f,-.17f,.055f,.25f,.44f,.635f},new[]{.20f,.50f,.635f,.515f,.355f,.255f},new[]{.29f,.55f,.585f,.42f,.225f,.145f},new[]{.125f,.105f,.072f,.015f,-.035f,-.065f},18,.63f),Flesh);
            Part(t,"Separate jutting lower mandible",LoftZ("Underbiting lower jaw",new[]{.045f,.22f,.44f,.64f},new[]{.44f,.505f,.405f,.235f},new[]{.225f,.235f,.16f,.095f},new[]{-.225f,-.255f,-.26f,-.237f},16,.66f),Flesh);
            Part(t,"Visible shadow inside the open maw",LoftZ("Open maw shadow",new[]{.18f,.39f,.615f},new[]{.435f,.36f,.215f},new[]{.09f,.075f,.055f},new[]{-.166f,-.171f,-.163f},14,.68f),"Rubber");
            Part(t,"Angular chin underside",LoftZ("Chin keel",new[]{.18f,.40f,.58f},new[]{.31f,.29f,.16f},new[]{.11f,.13f,.075f},new[]{-.34f,-.33f,-.285f},12,.69f),WarmFlesh);
            for(int side=-1;side<=1;side+=2)
            {
                // The inner brow sits lower than its outer end: an angry, predatory scowl.
                SweepPart(t,"Heavy slanted scowling brow",new[]{V(side*.075f,.125f,.34f),V(side*.185f,.205f,.323f),V(side*.303f,.243f,.205f)},new[]{.125f,.165f,.115f},new[]{.12f,.145f,.105f},Flesh,12,.63f);
                Part(t,"Narrow angular eye socket",Plate(.183f,.059f,.026f),"Rubber",V(side*.215f,.13f,.316f),Quaternion.Euler(0,side*19,side*16));
                Ellipsoid(t,"Narrow watchful amber eye",V(side*.213f,.132f,.337f),V(.077f,.022f,.015f),"Yellow",12,6,Quaternion.Euler(0,side*19,side*16));
                Ellipsoid(t,"Slit pupil",V(side*.213f,.132f,.346f),V(.012f,.021f,.006f),"Rubber",8,5);
                SweepPart(t,"Angular cheek and jaw hinge",new[]{V(side*.28f,.10f,.175f),V(side*.278f,-.071f,.32f),V(side*.19f,-.17f,.447f)},new[]{.195f,.18f,.10f},new[]{.13f,.115f,.07f},Flesh,12,.62f);
                SweepPart(t,"Recessed diagonal cheek crease",new[]{V(side*.292f,.01f,.269f),V(side*.25f,-.105f,.383f),V(side*.185f,-.151f,.46f)},new[]{.027f,.026f,.018f},new[]{.016f,.015f,.01f},"RustDark",8);
                // A flat slitted nostril at the snout tip avoids a round bear/pig nose.
                Part(t,"Predatory nostril slit",Plate(.064f,.021f,.007f),"Rubber",V(side*.074f,-.032f,.642f),Quaternion.Euler(0,0,side*12));
                SweepPart(t,"Subtle swept ear fin",new[]{V(side*.30f,.09f,-.09f),V(side*.385f,.16f,-.20f),V(side*.42f,.13f,-.29f)},new[]{.16f,.13f,.006f},new[]{.105f,.08f,.005f},Flesh,12,.75f);
                SweepPart(t,"Horn root ridge",new[]{V(side*.235f,.245f,-.08f),V(side*.275f,.335f,-.12f),V(side*.315f,.355f,-.17f)},new[]{.24f,.20f,.14f},new[]{.19f,.18f,.12f},WarmFlesh,12,.72f);
                SweepPart(t,"Dark lower mouth corner",new[]{V(side*.235f,-.165f,.25f),V(side*.194f,-.16f,.46f),V(side*.12f,-.158f,.625f)},new[]{.024f,.027f,.018f},new[]{.016f,.016f,.012f},"Rubber",8);
            }
            SweepPart(t,"Long swept backward temple horn",new[]{V(-.267f,.29f,-.09f),V(-.42f,.535f,-.255f),V(-.53f,.69f,-.51f),V(-.435f,.675f,-.80f)},new[]{.205f,.16f,.09f,.004f},new[]{.18f,.14f,.08f,.004f},Bone,14,.82f);
            SweepPart(t,"Short fractured right temple horn",new[]{V(.267f,.285f,-.09f),V(.38f,.425f,-.245f),V(.395f,.392f,-.435f)},new[]{.195f,.14f,.074f},new[]{.18f,.125f,.067f},Bone,14,.82f);
            SweepPart(t,"Central plated cranial ridge",new[]{V(0,.425f,-.095f),V(-.012f,.347f,.10f),V(.004f,.212f,.29f)},new[]{.10f,.11f,.055f},new[]{.07f,.075f,.035f},Bone,10,.65f);
            for(int side=-1;side<=1;side+=2)
            {
                SweepPart(t,"Long curved lower jaw fang",new[]{V(side*.215f,-.25f,.36f),V(side*.282f,-.213f,.495f),V(side*.31f,-.08f,.62f),V(side*.278f,.024f,.64f)},new[]{.103f,.092f,.052f,.003f},new[]{.09f,.082f,.048f,.003f},Bone,14,.9f);
                for(int i=0;i<3;i++)
                {
                    float z=.30f+i*.105f,x=side*(.22f-i*.035f);
                    SweepPart(t,"Small exposed upper tooth "+i,new[]{V(x,-.11f,z),V(x*.98f,-.185f,z+.009f)},new[]{.044f,.003f},new[]{.041f,.003f},Bone,8);
                }
            }
        }

        static void Arm(Transform t, int side, bool giant)
        {
            float s = giant ? 1.22f : 1;
            Vector3[] points = giant
                ? new[] { V(0,.04f,0),V(.16f,-.25f,.01f),V(.24f,-.51f,.07f),V(.25f,-.65f,.11f),V(.27f,-.88f,.21f),V(.20f,-1.12f,.30f),V(.18f,-1.24f,.32f) }
                : new[] { V(0,.04f,0),V(-.12f,-.22f,.01f),V(-.18f,-.42f,.05f),V(-.18f,-.52f,.08f),V(-.16f,-.72f,.17f),V(-.08f,-.90f,.25f),V(-.05f,-1.01f,.28f) };
            SweepPart(t,"Angular bicep elbow and forearm anatomy",points,new[]{.55f*s,.62f*s,.455f*s,.335f*s,.48f*s,.37f*s,.295f*s},new[]{.585f*s,.59f*s,.425f*s,.345f*s,.445f*s,.35f*s,.285f*s},Flesh,16,.67f);
            SweepPart(t,"Shaped powerful deltoid",new[]{V(0,.17f,-.03f),V(side*.10f,-.03f,-.005f),V(side*.16f,-.31f,.055f)},new[]{.48f*s,.65f*s,.44f*s},new[]{.48f*s,.63f*s,.42f*s},Flesh,14,.64f);
            SweepPart(t,"Raised angular bicep plane",new[]{V(side*.08f,-.075f,.16f),V(side*.16f,-.25f,.22f),V(side*.20f,-.43f,.15f)},new[]{.22f*s,.37f*s,.15f*s},new[]{.10f*s,.18f*s,.07f*s},Flesh,12,.65f);
            SweepPart(t,"Forearm extensor muscle plane",new[]{points[3]+V(side*.055f,-.035f,.11f),points[4]+V(side*.035f,.015f,.19f),points[5]+V(side*.012f,.015f,.13f)},new[]{.15f*s,.27f*s,.12f*s},new[]{.07f*s,.13f*s,.06f*s},Flesh,12,.65f);
            Ellipsoid(t,"Prominent angular elbow knot",points[3]+V(side*.065f,.01f,-.075f),V(.25f*s,.23f*s,.25f*s),Bone,10,7);
            Vector3 palm = points[points.Length-1];
            Ellipsoid(t, "Broad powerful claw palm", palm + V(0, -.045f, .025f), V((giant?.53f:.37f), (giant?.47f:.37f), (giant?.43f:.32f)), Flesh, 18, 12, Quaternion.Euler(-8, 0, side * -6));

            int fingers = 3;
            for (int i=0; i<fingers; i++)
            {
                float x=(i-1)*(giant?.17f:.125f);
                float stagger=i==1?.045f:0;
                Vector3 a=palm+V(x,-(giant?.17f:.13f),.09f);
                Vector3 b=a+V(x*.04f,-(giant?.22f:.17f)-stagger,.06f);
                Vector3 c=b+V(-x*.10f,-.065f,.12f);
                SweepPart(t,"Separate thick claw finger "+i,new[]{a,b,c},new[]{giant?.155f:.115f,giant?.13f:.095f,.082f},new[]{giant?.15f:.11f,giant?.12f:.09f,.075f},Flesh,12);
                SweepPart(t,"Curved keratin claw "+i,new[]{c,c+V(0,-.065f,.12f),c+V(-x*.08f,-.025f,.235f)},new[]{.083f,.061f,.003f},new[]{.075f,.055f,.003f},Bone,12);
                Ellipsoid(t,"Finger knuckle "+i,a+V(0,.02f,-.04f),V(.13f,.13f,.11f),WarmFlesh,10,7);
            }
            Vector3 thumb = palm + V(-side * (giant?.23f:.17f), .04f, .10f);
            SweepPart(t,"Opposable heavy claw thumb",new[]{thumb,thumb+V(-side*.11f,-.13f,.10f),thumb+V(-side*.07f,-.25f,.16f)},new[]{giant?.19f:.14f,giant?.15f:.11f,.09f},new[]{giant?.18f:.13f,giant?.14f:.10f,.08f},Flesh,12);
            SweepPart(t,"Hooked thumb talon",new[]{thumb+V(-side*.07f,-.25f,.16f),thumb+V(side*.005f,-.30f,.26f),thumb+V(side*.075f,-.255f,.31f)},new[]{.09f,.06f,.003f},new[]{.08f,.05f,.003f},Bone,12);

            if (!giant)
            {
                // An actual salvaged, curved plough plate, not a scaled raider shoulder pad.
                Part(t,"Scavenged asymmetric shoulder guard",Plate(.73f,.48f,.13f),"Steel",V(-.08f,-.045f,.275f),Quaternion.Euler(-20,-12,18));
                Part(t,"Broad oxidized shoulder corner",Plate(.27f,.15f,.012f),"Rust",V(-.22f,.025f,.355f),Quaternion.Euler(-20,-12,18));
                Part(t,"Shoulder exposed metal edge",Plate(.18f,.045f,.012f),"SteelLight",V(.02f,.13f,.33f),Quaternion.Euler(-20,-12,18));
                var strap=ArtKit.Torus(t,"Leather shoulder restraint",V(-.095f,-.25f,.025f),.315f,.28f,.11f,"RustDark",Quaternion.Euler(0,0,-13),20);strap.localScale=V(1,1,.93f);
                Part(t,"Forearm short salvaged guard",Plate(.32f,.37f,.095f),"Steel",V(-.13f,-.73f,.397f),Quaternion.Euler(-8,0,-12));
                Part(t,"Forearm broad worn stripe",Plate(.12f,.23f,.012f),"Rust",V(-.14f,-.73f,.452f),Quaternion.Euler(-8,0,-12));
            }
            else
            {
                // The giant unarmored right arm is the miniboss's dominant silhouette.
                for(int i=0;i<3;i++)
                {
                    var p=V(.27f+i*.03f,-.46f-i*.19f,-.075f+i*.04f);
                    Ellipsoid(t,"Large forearm bony knot "+i,p,V(.24f-i*.025f,.22f,.20f),Bone,12,8,Quaternion.Euler(-15,0,15));
                }
                Ellipsoid(t,"Outer deltoid bony crest",V(.265f,.105f,-.025f),V(.27f,.21f,.31f),Bone,14,10,Quaternion.Euler(0,0,-20));
            }
        }

        static void Leg(Transform t, int side)
        {
            SweepPart(t,"Heavy bowed muscular thigh",new[]{V(0,.04f,0),V(side*.07f,-.26f,.025f),V(side*.10f,-.53f,.07f)},new[]{.51f,.54f,.40f},new[]{.53f,.52f,.38f},Flesh,18,.94f);
            Ellipsoid(t,"Heavy outer thigh muscle",V(side*.08f,-.24f,.13f),V(.39f,.52f,.32f),WarmFlesh,16,12,Quaternion.Euler(0,0,side*12));
            SweepPart(t,"Bent weight bearing shin",new[]{V(side*.10f,-.51f,.06f),V(side*.08f,-.74f,.10f),V(side*.045f,-.98f,.14f)},new[]{.38f,.33f,.255f},new[]{.39f,.32f,.26f},Flesh,16,.91f);
            Ellipsoid(t,"Bony broad knee shield",V(side*.105f,-.53f,.245f),V(.32f,.29f,.13f),Bone,14,10,Quaternion.Euler(-10,0,side*8));
            Ellipsoid(t,"Rounded heavy heel and planted foot",V(side*.04f,-1.098f,.205f),V(.45f,.235f,.65f),Flesh,20,12);
            Ellipsoid(t,"Dust worn upper foot plane",V(side*.04f,-1.056f,.35f),V(.38f,.14f,.40f),WarmFlesh,16,10);
            for(int i=0;i<3;i++)
            {
                float x=(i-1)*.143f+side*.04f;
                var start=V(x,-1.10f,.407f);
                SweepPart(t,"Separate thick planted toe "+i,new[]{start,start+V(0,-.015f,.12f),start+V((i-1)*.012f,-.025f,.215f)},new[]{.165f,.155f,.11f},new[]{.15f,.14f,.09f},Flesh,12);
                SweepPart(t,"Short weight bearing toe claw "+i,new[]{start+V((i-1)*.012f,-.025f,.205f),start+V((i-1)*.017f,-.028f,.295f),start+V((i-1)*.017f,-.005f,.365f)},new[]{.113f,.085f,.004f},new[]{.085f,.06f,.004f},Bone,12);
            }
            if(side<0)
            {
                Part(t,"Old knee salvaged protection",Plate(.285f,.255f,.07f),"Steel",V(-.11f,-.52f,.313f),Quaternion.Euler(-10,0,-8));
                Part(t,"Knee restrained rust patch",Plate(.12f,.09f,.008f),"Rust",V(-.155f,-.55f,.357f),Quaternion.Euler(-10,0,-14));
            }
        }

        static Transform Pivot(Transform parent,string name,Vector3 position)
        {var t=ArtKit.Node(name,parent).transform;t.localPosition=position;return t;}
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static void Layer(Transform t,int layer){t.gameObject.layer=layer;foreach(Transform c in t)Layer(c,layer);}
        static void Ellipsoid(Transform parent,string name,Vector3 position,Vector3 size,string material,int segments=16,int rings=10,Quaternion? rotation=null)
        {var t=ArtKit.Ellipsoid(parent,name,position,size,material,segments,rings);if(rotation.HasValue)t.localRotation=rotation.Value;}
        static Transform Part(Transform parent,string name,Mesh mesh,string material,Vector3? position=null,Quaternion? rotation=null)
        {return ArtKit.Part(parent,name,mesh,position??Vector3.zero,Vector3.one,material,rotation);}
        static void SweepPart(Transform parent,string name,Vector3[] points,float[] widths,float[] depths,string material,int segments=14,float power=1)
        {Part(parent,name,Sweep(name,points,widths,depths,segments,power),material);}

        // Smooth station-based anatomy with elliptical cross sections. The start/end caps
        // are closed and all triangle winding is outward. No capsule primitives are used.
        static Mesh Sweep(string name,Vector3[] points,float[] widths,float[] depths,int segments=14,float power=1)
        {
            var v=new List<Vector3>();var tr=new List<int>();Vector3 lastU=Vector3.right;
            for(int row=0;row<points.Length;row++)
            {
                Vector3 tangent=(points[Math.Min(row+1,points.Length-1)]-points[Math.Max(0,row-1)]).normalized;
                Vector3 u=Vector3.Cross(tangent,Vector3.forward);
                if(u.sqrMagnitude<.015f)u=Vector3.ProjectOnPlane(lastU,tangent);
                if(u.sqrMagnitude<.001f)u=Vector3.Cross(tangent,Vector3.up);
                u.Normalize();if(row>0&&Vector3.Dot(u,lastU)<0)u=-u;lastU=u;
                Vector3 b=Vector3.Cross(tangent,u).normalized;
                for(int i=0;i<segments;i++)
                {
                    float a=i*Mathf.PI*2/segments,c=Mathf.Cos(a),s=Mathf.Sin(a);
                    v.Add(points[row]+u*(Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),power)*widths[row]*.5f)+b*(Mathf.Sign(s)*Mathf.Pow(Mathf.Abs(s),power)*depths[row]*.5f));
                }
            }
            for(int row=0;row<points.Length-1;row++)for(int i=0;i<segments;i++)
            {int a=row*segments+i,b=row*segments+(i+1)%segments,c=a+segments,d=b+segments;tr.AddRange(new[]{a,b,c,b,d,c});}
            int start=v.Count;v.Add(points[0]);int end=v.Count;v.Add(points[points.Length-1]);int last=(points.Length-1)*segments;
            for(int i=0;i<segments;i++){int next=(i+1)%segments;tr.AddRange(new[]{start,next,i,end,last+i,last+next});}
            return Mesh(name,v,tr);
        }
        static Mesh LoftY(string name,float[] y,float[] widths,float[] depths,float[] z,int segments=18,float power=.9f)
        {return Sweep(name,y.Select((value,i)=>V(0,value,z[i])).ToArray(),widths,depths,segments,power);}
        static Mesh LoftZ(string name,float[] z,float[] widths,float[] heights,float[] y,int segments=16,float power=.7f)
        {return Sweep(name,z.Select((value,i)=>V(0,y[i],value)).ToArray(),widths,heights,segments,power);}

        static Mesh Plate(float width,float height,float depth)
        {
            Vector2[] corners={new Vector2(-.32f,-.5f),new Vector2(.35f,-.47f),new Vector2(.49f,-.13f),new Vector2(.44f,.30f),new Vector2(.29f,.5f),new Vector2(-.35f,.45f),new Vector2(-.5f,.20f),new Vector2(-.46f,-.23f)};
            var outline=new List<Vector2>();
            for(int i=0;i<corners.Length;i++)
            {
                var p=corners[i];var from=Vector2.Lerp(p,corners[(i+corners.Length-1)%corners.Length],.20f);var to=Vector2.Lerp(p,corners[(i+1)%corners.Length],.20f);
                for(int j=0;j<3;j++){float t=j*.5f;outline.Add((1-t)*(1-t)*from+2*(1-t)*t*p+t*t*to);}
            }
            int n=outline.Count;var v=new List<Vector3>();var tr=new List<int>();float bevel=Mathf.Min(.025f,depth*.28f);
            float[] z={-depth*.5f,-depth*.5f+bevel,depth*.5f-bevel,depth*.5f};float[] scales={.91f,1f,1f,.875f};
            for(int row=0;row<4;row++)foreach(var p in outline)v.Add(V(p.x*width*scales[row],p.y*height*scales[row],z[row]));
            for(int row=0;row<3;row++)for(int i=0;i<n;i++){int a=row*n+i,b=row*n+(i+1)%n,c=a+n,d=b+n;tr.AddRange(new[]{a,b,c,b,d,c});}
            int back=v.Count;v.Add(V(0,0,-depth*.5f));int front=v.Count;v.Add(V(0,0,depth*.5f+Mathf.Min(.018f,depth*.16f)));
            for(int i=0;i<n;i++){int next=(i+1)%n;tr.AddRange(new[]{back,next,i,front,n*3+i,n*3+next});}
            return Mesh("Beveled asymmetric salvage plate",v,tr);
        }

        static Mesh TornCloth()
        {
            const int n=24;var v=new List<Vector3>();var tr=new List<int>();
            for(int shell=0;shell<2;shell++)for(int row=0;row<3;row++)for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n;
                float fold=1+Mathf.Cos(a*6)*.035f;
                float width=(row==0?.98f:row==1?.91f:.78f)*fold-shell*.018f;
                float depth=(row==0?.67f:row==1?.69f:.58f)*fold-shell*.018f;
                float y=row==0?-.44f:row==1?-.66f:-.86f-(i%4==0?.105f:i%4==2?.045f:0);
                v.Add(V(Mathf.Cos(a)*width*.5f,y,Mathf.Sin(a)*depth*.5f+.025f));
            }
            for(int shell=0;shell<2;shell++)for(int row=0;row<2;row++)for(int i=0;i<n;i++)
            {
                int a=shell*n*3+row*n+i,b=shell*n*3+row*n+(i+1)%n,c=a+n,d=b+n;
                // Stations descend in Y, so the outer face winds angle-to-next-station.
                if(shell==0)tr.AddRange(new[]{a,b,c,b,d,c});else tr.AddRange(new[]{a,c,b,b,c,d});
            }
            for(int row=0;row<3;row+=2)for(int i=0;i<n;i++)
            {
                int a=row*n+i,b=row*n+(i+1)%n,c=a+n*3,d=b+n*3;
                if(row==0)tr.AddRange(new[]{a,c,b,b,c,d});else tr.AddRange(new[]{a,b,c,b,d,c});
            }
            return Mesh("Torn thick folded waist cloth",v,tr);
        }

        static Mesh Mesh(string name,List<Vector3> vertices,List<int> triangles)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
