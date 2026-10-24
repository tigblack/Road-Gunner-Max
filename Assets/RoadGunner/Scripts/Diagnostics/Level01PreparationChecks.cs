#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace RoadGunner
{
    // Verifies the prepared scene and authored spawn integration, not reference fidelity.
    public sealed class Level01PreparationChecks : MonoBehaviour
    {
        GameSession session;
        bool running;
        readonly List<string> results = new List<string>();
        public void Begin()
        {
            if (running) return;
            running = true;
            session = GetComponent<GameSession>();
            StartCoroutine(Checks());
        }
        void Record(string name, bool passed, string observation)
        {
            string line = (passed ? "PASS " : "FAIL ") + name + ": " + observation;
            results.Add(line);
            Debug.Log("RG LEVEL01 " + line);
            Directory.CreateDirectory("Documentation/RoadGunner/Level01-2026-10-07");
            File.WriteAllLines("Documentation/RoadGunner/Level01-2026-10-07/PreparationChecks.txt", results);
        }
        Dictionary<int, Vector3> Positions()
        {
            var result = new Dictionary<int, Vector3>();
            for (int i = 0; i < session.spawns.pool.Length; i++)
                if (session.spawns.pool[i].Alive) result[i] = session.spawns.pool[i].transform.position;
            return result;
        }
        int CountGroup(int group)
        {
            int count = 0;
            foreach (var raider in session.spawns.pool) if (raider.Alive && raider.Group == group) count++;
            return count;
        }
        IEnumerator Checks()
        {
            var director = session.spawns;
            var layout = Object.FindAnyObjectByType<LevelLayout>();
            session.turret.enabled = false;
            session.health.DamageEnabled = false;
            foreach (var raider in director.pool) raider.enabled = false;
            if(director.buggyPool!=null)foreach(var buggy in director.buggyPool)buggy.enabled=false;
            int originalCap = director.tuning.activeCap;
            int count0 = director.encounters[0].enemyCount, count1 = director.encounters[1].enemyCount;
            try
            {
                session.Restart();
                yield return new WaitForSeconds(.8f);
                Record("working systems retained", session.vehicle.wheels.Length == 4 && session.vehicle.GroundedWheels == 4
                    && session.input.joystick && session.input.view.orthographic
                    && Object.FindObjectsByType<GameSession>().Length == 1,
                    "4 grounded wheels; joystick, portrait camera, single session");
                Record("reference status explicit", layout && !layout.referenceInspected
                    && layout.requestedFootprintScale == 2 && layout.appliedFootprintScale == 1,
                    "Existing map retained; video layout and 2x enlargement pending inspected frames.");
                Record("player and recovery markers", layout && layout.playerSpawn
                    && Vector3.Distance(session.startPosition, layout.playerSpawn.position) < .001f
                    && Quaternion.Angle(session.startRotation, layout.playerSpawn.rotation) < .001f
                    && session.recoveryPoints.Length == layout.recoveryPoints.Length,
                    "recovery markers=" + session.recoveryPoints.Length);
                bool distribution = true;
                string groups = "";
                for (int g = 0; g < director.GroupCount; g++)
                {
                    int count = CountGroup(g);
                    distribution &= count > 0 && count <= director.encounters[g].enemyCount;
                    groups += "group " + g + "=" + count + "/" + director.encounters[g].enemyCount + "; ";
                }
                Record("authored group distribution", distribution && director.ActiveCount <= originalCap, groups);
                bool reachable = NavMesh.SamplePosition(session.startPosition, out var anchor, 3, NavMesh.AllAreas);
                var path = new NavMeshPath();
                foreach (var raider in director.pool)
                {
                    if (!raider.Alive) continue;
                    reachable &= raider.agent.isOnNavMesh && NavMesh.CalculatePath(raider.transform.position, anchor.position, NavMesh.AllAreas, path)
                        && path.status == NavMeshPathStatus.PathComplete;
                    reachable &= !Physics.CheckCapsule(raider.transform.position + Vector3.up * .55f,
                        raider.transform.position + Vector3.up * 1.25f, .4f, 1, QueryTriggerInteraction.Ignore);
                }
                Record("spawn reachability and collision", reachable, "All active spawns have complete paths to start and clear capsules.");
                session.Restart();
                var first = Positions();
                session.Restart();
                var second = Positions();
                bool deterministic = first.Count == second.Count;
                foreach (var pair in first) deterministic &= second.TryGetValue(pair.Key, out var p) && Vector3.Distance(pair.Value, p) < .001f;
                Record("deterministic restart placement", deterministic, "Compared " + first.Count + " indexed spawn positions across two restarts.");
                director.encounters[0].enemyCount = 2;
                director.encounters[1].enemyCount = 1;
                session.Restart();
                Record("independent serialized group sizes", CountGroup(0) == 2 && CountGroup(1) == 1,
                    "group 0=" + CountGroup(0) + ", group 1=" + CountGroup(1));
                director.encounters[0].enemyCount = count0;
                director.encounters[1].enemyCount = count1;
                director.tuning.activeCap = 5;
                session.Restart();
                Record("global population cap", director.ActiveCount == 5 && Positions().Count == 5,
                    "Configured cap=5, active=" + director.ActiveCount + ", reusable pool=" + director.pool.Length);
                director.tuning.activeCap = originalCap;
                session.Restart();
                yield return new WaitForSeconds(.7f);
                int farGroup = -1;
                for (int g = 0; g < director.GroupCount; g++)
                {
                    var center = director.encounters[g].transform.position;
                    bool eligible = !director.InView(center, .12f)
                        && Vector3.Distance(center, session.vehicle.transform.position) > director.tuning.spawnDistance;
                    for (int i = 0; i < director.encounters[g].enemyCount; i++)
                    {
                        var point = director.encounters[g].Position(i);
                        eligible &= !director.InView(point, .1f)
                            && Vector3.Distance(point, session.vehicle.transform.position) > director.tuning.spawnDistance;
                    }
                    if (eligible)
                    { farGroup = g; break; }
                }
                int farCount = farGroup >= 0 ? CountGroup(farGroup) : 0;
                foreach (var raider in director.pool)
                    if (raider.Alive && (raider.Group == 0 || raider.Group == farGroup)) raider.Despawn();
                yield return new WaitForSeconds(director.tuning.replenishDelay + 1.2f);
                Record("respawn guards and delay", farGroup >= 0 && farCount > 0 && CountGroup(farGroup) == farCount && CountGroup(0) == 0,
                    "Offscreen distant group=" + farGroup + ", restored=" + CountGroup(farGroup) + "; near start group=" + CountGroup(0));
            }
            finally
            {
                director.tuning.activeCap = originalCap;
                director.encounters[0].enemyCount = count0;
                director.encounters[1].enemyCount = count1;
                foreach (var raider in director.pool) raider.enabled = true;
                if(director.buggyPool!=null)foreach(var buggy in director.buggyPool)buggy.enabled=true;
                session.turret.enabled = true;
                session.health.DamageEnabled = true;
                session.Restart();
                running = false;
            }
            Record("completed", true, "Preparation checks finished; working session restored. Reference adaptation remains pending.");
        }
    }
}
#endif
