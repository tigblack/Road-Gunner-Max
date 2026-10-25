#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator CorridorUpgradeOnboarding()
        {
            Isolate();
            var health = s.runStats.stations.Where(station => station && station.kind == ArenaUpgradeKind.Health).ToArray();
            foreach (var station in health) station.ResetStation();
            var corridor = layout.GetComponent<ArenaCorridorLayout>();
            Check("three independent destructible Health +1 start objects", health.Length == 3 &&
                health.All(station => station.requiresDestruction && station.session == s && station.stats == s.runStats &&
                    station.Alive && station.hitbox && station.hitbox.enabled && !station.hitbox.isTrigger &&
                    station.transform.position != s.startPosition && AttackRange.Contains(s.startPosition, station.transform.position, s.turret.Range)) &&
                corridor && corridor.startHealthPoints.Length == 3,
                "Existing ArenaUpgradeStation + ArenaRunStats, three distinct Health targets within actual10m weapon range");
            if (health.Length != 3) yield break;
            bool clearShots = health.All(station => s.turret.CanAttack(station));
            var camera = s.input.view; float oldAspect = camera.aspect; camera.aspect = 9f / 16;
            bool onScreen = health.All(station =>
            {
                Vector3 view = camera.WorldToViewportPoint(station.AimPoint);
                return view.z > 0 && view.x > .05f && view.x < .95f && view.y > .12f && view.y < .78f;
            });
            camera.aspect = oldAspect;
            Check("all three start objects are immediately visible and shootable", clearShots && onScreen,
                "Real muzzle line of sight=" + clearShots + "; normal9:16 gameplay viewport=" + onScreen);
            bool separatedLabels = health.All(station =>
            {
                var bar = station.GetComponent<EnemyHealthBar>();
                if (!station.displayText || !bar || !bar.anchor) return false;
                Vector3 labelView = camera.WorldToViewportPoint(station.displayText.transform.position);
                Vector3 barView = camera.WorldToViewportPoint(bar.anchor.position);
                return labelView.y - barView.y > .012f && station.displayText.color.grayscale < .25f;
            });
            Check("Health reward labels clear their durability bars", separatedLabels,
                "Actual camera projection places labels above bars, with dark readable text over bright sand");
            Capture("three-health-onboarding");
            yield return new WaitForSecondsRealtime(.4f);
            bool noProximityGrant = s.runStats.HealthLevel == 1 && health.All(station => !station.Consumed && !station.TryCollect());
            var first = health[0]; first.Hit(first.MaxHealth * .5f, Vector3.forward);
            Check("Health +1 requires destruction rather than proximity", noProximityGrant && first.Alive &&
                first.CurrentHealth > 0 && s.runStats.HealthLevel == 1 && !first.Consumed,
                "Standing nearby grants nothing; a partial hit changes object durability but not player health level");
            float originalMax = s.health.Maximum;
            first.Hit(first.MaxHealth, Vector3.forward);
            Check("destroying one object grants exactly one existing Health level", first.Consumed && !first.Alive &&
                !first.hitbox.enabled && s.runStats.HealthLevel == 2 && s.health.Maximum > originalMax &&
                Mathf.Approximately(s.health.Current, s.health.Maximum) && s.Kills == 0 && s.ScrapEarned == 0,
                "HP level1->2; existing VehicleHealth maximum=" + s.health.Maximum + "; no enemy kill or Metal Gear reward");
            first.Hit(1000, Vector3.forward); bool collectedAgain = first.TryCollect();
            Check("destroyed Health object cannot grant twice", !collectedAgain && s.runStats.HealthLevel == 2,
                "Repeated hits/collection requests remain idempotent");

            Isolate(); foreach (var station in health) station.ResetStation();
            s.turret.enabled = true; int initialShots = s.turret.ShotsFired;
            float deadline = Time.realtimeSinceStartup + 10;
            while (s.runStats.HealthLevel < 4 && Time.realtimeSinceStartup < deadline) yield return null;
            s.turret.enabled = false;
            yield return new WaitForSecondsRealtime(.15f);
            Check("actual roof gun destroys all three upgrades independently", health.All(station => station.Consumed && !station.Alive) &&
                s.runStats.HealthLevel == 4 && s.turret.ShotsFired - initialShots >= 6 &&
                Mathf.Abs(s.health.Maximum - s.health.tuning.vehicleHealth * 1.6f) < .01f && s.Kills == 0 && s.ScrapEarned == 0 &&
                s.hud.statsText.text.Contains("HP 4"),
                "Actual gun shots=" + (s.turret.ShotsFired - initialShots) + "; HP level=" + s.runStats.HealthLevel +
                "; vehicle HP=" + s.health.Current + "/" + s.health.Maximum + "; HUD=" + s.hud.statsText.text);
            Check("destroyed upgrade dots leave minimap", health.All(station => !s.minimap.TryGetMarker(station.transform, out _)),
                "All three red points removed only when their own station is consumed");
            Capture("three-health-destroyed");
            Isolate();
            Check("restart restores all destructible onboarding objects", health.All(station => !station.Consumed && station.Alive &&
                Mathf.Approximately(station.CurrentHealth, station.MaxHealth)) && s.runStats.HealthLevel == 1,
                "Existing ResetRun/ResetStation restores durability, visuals and one-use availability");
        }
    }
}
#endif
