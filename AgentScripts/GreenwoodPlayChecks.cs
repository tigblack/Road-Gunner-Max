using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading.Tasks;using UnityEngine;using UnityEngine.AI;using RoadGunner;using RoadGunner.Editor;
public static class GreenwoodPlayChecks {
 static GameSession s;static LevelLayout l;static List<string> lines=new List<string>();static int failures;
 static void Check(string name,bool pass,string detail){if(!pass)failures++;lines.Add((pass?"PASS ":"FAIL ")+name+": "+detail);File.WriteAllLines(AstraZone01Builder.Report+"/PlayChecks.txt",lines);Debug.Log("GREENWOOD "+lines.Last());}
 static async Task Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)await Task.Delay(40);}
 static void Isolate(){s.Restart();s.spawns.enabled=false;s.turret.enabled=false;s.health.DamageEnabled=false;foreach(var r in s.spawns.pool)r.Despawn();foreach(var b in s.spawns.buggyPool)b.Despawn();s.input.testInput=true;s.input.testDirection=Vector2.zero;s.input.testBrake=true;s.vehicle.Body.constraints=RigidbodyConstraints.None;}
 static Vector2 Local(Vector3 world){var p=Quaternion.Inverse(Quaternion.Euler(0,45,0))*world;return new Vector2(p.x,p.z);}
 static async Task Drive(string name,Vector2 from,Vector2[] route,float yaw=0,float timeout=25){
 Isolate();s.vehicle.ResetPose(AstraZone01Builder.World(from.x,from.y,.7f),Quaternion.Euler(0,45+yaw,0));s.follow.Snap();await Wait(.5f);s.input.testBrake=false;
 int step=0;float end=Time.realtimeSinceStartup+timeout;int minWheels=4;while(step<route.Length&&Time.realtimeSinceStartup<end){var at=Local(s.vehicle.transform.position);Vector2 delta=route[step]-at;if(delta.magnitude<2.6f){step++;continue;}float throttle=Mathf.Lerp(.36f,.58f,Mathf.InverseLerp(3,10,delta.magnitude));s.input.testDirection=delta.normalized*throttle;minWheels=Math.Min(minWheels,s.vehicle.GroundedWheels);await Task.Delay(40);}
 s.input.testDirection=Vector2.zero;s.input.testBrake=true;await Wait(.35f);Check("drive "+name,step==route.Length&&minWheels>=3,"waypoints="+step+"/"+route.Length+", end="+Local(s.vehicle.transform.position)+", grounded minimum="+minWheels);
 }
 public static async Task<string> Main(){
 if(!UnityEditor.EditorApplication.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");l=UnityEngine.Object.FindAnyObjectByType<LevelLayout>();s=l.session;Application.runInBackground=true;
 try{
 s.Restart();s.turret.enabled=false;s.health.DamageEnabled=false;await Wait(.7f);
 Check("all placed enemies spawn",s.spawns.authoredRaiders.All(r=>r.Alive&&r.agent.isOnNavMesh),"alive="+s.spawns.authoredRaiders.Count(r=>r.Alive)+"/13");
 Check("spawn grounded",s.vehicle.GroundedWheels==4&&Vector2.Distance(Local(s.vehicle.transform.position),new Vector2(0,-48))<.3f,"grounded="+s.vehicle.GroundedWheels);
 await Drive("main axis",new Vector2(0,-48),new[]{new Vector2(1,-26),new Vector2(0,0),new Vector2(0,22),new Vector2(-1,42),new Vector2(0,58)},0,55);
 await Drive("southwest camp",new Vector2(0,-9),new[]{new Vector2(-11,-9),new Vector2(-22,-16)},-90,15);
 await Drive("west branch and arena",new Vector2(0,22),new[]{new Vector2(-17,22),new Vector2(-25,27),new Vector2(-28,33),new Vector2(-33,35)},-90,30);
 await Drive("northeast camp",new Vector2(0,36.5f),new[]{new Vector2(14,36.5f),new Vector2(24,40)},90,22);
 await Drive("sealed entrance approach",new Vector2(24,40),new[]{new Vector2(37,38)},90,12);
 await Drive("tree pocket bypass",new Vector2(26,43),new[]{new Vector2(32,47),new Vector2(33,53),new Vector2(38,57),new Vector2(41,58)},25,35);
 Isolate();s.vehicle.ResetPose(AstraZone01Builder.World(0,58,.7f),Quaternion.Euler(0,45,0));await Wait(.5f);s.input.testBrake=false;s.input.testDirection=new Vector2(0,.65f);await Wait(6);s.input.testDirection=Vector2.zero;s.input.testBrake=true;Check("north gate blocks actual car",Local(s.vehicle.transform.position).y<64,"position="+Local(s.vehicle.transform.position));
 Isolate();s.vehicle.ResetPose(AstraZone01Builder.World(-40,24,.7f),Quaternion.Euler(0,-45,0));await Wait(.5f);s.input.testBrake=false;s.input.testDirection=new Vector2(-.65f,0);await Wait(6);s.input.testDirection=Vector2.zero;s.input.testBrake=true;Check("west gate blocks actual car",Local(s.vehicle.transform.position).x>-48,"position="+Local(s.vehicle.transform.position));
 Isolate();s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;var r=s.spawns.authoredRaiders[0];r.Spawn(AstraZone01Builder.World(0,-42),-1,0);r.enabled=false;s.turret.enabled=true;int shots=s.turret.ShotsFired;float until=Time.realtimeSinceStartup+5;while(r.Alive&&Time.realtimeSinceStartup<until)await Task.Delay(40);Check("existing turret kills forest enemy",!r.Alive&&s.turret.ShotsFired>shots,"shots="+(s.turret.ShotsFired-shots));s.turret.enabled=false;
 until=Time.realtimeSinceStartup+8;while(s.Scrap<1&&Time.realtimeSinceStartup<until)await Task.Delay(40);Check("loot harpoon and HUD retained",s.Scrap==1&&s.pickups.ActiveCount==0&&s.hud.scrapText.text=="001","scrap="+s.Scrap+", pickups="+s.pickups.ActiveCount+", HUD="+s.hud.scrapText.text);r.enabled=true;
 Isolate();s.vehicle.ResetPose(AstraZone01Builder.World(6,-9,.7f),Quaternion.Euler(0,45,0));s.Recover();Check("sanctuary recovery marker",Vector2.Distance(Local(s.vehicle.transform.position),new Vector2(6,-12))<.1f,"recovered="+Local(s.vehicle.transform.position));
 s.spawns.enabled=true;s.turret.enabled=true;s.vehicle.Body.constraints=RigidbodyConstraints.None;s.Restart();await Wait(.3f);s.SetPaused(true);Vector3 before=s.vehicle.transform.position;await Wait(.3f);Check("pause preserved",s.State==SessionState.Paused&&Vector3.Distance(before,s.vehicle.transform.position)<.01f,"state="+s.State);s.SetPaused(false);s.Restart();await Wait(.3f);Check("restart repopulates level",s.spawns.authoredRaiders.All(e=>e.Alive)&&s.Scrap==0,"alive="+s.spawns.authoredRaiders.Count(e=>e.Alive));

 Isolate();s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;s.vehicle.ResetPose(AstraZone01Builder.World(12,-12,.7f),Quaternion.Euler(0,45,0));s.health.DamageEnabled=true;s.health.Damage(40,false);float damaged=s.health.Current;await Wait(1.1f);Check("pool heals in radius",s.health.Current>damaged+5&&s.health.Current<=s.health.tuning.vehicleHealth,"before="+damaged+", after="+s.health.Current);
 s.vehicle.ResetPose(AstraZone01Builder.World(6,-12,.7f),Quaternion.Euler(0,45,0));float outside=s.health.Current;await Wait(.7f);Check("pool does not heal outside",Mathf.Abs(s.health.Current-outside)<.01f,"health="+s.health.Current);
 s.vehicle.ResetPose(AstraZone01Builder.World(12,-12,.7f),Quaternion.Euler(0,45,0));s.SetPaused(true);float paused=s.health.Current;await Wait(.6f);Check("pool respects pause",Mathf.Abs(s.health.Current-paused)<.01f,"health="+s.health.Current);s.SetPaused(false);
 s.health.ResetHealth();s.health.Damage(2,false);await Wait(.7f);Check("pool clamps maximum health",s.health.Current==s.health.tuning.vehicleHealth,"health="+s.health.Current);
 }finally{foreach(var r in s.spawns.pool)r.enabled=true;s.vehicle.Body.constraints=RigidbodyConstraints.None;s.spawns.enabled=true;s.turret.enabled=true;s.Restart();}
 return "Failures="+failures+"\n"+string.Join("\n",lines);
 }
}