using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Presentation-only receiver of gameplay moments. Audio, VFX and HUD juice override what they need.
    /// Sinks must never change gameplay state (score, health, damage, timeScale, object activity checked by tests).
    /// </summary>
    public abstract class ArcadeFeedbackSink : MonoBehaviour
    {
        public virtual void Bind(ArcadeFeedback hub) { }
        public virtual void RunRestarted() { }
        public virtual void PausedChanged(bool paused) { }
        public virtual void Defeat(Vector3 carPosition) { }
        public virtual void Victory(Vector3 bossPosition) { }
        public virtual void Recovered(Vector3 carPosition) { }
        public virtual void UiClick() { }

        public virtual void Shot(Vector3 muzzle, Vector3 end, bool hostile) { }
        public virtual void Impact(Vector3 point) { }
        public virtual void EnemyHit(CombatTarget target, Vector3 point, float damage, bool ram, bool lethal) { }
        public virtual void EnemyKilled(CombatTarget target, Vector3 position, int reward) { }
        public virtual void VehicleExploded(Vector3 position) { }
        public virtual void PlayerDamaged(float amount, float healthFraction) { }
        public virtual void PlayerHealed(float amount, float healthFraction) { }
        public virtual void MeleeHitPlayer(Vector3 point) { }
        public virtual void AttackTelegraph(CombatTarget attacker, Vector3 point, float radius, float duration) { }
        public virtual void AttackSlam(CombatTarget attacker, Vector3 point, bool hitPlayer) { }
        public virtual void VehicleCollision(Vector3 point, float impactSpeed) { }

        public virtual void GearDropped(Vector3 position) { }
        public virtual void ScrapCollected(Vector3 worldPosition, int amount) { }
        public virtual void HarpoonLaunched(Vector3 from) { }
        public virtual void HarpoonLatched(Vector3 at) { }
        public virtual void HarpoonReeled(Vector3 at) { }
        public virtual void UpgradeCollected(ArenaUpgradeKind kind, Vector3 position) { }
        public virtual void CacheHit(ArenaRewardCache cache, Vector3 point) { }
        public virtual void CacheOpened(ArenaRewardCache cache, bool secret) { }
        public virtual void SecretRevealed(ArenaRewardCache cache) { }
        public virtual void RepairChanged(bool active, Vector3 position) { }
        public virtual void RegionEntered(ArenaExplorationZone zone) { }
    }

    /// <summary>One hub on the Combat effects object. Gameplay code reaches it as session.Feedback without any per-frame search.</summary>
    [DisallowMultipleComponent]
    public sealed class ArcadeFeedback : MonoBehaviour
    {
        public GameSession session;
        public List<ArcadeFeedbackSink> sinks = new List<ArcadeFeedbackSink>();
        /// <summary>Set by the arcade audio sink once it has its clips; legacy CombatEffects one-shots then stay silent.</summary>
        [NonSerialized] public bool ownsAudio;
        VehicleHealth subscribedHealth;

        void Awake()
        {
            if (!session) session = GetComponentInParent<GameSession>();
            sinks.RemoveAll(s => !s);
            foreach (var sink in GetComponentsInChildren<ArcadeFeedbackSink>(true)) if (!sinks.Contains(sink)) sinks.Add(sink);
            foreach (var sink in sinks) Safe(() => sink.Bind(this));
        }

        void OnEnable()
        {
            if (session && session.vehicle) session.vehicle.feedback = this;
            if (session && session.health) { subscribedHealth = session.health; subscribedHealth.Damaged += OnDamaged; subscribedHealth.Healed += OnHealed; }
        }

        void OnDisable()
        {
            if (subscribedHealth) { subscribedHealth.Damaged -= OnDamaged; subscribedHealth.Healed -= OnHealed; subscribedHealth = null; }
            if (session && session.vehicle && session.vehicle.feedback == this) session.vehicle.feedback = null;
        }

        float HealthFraction => session && session.health ? session.health.Current / Mathf.Max(1, session.health.Maximum) : 1;
        void OnDamaged(float amount) => Each(static (s, a) => s.PlayerDamaged(a.amount, a.fraction), (amount, fraction: HealthFraction));
        void OnHealed(float amount) => Each(static (s, a) => s.PlayerHealed(a.amount, a.fraction), (amount, fraction: HealthFraction));

        public void RunRestarted() => Each(static (s, _) => s.RunRestarted(), 0);
        public void PausedChanged(bool paused) => Each(static (s, a) => s.PausedChanged(a), paused);
        public void Defeat(Vector3 carPosition) => Each(static (s, a) => s.Defeat(a), carPosition);
        public void Victory(Vector3 bossPosition) => Each(static (s, a) => s.Victory(a), bossPosition);
        public void Recovered(Vector3 carPosition) => Each(static (s, a) => s.Recovered(a), carPosition);
        public void UiClick() => Each(static (s, _) => s.UiClick(), 0);
        public void Shot(Vector3 muzzle, Vector3 end, bool hostile) => Each(static (s, a) => s.Shot(a.muzzle, a.end, a.hostile), (muzzle, end, hostile));
        public void Impact(Vector3 point) => Each(static (s, a) => s.Impact(a), point);
        public void EnemyHit(CombatTarget target, Vector3 point, float damage, bool ram, bool lethal) => Each(static (s, a) => s.EnemyHit(a.target, a.point, a.damage, a.ram, a.lethal), (target, point, damage, ram, lethal));
        public void EnemyKilled(CombatTarget target, Vector3 position, int reward) => Each(static (s, a) => s.EnemyKilled(a.target, a.position, a.reward), (target, position, reward));
        public void VehicleExploded(Vector3 position) => Each(static (s, a) => s.VehicleExploded(a), position);
        public void MeleeHitPlayer(Vector3 point) => Each(static (s, a) => s.MeleeHitPlayer(a), point);
        public void AttackTelegraph(CombatTarget attacker, Vector3 point, float radius, float duration) => Each(static (s, a) => s.AttackTelegraph(a.attacker, a.point, a.radius, a.duration), (attacker, point, radius, duration));
        public void AttackSlam(CombatTarget attacker, Vector3 point, bool hitPlayer) => Each(static (s, a) => s.AttackSlam(a.attacker, a.point, a.hitPlayer), (attacker, point, hitPlayer));
        public void VehicleCollision(Vector3 point, float impactSpeed) => Each(static (s, a) => s.VehicleCollision(a.point, a.impactSpeed), (point, impactSpeed));
        public void GearDropped(Vector3 position) => Each(static (s, a) => s.GearDropped(a), position);
        public void ScrapCollected(Vector3 worldPosition, int amount) => Each(static (s, a) => s.ScrapCollected(a.worldPosition, a.amount), (worldPosition, amount));
        public void HarpoonLaunched(Vector3 from) => Each(static (s, a) => s.HarpoonLaunched(a), from);
        public void HarpoonLatched(Vector3 at) => Each(static (s, a) => s.HarpoonLatched(a), at);
        public void HarpoonReeled(Vector3 at) => Each(static (s, a) => s.HarpoonReeled(a), at);
        public void UpgradeCollected(ArenaUpgradeKind kind, Vector3 position) => Each(static (s, a) => s.UpgradeCollected(a.kind, a.position), (kind, position));
        public void CacheHit(ArenaRewardCache cache, Vector3 point) => Each(static (s, a) => s.CacheHit(a.cache, a.point), (cache, point));
        public void CacheOpened(ArenaRewardCache cache, bool secret) => Each(static (s, a) => s.CacheOpened(a.cache, a.secret), (cache, secret));
        public void SecretRevealed(ArenaRewardCache cache) => Each(static (s, a) => s.SecretRevealed(a), cache);
        public void RepairChanged(bool active, Vector3 position) => Each(static (s, a) => s.RepairChanged(a.active, a.position), (active, position));
        public void RegionEntered(ArenaExplorationZone zone) => Each(static (s, a) => s.RegionEntered(a), zone);

        // Presentation must never interrupt a gameplay call path (many hooks run inside Hit/Damage).
        // Static lambdas plus a value-type argument keep the per-event dispatch allocation-free.
        void Each<T>(Action<ArcadeFeedbackSink, T> call, T args)
        {
            for (int i = 0; i < sinks.Count; i++)
            {
                var sink = sinks[i];
                if (!sink || !sink.isActiveAndEnabled) continue;
                try { call(sink, args); } catch (Exception error) { Debug.LogException(error, sink); }
            }
        }
        static void Safe(Action call) { try { call(); } catch (Exception error) { Debug.LogException(error); } }
    }
}
