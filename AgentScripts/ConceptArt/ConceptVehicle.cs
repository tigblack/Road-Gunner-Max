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
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))r.enabled=false;
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
                harpoon.cable.widthMultiplier=.035f;
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
            // Six intentionally shaped longitudinal stations create the muscle-car shoulders,
            // sloped long bonnet, tapered prow and narrower lower chassis.
            Loft(p,"Sculpted muscle-car lower hull",new[]{
                new Section(-2.15f,.83f,.46f,.78f),new Section(-1.7f,.90f,.43f,.79f),
                new Section(-.70f,.93f,.43f,.80f),new Section(-.55f,.94f,.43f,1.05f),new Section(.8f,.94f,.43f,1.13f),
                new Section(2.35f,.91f,.46f,1.02f),new Section(2.78f,.80f,.52f,.88f)},"Teal");
            B(p,"Deep central transmission and suspension",V(0,.41f,.05f),V(1.37f,.24f,4.2f),"Steel",.07f);
            // Hood is a single broad, gently profiled panel rather than stacked boxes.
            Loft(p,"Long raised bonnet",new[]{new Section(.84f,.82f,1.03f,1.19f),new Section(1.38f,.84f,1.02f,1.17f),new Section(2.32f,.80f,.93f,1.09f),new Section(2.70f,.73f,.87f,.99f)},"Teal");
            var stripe=ArtKit.Node("Inset ivory bonnet stripe",p).transform;stripe.localPosition=V(-.24f,0,0);
            Loft(stripe,"Contoured ivory bonnet insert",new[]{new Section(1.0f,.175f,1.190f,1.201f),new Section(1.38f,.175f,1.176f,1.187f),new Section(2.32f,.175f,1.096f,1.107f),new Section(2.58f,.175f,1.028f,1.039f)},"Ivory");
            B(p,"Hood charcoal intake",V(.06f,1.235f,1.05f),V(.55f,.12f,.45f),"Steel",.035f);
            for(int i=0;i<3;i++)B(p,"Broad intake louver",V(.06f,1.295f,.93f+i*.105f),V(.43f,.021f,.038f),"SteelLight",.006f);
            // Open cargo conversion: a recessed floor surrounded by relatively thin body walls.
            B(p,"Recessed pickup bed floor",V(0,.84f,-1.43f),V(1.53f,.11f,1.39f),"Steel",.045f);
            for(int i=0;i<6;i++)B(p,"Cargo floor pressed rib",V(-.62f+i*.25f,.905f,-1.44f),V(.027f,.025f,1.28f),"SteelLight",.006f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Cargo quarter body",V(s*.86f,1.02f,-1.45f),V(.16f,.42f,1.5f),"Teal",.06f);
                B(p,"Worn cargo top rail",V(s*.87f,1.235f,-1.44f),V(.19f,.075f,1.56f),"Ivory",.025f);
                B(p,"Pickup rear corner armor",V(s*.9f,1.035f,-2.08f),V(.22f,.50f,.23f),"IvoryShade",.045f);
                B(p,"Rear tail lamp backing",V(s*.78f,.99f,-2.215f),V(.24f,.31f,.06f),"Steel",.025f);
                B(p,"Rear red tail lamp",V(s*.78f,1.04f,-2.252f),V(.15f,.19f,.036f),"Red",.02f);
                B(p,"Amber tail reflector",V(s*.78f,.895f,-2.25f),V(.15f,.055f,.035f),"Yellow",.009f);
                Beam(p,"Low side exhaust",V(s*1.02f,.55f,-.75f),V(s*1.02f,.55f,.90f),.105f,"SteelLight");
                C(p,"Exhaust outlet",V(s*1.02f,.56f,-.79f),.075f,.16f,"Steel",Q(90,0,0));
            }
            B(p,"Stamped ivory tailgate",V(0,1.015f,-2.17f),V(1.44f,.43f,.10f),"Ivory",.045f);
            B(p,"Tailgate teal inset",V(0,1.035f,-2.23f),V(1.15f,.19f,.018f),"Teal",.018f);
            B(p,"Tailgate handle",V(0,1.19f,-2.25f),V(.25f,.055f,.035f),"Steel",.01f);
            B(p,"Rear protection beam",V(0,.66f,-2.29f),V(2.08f,.18f,.17f),"Steel",.055f);
            for(int s=-1;s<=1;s+=2)Beam(p,"Rear bumper return",V(s*.94f,.66f,-2.29f),V(s*.99f,.69f,-1.98f),.13f,"RustDark");
            Cabin(p);
            // Arches sit outside the hull. Their circular inner cutout leaves the complete tire
            // silhouette legible while allowing the original suspension movement.
            foreach(float z in new[]{1.71f,-1.34f})for(int s=-1;s<=1;s+=2)
            {
                Arch(p,"Pressed teal off-road wheel arch",s,z,.54f,.535f,.615f,"Teal",.125f);
                Arch(p,"Worn exposed arch lip",s,z,.54f,.530f,.550f,"Rust",.14f);
                B(p,"Mud flap",V(s*1.08f,.47f,z-.47f),V(.30f,.37f,.055f),"Rubber",.018f,Q(-8,0,0));
            }
            // The front stays long and low: inset grille, two broad headlights, heavy ram.
            B(p,"Recessed dark front grille",V(0,.79f,2.787f),V(1.54f,.30f,.07f),"Steel",.035f);
            for(int i=0;i<7;i++)B(p,"Horizontal grille teeth",V(-.62f+i*.205f,.79f,2.835f),V(.043f,.20f,.035f),"SteelLight",.009f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Ivory headlight bezel",V(s*.68f,.93f,2.735f),V(.34f,.22f,.11f),"Ivory",.055f);
                B(p,"Warm headlight",V(s*.68f,.94f,2.801f),V(.24f,.11f,.035f),"Glow",.027f);
                Beam(p,"Ram chassis brace",V(s*.64f,.55f,2.30f),V(s*.70f,.68f,3.01f),.17f,"Steel");
            }
            B(p,"Heavy beveled battering ram",V(0,.72f,3.01f),V(2.17f,.33f,.32f),"Steel",.09f);
            B(p,"Ram dirty ivory impact strip",V(0,.76f,3.187f),V(1.97f,.12f,.035f),"IvoryShade",.016f);
            for(int s=-1;s<=1;s+=2)
            {
                B(p,"Ram compact welded end shoe",V(s*1.02f,.75f,3.07f),V(.22f,.29f,.23f),"Steel",.035f,Q(0,0,s*8));
                B(p,"Ram end shoe broad edge oxidation",V(s*1.02f,.83f,3.195f),V(.16f,.07f,.018f),"RustDark",.008f,Q(0,0,s*8));
                Spike(p,"Ram tow tooth",V(s*.68f,.80f,3.145f),V(s*.70f,.88f,3.40f),.085f,"SteelLight");
                // A few large defensive spikes, purposefully limited to the front shoulders.
                B(p,"Welded shoulder spike socket",V(s*1.04f,1.02f,2.02f),V(.18f,.13f,.21f),"Steel",.025f,Q(0,0,s*12));
                Spike(p,"Shoulder defensive spike",V(s*1.10f,1.065f,2.02f),V(s*1.29f,1.27f,2.07f),.085f,"SteelLight");
            }
            Wear(p);
            Cargo(p);
        }

        static void Cabin(Transform p)
        {
            // The concept's cabin is low, with a sloping windscreen and side windows left open.
            B(p,"Cabin dark interior",V(0,1.26f,.12f),V(1.40f,.34f,1.31f),"Steel",.065f);
            for(int s=-1;s<=1;s+=2)
            {
                var profile=new[]{new Vector2(-.63f,1.06f),new Vector2(-.62f,1.36f),new Vector2(.87f,1.36f),new Vector2(.99f,1.10f)};
                ExtrudeSide(p,"Muscle-car door",profile,s*.84f,.13f,"Teal");
                ExtrudeSide(p,"Ivory lower door armor",new[]{new Vector2(-.52f,.90f),new Vector2(-.48f,1.22f),new Vector2(.63f,1.22f),new Vector2(.76f,.99f)},s*.924f,.035f,"Ivory");
                Beam(p,"Raked windscreen pillar",V(s*.76f,1.31f,.95f),V(s*.65f,1.89f,.45f),.085f,"Ivory");
                Beam(p,"Rear cabin pillar",V(s*.79f,1.27f,-.62f),V(s*.65f,1.88f,-.52f),.10f,"TealLight");
                Beam(p,"Window lower trim",V(s*.84f,1.36f,-.57f),V(s*.82f,1.36f,.87f),.045f,"SteelLight");
                B(p,"Door handle",V(s*.96f,1.30f,-.33f),V(.035f,.035f,.23f),"Steel",.009f);
                B(p,"Side mirror",V(s*1.01f,1.45f,.69f),V(.24f,.13f,.17f),"Steel",.035f);
                B(p,"Side mirror glass",V(s*1.01f,1.46f,.594f),V(.17f,.08f,.021f),"Glass",.014f);
            }
            Loft(p,"Low streamlined roof",new[]{new Section(-.59f,.66f,1.83f,1.95f),new Section(-.28f,.72f,1.86f,1.98f),new Section(.40f,.67f,1.84f,1.95f),new Section(.53f,.62f,1.80f,1.90f)},"Teal");
            B(p,"Roof ivory armor insert",V(-.32f,1.986f,-.03f),V(.39f,.035f,.76f),"Ivory",.023f);
            // Small lower tinted windscreen strips retain a clear open view of the driver.
            Quad(p,"Lower dusty windshield",new[]{V(-.69f,1.36f,.89f),V(.69f,1.36f,.89f),V(.64f,1.60f,.68f),V(-.64f,1.60f,.68f)},"Glass");
            Beam(p,"Windscreen cross frame",V(-.68f,1.60f,.69f),V(.68f,1.60f,.69f),.045f,"SteelLight");
            B(p,"Rear cabin bulkhead",V(0,1.34f,-.60f),V(1.44f,.38f,.09f),"TealDark",.04f);
            for(int s=-1;s<=1;s+=2)
            {
                Beam(p,"Bed roll bar upright",V(s*.72f,1.03f,-.74f),V(s*.72f,1.80f,-.73f),.085f,"Steel");
                Beam(p,"Bed roll bar diagonal",V(s*.72f,1.80f,-.73f),V(s*.74f,1.11f,-1.34f),.075f,"SteelLight");
            }
            Beam(p,"Bed roll bar upper crossbar",V(-.72f,1.80f,-.73f),V(.72f,1.80f,-.73f),.09f,"Steel");
        }

        static void Cargo(Transform p)
        {
            // Few substantial readable cargo pieces leave the mechanic and stand visible.
            B(p,"Olive equipment chest",V(-.47f,1.10f,-1.04f),V(.50f,.35f,.43f),"IvoryShade",.045f);
            B(p,"Equipment chest leather strap",V(-.47f,1.282f,-1.04f),V(.09f,.025f,.44f),"Cloth",.008f);
            Jerrycan(p,V(.54f,1.20f,-1.89f),"Red",.72f);
            B(p,"Spare parts toolbox",V(.20f,1.02f,-1.92f),V(.34f,.22f,.34f),"Steel",.04f);
            C(p,"Harpoon fixed deck pedestal",V(.33f,1.50f,-1.09f),.10f,1.16f,"Steel");
            C(p,"Harpoon welded foot plate",V(.33f,.955f,-1.09f),.18f,.085f,"SteelLight");
            C(p,"Harpoon swiveling socket",V(.33f,2.045f,-1.09f),.16f,.13f,"RustDark");
        }
        static void Jerrycan(Transform p,Vector3 pos,string mat,float scale)
        {
            var g=ArtKit.Node("Cargo jerrycan",p).transform;g.localPosition=pos;g.localScale=Vector3.one*scale;
            B(g,"Rounded pressed fuel can",V(0,0,0),V(.34f,.47f,.22f),mat,.045f);
            B(g,"Fuel can top shoulder",V(0,.25f,0),V(.30f,.08f,.21f),mat,.025f);
            Beam(g,"Fuel handle left",V(-.09f,.27f,0),V(-.09f,.36f,0),.035f,"Steel");
            Beam(g,"Fuel handle right",V(.07f,.27f,0),V(.07f,.36f,0),.035f,"Steel");
            Beam(g,"Fuel handle grip",V(-.09f,.36f,0),V(.07f,.36f,0),.035f,"Steel");
            C(g,"Fuel cap",V(.115f,.302f,0),.039f,.03f,"Steel");
            Beam(g,"Fuel pressed diagonal",V(-.10f,-.12f,-.118f),V(.1f,.13f,-.118f),.022f,"RustDark");
            Beam(g,"Fuel pressed diagonal",V(.10f,-.12f,-.118f),V(-.1f,.13f,-.118f),.022f,"RustDark");
        }

        static void Wear(Transform p)
        {
            // Broad hand-placed patches: no high-frequency scratch texture or noisy grunge.
            // Wear hugs manufactured edges and joins; broad tonal undercoat appears around a
            // few smaller oxidized centers instead of isolated orange polygon stickers.
            HoodPatch(p,"Bonnet shoulder worn undercoat",new[]{new Vector2(.70f,1.43f),new Vector2(.773f,1.50f),new Vector2(.744f,1.85f),new Vector2(.682f,1.94f),new Vector2(.655f,1.69f)},"IvoryShade");
            HoodPatch(p,"Bonnet shoulder restrained rust",new[]{new Vector2(.733f,1.49f),new Vector2(.765f,1.54f),new Vector2(.734f,1.82f),new Vector2(.704f,1.86f),new Vector2(.698f,1.68f)},"RustDark",.010f);
            HoodPatch(p,"Nose edge exposed undercoat",new[]{new Vector2(-.68f,2.48f),new Vector2(-.53f,2.51f),new Vector2(-.52f,2.63f),new Vector2(-.64f,2.65f)},"IvoryShade");
            HoodPatch(p,"Nose edge muted oxidation",new[]{new Vector2(-.66f,2.50f),new Vector2(-.60f,2.52f),new Vector2(-.57f,2.62f),new Vector2(-.64f,2.64f)},"Rust",.010f);
            HoodPatch(p,"Hood front lip chip",new[]{new Vector2(-.12f,2.65f),new Vector2(.17f,2.65f),new Vector2(.20f,2.685f),new Vector2(-.13f,2.685f)},"IvoryShade");
            HoodPatch(p,"Hood rear corner wear",new[]{new Vector2(.61f,1.03f),new Vector2(.74f,1.06f),new Vector2(.75f,1.13f),new Vector2(.66f,1.15f)},"RustDark");
            HoodPatch(p,"Ivory stripe edge chip",new[]{new Vector2(-.414f,1.72f),new Vector2(-.37f,1.75f),new Vector2(-.38f,1.84f),new Vector2(-.414f,1.87f)},"RustDark",.025f);
            RoofPatch(p,"Roof right edge worn undercoat",new[]{new Vector2(.548f,-.20f),new Vector2(.615f,-.17f),new Vector2(.602f,.13f),new Vector2(.553f,.17f)},"IvoryShade");
            RoofPatch(p,"Roof right edge warm oxidation",new[]{new Vector2(.584f,-.13f),new Vector2(.612f,-.11f),new Vector2(.597f,.12f),new Vector2(.573f,.10f)},"Rust",.010f);
            for(int s=-1;s<=1;s+=2)
            {
                float x=s*.948f;
                Quad(p,"Door lower edge oxidation",new[]{V(x,.97f,-.46f),V(x,1.045f,-.35f),V(x,1.025f,-.16f),V(x,.968f,-.10f)},"RustDark");
                Quad(p,"Door front seam chipped edge",new[]{V(x,1.19f,.54f),V(x,1.21f,.59f),V(x,1.08f,.64f),V(x,1.05f,.59f)},"Rust");
                B(p,"Cargo flank chipped plate",V(s*.953f,1.08f,-1.88f),V(.014f,.17f,.28f),"Rust",.012f,Q(0,0,s*4));
                B(p,"Front wing repair plate",V(s*.951f,.94f,2.45f),V(.035f,.19f,.30f),"Rust",.025f,Q(0,s*5,0));
                for(int i=0;i<2;i++)C(p,"Door plate broad rivet",V(s*.974f,1.16f,-.35f+i*.75f),.026f,.026f,"SteelLight",Q(0,0,90),8);
            }
        }

        static void Wheel(Transform p,int side)
        {
            Quaternion axis=Q(0,0,90);
            ArtKit.Torus(p,"Rounded all-terrain tire carcass",Vector3.zero,.463f,.245f,.395f,"Rubber",axis,24);
            C(p,"Broad reinforced rubber sidewall",Vector3.zero,.426f,.40f,"Rubber",axis,24);
            // Sidewall ring, broad inset rim and five substantial recessed spokes.
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
            var yaw=Group("Roof gun protected bearing",gun.yaw);
            C(yaw,"Roof-seated steel swivel",V(0,-.18f,0),.255f,.16f,"Steel");
            C(yaw,"Connected bearing neck",V(0,-.085f,0),.16f,.11f,"Steel");
            C(yaw,"Ivory bearing collar",V(0,-.045f,0),.209f,.055f,"IvoryShade");
            for(int s=-1;s<=1;s+=2)B(yaw,"Gun trunnion cheek",V(s*.16f,.075f,.18f),V(.075f,.26f,.40f),"Steel",.025f);
            B(yaw,"Small angular gun shield",V(-.225f,.17f,.30f),V(.10f,.36f,.38f),"IvoryShade",.035f,Q(-7,0,-7));
            Bake(yaw,"Hero_RoofGunBearing");
            var pitch=Group("Automatic roof machine gun",gun.pitch);
            B(pitch,"Distinct squared gun receiver",V(0,0,.05f),V(.25f,.24f,.64f),"Steel",.037f);
            B(pitch,"Receiver top cover",V(0,.14f,.09f),V(.23f,.075f,.46f),"SteelLight",.018f);
            B(pitch,"Rear recoil stock",V(0,-.02f,-.40f),V(.13f,.15f,.29f),"RustDark",.025f);
            C(pitch,"Heavy barrel jacket",V(0,0,.76f),.077f,.85f,"Steel",Q(90,0,0),12);
            for(int i=0;i<5;i++)C(pitch,"Barrel cooling collar",V(0,0,.42f+i*.145f),.086f,.045f,"SteelLight",Q(90,0,0),12);
            C(pitch,"Extended exposed barrel",V(0,0,1.34f),.047f,.43f,"SteelLight",Q(90,0,0),12);
            B(pitch,"Oblong muzzle brake",V(0,0,1.57f),V(.14f,.13f,.14f),"Steel",.02f);
            B(pitch,"Dark muzzle opening",V(0,0,1.646f),V(.07f,.058f,.012f),"Rubber",.006f);
            B(pitch,"Top rear sight",V(0,.20f,-.07f),V(.08f,.09f,.055f),"Steel",.008f);
            B(pitch,"Front sight",V(0,.12f,1.20f),V(.04f,.14f,.04f),"Steel",.008f);
            B(pitch,"Side-mounted ammunition box",V(.245f,-.06f,-.04f),V(.25f,.27f,.34f),"IvoryShade",.03f);
            for(int i=0;i<4;i++)C(pitch,"Short ammunition belt",V(.16f+i*.044f,.08f,.08f),.019f,.12f,"Rust",Q(90,0,0),6);
            Bake(pitch,"Hero_RoofMachineGun");
        }

        static void Harpoon(HarpoonCollector h)
        {
            var pivot=Group("Rear mechanical harpoon launcher",h.pivot);
            B(pivot,"Harpoon slim profiled launch cradle",V(0,-.055f,.45f),V(.22f,.14f,1.14f),"Steel",.025f);
            Beam(pivot,"Twin exposed upper rail",V(-.11f,.07f,.02f),V(-.11f,.07f,1.27f),.055f,"SteelLight");
            Beam(pivot,"Twin exposed upper rail",V(.11f,.07f,.02f),V(.11f,.07f,1.27f),.055f,"SteelLight");
            B(pivot,"Harpoon rear armored winch housing",V(0,-.03f,-.17f),V(.31f,.24f,.34f),"Steel",.04f);
            B(pivot,"Winch ivory top service lid",V(0,.10f,-.17f),V(.23f,.025f,.25f),"IvoryShade",.012f);
            C(pivot,"Visible cable winding drum",V(.185f,-.01f,-.13f),.12f,.13f,"SteelLight",Q(0,0,90),16);
            C(pivot,"Cable drum end",V(.26f,-.01f,-.13f),.14f,.028f,"Steel",Q(0,0,90),16);
            C(pivot,"Loaded harpoon shaft",V(0,.084f,.78f),.025f,1.15f,"IvoryShade",Q(90,0,0),8);
            Spike(pivot,"Large readable harpoon arrowhead",V(0,.084f,1.26f),V(0,.084f,1.535f),.115f,"SteelLight");
            B(pivot,"Harpoon receiver ivory service panel",V(-.184f,.025f,-.14f),V(.025f,.16f,.25f),"Ivory",.02f);
            Beam(pivot,"Operator upper handle",V(-.18f,-.04f,-.17f),V(-.30f,-.09f,-.36f),.05f,"Steel");
            Bake(pivot,"Hero_RearHarpoon");
            if(h.projectile)
            {
                // Existing moving projectile root remains +Z-facing and retains all state logic.
                var tip=Group("Flying harpoon head",h.projectile);
                Spike(tip,"Steel flying barb",V(0,0,-.16f),V(0,0,.23f),.09f,"SteelLight");
                C(tip,"Short flying shaft",V(0,0,-.22f),.022f,.20f,"IvoryShade",Q(90,0,0),8);
                Bake(tip,"Hero_HarpoonProjectile");
            }
        }

        static void Driver(Transform p)
        {
            // Seated crew fits inside the unaltered cabin hitbox and can be seen through the
            // open side window. Head is purposefully a broad silhouette, not micro geometry.
            B(p,"Driver seat",V(-.39f,1.16f,-.22f),V(.44f,.56f,.23f),"Rubber",.07f,Q(7,0,0));
            E(p,"Driver clothed torso",V(-.39f,1.37f,-.11f),V(.40f,.49f,.27f),"Cloth");
            E(p,"Driver head",V(-.39f,1.72f,-.04f),V(.25f,.29f,.25f),"Skin");
            E(p,"Driver leather cap",V(-.39f,1.83f,-.06f),V(.28f,.15f,.28f),"Steel");
            B(p,"Driver goggle strap",V(-.39f,1.75f,.082f),V(.24f,.065f,.045f),"Steel",.015f);
            B(p,"Driver dusty lenses",V(-.39f,1.755f,.11f),V(.16f,.040f,.016f),"Glass",.012f);
            B(p,"Driver rust scarf",V(-.39f,1.58f,.01f),V(.31f,.12f,.27f),"Scarf",.04f,Q(0,8,0));
            Limb(p,"Driver left upper arm",V(-.60f,1.49f,-.08f),V(-.64f,1.35f,.18f),.12f,"Skin");
            Limb(p,"Driver left forearm",V(-.64f,1.35f,.18f),V(-.54f,1.38f,.39f),.105f,"Skin");
            Limb(p,"Driver right upper arm",V(-.19f,1.49f,-.07f),V(-.13f,1.33f,.16f),.12f,"Cloth");
            Limb(p,"Driver right forearm",V(-.13f,1.33f,.16f),V(-.27f,1.38f,.39f),.10f,"Skin");
            E(p,"Driver glove left",V(-.53f,1.38f,.40f),V(.11f,.10f,.12f),"Rubber",8,6);
            E(p,"Driver glove right",V(-.26f,1.38f,.40f),V(.11f,.10f,.12f),"Rubber",8,6);
            ArtKit.Torus(p,"Steering wheel",V(-.40f,1.37f,.43f),.19f,.155f,.034f,"Steel",Q(67,0,0),16);
        }

        static void Mechanic(Transform p)
        {
            // A small bent-backed mechanic counterbalances the roof gun with a visibly separate
            // rear station. Bent limbs and shoulder bulk convey a deliberate hunched pose.
            B(p,"Mechanic left dusty boot",V(-.52f,1.00f,-1.74f),V(.18f,.18f,.33f),"Rubber",.05f,Q(0,-10,0));
            B(p,"Mechanic right dusty boot",V(-.10f,1.00f,-1.67f),V(.18f,.18f,.32f),"Rubber",.05f,Q(0,14,0));
            Limb(p,"Mechanic bent left shin",V(-.51f,1.06f,-1.70f),V(-.57f,1.32f,-1.52f),.16f,"Cloth");
            Limb(p,"Mechanic bent left thigh",V(-.57f,1.32f,-1.52f),V(-.32f,1.42f,-1.76f),.19f,"Cloth");
            Limb(p,"Mechanic bent right shin",V(-.10f,1.06f,-1.64f),V(.00f,1.30f,-1.43f),.16f,"Cloth");
            Limb(p,"Mechanic bent right thigh",V(.00f,1.30f,-1.43f),V(-.20f,1.42f,-1.72f),.19f,"Cloth");
            E(p,"Mechanic hunched torso",V(-.28f,1.67f,-1.62f),V(.48f,.53f,.36f),"Cloth",12,8).localRotation=Q(15,-24,-11);
            B(p,"Mechanic leather back vest",V(-.37f,1.70f,-1.81f),V(.40f,.37f,.075f),"Cloth",.07f,Q(15,-24,-11));
            B(p,"Mechanic small scavenged back plate",V(-.40f,1.73f,-1.85f),V(.23f,.23f,.045f),"IvoryShade",.04f,Q(15,-24,-11));
            Beam(p,"Mechanic back leather strap",V(-.55f,1.83f,-1.79f),V(-.17f,1.56f,-1.83f),.047f,"RustDark");
            E(p,"Mechanic wrapped red scarf",V(-.18f,1.89f,-1.48f),V(.33f,.15f,.31f),"Scarf");
            B(p,"Mechanic scarf short trailing end",V(-.37f,1.79f,-1.76f),V(.09f,.22f,.025f),"Scarf",.018f,Q(12,-10,-13));
            E(p,"Mechanic head leaning to harpoon",V(-.08f,2.02f,-1.43f),V(.27f,.30f,.27f),"Skin");
            E(p,"Mechanic worn steel helmet",V(-.08f,2.15f,-1.45f),V(.31f,.23f,.31f),"Steel");
            C(p,"Mechanic helmet rolled brow rim",V(-.08f,2.105f,-1.45f),.158f,.030f,"SteelLight",null,16);
            B(p,"Mechanic goggles",V(.035f,2.04f,-1.335f),V(.22f,.075f,.065f),"Steel",.02f,Q(0,45,0));
            B(p,"Mechanic amber goggle lenses",V(.066f,2.045f,-1.306f),V(.14f,.045f,.026f),"Ivory",.012f,Q(0,45,0));
            Limb(p,"Mechanic shoulder left",V(-.48f,1.83f,-1.50f),V(-.36f,1.78f,-1.16f),.14f,"Skin");
            Limb(p,"Mechanic hand left reaching",V(-.36f,1.78f,-1.16f),V(-.09f,2.08f,-.89f),.115f,"Skin");
            Limb(p,"Mechanic shoulder right",V(-.01f,1.83f,-1.55f),V(.20f,1.85f,-1.30f),.15f,"Cloth");
            Limb(p,"Mechanic hand right reaching",V(.20f,1.85f,-1.30f),V(.20f,2.15f,-1.00f),.11f,"Skin");
            E(p,"Mechanic dark glove left",V(-.09f,2.08f,-.89f),V(.13f,.11f,.13f),"Rubber",8,6);
            E(p,"Mechanic dark glove right",V(.20f,2.15f,-1.00f),V(.13f,.11f,.13f),"Rubber",8,6);
            B(p,"Mechanic utility belt",V(-.28f,1.43f,-1.64f),V(.44f,.11f,.37f),"RustDark",.04f,Q(0,-10,0));
            B(p,"Mechanic belt tool pouch",V(-.48f,1.42f,-1.56f),V(.15f,.20f,.18f),"Wood",.035f);
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
            Quad(p,name,xz.Select(v=>V(v.x,Profile(v.y,new[]{.84f,1.38f,2.32f,2.70f},new[]{1.19f,1.17f,1.09f,.99f})+lift,v.y)).ToArray(),mat);
        }
        static void RoofPatch(Transform p,string name,Vector2[] xz,string mat,float lift=.007f)
        {
            Quad(p,name,xz.Select(v=>V(v.x,Profile(v.y,new[]{-.59f,-.28f,.40f,.53f},new[]{1.95f,1.98f,1.95f,1.90f})+lift,v.y)).ToArray(),mat);
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
            // 44 triangles, versus a subdivided rounded primitive's 108. These small tread
            // blocks need one clear chamfer per edge, not densely tessellated round corners.
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
