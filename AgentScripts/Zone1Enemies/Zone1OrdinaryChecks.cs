using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using RoadGunner;
using Object = UnityEngine.Object;

/// <summary>Actual Play regression for existing actors/vehicle/collection/repair. Usually 25–40 real seconds.</summary>
public static class Zone1OrdinaryChecks
{
    static GameSession s;
    static readonly List<string> lines = new List<string>();
    static int failures;
    static string report;
    static void Check(string name, bool pass, string details)
    {
        if (!pass) failures++;
        string line = (pass ? "PASS " : "FAIL ") + name + ": " + details;
        lines.Add(line); File.WriteAllLines(report, lines); Debug.Log("ZONE1 ORDINARY " + line);
    }
    static async Task Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) await Task.Delay(25);
    }
    static async Task<bool> Until(Func<bool> condition, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) { if (condition()) return true; await Task.Delay(25); }
        return condition();
    }
    static Quaternion Facing(Vector3 from, Vector3 to)
    { Vector3 direction = to - from; direction.y = 0; return Quaternion.LookRotation(direction.sqrMagnitude > .001f ? direction : Vector3.forward); }
    static void RestoreMovableActors()
    {
        foreach (var raider in s.spawns.pool) if (raider) raider.enabled = true;
        if (s.spawns.buggyPool != null) foreach (var buggy in s.spawns.buggyPool) if (buggy)
        {
            buggy.enabled = true; buggy.AIEnabled = true;
            if (buggy.Body) buggy.Body.constraints = RigidbodyConstraints.None;
            if (buggy.gun) { buggy.gun.enabled = true; buggy.gun.CanShoot = true; }
        }
        if (s.zone1 && s.zone1.bosses != null) foreach (var boss in s.zone1.bosses)
        {
            if (boss is WastelandMutant mutant) { mutant.enabled = true; mutant.AIEnabled = true; }
            else if (boss is TruckArenaBoss truck) { truck.enabled = true; truck.AIEnabled = true; }
        }
        s.vehicle.Body.constraints = RigidbodyConstraints.None;
    }
    static void Isolate(EnemyBuggy keep = null)
    {
        RestoreMovableActors(); s.spawns.enabled = true; s.turret.enabled = true; s.Restart();
        s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
        foreach (var raider in s.spawns.pool) if (raider) raider.Despawn();
        if (s.spawns.buggyPool != null) foreach (var buggy in s.spawns.buggyPool) if (buggy && buggy != keep) buggy.Despawn();
        if (s.zone1) s.zone1.DespawnAll();
        s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
    }
    static void PlaceCar(Vector3 position, Quaternion rotation, bool freeze = true)
    {
        if (NavMesh.SamplePosition(position, out var ground, 2, NavMesh.AllAreas)) position = ground.position;
        position.y += .7f;
        s.vehicle.Body.constraints = RigidbodyConstraints.None;
        s.vehicle.ResetPose(position, rotation);
        if (freeze) s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
        s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
        Physics.SyncTransforms(); s.follow.Snap();
    }
    static bool HasMesh(CombatTarget target)
    {
        return target.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh && f.sharedMesh.vertexCount > 0) ||
            target.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.sharedMesh && r.sharedMesh.vertexCount > 0);
    }
    static void VisualAudit()
    {
        int renderers = 0, missingMaterials = 0, badShaders = 0, missingMeshes = 0;
        var details = new List<string>();
        foreach (var root in s.gameObject.scene.GetRootGameObjects()) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderers++;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) { missingMaterials++; if (details.Count < 8) details.Add(renderer.name + " has no materials"); }
            foreach (var material in materials)
            {
                if (!material) { missingMaterials++; if (details.Count < 8) details.Add(renderer.name + " missing material"); continue; }
                var shader = material.shader;
                if (!shader || !shader.isSupported || shader.name.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0)
                { badShaders++; if (details.Count < 8) details.Add(renderer.name + " shader=" + (shader ? shader.name : "missing")); }
            }
            if (renderer is MeshRenderer && !renderer.GetComponent<TextMesh>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || filter.sharedMesh.vertexCount == 0)
                { missingMeshes++; if (details.Count < 8) details.Add(renderer.name + " missing/empty mesh"); }
            }
            else if (renderer is SkinnedMeshRenderer skinned && (!skinned.sharedMesh || skinned.sharedMesh.vertexCount == 0)) missingMeshes++;
        }
        Check("whole scene / inactive pools have materials and supported shaders", missingMaterials == 0 && badShaders == 0 && missingMeshes == 0,
            "renderers=" + renderers + ", missing materials=" + missingMaterials + ", bad/pink shaders=" + badShaders + ", missing meshes=" + missingMeshes + (details.Count > 0 ? "; " + string.Join("; ", details) : ""));
        bool actorMeshes = s.spawns.pool.Where(r => r).All(HasMesh) && s.spawns.buggyPool.Where(b => b).All(HasMesh) && s.zone1.bosses.Where(b => b).All(HasMesh);
        Check("all reusable enemy actor types have actual 3D meshes", actorMeshes, "humans=" + s.spawns.pool.Count(r => r) + ", buggies=" + s.spawns.buggyPool.Count(b => b) + ", bosses=" + s.zone1.bosses.Length);
    }
    static async Task StartupAndDrive()
    {
        RestoreMovableActors(); s.spawns.enabled = true; s.turret.enabled = false; s.Restart(); s.health.DamageEnabled = false;
        await Until(() => s.spawns.authoredBuggies.All(b => b && b.Alive && b.GroundedWheels == 4), 3);
        Check("zone roster is 17 raiders + 2 authored buggies + 2 bosses",
            s.spawns.authoredRaiders.Count(r => r) == 17 && s.spawns.authoredRaiders.All(r => r && r.Alive) &&
            s.spawns.authoredBuggies.Count(b => b) == 2 && s.zone1.ActiveBossCount == 2,
            "authored humans=" + s.spawns.authoredRaiders.Count(r => r && r.Alive) + ", buggies=" + s.spawns.authoredBuggies.Count(b => b && b.Alive) + ", bosses=" + s.zone1.ActiveBossCount + ", total=" + s.spawns.ActiveCount);
        for (int i = 0; i < s.spawns.authoredBuggies.Length; i++)
        {
            var b = s.spawns.authoredBuggies[i];
            Check("authored buggy " + i + " restarts active with four grounded wheels", b && b.Alive && b.gameObject.activeInHierarchy && b.GroundedWheels == 4 && b.wheels.Length == 4 && !b.Body.isKinematic,
                b ? "grounded=" + b.GroundedWheels + ", alive=" + b.Alive + ", constraints=" + b.Body.constraints : "missing");
        }
        VisualAudit();
        Isolate(); PlaceCar(s.startPosition, s.startRotation, false); await Wait(.35f);
        Vector3 before = s.vehicle.transform.position; float maximumSpeed = 0, minimumUp = 1; int minWheels = 4;
        s.input.testBrake = false; s.input.testDirection = new Vector2(0, .68f);
        float end = Time.realtimeSinceStartup + 4;
        while (Time.realtimeSinceStartup < end && AttackRange.SquaredDistance(before, s.vehicle.transform.position) < 5.2f * 5.2f)
        {
            maximumSpeed = Mathf.Max(maximumSpeed, s.vehicle.Speed); minimumUp = Mathf.Min(minimumUp, Vector3.Dot(s.vehicle.transform.up, Vector3.up));
            minWheels = Mathf.Min(minWheels, s.vehicle.GroundedWheels); await Task.Delay(25);
        }
        s.input.testDirection = Vector2.zero; s.input.testBrake = true;
        float distance = Mathf.Sqrt(AttackRange.SquaredDistance(before, s.vehicle.transform.position));
        Check("player actually drives through existing input / suspension", distance > 4 && maximumSpeed > 1 && minWheels >= 3 && minimumUp > .9f,
            "distance=" + distance.ToString("F2") + "m, speed=" + maximumSpeed.ToString("F2") + ", grounded min=" + minWheels + ", upright=" + minimumUp.ToString("F2"));
    }
    static async Task RaiderAndHarpoon()
    {
        Isolate(); PlaceCar(s.startPosition, s.startRotation);
        var raider = s.spawns.authoredRaiders.First(r => r);
        Vector3 wanted = s.vehicle.transform.position + s.vehicle.transform.forward * 6;
        if (!NavMesh.SamplePosition(wanted, out var spawn, 2, NavMesh.AllAreas)) { Check("raider fixture NavMesh", false, "no clear point in front of spawn"); return; }
        raider.Spawn(spawn.position, -999, 0);
        // Hold its movement only, so the drop remains beyond passive pickup radius and inside the real harpoon radius.
        raider.enabled = false;
        var harpoon = s.vehicle.GetComponent<HarpoonCollector>();
        int collections = harpoon ? harpoon.CollectionsCompleted : 0, shots = s.turret.ShotsFired, kills = s.Kills;
        s.turret.enabled = true;
        bool killed = await Until(() => !raider.Alive, 4);
        raider.enabled = true; s.turret.enabled = false;
        Check("normal raider dies to real auto-aim fire", killed && s.turret.ShotsFired > shots && s.Kills == kills + 1,
            "HP=" + raider.Health + ", shots=" + (s.turret.ShotsFired - shots) + ", killsDelta=" + (s.Kills - kills));
        bool collected = await Until(() => s.Scrap >= 1 && s.pickups.ActiveCount == 0 && harpoon && harpoon.CollectionsCompleted > collections, 4.5f);
        Check("existing companion harpoon collects actual death gear / HUD", collected && s.ScrapEarned == 1 && s.hud.scrapText.text == s.Scrap.ToString("000"),
            "scrap=" + s.Scrap + ", earned=" + s.ScrapEarned + ", active gear=" + s.pickups.ActiveCount + ", harpoon completions=" + (harpoon ? harpoon.CollectionsCompleted - collections : -1));
    }
    static bool BuggyCarPosition(EnemyBuggy buggy, out Vector3 position)
    {
        float[] angles = { 0, 25, -25, 50, -50, 90, -90, 180 };
        var path = new NavMeshPath(); var car = s.vehicle.GetComponent<BoxCollider>();
        foreach (float angle in angles)
        {
            var direction = Quaternion.Euler(0, angle, 0) * buggy.transform.forward;
            var wanted = buggy.transform.position + direction * 16.2f;
            if (!NavMesh.SamplePosition(wanted, out var sample, 2, NavMesh.AllAreas)) continue;
            if (!NavMesh.SamplePosition(buggy.transform.position, out var from, 3, NavMesh.AllAreas) || !NavMesh.CalculatePath(from.position, sample.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var facing = Facing(sample.position, buggy.transform.position);
            if (car && Physics.CheckBox(sample.position + Vector3.up * .7f + facing * car.center, car.size * .5f + Vector3.one * .15f, facing, 1, QueryTriggerInteraction.Ignore)) continue;
            position = sample.position; return true;
        }
        position = default; return false;
    }
    static async Task BuggyCheck(EnemyBuggy buggy, int index)
    {
        Isolate(buggy);
        if (!BuggyCarPosition(buggy, out var at)) { Check("buggy " + index + " reachable approach fixture", false, "no safe NavMesh point 16m away"); return; }
        PlaceCar(at, Facing(at, buggy.transform.position));
        buggy.AIEnabled = true; buggy.gun.CanShoot = true; s.health.DamageEnabled = true;
        int shots = buggy.gun.ShotsFired, hits = buggy.gun.PlayerHits;
        Vector3 before = buggy.transform.position; float hp = s.health.Current;
        bool fired = await Until(() => buggy.gun.ShotsFired > shots && buggy.gun.PlayerHits > hits, 8);
        float travel = Mathf.Sqrt(AttackRange.SquaredDistance(before, buggy.transform.position));
        Check("authored buggy " + index + " really approaches / shoots / damages", fired && travel > .75f && s.health.Current < hp,
            "travel=" + travel.ToString("F2") + "m, shots=" + (buggy.gun.ShotsFired - shots) + ", playerHits=" + (buggy.gun.PlayerHits - hits) + ", damage=" + (hp - s.health.Current) + ", state=" + buggy.State);
        buggy.AIEnabled = false; buggy.gun.CanShoot = false; s.health.DamageEnabled = false;
        float enemyHP = buggy.Health; int playerShots = s.turret.ShotsFired, kills = s.Kills, earned = s.ScrapEarned;
        s.turret.enabled = true;
        bool damaged = await Until(() => buggy.Health < enemyHP, 3);
        Check("authored buggy " + index + " receives actual player turret damage", damaged && s.turret.ShotsFired > playerShots,
            "HP " + enemyHP + " -> " + buggy.Health + ", player shots=" + (s.turret.ShotsFired - playerShots));
        bool dead = await Until(() => !buggy.Alive, 4);
        s.turret.enabled = false; if (dead) buggy.Hit(9999, Vector3.forward);
        Check("authored buggy " + index + " death preserves reward once", dead && s.Kills == kills + 1 && s.ScrapEarned == earned + buggy.tuning.scrapReward,
            "killsDelta=" + (s.Kills - kills) + ", earnedDelta=" + (s.ScrapEarned - earned) + ", configured=" + buggy.tuning.scrapReward + ", stored loot=" + s.pickups.StoredValue + ", collected=" + s.Scrap);
    }
    static async Task Repair()
    {
        Isolate(); var workshop = Object.FindAnyObjectByType<RepairWorkshop>();
        if (!workshop) { Check("existing auto repair workshop", false, "component missing"); return; }
        PlaceCar(workshop.transform.position, workshop.transform.rotation);
        s.health.DamageEnabled = true; s.health.Damage(s.health.tuning.vehicleHealth * .6f, false);
        float hp = s.health.Current, time = Time.time; await Wait(1.05f);
        float expected = s.health.tuning.vehicleHealth * workshop.maximumHealthFractionPerSecond * (Time.time - time);
        Check("existing workshop heals at configured rate with feedback", Mathf.Abs((s.health.Current - hp) - expected) < 1.5f && workshop.IsRepairing &&
            workshop.radiusMarking && (!workshop.activeRepairEffect || workshop.activeRepairEffect.activeInHierarchy),
            "healed=" + (s.health.Current - hp).ToString("F2") + ", expected=" + expected.ToString("F2") + ", radius=" + workshop.healingRadius + ", rate=" + workshop.maximumHealthFractionPerSecond);
        PlaceCar(workshop.transform.position - workshop.transform.forward * (workshop.healingRadius + 1.2f), workshop.transform.rotation);
        await Wait(.1f); hp = s.health.Current; await Wait(.3f);
        Check("leaving workshop stops healing / active effect", s.health.Current == hp && !workshop.IsRepairing && (!workshop.activeRepairEffect || !workshop.activeRepairEffect.activeInHierarchy), "HP=" + s.health.Current);
    }
    public static async Task<string> Main()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play before ordinary Zone 1 regression");
        s = Object.FindAnyObjectByType<GameSession>();
        if (!s || !s.zone1) throw new Exception("No Zone 1 gameplay session / roster");
        string folder = "Documentation/RoadGunner/Zone1-Enemies-2026-10-17"; Directory.CreateDirectory(folder);
        report = folder + "/Checks-ordinary.txt"; failures = 0; lines.Clear(); float started = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        try
        {
            await StartupAndDrive(); await RaiderAndHarpoon();
            for (int i = 0; i < s.spawns.authoredBuggies.Length; i++) if (s.spawns.authoredBuggies[i]) await BuggyCheck(s.spawns.authoredBuggies[i], i);
            await Repair();
        }
        catch (Exception error) { Check("exception", false, error.ToString()); }
        finally { Time.timeScale = 1; RestoreMovableActors(); s.spawns.enabled = true; s.turret.enabled = true; s.Restart(); }
        lines.Add("Failures=" + failures + "; elapsed=" + (Time.realtimeSinceStartup - started).ToString("F2") + " real seconds. Final state: fresh Restart.");
        File.WriteAllLines(report, lines); return string.Join("\n", lines);
    }
}
