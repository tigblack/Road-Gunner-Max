using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using RoadGunner;
using RoadGunner.Editor;

public static class JoystickReverseStateChecks
{
    static GameSession s;
    static Vector2 origin;
    static float radius;
    static readonly List<string> results=new List<string>();
    static int failures;
    static void Record(string name,bool pass,string detail)
    {
        if(!pass) failures++;
        results.Add((pass?"PASS ":"FAIL ")+name+": "+detail);
        File.WriteAllLines("Documentation/RoadGunner/Joystick-Reverse-2026-10-10/StateChecks.txt",results);
    }
    static async Task Wait(float seconds)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<until) await Task.Delay(25);
    }
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(keys)); }
    static void Pointer(Vector2 direction,bool held=true)
    {
        InputSystem.QueueStateEvent(Mouse.current,new MouseState {position=origin+direction*radius,buttons=(ushort)(held?1:0)});
    }
    static Vector2 ScreenDirection(Vector3 world)
    {
        var camera=s.input.view.transform;
        return new Vector2(Vector3.Dot(world,Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized),Vector3.Dot(world,Vector3.ProjectOnPlane(camera.forward,Vector3.up).normalized)).normalized;
    }
    static async Task Press()
    {
        var joy=s.input.joystick;
        origin=RectTransformUtility.WorldToScreenPoint(null,joy.ring.position);
        radius=Vector2.Distance(origin,RectTransformUtility.WorldToScreenPoint(null,joy.ring.TransformPoint(Vector3.right*joy.radius)));
        Pointer(Vector2.zero,false); await Wait(.1f);
        Pointer(Vector2.zero); await Wait(.1f);
    }
    static void Isolate()
    {
        s.Restart(); s.spawns.enabled=false; s.turret.enabled=false; s.health.DamageEnabled=false;
        foreach(var r in s.spawns.pool) r.Despawn();
        foreach(var b in s.spawns.buggyPool) b.Despawn();
    }
    public static async Task<string> Main()
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        s=UnityEngine.Object.FindAnyObjectByType<GameSession>(); results.Clear(); failures=0;
        bool background=Application.runInBackground; Application.runInBackground=true;
        EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
        try
        {
            Keys();
            foreach(float yaw in new[]{-45f,45f,135f,225f,77f})
            {
                Pointer(Vector2.zero,false); await Wait(.1f); Isolate();
                var rotation=Quaternion.Euler(0,yaw,0);
                s.vehicle.ResetPose(AstraZone01Builder.World(0,-34,.7f),rotation); s.follow.Snap(); Physics.SyncTransforms();
                await Wait(.5f); await Press();
                var start=s.vehicle.transform.position;
                var backward=rotation*Vector3.back;
                Pointer(ScreenDirection(backward)); await Wait(1.1f);
                float distance=Vector3.Dot(s.vehicle.transform.position-start,backward);
                float error=Vector3.Angle(Vector3.ProjectOnPlane(-s.vehicle.transform.forward,Vector3.up),backward);
                Record("reverse follows chassis at yaw "+yaw,s.input.joystick.IsHeld&&s.input.Reverse&&s.input.UsesHeadingSteering&&distance>1&&s.vehicle.ForwardSpeed<-2&&error<3&&s.vehicle.GroundedWheels>=3,"distance="+distance.ToString("F2")+", speed="+s.vehicle.ForwardSpeed.ToString("F2")+", heading error="+error.ToString("F2"));
            }
            Pointer(Vector2.zero,false); await Wait(.15f);
            Record("pointer release clears reverse",!s.input.joystick.IsHeld&&s.input.Magnitude==0&&!s.input.Reverse,"held="+s.input.joystick.IsHeld+", throttle="+s.input.Magnitude);

            Isolate(); s.vehicle.ResetPose(AstraZone01Builder.World(0,-34,.7f),Quaternion.Euler(0,45,0));
            s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll; s.follow.Snap(); await Press();
            Pointer(Vector2.down); await Wait(.12f); bool reverseEntered=s.input.Reverse;
            Pointer(new Vector2(.94f,-.342f)); await Wait(.12f); bool rearSideStaysReverse=s.input.Reverse;
            Pointer(new Vector2(.999f,.04f)); await Wait(.12f); bool forwardSideLeavesReverse=!s.input.Reverse;
            Pointer(new Vector2(.94f,-.342f)); await Wait(.12f); bool rearSideStaysForward=!s.input.Reverse;
            Record("gear remains stable around sideways stick",reverseEntered&&rearSideStaysReverse&&forwardSideLeavesReverse&&rearSideStaysForward,"entered="+reverseEntered+", held rear side="+rearSideStaysReverse+", exited front side="+forwardSideLeavesReverse+", no chatter="+rearSideStaysForward);
            Pointer(Vector2.down); await Wait(.12f); Pointer(Vector2.down*.1f); await Wait(.12f);
            Record("dead zone clears reverse latch",s.input.Magnitude==0&&!s.input.Reverse,"throttle="+s.input.Magnitude+", reverse="+s.input.Reverse);
            Pointer(new Vector2(.94f,-.342f)); await Wait(.12f);
            Record("neutral restarts gear selection",s.input.Magnitude>0&&!s.input.Reverse,"reverse="+s.input.Reverse);

            Pointer(Vector2.down); await Wait(.12f); s.SetPaused(true); var pausedPosition=s.vehicle.transform.position; await Wait(.15f);
            Record("pause clears held reverse",!s.input.Enabled&&!s.input.Reverse&&s.input.Magnitude==0&&!s.input.joystick.IsHeld&&Vector3.Distance(pausedPosition,s.vehicle.transform.position)<.01f,"state="+s.State);
            s.SetPaused(false); await Wait(.15f);
            Record("resume waits for fresh drag",s.input.Enabled&&!s.input.Reverse&&s.input.Magnitude==0&&!s.input.joystick.IsHeld,"throttle="+s.input.Magnitude);
            await Press(); Pointer(Vector2.down); await Wait(.12f); s.Restart(); await Wait(.15f);
            Record("restart clears held reverse",!s.input.Reverse&&s.input.Magnitude==0&&!s.input.joystick.IsHeld,"throttle="+s.input.Magnitude+", reverse="+s.input.Reverse);

            Isolate(); s.vehicle.Body.constraints=RigidbodyConstraints.FreezeAll;
            s.vehicle.ResetPose(AstraZone01Builder.World(0,-34,.7f),Quaternion.Euler(0,137,0));
            Pointer(Vector2.zero,false); await Wait(.15f); Keys(Key.S,Key.D); await Wait(.4f);
            bool right=s.input.Reverse&&!s.input.UsesHeadingSteering&&s.vehicle.wheels[0].steerAngle>1;
            Keys(Key.S,Key.A); await Wait(.7f);
            Record("keyboard reverse steering preserved",right&&s.input.Reverse&&!s.input.UsesHeadingSteering&&s.vehicle.wheels[0].steerAngle<-1,"right response="+right+", left angle="+s.vehicle.wheels[0].steerAngle.ToString("F2"));
            Keys(); await Press(); Pointer(ScreenDirection(s.vehicle.transform.forward)); await Wait(.15f);
            Record("forward drag reengages forward gear",s.input.joystick.IsHeld&&s.input.Magnitude>.99f&&!s.input.Reverse,"reverse="+s.input.Reverse);
            return "Failures="+failures+"\n"+string.Join("\n",results);
        }
        finally
        {
            Pointer(Vector2.zero,false); Keys();
            s.vehicle.Body.constraints=RigidbodyConstraints.None; s.spawns.enabled=true; s.turret.enabled=true; s.Restart();
            Application.runInBackground=background;
            await Wait(.1f);
        }
    }
}
