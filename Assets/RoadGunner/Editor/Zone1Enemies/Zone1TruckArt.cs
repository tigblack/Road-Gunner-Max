using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    /// <summary>
    /// Original six-wheel arena truck. Visual-only authoring; runtime binds the named bones.
    /// Ground-space origin: nominal ground Y=0. Tire radius .65; wheel centers Y=.70.
    /// Approximate bounds X +/-1.61, Z -3.48..3.78; cab roof3.07, gun/sight top4.33.
    /// Bone paths relative to "Zone1 Truck visuals":
    /// Wheels/Front left, Front right, Middle left, Middle right, Rear left, Rear right.
    /// TurretYaw=(0,3.60,-1.55); TurretYaw/TurretPitch=(0,.42,.20);
    /// TurretYaw/TurretPitch/Muzzle=(0,0,1.90). Identity rest rotations, bore +Z.
    /// Six wheels rotate about local X and use the existing shared palette/surface response.
    /// Nine atlas renderers: body, six wheels, yaw armor, elevating cannon.
    /// Do not bake the entire protected yaw/pitch groups after the runtime binds their bones.
    /// </summary>
    public static class Zone1TruckArt
    {
        static readonly Quaternion Axle=Quaternion.Euler(0,0,90),Forward=Quaternion.Euler(90,0,0);
        static Mesh lug;
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static Quaternion Q(float x,float y,float z)=>Quaternion.Euler(x,y,z);
        public static void Apply(GameObject root)
        {
            if(!root)throw new ArgumentNullException(nameof(root));
            var old=root.transform.Find("Zone1 Truck visuals");if(old)Object.DestroyImmediate(old.gameObject);
            var visual=ArtKit.Node("Zone1 Truck visuals",root.transform).transform;
            var body=ArtKit.Node("Body",visual).transform;
            Chassis(body);Cab(body);Cargo(body);Wear(body);ConceptEnemies.BakeAtlas(body,"Zone1_Truck_Body");
            var wheels=ArtKit.Node("Wheels",visual).transform;
            string[] names={"Front left","Front right","Middle left","Middle right","Rear left","Rear right"};
            float[] axles={2.05f,-1.15f,-2.55f};
            for(int i=0;i<6;i++)
            {
                var wheel=ArtKit.Node(names[i],wheels).transform;wheel.localPosition=V(i%2==0?-1.22f:1.22f,.70f,axles[i/2]);
                Wheel(wheel,i%2==0?-1:1);ConceptEnemies.BakeAtlas(wheel,"Zone1_Truck_"+(i%2==0?"Left":"Right")+"Wheel");
            }
            Turret(visual);Layer(visual,root.layer);
        }
        static void Layer(Transform t,int layer){t.gameObject.layer=layer;foreach(Transform c in t)Layer(c,layer);}
        static void B(Transform p,string n,Vector3 at,Vector3 size,string mat,float bevel=.045f,Quaternion? rotation=null)
            =>ArtKit.Box(p,n,at,size,mat,bevel,rotation);
        static void C(Transform p,string n,Vector3 at,float radius,float height,string mat,Quaternion? rotation=null,int segments=16)
            =>ArtKit.Cylinder(p,n,at,radius,height,mat,rotation,segments);
        static void Beam(Transform p,string n,Vector3 a,Vector3 b,float width,string mat)=>ArtKit.Beam(p,n,a,b,width,mat);

        static void Chassis(Transform p)
        {
            // Deep narrow longitudinal rails remain visible between the three real axle positions.
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Exposed long chassis rail",V(s*.71f,.72f,-.10f),V(.18f,.27f,6.24f),"Steel",.04f);
                B(p,"Rust-red frame lower flange",V(s*.71f,.565f,-.10f),V(.25f,.055f,6.20f),"RustDark",.015f);
            }
            foreach(float z in new[]{2.05f,-1.15f,-2.55f})
            {
                C(p,"Three substantial steel axle housings",V(0,.67f,z),.14f,2.38f,"Steel",Axle,12);
                ArtKit.Ellipsoid(p,"Axle differential housing",V(0,.65f,z),V(.45f,.38f,.45f),"Steel",12,8);
                for(int s=-1;s<=1;s+=2)
                {
                    Beam(p,"Visible suspension link",V(s*.68f,.90f,z-.26f),V(s*1.13f,.67f,z),.12f,"SteelLight");
                    Beam(p,"Dusty shock absorber",V(s*.91f,1.12f,z+.12f),V(s*1.19f,.69f,z),.10f,"IvoryShade");
                    B(p,"Layered leaf spring",V(s*.78f,.69f,z),V(.11f,.075f,.88f),"RustDark",.018f);
                    B(p,"Leaf spring steel lower layer",V(s*.78f,.65f,z),V(.12f,.045f,.70f),"SteelLight",.011f);
                }
            }
            Loft(p,"Sculpted tapered front body",new[]{
                new Section(.92f,1.14f,1.08f,1.58f),new Section(1.54f,1.17f,1.07f,1.59f),
                new Section(2.57f,1.10f,1.08f,1.47f),new Section(3.05f,.96f,1.12f,1.36f)},"Red");
            // A sloping, shouldered bonnet makes the front read as a heavy truck, not a cube.
            Loft(p,"Raised heavy truck bonnet",new[]{
                new Section(1.05f,1.05f,1.50f,1.76f),new Section(1.57f,1.075f,1.49f,1.73f),
                new Section(2.62f,1.00f,1.37f,1.59f,.045f),new Section(3.04f,.91f,1.30f,1.45f)},"Red");
            var stripe=ArtKit.Node("Broad contoured ivory hood band",p).transform;stripe.localPosition=V(-.42f,0,0);
            Loft(stripe,"Worn ivory bonnet band",new[]{new Section(1.10f,.205f,1.764f,1.778f),new Section(1.57f,.205f,1.736f,1.75f),new Section(2.62f,.205f,1.596f,1.61f),new Section(2.98f,.205f,1.476f,1.49f)},"Ivory");
            for(int s=-1;s<=1;s+=2)
            {
                // Fender bands have an actual circular wheel opening, with a soft substantial lip.
                Arch(p,"Front formed red fender",V(s*1.22f,.70f,2.05f),s,.705f,.845f,.22f,"Red");
                Arch(p,"Front worn fender rolled edge",V(s*1.22f,.70f,2.05f),s,.695f,.735f,.255f,"RustDark");
                for(int k=1;k<3;k++)Arch(p,"Tandem axle armored fender",V(s*1.22f,.70f,k==1?-1.15f:-2.55f),s,.71f,.82f,.205f,k==1?"RustDark":"Red");
                B(p,"Front mud flap",V(s*1.22f,.55f,1.37f),V(.50f,.66f,.055f),"Rubber",.02f,Q(-8,0,0));
                B(p,"Tandem rear mud flap",V(s*1.23f,.51f,-3.23f),V(.52f,.72f,.065f),"Rubber",.018f,Q(-6,0,0));
                B(p,"Cab lower climbing step",V(s*1.17f,.88f,.25f),V(.41f,.13f,.93f),"Steel",.04f);
                B(p,"Cab upper climbing step",V(s*1.15f,1.14f,.34f),V(.33f,.115f,.76f),"SteelLight",.03f);
                for(int j=0;j<3;j++)B(p,"Broad step grip ridge",V(s*1.18f,.955f,.00f+j*.26f),V(.32f,.025f,.035f),"SteelLight",.006f);
            }
            // Deep grille, intentionally limited broad slats and lamp guards stay readable from above.
            B(p,"Recessed charcoal grille",V(0,1.30f,3.065f),V(1.83f,.58f,.085f),"Steel",.05f);
            for(int j=-4;j<=4;j++)B(p,"Nine substantial radiator grille slats",V(j*.185f,1.30f,3.125f),V(.055f,.48f,.045f),"SteelLight",.012f);
            B(p,"Grille top ivory brow",V(0,1.605f,3.07f),V(1.94f,.10f,.12f),"IvoryShade",.025f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Headlamp recessed guard housing",V(s*1.03f,1.32f,2.965f),V(.38f,.34f,.23f),"Steel",.06f);
                C(p,"Large warm headlamp lens",V(s*1.03f,1.32f,3.10f),.117f,.035f,"Yellow",Forward,16);
                Beam(p,"Lamp protective crossbar",V(s*1.03f-.13f,1.32f,3.13f),V(s*1.03f+.13f,1.32f,3.13f),.025f,"Steel");
                Beam(p,"Battering ram frame brace",V(s*.80f,.70f,2.55f),V(s*.97f,.82f,3.32f),.22f,"Steel");
            }
            Loft(p,"Profiled massive forward ram",new[]{new Section(3.20f,1.35f,.65f,1.07f),new Section(3.48f,1.47f,.64f,1.02f),new Section(3.61f,1.41f,.77f,.96f)},"Steel");
            B(p,"Ram scuffed off-white impact stripe",V(0,.85f,3.622f),V(2.56f,.105f,.025f),"IvoryShade",.01f);
            for(int j=-1;j<=1;j++)Spike(p,"Three welded ram breaker teeth",V(j*.86f,1.00f,3.44f),V(j*.87f,1.16f,3.78f),.13f,"SteelLight");
            for(int s=-1;s<=1;s+=2)B(p,"Ram narrow welded end shoes",V(s*1.38f,.87f,3.43f),V(.14f,.40f,.24f),"SteelLight",.03f,Q(0,0,s*7));
        }

        static void Cab(Transform p)
        {
            Loft(p,"Stepped heavy cab lower shell",new[]{new Section(-.40f,1.12f,1.17f,1.94f),new Section(.04f,1.19f,1.14f,2.02f),new Section(1.22f,1.15f,1.18f,1.96f)},"IvoryShade");
            B(p,"Cabin recessed charcoal interior",V(0,2.03f,.42f),V(1.93f,.48f,1.49f),"Steel",.07f);
            // The back and raked roof are custom profiled shells. Windows remain open at the sides.
            Loft(p,"Rounded upright cab rear wall",new[]{new Section(-.43f,1.06f,1.72f,2.86f),new Section(-.25f,1.12f,1.72f,2.93f),new Section(-.11f,1.10f,1.72f,2.90f)},"Red");
            Loft(p,"Shaped heavy cab roof",new[]{new Section(-.40f,1.065f,2.87f,3.02f),new Section(-.10f,1.16f,2.90f,3.07f),new Section(.87f,1.10f,2.85f,3.02f),new Section(1.08f,.99f,2.76f,2.91f)},"Ivory");
            for(int s=-1;s<=1;s+=2)
            {
                Side(p,"Raised red cab door",new[]{new Vector2(-.15f,1.37f),new Vector2(-.17f,2.14f),new Vector2(1.03f,2.14f),new Vector2(1.17f,1.44f)},s*1.15f,.12f,"Red");
                Side(p,"Cab door large dirty ivory salvage panel",new[]{new Vector2(-.10f,1.50f),new Vector2(-.10f,1.96f),new Vector2(.75f,1.96f),new Vector2(.89f,1.52f)},s*1.218f,.035f,"Ivory");
                Beam(p,"Raked steel windscreen pillar",V(s*1.10f,1.94f,1.27f),V(s*.98f,2.87f,.97f),.12f,"Steel");
                Beam(p,"Cab rear door pillar",V(s*1.12f,1.93f,-.16f),V(s*1.05f,2.92f,-.15f),.14f,"Red");
                Beam(p,"Open side window lower trim",V(s*1.21f,2.13f,-.10f),V(s*1.17f,2.13f,1.02f),.055f,"SteelLight");
                B(p,"Cab door recessed handle",V(s*1.245f,2.055f,.07f),V(.045f,.07f,.23f),"Steel",.018f);
                Beam(p,"Tall grab handle upright",V(s*1.26f,1.57f,.91f),V(s*1.26f,2.12f,.91f),.045f,"SteelLight");
                Beam(p,"Mirror sturdy bracket",V(s*1.12f,2.47f,.98f),V(s*1.41f,2.47f,1.04f),.055f,"Steel");
                B(p,"Truck side mirror",V(s*1.43f,2.47f,1.04f),V(.18f,.29f,.14f),"Steel",.04f);
                B(p,"Mirror glass",V(s*1.43f,2.47f,.957f),V(.115f,.22f,.018f),"Glass",.015f);
                // Tall exhausts are connected to an underbody manifold and carry readable heat sleeves.
                Beam(p,"Exhaust side manifold",V(s*.88f,.91f,-.50f),V(s*1.10f,1.34f,-.53f),.13f,"Steel");
                C(p,"Twin high exhaust stacks",V(s*1.10f,2.22f,-.53f),.092f,1.89f,"SteelLight");
                C(p,"Broad perforation-free heat shield",V(s*1.10f,1.91f,-.53f),.122f,.91f,"Steel");
                C(p,"Stack dark mouth",V(s*1.10f,3.172f,-.53f),.073f,.014f,"Rubber");
                B(p,"Stack hinged rain cap",V(s*1.10f,3.20f,-.53f),V(.25f,.035f,.22f),"Steel",.015f,Q(0,0,s*9));
            }
            // Two windshield halves, central pillar and visor, plus an actual seated raider driver.
            Quad(p,"Left broad tinted windscreen",new[]{V(-1.02f,2.10f,1.219f),V(-.07f,2.10f,1.219f),V(-.07f,2.71f,1.022f),V(-.94f,2.71f,1.022f)},"Glass");
            Quad(p,"Right broad tinted windscreen",new[]{V(.07f,2.10f,1.219f),V(1.02f,2.10f,1.219f),V(.94f,2.71f,1.022f),V(.07f,2.71f,1.022f)},"Glass");
            Beam(p,"Windscreen central steel mullion",V(0,2.04f,1.24f),V(0,2.83f,.99f),.075f,"Steel");
            Beam(p,"Windscreen lower surround",V(-1.10f,2.08f,1.24f),V(1.10f,2.08f,1.24f),.085f,"IvoryShade");
            B(p,"Cab visor overhang",V(0,2.86f,1.10f),V(2.30f,.09f,.31f),"Red",.04f,Q(5,0,0));
            for(int j=-1;j<=1;j++)B(p,"Three cab marker lamps",V(j*.66f,3.061f,.66f),V(.15f,.065f,.20f),"Yellow",.025f);
            B(p,"Cab armor roof service hatch",V(.30f,3.081f,.03f),V(.62f,.055f,.65f),"RustDark",.035f);
            B(p,"Driver tall seat",V(-.53f,2.02f,.18f),V(.52f,.78f,.27f),"Rubber",.09f);
            ArtKit.Ellipsoid(p,"Driver heavy cloth torso",V(-.53f,2.18f,.35f),V(.49f,.59f,.32f),"Cloth",12,8);
            ArtKit.Ellipsoid(p,"Driver head",V(-.53f,2.58f,.46f),V(.28f,.31f,.28f),"Skin",12,8);
            ArtKit.Ellipsoid(p,"Driver scavenged helmet",V(-.53f,2.70f,.45f),V(.33f,.19f,.32f),"Steel",12,8);
            B(p,"Driver mask and goggles",V(-.53f,2.59f,.602f),V(.25f,.13f,.085f),"Rubber",.03f);
            ArtKit.Ellipsoid(p,"Driver wrapped red scarf",V(-.53f,2.43f,.41f),V(.41f,.14f,.35f),"Scarf",12,8);
            Beam(p,"Driver left arm",V(-.78f,2.31f,.40f),V(-.75f,2.21f,.84f),.14f,"Skin");
            Beam(p,"Driver right arm",V(-.29f,2.31f,.40f),V(-.34f,2.21f,.84f),.14f,"Cloth");
            ArtKit.Torus(p,"Driver steering wheel",V(-.54f,2.21f,.87f),.24f,.195f,.04f,"Steel",Q(70,0,0),16);
        }

        static void Cargo(Transform p)
        {
            Loft(p,"Rear cargo deck substantial edge",new[]{new Section(-3.31f,1.13f,1.04f,1.30f),new Section(-2.94f,1.22f,1.03f,1.34f),new Section(-.48f,1.22f,1.04f,1.34f)},"RustDark");
            B(p,"Cargo deck steel floor",V(0,1.34f,-1.85f),V(2.25f,.10f,2.95f),"Steel",.04f);
            // Horizontal hoarded-fuel tank has a round silhouette and dished end caps.
            C(p,"Long armored salvaged fuel tank",V(0,2.00f,-1.82f),.69f,2.60f,"RustDark",Forward,24);
            ArtKit.Ellipsoid(p,"Dished rear fuel tank end",V(0,2.00f,-3.13f),V(1.32f,1.32f,.20f),"IvoryShade",20,12);
            ArtKit.Ellipsoid(p,"Dished front fuel tank end",V(0,2.00f,-.51f),V(1.32f,1.32f,.20f),"Red",20,12);
            foreach(float z in new[]{-2.68f,-.94f})
            {
                ArtKit.Torus(p,"Broad steel tank retaining hoop",V(0,2.00f,z),.727f,.687f,.13f,"SteelLight",Forward,24);
                for(int s=-1;s<=1;s+=2)Beam(p,"Tank hoop deck anchor",V(s*.57f,1.59f,z),V(s*.79f,1.32f,z),.14f,"Steel");
            }
            C(p,"Tank top filler neck",V(.39f,2.65f,-2.68f),.13f,.16f,"Steel");
            C(p,"Sealed filler cap",V(.39f,2.75f,-2.68f),.17f,.055f,"IvoryShade");
            B(p,"Rear tank three broad warning bars",V(0,2.0f,-3.242f),V(.17f,.66f,.018f),"Red",.015f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Rear tank flank warning bar",V(s*.25f,2.0f,-3.225f),V(.14f,.59f,.018f),"Red",.015f,Q(0,s*15,0));
                // Scavenged side armor is a formed profile with visible standoff brackets.
                Side(p,"Tandem wheel flank armor",new[]{new Vector2(-3.08f,1.54f),new Vector2(-3.08f,1.87f),new Vector2(-2.70f,2.03f),new Vector2(-.69f,2.03f),new Vector2(-.55f,1.53f)},s*1.13f,.095f,"Red");
                B(p,"Side armor off-white salvaged insert",V(s*1.189f,1.79f,-2.06f),V(.025f,.28f,1.01f),"IvoryShade",.025f,Q(0,0,s*3));
                foreach(float z in new[]{-2.85f,-.79f})Beam(p,"Armor standoff welded bracket",V(s*.92f,1.37f,z),V(s*1.14f,1.68f,z),.11f,"Steel");
                B(p,"Rear vertical tail lamp guard",V(s*.98f,1.35f,-3.34f),V(.24f,.40f,.11f),"Steel",.035f);
                B(p,"Rear red tail lamp",V(s*.98f,1.42f,-3.407f),V(.14f,.19f,.025f),"Red",.02f);
                B(p,"Rear lamp ivory reverse strip",V(s*.98f,1.24f,-3.407f),V(.14f,.07f,.025f),"Ivory",.012f);
            }
            B(p,"Rear heavy impact crossmember",V(0,.93f,-3.37f),V(2.76f,.22f,.22f),"Steel",.055f);
            // Connected platform and pedestal support the high gun without an unexplained floating disc.
            for(int s=-1;s<=1;s+=2)
            {
                Beam(p,"Turret support inner leg",V(s*.56f,1.34f,-1.55f),V(s*.49f,2.83f,-1.55f),.17f,"Steel");
                Beam(p,"Turret support diagonal",V(s*.89f,1.35f,-2.17f),V(s*.49f,2.83f,-1.55f),.13f,"RustDark");
            }
            B(p,"Turret armored platform",V(0,2.83f,-1.55f),V(1.33f,.18f,1.36f),"Steel",.07f);
            C(p,"Turret reinforced pedestal",V(0,3.16f,-1.55f),.43f,.58f,"RustDark",null,20);
            C(p,"Pedestal lower steel reinforcing collar",V(0,2.98f,-1.55f),.48f,.12f,"Steel",null,20);
            C(p,"Pedestal central clamping ring",V(0,3.22f,-1.55f),.448f,.075f,"SteelLight",null,20);
            for(int s=-1;s<=1;s+=2)for(int z=-1;z<=1;z+=2)
                Beam(p,"Four welded pedestal reinforcing gussets",V(s*.57f,2.94f,-1.55f+z*.46f),V(s*.30f,3.43f,-1.55f+z*.23f),.12f,"Steel");
            C(p,"Turret fixed upper bearing",V(0,3.46f,-1.55f),.51f,.12f,"SteelLight",null,20);
            // Small number of readable cargo pieces, away from wheel and turret sweeps.
            Crate(p,V(.83f,1.62f,-2.73f),Q(0,-8,0));
            Jerrycan(p,V(-.84f,1.65f,-.90f));
        }

        static void Turret(Transform visual)
        {
            var yaw=ArtKit.Node("TurretYaw",visual).transform;yaw.localPosition=V(0,3.60f,-1.55f);
            var armor=ArtKit.Node("Yaw armor",yaw).transform;
            C(armor,"Connected rotating turret bearing",V(0,-.10f,0),.53f,.20f,"Steel",null,20);
            Loft(armor,"Profiled hostile turret armored shell",new[]{new Section(-.60f,.47f,.03f,.54f),new Section(-.34f,.65f,.01f,.62f),new Section(.45f,.60f,.01f,.54f,.028f),new Section(.65f,.43f,.08f,.45f)},"Red");
            B(armor,"Turret roof ivory identification plate",V(-.19f,.615f,-.11f),V(.29f,.045f,.59f),"IvoryShade",.02f,Q(0,0,-2));
            B(armor,"Turret left ammunition enclosure",V(-.67f,.20f,-.14f),V(.31f,.42f,.55f),"Steel",.05f);
            B(armor,"Ammunition enclosure worn lid",V(-.67f,.43f,-.14f),V(.30f,.055f,.55f),"IvoryShade",.018f);
            B(armor,"Turret rear oxidized service hatch",V(0,.29f,-.62f),V(.61f,.31f,.045f),"RustDark",.03f);
            for(int s=-1;s<=1;s+=2)
            {
                C(armor,"Heavy gun trunnion",V(s*.30f,.42f,.42f),.18f,.14f,"SteelLight",Axle,16);
                B(armor,"Hostile turret flank edge wear",V(s*.62f,.30f,-.17f),V(.022f,.09f,.24f),"Rust",.012f);
            }
            C(armor,"Connected deep elevation-bearing cradle",V(0,.32f,.38f),.235f,.52f,"Steel",Axle,16);
            TurretRoofPatch(armor,"Turret one broken undercoat edge",new[]{new Vector2(.46f,-.25f),new Vector2(.53f,-.24f),new Vector2(.53f,-.18f),new Vector2(.50f,-.18f),new Vector2(.49f,-.22f)},"IvoryShade");
            TurretRoofPatch(armor,"Turret asymmetric torn paint loss",new[]{new Vector2(.39f,-.25f),new Vector2(.48f,-.23f),new Vector2(.48f,-.16f),new Vector2(.54f,-.15f),new Vector2(.51f,-.05f),new Vector2(.46f,-.02f),new Vector2(.48f,.08f),new Vector2(.43f,.09f),new Vector2(.42f,.19f),new Vector2(.36f,.15f),new Vector2(.38f,.03f),new Vector2(.34f,-.06f)},"RustDark",.012f);
            B(armor,"Turret ammunition lid chipped end",V(-.67f,.462f,-.34f),V(.24f,.014f,.11f),"Rust",.009f);
            Quad(armor,"Turret left shoulder scraped steel",new[]{V(-.653f,.23f,-.29f),V(-.653f,.38f,-.23f),V(-.648f,.39f,.03f),V(-.644f,.28f,.09f)},"SteelLight");
            ConceptEnemies.BakeAtlas(armor,"Zone1_Truck_TurretYaw");
            var pitch=ArtKit.Node("TurretPitch",yaw).transform;pitch.localPosition=V(0,.42f,.20f);
            var gun=ArtKit.Node("Gun",pitch).transform;
            B(gun,"Massive auto-cannon receiver",V(0,0,.17f),V(.40f,.34f,.73f),"Steel",.055f);
            B(gun,"Gun upper red armor jacket",V(0,.14f,.30f),V(.45f,.14f,.61f),"Red",.04f);
            C(gun,"Large exposed auto-cannon barrel",V(0,0,1.04f),.115f,1.18f,"Steel",Forward,16);
            for(int j=0;j<4;j++)C(gun,"Four broad barrel cooling ribs",V(0,0,.64f+j*.20f),.145f,.07f,"SteelLight",Forward,16);
            B(gun,"Barrel breech ivory service panel",V(.226f,.035f,.20f),V(.025f,.16f,.29f),"IvoryShade",.018f);
            B(gun,"Armored rectangular muzzle brake",V(0,0,1.72f),V(.30f,.27f,.31f),"Steel",.04f);
            B(gun,"Muzzle upper light-catching wear",V(0,.148f,1.72f),V(.21f,.018f,.25f),"SteelLight",.008f);
            C(gun,"Deep dark cannon bore",V(0,0,1.884f),.091f,.015f,"Rubber",Forward,12);
            B(gun,"Tall broad cannon front sight",V(0,.20f,1.39f),V(.075f,.22f,.08f),"Steel",.012f);
            ConceptEnemies.BakeAtlas(gun,"Zone1_Truck_TurretPitch");
            var muzzle=ArtKit.Node("Muzzle",pitch).transform;muzzle.localPosition=V(0,0,1.90f);
        }

        static void Wheel(Transform p,int side)
        {
            ArtKit.Torus(p,"Chunky rounded truck tire",Vector3.zero,.635f,.305f,.52f,"Rubber",Axle,24);
            C(p,"Broad reinforced tire sidewall",Vector3.zero,.58f,.51f,"Rubber",Axle,24);
            C(p,"Inset steel rim dish",V(side*.269f,0,0),.35f,.035f,"Steel",Axle,20);
            C(p,"Recessed oxidized hub dish",V(side*.292f,0,0),.27f,.026f,"RustDark",Axle,20);
            ArtKit.Torus(p,"Heavy rolled rim lip",V(side*.298f,0,0),.35f,.308f,.035f,"SteelLight",Axle,20);
            C(p,"Truck axle hub cap",V(side*.324f,0,0),.12f,.075f,"SteelLight",Axle,12);
            for(int j=0;j<6;j++)
            {
                float a=j*Mathf.PI*2/6;Vector3 v=V(0,Mathf.Cos(a),Mathf.Sin(a));
                Beam(p,"Six substantial wheel spokes",V(side*.317f,0,0)+v*.13f,V(side*.317f,0,0)+v*.266f,.065f,"Steel");
                C(p,"Wheel six broad fasteners",V(side*.375f,0,0)+v*.075f,.021f,.022f,"Steel",Axle,6);
            }
            if(!lug)lug=ChamferBlock(V(.235f,.048f,.115f),.012f);
            for(int j=0;j<20;j++)for(int row=-1;row<=1;row+=2)
            {
                Quaternion q=Q(j*18+row*2,0,0);Vector3 at=q*V(row*.133f,.626f,0);
                ArtKit.Part(p,"Contoured off-road truck tread",lug,at,Vector3.one,"Rubber",q*Q(0,row*15,0));
            }
        }

        static void Wear(Transform p)
        {
            // Broad wear follows real panel edges; no photographic grunge or floating scratches.
            HoodPatch(p,"Bonnet right edge undercoat",new[]{new Vector2(.87f,1.66f),new Vector2(.98f,1.69f),new Vector2(.94f,2.30f),new Vector2(.85f,2.39f)},"IvoryShade");
            HoodPatch(p,"Bonnet right edge oxidation",new[]{new Vector2(.93f,1.73f),new Vector2(.97f,1.75f),new Vector2(.93f,2.26f),new Vector2(.89f,2.31f)},"Rust",.012f);
            HoodPatch(p,"Front bonnet lip chip",new[]{new Vector2(-.68f,2.91f),new Vector2(-.42f,2.91f),new Vector2(-.39f,3.00f),new Vector2(-.66f,3.00f)},"SteelLight");
            HoodPatch(p,"Bonnet short broken undercoat remnant",new[]{new Vector2(.26f,1.94f),new Vector2(.42f,1.89f),new Vector2(.44f,1.91f),new Vector2(.28f,1.97f),new Vector2(.255f,2.04f),new Vector2(.23f,2.035f)},"IvoryShade");
            HoodPatch(p,"Bonnet asymmetric stepped paint tear",new[]{new Vector2(.28f,1.95f),new Vector2(.43f,1.90f),new Vector2(.46f,1.96f),new Vector2(.60f,1.96f),new Vector2(.65f,2.02f),new Vector2(.59f,2.07f),new Vector2(.63f,2.16f),new Vector2(.51f,2.16f),new Vector2(.49f,2.25f),new Vector2(.39f,2.23f),new Vector2(.36f,2.16f),new Vector2(.26f,2.14f),new Vector2(.28f,2.08f),new Vector2(.23f,2.04f)},"RustDark",.012f);
            HoodPatch(p,"Bonnet short uneven warm rust lip",new[]{new Vector2(.27f,2.12f),new Vector2(.38f,2.14f),new Vector2(.39f,2.20f),new Vector2(.37f,2.22f),new Vector2(.34f,2.16f),new Vector2(.26f,2.14f)},"Rust",.016f);
            HoodPatch(p,"Hood ivory band front edge paint loss",new[]{new Vector2(-.59f,2.70f),new Vector2(-.34f,2.70f),new Vector2(-.28f,2.81f),new Vector2(-.41f,2.89f),new Vector2(-.58f,2.86f)},"RustDark",.025f);
            RoofPatch(p,"Cab roof unoutlined irregular oxidation",new[]{new Vector2(-1.02f,.20f),new Vector2(-.88f,.18f),new Vector2(-.87f,.25f),new Vector2(-.78f,.24f),new Vector2(-.80f,.34f),new Vector2(-.70f,.38f),new Vector2(-.76f,.45f),new Vector2(-.75f,.57f),new Vector2(-.84f,.56f),new Vector2(-.89f,.64f),new Vector2(-1.0f,.61f),new Vector2(-1.0f,.49f),new Vector2(-.94f,.46f),new Vector2(-1.03f,.38f)},"Rust");
            RoofPatch(p,"Cab roof separate exposed metal flake",new[]{new Vector2(-.94f,.13f),new Vector2(-.87f,.13f),new Vector2(-.85f,.18f),new Vector2(-.95f,.20f)},"SteelLight",.012f);
            RoofPatch(p,"Cab roof ragged forward-corner chip",new[]{new Vector2(.80f,.64f),new Vector2(.93f,.65f),new Vector2(.93f,.70f),new Vector2(.99f,.71f),new Vector2(.97f,.78f),new Vector2(.92f,.78f),new Vector2(.90f,.84f),new Vector2(.82f,.81f),new Vector2(.84f,.74f),new Vector2(.78f,.72f)},"RustDark");
            TankPatch(p,"Tank upper flank battered ivory remnants",new[]{25f,40f,55f,70f},new[]{-2.51f,-2.60f,-2.54f,-2.42f},new[]{-1.99f,-1.86f,-1.91f,-2.05f},"IvoryShade");
            TankPatch(p,"Tank remnant broad rust intrusion",new[]{30f,45f,60f},new[]{-2.43f,-2.49f,-2.40f},new[]{-2.26f,-2.18f,-2.24f},"Rust",.704f);
            TankPatch(p,"Opposite tank worn paint strip",new[]{110f,125f,140f},new[]{-1.57f,-1.68f,-1.52f},new[]{-1.01f,-.99f,-1.13f},"IvoryShade");
            for(int s=-1;s<=1;s+=2)
            {
                float x=s*1.245f;
                Paint(p,"Cab lower seam irregular unoutlined paint loss",new[]{V(x,1.51f,-.06f),V(x,1.62f,-.03f),V(x,1.62f,.08f),V(x,1.56f,.10f),V(x,1.56f,.18f),V(x,1.64f,.22f),V(x,1.60f,.31f),V(x,1.55f,.30f),V(x,1.56f,.43f),V(x,1.51f,.48f)},"RustDark");
                Paint(p,"Cab separate oxide flake",new[]{V(x+s*.003f,1.78f,.30f),V(x+s*.003f,1.84f,.32f),V(x+s*.003f,1.82f,.38f),V(x+s*.003f,1.77f,.36f)},"Rust");
                Quad(p,"Cab front seam paint loss",new[]{V(x,1.91f,.72f),V(x,1.92f,.77f),V(x,1.59f,.86f),V(x,1.57f,.81f)},"Rust");
                B(p,"Rear cargo armor exposed lower seam",V(s*1.195f,1.66f,-2.17f),V(.025f,.055f,.73f),"RustDark",.012f);
                B(p,"Cab rear upper plate chipped edge",V(s*.89f,2.941f,-.26f),V(.18f,.03f,.23f),"Rust",.012f);
                Paint(p,"Cargo ivory armor uneven end paint loss",new[]{V(s*1.210f,1.67f,-2.54f),V(s*1.210f,1.82f,-2.49f),V(s*1.210f,1.84f,-2.42f),V(s*1.210f,1.77f,-2.39f),V(s*1.210f,1.79f,-2.30f),V(s*1.210f,1.70f,-2.19f),V(s*1.210f,1.69f,-2.35f)},"Rust");
                FenderPatch(p,"Front fender battered paint edge",s,2.05f,1.449f,27,64,.745f,.834f,"IvoryShade");
                FenderPatch(p,"Front fender oxidation inside worn edge",s,2.05f,1.453f,34,57,.768f,.834f,"RustDark");
                FenderPatch(p,"Rear fender broad scraped edge",s,-2.55f,1.431f,94,125,.744f,.811f,"Rust");
            }
            B(p,"Ram restrained central oxidation",V(-.57f,.865f,3.639f),V(.28f,.065f,.012f),"RustDark",.007f);
        }
        static void Crate(Transform p,Vector3 position,Quaternion rotation)
        {
            var g=ArtKit.Node("Spare ammunition crate",p).transform;g.localPosition=position;g.localRotation=rotation;
            B(g,"Heavy wooden supply chest",Vector3.zero,V(.54f,.47f,.60f),"Wood",.04f);
            foreach(int s in new[]{-1,1})B(g,"Chest steel band",V(s*.18f,0,0),V(.065f,.49f,.62f),"Steel",.015f);
            B(g,"Chest top dirty ivory stencil",V(0,.244f,0),V(.15f,.015f,.23f),"IvoryShade",.009f);
        }
        static void Jerrycan(Transform p,Vector3 position)
        {
            var g=ArtKit.Node("Salvaged spare fuel can",p).transform;g.localPosition=position;
            B(g,"Pressed dark red fuel can",Vector3.zero,V(.42f,.57f,.28f),"Red",.055f);
            B(g,"Fuel can top shoulder",V(0,.29f,0),V(.36f,.09f,.25f),"Rust",.027f);
            Beam(g,"Fuel can carrying handle",V(-.10f,.41f,0),V(.09f,.41f,0),.045f,"Steel");
            Beam(g,"Handle upright",V(-.10f,.32f,0),V(-.10f,.41f,0),.045f,"Steel");
            Beam(g,"Handle upright",V(.09f,.32f,0),V(.09f,.41f,0),.045f,"Steel");
            C(g,"Fuel cap",V(.14f,.35f,0),.05f,.04f,"Steel",null,10);
            Beam(g,"Can broad pressed diagonal",V(-.13f,-.18f,.15f),V(.13f,.18f,.15f),.027f,"RustDark");
            Beam(g,"Can broad pressed diagonal",V(.13f,-.18f,.15f),V(-.13f,.18f,.15f),.027f,"RustDark");
        }
        struct Section
        {public float z,w,b,t,dent;public Section(float z,float width,float bottom,float top,float dent=0){this.z=z;w=width;b=bottom;t=top;this.dent=dent;}}
        static void Loft(Transform p,string name,Section[] sections,string mat)
        {
            var mesh=new MeshBuilder();var rings=new List<Vector3[]>();
            foreach(var a in sections)
            {float b=Mathf.Min(.09f,(a.t-a.b)*.25f),x=Mathf.Min(.075f,a.w*.18f);rings.Add(new[]{V(-a.w+x,a.b,a.z),V(a.w-x,a.b,a.z),V(a.w-a.dent,a.b+b,a.z),V(a.w-a.dent,a.t-b-a.dent*.35f,a.z),V(a.w-x-a.dent,a.t-a.dent*.4f,a.z),V(-a.w+x,a.t,a.z),V(-a.w,a.t-b,a.z),V(-a.w,a.b+b,a.z)});}
            for(int j=0;j<rings.Count-1;j++)for(int k=0;k<8;k++)mesh.Face(new[]{rings[j][k],rings[j][(k+1)%8],rings[j+1][(k+1)%8],rings[j+1][k]});
            mesh.Face(rings[0].Reverse().ToArray());mesh.Face(rings.Last());Part(p,name,mesh.Build(name),mat);
        }
        static void Side(Transform p,string name,Vector2[] zy,float x,float depth,string mat)
        {
            var mesh=new MeshBuilder();var a=zy.Select(v=>V(x-depth*.5f,v.y,v.x)).ToArray();var b=zy.Select(v=>V(x+depth*.5f,v.y,v.x)).ToArray();
            mesh.Face(a.Reverse().ToArray());mesh.Face(b);for(int j=0;j<zy.Length;j++)mesh.Face(new[]{a[j],a[(j+1)%zy.Length],b[(j+1)%zy.Length],b[j]});Part(p,name,mesh.Build(name),mat);
        }
        static void Arch(Transform p,string name,Vector3 center,int side,float inner,float outer,float depth,string mat)
        {
            var mesh=new MeshBuilder();Action<Vector3[]> face=v=>mesh.Face(side<0?v.Reverse().ToArray():v);
            for(int j=0;j<14;j++)
            {
                float a=Mathf.Lerp(-7,187,j/14f)*Mathf.Deg2Rad,b=Mathf.Lerp(-7,187,(j+1)/14f)*Mathf.Deg2Rad;
                Vector3 i0=center+V(0,Mathf.Sin(a)*inner,Mathf.Cos(a)*inner),i1=center+V(0,Mathf.Sin(b)*inner,Mathf.Cos(b)*inner);
                Vector3 o0=center+V(0,Mathf.Sin(a)*outer,Mathf.Cos(a)*outer),o1=center+V(0,Mathf.Sin(b)*outer,Mathf.Cos(b)*outer),d=V(side*depth,0,0);
                face(new[]{i0+d,i1+d,o1+d,o0+d});face(new[]{o0,o1,i1,i0});face(new[]{o0,o0+d,o1+d,o1});face(new[]{i0,i1,i1+d,i0+d});
            }
            Part(p,name,mesh.Build(name),mat);
        }
        static void Quad(Transform p,string name,Vector3[] points,string mat)
        {var mesh=new MeshBuilder();mesh.Face(points);mesh.Face(points.Reverse().ToArray());Part(p,name,mesh.Build(name),mat);}
        static void Paint(Transform p,string name,Vector3[] points,string mat)
        {var mesh=new MeshBuilder();mesh.Polygon(points);mesh.Polygon(points.Reverse().ToArray());Part(p,name,mesh.Build(name),mat);}
        static void HoodPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.008f)
        {
            float[] z={1.05f,1.57f,2.62f,3.04f},h={1.76f,1.73f,1.59f,1.45f};
            float Height(float at){for(int i=1;i<z.Length;i++)if(at<=z[i])return Mathf.Lerp(h[i-1],h[i],Mathf.InverseLerp(z[i-1],z[i],at));return h.Last();}
            Paint(p,name,xz.Select(v=>V(v.x,Height(v.y)+lift,v.y)).ToArray(),mat);
        }
        static void RoofPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.008f)
        {
            float[] z={-.40f,-.10f,.87f,1.08f},h={3.02f,3.07f,3.02f,2.91f};
            float Height(float at){for(int i=1;i<z.Length;i++)if(at<=z[i])return Mathf.Lerp(h[i-1],h[i],Mathf.InverseLerp(z[i-1],z[i],at));return h.Last();}
            Paint(p,name,xz.Select(v=>V(v.x,Height(v.y)+lift,v.y)).ToArray(),mat);
        }
        static void TurretRoofPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.008f)
        {
            float[] z={-.60f,-.34f,.45f,.65f},h={.54f,.62f,.54f,.45f};
            float Height(float at){for(int i=1;i<z.Length;i++)if(at<=z[i])return Mathf.Lerp(h[i-1],h[i],Mathf.InverseLerp(z[i-1],z[i],at));return h.Last();}
            Paint(p,name,xz.Select(v=>V(v.x,Height(v.y)+lift,v.y)).ToArray(),mat);
        }
        static void TankPatch(Transform p,string name,float[] angles,float[] start,float[] end,string mat,float radius=.699f)
        {
            var mesh=new MeshBuilder();Vector3 Point(float degrees,float z){float a=degrees*Mathf.Deg2Rad;return V(Mathf.Cos(a)*radius,2+Mathf.Sin(a)*radius,z);}
            for(int j=0;j<angles.Length-1;j++)mesh.Face(new[]{Point(angles[j],start[j]),Point(angles[j+1],start[j+1]),Point(angles[j+1],end[j+1]),Point(angles[j],end[j])});
            Part(p,name,mesh.Build(name),mat);
        }
        static void FenderPatch(Transform p,string name,int side,float z,float x,float angleFrom,float angleTo,float inner,float outer,string mat)
        {
            var mesh=new MeshBuilder();Vector3 Point(float degrees,float r){float a=degrees*Mathf.Deg2Rad;return V(side*x,.70f+Mathf.Sin(a)*r,z+Mathf.Cos(a)*r);}
            for(int j=0;j<4;j++)
            {
                float a=Mathf.Lerp(angleFrom,angleTo,j/4f),b=Mathf.Lerp(angleFrom,angleTo,(j+1)/4f);
                var v=new[]{Point(a,inner),Point(b,inner),Point(b,outer),Point(a,outer)};mesh.Face(side<0?v.Reverse().ToArray():v);
            }
            Part(p,name,mesh.Build(name),mat);
        }
        static void Spike(Transform p,string name,Vector3 a,Vector3 b,float radius,string mat)
        {
            Vector3 dir=(b-a).normalized,x=Vector3.Cross(dir,Vector3.up).normalized*radius,y=Vector3.Cross(dir,x).normalized*radius;var mesh=new MeshBuilder();Vector3[] ring={a+x,a+y,a-x,a-y};
            for(int i=0;i<4;i++)mesh.Face(new[]{ring[i],ring[(i+1)%4],b});mesh.Face(ring.Reverse().ToArray());Part(p,name,mesh.Build(name),mat);
        }
        static void Part(Transform p,string name,Mesh mesh,string mat)=>ArtKit.Part(p,name,mesh,Vector3.zero,Vector3.one,mat);
        static Mesh ChamferBlock(Vector3 size,float bevel)
        {
            var mesh=new MeshBuilder();Vector3 h=size*.5f;float b=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.75f);Vector3[] axes={Vector3.right,Vector3.up,Vector3.forward};
            Action<Vector3[],Vector3> face=(v,n)=>{if(Vector3.Dot(Vector3.Cross(v[1]-v[0],v[2]-v[0]),n)<0)Array.Reverse(v);mesh.Face(v);};
            for(int a=0;a<3;a++)for(int s=-1;s<=1;s+=2)
            {Vector3 n=axes[a]*s,u=axes[(a+1)%3],v=axes[(a+2)%3],c=n*h[a];float hu=h[(a+1)%3]-b,hv=h[(a+2)%3]-b;face(new[]{c-u*hu-v*hv,c+u*hu-v*hv,c+u*hu+v*hv,c-u*hu+v*hv},n);}
            for(int e=0;e<3;e++)for(int s=-1;s<=1;s+=2)for(int t=-1;t<=1;t+=2)
            {int a=(e+1)%3,c=(e+2)%3;Vector3 edge=axes[e]*(h[e]-b),pa=axes[a]*(s*h[a])+axes[c]*(t*(h[c]-b)),pc=axes[a]*(s*(h[a]-b))+axes[c]*(t*h[c]);face(new[]{pa-edge,pa+edge,pc+edge,pc-edge},axes[a]*s+axes[c]*t);}
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)face(new[]{V(x*h.x,y*(h.y-b),z*(h.z-b)),V(x*(h.x-b),y*h.y,z*(h.z-b)),V(x*(h.x-b),y*(h.y-b),z*h.z)},V(x,y,z));
            return mesh.Build("Zone1_Truck_ChamferedTireLug");
        }
        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();
            public void Face(Vector3[] v){int start=vertices.Count;vertices.AddRange(v);for(int j=1;j<v.Length-1;j++){triangles.Add(start);triangles.Add(start+j);triangles.Add(start+j+1);}}
            public void Polygon(Vector3[] points)
            {
                // Wear has genuinely concave torn edges. Ear clipping preserves the notches;
                // a triangle fan would fill them and turn the paint loss back into a regular badge.
                int start=vertices.Count;vertices.AddRange(points);Vector3 normal=Vector3.zero;
                for(int i=0;i<points.Length;i++)normal+=Vector3.Cross(points[i],points[(i+1)%points.Length]);
                int axis=Mathf.Abs(normal.x)>Mathf.Abs(normal.y)?0:1;if(Mathf.Abs(normal.z)>Mathf.Abs(normal[axis]))axis=2;
                Vector2 Project(Vector3 p)=>axis==0?new Vector2(p.z,p.y):axis==1?new Vector2(p.x,p.z):new Vector2(p.x,p.y);
                var p2=points.Select(Project).ToArray();float area=0;
                for(int i=0;i<p2.Length;i++)area+=Cross(p2[i],p2[(i+1)%p2.Length]);
                float sign=area>=0?1:-1;var remaining=Enumerable.Range(0,points.Length).ToList();
                while(remaining.Count>3)
                {
                    bool clipped=false;
                    for(int k=0;k<remaining.Count;k++)
                    {
                        int a=remaining[(k+remaining.Count-1)%remaining.Count],b=remaining[k],c=remaining[(k+1)%remaining.Count];
                        if(Cross(p2[b]-p2[a],p2[c]-p2[b])*sign<=.0000001f)continue;
                        bool occupied=false;
                        foreach(int n in remaining)
                        {
                            if(n==a||n==b||n==c)continue;
                            if(Cross(p2[b]-p2[a],p2[n]-p2[a])*sign>=-.0000001f&&Cross(p2[c]-p2[b],p2[n]-p2[b])*sign>=-.0000001f&&Cross(p2[a]-p2[c],p2[n]-p2[c])*sign>=-.0000001f){occupied=true;break;}
                        }
                        if(occupied)continue;triangles.Add(start+a);triangles.Add(start+b);triangles.Add(start+c);remaining.RemoveAt(k);clipped=true;break;
                    }
                    if(!clipped)throw new InvalidOperationException("Non-simple wear polygon; correct the authored paint contour.");
                }
                if(remaining.Count==3){triangles.Add(start+remaining[0]);triangles.Add(start+remaining[1]);triangles.Add(start+remaining[2]);}
            }
            static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
            public Mesh Build(string name){var m=new Mesh{name=name};m.SetVertices(vertices);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();m.uv=vertices.Select(v=>new Vector2(v.x+v.z*.2f,v.y+v.z*.14f)).ToArray();return m;}
        }
    }
}
