using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace RoadGunner.Editor
{
    public static partial class PrototypeBuilder
    {
        static Mesh softBox;
        static Sprite roundedPanel;
        static Mesh[] boulders;
        static Material grassGold,grassLight,stoneLight,stoneWarm,steelEdge,redPaint,trackMaterial;
        static Color Hex(string value) { ColorUtility.TryParseHtmlString("#"+value,out var c);return c; }

        static void PrepareScrapRushArt()
        {
            softBox=SaveMesh("Scrap Rush beveled block",BeveledBlock());
            grassGold=Mat("Scrap Rush ochre grass",Hex("BB7828"));
            grassLight=Mat("Scrap Rush sunlit grass",Hex("E9B653"));
            stoneLight=Mat("Scrap Rush pale sandstone",Hex("C68A65"));
            stoneWarm=Mat("Scrap Rush rose sandstone",Hex("B27B66"));
            steelEdge=Mat("Scrap Rush steel edges",Hex("626A65"),.12f);
            redPaint=Mat("Scrap Rush chipped red paint",Hex("D95035"),.05f);
            boulders=new Mesh[5];for(int i=0;i<boulders.Length;i++)boulders[i]=SaveMesh("Scrap Rush boulder "+i,Boulder(i+72));
            var texture=Texture("Wind brushed sand",512,(x,y,n)=>{
                float u=x/(float)n*Mathf.PI*2,v=y/(float)n*Mathf.PI*2;
                float waves=Mathf.Sin(u*3+v*5+Mathf.Sin(v*2)*1.4f);
                float strokes=Mathf.Pow(Mathf.Abs(Mathf.Sin(u*14+v*23+Mathf.Sin(u*3+v*2)*2)),8);
                float grain=Mathf.Sin(u*71+v*97)*Mathf.Sin(u*103-v*43);
                return Color.white*(.95f+waves*.025f+strokes*.016f+grain*.008f);
            });
            sand.SetTexture("_BaseMap",texture);sand.SetTextureScale("_BaseMap",Vector2.one);
            var dustTex=Texture("Soft golden dust",64,(x,y,n)=>{
                float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),Vector2.one*n*.5f)/(n*.5f);
                return new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d*d),2));
            },true);
            var particle=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Dust particles.mat");
            if(particle)particle.SetTexture("_BaseMap",dustTex);
            trackMaterial=Mat("Scrap Rush tire tracks",Hex("9C602C"));
            trackMaterial.shader=Shader.Find("RoadGunner/Sand Tracks");
            trackMaterial.SetTexture("_BaseMap",Texture("Tire tread imprint",64,(x,y,n)=>{
                float u=(x+.5f)/n,v=(y+.5f)/n;
                float chevron=Mathf.Repeat(v*4+Mathf.Abs(u-.5f)*1.8f,1);
                float edge=Mathf.SmoothStep(0,1,Mathf.Min(u,1-u)*10);
                return new Color(1,1,1,edge*(chevron<.52f?.58f:.18f));
            },true));
            foreach(var m in new[]{sand,rust,dark,rubber,cream,accent,glass,rock,cloth,skin,grassGold,grassLight,stoneLight,stoneWarm}){
                m.enableInstancing=true;m.SetFloat("_Smoothness",m==glass?.38f:.16f);EditorUtility.SetDirty(m);
            }
        }
        static Texture2D Texture(string name,int size,System.Func<int,int,int,Color> sample,bool alpha=false)
        {
            Directory.CreateDirectory(Root+"/Textures");string path=Root+"/Textures/"+name+".png";
            var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=sample(x,y,size);
            tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(path);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.alphaIsTransparency=alpha;imp.wrapMode=alpha?TextureWrapMode.Clamp:TextureWrapMode.Repeat;imp.mipmapEnabled=!alpha;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Sprite RoundedPanel()
        {
            if(roundedPanel)return roundedPanel;
            string path=Root+"/Textures/Rounded UI panel.png";
            if(!File.Exists(path))Texture("Rounded UI panel",64,(x,y,n)=>{
                var q=new Vector2(Mathf.Max(Mathf.Abs(x-31.5f)-16,0),Mathf.Max(Mathf.Abs(y-31.5f)-16,0));
                return new Color(1,1,1,Mathf.Clamp01(16-q.magnitude));
            },true);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(importer.textureType!=TextureImporterType.Sprite||importer.spriteImportMode!=SpriteImportMode.Single){importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=new Vector4(18,18,18,18);importer.SaveAndReimport();}
            return roundedPanel=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static void Face(List<Vector3> v,List<int> t,params Vector3[] face)
        {
            Vector3 center=Vector3.zero;foreach(var p in face)center+=p;
            bool reverse=Vector3.Dot(Vector3.Cross(face[1]-face[0],face[2]-face[0]),center)<0;
            int first=v.Count;v.AddRange(face);for(int i=1;i<face.Length-1;i++){t.Add(first);t.Add(first+(reverse?i+1:i));t.Add(first+(reverse?i:i+1));}
        }
        static Vector3 Axis(int axis,float value) { return axis==0?new Vector3(value,0,0):axis==1?new Vector3(0,value,0):new Vector3(0,0,value); }
        static Mesh BeveledBlock()
        {
            const float a=.5f,b=.42f;var v=new List<Vector3>();var t=new List<int>();
            for(int axis=0;axis<3;axis++)for(int s=-1;s<=1;s+=2){int j=(axis+1)%3,k=(axis+2)%3;var p=Axis(axis,s*a);Face(v,t,p+Axis(j,-b)+Axis(k,-b),p+Axis(j,b)+Axis(k,-b),p+Axis(j,b)+Axis(k,b),p+Axis(j,-b)+Axis(k,b));}
            for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)for(int s=-1;s<=1;s+=2)for(int q=-1;q<=1;q+=2){int k=3-i-j;Face(v,t,Axis(i,s*a)+Axis(j,q*b)+Axis(k,-b),Axis(i,s*a)+Axis(j,q*b)+Axis(k,b),Axis(i,s*b)+Axis(j,q*a)+Axis(k,b),Axis(i,s*b)+Axis(j,q*a)+Axis(k,-b));}
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)Face(v,t,new Vector3(x*a,y*b,z*b),new Vector3(x*b,y*a,z*b),new Vector3(x*b,y*b,z*a));
            var m=new Mesh();m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        static Mesh Boulder(int seed)
        {
            var random=new System.Random(seed);var vertices=new List<Vector3>();var triangles=new List<int>();
            float[] heights={-.50f,-.27f,.26f,.50f},radii={.64f,1,.86f,.51f};
            for(int ring=0;ring<4;ring++)for(int i=0;i<8;i++){
                float a=i*Mathf.PI/4+.12f*(ring%2);float r=radii[ring]*(.88f+(float)random.NextDouble()*.21f)*.5f;
                vertices.Add(new Vector3(Mathf.Cos(a)*r,heights[ring]+((float)random.NextDouble()-.5f)*.08f,Mathf.Sin(a)*r));
            }
            for(int ring=0;ring<3;ring++)for(int i=0;i<8;i++){int a=ring*8+i,b=ring*8+(i+1)%8,c=b+8,d=a+8;triangles.AddRange(new[]{a,d,b,b,d,c});}
            for(int i=1;i<7;i++)triangles.AddRange(new[]{24,24+i+1,24+i});
            return Faceted(vertices.ToArray(),triangles.ToArray());
        }
        static void BuildScrapRushArena(Transform root)
        {
            const int n=100;var mesh=new Mesh();var vertices=new Vector3[(n+1)*(n+1)];var uv=new Vector2[vertices.Length];var tris=new List<int>();
            for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){int i=z*(n+1)+x;float px=x-50,pz=z-50;vertices[i]=new Vector3(px,Height(px,pz),pz);uv[i]=new Vector2(px,pz)/14;}
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x;tris.AddRange(new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2});}
            mesh.vertices=vertices;mesh.uv=uv;mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var ground=MeshShape("Golden dunes • smooth drivable sand",root,SaveMesh("Rust basin terrain",mesh),Vector3.zero,Vector3.one,sand);
            ground.AddComponent<MeshCollider>().sharedMesh=ground.GetComponent<MeshFilter>().sharedMesh;ground.isStatic=true;
            var random=new System.Random(9471);
            Vector2[] spots={new Vector2(-12,18),new Vector2(13,12),new Vector2(24,-7),new Vector2(-22,-13),new Vector2(0,27),new Vector2(-30,22),new Vector2(28,27),new Vector2(13,-26),new Vector2(-12,-30),new Vector2(36,-25),new Vector2(14,5),new Vector2(-10,8),new Vector2(10,-13),new Vector2(-1,18)};
            for(int i=0;i<spots.Length;i++){
                var p=spots[i];Rock(root,p.x,p.y,new Vector3(3.6f,2.4f,3),i,true);
                for(int j=0;j<3;j++){float x=p.x+Rand(random,-2.8f,2.8f),z=p.y+Rand(random,-2.6f,2.6f);Rock(root,x,z,new Vector3(1.2f,.85f,1.1f),i+j,false);Grass(root,x+1,z-.7f,random);}
                if(i%3==0)Fence(root,new Vector3(p.x+2,Height(p.x+2,p.y),p.y),i*43);
            }
            for(int i=0;i<66;i++){float a=i*Mathf.PI*2/66,x=Mathf.Cos(a)*49,z=Mathf.Sin(a)*49;Rock(root,x,z,new Vector3(Rand(random,4,6),Rand(random,2.8f,4.8f),4.3f),i,true);Grass(root,x*.94f,z*.94f,random);}
            for(int i=0;i<4;i++){bool along=i<2;float sign=i%2==0?-1:1;var g=new GameObject("Invisible perimeter safety wall");g.transform.SetParent(root);g.transform.position=along?new Vector3(sign*51,3,0):new Vector3(0,3,sign*51);var box=g.AddComponent<BoxCollider>();box.size=along?new Vector3(2,9,104):new Vector3(104,9,2);g.isStatic=true;}
            // Small dressing has no colliders; combine meshes by material to keep the arena cheap.
            var dressing=new GameObject("Scattered pebbles and dry grass").transform;dressing.SetParent(root,false);
            for(int i=0;i<360;i++){
                float x=Rand(random,-46,46),z=Rand(random,-46,46);if(x*x+z*z<16)continue;
                float size=Rand(random,.12f,.42f);Rock(dressing,x,z,new Vector3(size,size*.65f,size*.8f),i,false);
                if(i%3==0)Grass(dressing,x+Rand(random,-1,1),z+Rand(random,-1,1),random);
            }
            // Visible fine dressing in the first camera framing.
            for(int i=0;i<75;i++){float x=Rand(random,-12,12),z=Rand(random,-12,12);if(x*x+z*z<9)continue;float size=Rand(random,.09f,.23f);Rock(dressing,x,z,new Vector3(size,size*.65f,size),i,false);if(i%9==0)Grass(dressing,x,z,random);}
            for(int i=0;i<15;i++){float a=i*2.4f;ScrapPile(dressing,Mathf.Cos(a)*(9+i*2),Mathf.Sin(a)*(9+i*2),random);}
            CombineDressing(dressing);
            for(int i=0;i<5;i++){float z=-7+i*4;Shape("Suspension lane buried plank",root,PrimitiveType.Cube,new Vector3(-17,Height(-17,z)+.03f,z),new Vector3(.18f,.05f,.8f),cream);}
        }
        static float Rand(System.Random r,float min,float max) { return Mathf.Lerp(min,max,(float)r.NextDouble()); }
        static void Rock(Transform root,float x,float z,Vector3 size,int variant,bool collision)
        {
            var g=MeshShape("Rounded sandstone",root,boulders[variant%5],new Vector3(x,Height(x,z)+size.y*.43f,z),size,variant%3==0?stoneLight:variant%3==1?rock:stoneWarm);
            g.transform.rotation=Quaternion.Euler(0,variant*137.5f,0);if(collision){var c=g.AddComponent<BoxCollider>();c.center=new Vector3(0,-.08f,0);c.size=new Vector3(.86f,.8f,.86f);}g.isStatic=true;
        }
        static void Grass(Transform root,float x,float z,System.Random random)
        {
            var tuft=new GameObject("Dry golden tuft").transform;tuft.SetParent(root,false);tuft.localPosition=new Vector3(x,Height(x,z)-.02f,z);
            for(int i=0;i<14;i++){float a=i*137.5f;var blade=MeshShape("Grass blade",tuft,cone,new Vector3(Mathf.Cos(a)*.12f,0,Mathf.Sin(a)*.12f),new Vector3(.12f,Rand(random,.3f,.76f),.10f),i%3==0?grassGold:grassLight);blade.transform.localRotation=Quaternion.Euler(Rand(random,8,37),a,Rand(random,-20,20));blade.isStatic=true;}
        }
        static void Fence(Transform parent,Vector3 position,float yaw)
        {
            var root=new GameObject("Rusted corrugated barricade").transform;root.SetParent(parent,false);root.localPosition=position;root.localRotation=Quaternion.Euler(0,yaw,0);
            for(int i=0;i<10;i++){float x=-1.6f+i*.35f;var sheet=Shape("Corrugated red steel",root,PrimitiveType.Cube,new Vector3(x,.85f,i%2*.08f),new Vector3(.36f,1.7f,.12f),i%3==0?redPaint:rust);sheet.transform.localRotation=Quaternion.Euler(0,0,(i%3-1)*3);}
            for(int i=-1;i<=1;i+=2){Shape("Steel fence post",root,PrimitiveType.Cube,new Vector3(i*1.85f,.94f,.1f),new Vector3(.23f,2.1f,.23f),dark);for(int j=0;j<3;j++)Shape("Fence rivet",root,PrimitiveType.Sphere,new Vector3(i*1.85f,.35f+j*.57f,-.06f),Vector3.one*.10f,steelEdge);}
            var collider=root.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.85f,0);collider.size=new Vector3(3.9f,1.7f,.35f);root.gameObject.isStatic=true;
        }
        static void ScrapPile(Transform parent,float x,float z,System.Random random)
        {
            var root=new GameObject("Salvage • sheet metal and tire").transform;root.SetParent(parent,false);root.localPosition=new Vector3(x,Height(x,z),z);root.localRotation=Quaternion.Euler(0,Rand(random,0,360),0);
            for(int i=0;i<3;i++){var plank=Shape("Broken cream plank",root,PrimitiveType.Cube,new Vector3(i*.24f-.2f,.10f,0),new Vector3(.16f,.11f,1.15f),cream);plank.transform.localRotation=Quaternion.Euler(0,i*31,0);}
            for(int i=0;i<5;i++){var metal=Shape("Scrap corrugation",root,PrimitiveType.Cube,new Vector3(-.35f+i*.15f,.19f,.04f),new Vector3(.16f,.10f,.85f),i%2==0?rust:redPaint);metal.transform.localRotation=Quaternion.Euler(15,0,0);}
            for(int i=0;i<10;i++){float a=i*Mathf.PI/5;var chunk=Shape("Discarded tire",root,PrimitiveType.Cube,new Vector3(.72f+Mathf.Cos(a)*.28f,.14f,Mathf.Sin(a)*.28f),new Vector3(.21f,.24f,.18f),rubber);chunk.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);}
        }
        static void CombineDressing(Transform root)
        {
            var batches=new Dictionary<Material,List<CombineInstance>>();var originals=new List<GameObject>();
            foreach(var f in root.GetComponentsInChildren<MeshFilter>()){
                var mat=f.GetComponent<MeshRenderer>().sharedMaterial;if(!batches.ContainsKey(mat))batches[mat]=new List<CombineInstance>();
                batches[mat].Add(new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix});originals.Add(f.gameObject);
            }
            foreach(var pair in batches){var mesh=new Mesh();mesh.indexFormat=IndexFormat.UInt32;mesh.CombineMeshes(pair.Value.ToArray());MeshShape("Batched "+pair.Key.name,root,SaveMesh("Dressing "+pair.Key.name,mesh),Vector3.zero,Vector3.one,pair.Key).isStatic=true;}
            foreach(var g in originals)Object.DestroyImmediate(g);
        }
        static void StyleScrapRushVehicle(VehicleController car)
        {
            var visual=car.transform.Find("Replaceable visuals");
            visual.Find("Armored hood").GetComponent<Renderer>().sharedMaterial=redPaint;
            visual.Find("Armored hood").GetComponent<MeshFilter>().sharedMesh=softBox;
            visual.Find("Long sculpted muscle body").GetComponent<Renderer>().sharedMaterial=rust;
            foreach(var child in visual.GetComponentsInChildren<Transform>()){
                if(child.name=="Headlamp")child.localPosition=new Vector3(child.localPosition.x,.72f,2.23f);
                if(child.name=="Grille")child.localPosition=new Vector3(0,.65f,2.22f);
                if(child.name=="Orange door stripe")child.GetComponent<Renderer>().sharedMaterial=dark;
                if(child.name=="Fuel can"){
                    child.GetComponent<Renderer>().sharedMaterial=redPaint;
                    Shape("Fuel cap",visual,PrimitiveType.Cylinder,child.localPosition+Vector3.up*.34f,new Vector3(.13f,.035f,.13f),dark);
                    Shape("Can embossed strap",visual,PrimitiveType.Cube,child.localPosition+new Vector3(0,0,-.211f),new Vector3(.035f,.42f,.022f),rust);
                }
            }
            for(int side=-1;side<=1;side+=2){
                for(int j=0;j<5;j++)Shape("Armor bolt",visual,PrimitiveType.Sphere,new Vector3(side*1.09f,.83f,-.96f+j*.30f),new Vector3(.055f,.075f,.075f),steelEdge);
                for(int j=0;j<4;j++){var chip=Shape("Chipped armor edge",visual,PrimitiveType.Cube,new Vector3(side*1.088f,.45f+j*.1f,-.86f+j*.38f),new Vector3(.013f,.045f,.10f),rust);chip.transform.localRotation=Quaternion.Euler(j*22,0,0);}
                Shape("Hood raised seam",visual,PrimitiveType.Cube,new Vector3(side*.69f,.96f,1.13f),new Vector3(.038f,.028f,1.28f),accent);
                Shape("Rear fender armor",visual,PrimitiveType.Cube,new Vector3(side*.94f,.57f,-1.45f),new Vector3(.35f,.22f,.93f),cream);
            }
            var scuff=Mat("Scrap Rush faded paint",Hex("E27B53"));
            for(int j=0;j<12;j++){
                float x=Mathf.Sin(j*2.4f)*.74f,z=.53f+(j%5)*.27f;
                var mark=Shape("Hood paint scuff",visual,PrimitiveType.Cube,new Vector3(x,.982f,z),new Vector3(.025f,.006f,.09f+(j%3)*.03f),scuff);mark.transform.localRotation=Quaternion.Euler(0,j*13,0);
            }
            for(int j=0;j<5;j++)Shape("Blower intake slit",visual,PrimitiveType.Cube,new Vector3(-.22f+j*.11f,1.04f,1.385f),new Vector3(.055f,.12f,.025f),rubber);
            for(int i=0;i<4;i++){
                var pivot=car.wheelVisuals[i];float side=i%2==0?-1:1;
                pivot.Find("Rubber tire").localScale=new Vector3(.96f,.24f,.96f);
                foreach(Transform t in pivot)if(t.name=="Tread cleat")t.gameObject.SetActive(false);
                for(int j=0;j<14;j++){float a=j*Mathf.PI*2/14;for(int row=-1;row<=1;row+=2){var tread=Shape("Chunky chevron tread",pivot,PrimitiveType.Cube,new Vector3(row*.12f,Mathf.Cos(a)*.46f,Mathf.Sin(a)*.46f),new Vector3(.23f,.075f,.15f),rubber);tread.transform.localRotation=Quaternion.Euler(a*Mathf.Rad2Deg,0,row*10);}}
                for(int j=0;j<5;j++){float a=j*Mathf.PI*2/5;Shape("Wheel hub bolt",pivot,PrimitiveType.Sphere,new Vector3(side*.24f,Mathf.Cos(a)*.12f,Mathf.Sin(a)*.12f),Vector3.one*.06f,steelEdge);}
            }
            var tracks=car.gameObject.AddComponent<TireTracks>();tracks.vehicle=car;tracks.material=trackMaterial;
            SetLayer(car.transform,vehicleLayer);
        }
        static void StyleScrapRushRaider(Raider raider)
        {
            var v=raider.visual;v.Find("Head").localScale=new Vector3(.61f,.53f,.53f);
            v.Find("Head").localPosition=new Vector3(0,1.77f,0);
            v.Find("Scarf").localScale=new Vector3(.62f,.27f,.53f);v.Find("Scarf").localPosition=new Vector3(0,1.59f,.10f);
            v.Find("Goggles").localScale=new Vector3(.66f,.21f,.14f);v.Find("Goggles").localPosition=new Vector3(0,1.86f,.28f);
            Object.DestroyImmediate(v.Find("Orange lenses").gameObject);
            MeshShape("Red head wrap",v,boulders[1],new Vector3(0,2.01f,-.015f),new Vector3(.65f,.24f,.57f),cloth);
            for(int side=-1;side<=1;side+=2){
                Shape("Goggle rim",v,PrimitiveType.Cube,new Vector3(side*.17f,1.86f,.357f),new Vector3(.25f,.19f,.08f),steelEdge);
                Shape("Smoky teal goggle lens",v,PrimitiveType.Cube,new Vector3(side*.17f,1.86f,.405f),new Vector3(.18f,.125f,.025f),glass);
                Shape("Lens glint",v,PrimitiveType.Cube,new Vector3(side*.17f-.035f,1.888f,.422f),new Vector3(.055f,.022f,.012f),cream);
                var strap=Shape("Harness strap",v,PrimitiveType.Cube,new Vector3(side*.21f,1.2f,.262f),new Vector3(.12f,.59f,.06f),rust);strap.transform.localRotation=Quaternion.Euler(0,0,side*9);
                Shape("Utility pouch",v,PrimitiveType.Cube,new Vector3(side*.28f,.89f,.30f),new Vector3(.24f,.22f,.16f),grassGold);
                var arm=side<0?raider.leftArm:raider.rightArm;
                Shape("Work glove",arm,PrimitiveType.Cube,new Vector3(0,-.54f,.015f),new Vector3(.28f,.23f,.28f),rust);
            }
            raider.torso.sharedMaterial=cream;
            v.Find("Ragged torso").localScale=new Vector3(.80f,.77f,.50f);
            Shape("Belt buckle",v,PrimitiveType.Cube,new Vector3(0,.86f,.29f),new Vector3(.17f,.14f,.06f),cream);
            Object.DestroyImmediate(raider.rightArm.Find("Crowbar grip").gameObject);Object.DestroyImmediate(raider.rightArm.Find("Crowbar hook").gameObject);
            var pipe=Shape("Raised crowbar shaft",raider.rightArm,PrimitiveType.Cylinder,new Vector3(.04f,-.08f,.22f),new Vector3(.09f,.60f,.09f),steelEdge);pipe.transform.localRotation=Quaternion.Euler(-13,0,0);
            var hook=Shape("Crowbar hooked tip",raider.rightArm,PrimitiveType.Cube,new Vector3(.12f,.51f,.08f),new Vector3(.22f,.11f,.11f),steelEdge);hook.transform.localRotation=Quaternion.Euler(0,0,-25);
            MeshShape("Scarf folded point",v,cone,new Vector3(0,1.58f,.4f),new Vector3(.39f,.36f,.10f),cloth).transform.localRotation=Quaternion.Euler(180,0,0);
        }
        static void ScrapRushLighting(Camera camera)
        {
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            camera.allowHDR=true;
            var fill=new GameObject("Soft sky fill • cartoon form").AddComponent<Light>();
            fill.type=LightType.Directional;fill.color=new Color(.72f,.82f,1);fill.intensity=.45f;fill.shadows=LightShadows.None;fill.transform.rotation=Quaternion.Euler(38,145,0);
            string path=Root+"/Settings/Scrap Rush color.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            if(!profile.TryGet<ColorAdjustments>(out var color)){color=profile.Add<ColorAdjustments>();AssetDatabase.AddObjectToAsset(color,profile);}
            color.postExposure.Override(.2f);color.contrast.Override(5);color.saturation.Override(4);color.hueShift.Override(0);
            if(!profile.TryGet<Tonemapping>(out var tonemap)){tonemap=profile.Add<Tonemapping>();AssetDatabase.AddObjectToAsset(tonemap,profile);}
            tonemap.mode.Override(TonemappingMode.Neutral);
            EditorUtility.SetDirty(profile);EditorUtility.SetDirty(color);EditorUtility.SetDirty(tonemap);
            var volume=new GameObject("Scrap Rush • warm cartoon grading").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
        }
    }
}
