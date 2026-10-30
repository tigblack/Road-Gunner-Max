#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoadGunner
{
    /// <summary>Opt-in tests of the saved open arena, real reward components, turret and enemy damage paths.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class ExplorationPlayChecks : MonoBehaviour
    {
        public const string ReportPath = "Documentation/RoadGunner/Exploration-2026-10-31/PlayMode.txt";
        public const string ArtifactFolder = "outputs/exploration-validation";
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }
        readonly List<string> report = new List<string>();
        readonly List<GameObject> temporary = new List<GameObject>();
        GameSession s;
        ArenaExploration exploration;
        ArenaRewardCache[] caches;
        RepairWorkshop[] workshops;
        bool[] workshopEnabled;
        LevelLayout layout;
        RigidbodyConstraints constraints;
        bool spawnsEnabled, turretEnabled, running, restored;

        public void Begin()
        {
            if (running) return;
            running = true;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); Directory.CreateDirectory(ArtifactFolder);
            report.Add("Arena 1 exploration — saved production scene; Unity " + Application.unityVersion);
            report.Add("Opt-in fixtures move the real car between cases and isolate enemies. Auto-gun and proximity claims use production components; direct Hit calls are disclosed threshold/affinity tests, not natural progression.");
            Write(); StartCoroutine(RunGuarded());
        }
        IEnumerator RunGuarded()
        {
            yield return null; yield return null;
            foreach (var factory in new Func<IEnumerator>[] { SceneAndMarkers, CacheGunAndLineOfSight, RewardThresholds, ReentrantRestart, SecretRangeAndState, EnemyCounterplay, AuthoredRoutesAndReachability, RestartContract })
            {
                var stack = new Stack<IEnumerator>(); stack.Push(factory()); bool failed = false;
                while (stack.Count > 0 && !failed)
                {
                    IEnumerator part = stack.Peek(); bool more = false; object current = null;
                    try { more = part.MoveNext(); if (more) current = part.Current; }
                    catch (Exception error) { Check(factory.Method.Name + " exception", false, error.ToString()); failed = true; }
                    if (failed) break;
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (current is IEnumerator nested) stack.Push(nested); else yield return current;
                }
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                if (!s || !exploration) break;
            }
            try { Restore(); } catch (Exception error) { Check("restore", false, error.ToString()); }
            report.Add("RESULT checks=" + Checks + "; failures=" + Failures + "; saved scene and tuning were not modified.");
            running = false; Completed = true; Write();
        }

        IEnumerator SceneAndMarkers()
        {
            s = GetComponent<GameSession>(); if (!s) throw new InvalidOperationException("Attach checks to the actual saved GameSession.");
            exploration = s.exploration; layout = Object.FindAnyObjectByType<LevelLayout>();
            workshops = s.minimap.workshops; workshopEnabled = workshops.Select(w => w.enabled).ToArray();
            constraints = s.vehicle.Body.constraints; spawnsEnabled = s.spawns.enabled; turretEnabled = s.turret.enabled;
            Check("production exploration and baseline remain connected", exploration && exploration.session == s && exploration.tuning &&
                layout && layout.session == s && Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 5.25f) < .001f &&
                Mathf.Abs(s.health.Maximum - 100) < .001f && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1,
                "Initial bullet=" + s.PlayerDamage(s.turret.tuning.bulletDamage) + "; max HP=" + s.health.Maximum);
            if (!exploration) throw new InvalidOperationException("Run the exploration installer and save the scene before acceptance.");
            caches = exploration.caches;
            Check("saved reward objects have unique identities and clear labels", caches != null && caches.Length == 8 &&
                caches.All(c => c && c.session == s && c.exploration == exploration && !string.IsNullOrWhiteSpace(c.id) && c.displayText &&
                    c.availableVisual && c.consumedVisual && c.amount > 0 && !string.IsNullOrWhiteSpace(c.RewardLabel)) &&
                caches.Select(c => c.id).Distinct().Count() == caches.Length && exploration.SecretCount == 3,
                "Authored caches=" + (caches == null ? 0 : caches.Length) + "; secret count=" + exploration.SecretCount);
            Check("four readable regions coexist with the finite enemy roster", exploration.zones.Length == 4 &&
                exploration.zones.All(z => z != null && !string.IsNullOrWhiteSpace(z.id) && !string.IsNullOrWhiteSpace(z.displayName) && !string.IsNullOrWhiteSpace(z.hint) && z.radius > 0) &&
                exploration.zones.Select(z => z.id).Distinct().Count() == 4 && EnemyRoster().Length == 16 && EnemyRoster().All(t => t.Alive),
                "Regions=" + exploration.zones.Length + "; live enemy roster=" + EnemyRoster().Count(t => t.Alive));
            s.turret.enabled = false; s.health.DamageEnabled = false; FreezeEnemies();
            yield return new WaitForSecondsRealtime(.2f);
            bool markers = true;
            foreach (var cache in caches)
            {
                bool visible = s.minimap.TryGetMarker(cache.transform, out var point);
                bool expected = ArenaMinimap.IsCacheVisible(cache);
                markers &= visible == expected && (!visible || Vector2.Distance(point, s.minimap.Project(cache.transform.position)) < .02f);
            }
            Check("cache markers share the real north-up world projection", markers,
                "All breakables and revealed secrets use cache.transform; unrevealed secret locations remain hidden");
            Canvas.ForceUpdateCanvases();
            var map = s.minimap;
            var background = map.backgroundImage;
            var backdrop = map.mapRect.Find("Warm sand behind arena");
            Check("saved minimap bake draws above the opaque sand backdrop", background && background.enabled &&
                background.gameObject.activeInHierarchy && map.BackgroundTexture && backdrop && backdrop.gameObject.activeInHierarchy &&
                background.transform.parent == map.mapRect && backdrop.parent == map.mapRect &&
                backdrop.GetSiblingIndex() < background.transform.GetSiblingIndex(),
                "Backdrop sibling=" + (backdrop ? backdrop.GetSiblingIndex() : -1) + "; baked image sibling=" +
                (background ? background.transform.GetSiblingIndex() : -1) + "; the terrain image remains visible after rebaking");
            bool fitted = false, projectionMatches = false;
            if (background)
            {
                var imageRect = background.rectTransform;
                fitted = Vector2.Distance(imageRect.rect.size, map.mapRect.rect.size * map.contentScale) < .02f;
                var actualCorners = new Vector3[4]; imageRect.GetWorldCorners(actualCorners);
                var bounds = map.WorldBounds;
                var worldCorners = new[]
                {
                    new Vector3(bounds.min.x, 0, bounds.min.z), new Vector3(bounds.min.x, 0, bounds.max.z),
                    new Vector3(bounds.max.x, 0, bounds.max.z), new Vector3(bounds.max.x, 0, bounds.min.z)
                };
                projectionMatches = map.hasBakedWorldBounds && background.uvRect == new Rect(0, 0, 1, 1);
                for (int i = 0; i < actualCorners.Length; i++)
                    projectionMatches &= Vector2.Distance(map.mapRect.InverseTransformPoint(actualCorners[i]), map.Project(worldCorners[i])) < .02f;
            }
            Check("saved minimap texture and markers share the complete fitted projection", fitted && projectionMatches,
                "Expected image size=" + map.mapRect.rect.size * map.contentScale + "; actual=" +
                (background ? background.rectTransform.rect.size.ToString() : "missing") +
                "; all four baked-world corners match marker projection=" + projectionMatches);
            Check("reward targets do not inflate the enemy roster or boss HUD", exploration.Targets.Count() == caches.Count(c => !c.proximityOnly) &&
                exploration.Targets.All(t => t is ArenaRewardCache && !t.UsesBossHealthHud) && s.zone1.ExtraTargets.Length == 2,
                "Breakable targets=" + exploration.Targets.Count() + "; original ordinary/boss pools remain separate");
            Check("player retains physical wheel steering and dynamic body", !s.vehicle.Body.isKinematic && constraints == RigidbodyConstraints.None &&
                s.vehicle.wheels.Length == 4 && s.vehicle.wheels.All(w => w.enabled && w.suspensionDistance > .05f) &&
                Mathf.Approximately(s.vehicle.Body.mass, s.vehicle.tuning.mass), "Detailed wheel-physics regression is run separately; exploration does not replace the player");
            Capture("fresh-start");
        }

        IEnumerator CacheGunAndLineOfSight()
        {
            Isolate();
            var cache = caches.First(c => !c.proximityOnly && c.reward == ArenaRewardKind.Damage);
            Vector3 site = FiringSite(cache);
            Place(site, Quaternion.LookRotation(Flat(cache.transform.position - site)));
            Check("authored cache has a real hittable target collider", cache.Alive && s.turret.CanAttack(cache) &&
                cache.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger), "Cache=" + cache.id + "; car=" + site);
            yield return null; yield return null;
            Capture("cache-before-reward");
            var wall = Obstruct(s.turret.muzzle.position, cache.AimPoint);
            float hp = cache.CurrentHealth; int kills = s.Kills, earned = s.ScrapEarned;
            s.turret.enabled = true; s.turret.ResetCombat();
            yield return new WaitForSecondsRealtime(.7f);
            Check("solid cover blocks cache acquisition and damage", !s.turret.CanAttack(cache) && s.turret.Target != cache &&
                Mathf.Approximately(cache.CurrentHealth, hp) && !cache.Claimed,
                "Unchanged durability=" + cache.CurrentHealth + "; solid obstruction lies between real muzzle and cache");
            wall.SetActive(false); Physics.SyncTransforms(); s.turret.ResetCombat();
            bool acquired = false; float deadline = Time.realtimeSinceStartup + 8;
            while (!cache.Claimed && Time.realtimeSinceStartup < deadline) { acquired |= s.turret.Target == cache; yield return null; }
            s.turret.enabled = false;
            Check("real auto gun discovers and consumes an uncovered cache", acquired && cache.Claimed && !cache.Alive && s.turret.ShotsFired > 0 &&
                s.Kills == kills && s.ScrapEarned == earned && s.pickups.StoredValue == 0 && s.runStats.BonusDamagePercent > 0,
                "Actual acquired=" + acquired + "; shots=" + s.turret.ShotsFired + "; claimed=" + cache.Claimed + "; kills=" + s.Kills);
            yield return new WaitForSecondsRealtime(.15f);
            Check("claimed cache leaves targeting and minimap", !exploration.Targets.Contains(cache) && !s.minimap.TryGetMarker(cache.transform, out _) &&
                !cache.GetComponentsInChildren<Collider>(true).Any(c => c.enabled), "Claimed world object pays once and no longer blocks shots");
            Capture("cache-after-reward");
        }

        IEnumerator RewardThresholds()
        {
            Isolate();
            var damage = caches.First(c => !c.proximityOnly && c.reward == ArenaRewardKind.Damage);
            float baseShot = s.PlayerDamage(s.turret.tuning.bulletDamage), hp = damage.CurrentHealth;
            damage.Hit(hp - .25f, Vector3.forward);
            Check("cache requires the full real damage threshold", !damage.Claimed && damage.Alive && Mathf.Abs(damage.CurrentHealth - .25f) < .001f &&
                s.runStats.BonusDamagePercent == 0, "Sublethal Hit leaves0.25durability and no partial reward");
            damage.ResetCache();
            Check("same-run reset preserves partial damage", Mathf.Abs(damage.CurrentHealth - .25f) < .001f && !damage.Claimed, "ResetCache is not a durability refill exploit");
            int notifications = 0;
            Action reentrant = () => { if (damage.CurrentHealth <= 0) { notifications++; damage.TryCollect(); } };
            damage.HealthChanged += reentrant;
            try { damage.Hit(.25f, Vector3.forward); }
            finally { damage.HealthChanged -= reentrant; }
            float rewarded = s.PlayerDamage(s.turret.tuning.bulletDamage), bonus = s.runStats.BonusDamagePercent;
            Check("damage cache changes the actual outgoing shot value", damage.Claimed && Mathf.Abs(bonus - damage.amount) < .001f &&
                Mathf.Abs(rewarded - baseShot * (1 + damage.amount / 100)) < .001f && rewarded > baseShot && s.runStats.AttackLevel == 1,
                "Bullet=" + baseShot + " -> " + rewarded + "; bonus=" + bonus + "%; station level remains1");
            Check("reward feedback describes the actual run bonus", notifications > 0 && s.hud.upgradeFeedbackText.gameObject.activeInHierarchy &&
                s.hud.upgradeFeedbackText.text.Contains("DAMAGE") && s.hud.upgradeFeedbackText.text.Contains(damage.amount.ToString("0.#") + "%") &&
                s.hud.upgradeFeedbackText.text.Contains("THIS RUN"),
                "Health-change subscriber attempted reentrant claim; a single reward produced toast=" + s.hud.upgradeFeedbackText.text.Replace('\n', ' '));
            var raider = s.spawns.authoredRaiders.First(r => !r.arenaBoss);
            raider.ResetEncounter(); raider.AIEnabled = false; float enemyHP = raider.Health;
            raider.Hit(rewarded, Vector3.forward);
            Check("rewarded bullet amount reaches real enemy health", raider.Alive && Mathf.Abs(enemyHP - raider.Health - rewarded) < .001f,
                "Direct Hit through the normal Raider damage path; actual HP delta=" + (enemyHP - raider.Health));
            raider.Despawn();
            bool retry = damage.TryCollect(); damage.Hit(1000, Vector3.forward); damage.ResetCache(); exploration.ResetRun();
            Check("reward cannot be duplicated by retry hit or reset", !retry && damage.Claimed && !damage.Alive &&
                Mathf.Abs(s.runStats.BonusDamagePercent - bonus) < .001f && s.Kills == 0 && s.pickups.StoredValue == 0,
                "Repeated callbacks preserve a single bonus and create neither kill nor loot");
            var ram = caches.First(c => c.reward == ArenaRewardKind.Ram);
            float beforeRam = s.PlayerDamage(30, true), beforeBullet = s.PlayerDamage(s.turret.tuning.bulletDamage);
            Claim(ram);
            Check("RAM reward strengthens ram damage without a second bullet buff", ram.Claimed &&
                Mathf.Abs(s.PlayerDamage(30, true) - beforeRam * (1 + ram.amount / 100)) < .001f &&
                Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - beforeBullet) < .001f && s.runStats.BonusRamPercent == ram.amount,
                "Actual ram=" + beforeRam + " -> " + s.PlayerDamage(30, true) + "; bullet retained=" + beforeBullet);
            var health = caches.First(c => c.reward == ArenaRewardKind.Health);
            s.health.DamageEnabled = true; s.health.Damage(25, false); s.health.DamageEnabled = false;
            float previousMax = s.health.Maximum, previousHP = s.health.Current;
            Claim(health);
            Check("max HP reward retains existing damage", health.Claimed && Mathf.Abs(s.health.Maximum - previousMax - health.amount) < .001f &&
                Mathf.Abs(s.health.Current - previousHP - health.amount) < .001f && Mathf.Abs(s.health.Maximum - s.health.Current - 25) < .01f &&
                s.runStats.HealthLevel == 1, "Max/current=" + previousMax + "/" + previousHP + " -> " + s.health.Maximum + "/" + s.health.Current);
            string healthToast = s.hud.upgradeFeedbackText.text; int rewardSerial = exploration.RewardSerial;
            yield return new WaitForSecondsRealtime(3.4f);
            bool expired = !s.hud.upgradeFeedbackText.gameObject.activeSelf;
            var secondHealth = caches.First(c => c.reward == ArenaRewardKind.Health && c != health);
            Claim(secondHealth);
            Check("two equal health rewards each produce fresh feedback", expired && secondHealth.Claimed && exploration.RewardSerial == rewardSerial + 1 &&
                s.hud.upgradeFeedbackText.gameObject.activeInHierarchy && s.hud.upgradeFeedbackText.text == healthToast &&
                Mathf.Abs(s.runStats.BonusMaxHP - health.amount - secondHealth.amount) < .001f,
                "The first toast expired; a separate equal reward starts a new visible toast rather than being deduplicated by text");
            s.SetPaused(true);
            Check("pause result describes actual accumulated exploration effects", s.hud.overlayDetails.text.Contains("DAMAGE  " + HudView.Number(s.PlayerDamage(s.turret.tuning.bulletDamage))) &&
                s.hud.overlayDetails.text.Contains("LOOT DMG +" + HudView.Number(s.runStats.BonusDamagePercent) + "%") &&
                s.hud.overlayDetails.text.Contains("RAM +" + HudView.Number(s.runStats.BonusRamPercent) + "%") &&
                s.hud.overlayDetails.text.Contains("EXTRA HP +" + HudView.Number(s.runStats.BonusMaxHP)), s.hud.overlayDetails.text.Replace('\n', ' '));
            Capture("pause-real-stats");
            s.SetPaused(false); yield return null;
        }

        IEnumerator ReentrantRestart()
        {
            foreach (var kind in new[] { ArenaRewardKind.Damage, ArenaRewardKind.Health })
            {
                Isolate();
                var cache = caches.First(c => !c.proximityOnly && c.reward == kind);
                int originalRun = s.RunSerial, restarted = 0, collectedCallbacks = 0;
                Action restartOnStats = null;
                restartOnStats = () =>
                {
                    // Restart resets stats and publishes Changed again; detach before entering it.
                    s.runStats.Changed -= restartOnStats;
                    restarted++;
                    s.Restart();
                };
                UnityEngine.Events.UnityAction collected = () => collectedCallbacks++;
                s.runStats.Changed += restartOnStats; cache.onCollected.AddListener(collected);
                try { cache.Hit(cache.CurrentHealth + 1, Vector3.forward); }
                finally { s.runStats.Changed -= restartOnStats; cache.onCollected.RemoveListener(collected); }
                s.turret.enabled = false; s.health.DamageEnabled = false; FreezeEnemies();
                Check(kind + " reward interrupted by real Restart preserves the new run", restarted == 1 && s.RunSerial == originalRun + 1 &&
                    s.Playing && s.Scrap == 0 && s.ScrapEarned == 0 && s.Kills == 0 && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 &&
                    s.runStats.BonusDamagePercent == 0 && s.runStats.BonusRamPercent == 0 && s.runStats.BonusMaxHP == 0 &&
                    Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 5.25f) < .001f &&
                    Mathf.Abs(s.health.Maximum - 100) < .001f && Mathf.Abs(s.health.Current - 100) < .001f &&
                    !cache.Claimed && cache.Alive && Mathf.Abs(cache.CurrentHealth - cache.MaxHealth) < .001f &&
                    cache.availableVisual.activeSelf && !cache.consumedVisual.activeSelf,
                    "A one-shot stats.Changed subscriber restarted during production Hit/claim; run=" + originalRun + " -> " + s.RunSerial +
                    "; callbacks=" + restarted + "; cache HP=" + cache.CurrentHealth + "; player HP=" + s.health.Current);
                yield return null; yield return null;
                Check(kind + " interrupted reward emits no stale result after Restart", collectedCallbacks == 0 && exploration.RewardSerial == 0 &&
                    string.IsNullOrEmpty(exploration.LastReward) && exploration.SecretsFound == 0 &&
                    !s.hud.upgradeFeedbackText.gameObject.activeSelf && caches.All(c => !c.Claimed),
                    "Old-run collected callbacks=" + collectedCallbacks + "; reward serial=" + exploration.RewardSerial +
                    "; last reward='" + exploration.LastReward + "'; a new run receives no old bonus, claim or toast");
            }
        }

        IEnumerator SecretRangeAndState()
        {
            Isolate();
            var secret = caches.First(c => c.proximityOnly && c.secret);
            Check("distant secret starts absent from the minimap", !secret.Revealed && !s.minimap.TryGetMarker(secret.transform, out _),
                "A fresh run does not advertise the distant hidden cache");
            Vector3 near = NearbySite(secret, Mathf.Min(secret.radius * .8f, 2.4f));
            Vector3 direction = Flat(near - secret.transform.position).normalized;
            Place(Ground(secret.transform.position + direction * (secret.radius + .5f)) + Vector3.up * .7f, s.startRotation);
            secret.enabled = true; yield return new WaitForSecondsRealtime(.25f); secret.enabled = false;
            Check("approaching reveals a secret without collecting it", secret.Revealed && !secret.Claimed && s.minimap.TryGetMarker(secret.transform, out var discovered) &&
                Vector2.Distance(discovered, s.minimap.Project(secret.transform.position)) < .02f,
                "Automatic discovery occurs before the smaller collection radius");
            Capture("secret-near");
            Place(s.startPosition, s.startRotation); yield return new WaitForSecondsRealtime(.12f);
            Check("discovery remains on the map after driving away", secret.Revealed && s.minimap.TryGetMarker(secret.transform, out _),
                "Discovery is remembered for this run rather than flickering at the reveal boundary");
            Place(Ground(secret.transform.position + direction * (secret.radius + .5f)) + Vector3.up * .7f, s.startRotation);
            Check("secret rejects a claim outside its collection radius", !secret.TryCollect() && !secret.Claimed, "Real proximity boundary radius=" + secret.radius);
            Place(near, s.startRotation);
            var wall = Obstruct(s.vehicle.transform.position + Vector3.up * .7f, secret.AimPoint);
            Check("secret cannot be collected through solid scenery", !secret.InCollectionRange() && !secret.TryCollect() && !secret.Claimed,
                "Distance is inside radius; the production LOS query still rejects the claim");
            wall.SetActive(false); Physics.SyncTransforms();
            s.SetPaused(true); secret.enabled = true;
            yield return new WaitForSecondsRealtime(.2f);
            Check("pause prevents proximity rewards", !secret.TryCollect() && !secret.Claimed && exploration.SecretsFound == 0,
                "A nearby clear chest remains unclaimed while paused");
            var pausedCrate = caches.First(c => !c.proximityOnly); float pausedHP = pausedCrate.CurrentHealth;
            pausedCrate.Hit(pausedHP + 1, Vector3.forward);
            Check("pause also prevents destructible rewards", !pausedCrate.Claimed && pausedCrate.CurrentHealth == pausedHP,
                "A late damage callback cannot award a paused run");
            s.SetPaused(false); int kills = s.Kills, beforeScrap = s.Scrap, beforeEarned = s.ScrapEarned;
            s.health.DamageEnabled = true; s.health.Damage(25, false); s.health.DamageEnabled = false; float hp = s.health.Current;
            yield return new WaitForSecondsRealtime(.35f);
            Check("normal proximity update claims one secret reward", secret.Claimed && secret.Revealed && exploration.SecretsFound == 1 &&
                s.Kills == kills && s.pickups.StoredValue == 0 && s.Scrap == beforeScrap + Mathf.RoundToInt(secret.amount) &&
                s.ScrapEarned == beforeEarned + Mathf.RoundToInt(secret.amount) && Mathf.Abs(s.health.Current - Mathf.Min(s.health.Maximum, hp + secret.repairAmount)) < .05f,
                "Secrets=" + exploration.SecretsFound + "; score=" + s.Scrap + "; direct earned=" + s.ScrapEarned + "; HP=" + s.health.Current);
            int rewardedScrap = s.Scrap; secret.ResetCache(); exploration.ResetRun();
            Check("secret discovery and reward survive same-run reset without duplication", secret.Claimed && secret.Revealed && !secret.TryCollect() &&
                exploration.SecretsFound == 1 && s.Scrap == rewardedScrap, "A reset helper cannot farm the chest repeatedly");

            Isolate(); secret = caches.First(c => c.proximityOnly && c.secret); Place(NearbySite(secret, 2), s.startRotation);
            s.CompleteArena();
            Check("completed state cannot award nearby or broken caches", !secret.TryCollect() && !secret.Claimed && exploration.SecretsFound == 0,
                "Completion is terminal for exploration rewards");
            var crate = caches.First(c => !c.proximityOnly); float durability = crate.CurrentHealth;
            crate.Hit(durability + 1, Vector3.forward);
            Check("terminal hit cannot consume a cache", !crate.Claimed && crate.CurrentHealth == durability, "No post-victory damage or bonus award");

            Isolate(); secret = caches.First(c => c.proximityOnly && c.secret); Place(NearbySite(secret, 2), s.startRotation);
            s.health.DamageEnabled = true; s.health.Damage(s.health.Maximum + 1, false);
            Check("defeat cannot award a chest", s.State == SessionState.Defeated && !secret.TryCollect() && !secret.Claimed,
                "A dead player cannot receive a late proximity callback");
            yield return null;
        }

        IEnumerator EnemyCounterplay()
        {
            Isolate();
            var buggy = s.spawns.authoredBuggies.First();
            buggy.Spawn(buggy.transform.position, buggy.transform.rotation, -1); buggy.AIEnabled = false; buggy.Body.constraints = RigidbodyConstraints.FreezeAll;
            var affinity = buggy.GetComponent<ArenaCombatAffinity>();
            float hp = buggy.Health; buggy.Hit(10, Vector3.forward); float bulletDelta = hp - buggy.Health;
            hp = buggy.Health; buggy.Hit(10, Vector3.forward, true); float ramDelta = hp - buggy.Health;
            Check("armored Buggy has an explicit effective RAM weakness", affinity && affinity.profile == ArenaAffinityProfile.Buggy &&
                Mathf.Abs(bulletDelta - 10) < .001f && Mathf.Abs(ramDelta - 10 * exploration.tuning.buggyRamMultiplier) < .001f && ramDelta > bulletDelta,
                "Same10input through enemy.Hit; bullet/RAM HP delta=" + bulletDelta + "/" + ramDelta);
            buggy.Despawn();
            var boss = s.zone1.ExtraTargets.OfType<Raider>().Single(r => r.arenaBoss);
            boss.ResetEncounter(); boss.AIEnabled = false; hp = boss.Health;
            boss.Hit(10, Vector3.forward); float normal = hp - boss.Health;
            var point = Ground(boss.transform.position + boss.transform.forward * 4.6f) + Vector3.up * .7f;
            Place(point, Quaternion.LookRotation(-boss.transform.forward)); boss.AIEnabled = true;
            float deadline = Time.realtimeSinceStartup + 5;
            while (!boss.IsTelegraphing && Time.realtimeSinceStartup < deadline) yield return null;
            bool committed = boss.IsTelegraphing;
            while (!boss.IsRecovering && Time.realtimeSinceStartup < deadline) yield return null;
            hp = boss.Health; boss.Hit(10, Vector3.forward); float recovering = hp - boss.Health;
            var bossAffinity = boss.GetComponent<ArenaCombatAffinity>();
            Check("Warlord recovery creates a real timed bullet opportunity", committed && boss.IsRecovering && bossAffinity &&
                bossAffinity.profile == ArenaAffinityProfile.Warlord && Mathf.Abs(normal - 10) < .001f &&
                Mathf.Abs(recovering - 10 * exploration.tuning.warlordRecoveryBulletMultiplier) < .001f && recovering > normal,
                "A normal committed strike ran before the test hit; normal/recovery HP delta=" + normal + "/" + recovering);
            hp = boss.Health; boss.Hit(10, Vector3.forward, true);
            Check("Warlord recovery bonus is specific to bullets", Mathf.Abs(hp - boss.Health - 10) < .001f,
                "Recovery does not accidentally multiply RAM a second time");
        }

        IEnumerator AuthoredRoutesAndReachability()
        {
            Isolate();
            yield return VerifyAuthoredGeometry();
            yield return WorkshopDriveThrough();
        }

        IEnumerator VerifyAuthoredGeometry()
        {
            const string source = "Assets/RoadGunner/Levels/Greenwood/Exploration/Exploration layout.asset";
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>(source);
            var owned = layout.authoredContentRoot.Find("Arena exploration");
            Check("one idempotent exploration root and editable authoring source exist", definition && owned &&
                Enumerable.Range(0, layout.authoredContentRoot.childCount).Count(i => layout.authoredContentRoot.GetChild(i).name == "Arena exploration") == 1 &&
                !layout.navigationNeedsBake, "Saved source=" + source + "; navigationNeedsBake=" + layout.navigationNeedsBake);
            if (!definition || !owned) yield break;
            var road = owned.Find("Physical connected roads and combat pockets").GetComponent<MeshCollider>();
            var routeRoot = owned.Find("Routes");
            Check("visible road surface uses a real shared static collision mesh", road && road.enabled && !road.isTrigger && road.sharedMesh &&
                road.GetComponent<MeshFilter>().sharedMesh == road.sharedMesh && road.sharedMesh.vertexCount > 100,
                "Road collider/render vertices=" + (road && road.sharedMesh ? road.sharedMesh.vertexCount : 0));
            var definitions = Field<Array>(definition, "routes");
            var allNodes = new List<Vector3>(); var checkedLoops = new Dictionary<string, int>();
            var details = new List<string> { "route,width_m,samples,nav_samples,car_clear_samples,road_cross_sections" };
            bool startFound = NavMesh.SamplePosition(s.startPosition, out var start, 3, NavMesh.AllAreas);
            foreach (var item in definitions)
            {
                string id = Field<string>(item, "id"), loop = Field<string>(item, "loopId");
                float width = Field<float>(item, "widthMeters");
                var route = routeRoot.Find(id);
                var points = route ? Enumerable.Range(0, route.childCount).Select(i => route.GetChild(i).position).ToArray() : Array.Empty<Vector3>();
                allNodes.AddRange(points);
                int navigable = 0, clear = 0, crossSections = 0; string obstruction = "";
                for (int i = 0; i < points.Length; i++)
                {
                    if (NavMesh.SamplePosition(points[i], out _, 1, NavMesh.AllAreas)) navigable++;
                    Vector3 direction = Flat(points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                    string hit = "No route tangent";
                    if (direction.sqrMagnitude > .1f && VehicleClear(Ground(points[i]) + Vector3.up * .7f, Quaternion.LookRotation(direction), null, out hit)) clear++;
                    else if (obstruction == "") obstruction = hit;
                    if (i % Mathf.Max(1, points.Length / 10) == 0 && direction.sqrMagnitude > .1f)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, direction);
                        bool covered = true;
                        foreach (float offset in new[] { -width * .42f, 0, width * .42f })
                        {
                            Vector3 p = points[i] + side * offset;
                            covered &= road.Raycast(new Ray(p + Vector3.up * 4, Vector3.down), out _, 12);
                        }
                        if (covered) crossSections++;
                    }
                    if (i % 80 == 0) yield return null;
                }
                bool connected = points.Length >= 2 && startFound && CompletePath(start.position, points[0]) && CompletePath(start.position, points[points.Length - 1]);
                int sections = points.Length == 0 ? 0 : (points.Length - 1) / Mathf.Max(1, points.Length / 10) + 1;
                Check("authored route " + id + " has navigation and chassis clearance", connected && width >= 8 && points.Length >= 2 &&
                    navigable == points.Length && clear == points.Length && crossSections == sections,
                    "Width=" + width + "m; nav=" + navigable + "/" + points.Length + "; actual car clear=" + clear +
                    "; surface width samples=" + crossSections + "/" + sections + "; first obstruction=" + obstruction);
                details.Add(id + "," + width + "," + points.Length + "," + navigable + "," + clear + "," + crossSections);
                if (connected && clear == points.Length) checkedLoops[loop] = checkedLoops.TryGetValue(loop, out int count) ? count + 1 : 1;
            }
            File.WriteAllLines(ArtifactFolder + "/RouteClearance.csv", details);
            Check("parallel Warlord and Rustworks routes remain usable", checkedLoops.TryGetValue("warlord-loop", out int warlordRoutes) && warlordRoutes >= 2 &&
                checkedLoops.TryGetValue("rustworks-loop", out int rustRoutes) && rustRoutes >= 2 && checkedLoops.TryGetValue("salvage-loop", out int salvageRoutes) && salvageRoutes >= 2,
                "Each alternate authored branch has its own complete centreline clearance and connects to the same start navigation component");

            Vector3 front = (s.vehicle.wheels[0].transform.position + s.vehicle.wheels[1].transform.position) * .5f;
            Vector3 rear = (s.vehicle.wheels[2].transform.position + s.vehicle.wheels[3].transform.position) * .5f;
            float steer = s.vehicle.tuning.steeringBySpeed.Evaluate(s.vehicle.tuning.turningSpeed / s.vehicle.tuning.maxSpeed);
            float turnRadius = Flat(front - rear).magnitude / Mathf.Tan(steer * Mathf.Deg2Rad) * 1.25f;
            foreach (var item in Field<Array>(definition, "pockets"))
            {
                string id = Field<string>(item, "id"); var pocket = owned.Find("Combat pockets/" + id);
                bool clear = pocket && Field<float>(item, "radiusMeters") > turnRadius + 2.5f;
                for (int i = 0; clear && i < 24; i++)
                {
                    Vector3 radial = Quaternion.Euler(0, i * 15, 0) * Vector3.forward;
                    Vector3 position = Ground(pocket.position + radial * turnRadius) + Vector3.up * .7f;
                    clear &= VehicleClear(position, Quaternion.LookRotation(Vector3.Cross(Vector3.up, radial)), null, out _);
                }
                Check("turning pocket " + id + " fits the physical car sweep", clear,
                    "Static full-body circle clearance at conservative wheel-geometry radius=" + turnRadius.ToString("F2") + "m; actual dynamic arc tested by the separate physics suite");
            }

            foreach (var cache in caches)
            {
                Vector3 site = cache.proximityOnly ? NearbySite(cache, Mathf.Min(2, cache.radius * .7f)) : FiringSite(cache);
                bool approach = allNodes.OrderBy(p => Flat(p - site).sqrMagnitude).Take(30).Any(p => Flat(p - site).sqrMagnitude < 25 * 25 && ClearSegment(p, site));
                Check("reward " + cache.id + " has a real reachable approach", startFound && CompletePath(start.position, site) && approach,
                    "NavMesh plus a chassis-clear connection from an authored route; collection/firing site=" + site);
            }
            // Exercise the normal input-driven onboarding transition so the production region card is visible.
            // The body remains frozen for these authored-location fixtures; this is not a natural driving capture.
            s.input.testDirection = Vector2.up; yield return null; yield return null;
            s.input.testDirection = Vector2.zero;
            foreach (var zone in exploration.zones)
            {
                bool reachable = startFound && CompletePath(start.position, zone.center);
                Place(Ground(zone.center) + Vector3.up * .7f, s.startRotation); yield return null; yield return null;
                string expectedHint = (zone.hint ?? "")
                    .Replace("{buggyRamBonus}", ((exploration.tuning.buggyRamMultiplier - 1) * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{bossRecoveryBonus}", ((exploration.tuning.warlordRecoveryBulletMultiplier - 1) * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                Check("region " + zone.id + " is reachable and identifies itself", reachable && exploration.CurrentRegionId == zone.id &&
                    exploration.CurrentRegionName == zone.displayName && exploration.CurrentRegionHint == expectedHint,
                    "Actual region UI source=" + exploration.CurrentRegionName + "; hint=" + exploration.CurrentRegionHint);
                if (zone.id == "rustworks" || zone.id == "ash-ridge")
                {
                    yield return new WaitForSecondsRealtime(.15f);
                    Capture("region-" + zone.id);
                    if (zone.id == "ash-ridge") yield return CaptureLandscapeRegion();
                }
            }
            var blockers = owned.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c != road && !c.GetComponentInParent<ArenaRewardCache>()).ToArray();
            bool solidSight = blockers.Length > 0;
            foreach (var blocker in blockers)
            {
                var bounds = blocker.bounds;
                Vector3 a = bounds.center - Vector3.right * (bounds.extents.x + .5f), b = bounds.center + Vector3.right * (bounds.extents.x + .5f);
                solidSight &= (exploration.tuning.obstructionMask.value & (1 << blocker.gameObject.layer)) != 0 &&
                    !Physics.GetIgnoreLayerCollision(s.vehicle.gameObject.layer, blocker.gameObject.layer) &&
                    Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), exploration.tuning.obstructionMask, QueryTriggerInteraction.Ignore).Any(hit => hit.collider == blocker);
            }
            Check("new solid landmarks block both vehicle collision and line of sight", solidSight,
                "Actual authored non-trigger scenery colliders=" + blockers.Length + "; all belong to the collision and exploration LOS masks");
        }

        IEnumerator WorkshopDriveThrough()
        {
            var workshop = workshops.First();
            Vector3 forward = Quaternion.Euler(0, 45, 0) * Vector3.forward;
            Vector3 entry = Ground(workshop.transform.position - forward * (workshop.healingRadius + 2)) + Vector3.up * .7f;
            Vector3 exit = Ground(workshop.transform.position + forward * (workshop.healingRadius + 2)) + Vector3.up * .7f;
            Check("repair hub has an unobstructed full-car drive-through", ClearSegment(entry, exit), "Entry=" + entry + "; exit=" + exit);
            Place(entry, Quaternion.LookRotation(forward));
            s.vehicle.Body.constraints = constraints; s.health.DamageEnabled = true; s.health.Damage(40, false); s.health.DamageEnabled = false;
            workshop.enabled = true; s.input.testBrake = false;
            Vector3 right = Flat(s.input.view.transform.right).normalized, up = Flat(s.input.view.transform.forward).normalized;
            s.input.testDirection = new Vector2(Vector3.Dot(forward, right), Vector3.Dot(forward, up));
            float before = s.health.Current, deadline = Time.realtimeSinceStartup + 9; int healingFrames = 0, groundedFrames = 0;
            while (Vector3.Dot(s.vehicle.Body.position - workshop.transform.position, forward) < workshop.healingRadius + 1 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (workshop.IsRepairing) healingFrames++;
                if (s.vehicle.GroundedWheels >= 3) groundedFrames++;
            }
            s.input.testDirection = Vector2.zero; s.input.testBrake = true; s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll;
            workshop.enabled = false;
            Check("real wheel-driven car can enter heal and leave the service hub", Vector3.Dot(s.vehicle.Body.position - workshop.transform.position, forward) >= workshop.healingRadius + 1 &&
                healingFrames > 0 && groundedFrames > 30 && s.health.Current > before && Vector3.Dot(s.vehicle.transform.up, Vector3.up) > .9f,
                "Actual healing frames=" + healingFrames + "; grounded frames=" + groundedFrames + "; HP=" + before + " -> " + s.health.Current + "; car=" + s.vehicle.Body.position);
        }

        static T Field<T>(object source, string name) => (T)source.GetType().GetField(name).GetValue(source);
        bool CompletePath(Vector3 start, Vector3 destination)
        {
            return NavMesh.SamplePosition(destination, out var to, 2, NavMesh.AllAreas) && PathComplete(start, to.position);
        }
        static bool PathComplete(Vector3 start, Vector3 end)
        { var path = new NavMeshPath(); return NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete; }
        bool ClearSegment(Vector3 a, Vector3 b)
        {
            Vector3 direction = Flat(b - a); if (direction.sqrMagnitude < .01f) return VehicleClear(Ground(a) + Vector3.up * .7f, s.startRotation, null, out _);
            var rotation = Quaternion.LookRotation(direction); int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / 1.5f));
            for (int i = 0; i <= steps; i++)
                if (!VehicleClear(Ground(Vector3.Lerp(a, b, i / (float)steps)) + Vector3.up * .7f, rotation, null, out _)) return false;
            return true;
        }

        IEnumerator RestartContract()
        {
            Isolate();
            Claim(caches.First(c => c.reward == ArenaRewardKind.Damage));
            Claim(caches.First(c => c.reward == ArenaRewardKind.Ram));
            Claim(caches.First(c => c.reward == ArenaRewardKind.Health));
            var secret = caches.First(c => c.secret && c.proximityOnly); Claim(secret);
            int serial = s.RunSerial;
            s.Restart(); s.turret.enabled = false; s.health.DamageEnabled = false; FreezeEnemies();
            foreach (var cache in caches) cache.enabled = true;
            yield return new WaitForSecondsRealtime(.2f);
            Check("real Restart resets exploration without changing the original arena", s.RunSerial > serial && s.Playing && s.Scrap == 0 && s.Kills == 0 && s.ScrapEarned == 0 &&
                exploration.SecretsFound == 0 && s.runStats.BonusDamagePercent == 0 && s.runStats.BonusRamPercent == 0 && s.runStats.BonusMaxHP == 0 &&
                Mathf.Abs(s.PlayerDamage(s.turret.tuning.bulletDamage) - 5.25f) < .001f && Mathf.Abs(s.health.Maximum - 100) < .001f &&
                Mathf.Abs(s.health.Current - 100) < .001f && EnemyRoster().Length == 16 && EnemyRoster().All(t => t.Alive),
                "Run serial=" + serial + " -> " + s.RunSerial + "; shot=" + s.PlayerDamage(s.turret.tuning.bulletDamage) + "; HP=" + s.health.Current);
            bool markers = true;
            foreach (var cache in caches)
            {
                bool visible = s.minimap.TryGetMarker(cache.transform, out var point);
                markers &= !cache.Claimed && Mathf.Abs(cache.CurrentHealth - cache.MaxHealth) < .001f &&
                    (cache.secret && !cache.Revealed ? !visible : visible && Vector2.Distance(point, s.minimap.Project(cache.transform.position)) < .02f);
            }
            Check("Restart restores cache durability visuals and eligible map markers", markers && caches.All(c => c.availableVisual.activeSelf && !c.consumedVisual.activeSelf),
                "Original reward objects are rearmed once for the new run; secret visibility follows new-run discovery");
            Check("Restart forgets distant secret discovery and clears reward feedback", Flat(secret.transform.position - s.startPosition).magnitude > exploration.tuning.secretRevealRadius &&
                !secret.Revealed && !s.minimap.TryGetMarker(secret.transform, out _) && !s.hud.upgradeFeedbackText.gameObject.activeSelf,
                "Previously found secret is hidden again; old run bonus toast is absent");
        }

        void Isolate()
        {
            foreach (var go in temporary) if (go) go.SetActive(false);
            s.Restart(); s.spawns.enabled = false; s.turret.enabled = false; s.health.DamageEnabled = false;
            foreach (var r in s.spawns.pool) if (r) r.Despawn();
            foreach (var b in s.spawns.buggyPool) if (b) b.Despawn();
            s.zone1.DespawnAll(); foreach (var cache in caches) cache.enabled = false;
            foreach (var workshop in workshops) workshop.enabled = false;
            s.input.testInput = true; s.input.testDirection = Vector2.zero; s.input.testBrake = true;
            s.vehicle.Body.constraints = RigidbodyConstraints.FreezeAll; Place(s.startPosition, s.startRotation);
        }
        void FreezeEnemies()
        {
            foreach (var r in s.spawns.pool) if (r) r.AIEnabled = false;
            foreach (var b in s.spawns.buggyPool) if (b) { b.AIEnabled = false; FixtureBody(b).constraints = RigidbodyConstraints.FreezeAll; }
            foreach (var t in s.zone1.ExtraTargets)
                if (t is Raider r) r.AIEnabled = false; else if (t is WastelandMutant m) m.AIEnabled = false;
        }
        static Rigidbody FixtureBody(EnemyBuggy buggy)
        {
            // Inactive reserve pool entries have not necessarily run Awake, which initializes Body.
            // Read their required Rigidbody without activating or spawning those unused enemies.
            var body = buggy.Body ? buggy.Body : buggy.GetComponent<Rigidbody>();
            if (!body) throw new InvalidOperationException("Enemy Buggy has no required Rigidbody: " + buggy.name);
            return body;
        }
        CombatTarget[] EnemyRoster() => s.spawns.authoredRaiders.Cast<CombatTarget>().Concat(s.spawns.authoredBuggies).Concat(s.zone1.ExtraTargets).ToArray();
        void Claim(ArenaRewardCache cache)
        {
            if (cache.proximityOnly) { Place(NearbySite(cache, Mathf.Min(2, cache.radius * .7f)), s.startRotation); cache.TryCollect(); }
            else cache.Hit(cache.CurrentHealth + 1, Vector3.forward);
        }
        void Place(Vector3 p, Quaternion rotation) { s.vehicle.ResetPose(p, rotation); Physics.SyncTransforms(); s.follow.Snap(); }
        Vector3 Ground(Vector3 p)
        {
            var collider = layout.ground.GetComponent<Collider>();
            if (collider && collider.Raycast(new Ray(p + Vector3.up * 100, Vector3.down), out var hit, 250)) return hit.point;
            throw new InvalidOperationException("No authored terrain below " + p);
        }
        Vector3 FiringSite(ArenaRewardCache cache)
        {
            for (int i = 0; i < 32; i++)
            {
                Vector3 p = cache.transform.position + Quaternion.Euler(0, i * 11.25f, 0) * Vector3.forward * 7;
                if (!NavMesh.SamplePosition(p, out var nav, 1, NavMesh.AllAreas)) continue;
                p = Ground(nav.position) + Vector3.up * .7f;
                var rotation = Quaternion.LookRotation(Flat(cache.transform.position - p));
                if (!VehicleClear(p, rotation, cache, out _)) continue;
                Place(p, rotation);
                if (s.turret.CanAttack(cache) && !caches.Any(c => c != cache && c.Alive && Flat(c.transform.position - p).sqrMagnitude < Flat(cache.transform.position - p).sqrMagnitude && s.turret.CanAttack(c))) return p;
            }
            throw new InvalidOperationException("No clear authored firing site within range for " + cache.id);
        }
        Vector3 NearbySite(ArenaRewardCache cache, float distance)
        {
            for (int i = 0; i < 32; i++)
            {
                Vector3 p = cache.transform.position + Quaternion.Euler(0, i * 11.25f, 0) * Vector3.forward * distance;
                if (!NavMesh.SamplePosition(p, out var nav, .8f, NavMesh.AllAreas)) continue;
                p = Ground(nav.position) + Vector3.up * .7f;
                if (!VehicleClear(p, s.startRotation, cache, out _)) continue;
                Place(p, s.startRotation); if (cache.InCollectionRange()) return p;
            }
            throw new InvalidOperationException("No clear nearby authored collection site for " + cache.id);
        }
        GameObject Obstruct(Vector3 a, Vector3 b)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Temporary exploration LOS fixture"; go.hideFlags = HideFlags.DontSave;
            go.transform.position = (a + b) * .5f; go.transform.rotation = Quaternion.LookRotation(Flat(b - a));
            go.transform.localScale = new Vector3(2, 3, .25f); temporary.Add(go); Physics.SyncTransforms(); return go;
        }
        bool VehicleClear(Vector3 root, Quaternion rotation, ArenaRewardCache ignored, out string obstacle)
        {
            foreach (var chassis in s.vehicle.GetComponents<BoxCollider>())
            {
                Vector3 half = Vector3.Scale(chassis.size * .5f, s.vehicle.transform.lossyScale); half.x += .2f; half.z += .15f;
                Vector3 centre = root + rotation * Vector3.Scale(chassis.center, s.vehicle.transform.lossyScale);
                foreach (var hit in Physics.OverlapBox(centre, half, rotation, 1, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(s.vehicle.transform) || hit.transform == layout.ground.transform || hit.GetComponentInParent<CombatTarget>() ||
                        ignored && hit.transform.IsChildOf(ignored.transform)) continue;
                    // Road surfaces and terrain are below the chassis's physical bottom.
                    if (hit.bounds.max.y <= centre.y - half.y + .04f) continue;
                    obstacle = hit.name; return false;
                }
            }
            obstacle = ""; return true;
        }
        sealed class CanvasState
        {
            public Canvas canvas; public RenderMode mode; public Camera camera; public float plane;
        }

        // Same actual-camera/URP rendering path as the established arena acceptance captures.
        // World-space reward labels remain world-space; overlay HUD canvases render through the game camera.
        void Capture(string name, int width = 540, int height = 960)
        {
            var camera = s.input.view;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active; float previousAspect = camera.aspect;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, plane = c.planeDistance }).ToArray();
            string path = ArtifactFolder + "/" + name + ".png";
            try
            {
                camera.aspect = (float)width / height; camera.targetTexture = target;
                foreach (var state in canvases)
                {
                    state.canvas.renderMode = RenderMode.ScreenSpaceCamera; state.canvas.worldCamera = camera;
                    state.canvas.planeDistance = Mathf.Max(camera.nearClipPlane + .5f, 1);
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                report.Add("CAPTURE " + path + " — actual gameplay camera, world labels and HUD; " + width + " x " + height +
                    "; fixture car position=" + s.vehicle.transform.position + "; region=" + exploration.CurrentRegionId);
                Write();
            }
            finally
            {
                foreach (var state in canvases) if (state.canvas)
                {
                    state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.plane;
                }
                camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target); Destroy(texture); Canvas.ForceUpdateCanvases();
            }
        }

        IEnumerator CaptureLandscapeRegion()
        {
            try
            {
                SetCaptureResolution(960, 540);
                float deadline = Time.realtimeSinceStartup + 4;
                while ((Screen.width != 960 || Screen.height != 540) && Time.realtimeSinceStartup < deadline) yield return null;
                if (Screen.width != 960 || Screen.height != 540) throw new InvalidOperationException("Landscape Game View did not resize before the region-card capture.");
                yield return null; yield return null;
                yield return new WaitForSecondsRealtime(.15f);
                Capture("region-ash-ridge-landscape", 960, 540);
            }
            finally { SetCaptureResolution(540, 960); }
            yield return null; yield return null;
        }

        static void SetCaptureResolution(int width, int height)
        {
            // Runtime diagnostics cannot directly reference the separate editor assembly.
            var builder = Type.GetType("RoadGunner.Editor.PrototypeBuilder, Assembly-CSharp-Editor");
            var method = builder?.GetMethod("SetPortraitGameView", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("Missing editor Game View size helper.");
            method.Invoke(null, new object[] { width, height });
        }

        static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up);
        void Check(string name, bool pass, string detail)
        {
            Checks++; if (!pass) Failures++;
            string line = (pass ? "PASS " : "FAIL ") + name + ": " + detail;
            report.Add(line); Debug.Log("RG EXPLORATION " + line); Write();
        }
        void Write() { File.WriteAllLines(ReportPath, report); }
        void Restore()
        {
            if (restored) return; restored = true;
            foreach (var go in temporary) if (go) { go.SetActive(false); Destroy(go); }
            if (!s) return;
            foreach (var cache in caches) if (cache) cache.enabled = true;
            for (int i = 0; i < workshops.Length; i++) if (workshops[i]) workshops[i].enabled = workshopEnabled[i];
            foreach (var r in s.spawns.pool) if (r) r.AIEnabled = true;
            foreach (var b in s.spawns.buggyPool) if (b) { b.AIEnabled = true; FixtureBody(b).constraints = RigidbodyConstraints.None; }
            foreach (var t in s.zone1.ExtraTargets) if (t is Raider r) r.AIEnabled = true; else if (t is WastelandMutant m) m.AIEnabled = true;
            s.vehicle.Body.constraints = constraints; s.input.testInput = false; s.input.Clear();
            s.spawns.enabled = spawnsEnabled; s.turret.enabled = turretEnabled; s.Restart();
        }
        void OnDestroy() { if (running) Restore(); }
    }
}
#endif
