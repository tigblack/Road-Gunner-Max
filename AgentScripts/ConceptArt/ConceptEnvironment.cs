using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.ConceptArt.Editor
{
    /// <summary>Finished reusable wasteland set. All dimensions are authored in the original prop's local metres.
    /// The caller owns prefab loading/saving. Blocking colliders and all gameplay components survive.</summary>
    public static class ConceptEnvironment
    {
        static Transform N(Transform p,string n,Vector3 at=default,Quaternion? rot=null)
        { var t=ArtKit.Node(n,p).transform;t.localPosition=at;t.localRotation=rot??Quaternion.identity;return t; }
        static Transform B(Transform p,string n,Vector3 at,Vector3 size,string m,float bevel=.045f,Vector3 angles=default)
            =>ArtKit.Box(p,n,at,size,m,bevel,Quaternion.Euler(angles));
        static Transform C(Transform p,string n,Vector3 at,float r,float h,string m,Vector3 angles=default,int seg=16)
            =>ArtKit.Cylinder(p,n,at,r,h,m,Quaternion.Euler(angles),seg);
        static void Beam(Transform p,string n,Vector3 a,Vector3 b,float width,string m="Rust")=>ArtKit.Beam(p,n,a,b,width,m);
        static void Mark(Transform p,string n,Vector3 at,Vector2 size,string m,Vector3 angles=default,int variant=0)
        {
            // Broad eroded paint silhouettes, with irregular inlets instead of regular polygon stickers.
            // Fourteen points remain quiet at mobile size while giving close roof views deliberately broken edges.
            const int outline=14;var v=new Vector3[outline+1];v[0]=Vector3.zero;
            for(int i=0;i<outline;i++){float a=(i*360f/outline+variant*13.2f)*Mathf.Deg2Rad;float radius=.76f+Mathf.Sin(i*2.31f+variant*1.7f)*.17f+Mathf.Sin(i*4.73f+variant)*.06f;v[i+1]=new Vector3(Mathf.Cos(a)*size.x*.5f*radius,Mathf.Sin(a)*size.y*.5f*radius,0);}
            var tr=new List<int>();for(int i=0;i<outline;i++){tr.Add(0);tr.Add(i+1);tr.Add((i+1)%outline+1);}
            var mesh=new Mesh{name="Env_"+n};mesh.vertices=v;mesh.triangles=tr.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            ArtKit.Part(p,n,mesh,at,Vector3.one,m,Quaternion.Euler(angles));
        }
        static void WornPanel(Transform p,string n,Vector3 at,Vector3 size,string paint="Ivory",Vector3 rot=default,int variant=0)
        {
            var t=N(p,n,at,Quaternion.Euler(rot));B(t,n+" folded sheet",Vector3.zero,size,paint,.025f);
            // Two rust blooms at sheet edges, large enough to read at the game camera.
            for(int s=-1;s<=1;s+=2){var a=new Vector3(s*size.x*.34f,-size.y*.32f,size.z*.5f+.006f);Mark(t,"Edge corrosion",a,new Vector2(size.x*.26f,size.y*.34f),"Rust",default,variant+(s==1?2:0));}
        }
        static Bounds LocalVisualBounds(GameObject root)
        {
            var b=new Bounds();bool first=true;foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(f.sharedMesh)
            {var m=root.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;var q=f.sharedMesh.bounds;for(int i=0;i<8;i++){var v=m.MultiplyPoint3x4(q.center+Vector3.Scale(q.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(first){b=new Bounds(v,Vector3.zero);first=false;}else b.Encapsulate(v);}}
            return b;
        }
        static Bounds LocalBoxBounds(GameObject root,Bounds fallback)
        {
            bool found=false;var result=new Bounds();foreach(var box in root.GetComponentsInChildren<BoxCollider>(true))
            {if(box.isTrigger)continue;var matrix=root.transform.worldToLocalMatrix*box.transform.localToWorldMatrix;for(int i=0;i<8;i++){var q=matrix.MultiplyPoint3x4(box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(!found){result=new Bounds(q,Vector3.zero);found=true;}else result.Encapsulate(q);}}
            return found?result:fallback;
        }
        static void ClearRenderers(GameObject root)
        {
            foreach(var t in root.GetComponentsInChildren<TextMesh>(true))Object.DestroyImmediate(t);
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))Object.DestroyImmediate(r);
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))Object.DestroyImmediate(f);
            // Safe idempotence; the historical collider transforms are intentionally retained.
            var old=root.transform.Find("Concept environment visuals");if(old)Object.DestroyImmediate(old.gameObject);
        }
        public static void ApplyProp(GameObject root,string kind)
        {
            var bounds=LocalVisualBounds(root);if(kind=="Cliff")bounds=LocalBoxBounds(root,bounds);ClearRenderers(root);var p=N(root.transform,"Concept environment visuals");
            switch(kind)
            {
                case "Corrugated fence": Fence(p);break;
                case "Shipping container": Container(p);break;
                case "Rusted lookout": Tower(p);break;
                case "Abandoned pickup": Wreck(p,false);break;
                case "Bus wreck": Wreck(p,true);break;
                case "Damaged utility pole": Pole(p);break;
                case "Closed scrap gate": Gate(p,true);break;
                case "Open scrap gate": Gate(p,false);break;
                case "Salvage gantry": Gantry(p);break;
                case "Sealed industrial bunker": Bunker(p);break;
                case "Scrap wind pump": WindPump(p);break;
                case "Tire and scrap pile": Scrap(p);break;
                case "Fuel drums": Drum(p,Vector3.zero);Drum(p,new Vector3(.97f,0,.26f),"Red");Jerry(p,new Vector3(-.67f,0,-.31f),"Ivory");break;
                case "Tire stack": TireStack(p);break;
                case "Metal storage crate": Crate(p,Vector3.zero,false);break;
                case "Wooden supply crate": Crate(p,Vector3.zero,true);break;
                case "Weathered concrete": Concrete(p);break;
                case "Route sign": Sign(p,false);break;
                case "Danger sign": Sign(p,true);break;
                case "Scrap stakes": Stakes(p);break;
                case "Dry scrub": Vegetation(p,false);break;
                case "Cactus": Vegetation(p,true);break;
                case "Service kiosk": Pump(p);break;
                case "Cliff": case "Boulder": case "Rock": Cliff(p,bounds,kind);break;
                default: throw new ArgumentException("Unknown concept environment prop "+kind);
            }
            ArtKit.Bake(p,"Env_"+kind.Replace(' ','_'));
        }

        static void Corrugated(Transform p,string n,Vector3 at,Vector2 size,string m,int folds=12,Vector3 rot=default)
        {
            // Actual folded metal cross section. Surface variation is broad, geometry casts clean readable shadows.
            var v=new List<Vector3>();var tr=new List<int>();float w=size.x/(folds*2);
            for(int i=0;i<=folds*2;i++){float x=-size.x*.5f+i*w,z=(i%2)*.055f;v.Add(new Vector3(x,-size.y*.5f,z));v.Add(new Vector3(x,size.y*.5f,z));if(i>0){int k=i*2;tr.AddRange(new[]{k-2,k,k+1,k-2,k+1,k-1});}}
            int count=v.Count;v.AddRange(v.Select(q=>q-new Vector3(0,0,.045f)).ToArray());for(int i=0;i<count-2;i+=2)tr.AddRange(new[]{count+i,count+i+3,count+i+2,count+i,count+i+1,count+i+3});
            var mesh=new Mesh{name="Env_Corrugation"};mesh.SetVertices(v);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateBounds();ArtKit.Part(p,n,mesh,at,Vector3.one,m,Quaternion.Euler(rot));
        }
        static void WornRoof(Transform p,string n,Vector3 at,Vector2 size,Vector3 rotation,int seed=0)
        {
            var roof=N(p,n,at,Quaternion.Euler(rotation));Corrugated(roof,"Ivory folded roof sheets",Vector3.zero,size,"Ivory",Mathf.Max(8,Mathf.RoundToInt(size.x*4)));
            // Corrosion follows exposed sheet edges, with occasional broad patches. The top-facing back of the
            // corrugation lies at z=-.045; all paint is laid directly on that surface rather than floating boxes.
            int pieces=Mathf.Max(3,Mathf.RoundToInt(size.y/2.1f));for(int i=0;i<pieces;i++)
            {
                float y=-size.y*.43f+(i+.25f)*(size.y*.84f/pieces);
                for(int edge=-1;edge<=1;edge+=2){float width=size.x*(edge==1?.17f:.095f);float length=size.y/pieces*(.77f+(i%2)*.16f);Mark(roof,"Irregular rust along folded sheet edge",new Vector3(edge*(size.x*.48f-width*.31f),y,-.053f),new Vector2(width,length),i%3==0?"RustDark":"Rust",new Vector3(0,180,0),seed+i+(edge==1?2:0));}
            }
            for(int i=0;i<2;i++){float x=size.x*(i==0?-.11f:.22f),y=size.y*(i==0?-.34f:.28f);Mark(roof,"Old roof corrosion bloom",new Vector3(x,y,-.055f),new Vector2(size.x*.30f,size.y*.13f),"Rust",new Vector3(0,180,0),seed+i+1);}
            // A few joined sheet seams support a sense of scale and fabrication without a noisy texture.
            for(int i=1;i<3;i++)B(roof,"Folded roof sheet joining strip",new Vector3(0,-size.y*.5f+i*size.y/3,-.061f),new Vector3(size.x,.035f,.018f),"IvoryShade",.005f);
        }
        static void Fence(Transform p)
        {
            for(int i=0;i<6;i++){var t=N(p,"Bolted sheet "+i,new Vector3(-2.75f+i*1.1f,1.08f,(i%2)*.045f),Quaternion.Euler(0,0,i==1?-3:i==4?2:0));Corrugated(t,"Worn corrugated sheet",Vector3.zero,new Vector2(1.13f,2.08f),i%3==0?"Ivory":"Rust",5);Mark(t,"Sheet rust bloom",new Vector3(-.22f,-.61f,.065f),new Vector2(.48f,.5f),"RustDark",default,i);}
            for(int s=-1;s<=1;s+=2){B(p,"Box steel post",new Vector3(s*3.08f,1.25f,.06f),new Vector3(.19f,2.5f,.24f),"Steel",.025f);B(p,"Post cap",new Vector3(s*3.08f,2.52f,.06f),new Vector3(.25f,.09f,.28f),"SteelLight",.018f);}
            for(int h=0;h<2;h++)B(p,"Horizontal rusty channel",new Vector3(0,.42f+h*1.25f,-.11f),new Vector3(6.2f,.14f,.15f),"RustDark",.018f);
            for(int i=0;i<7;i++)for(int h=0;h<2;h++)C(p,"Fence fastener",new Vector3(-3+i, .42f+h*1.25f,.11f),.045f,.025f,"SteelLight",new Vector3(90,0,0),8);
        }
        static void Container(Transform p)
        {
            B(p,"Container raised floor",new Vector3(0,.15f,0),new Vector3(3.1f,.3f,6.2f),"Steel",.06f);
            for(int s=-1;s<=1;s+=2){Corrugated(p,"Long corrugated wall",new Vector3(s*1.5f,1.55f,0),new Vector2(6.12f,2.73f),s==1?"Rust":"Red",18,new Vector3(0,90,0));for(int z=-1;z<=1;z+=2)B(p,"Container corner casting",new Vector3(s*1.49f,1.55f,z*3.02f),new Vector3(.17f,3.0f,.18f),"RustDark",.025f);}
            WornRoof(p,"Pale weathered container roof",new Vector3(0,2.98f,0),new Vector2(3.05f,6.12f),new Vector3(90,0,0),3);
            for(int i=0;i<2;i++){var d=N(p,"Container double door",new Vector3((i-.5f)*1.45f,1.55f,-3.12f));WornPanel(d,"Door panel",Vector3.zero,new Vector3(1.42f,2.72f,.08f),i==0?"Rust":"IvoryShade",new Vector3(0,180,0),i);for(int j=0;j<2;j++){B(d,"Door locking bar",new Vector3((j-.5f)*.88f,0,-.085f),new Vector3(.055f,2.36f,.06f),"SteelLight",.016f);B(d,"Lock handle",new Vector3((j-.5f)*.88f,-.12f,-.15f),new Vector3(.29f,.06f,.06f),"Steel",.014f);}for(int h=0;h<3;h++)B(d,"Heavy door hinge",new Vector3(i==0?-.68f:.68f,-.87f+h*.87f,-.075f),new Vector3(.2f,.16f,.12f),"Steel",.022f);}
            for(int i=0;i<3;i++)WornPanel(p,"Corrugated patch",new Vector3(1.575f,.6f+i*.68f,-1.9f+i*1.43f),new Vector3(.72f,.55f,.045f),i==1?"Ivory":"RustDark",new Vector3(0,90,0),i);
            B(p,"Stencilled empty storage stripe",new Vector3(0,2.24f,-3.18f),new Vector3(1.0f,.16f,.025f),"Ivory",.006f);
        }
        static void Tire(Transform p,Vector3 at,float radius=.57f,float width=.34f,Quaternion? rot=null)
        {
            var t=N(p,"Retread offroad tire",at,rot);ArtKit.Torus(t,"Rounded rubber carcass",Vector3.zero,radius,radius*.52f,width,"Rubber",null,20);
            ArtKit.Torus(t,"Dusty raised sidewall",Vector3.up*(width*.47f),radius*.84f,radius*.60f,.035f,"RubberEdge",null,20);
            for(int i=0;i<16;i++){float a=i*22.5f*Mathf.Deg2Rad;B(t,"Large tread block",new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius),new Vector3(.18f,width*.86f,.15f),"RubberEdge",.027f,new Vector3(0,-i*22.5f,0));}
        }
        static void TireStack(Transform p){for(int i=0;i<3;i++)Tire(p,new Vector3(i==1?.035f:0,.18f+i*.33f,i==1?-.025f:0),.6f,.32f);}
        static void Wheel(Transform p,Vector3 at,float r=.55f)
        {var t=N(p,"Wreck wheel",at,Quaternion.Euler(0,0,90));Tire(t,Vector3.zero,r,.29f);C(t,"Exposed steel rim",Vector3.zero,r*.46f,.32f,"RustDark");C(t,"Dented hub",new Vector3(0,.17f,0),r*.21f,.04f,"SteelLight");}
        static void Wreck(Transform p,bool bus)
        {
            if(bus){Bus(p);return;}var car=N(p,"Abandoned scavenged muscle pickup",new Vector3(0,-.10f,0),Quaternion.Euler(0,0,-2));
            B(car,"Exposed ladder chassis",new Vector3(0,.6f,0),new Vector3(1.83f,.24f,4.85f),"Steel",.06f);
            B(car,"Long bent hood",new Vector3(0,1.2f,1.40f),new Vector3(2.30f,.32f,2.09f),"Rust",.12f,new Vector3(-3,0,2));
            B(car,"Front steel valance",new Vector3(0,.91f,2.42f),new Vector3(2.40f,.52f,.20f),"RustDark",.07f);B(car,"Broken front grille",new Vector3(0,1.02f,2.545f),new Vector3(1.42f,.25f,.045f),"Steel",.025f);
            for(int i=0;i<6;i++)B(car,"Radiator grille slat",new Vector3(-.63f+i*.25f,1.02f,2.58f),new Vector3(.06f,.22f,.035f),"SteelLight",.01f);
            for(int s=-1;s<=1;s+=2){SideBody(car,s);Wheel(car,new Vector3(s*1.22f,.57f,1.64f));Wheel(car,new Vector3(s*1.22f,.57f,-1.58f));
                B(car,"Lower door sill",new Vector3(s*1.12f,.78f,-.13f),new Vector3(.13f,.20f,1.5f),"RustDark",.04f);
                WornPanel(car,"Chipped door skin",new Vector3(s*1.155f,1.19f,-.25f),new Vector3(1.30f,.65f,.07f),"IvoryShade",new Vector3(0,s*90,0),s==1?1:2);
                Beam(car,"A pillar",new Vector3(s*1.0f,1.40f,.60f),new Vector3(s*.87f,2.13f,.13f),.09f,"Steel");Beam(car,"B pillar",new Vector3(s*.94f,1.35f,-.91f),new Vector3(s*.87f,2.15f,-.86f),.10f,"RustDark");
                Beam(car,"Cabin top rail",new Vector3(s*.87f,2.15f,-.87f),new Vector3(s*.87f,2.13f,.13f),.10f,"Rust");
                B(car,"Empty rear bed side",new Vector3(s*1.08f,1.13f,-1.82f),new Vector3(.15f,.64f,1.20f),"Rust",.06f);
                B(car,"Dented front bumper",new Vector3(s*.61f,.68f,2.57f),new Vector3(1.15f,.15f,.23f),"SteelLight",.035f,new Vector3(0,s*7,0));
            }
            B(car,"Weathered roof sheet",new Vector3(0,2.17f,-.36f),new Vector3(1.95f,.13f,1.22f),"Rust",.085f,new Vector3(0,0,-2));
            Mark(car,"Large roof paint remnant",new Vector3(.20f,2.255f,-.38f),new Vector2(1.1f,.83f),"IvoryShade",new Vector3(-90,0,0),1);
            Beam(car,"Windscreen lower frame",new Vector3(-.99f,1.43f,.59f),new Vector3(.99f,1.43f,.59f),.10f,"Steel");Beam(car,"Windscreen upper frame",new Vector3(-.86f,2.12f,.13f),new Vector3(.86f,2.12f,.13f),.09f,"Steel");
            for(int s=-1;s<=1;s+=2){B(car,"Exposed worn seat",new Vector3(s*.47f,1.14f,-.36f),new Vector3(.63f,.18f,.60f),"Cloth",.09f);B(car,"Seat back",new Vector3(s*.47f,1.50f,-.63f),new Vector3(.61f,.62f,.15f),"Cloth",.08f,new Vector3(-8,0,0));}
            B(car,"Bed floor",new Vector3(0,.83f,-1.8f),new Vector3(2.01f,.12f,1.30f),"RustDark",.025f);B(car,"Drop tailgate",new Vector3(0,1.05f,-2.43f),new Vector3(2.24f,.55f,.13f),"Rust",.065f,new Vector3(-10,0,0));
            B(car,"Hood center seam",new Vector3(0,1.385f,1.46f),new Vector3(.025f,.018f,1.84f),"RustDark",.004f,new Vector3(-3,0,0));Mark(car,"Broad hood paint remnant",new Vector3(-.28f,1.39f,1.25f),new Vector2(1.45f,1.0f),"Red",new Vector3(-90,0,0),2);
            Crate(car,new Vector3(.16f,.9f,-1.78f),true,.58f);Jerry(car,new Vector3(-.70f,.9f,-1.78f),"Red",.7f);
        }
        static void SideBody(Transform p,int side)
        {
            // Real scalloped cut-outs around each tire instead of intersecting solid rectangular bodywork.
            const int steps=40;var v=new List<Vector3>();var tr=new List<int>();float x=side*1.15f;
            for(int i=0;i<=steps;i++){float z=-2.43f+i*4.86f/steps;float low=.72f;foreach(float wheel in new[]{-1.58f,1.64f}){float d=Mathf.Abs(z-wheel);if(d<.69f)low=Mathf.Max(low,.57f+Mathf.Sqrt(.69f*.69f-d*d));}v.Add(new Vector3(x,low,z));v.Add(new Vector3(x,1.37f,z));if(i>0){int a=(i-1)*2,b=i*2;if(side==1)tr.AddRange(new[]{a,a+1,b+1,a,b+1,b});else tr.AddRange(new[]{a,b+1,a+1,a,b,b+1});}}
            var mesh=new Mesh{name="Env_Wreck_scalloped_fender"};mesh.SetVertices(v);mesh.SetTriangles(tr,0);mesh.RecalculateNormals();mesh.RecalculateBounds();ArtKit.Part(p,"Scalloped quarter panel",mesh,Vector3.zero,Vector3.one,"Rust");
            foreach(float z in new[]{-1.58f,1.64f})for(int i=0;i<8;i++){float a=(i*180f/7)*Mathf.Deg2Rad;var q=new Vector3(x,.57f+Mathf.Sin(a)*.70f,z+Mathf.Cos(a)*.70f);B(p,"Rolled wheel arch edge",q,new Vector3(.11f,.09f,.29f),"RustDark",.025f,new Vector3(-i*180f/7,0,0));}
        }
        static void Bus(Transform p)
        {
            B(p,"Burnt utility bus chassis",new Vector3(0,.67f,0),new Vector3(2.35f,.31f,8.9f),"Steel",.08f);B(p,"Lower bus shell",new Vector3(0,1.19f,0),new Vector3(2.53f,.75f,8.85f),"Rust",.11f);B(p,"Ribbed roof shell",new Vector3(0,2.85f,0),new Vector3(2.53f,.22f,8.87f),"IvoryShade",.14f);
            for(int s=-1;s<=1;s+=2){for(int i=0;i<7;i++){float z=-3.65f+i*1.22f;B(p,"Bus window pillar",new Vector3(s*1.18f,2.18f,z),new Vector3(.11f,1.14f,.12f),"RustDark",.025f);if(i==1||i==5)B(p,"Remaining dirty window",new Vector3(s*1.17f,2.12f,z+.48f),new Vector3(.05f,.73f,.68f),"Glass",.02f);}B(p,"Upper window rail",new Vector3(s*1.19f,2.70f,0),new Vector3(.12f,.10f,8.6f),"Rust",.023f);Wheel(p,new Vector3(s*1.30f,.53f,-2.9f));Wheel(p,new Vector3(s*1.30f,.53f,2.9f));for(int i=0;i<4;i++)WornPanel(p,"Old bus paint",new Vector3(s*1.28f,1.19f,-3.1f+i*1.98f),new Vector3(1.36f,.46f,.025f),i%2==0?"Red":"IvoryShade",new Vector3(0,s*90,0),i);}
            for(int z=-1;z<=1;z+=2){B(p,"Bus end panel",new Vector3(0,1.7f,z*4.4f),new Vector3(2.48f,2.0f,.13f),"Rust",.09f);B(p,"Empty windshield opening",new Vector3(0,2.20f,z*4.49f),new Vector3(2.10f,.78f,.025f),"Steel",.02f);B(p,"Steel bumper",new Vector3(0,.77f,z*4.53f),new Vector3(2.74f,.18f,.26f),"SteelLight",.04f);}for(int i=0;i<6;i++)B(p,"Roof channel",new Vector3(0,2.98f,-3.6f+i*1.44f),new Vector3(2.51f,.05f,.055f),"RustDark",.012f);
        }
        static void Drum(Transform p,Vector3 at,string paint="Rust",float s=1)
        {
            var t=N(p,"Empty steel fuel drum",at);t.localScale=Vector3.one*s;C(t,"Round drum body",new Vector3(0,.55f,0),.42f,1.06f,paint);for(int i=0;i<2;i++)ArtKit.Torus(t,"Rolled drum hoop",new Vector3(0,.25f+i*.58f,0),.441f,.395f,.055f,"Steel",null,16);C(t,"Recessed drum lid",new Vector3(0,1.085f,0),.382f,.025f,"RustDark");ArtKit.Torus(t,"Top rolled lip",new Vector3(0,1.09f,0),.43f,.386f,.035f,"SteelLight",null,16);C(t,"Fuel bung",new Vector3(.18f,1.111f,.06f),.062f,.028f,"Steel");Mark(t,"Broad drum rust",new Vector3(-.10f,.58f,.424f),new Vector2(.49f,.56f),"RustDark",default,1);B(t,"Faded fuel label",new Vector3(.04f,.73f,.435f),new Vector3(.30f,.18f,.02f),"IvoryShade",.009f);
        }
        static void Jerry(Transform p,Vector3 at,string paint="Red",float s=1)
        {
            var t=N(p,"Salvaged empty jerrycan",at);t.localScale=Vector3.one*s;B(t,"Pressed fuel can",new Vector3(0,.43f,0),new Vector3(.48f,.78f,.28f),paint,.085f);B(t,"Fuel can shoulder",new Vector3(0,.86f,0),new Vector3(.43f,.14f,.27f),paint,.06f,new Vector3(0,0,8));for(int side=-1;side<=1;side+=2){Beam(t,"Pressed diagonal rib",new Vector3(-.13f,.22f,side*.151f),new Vector3(.13f,.66f,side*.151f),.035f,"RustDark");Beam(t,"Pressed diagonal rib",new Vector3(.13f,.22f,side*.151f),new Vector3(-.13f,.66f,side*.151f),.035f,"RustDark");}B(t,"Handle open top",new Vector3(-.025f,1.02f,0),new Vector3(.26f,.065f,.10f),"Steel",.025f);for(int side=-1;side<=1;side+=2)B(t,"Handle foot",new Vector3(side*.12f,.96f,0),new Vector3(.055f,.13f,.10f),"Steel",.018f);C(t,"Fuel cap",new Vector3(.17f,.97f,0),.07f,.05f,"SteelLight");
        }
        static void Crate(Transform p,Vector3 at,bool wood,float s=1)
        {
            var t=N(p,wood?"Wooden supply crate":"Salvaged metal supply crate",at);t.localScale=Vector3.one*s;string m=wood?"Wood":"TealDark";B(t,"Crate body",new Vector3(0,.58f,0),new Vector3(1.16f,1.16f,1.16f),m,.045f);
            if(wood){for(int side=-1;side<=1;side+=2){for(int i=0;i<4;i++)B(t,"Broad timber board",new Vector3(-.44f+i*.29f,.59f,side*.592f),new Vector3(.265f,1.10f,.035f),i%3==0?"WoodLight":"Wood",.015f);Beam(t,"Crate diagonal brace",new Vector3(-.46f,.12f,side*.63f),new Vector3(.46f,1.04f,side*.63f),.11f,"WoodLight");}for(int side=-1;side<=1;side+=2)for(int z=-1;z<=1;z+=2)B(t,"Timber edge batten",new Vector3(side*.52f,.59f,z*.61f),new Vector3(.13f,1.18f,.085f),"WoodLight",.025f);}
            else{for(int s1=-1;s1<=1;s1+=2){B(t,"Crate binding rail",new Vector3(s1*.40f,.60f,0),new Vector3(.085f,1.22f,1.24f),"SteelLight",.018f);B(t,"Crate lower foot",new Vector3(s1*.43f,.06f,0),new Vector3(.16f,.14f,1.27f),"Steel",.027f);}B(t,"Crate lid",new Vector3(0,1.19f,0),new Vector3(1.23f,.10f,1.24f),"Teal",.035f);B(t,"Ivory identifier",new Vector3(0,.70f,-.602f),new Vector3(.40f,.28f,.028f),"Ivory",.018f);B(t,"Crate latch",new Vector3(0,1.04f,-.64f),new Vector3(.15f,.18f,.06f),"Steel",.016f);}
        }
        static void Scrap(Transform p)
        {for(int i=0;i<4;i++)B(p,"Broad salvaged sheet",new Vector3((i-1.5f)*.28f,.12f+i*.065f,0),new Vector3(.59f,.10f,1.28f),i%2==0?"Rust":"IvoryShade",.035f,new Vector3(3+i*4,i*27,0));Tire(p,new Vector3(1.03f,.17f,.40f),.43f,.29f);C(p,"Scavenged gearbox",new Vector3(-.62f,.35f,.31f),.25f,.58f,"Steel",new Vector3(0,0,90));B(p,"Machinery casing",new Vector3(-.56f,.25f,.07f),new Vector3(.55f,.42f,.60f),"SteelLight",.06f);for(int i=0;i<3;i++)B(p,"Cooling fin",new Vector3(-.56f,.23f,-.17f+i*.12f),new Vector3(.60f,.37f,.045f),"Steel",.012f);}
        static void Pump(Transform p)
        {
            // Original 2 x 2 x 1.5 kiosk envelope, now a clearly empty fuel dispenser.
            B(p,"Pump concrete plinth",new Vector3(0,.09f,0),new Vector3(2,.18f,1.5f),"IvoryShade",.07f);B(p,"Rounded old pump cabinet",new Vector3(-.30f,.94f,0),new Vector3(.96f,1.68f,.65f),"Ivory",.14f);B(p,"Painted pump side band",new Vector3(-.30f,.69f,-.355f),new Vector3(.83f,.66f,.045f),"Red",.035f);B(p,"Dark meter bezel",new Vector3(-.30f,1.36f,-.356f),new Vector3(.68f,.37f,.065f),"Steel",.05f);B(p,"Old ivory meter face",new Vector3(-.30f,1.36f,-.398f),new Vector3(.52f,.21f,.025f),"IvoryShade",.012f);for(int i=0;i<3;i++)B(p,"Empty meter marks",new Vector3(-.46f+i*.16f,1.37f,-.414f),new Vector3(.09f,.05f,.01f),"Steel",.003f);
            Mark(p,"Pump paint peeled",new Vector3(-.05f,.47f,-.39f),new Vector2(.34f,.43f),"Rust",new Vector3(0,180,0),1);C(p,"Pump red cap",new Vector3(-.30f,1.84f,0),.28f,.12f,"Red");
            Vector3[] hose={new Vector3(.20f,1.55f,.05f),new Vector3(.57f,1.48f,.05f),new Vector3(.66f,.92f,.05f),new Vector3(.62f,.28f,.05f),new Vector3(.92f,.19f,.05f),new Vector3(.97f,.63f,.05f),new Vector3(.82f,1.02f,.05f)};for(int i=1;i<hose.Length;i++)Beam(p,"Dry black fuel hose",hose[i-1],hose[i],.055f,"Rubber");B(p,"Hanging fuel nozzle",new Vector3(.80f,1.08f,.03f),new Vector3(.11f,.30f,.08f),"Steel",.025f,new Vector3(0,0,-24));Jerry(p,new Vector3(.45f,.14f,-.41f),"Red",.65f);
            for(int side=-1;side<=1;side+=2)B(p,"Small canopy post",new Vector3(side*1.05f,1.15f,.48f),new Vector3(.10f,2.3f,.10f),"RustDark",.025f);Corrugated(p,"Damaged ivory pump shelter",new Vector3(0,2.29f,0),new Vector2(2.58f,1.9f),"IvoryShade",10,new Vector3(90,0,0));B(p,"Empty fuel station header",new Vector3(0,2.19f,-.78f),new Vector3(2.11f,.22f,.08f),"Rust",.025f);B(p,"Weathered ivory header strip",new Vector3(0,2.19f,-.83f),new Vector3(.73f,.07f,.016f),"Ivory",.004f);
        }
        static void Pole(Transform p)
        {Beam(p,"Leaning utility pole",Vector3.zero,new Vector3(.30f,5,0),.19f,"Wood");B(p,"Pole crossarm",new Vector3(.30f,4.45f,0),new Vector3(2.0f,.15f,.20f),"RustDark",.035f);for(int s=-1;s<=1;s+=2)C(p,"Ivory ceramic insulator",new Vector3(.3f+s*.78f,4.60f,0),.105f,.24f,"IvoryShade");B(p,"Broken sodium lamp",new Vector3(.95f,4.18f,0),new Vector3(.44f,.20f,.49f),"Steel",.05f);B(p,"Dusty lamp lens",new Vector3(.95f,4.06f,0),new Vector3(.34f,.04f,.37f),"IvoryShade",.02f);for(int i=0;i<3;i++)B(p,"Pole steel binding",new Vector3(.15f,2.1f+i*.34f,0),new Vector3(.25f,.07f,.25f),"Steel",.012f);}
        static void Tower(Transform p)
        {
            for(int s=-1;s<=1;s+=2)for(int z=-1;z<=1;z+=2){Beam(p,"Lookout structural leg",new Vector3(s*1.72f,0,z*1.72f),new Vector3(s*1.43f,3.5f,z*1.43f),.24f,"RustDark");B(p,"Lookout foot plate",new Vector3(s*1.72f,.08f,z*1.72f),new Vector3(.58f,.15f,.58f),"Steel",.04f);}
            B(p,"Lookout deck",new Vector3(0,3.48f,0),new Vector3(3.95f,.22f,3.95f),"Wood",.055f);for(int s=-1;s<=1;s+=2){Beam(p,"Tower cross brace",new Vector3(-1.65f,.5f,s*1.66f),new Vector3(1.43f,3.3f,s*1.43f),.12f,"Steel");Corrugated(p,"Lookout partial wall",new Vector3(s*1.85f,4.13f,0),new Vector2(3.65f,1.12f),s==1?"Rust":"IvoryShade",10,new Vector3(0,90,0));for(int z=-1;z<=1;z+=2)B(p,"Canopy support",new Vector3(s*1.65f,4.62f,z*1.65f),new Vector3(.13f,2.2f,.13f),"Steel",.02f);}
            Corrugated(p,"Patched lookout canopy",new Vector3(0,5.59f,0),new Vector2(4.65f,4.58f),"Rust",16,new Vector3(90,0,5));for(int i=0;i<10;i++)B(p,"Access ladder rung",new Vector3(0,.35f+i*.33f,-1.89f),new Vector3(.64f,.065f,.07f),"SteelLight",.012f);for(int s=-1;s<=1;s+=2)Beam(p,"Ladder upright",new Vector3(s*.34f,.1f,-1.90f),new Vector3(s*.34f,3.7f,-1.90f),.07f,"Steel");
        }
        static void Gate(Transform p,bool closed)
        {
            for(int s=-1;s<=1;s+=2){B(p,"Gate massive anchor",new Vector3(s*14.3f,2.4f,0),new Vector3(2.8f,4.8f,3),"RockShade",.18f);WornPanel(p,"Gate pier armor",new Vector3(s*14.3f,2.2f,-1.53f),new Vector3(2.38f,3.85f,.10f),"IvoryShade",new Vector3(0,180,0),s==1?1:2);B(p,"Gate pier cap",new Vector3(s*14.3f,4.9f,0),new Vector3(3,.21f,3.2f),"Rust",.065f);for(int i=0;i<3;i++)B(p,"Gate pier strap",new Vector3(s*14.3f,.72f+i*1.5f,-1.63f),new Vector3(2.73f,.15f,.10f),"Steel",.025f);}
            if(closed){for(int i=0;i<12;i++){float x=-11.92f+i*2.17f;Corrugated(p,"Reclaimed gate sheet",new Vector3(x,2.1f,(i%2)*.04f),new Vector2(2.22f,4.2f),i%4==0?"IvoryShade":"Rust",7);Mark(p,"Gate corrosion",new Vector3(x,.53f,.11f),new Vector2(1.22f,.80f),"RustDark",default,i);}for(int s=-1;s<=1;s+=2){Beam(p,"Gate X girder",new Vector3(s*12.2f,.5f,-.18f),new Vector3(-s*12.2f,3.7f,-.18f),.25f,"Steel");B(p,"Locked gate center bar",new Vector3(s*.55f,2.2f,-.32f),new Vector3(.13f,2.7f,.12f),"SteelLight",.025f);}B(p,"Gate lock block",new Vector3(0,2.03f,-.41f),new Vector3(.50f,.48f,.24f),"Steel",.05f);}
            else for(int s=-1;s<=1;s+=2)Corrugated(p,"Open reclaimed gate leaf",new Vector3(s*13,1.8f,-4),new Vector2(7,3.6f),"Rust",18,new Vector3(0,90,0));
        }
        static void Gantry(Transform p)
        {for(int s=-1;s<=1;s+=2){B(p,"Salvage gantry column",new Vector3(s*4.4f,3,0),new Vector3(.60f,6,1.0f),"RustDark",.075f);B(p,"Column broad base",new Vector3(s*4.4f,.25f,0),new Vector3(1.7f,.50f,1.7f),"RockShade",.11f);Beam(p,"Knee bracket",new Vector3(s*4.2f,4.5f,0),new Vector3(s*2.7f,6,0),.19f,"Steel");}B(p,"Overhead salvage beam",new Vector3(0,6.1f,0),new Vector3(10,.58f,1.1f),"Rust",.075f);B(p,"Empty crane trolley",new Vector3(.2f,5.72f,0),new Vector3(.85f,.29f,.74f),"Steel",.05f);Beam(p,"Crane cable",new Vector3(.2f,5.6f,0),new Vector3(.2f,3.95f,0),.055f,"Steel");ArtKit.Torus(p,"Salvage hook",new Vector3(.2f,3.78f,0),.25f,.15f,.12f,"SteelLight",Quaternion.Euler(90,0,0),12);}
        static void Bunker(Transform p)
        {B(p,"Sandstone bunker casing",new Vector3(0,2.4f,0),new Vector3(6,4.8f,1),"RockShade",.20f);B(p,"Inset steel bunker door",new Vector3(0,2.08f,-.64f),new Vector3(4.2f,3.8f,.28f),"Steel",.10f);for(int s=-1;s<=1;s+=2){B(p,"Ivory door frame",new Vector3(s*2.38f,2,-.79f),new Vector3(.34f,4.0f,.14f),"IvoryShade",.035f);B(p,"Door locking bar",new Vector3(s*.72f,2.1f,-.83f),new Vector3(.13f,3.4f,.10f),"Rust",.025f);}for(int i=0;i<6;i++)B(p,"Door reinforcing rib",new Vector3(0,.65f+i*.56f,-.83f),new Vector3(3.8f,.10f,.10f),"SteelLight",.022f);B(p,"Sealed bunker red plaque",new Vector3(0,3.38f,-.91f),new Vector3(1.39f,.48f,.07f),"Red",.03f);B(p,"Bunker lintel",new Vector3(0,4.49f,-.25f),new Vector3(6.1f,.44f,1.6f),"Rock",.14f);}
        static void WindPump(Transform p)
        {for(int s=-1;s<=1;s+=2){Beam(p,"Wind pump braced foot",new Vector3(s*1.3f,0,s*.8f),new Vector3(s*.48f,5.5f,0),.18f,"RustDark");Beam(p,"Wind pump diagonal",new Vector3(s*1.2f,.6f,s*.73f),new Vector3(-s*.53f,4.8f,0),.08f,"Steel");}C(p,"Wind pump axle",new Vector3(0,5.85f,0),.26f,.65f,"Steel",new Vector3(90,0,0));for(int i=0;i<8;i++){float a=i*45*Mathf.Deg2Rad;var t=N(p,"Broad salvaged fan blade",new Vector3(Mathf.Sin(a)*1.4f,6+Mathf.Cos(a)*1.4f,0),Quaternion.Euler(0,0,-i*45));WornPanel(t,"Fan blade",Vector3.zero,new Vector3(.58f,1.56f,.07f),i%3==0?"Ivory":"Rust",new Vector3(0,12,0),i);}Beam(p,"Dry pump piston",new Vector3(0,.3f,0),new Vector3(0,5.5f,0),.065f,"SteelLight");B(p,"Dry pump base",new Vector3(0,.35f,0),new Vector3(1.33f,.7f,1.2f),"Rust",.10f);}
        static void Concrete(Transform p)
        {B(p,"Broken roadside foundation",new Vector3(0,.75f,0),new Vector3(4,1.50f,1.0f),"RockLight",.18f);RockMass(p,new Vector3(1.15f,0,0),new Vector3(.94f,2.8f,1.3f),2);for(int i=0;i<3;i++)Beam(p,"Exposed old rebar",new Vector3(-1.3f+i*.71f,1.4f,0),new Vector3(-1.4f+i*.71f,1.97f,.11f),.055f,"RustDark");}
        static void Sign(Transform p,bool danger)
        {B(p,"Road sign support",new Vector3(0,1.25f,0),new Vector3(.15f,2.5f,.18f),"Wood",.025f);WornPanel(p,"Reclaimed ivory signboard",new Vector3(0,2.4f,0),new Vector3(2.65f,.80f,.13f),danger?"Red":"IvoryShade",default,danger?1:0);if(danger){B(p,"Caution vertical stroke",new Vector3(0,2.49f,-.085f),new Vector3(.13f,.32f,.025f),"Ivory",.015f);B(p,"Caution dot",new Vector3(0,2.22f,-.085f),new Vector3(.13f,.10f,.025f),"Ivory",.012f);}else{B(p,"Route arrow shaft",new Vector3(-.10f,2.40f,-.085f),new Vector3(1.16f,.13f,.025f),"Steel",.018f);for(int s=-1;s<=1;s+=2)B(p,"Route arrow chevron",new Vector3(.58f,2.40f+s*.17f,-.085f),new Vector3(.46f,.13f,.025f),"Steel",.018f,new Vector3(0,0,s*43));}for(int s=-1;s<=1;s+=2)C(p,"Sign bolt",new Vector3(s*1.1f,2.4f,-.085f),.04f,.02f,"SteelLight",new Vector3(90,0,0),8);}
        static void Stakes(Transform p)
        {for(int i=0;i<4;i++){B(p,"Rough mismatched timber stake",new Vector3(i*.7f-1,.68f,0),new Vector3(.24f,1.36f+i%2*.19f,.18f),i%2==0?"Wood":"WoodLight",.035f,new Vector3(0,i*11,-3+i*2));}B(p,"Salvaged horizontal fence strap",new Vector3(0,.59f,-.13f),new Vector3(2.45f,.16f,.09f),"Rust",.02f);}
        static void Vegetation(Transform p,bool cactus)
        {
            if(cactus){C(p,"Chunky cactus main stem",new Vector3(0,.94f,0),.19f,1.72f,"Cactus",default,10);ArtKit.Ellipsoid(p,"Rounded cactus crown",new Vector3(0,1.83f,0),new Vector3(.38f,.29f,.38f),"Cactus",10,5);for(int s=-1;s<=1;s+=2){Beam(p,"Cactus side arm",new Vector3(0,.65f+s*.15f,0),new Vector3(s*.43f,.65f+s*.15f,0),.19f,"Cactus");C(p,"Upright cactus arm",new Vector3(s*.43f,.97f+s*.15f,0),.12f,.65f,"Cactus",default,10);ArtKit.Ellipsoid(p,"Rounded cactus arm",new Vector3(s*.43f,1.32f+s*.15f,0),new Vector3(.24f,.22f,.24f),"Cactus",10,5);}for(int i=0;i<4;i++){float a=i*90*Mathf.Deg2Rad;Beam(p,"Broad cactus rib",new Vector3(Mathf.Cos(a)*.19f,.18f,Mathf.Sin(a)*.19f),new Vector3(Mathf.Cos(a)*.19f,1.68f,Mathf.Sin(a)*.19f),.025f,"WoodLight");}}
            else for(int i=0;i<7;i++){float a=i*2.4f;Vector3 top=new Vector3(Mathf.Sin(a)*.42f,.21f+(i%3)*.15f,Mathf.Cos(a)*.42f);Beam(p,"Dry scrub branch",Vector3.zero,top,.035f,"Wood");ArtKit.Ellipsoid(p,"Dusty dry foliage",top,new Vector3(.23f,.26f,.22f),i%3==0?"WoodLight":"DryLeaves",7,4);}
        }
        static void Cliff(Transform p,Bounds bounds,string kind)
        {
            if(bounds.size.sqrMagnitude<.01f)bounds=kind=="Cliff"?new Bounds(new Vector3(0,2,0),new Vector3(7,4,7)):new Bounds(new Vector3(0,.85f,0),new Vector3(3,1.7f,2.7f));
            // Geographical collision remains unchanged. Broad surfaces replace the old faceted/flat cap treatment.
            RockMass(p,bounds.center-new Vector3(0,bounds.size.y*.5f,0),bounds.size,13);
            if(kind!="Cliff")return;
            var size=bounds.size;for(int i=0;i<3;i++){float x=(-.30f+i*.31f)*size.x;RockMass(p,new Vector3(bounds.center.x+x,bounds.min.y,bounds.center.z+size.z*.20f),new Vector3(size.x*.31f,size.y*(.28f+i%2*.18f),size.z*.31f),20+i);}
        }
        static void RockMass(Transform p,Vector3 at,Vector3 size,int seed)
        {
            // Five rings and ten broad planes; bevel strips at crown/foot soften silhouettes without noisy detail.
            const int seg=10;float[] ys={0,.07f,.28f,.72f,.94f,1};float[] rs={.75f,.98f,1,.91f,.69f,.53f};var vv=new List<Vector3>();
            for(int j=0;j<ys.Length;j++)for(int i=0;i<seg;i++){float a=(i*360f/seg+seed*13)*Mathf.Deg2Rad;float irregular=.90f+Mathf.Sin(i*2.17f+seed)*.10f;float y=ys[j];if(j>0&&j<ys.Length-1)y+=Mathf.Sin(i*1.9f+seed)*.025f;vv.Add(new Vector3(Mathf.Cos(a)*.5f*size.x*rs[j]*irregular,y*size.y,Mathf.Sin(a)*.5f*size.z*rs[j]*irregular));}
            var verts=new List<Vector3>();var normals=new List<Vector3>();var tris=new List<int>();var groups=new[]{new List<int>(),new List<int>(),new List<int>()};
            Action<Vector3,Vector3,Vector3,int> tri=(a,b,c,m)=>{int k=verts.Count;verts.Add(a);verts.Add(b);verts.Add(c);var n=Vector3.Cross(b-a,c-a).normalized;normals.Add(n);normals.Add(n);normals.Add(n);groups[m].AddRange(new[]{k,k+1,k+2});};
            for(int j=0;j<ys.Length-1;j++)for(int i=0;i<seg;i++){int n=(i+1)%seg;var a=vv[j*seg+i];var b=vv[j*seg+n];var c=vv[(j+1)*seg+n];var d=vv[(j+1)*seg+i];int m=j>2?1:(i+seed)%7==0?2:0;tri(a,c,b,m);tri(a,d,c,m);}var top=new Vector3(0,size.y,0);for(int i=0;i<seg;i++)tri(top,vv[(ys.Length-1)*seg+(i+1)%seg],vv[(ys.Length-1)*seg+i],1);
            var mesh=new Mesh{name="Env_Sculpted_sandstone",indexFormat=IndexFormat.UInt32};mesh.SetVertices(verts);mesh.SetNormals(normals);mesh.subMeshCount=3;for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);mesh.RecalculateBounds();var t=ArtKit.Part(p,"Sculpted sandstone mass",mesh,at,Vector3.one,"Rock");t.GetComponent<MeshRenderer>().sharedMaterials=new[]{ArtKit.Mat("Rock"),ArtKit.Mat("RockLight"),ArtKit.Mat("RockShade")};
        }

        static void ToolCabinet(Transform p,Vector3 at)
        {var t=N(p,"Workshop tool cabinet",at);B(t,"Cabinet steel casing",new Vector3(0,.91f,0),new Vector3(1.55f,1.82f,.78f),"Red",.085f);B(t,"Dark cabinet top",new Vector3(0,1.86f,0),new Vector3(1.65f,.13f,.87f),"Steel",.045f);for(int i=0;i<6;i++){B(t,"Drawer shadow",new Vector3(0,.29f+i*.25f,-.401f),new Vector3(1.35f,.18f,.025f),"RustDark",.013f);B(t,"Painted drawer front",new Vector3(0,.31f+i*.25f,-.42f),new Vector3(1.27f,.15f,.04f),"Red",.016f);B(t,"Drawer metal grip",new Vector3(0,.31f+i*.25f,-.46f),new Vector3(.92f,.042f,.035f),"SteelLight",.014f);}B(t,"Rag on cabinet",new Vector3(.40f,1.97f,-.15f),new Vector3(.42f,.08f,.37f),"Cloth",.035f,new Vector3(0,13,0));}
        static void Shelf(Transform p,Vector3 at)
        {var t=N(p,"Workshop equipment shelving",at);for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)B(t,"Shelf angle upright",new Vector3(x*.9f,1.35f,z*.34f),new Vector3(.07f,2.7f,.07f),"Steel",.014f);for(int i=0;i<3;i++)B(t,"Shelf steel tray",new Vector3(0,.35f+i*.83f,0),new Vector3(1.94f,.08f,.80f),"RustDark",.023f);Crate(t,new Vector3(-.43f,.41f,0),false,.58f);Jerry(t,new Vector3(.48f,1.22f,0),"IvoryShade",.68f);C(t,"Spare machinery casing",new Vector3(-.42f,2.22f,0),.25f,.36f,"SteelLight",new Vector3(0,0,90));}
        static void Workbench(Transform p,Vector3 at)
        {
            var t=N(p,"Mechanic workbench and tool wall",at);
            WornPanel(t,"Offwhite workbay backing",new Vector3(0,2.36f,.65f),new Vector3(2.94f,3.35f,.11f),"Ivory",new Vector3(0,180,0),3);
            for(int s=-1;s<=1;s+=2){B(t,"Toolwall steel upright",new Vector3(s*1.44f,2.30f,.71f),new Vector3(.12f,3.73f,.16f),"RustDark",.023f);for(int z=-1;z<=1;z+=2)B(t,"Workbench steel leg",new Vector3(s*1.27f,.63f,z*.44f),new Vector3(.13f,1.25f,.13f),"Steel",.025f);}
            B(t,"Heavy metal workbench top",new Vector3(0,1.32f,0),new Vector3(2.91f,.16f,1.16f),"SteelLight",.04f);B(t,"Workbench lower shelf",new Vector3(0,.27f,0),new Vector3(2.70f,.11f,.94f),"Wood",.025f);
            B(t,"Workbench red drawer unit",new Vector3(-.72f,.80f,0),new Vector3(1.05f,.88f,.85f),"Red",.05f);for(int i=0;i<3;i++){B(t,"Workbench drawer shadow",new Vector3(-.72f,.52f+i*.27f,-.443f),new Vector3(.91f,.20f,.026f),"RustDark",.01f);B(t,"Workbench drawer grip",new Vector3(-.72f,.54f+i*.27f,-.48f),new Vector3(.65f,.04f,.055f),"SteelLight",.013f);}
            B(t,"Recessed tool pegboard",new Vector3(0,2.75f,.572f),new Vector3(2.61f,1.62f,.045f),"IvoryShade",.025f);
            for(int row=0;row<3;row++)for(int col=0;col<5;col++)C(t,"Broad pegboard perforation",new Vector3(-1.04f+col*.52f,2.2f+row*.48f,.539f),.025f,.016f,"RustDark",new Vector3(90,0,0),6);
            for(int i=0;i<3;i++)Wrench(t,"Hanging mechanic spanner",new Vector3(-.77f+i*.63f,2.68f,.51f),.46f-i*.045f,"Steel",new Vector3(-90,0,-6+i*11));
            B(t,"Bench vice base",new Vector3(.86f,1.46f,-.23f),new Vector3(.51f,.17f,.39f),"Steel",.035f);B(t,"Bench vice jaw",new Vector3(.86f,1.69f,-.25f),new Vector3(.56f,.29f,.27f),"TealDark",.04f);B(t,"Vice exposed jaws",new Vector3(.86f,1.86f,-.25f),new Vector3(.60f,.08f,.29f),"SteelLight",.02f);Beam(t,"Vice handle",new Vector3(.65f,1.56f,-.58f),new Vector3(1.12f,1.56f,-.58f),.035f,"SteelLight");
            Jerry(t,new Vector3(-.82f,.33f,.0f),"IvoryShade",.56f);B(t,"Workbench folded shop rag",new Vector3(.22f,1.43f,.17f),new Vector3(.43f,.06f,.37f),"Cloth",.025f,new Vector3(0,13,0));
            B(t,"Workbay warm strip fixture",new Vector3(0,3.98f,.28f),new Vector3(2.38f,.12f,.24f),"Steel",.03f);B(t,"Warm workbench light diffuser",new Vector3(0,3.90f,.28f),new Vector3(2.13f,.045f,.18f),"Ivory",.018f);
        }
        static void EngineHoist(Transform p,Vector3 at)
        {
            var t=N(p,"Rebuilt engine hoist",at);
            for(int s=-1;s<=1;s+=2){Beam(t,"Hoist splayed rolling fork",new Vector3(-.48f,.15f,s*.29f),new Vector3(1.10f,.15f,s*.64f),.16f,"Red");C(t,"Small hoist caster",new Vector3(1.07f,.12f,s*.64f),.12f,.10f,"Rubber",new Vector3(0,0,90),10);}
            Beam(t,"Hoist heavy upright",new Vector3(-.42f,.17f,0),new Vector3(-.42f,2.72f,0),.19f,"RustDark");Beam(t,"Hoist diagonal support",new Vector3(-.90f,.18f,0),new Vector3(-.42f,1.55f,0),.13f,"Steel");Beam(t,"Hoist boom",new Vector3(-.42f,2.65f,0),new Vector3(1.22f,2.91f,0),.20f,"Red");
            Beam(t,"Hydraulic lift cylinder",new Vector3(-.36f,.56f,0),new Vector3(.49f,2.51f,0),.12f,"SteelLight");C(t,"Hydraulic pivot pin",new Vector3(-.42f,2.64f,0),.11f,.30f,"SteelLight",new Vector3(90,0,0),12);
            Beam(t,"Short salvage lifting chain",new Vector3(1.17f,2.83f,0),new Vector3(1.17f,1.80f,0),.035f,"Steel");ArtKit.Torus(t,"Open hoist lifting eye",new Vector3(1.17f,1.68f,0),.16f,.10f,.065f,"SteelLight",Quaternion.Euler(90,0,0),12);
            B(t,"Suspended salvaged engine block",new Vector3(1.02f,1.19f,0),new Vector3(.61f,.61f,.64f),"Steel",.065f);for(int i=0;i<3;i++)B(t,"Engine broad cooling fin",new Vector3(1.02f,1.07f+i*.15f,0),new Vector3(.68f,.045f,.69f),"SteelLight",.012f);C(t,"Engine gearbox bell",new Vector3(.59f,1.18f,0),.26f,.36f,"Rust",new Vector3(0,0,90),12);
        }
        static void Hose(Transform p,Vector3[] points)
        {for(int i=1;i<points.Length;i++){var d=points[i]-points[i-1];ArtKit.Cylinder(p,"Black service hose",(points[i]+points[i-1])*.5f,.025f,d.magnitude,"Rubber",Quaternion.FromToRotation(Vector3.up,d),8);}}
        static void Compressor(Transform p,Vector3 at)
        {
            var t=N(p,"Repaired portable air compressor",at);C(t,"Compressed air receiver",new Vector3(0,.58f,0),.34f,1.18f,"Teal",new Vector3(90,0,0),16);for(int z=-1;z<=1;z+=2)ArtKit.Ellipsoid(t,"Rounded air-tank end",new Vector3(0,.58f,z*.58f),new Vector3(.68f,.68f,.41f),"Teal",12,6);
            for(int s=-1;s<=1;s+=2)C(t,"Compressor rubber wheel",new Vector3(s*.35f,.20f,.40f),.20f,.12f,"Rubber",new Vector3(0,0,90),12);B(t,"Compressor front stand",new Vector3(0,.16f,-.44f),new Vector3(.45f,.24f,.17f),"Steel",.025f);
            B(t,"Compressor motor casing",new Vector3(0,1.10f,.17f),new Vector3(.48f,.47f,.47f),"Steel",.055f);for(int i=0;i<4;i++)B(t,"Motor cooling fin",new Vector3(0,1.04f+i*.09f,.17f),new Vector3(.57f,.035f,.54f),"SteelLight",.012f);C(t,"Compressor drive motor",new Vector3(.40f,1.0f,.17f),.18f,.41f,"RustDark",new Vector3(0,0,90),12);
            C(t,"Pressure gauge rim",new Vector3(-.02f,1.47f,-.14f),.16f,.08f,"Steel",new Vector3(90,0,0),14);C(t,"Pressure gauge ivory face",new Vector3(-.02f,1.47f,-.189f),.13f,.015f,"Ivory",new Vector3(90,0,0),14);Beam(t,"Pressure gauge needle",new Vector3(-.02f,1.47f,-.20f),new Vector3(.055f,1.52f,-.20f),.015f,"Steel");
            Beam(t,"Compressor moving handle",new Vector3(-.28f,1.1f,.66f),new Vector3(-.28f,1.53f,.77f),.06f,"Steel");Beam(t,"Compressor moving handle",new Vector3(.28f,1.1f,.66f),new Vector3(.28f,1.53f,.77f),.06f,"Steel");Beam(t,"Compressor handle grip",new Vector3(-.28f,1.53f,.77f),new Vector3(.28f,1.53f,.77f),.07f,"Rubber");
            Hose(t,new[]{new Vector3(.2f,1.06f,-.36f),new Vector3(.60f,.78f,-.53f),new Vector3(.76f,.21f,-.67f),new Vector3(.68f,.055f,-.85f)});ArtKit.Torus(t,"Coiled service air line",new Vector3(.63f,.055f,-1.03f),.41f,.36f,.045f,"Rubber",null,20);
        }
        static void TireRack(Transform p,Vector3 at)
        {
            var t=N(p,"Spare-wheel service rack",at);for(int s=-1;s<=1;s+=2){B(t,"Tire rack upright",new Vector3(s*.79f,.79f,0),new Vector3(.10f,1.58f,.13f),"Steel",.022f);B(t,"Tire rack splayed foot",new Vector3(s*.79f,.08f,0),new Vector3(.32f,.16f,.91f),"RustDark",.025f);}B(t,"Tire rack lower rail",new Vector3(0,.34f,0),new Vector3(1.7f,.12f,.15f),"SteelLight",.025f);for(int s=-1;s<=1;s+=2)Tire(t,new Vector3(s*.40f,.81f,0),.43f,.25f,Quaternion.Euler(90,0,0));
        }
        static void Wrench(Transform p,string n,Vector3 at,float size,string m,Vector3 rotation=default)
        {
            var t=N(p,n,at,Quaternion.Euler(rotation));t.localScale=Vector3.one*size;B(t,"Wrench shaft",Vector3.zero,new Vector3(.19f,.025f,1.25f),m,.015f,new Vector3(0,-35,0));
            var head=N(t,"Open spanner head",new Vector3(-.36f,0,.52f),Quaternion.Euler(0,-35,0));
            var verts=new List<Vector3>();var triangles=new List<int>();const int pieces=14;
            for(int i=0;i<=pieces;i++){float a=(60+i*240f/pieces)*Mathf.Deg2Rad;for(int h=0;h<2;h++)for(int r=0;r<2;r++){float radius=r==0?.16f:.28f;verts.Add(new Vector3(Mathf.Sin(a)*radius,h*.03f-.015f,Mathf.Cos(a)*radius));}}
            for(int i=0;i<pieces;i++){int k=i*4;triangles.AddRange(new[]{k+2,k+7,k+3,k+2,k+6,k+7,k,k+1,k+5,k,k+5,k+4,k+1,k+3,k+7,k+1,k+7,k+5,k,k+6,k+2,k,k+4,k+6});}
            triangles.Reverse();var mesh=new Mesh{name="Env_Open_wrench_head"};mesh.SetVertices(verts);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();ArtKit.Part(head,"Open wrench head",mesh,Vector3.zero,Vector3.one,m);
            for(int side=-1;side<=1;side+=2)B(head,"Spanner open jaw",new Vector3(side*.19f,0,.20f),new Vector3(.12f,.03f,.23f),m,.012f,new Vector3(0,side*12,0));
            ArtKit.Torus(t,"Wrench handle eye",new Vector3(.36f,0,-.52f),.17f,.07f,.03f,m,null,12);
        }
        public static void ApplyWorkshop(RepairWorkshop w)
        {
            var root=w.gameObject;ClearRenderers(root);var p=N(root.transform,"Concept environment visuals");
            // Fixed historical colliders at ±10.8m / ±11.7m remain. The 20m service disc remains fully driveable.
            B(p,"Worn sandstone-gray service apron",new Vector3(0,-.005f,0),new Vector3(22.9f,.08f,13.0f),"IvoryShade",.08f);
            for(int side=-1;side<=1;side+=2)B(p,"Equipment-side concrete footing",new Vector3(side*10.1f,.012f,0),new Vector3(2.6f,.055f,12.4f),"Ivory",.065f);
            for(int x=-1;x<=1;x++)B(p,"Restrained broad apron joint",new Vector3(x*4.6f,.041f,3.1f),new Vector3(.023f,.009f,5.5f),"SandShade",.004f);
            B(p,"Apron transverse slab joint",new Vector3(0,.041f,1.0f),new Vector3(15.1f,.009f,.023f),"SandShade",.004f);
            for(int s=-1;s<=1;s+=2)
            {
                for(int z=-1;z<=1;z+=2){var pos=new Vector3(s*10.8f,2.8f,z*6);B(p,"Heavy salvaged canopy upright",pos,new Vector3(.32f,5.6f,.39f),"RustDark",.05f);B(p,"Canopy foot plate",new Vector3(pos.x,.10f,pos.z),new Vector3(.79f,.20f,.80f),"Steel",.045f);B(p,"Steel column collar",new Vector3(pos.x,4.92f,pos.z),new Vector3(.46f,.21f,.52f),"Steel",.035f);Beam(p,"Canopy diagonal knee brace",new Vector3(pos.x,4.05f,pos.z),new Vector3(s*8.82f,5.56f,pos.z),.17f,"Rust");for(int i=0;i<2;i++)C(p,"Large structural bolt",new Vector3(pos.x,4.90f,pos.z+(z==-1?-.275f:.275f)),.068f,.035f,"SteelLight",new Vector3(90,0,0),8);}
                for(int j=0;j<5;j++){float z=-4.8f+j*2.4f;WornPanel(p,"Off-white workshop side cladding",new Vector3(s*11.68f,1.12f,z),new Vector3(2.28f,2.15f,.10f),j==3?"Rust":"Ivory",new Vector3(0,s*90,0),j);B(p,"Panel support rail",new Vector3(s*11.75f,2.28f,z),new Vector3(.11f,.11f,2.36f),"Steel",.02f);}
                // Only side roofs: service circle/cars remain visible from the portrait camera.
                WornRoof(p,"Salvaged ivory side canopy",new Vector3(s*9.75f,5.66f,0),new Vector2(3.20f,13.35f),new Vector3(90,0,s*4),s==1?1:4);
                Beam(p,"Long canopy edge channel",new Vector3(s*8.15f,5.52f,-6.7f),new Vector3(s*8.15f,5.52f,6.7f),.17f,"RustDark");
            }
            // Independent salvaged side awnings leave the complete playable service circle unobscured.
            // No front/back cross-bay girder sweeps over the player from the fixed gameplay camera.
            for(int z=-1;z<=1;z+=2)for(int side=-1;side<=1;side+=2){B(p,"Side awning end girder",new Vector3(side*9.75f,5.62f,z*6),new Vector3(3.55f,.27f,.34f),"RustDark",.045f);for(int i=0;i<2;i++)B(p,"Awning end binding plate",new Vector3(side*9.75f+(i-.5f)*1.98f,5.62f,z*6+.20f),new Vector3(.22f,.38f,.055f),"Rust",.023f);}
            var sign=N(p,"Workshop salvaged sign on side awning",new Vector3(-9.75f,5.58f,-6.87f));WornPanel(sign,"Offwhite workshop sign",Vector3.zero,new Vector3(3.08f,.93f,.12f),"Ivory",new Vector3(0,180,0),1);Wrench(sign,"Workshop wrench pictogram",new Vector3(0,-.01f,-.085f),.53f,"Steel",new Vector3(-90,0,0));
            var cabinet=N(p,"Camera-facing tool station",new Vector3(-10.65f,0,-3.7f));ToolCabinet(cabinet,Vector3.zero);Shelf(p,new Vector3(-10.55f,0,.10f));
            Workbench(p,new Vector3(-9.75f,0,1.45f));EngineHoist(p,new Vector3(-10.0f,0,-1.40f));
            Compressor(p,new Vector3(9.95f,0,.30f));TireRack(p,new Vector3(9.85f,0,-4.80f));
            for(int side=-1;side<=1;side+=2){WornPanel(p,"Workshop enclosed equipment end wall",new Vector3(side*9.85f,2.0f,5.98f),new Vector3(3.25f,3.8f,.11f),"Ivory",new Vector3(0,180,0),side==1?2:4);B(p,"Equipment room wall top channel",new Vector3(side*9.85f,3.99f,5.99f),new Vector3(3.37f,.16f,.18f),"RustDark",.03f);}
            for(int i=0;i<3;i++)Tire(p,new Vector3(-10.65f,.17f+i*.34f,3.10f),.61f,.32f);Tire(p,new Vector3(-10.70f,.67f,4.56f),.65f,.36f,Quaternion.Euler(90,0,0));
            Drum(p,new Vector3(10.92f,0,3.94f),"Rust");Drum(p,new Vector3(10.86f,0,2.80f),"Teal");Jerry(p,new Vector3(10.37f,0,-3.82f),"Red");Jerry(p,new Vector3(11.02f,0,-3.84f),"IvoryShade");Crate(p,new Vector3(10.57f,0,-1.92f),true,.91f);
            for(int i=0;i<2;i++){C(p,"Small salvaged oil tin",new Vector3(10.26f+i*.53f,1.24f,-1.92f),.14f,.34f,i==0?"Red":"IvoryShade",default,12);C(p,"Oil tin capped neck",new Vector3(10.26f+i*.53f,1.43f,-1.92f),.065f,.07f,"Steel",default,10);Beam(p,"Oil-tin carrying grip",new Vector3(10.18f+i*.53f,1.48f,-1.92f),new Vector3(10.34f+i*.53f,1.48f,-1.92f),.035f,"Steel");}
            // Hydraulic jack and work light make the equipment purpose readable without filling the bay.
            B(p,"Hydraulic jack foot",new Vector3(-10.14f,.13f,-5.11f),new Vector3(.74f,.26f,.91f),"Red",.06f);C(p,"Hydraulic jack ram",new Vector3(-10.14f,.70f,-5.11f),.13f,.95f,"SteelLight");Beam(p,"Jack handle",new Vector3(-9.86f,.29f,-5.11f),new Vector3(-9.66f,1.25f,-5.11f),.07f,"Steel");
            for(int s=-1;s<=1;s+=2){B(p,"Service fluorescent housing",new Vector3(s*9.4f,4.92f,-4.5f),new Vector3(1.63f,.15f,.25f),"Steel",.035f);B(p,"Warm service strip light",new Vector3(s*9.4f,4.83f,-4.5f),new Vector3(1.41f,.045f,.18f),"Ivory",.02f);}
            ArtKit.Bake(p,"Env_AutoRepairWorkshop");
            if(w.radiusMarking)Object.DestroyImmediate(w.radiusMarking.gameObject);if(w.activeRepairEffect)Object.DestroyImmediate(w.activeRepairEffect);
            var ring=N(root.transform,"Mint service radius — mechanic retains configured metres");for(int i=0;i<32;i++){float a=i*360f/32;float r=a*Mathf.Deg2Rad;B(ring,"Dashed repair perimeter",new Vector3(Mathf.Cos(r),.052f,Mathf.Sin(r)),new Vector3(.025f,.012f,.118f),"Mint",.004f,new Vector3(0,-a,0));}ArtKit.Bake(ring,"Env_Repair_radius_unit");ring.localScale=new Vector3(w.healingRadius,1,w.healingRadius);w.radiusMarking=ring;
            var symbol=N(root.transform,"Painted mint mechanic wrench",new Vector3(0,.055f,0));Wrench(symbol,"Service wrench ground mark",Vector3.zero,1.7f,"Mint");ArtKit.Bake(symbol,"Env_Repair_wrench");
            var effect=N(root.transform,"Active repair — restrained mint service pulses",new Vector3(0,.075f,0));for(int i=0;i<3;i++){var t=N(effect,"Mint repair plus",new Vector3(-1.25f+i*1.25f,.035f,i%2==0?.75f:-.60f));B(t,"Repair plus arm",Vector3.zero,new Vector3(.55f,.025f,.13f),"Mint",.015f);B(t,"Repair plus stem",Vector3.zero,new Vector3(.13f,.025f,.55f),"Mint",.015f);}ArtKit.Bake(effect,"Env_ActiveRepairFeedback");effect.gameObject.SetActive(false);w.activeRepairEffect=effect.gameObject;
            EditorUtility.SetDirty(w);
        }
    }
}
