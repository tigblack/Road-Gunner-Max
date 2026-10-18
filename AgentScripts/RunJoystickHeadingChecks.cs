using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using RoadGunner;
using RoadGunner.Editor;

public static class RunJoystickHeadingChecks
{
    public static async Task<string> Main()
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        const string source="Documentation/RoadGunner/JoystickHeadingResults.txt";
        File.WriteAllText(source,"");
        var session=UnityEngine.Object.FindAnyObjectByType<GameSession>();
        Application.runInBackground=true;
        EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
        var checks=session.gameObject.AddComponent<JoystickHeadingChecks>();
        // Open crossroads: heading checks need room for the forward turning radius.
        checks.Begin(AstraZone01Builder.World(0,22,.7f));
        float deadline=Time.realtimeSinceStartup+55;
        try
        {
            string report="";
            while(Time.realtimeSinceStartup<deadline)
            {
                await Task.Delay(100);
                report=File.ReadAllText(source);
                if(report.Contains("PASS completed:"))
                {
                    File.WriteAllText("Documentation/RoadGunner/Joystick-Reverse-2026-10-10/HeadingChecks.txt",report);
                    return report;
                }
            }
            throw new TimeoutException("Heading checks did not complete.\n"+report);
        }
        finally
        {
            var check=session.GetComponent<JoystickHeadingChecks>();
            if(check) { check.StopAllCoroutines(); UnityEngine.Object.Destroy(check); }
            InputSystem.QueueStateEvent(Mouse.current,new MouseState());
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
            session.spawns.enabled=true; session.turret.enabled=true; session.vehicle.Body.constraints=RigidbodyConstraints.None;
            session.Restart();
        }
    }
}
