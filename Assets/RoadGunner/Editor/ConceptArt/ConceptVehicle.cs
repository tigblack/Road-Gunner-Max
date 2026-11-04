using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    /// <summary>Visual-only replacement. Physics and every functional weapon transform are retained.</summary>
    public static class ConceptVehicle
    {
        const string Prefix = "Concept · ";
        static Mesh tireLug;
        static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);
        static Quaternion Q(float x,float y,float z) => Quaternion.Euler(x,y,z);

        public static void Apply(GameObject root)
        {
            var car=root.GetComponent<VehicleController>();
            var gun=root.GetComponent<TurretController>();
            var harpoon=root.GetComponent<HarpoonCollector>();
            if(!car||!gun||!gun.yaw||!gun.pitch||!gun.muzzle||!harpoon||!harpoon.pivot||!harpoon.spawnPoint)
                throw new InvalidOperationException("ConceptVehicle needs the existing car, roof gun and companion harpoon references.");
            // Delete only assemblies previously made by this authoring pass. The original hierarchy,
            // including its wheel roots, aiming pivots and muzzle transforms, remains intact.
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith(Prefix)).ToArray())
                if(t && !t.parent.name.StartsWith(Prefix)) Object.DestroyImmediate(t.gameObject);
            var trackRoot = root.transform.Find("Player tracked visuals");
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if(!trackRoot || !r.transform.IsChildOf(trackRoot)) r.enabled=false;
            var visual=root.transform.Find("Replaceable visuals");
            if(!visual)throw new InvalidOperationException("Missing replaceable visual root.");
            // Existing hero meshes use ground-space coordinates under a -0.481m visual offset.
            // Keep that transform and the controller's wheel/collider geometry unchanged.
            var body=Group("Body and open cargo bed",visual);
            Body(body);
            Bake(body,"Hero_Body");
            var bodyRenderer=body.GetComponentInChildren<MeshRenderer>(true);
            var driver=Group("Cabin driver",visual);Driver(driver);Bake(driver,"Hero_Driver");
            var mechanic=Group("Hunched harpoon mechanic",visual);Mechanic(mechanic);Bake(mechanic,"Hero_Mechanic");
            for(int i=0;i<car.wheelVisuals.Length;i++)
            {
                if(!car.wheelVisuals[i])throw new InvalidOperationException("Missing original wheel visual root "+i);
                var wheel=Group("Off-road tire "+i,car.wheelVisuals[i]);
                Wheel(wheel,i%2==0?-1:1);Bake(wheel,"Hero_Wheel_"+(i%2==0?"Left":"Right"));
            }
            RoofGun(gun);
            Harpoon(harpoon);
            if(harpoon.cable)
            {
                harpoon.cable.sharedMaterial=ArtKit.Mat("SteelLight");
                harpoon.cable.widthMultiplier=.042f;
            }
            var health=root.GetComponent<VehicleHealth>();
            if(health){health.bodyPanels=new Renderer[]{bodyRenderer};EditorUtility.SetDirty(health);}
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(t.name.StartsWith(Prefix))Layer(t,root.layer);
            EditorUtility.SetDirty(car);EditorUtility.SetDirty(gun);EditorUtility.SetDirty(harpoon);
        }

        static Transform Group(string name,Transform parent)
        {return ArtKit.Node(Prefix+name,parent).transform;}
        static void Bake(Transform root,string name)
        {
            // Preserve the Finished shared visuals lookup used by existing integration checks.
            // The atlas baker only removes our newly authored mesh children inside this wrapper.
            var finished=ArtKit.Node("Finished shared visuals",root).transform;
            foreach(Transform child in root.Cast<Transform>().ToArray())if(child!=finished)child.SetParent(finished,false);
            ConceptEnemies.BakeAtlas(finished,name);
        }
        static void Layer(Transform t,int layer){t.gameObject.layer=layer;foreach(Transform c in t)Layer(c,layer);}
        static Transform B(Transform p,string n,Vector3 at,Vector3 size,string mat,float b=.035f,Quaternion? q=null)
            => ArtKit.Box(p,n,at,size,mat,b,q);
        static Transform C(Transform p,string n,Vector3 at,float r,float h,string mat,Quaternion? q=null,int seg=16)
            => ArtKit.Cylinder(p,n,at,r,h,mat,q,seg);
        static Transform E(Transform p,string n,Vector3 at,Vector3 size,string mat,int seg=12,int rings=8)
            => ArtKit.Ellipsoid(p,n,at,size,mat,seg,rings);
        static void Beam(Transform p,string n,Vector3 a,Vector3 b,float w,string mat)
            => ArtKit.Beam(p,n,a,b,w,mat);

        static void Body(Transform p)
        {
            // Sculpted muscle-interceptor widebody lower hull with aggressive coke-bottle hip flare
            // and tapered predator prow.
            Loft(p,"Sculpted muscle-car lower hull",new[]{
                new Section(-2.22f,.88f,.44f,.80f),new Section(-1.72f,.96f,.42f,.82f),
                new Section(-.72f,.98f,.42f,.83f),new Section(-.52f,1.02f,.42f,1.08f),new Section(.82f,1.02f,.42f,1.15f),
                new Section(2.38f,.97f,.45f,1.04f),new Section(2.84f,.84f,.50f,.90f)},"Teal");
            B(p,"Deep central transmission and armored belly pan",V(0,.40f,.05f),V(1.44f,.26f,4.35f),"Steel",.07f);
            B(p,"Front angled armored skid plate",V(0,.52f,2.62f),V(1.38f,.14f,.85f),"SteelLight",.04f,Q(-18,0,0));

            // Contoured muscle-car bonnet with twin Ivory & Gold racing stripes.
            Loft(p,"Long raised bonnet",new[]{
                new Section(.82f,.86f,1.03f,1.21f),new Section(1.40f,.88f,1.02f,1.19f),
                new Section(2.34f,.83f,.93f,1.11f),new Section(2.76f,.75f,.86f,1.00f)},"Teal");
            for(int s=-1;s<=1;s+=2)
            {
                var stripe=ArtKit.Node("Inset ivory bonnet stripe "+s,p).transform;stripe.localPosition=V(s*.29f,0,0);
                Loft(stripe,"Contoured ivory bonnet insert",new[]{
                    new Section(.90f,.135f,1.200f,1.218f),new Section(1.40f,.135f,1.182f,1.200f),
                    new Section(2.34f,.125f,1.102f,1.120f),new Section(2.68f,.115f,1.012f,1.030f)},"Ivory");
                var pin=ArtKit.Node("Gold racing pinstripe "+s,p).transform;pin.localPosition=V(s*.455f,0,0);
                Loft(pin,"Gold pinstripe",new[]{
                    new Section(.94f,.028f,1.198f,1.214f),new Section(1.40f,.028f,1.180f,1.196f),
                    new Section(2.34f,.026f,1.100f,1.116f),new Section(2.64f,.024f,1.015f,1.028f)},"Yellow");
            }

            // Supercharger V8 blower assembly rising out of the hood with red bugcatcher scoop & zoomie headers!
            B(p,"Hoodreinforced blower collar",V(0,1.21f,1.52f),V(.74f,.10f,.82f),"Steel",.035f);
            B(p,"Supercharger lower manifold",V(0,1.30f,1.52f),V(.58f,.16f,.66f),"SteelLight",.04f);
            for(int i=0;i<5;i++)
                B(p,"Supercharger cooling fin "+i,V(0,1.33f,1.26f+i*.13f),V(.64f,.12f,.032f),"Steel",.008f);
            C(p,"Blower left rotor lobe",V(-.13f,1.42f,1.52f),.125f,.62f,"SteelLight",Q(90,0,0),14);
            C(p,"Blower right rotor lobe",V(.13f,1.42f,1.52f),.125f,.62f,"SteelLight",Q(90,0,0),14);
            B(p,"Blower front drive cover",V(0,1.36f,1.86f),V(.44f,.28f,.07f),"Steel",.025f);
            C(p,"Blower upper pulley",V(0,1.43f,1.91f),.095f,.05f,"Yellow",Q(90,0,0),14);
            C(p,"Blower lower pulley",V(0,1.24f,1.91f),.085f,.05f,"SteelLight",Q(90,0,0),14);
            B(p,"Blower drive belt",V(0,1.335f,1.91f),V(.16f,.22f,.035f),"Rubber",.01f);
            // Bugcatcher intake scoop
            B(p,"Bugcatcher scoop housing",V(0,1.57f,1.50f),V(.52f,.15f,.48f),"SteelLight",.04f,Q(-4,0,0));
            B(p,"Bugcatcher red throat",V(0,1.57f,1.745f),V(.44f,.10f,.03f),"Red",.012f,Q(-4,0,0));
            for(int i=-1;i<=1;i++)
            {
                C(p,"Butterfly intake valve "+i,V(i*.135f,1.57f,1.755f),.048f,.02f,"Yellow",Q(90,0,0),10);
            }
            // Four swept zoomie exhaust headers per side snaking out of the bonnet
            for(int s=-1;s<=1;s+=2)
            {
                for(int i=0;i<4;i++)
                {
                    float z=1.18f+i*.19f;
                    Beam(p,"Hood header pipe "+i,V(s*.30f,1.22f,z),V(s*.94f,1.06f,z-.06f),.062f,"SteelLight");
                    C(p,"Header brass flange "+i,V(s*.32f,1.22f,z),.042f,.035f,"Yellow",Q(0,0,s*65),8);
                }
                Beam(p,"Side collector manifold",V(s*.95f,1.03f,1.02f),V(s*1.03f,.62f,.55f),.095f,"Rust");
                Beam(p,"Low armored side exhaust",V(s*1.04f,.58f,-.82f),V(s*1.04f,.58f,.85f),.115f,"SteelLight");
                B(p,"Side exhaust perforated heat shield",V(s*1.06f,.59f,.02f),V(.04f,.13f,1.15f),"Steel",.015f);
                C(p,"Side exhaust outlet",V(s*1.04f,.58f,-.87f),.082f,.18f,"Steel",Q(90,0,0));
                // Bolted widebody flank armor plates & yellow hazard accents
                B(p,"Lower rocker armor rail",V(s*.98f,.69f,.08f),V(.11f,.16f,1.55f),"TealDark",.03f);
                B(p,"Front fender hazard plate",V(s*.97f,.96f,2.22f),V(.06f,.22f,.42f),"Yellow",.02f,Q(0,s*4,0));
                B(p,"Front fender hazard stripe",V(s*1.002f,.96f,2.22f),V(.018f,.08f,.36f),"Steel",.008f,Q(0,s*4,0));
            }

            // Open cargo conversion: recessed armored floor surrounded by reinforced bed walls & roll-cage sponsons.
            B(p,"Recessed pickup bed floor",V(0,.84f,-1.43f),V(1.56f,.11f,1.42f),"Steel",.045f);
            for(int i=0;i<6;i++)B(p,"Cargo floor pressed rib",V(-.62f+i*.25f,.905f,-1.44f),V(.032f,.028f,1.30f),"SteelLight",.006f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Cargo quarter body",V(s*.88f,1.03f,-1.45f),V(.18f,.44f,1.54f),"Teal",.06f);
                B(p,"Worn cargo top rail",V(s*.89f,1.255f,-1.44f),V(.21f,.08f,1.58f),"Ivory",.025f);
                B(p,"Cargo side external tool/ammo sponson",V(s*1.00f,1.05f,-1.38f),V(.11f,.26f,.78f),"TealDark",.03f);
                B(p,"Cargo sponson yellow latch",V(s*1.06f,1.06f,-1.38f),V(.025f,.10f,.18f),"Yellow",.008f);
                B(p,"Pickup rear corner armor",V(s*.92f,1.045f,-2.10f),V(.24f,.52f,.25f),"IvoryShade",.045f);
                B(p,"Rear tail lamp backing",V(s*.79f,1.00f,-2.23f),V(.28f,.34f,.07f),"Steel",.025f);
                B(p,"Rear red tail lamp",V(s*.79f,1.06f,-2.27f),V(.18f,.20f,.04f),"Red",.02f);
                B(p,"Glowing brake light core",V(s*.79f,1.06f,-2.292f),V(.11f,.11f,.02f),"Glow",.008f);
                B(p,"Amber tail reflector",V(s*.79f,.895f,-2.27f),V(.18f,.06f,.035f),"Yellow",.009f);
            }
            B(p,"Stamped ivory tailgate",V(0,1.02f,-2.18f),V(1.48f,.44f,.11f),"Ivory",.045f);
            B(p,"Tailgate teal inset",V(0,1.04f,-2.24f),V(1.18f,.22f,.022f),"Teal",.018f);
            B(p,"Tailgate hazard badge",V(0,1.04f,-2.255f),V(.38f,.09f,.02f),"Yellow",.008f);
            B(p,"Tailgate handle",V(0,1.195f,-2.255f),V(.28f,.06f,.04f),"Steel",.01f);
            B(p,"Rear heavy twin-tube bumper",V(0,.66f,-2.31f),V(2.14f,.21f,.19f),"Steel",.055f);
            for(int s=-1;s<=1;s+=2)
            {
                Beam(p,"Rear bumper return",V(s*.96f,.66f,-2.31f),V(s*1.02f,.70f,-1.98f),.14f,"RustDark");
                Spike(p,"Rear bumper corner spike",V(s*.98f,.68f,-2.38f),V(s*1.04f,.72f,-2.56f),.075f,"SteelLight");
            }

            Cabin(p);

            // Arches sit outside the hull. Their circular inner cutout leaves the complete tire/track
            // silhouette legible while allowing the original suspension movement.
            foreach(float z in new[]{1.71f,-1.34f})for(int s=-1;s<=1;s+=2)
            {
                Arch(p,"Pressed teal off-road wheel arch",s,z,.55f,.535f,.635f,"Teal",.145f);
                Arch(p,"Worn exposed arch lip",s,z,.55f,.530f,.565f,"Ivory",.16f);
                B(p,"Mud flap",V(s*1.09f,.47f,z-.48f),V(.32f,.38f,.055f),"Rubber",.018f,Q(-8,0,0));
                B(p,"Mud flap steel weight",V(s*1.09f,.31f,z-.50f),V(.28f,.06f,.065f),"SteelLight",.01f,Q(-8,0,0));
            }

            // Aggressive predator grille, quad glowing headlights, and V-wedge "Shark-Jaw" cowcatcher ram.
            B(p,"Recessed dark front grille",V(0,.80f,2.82f),V(1.58f,.32f,.08f),"Steel",.035f);
            for(int i=0;i<7;i++)B(p,"Vertical predator grille tooth",V(-.60f+i*.20f,.80f,2.87f),V(.052f,.24f,.042f),"SteelLight",.01f);
            B(p,"Grille central badge",V(0,.82f,2.895f),V(.22f,.14f,.035f),"Yellow",.012f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Ivory headlight bezel",V(s*.68f,.94f,2.76f),V(.38f,.24f,.12f),"Ivory",.055f);
                C(p,"Outer glowing headlamp",V(s*.75f,.94f,2.83f),.075f,.035f,"Glow",Q(90,0,0),12);
                C(p,"Inner glowing headlamp",V(s*.59f,.94f,2.83f),.062f,.035f,"Glow",Q(90,0,0),12);
                Beam(p,"Headlamp protective cage bar",V(s*.52f,.94f,2.855f),V(s*.84f,.94f,2.855f),.024f,"Steel");
                Beam(p,"Ram chassis lower brace",V(s*.64f,.54f,2.30f),V(s*.74f,.66f,3.04f),.18f,"Steel");
                Beam(p,"Ram upper bull-bar tube",V(s*.58f,1.02f,2.68f),V(s*.42f,1.06f,3.08f),.085f,"SteelLight");
            }
            Beam(p,"Bull-bar top crossbar",V(-.42f,1.06f,3.08f),V(.42f,1.06f,3.08f),.085f,"SteelLight");

            // V-Wedge Cowcatcher Ram
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Angled V-plow wing "+s,V(s*.56f,.72f,3.08f),V(1.18f,.36f,.28f),"Steel",.07f,Q(0,s*14,0));
                B(p,"Plow yellow hazard chevron "+s,V(s*.58f,.74f,3.22f),V(.88f,.11f,.035f),"Yellow",.012f,Q(0,s*14,0));
                B(p,"Ram welded end winglet "+s,V(s*1.08f,.78f,2.96f),V(.22f,.34f,.28f),"TealDark",.035f,Q(0,s*18,s*8));
                Spike(p,"Ram outer tusk "+s,V(s*.92f,.76f,3.10f),V(s*1.02f,.90f,3.44f),.095f,"Ivory");
                Spike(p,"Ram inner fang "+s,V(s*.44f,.74f,3.18f),V(s*.46f,.88f,3.52f),.105f,"SteelLight");
                // Front fender defensive shoulder spikes
                B(p,"Welded shoulder spike socket",V(s*1.05f,1.04f,2.02f),V(.20f,.14f,.24f),"Steel",.025f,Q(0,0,s*12));
                Spike(p,"Shoulder defensive spike",V(s*1.11f,1.08f,2.02f),V(s*1.35f,1.32f,2.08f),.095f,"Ivory");
            }
            B(p,"Plow central prow apex",V(0,.73f,3.21f),V(.46f,.38f,.32f),"SteelLight",.06f);
            Spike(p,"Plow central breaker horn",V(0,.76f,3.32f),V(0,.90f,3.62f),.11f,"Ivory");
            B(p,"Front winch drum housing",V(0,.94f,3.02f),V(.42f,.18f,.22f),"Red",.03f);
            C(p,"Front winch cable spool",V(0,.94f,3.04f),.075f,.26f,"SteelLight",Q(0,0,90),12);

            Wear(p);
            Cargo(p);
        }

        static void Cabin(Transform p)
        {
            // Low muscle-interceptor cab with external roll-cage, sun visor, roof searchlights & twin behind-cab turbo stacks!
            B(p,"Cabin dark interior",V(0,1.26f,.12f),V(1.42f,.34f,1.32f),"Steel",.065f);
            for(int s=-1;s<=1;s+=2)
            {
                var profile=new[]{new Vector2(-.63f,1.06f),new Vector2(-.62f,1.36f),new Vector2(.87f,1.36f),new Vector2(.99f,1.10f)};
                ExtrudeSide(p,"Muscle-car door",profile,s*.85f,.14f,"Teal");
                ExtrudeSide(p,"Ivory lower door armor",new[]{new Vector2(-.52f,.90f),new Vector2(-.48f,1.23f),new Vector2(.63f,1.23f),new Vector2(.76f,.99f)},s*.935f,.04f,"Ivory");
                B(p,"Door bolted yellow number plate",V(s*.962f,1.16f,.10f),V(.025f,.22f,.36f),"Yellow",.01f);
                Beam(p,"Raked windscreen pillar",V(s*.76f,1.31f,.95f),V(s*.65f,1.89f,.45f),.09f,"Ivory");
                Beam(p,"Rear cabin pillar",V(s*.79f,1.27f,-.62f),V(s*.65f,1.88f,-.52f),.11f,"TealLight");
                Beam(p,"Window lower trim",V(s*.85f,1.36f,-.57f),V(s*.83f,1.36f,.87f),.05f,"SteelLight");
                B(p,"Window armored mesh bar",V(s*.80f,1.58f,-.46f),V(.03f,.36f,.045f),"Steel",.008f,Q(8,0,s*-12));
                B(p,"Door handle",V(s*.97f,1.30f,-.33f),V(.04f,.04f,.24f),"Steel",.009f);
                B(p,"Side mirror",V(s*1.03f,1.46f,.69f),V(.25f,.14f,.18f),"Steel",.035f);
                B(p,"Side mirror glass",V(s*1.03f,1.47f,.594f),V(.18f,.09f,.022f),"Glass",.014f);

                // Twin vertical behind-cab turbo exhaust stacks!
                Beam(p,"Vertical stack lower bend",V(s*.78f,.88f,-.68f),V(s*.92f,1.25f,-.68f),.11f,"Steel");
                C(p,"Vertical chrome exhaust stack",V(s*.92f,1.76f,-.68f),.078f,1.08f,"SteelLight",null,14);
                C(p,"Exhaust perforated heat sleeve",V(s*.92f,1.62f,-.68f),.098f,.62f,"RustDark",null,14);
                C(p,"Stack top slash tip",V(s*.92f,2.32f,-.68f),.082f,.08f,"Steel",Q(-18,0,s*-10),12);
                C(p,"Stack dark bore",V(s*.92f,2.36f,-.68f),.062f,.02f,"Rubber",Q(-18,0,s*-10),10);
            }
            Loft(p,"Low streamlined roof",new[]{
                new Section(-.60f,.68f,1.83f,1.96f),new Section(-.28f,.74f,1.86f,1.99f),
                new Section(.40f,.69f,1.84f,1.96f),new Section(.55f,.64f,1.80f,1.91f)},"Teal");
            for(int s=-1;s<=1;s+=2)
                B(p,"Roof ivory racing stripe "+s,V(s*.26f,1.988f,-.04f),V(.24f,.032f,.92f),"Ivory",.015f);
            // Louvered windshield sun visor & roof rally lightbar with 4 glowing lamps!
            B(p,"Windshield sun visor",V(0,1.86f,.58f),V(1.42f,.065f,.24f),"TealDark",.02f,Q(14,0,0));
            Beam(p,"Roof lightbar mount bar",V(-.58f,1.98f,.42f),V(.58f,1.98f,.42f),.055f,"Steel");
            for(int i=0;i<4;i++)
            {
                float x=-.42f+i*.28f;
                B(p,"Rally searchlight housing "+i,V(x,2.04f,.44f),V(.18f,.14f,.14f),"Steel",.025f);
                B(p,"Rally searchlight glowing lens "+i,V(x,2.04f,.515f),V(.14f,.10f,.025f),"Glow",.01f);
            }
            // Small lower tinted windscreen strips retain a clear open view of the driver.
            Quad(p,"Lower dusty windshield",new[]{V(-.69f,1.36f,.89f),V(.69f,1.36f,.89f),V(.64f,1.60f,.68f),V(-.64f,1.60f,.68f)},"Glass");
            Beam(p,"Windscreen cross frame",V(-.68f,1.60f,.69f),V(.68f,1.60f,.69f),.048f,"SteelLight");
            B(p,"Rear cabin bulkhead",V(0,1.34f,-.60f),V(1.44f,.38f,.09f),"TealDark",.04f);
            for(int s=-1;s<=1;s+=2)
            {
                Beam(p,"Bed roll bar upright",V(s*.73f,1.03f,-.76f),V(s*.73f,1.84f,-.74f),.095f,"Steel");
                Beam(p,"Bed roll bar diagonal",V(s*.73f,1.84f,-.74f),V(s*.75f,1.11f,-1.40f),.082f,"SteelLight");
                B(p,"Roll bar yellow corner gusset",V(s*.63f,1.76f,-.74f),V(.16f,.14f,.05f),"Yellow",.012f);
            }
            Beam(p,"Bed roll bar upper crossbar",V(-.73f,1.84f,-.74f),V(.73f,1.84f,-.74f),.098f,"Steel");
        }

        static void Cargo(Transform p)
        {
            // Expressive battle-rig cargo: glowing plasma power cell, twin nitro bottles, ammo chest & harpoon pedestal.
            B(p,"Olive equipment chest",V(-.48f,1.10f,-1.04f),V(.50f,.36f,.44f),"IvoryShade",.045f);
            B(p,"Equipment chest yellow top stencil",V(-.48f,1.285f,-1.04f),V(.26f,.022f,.22f),"Yellow",.008f);
            B(p,"Equipment chest leather strap",V(-.48f,1.285f,-1.04f),V(.09f,.028f,.45f),"Cloth",.008f);
            // Glowing Mint Plasma Battery Core feeding the turret/harpoon
            B(p,"Turret power cell base",V(.02f,1.00f,-1.02f),V(.34f,.18f,.34f),"Steel",.03f);
            C(p,"Glowing mint plasma core",V(.02f,1.22f,-1.02f),.12f,.32f,"Mint",null,14);
            C(p,"Plasma core top cap",V(.02f,1.39f,-1.02f),.135f,.05f,"SteelLight",null,14);
            // Twin angled nitrous/fuel cylinders at right rear
            for(int i=0;i<2;i++)
            {
                float z=-1.68f-i*.24f;
                C(p,"Pressurized nitro tank "+i,V(.58f,1.18f,z),.105f,.52f,i==0?"Red":"Yellow",Q(0,0,18),12);
                C(p,"Nitro brass valve "+i,V(.50f,1.43f,z),.042f,.09f,"SteelLight",Q(0,0,18),8);
            }
            B(p,"Nitro tank bracket",V(.58f,1.14f,-1.80f),V(.24f,.08f,.54f),"Steel",.018f);
            B(p,"Spare parts toolbox",V(.18f,1.02f,-1.92f),V(.36f,.23f,.35f),"Red",.04f);
            B(p,"Toolbox handle",V(.18f,1.16f,-1.92f),V(.22f,.04f,.04f),"SteelLight",.008f);
            C(p,"Harpoon fixed deck pedestal",V(.33f,1.50f,-1.09f),.11f,1.16f,"Steel");
            C(p,"Harpoon pedestal reinforcing ring",V(.33f,1.42f,-1.09f),.14f,.12f,"Yellow");
            C(p,"Harpoon welded foot plate",V(.33f,.955f,-1.09f),.20f,.09f,"SteelLight");
            C(p,"Harpoon swiveling socket",V(.33f,2.045f,-1.09f),.17f,.14f,"RustDark");
        }

        static void Wear(Transform p)
        {
            HoodPatch(p,"Bonnet shoulder worn undercoat",new[]{new Vector2(.70f,1.43f),new Vector2(.773f,1.50f),new Vector2(.744f,1.85f),new Vector2(.682f,1.94f),new Vector2(.655f,1.69f)},"IvoryShade");
            HoodPatch(p,"Bonnet shoulder restrained rust",new[]{new Vector2(.733f,1.49f),new Vector2(.765f,1.54f),new Vector2(.734f,1.82f),new Vector2(.704f,1.86f),new Vector2(.698f,1.68f)},"RustDark",.010f);
            HoodPatch(p,"Nose edge exposed undercoat",new[]{new Vector2(-.68f,2.48f),new Vector2(-.53f,2.51f),new Vector2(-.52f,2.63f),new Vector2(-.64f,2.65f)},"IvoryShade");
            HoodPatch(p,"Nose edge muted oxidation",new[]{new Vector2(-.66f,2.50f),new Vector2(-.60f,2.52f),new Vector2(-.57f,2.62f),new Vector2(-.64f,2.64f)},"Rust",.010f);
            RoofPatch(p,"Roof right edge worn undercoat",new[]{new Vector2(.548f,-.20f),new Vector2(.615f,-.17f),new Vector2(.602f,.13f),new Vector2(.553f,.17f)},"IvoryShade");
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Cargo flank chipped plate",V(s*.975f,1.08f,-1.88f),V(.016f,.17f,.28f),"Rust",.012f,Q(0,0,s*4));
                for(int i=0;i<2;i++)C(p,"Door plate broad rivet",V(s*.985f,1.16f,-.35f+i*.75f),.026f,.026f,"SteelLight",Q(0,0,90),8);
            }
        }

        static void Wheel(Transform p,int side)
        {
            Quaternion axis=Q(0,0,90);
            ArtKit.Torus(p,"Rounded all-terrain tire carcass",Vector3.zero,.463f,.245f,.395f,"Rubber",axis,24);
            C(p,"Broad reinforced rubber sidewall",Vector3.zero,.426f,.40f,"Rubber",axis,24);
            C(p,"Recessed steel rim",V(side*.205f,0,0),.265f,.035f,"Steel",axis,20);
            ArtKit.Torus(p,"Rim worn steel lip",V(side*.228f,0,0),.264f,.224f,.035f,"SteelLight",axis,20);
            C(p,"Hub axle",V(side*.249f,0,0),.097f,.055f,"RustDark",axis,12);
            for(int j=0;j<5;j++)
            {
                float a=j*Mathf.PI*2/5;Vector3 radial=V(0,Mathf.Cos(a),Mathf.Sin(a));
                Beam(p,"Rim heavy spoke",V(side*.235f,0,0)+radial*.092f,V(side*.235f,0,0)+radial*.212f,.063f,"SteelLight");
                C(p,"Hub fastener",V(side*.281f,0,0)+radial*.062f,.016f,.018f,"SteelLight",axis,6);
            }
            if(!tireLug)tireLug=BevelBlock(V(.176f,.036f,.082f),.009f);
            for(int j=0;j<22;j++)
            {
                float a=j*360f/22;
                for(int row=-1;row<=1;row+=2)
                {
                    Quaternion rot=Q(a+row*2,0,0);
                    Vector3 at=rot*V(row*.100f,.462f,0);
                    ArtKit.Part(p,"Contoured staggered tread block",tireLug,at,Vector3.one,"Rubber",rot*Q(0,row*17,0));
                }
            }
        }

        static void RoofGun(TurretController gun)
        {
            // Heavy Twin-Linked Autocannon Turret with angled ballistic shield, optic sensor & dual drum magazines!
            var yaw=Group("Roof gun protected bearing",gun.yaw);
            C(yaw,"Roof-seated steel swivel",V(0,-.18f,0),.285f,.16f,"Steel");
            C(yaw,"Turret gold bearing ring",V(0,-.11f,0),.305f,.055f,"Yellow");
            C(yaw,"Connected bearing neck",V(0,-.055f,0),.19f,.11f,"SteelLight");
            for(int s=-1;s<=1;s+=2)
            {
                B(yaw,"Gun trunnion cheek "+s,V(s*.18f,.08f,.16f),V(.085f,.28f,.44f),"Steel",.025f);
                // Angled V-wing ballistic gunner shield
                B(yaw,"Angled turret ballistic shield "+s,V(s*.25f,.19f,.32f),V(.24f,.38f,.075f),"Teal",.025f,Q(-10,s*22,0));
                B(yaw,"Shield ivory trim "+s,V(s*.26f,.29f,.325f),V(.20f,.09f,.082f),"Ivory",.015f,Q(-10,s*22,0));
                B(yaw,"Shield yellow chevron "+s,V(s*.26f,.14f,.33f),V(.16f,.06f,.085f),"Yellow",.01f,Q(-10,s*22,s*-15));
            }
            Bake(yaw,"Hero_RoofGunBearing");

            var pitch=Group("Automatic roof machine gun",gun.pitch);
            B(pitch,"Heavy twin-gun receiver",V(0,.01f,.04f),V(.32f,.26f,.68f),"Steel",.04f);
            B(pitch,"Receiver teal armor shroud",V(0,.155f,.08f),V(.28f,.085f,.52f),"TealDark",.02f);
            B(pitch,"Receiver ivory identification stripe",V(0,.198f,.08f),V(.14f,.022f,.42f),"Ivory",.008f);
            B(pitch,"Rear recoil buffer block",V(0,0,-.36f),V(.24f,.19f,.22f),"RustDark",.03f);
            for(int s=-1;s<=1;s+=2)
                C(pitch,"Recoil hydro-cylinder "+s,V(s*.09f,.09f,-.38f),.042f,.22f,"Yellow",Q(90,0,0),10);

            // Twin side-by-side heavy barrels with cooling shrouds & slotted muzzle brakes!
            for(int s=-1;s<=1;s+=2)
            {
                float bx=s*.075f;
                C(pitch,"Barrel perforated shroud "+s,V(bx,.01f,.72f),.062f,.78f,"Steel",Q(90,0,0),12);
                for(int i=0;i<4;i++)
                    C(pitch,"Barrel cooling collar "+s+"_"+i,V(bx,.01f,.42f+i*.165f),.074f,.045f,"SteelLight",Q(90,0,0),12);
                C(pitch,"Extended heavy barrel "+s,V(bx,.01f,1.32f),.042f,.48f,"SteelLight",Q(90,0,0),12);
                B(pitch,"Heavy muzzle brake "+s,V(bx,.01f,1.57f),V(.115f,.12f,.16f),"Steel",.022f);
                B(pitch,"Muzzle brake gold collar "+s,V(bx,.01f,1.48f),V(.125f,.13f,.04f),"Yellow",.01f);
                B(pitch,"Dark muzzle opening "+s,V(bx,.01f,1.652f),V(.065f,.065f,.012f),"Rubber",.006f);
            }
            B(pitch,"Barrel front yoke clamp",V(0,.01f,1.18f),V(.24f,.15f,.07f),"Steel",.018f);
            // Glowing Mint Holo-Sight & Target Acquisition Laser Pod
            B(pitch,"Holo sight base",V(0,.22f,-.02f),V(.12f,.05f,.18f),"Steel",.01f);
            B(pitch,"Holo sight frame",V(0,.28f,-.02f),V(.11f,.09f,.035f),"SteelLight",.008f);
            B(pitch,"Glowing mint holo reticle",V(0,.28f,-.02f),V(.075f,.06f,.018f),"Mint",.004f);
            C(pitch,"Side target laser pod",V(-.20f,.06f,.22f),.048f,.24f,"Steel",Q(90,0,0),10);
            C(pitch,"Glowing laser optic",V(-.20f,.06f,.345f),.036f,.02f,"Glow",Q(90,0,0),10);

            // Dual high-capacity side drum magazines + brass bullet belts
            for(int s=-1;s<=1;s+=2)
            {
                C(pitch,"Side ammo drum "+s,V(s*.26f,-.05f,-.02f),.155f,.22f,s>0?"IvoryShade":"Red",Q(0,0,90),16);
                C(pitch,"Ammo drum hub cap "+s,V(s*.375f,-.05f,-.02f),.085f,.03f,"Yellow",Q(0,0,90),12);
                for(int i=0;i<4;i++)
                    C(pitch,"Brass ammo belt cartridge "+s+"_"+i,V(s*(.16f+i*.035f),.075f,.04f),.020f,.13f,"Yellow",Q(90,0,0),8);
            }
            Bake(pitch,"Hero_RoofMachineGun");
        }

        static void Harpoon(HarpoonCollector h)
        {
            // Rear Pneumatic Grapnel Ballista with twin pressure cylinders, bow prod-arms & glowing grapnel spear!
            var pivot=Group("Rear mechanical harpoon launcher",h.pivot);
            B(pivot,"Harpoon launch cradle",V(0,-.055f,.45f),V(.24f,.15f,1.18f),"Steel",.025f);
            for(int s=-1;s<=1;s+=2)
            {
                Beam(pivot,"Twin upper guide rail "+s,V(s*.115f,.075f,.02f),V(s*.115f,.075f,1.30f),.058f,"SteelLight");
                // Pneumatic pressure cylinder along each flank
                C(pivot,"Pneumatic pressure cylinder "+s,V(s*.195f,-.02f,.38f),.058f,.68f,"Yellow",Q(90,0,0),12);
                C(pivot,"Cylinder chrome piston "+s,V(s*.195f,-.02f,.82f),.035f,.28f,"SteelLight",Q(90,0,0),10);
                // Swept ballista bow prod-arms at the front of the launcher
                Beam(pivot,"Ballista bow limb "+s,V(s*.14f,.04f,1.12f),V(s*.46f,.06f,.88f),.065f,"Teal");
                C(pivot,"Bow limb tension coil "+s,V(s*.18f,.05f,1.14f),.062f,.14f,"Yellow",null,10);
                Beam(pivot,"Bow string strut "+s,V(s*.45f,.06f,.88f),V(s*.08f,.08f,.52f),.022f,"SteelLight");
            }
            B(pivot,"Harpoon rear armored winch housing",V(0,-.02f,-.18f),V(.34f,.26f,.36f),"Steel",.04f);
            B(pivot,"Winch teal top service lid",V(0,.12f,-.18f),V(.26f,.035f,.28f),"Teal",.012f);
            C(pivot,"Winch glowing mint status gauge",V(0,.145f,-.18f),.055f,.02f,"Mint",null,12);
            C(pivot,"Visible cable winding drum",V(.20f,-.01f,-.13f),.135f,.15f,"SteelLight",Q(0,0,90),16);
            C(pivot,"Cable drum geared flange",V(.28f,-.01f,-.13f),.16f,.032f,"Yellow",Q(0,0,90),16);
            C(pivot,"Loaded harpoon shaft",V(0,.088f,.78f),.028f,1.18f,"IvoryShade",Q(90,0,0),8);
            // Multi-barbed grapnel head with glowing tip
            Spike(pivot,"Large readable harpoon arrowhead",V(0,.088f,1.25f),V(0,.088f,1.56f),.125f,"SteelLight");
            Spike(pivot,"Harpoon glowing energy tip",V(0,.088f,1.44f),V(0,.088f,1.62f),.065f,"Glow");
            for(int s=-1;s<=1;s+=2)
                Beam(pivot,"Harpoon rear barb "+s,V(0,.088f,1.42f),V(s*.14f,.088f,1.22f),.042f,"Ivory");
            B(pivot,"Harpoon receiver ivory service panel",V(-.195f,.025f,-.14f),V(.028f,.17f,.26f),"Ivory",.02f);
            Beam(pivot,"Operator upper handle",V(-.18f,-.04f,-.17f),V(-.32f,-.08f,-.36f),.055f,"Steel");
            Bake(pivot,"Hero_RearHarpoon");

            if(h.projectile)
            {
                // Existing moving projectile root remains +Z-facing and retains all state logic.
                var tip=Group("Flying harpoon head",h.projectile);
                Spike(tip,"Steel flying barb",V(0,0,-.16f),V(0,0,.25f),.105f,"SteelLight");
                Spike(tip,"Glowing flying tip",V(0,0,.08f),V(0,0,.31f),.058f,"Glow");
                for(int s=-1;s<=1;s+=2)
                    Beam(tip,"Flying grapnel fluke "+s,V(0,0,.10f),V(s*.13f,0,-.10f),.038f,"Ivory");
                C(tip,"Short flying shaft",V(0,0,-.22f),.025f,.22f,"IvoryShade",Q(90,0,0),8);
                Bake(tip,"Hero_HarpoonProjectile");
            }
        }

        static void Driver(Transform p)
        {
            // Expressive road-warrior driver with crested helm, glowing visor goggles & trailing scarf.
            B(p,"Driver seat",V(-.39f,1.16f,-.22f),V(.44f,.56f,.23f),"Rubber",.07f,Q(7,0,0));
            E(p,"Driver clothed torso",V(-.39f,1.37f,-.11f),V(.42f,.50f,.28f),"Cloth");
            E(p,"Driver head",V(-.39f,1.72f,-.04f),V(.25f,.29f,.25f),"Skin");
            E(p,"Driver battle helmet",V(-.39f,1.83f,-.05f),V(.30f,.18f,.30f),"TealDark");
            B(p,"Driver helmet gold mohawk crest",V(-.39f,1.93f,-.05f),V(.055f,.09f,.24f),"Yellow",.015f);
            B(p,"Driver goggle strap",V(-.39f,1.75f,.082f),V(.25f,.07f,.05f),"Steel",.015f);
            B(p,"Driver glowing visor lenses",V(-.39f,1.755f,.112f),V(.18f,.048f,.02f),"Glow",.01f);
            B(p,"Driver orange scarf",V(-.39f,1.58f,.01f),V(.33f,.13f,.28f),"Scarf",.04f,Q(0,8,0));
            B(p,"Driver trailing scarf tail",V(-.54f,1.56f,-.22f),V(.11f,.06f,.28f),"Scarf",.02f,Q(12,-18,0));
            Limb(p,"Driver left upper arm",V(-.60f,1.49f,-.08f),V(-.64f,1.35f,.18f),.125f,"Teal");
            Limb(p,"Driver left forearm",V(-.64f,1.35f,.18f),V(-.54f,1.38f,.39f),.108f,"Skin");
            Limb(p,"Driver right upper arm",V(-.19f,1.49f,-.07f),V(-.13f,1.33f,.16f),.125f,"Cloth");
            Limb(p,"Driver right forearm",V(-.13f,1.33f,.16f),V(-.27f,1.38f,.39f),.105f,"Skin");
            E(p,"Driver glove left",V(-.53f,1.38f,.40f),V(.115f,.105f,.125f),"Rubber",8,6);
            E(p,"Driver glove right",V(-.26f,1.38f,.40f),V(.115f,.105f,.125f),"Rubber",8,6);
            ArtKit.Torus(p,"Steering wheel",V(-.40f,1.37f,.43f),.19f,.155f,.034f,"Steel",Q(67,0,0),16);
        }

        static void Mechanic(Transform p)
        {
            // Hunched harpoon engineer with welding visor, glowing optic lens & backpack power-coil rig.
            B(p,"Mechanic left dusty boot",V(-.52f,1.00f,-1.74f),V(.18f,.18f,.33f),"Rubber",.05f,Q(0,-10,0));
            B(p,"Mechanic right dusty boot",V(-.10f,1.00f,-1.67f),V(.18f,.18f,.32f),"Rubber",.05f,Q(0,14,0));
            Limb(p,"Mechanic bent left shin",V(-.51f,1.06f,-1.70f),V(-.57f,1.32f,-1.52f),.16f,"Cloth");
            Limb(p,"Mechanic bent left thigh",V(-.57f,1.32f,-1.52f),V(-.32f,1.42f,-1.76f),.19f,"Cloth");
            Limb(p,"Mechanic bent right shin",V(-.10f,1.06f,-1.64f),V(.00f,1.30f,-1.43f),.16f,"Cloth");
            Limb(p,"Mechanic bent right thigh",V(.00f,1.30f,-1.43f),V(-.20f,1.42f,-1.72f),.19f,"Cloth");
            E(p,"Mechanic hunched torso",V(-.28f,1.67f,-1.62f),V(.49f,.54f,.37f),"Cloth",12,8).localRotation=Q(15,-24,-11);
            B(p,"Mechanic leather back vest",V(-.37f,1.70f,-1.81f),V(.42f,.38f,.08f),"TealDark",.07f,Q(15,-24,-11));
            B(p,"Mechanic backpack power pack",V(-.41f,1.74f,-1.88f),V(.26f,.26f,.11f),"Steel",.035f,Q(15,-24,-11));
            C(p,"Mechanic backpack glowing cell",V(-.43f,1.75f,-1.94f),.065f,.14f,"Mint",Q(105,-24,-11),10);
            Beam(p,"Mechanic back leather strap",V(-.55f,1.83f,-1.79f),V(-.17f,1.56f,-1.83f),.048f,"RustDark");
            E(p,"Mechanic wrapped orange scarf",V(-.18f,1.89f,-1.48f),V(.34f,.16f,.32f),"Scarf");
            B(p,"Mechanic scarf trailing end",V(-.37f,1.80f,-1.78f),V(.11f,.26f,.03f),"Scarf",.018f,Q(12,-10,-13));
            E(p,"Mechanic head leaning to harpoon",V(-.08f,2.02f,-1.43f),V(.27f,.30f,.27f),"Skin");
            E(p,"Mechanic worn steel helmet",V(-.08f,2.15f,-1.45f),V(.32f,.24f,.32f),"Steel");
            C(p,"Mechanic helmet rolled brow rim",V(-.08f,2.105f,-1.45f),.164f,.032f,"Yellow",null,16);
            B(p,"Mechanic goggles",V(.035f,2.04f,-1.335f),V(.23f,.08f,.07f),"Steel",.02f,Q(0,45,0));
            B(p,"Mechanic glowing goggle lens",V(.068f,2.045f,-1.304f),V(.15f,.05f,.028f),"Glow",.012f,Q(0,45,0));
            Limb(p,"Mechanic shoulder left",V(-.48f,1.83f,-1.50f),V(-.36f,1.78f,-1.16f),.145f,"Teal");
            Limb(p,"Mechanic hand left reaching",V(-.36f,1.78f,-1.16f),V(-.09f,2.08f,-.89f),.115f,"Skin");
            Limb(p,"Mechanic shoulder right",V(-.01f,1.83f,-1.55f),V(.20f,1.85f,-1.30f),.15f,"Cloth");
            Limb(p,"Mechanic hand right reaching",V(.20f,1.85f,-1.30f),V(.20f,2.15f,-1.00f),.11f,"Skin");
            E(p,"Mechanic dark glove left",V(-.09f,2.08f,-.89f),V(.13f,.11f,.13f),"Rubber",8,6);
            E(p,"Mechanic dark glove right",V(.20f,2.15f,-1.00f),V(.13f,.11f,.13f),"Rubber",8,6);
            B(p,"Mechanic utility belt",V(-.28f,1.43f,-1.64f),V(.45f,.11f,.38f),"RustDark",.04f,Q(0,-10,0));
            B(p,"Mechanic belt tool pouch",V(-.48f,1.42f,-1.56f),V(.16f,.21f,.19f),"Yellow",.035f);
        }
        static void Limb(Transform p,string n,Vector3 a,Vector3 b,float w,string mat)
        {
            var t=E(p,n,(a+b)*.5f,V(w,(b-a).magnitude+w*.36f,w),mat,10,6);
            t.rotation=p.rotation*Quaternion.FromToRotation(Vector3.up,b-a);
        }

        struct Section
        {
            public float z,w,b,t;
            public Section(float z,float w,float bottom,float top){this.z=z;this.w=w;b=bottom;t=top;}
        }
        static void Loft(Transform p,string name,Section[] s,string mat)
        {
            var mb=new MeshBuilder();var rings=new List<Vector3[]>();
            foreach(var a in s)
            {
                float h=a.t-a.b,bev=Mathf.Min(.075f,h*.23f);
                rings.Add(new[]{V(-a.w+.08f,a.b,a.z),V(a.w-.08f,a.b,a.z),V(a.w,a.b+bev,a.z),V(a.w,a.t-bev,a.z),V(a.w-.065f,a.t,a.z),V(-a.w+.065f,a.t,a.z),V(-a.w,a.t-bev,a.z),V(-a.w,a.b+bev,a.z)});
            }
            for(int j=0;j<rings.Count-1;j++)for(int k=0;k<8;k++)mb.Face(new[]{rings[j][k],rings[j][(k+1)%8],rings[j+1][(k+1)%8],rings[j+1][k]});
            mb.Face(rings[0].Reverse().ToArray());mb.Face(rings[rings.Count-1]);
            MeshPart(p,name,mb.Mesh(name),mat);
        }
        static void ExtrudeSide(Transform p,string name,Vector2[] zy,float x,float depth,string mat)
        {
            var mb=new MeshBuilder();var a=zy.Select(v=>V(x-depth*.5f,v.y,v.x)).ToArray();var b=zy.Select(v=>V(x+depth*.5f,v.y,v.x)).ToArray();
            mb.Face(a.Reverse().ToArray());mb.Face(b);
            for(int i=0;i<zy.Length;i++)mb.Face(new[]{a[i],a[(i+1)%zy.Length],b[(i+1)%zy.Length],b[i]});
            MeshPart(p,name,mb.Mesh(name),mat);
        }
        static void Arch(Transform p,string name,int side,float z,float cy,float inner,float outer,string mat,float depth=.19f)
        {
            var mb=new MeshBuilder();float x=side*1.04f;
            Action<Vector3[]> face=v=>mb.Face(side<0?v.Reverse().ToArray():v);
            for(int i=0;i<10;i++)
            {
                float a=Mathf.Lerp(-12,192,i/10f)*Mathf.Deg2Rad,b=Mathf.Lerp(-12,192,(i+1)/10f)*Mathf.Deg2Rad;
                Vector3 i0=V(x,cy+Mathf.Sin(a)*inner,z+Mathf.Cos(a)*inner),i1=V(x,cy+Mathf.Sin(b)*inner,z+Mathf.Cos(b)*inner);
                Vector3 o0=V(x,cy+Mathf.Sin(a)*outer,z+Mathf.Cos(a)*outer),o1=V(x,cy+Mathf.Sin(b)*outer,z+Mathf.Cos(b)*outer);
                Vector3 d=V(side*depth,0,0);
                face(new[]{i0+d,i1+d,o1+d,o0+d});face(new[]{o0,o1,i1,i0});
                face(new[]{o0,o0+d,o1+d,o1});face(new[]{i0,i1,i1+d,i0+d});
            }
            MeshPart(p,name,mb.Mesh(name+side+z+mat),mat);
        }
        static void Quad(Transform p,string name,Vector3[] v,string mat)
        {
            var mb=new MeshBuilder();mb.Face(v);mb.Face(v.Reverse().ToArray());MeshPart(p,name,mb.Mesh(name),mat);
        }
        static void HoodPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.007f)
        {
            Quad(p,name,xz.Select(v=>V(v.x,Profile(v.y,new[]{.82f,1.40f,2.34f,2.76f},new[]{1.21f,1.19f,1.11f,1.00f})+lift,v.y)).ToArray(),mat);
        }
        static void RoofPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.007f)
        {
            Quad(p,name,xz.Select(v=>V(v.x,Profile(v.y,new[]{-.60f,-.28f,.40f,.55f},new[]{1.96f,1.99f,1.96f,1.91f})+lift,v.y)).ToArray(),mat);
        }
        static float Profile(float x,float[] at,float[] height)
        {
            for(int i=1;i<at.Length;i++)if(x<=at[i])return Mathf.LerpUnclamped(height[i-1],height[i],Mathf.InverseLerp(at[i-1],at[i],x));
            return height[height.Length-1];
        }
        static void Spike(Transform p,string name,Vector3 a,Vector3 b,float r,string mat)
        {
            var d=(b-a).normalized;var x=Vector3.Cross(d,Mathf.Abs(d.y)>.9f?Vector3.right:Vector3.up).normalized*r;var y=Vector3.Cross(d,x).normalized*r;
            var mb=new MeshBuilder();Vector3[] ring={a+x,a+y,a-x,a-y};
            for(int i=0;i<4;i++)mb.Face(new[]{ring[i],ring[(i+1)%4],b});mb.Face(ring.Reverse().ToArray());MeshPart(p,name,mb.Mesh(name),mat);
        }
        static void MeshPart(Transform p,string n,Mesh m,string mat)
        {
            var t=ArtKit.Node(n,p);t.AddComponent<MeshFilter>().sharedMesh=m;
            var r=t.AddComponent<MeshRenderer>();r.sharedMaterial=ArtKit.Mat(mat);r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
        }
        static Mesh BevelBlock(Vector3 size,float bevel)
        {
            var mb=new MeshBuilder();Vector3 h=size*.5f;float b=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.75f);
            var axes=new[]{Vector3.right,Vector3.up,Vector3.forward};
            Action<Vector3[],Vector3> face=(points,normal)=>
            {
                if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0)Array.Reverse(points);
                mb.Face(points);
            };
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
            {
                Vector3 n=axes[axis]*sign,u=axes[(axis+1)%3],v=axes[(axis+2)%3];
                Vector3 center=n*h[axis];float hu=h[(axis+1)%3]-b,hv=h[(axis+2)%3]-b;
                face(new[]{center-u*hu-v*hv,center+u*hu-v*hv,center+u*hu+v*hv,center-u*hu+v*hv},n);
            }
            for(int edge=0;edge<3;edge++)for(int s=-1;s<=1;s+=2)for(int t=-1;t<=1;t+=2)
            {
                int a=(edge+1)%3,c=(edge+2)%3;Vector3 e=axes[edge]*(h[edge]-b);
                Vector3 pa=axes[a]*(s*h[a])+axes[c]*(t*(h[c]-b));
                Vector3 pc=axes[a]*(s*(h[a]-b))+axes[c]*(t*h[c]);
                face(new[]{pa-e,pa+e,pc+e,pc-e},axes[a]*s+axes[c]*t);
            }
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                face(new[]{V(x*h.x,y*(h.y-b),z*(h.z-b)),V(x*(h.x-b),y*h.y,z*(h.z-b)),V(x*(h.x-b),y*(h.y-b),z*h.z)},V(x,y,z));
            return mb.Mesh("Chamfered mobile tire lug");
        }
        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<int> triangles=new List<int>();
            public void Face(Vector3[] v)
            {
                int start=vertices.Count;vertices.AddRange(v);
                for(int i=1;i<v.Length-1;i++){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
            }
            public Mesh Mesh(string n)
            {
                var m=new Mesh{name="Hero_"+n};m.SetVertices(vertices);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();
                m.uv=vertices.Select(v=>new Vector2(v.x+v.z*.31f,v.y+v.z*.17f)).ToArray();return m;
            }
        }
    }
}
