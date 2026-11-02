using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Pooled idle-arcade juice for every gameplay moment: glows, sparks, muzzle stars, toon smoke and sand-dust puffs, stars,
    /// rings, chunky debris, shell casings, merged floating numbers, filled telegraph zones with a '!' alert, scorch/crack decals,
    /// upgrade-station beacons, gold loot halos, speed streaks, repair pops and victory confetti that ducks behind the result card.
    /// Built and wired by ArcadeVfxInstaller. Presentation only: never touches gameplay state or timeScale, no per-frame allocation.
    /// World-space helpers (telegraphs, decals, beacons, loot, streaks, confetti) live in ArcadeVfxWorld.cs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ArcadeVfx : ArcadeFeedbackSink
    {
        [Header("Burst emitters (world space, loop with zero rate, emitted by script)")]
        public ParticleSystem glow;
        public ParticleSystem sparks, sparkles, puffs, dustPuffs, firePuffs, stars, rings, chips, casings, muzzleStars;
        [Header("Looping emitters")]
        public ParticleSystem repairPlus;
        public ParticleSystem wreckSmoke;
        [Min(0)] public float repairRate = 12, wreckSmokeRate = 6;
        [Header("Floating numbers (single combined mesh)")]
        public ArcadeVfxNumbers numbers;
        [Header("Camera trauma (FollowCamera.AddTrauma; amplitude ~ trauma^2 x 0.8 m)")]
        public float explosionTrauma = .62f;
        public float slamTrauma = .56f, damageTrauma = .2f, ramTrauma = .2f, collisionTrauma = .42f, killTrauma = .12f;

        // Palette (sRGB). Additive / HDR materials carry the intensity, so particle colours stay in 0..1.
        static readonly Color Gold = new Color(1f, .79f, .24f), GoldHot = new Color(1f, .93f, .62f), Cream = new Color(1f, .96f, .86f);
        static readonly Color Mint = new Color(.31f, .88f, .64f), Cyan = new Color(.25f, .82f, 1f);
        static readonly Color Orange = new Color(1f, .48f, .1f), Amber = new Color(1f, .7f, .24f), WarnRed = new Color(1f, .23f, .19f);
        static readonly Color Dust = new Color(.91f, .79f, .6f), DustDeep = new Color(.84f, .68f, .48f), DustLight = new Color(.97f, .88f, .72f);
        static readonly Color SmokeLit = new Color(.95f, .91f, .85f), Charcoal = new Color(.23f, .21f, .25f), Cloth = new Color(.91f, .28f, .23f);
        static readonly Color Wood = new Color(.73f, .48f, .27f), WoodDark = new Color(.55f, .34f, .2f);
        static readonly Color Steel = new Color(.33f, .37f, .44f), SteelLight = new Color(.62f, .66f, .7f), Teal = new Color(.12f, .72f, .69f);
        static readonly Color Rock = new Color(.92f, .65f, .5f), RockDark = new Color(.79f, .47f, .36f);
        static readonly Color FireYellow = new Color(1f, .79f, .24f), FireOrange = new Color(1f, .48f, .1f), Brass = new Color(1f, .8f, .4f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        struct Corpse { public CombatTarget target; public Transform visual; public Vector3 baseScale; public float start, life, size; public bool vanished; }

        GameSession session;
        VehicleController vehicle;
        FollowCamera follow;
        Transform view;
        ParticleSystem[] all;
        readonly Corpse[] corpses = new Corpse[12];
        MaterialPropertyBlock tintBlock;
        ParticleSystem.EmitParams emit;
        float clock, lastCollision = -10, healAccum, lastHealPop = -10;
        bool finale, wrecked;

        public override void Bind(ArcadeFeedback hub) { if (hub) session = hub.session; Cache(); }

        void Awake()
        {
            Cache();
            all = new[] { glow, sparks, sparkles, puffs, dustPuffs, firePuffs, stars, rings, chips, casings, muzzleStars, streaks, repairPlus, wreckSmoke };
            tintBlock = new MaterialPropertyBlock();
            AwakeWorld();
            HideAll();
        }

        void Cache()
        {
            if (!session) session = GetComponentInParent<GameSession>();
            if (!session) return;
            vehicle = session.vehicle; follow = session.follow;
            var cam = session.input ? session.input.view : null;
            view = cam ? cam.transform : follow ? follow.transform : null;
        }

        void OnDisable() { RestoreCorpses(); }

        // ---------- feedback ----------

        public override void RunRestarted()
        {
            finale = false; wrecked = false; clock = 0; lastCollision = -10; healAccum = 0; lastHealPop = -10;
            SetUnscaled(false); RestoreCorpses();
            if (all != null) foreach (var ps in all) if (ps) ps.Clear(true);
            Rate(repairPlus, 0); Rate(wreckSmoke, 0);
            HideAll();
        }

        public override void PausedChanged(bool paused) { if (paused) HideWarnings(); }

        public override void Shot(Vector3 muzzle, Vector3 end, bool hostile)
        {
            Vector3 dir = end - muzzle; float length = dir.magnitude; dir = length > .001f ? dir / length : Vector3.forward;
            Color c = hostile ? new Color(1f, .42f, .16f) : new Color(1f, .74f, .26f);
            Vector3 tip = muzzle + dir * .22f;
            // Flickering 4-point star: one big frame plus a shorter, smaller twin at another angle (reads as a 2-frame flicker).
            Emit(muzzleStars, tip, 1, c, c, R(hostile ? .75f : 1.05f, hostile ? .9f : 1.3f), R(0, 0), R(.05f, .06f));
            Emit(muzzleStars, tip + dir * .1f, 1, GoldHot, c, R(hostile ? .5f : .7f, hostile ? .6f : .85f), R(0, 0), R(.025f, .035f));
            Emit(glow, tip, 1, I(c, .7f), I(c, .8f), R(hostile ? 1.1f : 1.35f, hostile ? 1.3f : 1.6f), R(0, 0), R(.06f, .08f));
            if (hostile || !casings) return;
            Vector3 side = Vector3.Cross(Vector3.up, dir); side = side.sqrMagnitude > .001f ? side.normalized : Vector3.right;
            emit.ResetStartColor(); emit.ResetStartSize();
            emit.position = muzzle - dir * .5f + Vector3.up * .08f;
            emit.velocity = side * Random.Range(1.8f, 2.8f) + Vector3.up * Random.Range(2.6f, 3.6f) - dir * Random.Range(.2f, .7f);
            emit.rotation3D = new Vector3(Random.Range(0, 360f), Random.Range(0, 360f), Random.Range(0, 360f));
            emit.angularVelocity3D = new Vector3(Random.Range(-900f, 900f), Random.Range(-600f, 600f), Random.Range(-900f, 900f));
            emit.startLifetime = .7f; emit.startColor = Brass; emit.startSize = .5f;
            Play(casings); casings.Emit(emit, 1);
            emit.ResetPosition(); emit.ResetVelocity(); emit.ResetRotation(); emit.ResetAngularVelocity(); emit.ResetStartLifetime(); emit.ResetStartColor(); emit.ResetStartSize();
        }

        public override void Impact(Vector3 point)
        {
            Emit(sparks, point, 5, Gold, Orange, R(.12f, .17f), R(4f, 9f), R(.16f, .3f), .05f);
            Emit(glow, point, 1, I(new Color(1f, .78f, .4f), .65f), I(Cream, .6f), R(.75f, .95f), R(0, 0), R(.06f, .08f));
        }

        public override void EnemyHit(CombatTarget target, Vector3 point, float damage, bool ram, bool lethal)
        {
            // Low vehicles get their numbers lifted so the kill number never sits on top of the explosion core.
            if (numbers) numbers.Hit(target, point + (target is EnemyBuggy ? Vector3.up * 1.1f : Vector3.zero), damage, ram, lethal, clock, view);
            if (!ram) return;
            Trauma(ramTrauma);
            Emit(sparkles, point, 6, Orange, Amber, R(.4f, .7f), R(2.5f, 5f), R(.25f, .4f), .25f);
            Emit(glow, point, 1, I(Orange, .8f), I(Amber, .8f), R(1.7f, 2.1f), R(0, 0), R(.1f, .12f));
            if (target) Emit(dustPuffs, target.transform.position + Vector3.up * .3f, 4, Dust, DustLight, R(.6f, .9f), R(2f, 3.5f), R(.35f, .5f), .3f);
        }

        public override void EnemyKilled(CombatTarget target, Vector3 position, int reward)
        {
            if (target is Raider raider)
            {
                if (raider.arenaBoss) { BossBurst(position); TrackCorpse(raider, raider.visual, .6f, 2.4f); return; }
                DustPoof(position, 1, true); TrackCorpse(raider, raider.visual, .8f, 1); Trauma(killTrauma);
            }
            else if (target is WastelandMutant mutant)
            {
                DustPoof(position, 1.7f, true); Trauma(slamTrauma * .9f);
                TrackCorpse(mutant, mutant.visual, Mathf.Max(.3f, mutant.corpseLifetime), 1.7f);
            }
            else if (target is EnemyBuggy)
            {
                Emit(sparkles, position + Vector3.up * 1.2f, 4 + Mathf.Min(reward, 6), Gold, Amber, R(.6f, 1f), R(3f, 6f), R(.5f, .8f), .5f);
                Emit(stars, position + Vector3.up * 1.4f, 3, Gold, GoldHot, R(.4f, .55f), R(4f, 6f), R(.6f, .8f), .4f);
            }
            else DustPoof(position, 1, false);
        }

        public override void VehicleExploded(Vector3 position)
        {
            Trauma(explosionTrauma);
            Decal(scorches, scorchState, position, 5.2f, ScorchColor, 3.6f);
        }

        public override void PlayerDamaged(float amount, float healthFraction)
        {
            Trauma(damageTrauma + Mathf.Clamp01(amount / 25f) * .2f);
            if (!vehicle) return;
            Vector3 p = vehicle.transform.position + Vector3.up * .9f;
            Emit(sparks, p, 8, Orange, Amber, R(.1f, .15f), R(3f, 7f), R(.15f, .32f), .7f);
            Emit(glow, p, 1, I(new Color(1f, .35f, .2f), .65f), I(Orange, .65f), R(2f, 2.4f), R(0, 0), R(.08f, .1f));
        }

        public override void PlayerHealed(float amount, float healthFraction) { if (amount > 0) healAccum += amount; }

        public override void MeleeHitPlayer(Vector3 point)
        {
            Emit(sparks, point, 8, Gold, Orange, R(.1f, .15f), R(4f, 8f), R(.15f, .3f), .1f);
            Emit(chips, point, 3, Steel, SteelLight, R(.2f, .3f), R(3f, 5f), R(.4f, .6f), .1f);
        }

        public override void AttackSlam(CombatTarget attacker, Vector3 point, bool hitPlayer)
        {
            float radius = EndWarningFor(attacker, 2.2f);
            Vector3 g = point + Vector3.up * .12f;
            Emit(dustPuffs, g + Vector3.up * .2f, 12, Dust, DustLight, R(.9f, 1.4f), R(4f, 7f), R(.5f, .8f), radius * .5f);
            Emit(chips, g + Vector3.up * .3f, 9, Rock, RockDark, R(.24f, .4f), R(4f, 8f), R(.6f, .9f), .6f);
            Emit(rings, g, 1, DustLight, DustLight, R(radius * 2.6f, radius * 2.6f), R(0, 0), R(.42f, .42f));
            Emit(glow, g + Vector3.up * .3f, 1, I(Orange, .5f), I(Amber, .5f), R(radius * 1.6f, radius * 1.8f), R(0, 0), R(.1f, .12f));
            Decal(cracks, crackState, point, radius * 2.3f, CrackColor, 1.1f);
            Trauma(slamTrauma + (hitPlayer ? .12f : 0));
        }

        public override void VehicleCollision(Vector3 point, float impactSpeed)
        {
            if (clock - lastCollision < .15f) return;
            lastCollision = clock;
            float k = Mathf.Clamp01((impactSpeed - 2.5f) / 10f);
            Emit(dustPuffs, point, 3 + (int)(5 * k), Dust, DustLight, R(.5f * (1 + k), .9f * (1 + k)), R(1.5f, 3.5f), R(.4f, .6f), .3f);
            Emit(sparks, point, 2 + (int)(7 * k), Gold, Orange, R(.08f, .13f), R(3f, 7f), R(.15f, .3f), .1f);
            if (k > .35f) Emit(chips, point, (int)(6 * k), Steel, SteelLight, R(.18f, .28f), R(3f, 6f), R(.4f, .6f), .2f);
            Trauma(.06f + collisionTrauma * k);
        }

        public override void GearDropped(Vector3 position)
        {
            Emit(sparkles, position, 2, Gold, Amber, R(.5f, .75f), R(1f, 2f), R(.3f, .45f), .2f);
            Emit(glow, position, 1, I(Gold, .4f), I(Gold, .5f), R(1f, 1.3f), R(0, 0), R(.1f, .12f));
        }

        public override void ScrapCollected(Vector3 worldPosition, int amount)
        {
            Vector3 p = worldPosition + Vector3.up * .45f;
            Emit(sparkles, p, 6 + Mathf.Min(amount, 10), Gold, Amber, R(.4f, .75f), R(2.5f, 5f), R(.3f, .55f), .2f);
            Emit(glow, p, 1, I(Gold, .6f), I(GoldHot, .6f), R(1.6f, 2f), R(0, 0), R(.12f, .14f));
            Emit(rings, worldPosition - Vector3.up * .15f, 1, Gold, Gold, R(2.4f, 2.4f), R(0, 0), R(.3f, .3f));
        }

        public override void HarpoonLatched(Vector3 at)
        {
            Emit(sparkles, at, 3, Gold, Amber, R(.4f, .6f), R(1f, 2.5f), R(.25f, .35f), .1f);
            Emit(glow, at, 1, I(Cream, .5f), I(Cream, .5f), R(.9f, 1.1f), R(0, 0), R(.08f, .1f));
        }

        public override void UpgradeCollected(ArenaUpgradeKind kind, Vector3 position)
        {
            Color c = kind == ArenaUpgradeKind.Attack ? Amber : Mint;
            Vector3 up = Vector3.up;
            Emit(glow, position + up, 1, I(c, .85f), I(c, .85f), R(6f, 6.5f), R(0, 0), R(.2f, .22f));
            Emit(rings, position + up * .1f, 1, c, c, R(9f, 9f), R(0, 0), R(.5f, .5f));
            Emit(rings, position + up * .12f, 1, Cream, Cream, R(5.5f, 5.5f), R(0, 0), R(.32f, .32f));
            Emit(sparks, position + up, 22, c, c, R(.12f, .18f), R(7f, 13f), R(.3f, .5f), .3f);
            Emit(stars, position + up, 8, c, Cream, R(.5f, .7f), R(4f, 7f), R(.8f, 1.1f), .3f);
            Emit(sparkles, position + up * .3f, 16, c, c, R(.4f, .7f), R(1.6f, 3.6f), R(1f, 1.4f), 1.6f);
            Trauma(.15f);
        }

        public override void CacheHit(ArenaRewardCache cache, Vector3 point)
        {
            Emit(chips, point, 4, Wood, Steel, R(.18f, .28f), R(2.5f, 5f), R(.4f, .6f), .25f);
            Emit(dustPuffs, point, 2, Dust, DustLight, R(.45f, .7f), R(1f, 2f), R(.3f, .45f), .2f);
        }

        public override void CacheOpened(ArenaRewardCache cache, bool secret)
        {
            if (!cache) return;
            Color c = secret ? Cyan : Gold;
            Vector3 p = cache.transform.position, up = Vector3.up;
            Emit(chips, p + up * .6f, 14, Wood, WoodDark, R(.24f, .38f), R(4f, 8f), R(.6f, .9f), .4f);
            Emit(dustPuffs, p + up * .3f, 8, Dust, DustLight, R(.7f, 1.1f), R(2.5f, 4f), R(.45f, .65f), .5f);
            Emit(sparkles, p + up * .8f, 18, c, c, R(.45f, .8f), R(3f, 7f), R(.5f, .8f), .4f);
            Emit(stars, p + up, 6, c, Cream, R(.45f, .62f), R(4f, 6.5f), R(.7f, .95f), .3f);
            Emit(glow, p + up, 1, I(c, .8f), I(c, .8f), R(3.6f, 4f), R(0, 0), R(.16f, .18f));
            Emit(rings, p + up * .1f, 1, c, c, R(5.5f, 5.5f), R(0, 0), R(.4f, .4f));
            Trauma(.18f);
        }

        public override void SecretRevealed(ArenaRewardCache cache)
        {
            if (!cache) return;
            Vector3 p = cache.transform.position;
            Emit(sparkles, p + Vector3.up * .2f, 18, Cyan, Cyan, R(.4f, .75f), R(3.5f, 8f), R(.7f, 1.1f), .45f);
            Emit(glow, p + Vector3.up, 1, I(Cyan, .7f), I(Cyan, .7f), R(3.2f, 3.6f), R(0, 0), R(.2f, .22f));
            Emit(rings, p + Vector3.up * .1f, 1, Cyan, Cyan, R(4.5f, 4.5f), R(0, 0), R(.45f, .45f));
        }

        public override void RepairChanged(bool active, Vector3 position)
        {
            Rate(repairPlus, active ? repairRate : 0);
            if (active && vehicle) Emit(rings, vehicle.transform.position + Vector3.up * .1f, 1, Mint, Mint, R(5f, 5f), R(0, 0), R(.4f, .4f));
        }

        public override void Recovered(Vector3 carPosition)
        {
            ClearStreaks();
            Emit(dustPuffs, carPosition + Vector3.up * .3f, 9, DustLight, Dust, R(.8f, 1.2f), R(2.5f, 4f), R(.45f, .65f), .8f);
            Emit(sparkles, carPosition + Vector3.up * .5f, 10, Cyan, Cyan, R(.4f, .7f), R(2f, 4.5f), R(.4f, .7f), 1f);
        }

        public override void Victory(Vector3 carPosition)
        {
            finale = true; SetUnscaled(true); ClearStreaks(); HideWarnings();
            Emit(sparkles, carPosition + Vector3.up, 20, Gold, Amber, R(.5f, .9f), R(3f, 7f), R(.7f, 1.1f), 1f);
            Emit(rings, carPosition + Vector3.up * .1f, 1, Gold, Gold, R(8f, 8f), R(0, 0), R(.55f, .55f));
            StartConfetti();
        }

        public override void Defeat(Vector3 carPosition)
        {
            finale = true; wrecked = true; SetUnscaled(true); ClearStreaks(); HideWarnings();
            Vector3 p = carPosition + Vector3.up * .8f;
            Emit(firePuffs, p, 12, FireYellow, FireOrange, R(1.1f, 1.7f), R(2f, 4.5f), R(.5f, .8f), .6f);
            Emit(puffs, p, 10, SmokeLit, SmokeLit, R(1.2f, 1.8f), R(2f, 4f), R(.9f, 1.3f), .6f);
            Emit(sparks, p, 22, Gold, Orange, R(.12f, .18f), R(5f, 11f), R(.3f, .55f), .5f);
            Emit(chips, p, 10, Teal, Steel, R(.26f, .4f), R(4f, 8f), R(.7f, 1f), .5f);
            Emit(glow, p, 1, I(Orange, .9f), I(FireYellow, .9f), R(6f, 6.5f), R(0, 0), R(.22f, .25f));
            Emit(rings, carPosition + Vector3.up * .1f, 1, Orange, Orange, R(8f, 8f), R(0, 0), R(.45f, .45f));
            if (wreckSmoke) { wreckSmoke.transform.position = carPosition + Vector3.up * .9f; Rate(wreckSmoke, wreckSmokeRate); }
        }

        // ---------- composite effects ----------

        /// <summary>Sand-dust death poof (warm two-tone, never white cotton) + cloth/charcoal chunks + a gold glint.</summary>
        void DustPoof(Vector3 position, float size, bool withLoot)
        {
            Vector3 up = Vector3.up;
            Emit(dustPuffs, position + up * (.4f * size), Mathf.RoundToInt(7 * size), Dust, DustLight, R(.95f * size, 1.4f * size), R(2.4f * size, 4f * size), R(.42f, .62f), .35f * size);
            if (!withLoot) return;
            Emit(chips, position + up * (.7f * size), Mathf.RoundToInt(2 * size) + 1, Cloth, Charcoal, R(.26f * size, .4f * size), R(3.5f, 6f), R(.42f, .58f), .25f * size);
            Emit(stars, position + up * (1.1f * size), Mathf.RoundToInt(2 * size), Gold, GoldHot, R(.45f * size, .6f * size), R(3.5f, 5.5f), R(.55f, .75f), .2f);
            Emit(sparkles, position + up * (1.25f * size), 1, Gold, GoldHot, R(1.1f * size, 1.3f * size), R(0f, .4f), R(.32f, .42f), .1f);
        }

        void BossBurst(Vector3 position)
        {
            Vector3 up = Vector3.up;
            DustPoof(position, 2.4f, true);
            Emit(glow, position + up * 1.5f, 1, GoldHot, Cream, R(8f, 9f), R(0, 0), R(.22f, .28f));
            Emit(sparks, position + up * 1.5f, 30, Gold, Amber, R(.14f, .22f), R(8f, 15f), R(.4f, .7f), .6f);
            Emit(stars, position + up * 2f, 12, Gold, Cream, R(.6f, .9f), R(5f, 9f), R(.9f, 1.3f), .6f);
            Emit(rings, position + up * .1f, 1, Gold, Gold, R(11f, 11f), R(0, 0), R(.55f, .55f));
            Emit(rings, position + up * .12f, 1, Cream, Cream, R(6.5f, 6.5f), R(0, 0), R(.35f, .35f));
            Emit(chips, position + up, 12, Steel, Cloth, R(.3f, .46f), R(5f, 9f), R(.8f, 1.1f), .6f);
            Trauma(explosionTrauma);
        }

        void TrackCorpse(CombatTarget target, Transform visual, float life, float size)
        {
            if (!visual) return;
            int slot = 0; float oldest = float.MaxValue;
            for (int i = 0; i < corpses.Length; i++)
            {
                if (!corpses[i].visual || corpses[i].visual == visual) { slot = i; break; }
                if (corpses[i].start < oldest) { oldest = corpses[i].start; slot = i; }
            }
            if (corpses[slot].visual && corpses[slot].visual != visual) corpses[slot].visual.localScale = corpses[slot].baseScale;
            Vector3 baseScale = corpses[slot].visual == visual ? corpses[slot].baseScale : visual.localScale;
            corpses[slot] = new Corpse { target = target, visual = visual, baseScale = baseScale, start = clock, life = life, size = size };
        }

        // ---------- runtime ----------

        void Update()
        {
            float dt = finale ? Mathf.Min(Time.unscaledDeltaTime, .1f) : Time.deltaTime;
            clock += dt;
        }

        void LateUpdate()
        {
            AnimateWarnings();
            AnimateCorpses();
            AnimateDecals();
            if (repairPlus && vehicle) repairPlus.transform.position = vehicle.transform.position + Vector3.up * .2f;
            if (wreckSmoke && wrecked && vehicle) wreckSmoke.transform.position = vehicle.transform.position + Vector3.up * .9f;
            HealPops();
            AnimateStreaks();
            AnimateBeacons();
            AnimateLoot();
            AnimateConfetti();
            if (numbers) numbers.Tick(clock, view);
        }

        void HealPops()
        {
            if (healAccum <= 0) return;
            bool playing = session && session.Playing;
            if (!playing || !vehicle) { healAccum = 0; return; }
            if (clock - lastHealPop < .5f) return;
            if (healAccum >= 1)
            {
                // Beside and slightly below the car on screen, so it never collides with the HUD's AUTO REPAIR world label above it.
                Vector3 side = view ? Vector3.ProjectOnPlane(view.right, Vector3.up).normalized : Vector3.right;
                Vector3 back = view ? Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized : Vector3.forward;
                if (numbers) numbers.Heal(vehicle.transform.position + side * 2.3f - back * 1.5f + Vector3.up * .4f, healAccum, clock, view);
                healAccum = 0; lastHealPop = clock;
            }
        }

        void AnimateCorpses()
        {
            for (int i = 0; i < corpses.Length; i++)
            {
                var c = corpses[i];
                if (!c.visual) continue;
                float age = clock - c.start;
                // Done once the corpse is despawned or respawned (pooled raiders); a frozen finale keeps it vanished until restart.
                if ((c.target && c.target.Alive) || !c.visual.gameObject.activeInHierarchy) { c.visual.localScale = c.baseScale; corpses[i] = default; continue; }
                float squash = age < .16f ? Mathf.Sin(age / .16f * Mathf.PI) : 0;
                float shrink = Mathf.Max(.02f, 1 - Smooth((age - (c.life - .32f)) / .26f));
                Vector3 s = c.baseScale;
                s.x *= (1 + .3f * squash) * shrink; s.z *= (1 + .3f * squash) * shrink; s.y *= (1 - .24f * squash) * shrink;
                c.visual.localScale = s;
                if (!c.vanished && age >= c.life - .1f) { corpses[i].vanished = true; DustPoof(c.visual.position, c.size * .6f, false); }
            }
        }

        // ---------- helpers ----------

        static Vector2 R(float a, float b) => new Vector2(a, b);
        static Color I(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        static float EaseOut(float k) { k = 1 - Mathf.Clamp01(k); return 1 - k * k * k; }
        static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }
        static float BackOut(float k) { k = Mathf.Clamp01(k) - 1; return 1 + 2.70158f * k * k * k + 1.70158f * k * k; }
        static Renderer Slot(Renderer[] list, int i) => list != null && i < list.Length ? list[i] : null;
        static Transform Slot(Transform[] list, int i) => list != null && i < list.Length ? list[i] : null;

        void Emit(ParticleSystem ps, Vector3 at, int count, Color a, Color b, Vector2 size, Vector2 speed, Vector2 life, float radius = -1)
        {
            if (!ps || count <= 0) return;
            Play(ps);
            ps.transform.position = at;
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            if (radius >= 0) { var shape = ps.shape; shape.radius = Mathf.Max(.01f, radius); }
            ps.Emit(count);
        }

        static void Play(ParticleSystem ps) { if (ps && !ps.isPlaying) ps.Play(true); }
        static void Rate(ParticleSystem ps, float rate) { if (!ps) return; Play(ps); var emission = ps.emission; emission.rateOverTime = rate; }

        void Trauma(float amount) { if (follow && amount > 0) follow.AddTrauma(amount); }

        void Tint(Renderer r, Color c) { tintBlock.SetColor(BaseColorId, c); r.SetPropertyBlock(tintBlock); }

        static void Show(Component c, bool on) { if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

        void SetUnscaled(bool on)
        {
            if (all == null) return;
            foreach (var ps in all) { if (!ps) continue; var main = ps.main; if (main.useUnscaledTime != on) main.useUnscaledTime = on; }
        }

        void RestoreCorpses()
        {
            for (int i = 0; i < corpses.Length; i++)
            {
                if (corpses[i].visual) corpses[i].visual.localScale = corpses[i].baseScale;
                corpses[i] = default;
            }
        }

        void HideAll()
        {
            if (numbers) numbers.Clear();
            HideWarnings(); ClearStreaks(); HideDecals(); HideBeacons(); HideHalos();
            if (confettiRoot) confettiRoot.SetActive(false);
            confettiStart = -1; nextTwinkle = 0; twinkle = 0;
        }
    }
}
