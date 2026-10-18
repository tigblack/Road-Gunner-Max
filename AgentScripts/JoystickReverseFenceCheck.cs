using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using RoadGunner;
using RoadGunner.Editor;

public static class JoystickReverseFenceCheck
{
    const string Report = "Documentation/RoadGunner/Joystick-Reverse-2026-10-10";
    static GameSession session;
    static Vector2 origin;
    static float radius;
    static Vector2 Local(Vector3 p) { var q = Quaternion.Inverse(Quaternion.Euler(0,45,0))*p; return new Vector2(q.x,q.z); }
    static void Pointer(Vector2 direction, bool held = true)
    {
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position=origin+direction*radius, buttons=(ushort)(held?1:0) });
    }
    static async Task Wait(float seconds)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<until) await Task.Delay(25);
    }
    public static async Task<string> Main(string label)
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        session=UnityEngine.Object.FindAnyObjectByType<GameSession>();
        Application.runInBackground=true;
        EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
        session.Restart(); session.spawns.enabled=false; session.turret.enabled=false; session.health.DamageEnabled=false;
        foreach(var r in session.spawns.pool) r.Despawn();
        foreach(var b in session.spawns.buggyPool) b.Despawn();
        session.vehicle.ResetPose(AstraZone01Builder.World(14,42,.7f),Quaternion.Euler(0,-45,0));
        session.follow.Snap(); Physics.SyncTransforms();
        try
        {
            await Wait(.7f);
            var joy=session.input.joystick;
            origin=RectTransformUtility.WorldToScreenPoint(null,joy.ring.position);
            radius=Vector2.Distance(origin,RectTransformUtility.WorldToScreenPoint(null,joy.ring.TransformPoint(Vector3.right*joy.radius)));
            Pointer(Vector2.zero,false); await Wait(.1f);
            Pointer(Vector2.zero); await Wait(.1f);
            Pointer(Vector2.left); await Wait(2.5f);
            bool routed=joy.IsHeld&&session.input.Direction.x<-.99f&&session.input.Magnitude>.99f;
            var stuck=Local(session.vehicle.transform.position); float stuckSpeed=session.vehicle.ForwardSpeed;
            Pointer(Vector2.right); await Wait(.15f);
            bool reverse=session.input.Reverse;
            float steer=session.vehicle.wheels[0].steerAngle;
            await Wait(1.9f);
            var escaped=Local(session.vehicle.transform.position);
            float backwardSpeed=session.vehicle.ForwardSpeed;
            int grounded=session.vehicle.GroundedWheels;
            bool pass=routed&&Mathf.Abs(stuckSpeed)<.6f&&reverse&&escaped.x-stuck.x>2&&backwardSpeed<-1&&grounded>=3;
            string result=(pass?"PASS":"FAIL")+" joystick backs away from northeast palisade: routed="+routed+", stuck="+stuck+", stuck forward speed="+stuckSpeed.ToString("F2")+", rearward stick reverse="+reverse+", steer="+steer.ToString("F2")+", escaped="+escaped+", distance east="+(escaped.x-stuck.x).ToString("F2")+", backward speed="+backwardSpeed.ToString("F2")+", grounded="+grounded;
            Directory.CreateDirectory(Report); File.WriteAllText(Report+"/Fence-"+label+".txt",result+"\nReal Input System mouse events routed through the UI joystick; WheelCollider vehicle physics. No Recover or teleport during escape.\n");
            return result;
        }
        finally
        {
            Pointer(Vector2.zero,false);
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
            session.spawns.enabled=true; session.turret.enabled=true; session.vehicle.Body.constraints=RigidbodyConstraints.None;
            session.Restart(); await Wait(.1f);
        }
    }
}
