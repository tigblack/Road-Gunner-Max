using System;using System.IO;using System.Linq;using System.Threading.Tasks;using UnityEngine;using UnityEditor;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using RoadGunner;
public static class Zone1Capture {
 static async Task Wait(float n){float end=Time.realtimeSinceStartup+n;while(Time.realtimeSinceStartup<end)await Task.Delay(20);}
 public static async Task<string> Main(string mode,string label){
 if(!EditorApplication.isPlaying)throw new Exception("Enter Play first");Application.runInBackground=true;
 var s=UnityEngine.Object.FindAnyObjectByType<GameSession>();s.vehicle.Body.constraints=RigidbodyConstraints.None;foreach(var b in s.spawns.buggyPool)if(b)b.GetComponent<Rigidbody>().constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.Restart();s.spawns.enabled=false;s.turret.enabled=false;s.health.DamageEnabled=false;
 foreach(var r in s.spawns.pool)if(r){r.enabled=false;if(r.agent.enabled&&r.agent.isOnNavMesh)r.agent.isStopped=true;}
 foreach(var b in s.spawns.buggyPool)if(b){b.AIEnabled=false;if(b.gun)b.gun.CanShoot=false;}
 var m=(WastelandMutant)s.zone1.bosses[0];var t=(TruckArenaBoss)s.zone1.bosses[1];m.AIEnabled=false;t.AIEnabled=false;
 var axis=Quaternion.Euler(0,45,0);var actor=mode.StartsWith("mutant")?m.transform:mode.StartsWith("truck")?t.transform:mode.StartsWith("buggy")?s.spawns.authoredBuggies[0].transform:s.spawns.authoredRaiders[0].transform;
 var at=actor.position;var direction=axis*Vector3.forward;
 var carAt=at-direction*(mode.StartsWith("truck")?9:7);carAt.y=.7f;s.vehicle.ResetPose(carAt,Quaternion.LookRotation(direction));s.input.testInput=true;s.input.testBrake=true;s.input.testDirection=Vector2.zero;
 await Wait(.6f);s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;foreach(var b in s.spawns.authoredBuggies)b.Body.constraints=RigidbodyConstraints.FreezeAll;s.follow.Snap();
 bool close=mode.EndsWith("close");bool wide=mode=="roster";
 if(mode.EndsWith("action")){s.turret.enabled=true;if(mode.StartsWith("mutant"))m.AIEnabled=true;else t.AIEnabled=true;float until=Time.realtimeSinceStartup+4;while(Time.realtimeSinceStartup<until){if(mode.StartsWith("mutant")?m.IsTelegraphing&&m.AttackProgress>.4f:t.IsTelegraphing&&t.AttackProgress>.4f)break;await Wait(.025f);}s.follow.Snap();}
 if(mode=="truck-aim"){t.AIEnabled=true;await Wait(1.4f);t.AIEnabled=false;}
 var src=s.follow.GetComponent<Camera>();var go=new GameObject("Temporary zone 1 visual QA");var cam=go.AddComponent<Camera>();cam.CopyFrom(src);cam.transform.SetPositionAndRotation(src.transform.position,src.transform.rotation);cam.enabled=false;cam.aspect=720f/1280;var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=src.GetUniversalAdditionalCameraData().renderPostProcessing;data.renderShadows=true;
 int width=close?1200:720,height=close?1000:1280;
 if(close){var rot=Quaternion.Euler(mode.StartsWith("mutant")?24:29,actor.eulerAngles.y+150,0);s.vehicle.ResetPose(at+Vector3.right*30+Vector3.up*.7f,Quaternion.identity);cam.orthographic=true;cam.orthographicSize=mode.StartsWith("truck")?5.1f:mode.StartsWith("buggy")?3.1f:mode.StartsWith("mutant")?2.5f:1.7f;cam.aspect=width/(float)height;cam.transform.SetPositionAndRotation(actor.position+Vector3.up*(mode.StartsWith("truck")?1.9f:mode.StartsWith("mutant")?1.55f:.9f)-rot*Vector3.forward*40,rot);}
 var rt=new RenderTexture(width,height,24){antiAliasing=2};rt.Create();cam.targetTexture=rt;var canvas=s.hud.GetComponentInParent<Canvas>();var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;float plane=canvas.planeDistance;bool oldEnabled=canvas.enabled;var active=RenderTexture.active;
 try{if(close)canvas.enabled=false;else{canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=1;}await Wait(.15f);Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();string path="/Users/arthuri/Documents/Codex/2026-10-09/macbook-app-store/outputs/zone1-enemies-2026-10-17/"+label+"-"+mode+".png";File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.Destroy(tex);return path;}
 finally{canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;canvas.enabled=oldEnabled;RenderTexture.active=active;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(go);Canvas.ForceUpdateCanvases();}
 }
}
