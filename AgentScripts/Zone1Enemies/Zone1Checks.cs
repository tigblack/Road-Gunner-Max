using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using RoadGunner;
using Object = UnityEngine.Object;

/// <summary>Run in Play with args ["core"] or ["truck"]. Each mode is normally 20–35 real seconds.</summary>
public static class Zone1Checks
{
    static GameSession s;
    static WastelandMutant mutant;
    static TruckArenaBoss truck;
    static readonly List<string> lines = new List<string>();
    static int failures;
    static string report;
    static float started;
    static void Check(string name, bool pass, string detail)
    {
        if (!pass) failures++;
        string line = (pass ? "PASS " : "FAIL ") + name + ": " + detail;
        lines.Add(line); File.WriteAllLines(report, lines); Debug.Log("ZONE1 " + line);
    }
    static async Task Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) await Task.Delay(25);
    }
    static async Task<bool> Until(Func<bool> condition, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) { if (condition()) return true; await Task.Delay(25); }
        return condition();
    }
    static float CarNose
    {
        get { var box = s.vehicle.GetComponent<BoxCollider>(); return box ? box.size.z * .5f + Mathf.Abs(box.center.z) : 2.2f; }
    }
    static void PlaceCar(Vector3 at, Quaternion rotation)
    {
        if (NavMesh.SamplePosition(at, out var ground, 2, NavMesh.AllAreas)) at = ground.position;
        at.y += .7f;
        s.vehicle.Body.constraints = RigidbodyConstraints.None;
        s.vehicle.ResetPose(at, rotation);
        s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
        s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
        Physics.SyncTransforms(); s.follow.Snap();
    }
    static void Isolate(CombatTarget keep)
    {
        s.spawns.enabled = true; s.turret.enabled = true;
        foreach (var raider in s.spawns.pool) if (raider) raider.enabled = true;
        s.vehicle.Body.constraints = RigidbodyConstraints.None;
        s.Restart(); s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
        foreach (var raider in s.spawns.pool) if (raider) raider.Despawn();
        if (s.spawns.buggyPool != null) foreach (var buggy in s.spawns.buggyPool) if (buggy) buggy.Despawn();
        if (keep != mutant) mutant.Despawn(); else mutant.AIEnabled = false;
        if (keep != truck) truck.Despawn(); else truck.AIEnabled = false;
        s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
    }
    static void PutInMutantReach()
    {
        Vector3 forward = mutant.transform.forward;
        PlaceCar(mutant.transform.position + forward * (CarNose + 1.4f), Quaternion.LookRotation(-forward));
    }
    static async Task TurretCheck(CombatTarget target)
    {
        Isolate(target); await Wait(.12f);
        PlaceCar(target.transform.position - target.transform.forward * 7.2f, Quaternion.LookRotation(target.transform.forward));
        float before = target is WastelandMutant m ? m.Health : ((TruckArenaBoss)target).Health;
        int shots = s.turret.ShotsFired; s.turret.enabled = true;
        bool hit = await Until(() => (target is WastelandMutant mm ? mm.Health : ((TruckArenaBoss)target).Health) < before, 5);
        float after = target is WastelandMutant m2 ? m2.Health : ((TruckArenaBoss)target).Health;
        Check("real turret acquisition / " + target.GetType().Name, hit && s.turret.ShotsFired > shots,
            "HP " + before + " -> " + after + ", shots=" + (s.turret.ShotsFired - shots) + ", target=" + (s.turret.Target ? s.turret.Target.GetType().Name : "none"));
        s.turret.enabled = false; await Wait(.15f);
        var hud = Object.FindAnyObjectByType<Zone1BossHud>();
        Check("nearest boss HUD / " + target.GetType().Name, hud && hud.IsVisible && hud.Target == target && hud.titleText && !string.IsNullOrEmpty(hud.titleText.text) &&
            hud.healthFill && hud.healthFill.type == UnityEngine.UI.Image.Type.Filled,
            hud ? "visible=" + hud.IsVisible + ", title=" + (hud.titleText ? hud.titleText.text : "unbound") + ", target=" + (hud.Target ? hud.Target.GetType().Name : "none") : "HUD missing");
    }
    static async Task Core()
    {
        s.spawns.enabled = true; s.turret.enabled = false;
        foreach (var raider in s.spawns.pool) if (raider) raider.enabled = true;
        foreach(var b in s.spawns.buggyPool)if(b)b.GetComponent<Rigidbody>().constraints=RigidbodyConstraints.None; s.Restart(); s.health.DamageEnabled = false; await Wait(.65f);
        bool nav = mutant.Alive && truck.Alive && mutant.agent.enabled && mutant.agent.isOnNavMesh && truck.agent.enabled && truck.agent.isOnNavMesh;
        Check("both bosses alive on actual NavMesh", nav, "mutant=" + mutant.State + ", truck=" + truck.State);
        int expected = s.spawns.pool.Count(r => r && r.Alive) + (s.spawns.buggyPool == null ? 0 : s.spawns.buggyPool.Count(b => b && b.Alive)) + s.zone1.ActiveBossCount;
        Check("active count includes bosses once", s.spawns.ActiveCount == expected, "reported=" + s.spawns.ActiveCount + ", counted=" + expected);
        await TurretCheck(mutant); await TurretCheck(truck);

        Isolate(mutant); PutInMutantReach(); s.health.DamageEnabled = true; mutant.AIEnabled = true;
        float hp = s.health.Current;
        bool warning = await Until(() => mutant.IsTelegraphing, 3.5f);
        Check("mutant warns before damage", warning && s.health.Current == hp && mutant.SmashesLanded == 0,
            "warning=" + warning + ", HP=" + s.health.Current + ", progress=" + mutant.AttackProgress.ToString("F2"));
        bool landed = await Until(() => mutant.SmashesLanded > 0, 2.2f);
        Check("mutant visible smash deals heavy melee damage", landed && Mathf.Abs((hp - s.health.Current) - mutant.smashDamage) < .1f,
            "damage=" + (hp - s.health.Current) + ", configured=" + mutant.smashDamage + ", landed=" + mutant.SmashesLanded);

        Isolate(mutant); PutInMutantReach(); s.health.DamageEnabled = true; mutant.AIEnabled = true;
        warning = await Until(() => mutant.IsTelegraphing, 3.5f);
        hp = s.health.Current;
        PlaceCar(mutant.transform.position + mutant.transform.right * 8.5f, Quaternion.LookRotation(-mutant.transform.right));
        await Until(() => !mutant.IsTelegraphing, 2.1f); await Wait(.12f);
        Check("moving outside locked smash area dodges", warning && s.health.Current == hp && mutant.SmashesLanded == 0,
            "HP=" + s.health.Current + ", landed=" + mutant.SmashesLanded);

        Isolate(mutant); PutInMutantReach(); s.health.DamageEnabled = true; mutant.AIEnabled = true;
        warning = await Until(() => mutant.IsTelegraphing, 3.5f); hp = s.health.Current;
        s.SetPaused(true); await Wait(.3f);
        bool hidden = !mutant.telegraph || !mutant.telegraph.activeInHierarchy;
        Check("pause cancels mutant warning without damage", warning && !mutant.IsTelegraphing && hidden && hp == s.health.Current,
            "state=" + mutant.State + ", warningVisible=" + !hidden + ", HP=" + s.health.Current);
        s.SetPaused(false);
        PlaceCar(mutant.transform.position + mutant.transform.right * 8.5f, Quaternion.identity); await Wait(.25f);
        Check("resume gives no cached instant smash", s.Playing && s.health.Current == hp, "HP=" + s.health.Current);

        Isolate(mutant); PutInMutantReach(); s.health.DamageEnabled = true; mutant.AIEnabled = true;
        warning = await Until(() => mutant.IsTelegraphing, 3.5f);
        s.health.Damage(s.health.tuning.vehicleHealth + 1, false); await Wait(.15f);
        Check("player defeat cancels boss attack and ground warning", warning && s.State == RoadGunner.SessionState.Defeated && s.health.Current == 0 && !mutant.IsTelegraphing &&
            (!mutant.telegraph || !mutant.telegraph.activeInHierarchy), "session=" + s.State + ", mutant=" + mutant.State + ", HP=" + s.health.Current);

        Isolate(mutant);
        int kills = s.Kills, earned = s.ScrapEarned;
        mutant.Hit(mutant.MaximumHealth + 1, Vector3.forward);
        mutant.Hit(mutant.MaximumHealth + 1, Vector3.forward);
        Check("mutant death rewards exactly once", !mutant.Alive && mutant.RewardGranted && s.Kills == kills + 1 && s.ScrapEarned == earned + mutant.scrapReward,
            "killsDelta=" + (s.Kills - kills) + ", earnedDelta=" + (s.ScrapEarned - earned));
        await Wait(mutant.corpseLifetime + .2f);
        Check("mutant corpse despawns without automatic respawn", !mutant.gameObject.activeSelf, "active=" + mutant.gameObject.activeSelf);
        s.Restart(); await Wait(.15f);
        Check("restart restores both authored bosses", mutant.Alive && truck.Alive && mutant.Health == mutant.MaximumHealth && truck.Health == truck.MaximumHealth,
            "HP mutant=" + mutant.Health + ", truck=" + truck.Health);
    }
    static async Task Truck()
    {
        Isolate(truck); Quaternion expectedYaw = truck.turretYaw ? truck.turretYaw.localRotation : Quaternion.identity;
        Vector3 center = truck.arenaCenter ? truck.arenaCenter.position : truck.transform.position;
        PlaceCar(center + Vector3.forward * 32, Quaternion.identity); truck.AIEnabled = true;
        Vector3 before = truck.transform.position;
        Quaternion wheelBefore = truck.wheelVisuals != null && truck.wheelVisuals.Length > 0 ? truck.wheelVisuals[0].localRotation : Quaternion.identity;
        float maximumRadius = 0, end = Time.realtimeSinceStartup + 2.4f;
        while (Time.realtimeSinceStartup < end)
        {
            maximumRadius = Mathf.Max(maximumRadius, Mathf.Sqrt(AttackRange.SquaredDistance(truck.transform.position, center))); await Task.Delay(25);
        }
        bool wheelMoved = truck.wheelVisuals != null && truck.wheelVisuals.Length > 0 && Quaternion.Angle(wheelBefore, truck.wheelVisuals[0].localRotation) > 2;
        Check("truck patrol moves and spins real wheel pivots", Vector3.Distance(before, truck.transform.position) > .15f && wheelMoved,
            "distance=" + Vector3.Distance(before, truck.transform.position).ToString("F2") + ", wheelMoved=" + wheelMoved);
        Check("actual NavMesh movement stays inside arena leash", truck.agent.isOnNavMesh && maximumRadius <= truck.arenaRadius + .2f,
            "max center radius=" + maximumRadius.ToString("F2") + ", leash=" + truck.arenaRadius);

        Isolate(truck);
        Vector3 flank = truck.transform.right;
        PlaceCar(truck.transform.position + flank * 7, Quaternion.LookRotation(-flank));
        s.health.DamageEnabled = true; truck.AIEnabled = true; float hp = s.health.Current;
        bool gunWarning = await Until(() => truck.IsGunTelegraphing, 3.5f);
        Check("truck gun warms up before first bullet", gunWarning && truck.ShotsFired == 0 && s.health.Current == hp,
            "warning=" + gunWarning + ", shots=" + truck.ShotsFired);
        bool burst = await Until(() => truck.ShotsFired >= 3, 4.5f);
        int shots = truck.ShotsFired; await Wait(.4f);
        Check("truck fires a short physical-ray burst then rests", burst && shots == 3 && truck.ShotsFired == shots && truck.PlayerHits > 0 && s.health.Current < hp,
            "shots=" + truck.ShotsFired + ", playerHits=" + truck.PlayerHits + ", damage=" + (hp - s.health.Current));
        center = truck.arenaCenter ? truck.arenaCenter.position : center;
        PlaceCar(center + flank * 28, Quaternion.identity); shots = truck.ShotsFired; hp = s.health.Current; await Wait(1.1f);
        Check("truck does not attack beyond the player's range", truck.ShotsFired == shots && s.health.Current == hp,
            "shotsDelta=" + (truck.ShotsFired - shots) + ", HP=" + s.health.Current);

        Isolate(truck); flank = truck.transform.right;
        PlaceCar(truck.transform.position + flank * 7, Quaternion.LookRotation(-flank)); s.health.DamageEnabled = true; truck.AIEnabled = true;
        gunWarning = await Until(() => truck.IsGunTelegraphing, 3.5f); hp = s.health.Current;
        s.SetPaused(true); await Wait(.3f);
        Check("pause clears truck gun/ram warnings and muzzle flash", gunWarning && !truck.IsTelegraphing && (!truck.flash || !truck.flash.gameObject.activeSelf) && hp == s.health.Current,
            "state=" + truck.State + ", HP=" + s.health.Current);
        s.SetPaused(false); PlaceCar(center + flank * 28, Quaternion.identity); await Wait(.2f);

        Isolate(truck); Vector3 forward = truck.transform.forward;
        float nose = truck.frontRam ? Vector3.Dot(truck.frontRam.position - truck.transform.position, forward) : 3.5f;
        PlaceCar(truck.transform.position + forward * (nose + CarNose + 1.2f), Quaternion.LookRotation(-forward));
        s.health.DamageEnabled = true; truck.AIEnabled = true; hp = s.health.Current;
        bool ramWarning = await Until(() => truck.State == TruckBossState.RamWindup, 4);
        Check("truck warns before physical ram", ramWarning && s.health.Current == hp && truck.RamsLanded == 0,
            "warning=" + ramWarning + ", HP=" + s.health.Current + ", started=" + truck.RamsStarted);
        bool ramHit = await Until(() => truck.RamsLanded > 0, 3);
        Check("truck ram requires real front contact and hits once", ramHit && truck.RamsLanded == 1 && Mathf.Abs((hp - s.health.Current) - truck.ramDamage) < .1f,
            "landed=" + truck.RamsLanded + ", damage=" + (hp - s.health.Current) + ", state=" + truck.State);

        Isolate(truck); float health = truck.Health;
        truck.Hit(24, Vector3.forward, true); truck.Hit(24, Vector3.forward, true);
        Check("player ram damage callback debounces child contacts", Mathf.Abs(health - truck.Health - 24) < .1f,
            "two same-frame callbacks damage=" + (health - truck.Health));
        int kills = s.Kills, earned = s.ScrapEarned, explosions = s.effects.ExplosionsPlayed;
        truck.Hit(truck.MaximumHealth + 1, Vector3.forward); truck.Hit(truck.MaximumHealth + 1, Vector3.forward);
        Check("truck death reward and compact explosion occur once", !truck.Alive && truck.RewardGranted && s.Kills == kills + 1 && s.ScrapEarned == earned + truck.scrapReward && s.effects.ExplosionsPlayed == explosions + 1,
            "killsDelta=" + (s.Kills - kills) + ", earnedDelta=" + (s.ScrapEarned - earned) + ", explosionsDelta=" + (s.effects.ExplosionsPlayed - explosions));
        await Wait(truck.wreckLifetime + .2f);
        Check("truck wreck expires without automatic respawn", !truck.gameObject.activeSelf, "active=" + truck.gameObject.activeSelf);
        s.Restart(); await Wait(.15f);
        Check("restart restores boss HP, wheels and gun rest pose", truck.Alive && truck.Health == truck.MaximumHealth && truck.ShotsFired == 0 &&
            (!truck.turretYaw || Quaternion.Angle(expectedYaw, truck.turretYaw.localRotation) < .1f),
            "HP=" + truck.Health + ", shots=" + truck.ShotsFired + ", yawDelta=" + (truck.turretYaw ? Quaternion.Angle(expectedYaw, truck.turretYaw.localRotation).ToString("F2") : "unbound"));
    }
    public static async Task<string> Main(string mode)
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play before Zone1Checks");
        if (mode != "core" && mode != "truck") throw new ArgumentException("Mode must be core or truck");
        s = Object.FindAnyObjectByType<GameSession>();
        if (!s || !s.zone1 || s.zone1.bosses == null) throw new Exception("GameSession.zone1 is not bound");
        mutant = s.zone1.bosses.OfType<WastelandMutant>().FirstOrDefault();
        truck = s.zone1.bosses.OfType<TruckArenaBoss>().FirstOrDefault();
        if (!mutant || !truck) throw new Exception("Both new authored bosses must be in the roster");
        string folder = "Documentation/RoadGunner/Zone1-Enemies-2026-10-17"; Directory.CreateDirectory(folder);
        report = folder + "/Checks-" + mode + ".txt"; lines.Clear(); failures = 0; started = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        try { if (mode == "core") await Core(); else await Truck(); }
        catch (Exception error) { Check("exception", false, error.ToString()); }
        finally
        {
            Time.timeScale = 1;
            foreach (var raider in s.spawns.pool) if (raider) raider.enabled = true;
            if (s.spawns.buggyPool != null) foreach (var buggy in s.spawns.buggyPool) if (buggy && buggy.Body) buggy.Body.constraints = RigidbodyConstraints.None;
            mutant.enabled = true; truck.enabled = true; mutant.AIEnabled = true; truck.AIEnabled = true;
            s.vehicle.Body.constraints = RigidbodyConstraints.None; s.spawns.enabled = true; s.turret.enabled = true; s.Restart();
        }
        lines.Add("Failures=" + failures + "; elapsed=" + (Time.realtimeSinceStartup - started).ToString("F2") + " real seconds. Final state: fresh Restart.");
        File.WriteAllLines(report, lines); return string.Join("\n", lines);
    }
}
