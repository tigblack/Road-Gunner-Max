#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace RoadGunner
{
    public sealed class CombatRangeChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/Combat-2026-10-07/CombatRangeChecks.txt";
        GameSession s; EnemyBuggy buggy; AttackRangeIndicator ring; bool running;
        RigidbodyConstraints carConstraints, buggyConstraints;
        readonly List<string> results = new List<string>();
        public void Begin()
        {
            if (running) return; running = true; s = GetComponent<GameSession>();
            buggy = s.spawns.buggyPool[0]; ring = s.vehicle.GetComponent<AttackRangeIndicator>();
            carConstraints = s.vehicle.Body.constraints; buggyConstraints = buggy.Body.constraints;
            StartCoroutine(Run());
        }
        void Record(string name, bool pass, string details)
        {
            string line = (pass ? "PASS " : "FAIL ") + name + ": " + details;
            results.Add(line); Debug.Log("RG RANGE " + line);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllLines(ReportPath, results);
        }
        void Isolate()
        {
            s.Restart(); s.spawns.enabled = false; s.turret.enabled = false;
            foreach (var r in s.spawns.pool) r.Despawn(); foreach (var b in s.spawns.buggyPool) b.Despawn();
            s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            s.vehicle.ResetPose(new Vector3(0, ring.GroundHeight(0, 0) + .7f, 0), Quaternion.Euler(0, 45, 0));
            s.follow.Snap(); Physics.SyncTransforms(); ring.Refresh();
        }
        void Place(float distance, float angle = 45)
        {
            float a = angle * Mathf.Deg2Rad; Vector3 p = new Vector3(Mathf.Sin(a) * distance, 0, Mathf.Cos(a) * distance);
            p.y = ring.GroundHeight(p.x, p.z);
            buggy.Spawn(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(-p, Vector3.up)), 0);
            buggy.Body.constraints = RigidbodyConstraints.FreezeAll; buggy.AIEnabled = false;
            Physics.SyncTransforms();
        }
        IEnumerator Run()
        {
            Isolate(); Place(9.8f); yield return null;
            Record("shared ten metre setting", s.turret.Range == 10 && buggy.gun.Range == 10 && ring.Radius == 10,
                "player=" + s.turret.Range + ", buggy=" + buggy.gun.Range + ", circle=" + ring.Radius);
            Record("centre boundary includes exactly ten metres", AttackRange.Contains(Vector3.zero, Vector3.right * 10, 10) && !AttackRange.Contains(Vector3.zero, Vector3.right * 10.001f, 10), "horizontal centre distance; gun offsets do not change the boundary");

            Place(10.2f); int shots = buggy.gun.ShotsFired; float hp = s.health.Current;
            yield return new WaitForSeconds(1.4f);
            Record("buggy cannot fire outside circle", !buggy.gun.CanAttackPlayer() && buggy.gun.ShotsFired == shots && s.health.Current == hp,
                "distance=" + Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position, s.vehicle.transform.position)) + ", shots=" + (buggy.gun.ShotsFired - shots));
            Place(9.8f); shots = buggy.gun.ShotsFired; int hits = buggy.gun.PlayerHits; hp = s.health.Current;
            yield return new WaitForSeconds(1.8f);
            Record("buggy fires real damaging hits inside circle", buggy.gun.ShotsFired > shots && buggy.gun.PlayerHits > hits && s.health.Current < hp && hp - s.health.Current <= 20,
                "shots=" + (buggy.gun.ShotsFired - shots) + ", hits=" + (buggy.gun.PlayerHits - hits) + ", hp=" + hp + " -> " + s.health.Current);

            Vector3 from = buggy.gun.muzzle.position, aim = s.vehicle.transform.TransformPoint(new Vector3(0, .5f, 0));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary enemy gun LOS check";
            wall.transform.position = (from + aim) * .5f; wall.transform.localScale = new Vector3(3, 5, .7f); wall.transform.rotation = Quaternion.LookRotation(aim - from); Physics.SyncTransforms();
            shots = buggy.gun.ShotsFired; hp = s.health.Current; yield return new WaitForSeconds(1.2f);
            bool blocked = buggy.gun.ShotsFired == shots && s.health.Current == hp;
            Destroy(wall); yield return new WaitForSeconds(1.2f);
            Record("world wall blocks buggy fire", blocked && s.health.Current < hp, "protected=" + blocked + ", hp after removing wall=" + s.health.Current);

            Place(8); buggy.Body.rotation = Quaternion.Euler(0, 45, 0); buggy.transform.rotation = buggy.Body.rotation; Physics.SyncTransforms();
            Quaternion chassis = buggy.transform.rotation; shots = buggy.gun.ShotsFired; hp = s.health.Current;
            yield return new WaitForSeconds(1.9f);
            Record("roof gun swivels independently", Quaternion.Angle(chassis, buggy.transform.rotation) < .1f && buggy.gun.ShotsFired > shots && s.health.Current < hp,
                "chassis change=" + Quaternion.Angle(chassis, buggy.transform.rotation) + ", gun angle=" + Vector3.Angle(buggy.transform.forward, buggy.gun.yaw.forward));

            Isolate(); Place(10.2f); buggy.gun.CanShoot = false; s.turret.enabled = true; shots = s.turret.ShotsFired;
            yield return new WaitForSeconds(1.4f);
            Record("player cannot fire outside circle", s.turret.ShotsFired == shots && buggy.Health == buggy.tuning.health && !s.turret.CanAttack(buggy), "shots=" + (s.turret.ShotsFired - shots) + ", enemy hp=" + buggy.Health);
            Place(9.8f); buggy.gun.CanShoot = false; float enemyHP = buggy.Health; shots = s.turret.ShotsFired;
            yield return new WaitForSeconds(1.6f);
            Record("player shoots to displayed edge", s.turret.ShotsFired > shots && buggy.Health < enemyHP, "shots=" + (s.turret.ShotsFired - shots) + ", enemy hp=" + enemyHP + " -> " + buggy.Health);

            Isolate(); Place(9.8f, 90); buggy.gun.CanShoot = false; s.turret.enabled = true;
            var viewport = s.input.view.WorldToViewportPoint(buggy.AimPoint); enemyHP = buggy.Health;
            yield return new WaitForSeconds(1.8f);
            Record("camera crop does not shorten displayed range", buggy.Health < enemyHP && (viewport.x > .965f || viewport.x < .035f), "viewport x=" + viewport.x + ", enemy hp=" + buggy.Health);

            Isolate(); Place(11.5f); s.turret.tuning.range = 12; ring.Refresh(); Physics.SyncTransforms();
            Record("both weapons and circle follow shared tuning", buggy.gun.Range == 12 && ring.Radius == 12 && buggy.gun.CanAttackPlayer() && s.turret.CanAttack(buggy), "temporary shared range=" + ring.Radius);
            s.turret.tuning.range = 10; ring.Refresh();
            Mesh sameMesh = ring.Mesh;
            s.vehicle.ResetPose(new Vector3(3, ring.GroundHeight(3, 2) + .7f, 2), Quaternion.Euler(0, 150, 0)); Physics.SyncTransforms();
            yield return null; ring.Refresh(); Vector3[] points = ring.Mesh.vertices; float maxHeightError = 0, radiusError = 0;
            for (int i = 1; i < points.Length; i += 13) maxHeightError = Mathf.Max(maxHeightError, Mathf.Abs(points[i].y - ring.GroundHeight(points[i].x, points[i].z) - .045f));
            for (int i = 385; i <= 512; i++) radiusError = Mathf.Max(radiusError, Mathf.Abs(Mathf.Sqrt(AttackRange.SquaredDistance(ring.Centre, points[i])) - 10));
            Record("circle follows car and real terrain", AttackRange.SquaredDistance(ring.Centre, s.vehicle.transform.position) < .0001f && maxHeightError < .002f && radiusError < .002f,
                "ground height error=" + maxHeightError + ", radius error=" + radiusError);
            Record("circle uses one mesh and no physics collider", ring.Mesh == sameMesh && ring.Visible && Object.FindObjectsByType<AttackRangeIndicator>().Length == 1 && GameObject.Find("Player attack radius • 10 metres").GetComponent<Collider>() == null,
                "mesh vertices=" + points.Length + ", visible=" + ring.Visible);

            Isolate(); Place(8); yield return new WaitForSeconds(1.6f); shots = buggy.gun.ShotsFired; hp = s.health.Current;
            s.SetPaused(true); yield return new WaitForSecondsRealtime(.25f);
            bool frozen = buggy.gun.ShotsFired == shots && s.health.Current == hp && !buggy.gun.flash.gameObject.activeSelf;
            s.SetPaused(false); yield return new WaitForSeconds(.3f);
            Record("pause and resume clear pending shots", frozen && buggy.gun.ShotsFired == shots, "frozen=" + frozen + ", resume warmup shots=" + (buggy.gun.ShotsFired - shots));
            yield return new WaitForSeconds(.9f); shots = buggy.gun.ShotsFired; hp = s.health.Current; buggy.Hit(999, Vector3.forward);
            yield return new WaitForSeconds(1.1f);
            Record("destroyed buggy stops shooting", !buggy.Alive && buggy.gun.ShotsFired == shots && s.health.Current == hp && !buggy.gun.flash.gameObject.activeSelf, "shots after destruction=" + (buggy.gun.ShotsFired - shots));

            Isolate(); Place(8); yield return new WaitForSeconds(1.3f); shots = buggy.gun.ShotsFired;
            s.health.Damage(999, false); yield return new WaitForSecondsRealtime(.25f);
            Record("defeat stops gun and hides circle", s.State == SessionState.Defeated && buggy.gun.ShotsFired == shots && !buggy.gun.flash.gameObject.activeSelf && !ring.Visible, "state=" + s.State + ", circle=" + ring.Visible);

            Isolate(); Place(12); buggy.Body.constraints = buggyConstraints; buggy.AIEnabled = true; s.health.DamageEnabled = false; shots = buggy.gun.ShotsFired;
            float begun = Time.time; while (buggy.gun.ShotsFired == shots && Time.time - begun < 5) yield return null;
            Record("moving pursuit enters range and fires", buggy.gun.ShotsFired > shots && AttackRange.Contains(buggy.transform.position, s.vehicle.transform.position, 10), "distance=" + Mathf.Sqrt(AttackRange.SquaredDistance(buggy.transform.position, s.vehicle.transform.position)) + ", shots=" + (buggy.gun.ShotsFired - shots));
            s.vehicle.Body.constraints = carConstraints; buggy.Body.constraints = buggyConstraints; s.spawns.enabled = true; s.turret.enabled = true; s.Restart();
            yield return null; bool reset = true; foreach (var b in s.spawns.buggyPool) reset &= b.Alive && b.Health == b.tuning.health && b.gun.CanShoot && !b.gun.flash.gameObject.activeSelf;
            Record("restart restores pooled guns and ten metre circle", reset && s.health.Current == 100 && ring.Visible && ring.Radius == 10, "guns reset=" + reset + ", player hp=" + s.health.Current + ", radius=" + ring.Radius);
            Record("completed", true, "Actual Unity Play Mode hits, LOS, physics and ground mesh. Normal session restored."); running = false;
        }
    }
}
#endif
