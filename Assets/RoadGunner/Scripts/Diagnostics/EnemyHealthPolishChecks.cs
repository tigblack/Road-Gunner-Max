#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator EnemyHealthUi()
        {
            // Each section restores its own live population; earlier route/physics sections isolate it.
            s.Restart(); s.turret.enabled = false; s.health.DamageEnabled = false;
            yield return null; yield return null;
            // Cache components before Isolate deactivates their pooled gameplay roots.
            var raiderBar = human.GetComponent<EnemyHealthBar>();
            var buggyBar = buggy.GetComponent<EnemyHealthBar>();
            var normals = s.spawns.pool.Cast<CombatTarget>().Concat(s.spawns.buggyPool)
                .Where(t => t && !t.UsesBossHealthHud).Distinct().ToArray();
            var live = normals.Where(t => t.Alive).ToArray();
            bool wired = raiderBar && buggyBar && normals.All(t =>
            {
                var bar = t.GetComponent<EnemyHealthBar>();
                return bar && bar.target == t && bar.anchor && bar.worldCanvas && bar.healthFill &&
                    bar.gameplayCamera == s.input.view && bar.worldCanvas.renderMode == RenderMode.WorldSpace &&
                    !bar.healthFill.raycastTarget && !bar.worldCanvas.GetComponent<GraphicRaycaster>();
            });
            Check("enemy health UI reuses actual combat targets", wired,
                "Cached normal/pool targets=" + normals.Length + "; live=" + live.Length + "; Raider/Buggy bars configured before isolation");
            if (!wired) yield break;
            Check("HEALTH D multiple compact bars remain available", live.Length >= 14 && live.All(t =>
            {
                var bar = t.GetComponent<EnemyHealthBar>();
                float width = ((RectTransform)bar.worldCanvas.transform).rect.width * bar.worldCanvas.transform.lossyScale.x;
                float height = ((RectTransform)bar.worldCanvas.transform).rect.height * bar.worldCanvas.transform.lossyScale.y;
                return bar.IsVisible && t.GetComponents<EnemyHealthBar>().Length == 1 && width >= 1.7f && width <= 3 && height >= .2f &&
                    Mathf.Abs(bar.DisplayedRatio - t.CurrentHealth / t.MaxHealth) < .001f;
            }), "All living ordinary enemies have one compact world bar; no per-frame fill polling is required");

            Isolate();
            if (!SpawnInGunRange(human)) { Check("HEALTH A Raider staging", false, "No real clear target location"); yield break; }
            yield return null; yield return null;
            var raiderCanvas = raiderBar.worldCanvas; var raiderComponent = raiderBar;
            int refreshes = raiderBar.HealthRefreshCount;
            float before = human.Health;
            human.Hit(human.MaxHealth * .35f, Vector3.forward);
            Check("HEALTH A Raider damage updates actual HP ratio", human.Alive && human.Health < before &&
                Mathf.Abs(raiderBar.DisplayedRatio - human.Health / human.MaximumHealth) < .001f &&
                raiderBar.IsVisible && raiderBar.HealthRefreshCount == refreshes + 1,
                "HP=" + human.Health + "/" + human.MaximumHealth + "; fill=" + raiderBar.DisplayedRatio + "; one health notification");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var raiderMesh = raiderBar.healthFill.canvasRenderer.GetMesh();
            Check("damaged enemy bar produces visible UI geometry", raiderMesh && raiderMesh.vertexCount >= 4 &&
                raiderBar.healthFill.color.a >= .9f && raiderBar.worldCanvas.renderMode == RenderMode.WorldSpace,
                "Bright fill, rounded contrast track and camera-rendered world canvas precede screen HUD");
            Capture("raider-health-bar");

            refreshes = raiderBar.HealthRefreshCount;
            yield return new WaitForSecondsRealtime(.25f);
            Check("health fill updates only on health changes", raiderBar.HealthRefreshCount == refreshes,
                "Refresh count remained " + refreshes + " while the live bar followed the enemy for multiple frames");
            var camera = s.input.view; var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
            bool followWasEnabled = s.follow.enabled;
            try
            {
                s.follow.enabled = false;
                camera.transform.SetPositionAndRotation(oldPosition + new Vector3(3, 1, -2), Quaternion.Euler(6, 17, 0) * oldRotation);
                yield return null; yield return null;
                float positionError = Vector3.Distance(raiderBar.anchor.position, human.transform.position + Vector3.up * raiderBar.height);
                float rotationError = Quaternion.Angle(raiderBar.anchor.rotation, camera.transform.rotation);
                Check("HEALTH E bar follows enemy and cached camera", raiderBar.gameplayCamera == camera &&
                    raiderBar.worldCanvas.worldCamera == camera && positionError < .01f && rotationError < .1f &&
                    raiderBar.HealthRefreshCount == refreshes,
                    "Moved/rotated real gameplay camera; anchor error=" + positionError + "; angle error=" + rotationError + "; fill unchanged");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                s.follow.enabled = followWasEnabled; if (followWasEnabled) s.follow.Snap();
            }
            // Stage several existing pooled enemies in one normal gameplay view.
            var neighbours = s.spawns.authoredRaiders.Where(r => r && r != human).Take(3).ToArray();
            int visibleNeighbours = 0;
            for (int i = 0; i < neighbours.Length; i++)
            {
                Vector3 point = human.transform.position + (testRotation * Vector3.right) * ((i - 1) * 2.6f) +
                    (testRotation * Vector3.forward) * (i == 1 ? 3 : 1);
                if (!UnityEngine.AI.NavMesh.SamplePosition(point, out var hit, 2, UnityEngine.AI.NavMesh.AllAreas)) continue;
                neighbours[i].Spawn(hit.position, -1, i); neighbours[i].AIEnabled = false;
                neighbours[i].Hit(neighbours[i].MaxHealth * (.15f + i * .2f), Vector3.forward);
                visibleNeighbours++;
            }
            yield return null; yield return null;
            Capture("multiple-enemy-health-bars");
            Check("several damaged health bars share normal gameplay view", visibleNeighbours == 3 &&
                neighbours.All(r => r.Alive && r.GetComponent<EnemyHealthBar>().IsVisible),
                "Three neighbouring existing Raiders plus primary target; persistent cached canvases and distinct real HP fractions");
            human.Hit(human.MaxHealth * 2, Vector3.forward);
            bool hiddenOnDeath = !human.Alive && !raiderBar.IsVisible && raiderBar.DisplayedRatio == 0;
            human.Despawn();
            Check("HEALTH C Raider death and despawn hide its bar", hiddenOnDeath && !human.gameObject.activeSelf && !raiderBar.IsVisible,
                "The cached component survives deactivation; no corpse health UI remains");
            bool respawned = SpawnInGunRange(human);
            yield return null;
            Check("Raider pooled bar returns full without rebuilding", respawned && raiderBar.IsVisible &&
                raiderBar.DisplayedRatio == 1 && human.Health == human.MaximumHealth &&
                human.GetComponent<EnemyHealthBar>() == raiderComponent && raiderBar.worldCanvas == raiderCanvas,
                "Same component/canvas restored on the existing pool object");
            refreshes = raiderBar.HealthRefreshCount; human.Hit(human.MaxHealth * .1f, Vector3.forward);
            Check("pool reuse keeps one health subscription", raiderBar.HealthRefreshCount == refreshes + 1 &&
                Mathf.Abs(raiderBar.DisplayedRatio - human.Health / human.MaximumHealth) < .001f,
                "Exactly one fill update after a despawn/spawn cycle");

            Isolate();
            if (!SpawnInGunRange(buggy)) { Check("HEALTH B Buggy staging", false, "No real clear vehicle target location"); yield break; }
            yield return null; yield return null;
            var buggyCanvas = buggyBar.worldCanvas; var buggyComponent = buggyBar;
            refreshes = buggyBar.HealthRefreshCount; before = buggy.Health;
            buggy.Hit(buggy.MaxHealth * .4f, Vector3.forward);
            Check("HEALTH B Buggy damage updates actual HP ratio", buggy.Alive && buggy.Health < before &&
                Mathf.Abs(buggyBar.DisplayedRatio - buggy.Health / buggy.MaximumHealth) < .001f &&
                buggyBar.IsVisible && buggyBar.HealthRefreshCount == refreshes + 1,
                "HP=" + buggy.Health + "/" + buggy.MaximumHealth + "; fill=" + buggyBar.DisplayedRatio);
            yield return null; Capture("buggy-health-bar");
            buggy.Hit(buggy.MaxHealth * 2, Vector3.forward);
            hiddenOnDeath = !buggy.Alive && !buggyBar.IsVisible && buggyBar.DisplayedRatio == 0;
            buggy.Despawn();
            Check("HEALTH C Buggy wreck and despawn hide its bar", hiddenOnDeath && !buggy.gameObject.activeSelf && !buggyBar.IsVisible,
                "Actual vehicle destruction hides UI before the pooled root deactivates");
            respawned = SpawnInGunRange(buggy);
            yield return null;
            Check("Buggy pooled bar returns full without rebuilding", respawned && buggyBar.IsVisible && buggyBar.DisplayedRatio == 1 &&
                buggy.Health == buggy.MaximumHealth && buggy.GetComponent<EnemyHealthBar>() == buggyComponent &&
                buggyBar.worldCanvas == buggyCanvas,
                "Same vehicle health UI restored from existing tuning HP");

            Isolate(); monster.ResetEncounter(); monster.AIEnabled = false; boss.ResetEncounter(); boss.AIEnabled = false;
            var bossHud = Object.FindAnyObjectByType<Zone1BossHud>();
            bool noDuplicate = new CombatTarget[] { monster, boss }.All(t => t.UsesBossHealthHud &&
                t.GetComponentsInChildren<EnemyHealthBar>(true).All(bar => !bar.IsVisible));
            Check("bosses keep dedicated health HUD without duplicate world bars", bossHud && bossHud.roster == s.zone1 && noDuplicate,
                "Monster and Boss Raider retain the existing dedicated UI; any inherited world bar stays disabled");
            Vector3 bossApproach = Ground(boss.transform.position - boss.transform.forward * 8) + Vector3.up * .7f;
            PlaceCar(bossApproach, Quaternion.LookRotation(boss.transform.position - bossApproach));
            boss.Hit(boss.MaxHealth * .2f, Vector3.forward);
            yield return new WaitForSecondsRealtime(.15f);
            Check("existing boss HUD uses the same actual health ratio", bossHud && bossHud.IsVisible && bossHud.Target == boss &&
                Mathf.Abs(bossHud.healthFill.fillAmount - boss.CurrentHealth / boss.MaxHealth) < .001f && noDuplicate,
                "Boss Raider HP=" + boss.CurrentHealth + "/" + boss.MaxHealth + "; existing boss bar fraction=" + (bossHud ? bossHud.healthFill.fillAmount : -1));
            Isolate();
        }
    }
}
#endif
