using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>
    /// Unscaled-time HUD juice: gears flying from the world into the score chip, chip punches, damage /
    /// low-health screen edge, HP damage trail, toast pop + shine, hint pill sizing + gesture hand, region card
    /// slide-in, result screen colours, medal bounce, defeat badge, reward count-up copy and victory burst.
    /// Never writes authoritative texts, fills or gameplay state; only decorative graphics, colours and transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArcadeHudJuice : ArcadeFeedbackSink
    {
        [Header("Bound by ArcadeHudStyleInstaller")]
        public GameSession session;
        public HudView hud;
        public Canvas canvas;
        public RectTransform flyLayer, coinTarget, heart, killsIcon, sealRays, repairIcon;
        public Image[] coins = new Image[0];
        public Image[] stars = new Image[0];
        public Image vignette, hpTrail, bossTrail, toastGlow, toastIcon, stateIcon;
        public Sprite gearSprite, starSprite, attackIcon, healthIcon, pausedIcon, defeatIcon;
        public GameObject repairChip;
        public Color damageColor = new Color(.95f, .05f, .12f, 1);
        public Color healColor = new Color(.30f, .94f, .62f, 1);
        public Color phaseColor = new Color(1, .69f, .23f, 1), phaseFlashColor = new Color(1, .96f, .55f, 1);

        [Header("Stats pills")]
        public RectTransform statsAttack;
        public RectTransform statsHealth;

        [Header("Objective card (child background hugging the visible content)")]
        public RectTransform objectiveCard;
        public float objectiveTitleOnlyHeight = 88;

        [Header("Hint card")]
        public RectTransform hintPill;
        public Image gestureHand;
        public Graphic gestureThumb;
        public float hintTextLeft = 208, hintTextMax = 784, hintPillPadding = 56, hintPillMin = 560, hintPillMax = 1016;

        [Header("Region card")]
        public RectTransform regionCard;
        public CanvasGroup regionGroup;
        public Image regionAccent;

        [Header("Result screens")]
        public Image[] backdrops = new Image[0];
        public Color pausedBackdrop = new Color(.14f, .12f, .21f, 1), victoryBackdrop = new Color(.23f, .13f, .19f, 1), defeatBackdrop = new Color(.23f, .07f, .09f, 1);
        public Color pausedTitle = new Color(.15f, .18f, .27f, 1), victoryTitle = new Color(.69f, .28f, .04f, 1), defeatTitle = new Color(.70f, .15f, .12f, 1);
        public Image defeatBadge;
        public GameObject victoryShowcase, defeatTips;
        public RectTransform showcaseRays, showcaseIcon;
        public Text showcaseCount;
        [Tooltip("Result rows that stagger in on victory/defeat (alpha only; pause shows them at once).")]
        public CanvasGroup[] resultRows = new CanvasGroup[0];
        public int[] resultRowOrder = new int[0];

        // Scale pops keep the rect centre fixed although the authored HUD uses top-left pivots.
        sealed class Pop
        {
            public RectTransform target;
            Vector3 basePosition;
            float start, duration, amount;
            bool running, appear;
            public bool Running => running;
            public void Punch(float now, float strength, float seconds) => Begin(now, strength, seconds, false);
            public void Appear(float now, float from, float seconds) => Begin(now, from, seconds, true);
            void Begin(float now, float value, float seconds, bool fromScale)
            {
                if (!target) return;
                if (!running) basePosition = target.localPosition;
                running = true; start = now; duration = Mathf.Max(.05f, seconds); amount = value; appear = fromScale;
                Tick(now);
            }
            public void Tick(float now)
            {
                if (!running) return;
                if (!target) { running = false; return; }
                float t = (now - start) / duration;
                if (t >= 1) { Stop(); return; }
                float s;
                if (appear)
                {
                    // Ease-out-back from the start scale to 1 (overshoots slightly).
                    float u = t - 1, k = 1.9f;
                    s = Mathf.LerpUnclamped(amount, 1, 1 + (k + 1) * u * u * u + k * u * u);
                }
                else s = 1 + amount * Mathf.Exp(-6.5f * t * duration / .45f) * Mathf.Cos(t * 15f);
                target.localScale = new Vector3(s, s, 1);
                target.localPosition = basePosition + (Vector3)((1 - s) * target.rect.center);
            }
            public void Offset(Vector2 delta) { if (running && target) target.localPosition += (Vector3)delta; }
            public void Stop()
            {
                if (!running) return;
                running = false;
                if (!target) return;
                target.localScale = Vector3.one; target.localPosition = basePosition;
            }
        }

        struct Flight { public Vector2 from, control; public float start, duration, spin; public bool active; public int kind; }
        struct Spark { public Vector2 position, velocity; public float start, spin; public bool active; }

        readonly Pop scrapPop = new Pop(), killsPop = new Pop(), statsPop = new Pop(), coinPop = new Pop(), heartPop = new Pop(),
            toastPop = new Pop(), gainPop = new Pop(), titlePop = new Pop(), attackPop = new Pop(), healthPop = new Pop(), regionPop = new Pop();
        Flight[] flights = new Flight[0];
        Spark[] sparks = new Spark[0];
        float flash, trailHold, bossTrailHold, lastFill = 1, lastBossFill = 1, toastStart, killsWiggle = -10;
        float overlayStart, badgeStart = -10, regionStart = -10;
        Color flashColor;
        int repairCount, countTarget, countShown = -1;
        bool toastWas, gainWas, badgeWas, pendingTitle, initialized, phaseUrgent, overlayWas, shakeTitle, captionHidden;
        Color captionColor;
        SessionState overlayState;
        string lastGainText, lastPhaseText, lastHintText;
        CombatTarget lastBoss;
        RectTransform statsTarget;
        Graphic phaseGraphic;
        Vector2 stateIconSize;
        // Private generator: presentation must not consume UnityEngine.Random draws that gameplay may rely on.
        readonly System.Random rng = new System.Random(7919);
        float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        public override void Bind(ArcadeFeedback hub) { if (!session && hub) session = hub.session; }

        void Awake() { Initialize(); }

        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (!session && hud) session = hud.session;
            flights = new Flight[coins != null ? coins.Length : 0];
            sparks = new Spark[stars != null ? stars.Length : 0];
            if (hud)
            {
                scrapPop.target = hud.scrapText ? hud.scrapText.rectTransform.parent as RectTransform : null;
                killsPop.target = hud.killsText ? hud.killsText.rectTransform.parent as RectTransform : null;
                statsTarget = hud.statsPanel ? hud.statsPanel.transform as RectTransform : null;
                statsPop.target = statsTarget;
                toastPop.target = hud.upgradeFeedbackPanel ? hud.upgradeFeedbackPanel.transform as RectTransform : null;
                gainPop.target = hud.scoreGainText ? hud.scoreGainText.rectTransform : null;
                titlePop.target = hud.overlayTitle ? hud.overlayTitle.rectTransform : null;
                phaseGraphic = hud.bossHud ? hud.bossHud.phaseText : null;
            }
            coinPop.target = coinTarget; heartPop.target = heart;
            attackPop.target = statsAttack; healthPop.target = statsHealth; regionPop.target = regionCard;
            if (stateIcon) stateIconSize = stateIcon.rectTransform.sizeDelta;
            if (!canvas && flyLayer) canvas = flyLayer.GetComponentInParent<Canvas>();
            if (canvas && !canvas.isRootCanvas) canvas = canvas.rootCanvas;
        }

        void OnEnable() { Initialize(); ResetAll(); }
        void OnDisable() { ResetAll(); }

        // ---------- feedback events ----------

        public override void RunRestarted() { ResetAll(); }

        public override void PausedChanged(bool paused)
        {
            if (!paused) return;
            ClearFlights(); StopPops(); flash = 0; ApplyVignette(0, damageColor);
        }

        public override void Defeat(Vector3 carPosition) { EndOfRun(); }
        public override void Victory(Vector3 bossPosition) { EndOfRun(); }

        public override void ScrapCollected(Vector3 worldPosition, int amount)
        {
            if (amount > 0) Launch(worldPosition, Mathf.Clamp(amount, 1, 5), 0);
        }

        public override void UpgradeCollected(ArenaUpgradeKind kind, Vector3 position)
        {
            Launch(position, 3, kind == ArenaUpgradeKind.Attack ? 1 : 2);
        }

        public override void EnemyKilled(CombatTarget target, Vector3 position, int reward)
        {
            if (!CanAnimate) return;
            float now = Time.unscaledTime;
            if (killsPop.target && killsPop.target.gameObject.activeInHierarchy) { killsPop.Punch(now, .2f, .45f); killsWiggle = now; }
        }

        public override void PlayerDamaged(float amount, float healthFraction)
        {
            if (!isActiveAndEnabled) return;
            float maximum = session && session.health ? Mathf.Max(1, session.health.Maximum) : 100;
            flash = Mathf.Clamp(.4f + amount / maximum * 2.5f, .4f, .8f); flashColor = damageColor;
            if (heart && heart.gameObject.activeInHierarchy) heartPop.Punch(Time.unscaledTime, .28f, .4f);
        }

        public override void PlayerHealed(float amount, float healthFraction)
        {
            // Workshop repair heals every frame; only a real burst (upgrade, cache) earns a green edge.
            float maximum = session && session.health ? Mathf.Max(1, session.health.Maximum) : 100;
            if (!isActiveAndEnabled || amount < maximum * .05f || flash > .12f) return;
            flash = .32f; flashColor = healColor;
        }

        public override void RepairChanged(bool active, Vector3 position)
        {
            repairCount = Mathf.Max(0, repairCount + (active ? 1 : -1));
        }

        public override void RegionEntered(ArenaExplorationZone zone)
        {
            if (regionAccent && zone != null) regionAccent.color = RegionColor(zone.id);
            if (!isActiveAndEnabled || !regionCard || !regionCard.gameObject.activeInHierarchy) return;
            regionStart = Time.unscaledTime;
            regionPop.Appear(regionStart, .86f, .38f);
        }

        static Color RegionColor(string id)
        {
            switch (id)
            {
                case "salvage-yard": return new Color(.68f, .76f, .42f, 1);
                case "rustworks": return new Color(.93f, .52f, .33f, 1);
                case "ash-ridge": return new Color(.69f, .58f, .82f, 1);
                default: return new Color(1f, .79f, .24f, 1);
            }
        }

        // ---------- per-frame presentation ----------

        void Update()
        {
            if (!hud) return;
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            bool playing = session && session.Playing;

            TickFlights(now, dt);
            TickSparks(now, dt);
            scrapPop.Tick(now); killsPop.Tick(now); statsPop.Tick(now); coinPop.Tick(now); toastPop.Tick(now); gainPop.Tick(now);
            attackPop.Tick(now); healthPop.Tick(now); regionPop.Tick(now);
            titlePop.Tick(now);
            if (shakeTitle && titlePop.Running)
            {
                float k = now - overlayStart;
                titlePop.Offset(new Vector2(Mathf.Sin(k * 55) * 14 * Mathf.Clamp01(1 - k / .36f), 0));
            }

            if (killsIcon)
            {
                float k = now - killsWiggle;
                float angle = k < .6f ? Mathf.Sin(k * 34) * Mathf.Exp(-k * 6) * 22 : 0;
                killsIcon.localRotation = Quaternion.Euler(0, 0, angle);
            }

            // Heart: damage punch, otherwise a heartbeat while health is low.
            float fraction = session && session.health ? session.health.Current / Mathf.Max(1, session.health.Maximum) : 1;
            bool low = playing && fraction > 0 && fraction < .3f;
            if (heartPop.Running) heartPop.Tick(now);
            else if (heart)
            {
                float beat = low ? 1 + .16f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(now * 7.5f)), 8) : 1;
                heart.localScale = new Vector3(beat, beat, 1);
            }

            // Screen edge: damage flash wins over the low-health pulse.
            flash = Mathf.Max(0, flash - dt * 2.3f);
            float pulse = low ? (.2f + .18f * (.5f + .5f * Mathf.Sin(now * 6.5f))) * Mathf.Lerp(1.25f, .75f, fraction / .3f) : 0;
            if (flash >= pulse) ApplyVignette(flash, flashColor); else ApplyVignette(pulse, damageColor);

            TickTrail(hpTrail, hud.healthFill, ref trailHold, ref lastFill, now, dt);
            var boss = hud.bossHud;
            if (boss && bossTrail && boss.healthFill)
            {
                if (boss.Target != lastBoss) { lastBoss = boss.Target; bossTrail.fillAmount = lastBossFill = boss.healthFill.fillAmount; }
                TickTrail(bossTrail, boss.healthFill, ref bossTrailHold, ref lastBossFill, now, dt);
            }
            TickBossPhase(now);
            TickToast(now);
            TickGain(now);
            TickObjective(dt);
            TickHint(now);
            TickRegion(now);
            TickResult(now, dt);

            if (repairChip)
            {
                bool show = playing && repairCount > 0;
                if (repairChip.activeSelf != show) repairChip.SetActive(show);
                if (show)
                {
                    float s = 1 + .05f * Mathf.Sin(now * 6);
                    repairChip.transform.localScale = new Vector3(s, s, 1);
                    if (repairIcon) repairIcon.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(now * 9) * 16);
                }
            }
        }

        void LateUpdate()
        {
            // HudView pops the seal in Update; this runs afterwards and replaces it with a 0 -> 1.2 -> 1 bounce.
            if (!hud || !hud.resultBadge || !hud.resultBadge.gameObject.activeInHierarchy) return;
            // Until Update has registered the newly shown seal, hold it at the start of the bounce (no 1-frame pop).
            float t = badgeWas ? Time.unscaledTime - badgeStart : 0;
            float s = t >= 1.2f ? 1 : 1 - Mathf.Exp(-6 * t) * (Mathf.Cos(12 * t) + .5f * Mathf.Sin(12 * t));
            hud.resultBadge.localScale = new Vector3(s, s, 1);
        }

        void TickTrail(Image trail, Image fill, ref float hold, ref float last, float now, float dt)
        {
            if (!trail || !fill) return;
            float f = fill.fillAmount, t = trail.fillAmount;
            if (f < last - .0001f) hold = now + .38f;
            last = f;
            if (f >= t) { if (t != f) trail.fillAmount = f; return; }
            if (now < hold) return;
            trail.fillAmount = Mathf.Max(f, t - dt * Mathf.Max(.4f, (t - f) * 3.5f));
        }

        void TickBossPhase(float now)
        {
            var phase = hud.bossHud ? hud.bossHud.phaseText : null;
            if (!phase || !phase.gameObject.activeInHierarchy) return;
            if (!ReferenceEquals(phase.text, lastPhaseText))
            {
                lastPhaseText = phase.text;
                phaseUrgent = lastPhaseText != null && (lastPhaseText.Contains("DODGE") || lastPhaseText.Contains("COVER") || lastPhaseText.Contains("ASIDE"));
                if (!phaseUrgent) phase.color = phaseColor;
            }
            if (phaseUrgent) phase.color = Mathf.Repeat(now * 5, 1) < .5f ? phaseFlashColor : phaseColor;
        }

        void TickToast(float now)
        {
            var panel = hud.upgradeFeedbackPanel;
            bool active = panel && panel.activeInHierarchy;
            if (active && !toastWas)
            {
                toastStart = now;
                toastPop.Appear(now, .45f, .42f);
                if (toastIcon && hud.upgradeFeedbackText)
                {
                    var c = hud.upgradeFeedbackText.color;
                    var sprite = c.r < .6f && c.g > .7f ? healthIcon : attackIcon;
                    if (sprite && toastIcon.sprite != sprite) toastIcon.sprite = sprite;
                }
            }
            else if (!active && toastWas) toastPop.Stop();
            toastWas = active;
            if (!active) return;
            float age = now - toastStart;
            if (toastIcon)
            {
                float bob = age < .5f ? 1 + .25f * Mathf.Sin(age / .5f * Mathf.PI) : 1 + .04f * Mathf.Sin(age * 6);
                toastIcon.rectTransform.localScale = new Vector3(bob, bob, 1);
                toastIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, age < .5f ? Mathf.Sin(age * 30) * 12 * (1 - age / .5f) : 0);
            }
            if (!toastGlow) return;
            float alpha = Mathf.Clamp01(age / .15f) * (.8f + .2f * Mathf.Sin(age * 5)) * Mathf.Clamp01((3.2f - age) / .45f);
            var color = toastGlow.color; color.a = alpha; toastGlow.color = color;
            float s = (age < .3f ? Mathf.Lerp(.4f, 1.08f, age / .3f) : 1 + .06f * Mathf.Sin(age * 3.6f));
            toastGlow.rectTransform.localScale = new Vector3(s, s, 1);
            toastGlow.rectTransform.localRotation = Quaternion.Euler(0, 0, -age * 40);
        }

        void TickGain(float now)
        {
            var gain = hud.scoreGainText;
            bool active = gain && gain.gameObject.activeInHierarchy;
            if (active && (!gainWas || !ReferenceEquals(gain.text, lastGainText))) gainPop.Appear(now, 1.6f, .34f);
            else if (!active && gainWas) gainPop.Stop();
            if (active != gainWas) SetCaptionHidden(active);
            gainWas = active;
            lastGainText = active ? gain.text : null;
        }

        // "+N SCRAP" pops in the scrap chip's caption slot: the caption steps aside while it shows (colour only).
        void SetCaptionHidden(bool hidden)
        {
            var caption = hud ? hud.scoreLabel : null;
            if (!caption) return;
            if (hidden && !captionHidden) { captionColor = caption.color; var c = captionColor; c.a = 0; caption.color = c; captionHidden = true; }
            else if (!hidden && captionHidden) { caption.color = captionColor; captionHidden = false; }
        }

        void TickHint(float now)
        {
            var guidance = hud.guidance;
            if (!guidance || !guidance.hintPanel || !guidance.hintPanel.activeInHierarchy) { lastHintText = null; return; }
            var text = guidance.hintText;
            if (hintPill && text && !ReferenceEquals(text.text, lastHintText))
            {
                lastHintText = text.text;
                float width = hintTextLeft + Mathf.Min(text.preferredWidth, hintTextMax) + hintPillPadding;
                var size = hintPill.sizeDelta; size.x = Mathf.Clamp(width, hintPillMin, hintPillMax);
                hintPill.sizeDelta = size;
            }
            if (gestureHand && gestureThumb)
            {
                bool show = gestureThumb.gameObject.activeInHierarchy;
                if (show)
                {
                    var c = gestureHand.color; c.a = gestureThumb.color.a; gestureHand.color = c;
                    float press = 1 + .06f * Mathf.Sin(now * 9);
                    gestureHand.rectTransform.localScale = new Vector3(press, press, 1);
                }
            }
        }

        void TickObjective(float dt)
        {
            var g = hud.guidance;
            if (!objectiveCard || !g || !g.objectivePanel) return;
            bool detail = g.objectiveDetailText && g.objectiveDetailText.gameObject.activeSelf;
            bool bearing = g.directionArrow && g.directionArrow.gameObject.activeSelf;
            float full = g.objectivePanel.rect.height;
            float target = detail || bearing ? full : Mathf.Min(full, objectiveTitleOnlyHeight);
            var size = objectiveCard.sizeDelta;
            if (Mathf.Abs(size.y - target) < .01f) return;
            size.y = Mathf.MoveTowards(size.y, target, dt * 1100);
            objectiveCard.sizeDelta = size;
        }

        void TickRegion(float now)
        {
            if (!regionGroup) return;
            bool visible = regionCard && regionCard.gameObject.activeInHierarchy;
            float k = now - regionStart;
            float a = visible && k < .3f ? Mathf.Clamp01(k / .3f) : 1;
            if (!Mathf.Approximately(regionGroup.alpha, a)) regionGroup.alpha = a;
        }

        void TickResult(float now, float dt)
        {
            bool overlay = hud.overlay && hud.overlay.activeInHierarchy;
            var state = session ? session.State : SessionState.Playing;
            if (overlay && (!overlayWas || state != overlayState)) OverlayShown(state, now);
            overlayWas = overlay; overlayState = state;
            if (pendingTitle && overlay)
            {
                pendingTitle = false;
                titlePop.Appear(now, 1.5f, .4f);
            }
            bool badge = hud.resultBadge && hud.resultBadge.gameObject.activeInHierarchy;
            if (badge && !badgeWas) { badgeStart = now; Burst(now); }
            badgeWas = badge;
            if (badge && sealRays)
            {
                sealRays.localRotation = Quaternion.Euler(0, 0, now * 30);
                float s = 1 + .05f * Mathf.Sin(now * 2.4f);
                sealRays.localScale = new Vector3(s, s, 1);
            }
            bool defeated = overlay && state == SessionState.Defeated;
            if (stateIcon)
            {
                bool show = overlay && !badge && session;
                if (show)
                {
                    float age = now - overlayStart;
                    float s = defeated ? (age < 1.2f ? 1 - Mathf.Exp(-6 * age) * (Mathf.Cos(12 * age) + .5f * Mathf.Sin(12 * age)) : 1 + .03f * Mathf.Sin(now * 3.2f))
                        : 1 + .04f * Mathf.Sin(now * 3.2f);
                    stateIcon.rectTransform.localScale = new Vector3(s, s, 1);
                    float wobble = defeated && age > .2f && age < 1f ? Mathf.Sin(age * 26) * 9 * (1 - age) : 0;
                    stateIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, wobble);
                    if (defeatBadge)
                    {
                        defeatBadge.rectTransform.localScale = new Vector3(s, s, 1);
                        defeatBadge.rectTransform.localRotation = Quaternion.Euler(0, 0, wobble * .6f);
                    }
                }
                if (stateIcon.enabled != show) stateIcon.enabled = show;
            }
            if (defeatBadge && defeatBadge.enabled != defeated) defeatBadge.enabled = defeated;
            if (overlay && resultRows != null)
            {
                bool stagger = state != SessionState.Paused;
                float age = now - overlayStart;
                for (int i = 0; i < resultRows.Length; i++)
                {
                    var row = resultRows[i];
                    if (!row) continue;
                    int order = resultRowOrder != null && i < resultRowOrder.Length ? resultRowOrder[i] : i;
                    float a = stagger ? Mathf.Clamp01((age - .14f - order * .07f) / .15f) : 1;
                    if (!Mathf.Approximately(row.alpha, a)) row.alpha = a;
                }
            }

            if (victoryShowcase && victoryShowcase.activeSelf)
            {
                float age = now - overlayStart;
                if (showcaseRays)
                {
                    showcaseRays.localRotation = Quaternion.Euler(0, 0, -now * 24);
                    float rs = Mathf.Clamp01((age - .08f) / .3f) * (1 + .05f * Mathf.Sin(now * 2.8f));
                    showcaseRays.localScale = new Vector3(rs, rs, 1);
                }
                if (showcaseIcon)
                {
                    float a = age - .12f;
                    float s = a <= 0 ? 0 : a >= 1.2f ? 1 + .03f * Mathf.Sin(now * 4) : 1 - Mathf.Exp(-6 * a) * (Mathf.Cos(12 * a) + .5f * Mathf.Sin(12 * a));
                    showcaseIcon.localScale = new Vector3(s, s, 1);
                    showcaseIcon.localRotation = Quaternion.Euler(0, 0, a > 0 && a < 1.1f ? -360 * (1 - Mathf.Pow(1 - a / 1.1f, 3)) : 0);
                }
                if (showcaseCount)
                {
                    float k = Mathf.Clamp01((age - .15f) / .33f);
                    int value = Mathf.RoundToInt(countTarget * (1 - (1 - k) * (1 - k)));
                    if (value != countShown) { countShown = value; showcaseCount.text = "+" + value; }
                }
            }
        }

        void OverlayShown(SessionState state, float now)
        {
            overlayStart = now;
            shakeTitle = state == SessionState.Defeated;
            if (hud.overlayTitle)
                hud.overlayTitle.color = state == SessionState.Completed ? victoryTitle : state == SessionState.Defeated ? defeatTitle : pausedTitle;
            var tint = state == SessionState.Completed ? victoryBackdrop : state == SessionState.Defeated ? defeatBackdrop : pausedBackdrop;
            if (backdrops != null)
                foreach (var image in backdrops)
                    if (image) { var c = tint; c.a = image.color.a; image.color = c; }
            if (victoryShowcase && victoryShowcase.activeSelf != (state == SessionState.Completed)) victoryShowcase.SetActive(state == SessionState.Completed);
            if (defeatTips && defeatTips.activeSelf != (state == SessionState.Defeated)) defeatTips.SetActive(state == SessionState.Defeated);
            countTarget = session ? Mathf.Max(0, session.Scrap) : 0;
            countShown = -1;
            if (showcaseIcon) showcaseIcon.localScale = Vector3.zero;
            if (showcaseRays) showcaseRays.localScale = Vector3.zero;
            if (stateIcon)
            {
                var sprite = state == SessionState.Defeated ? defeatIcon : pausedIcon;
                if (sprite && stateIcon.sprite != sprite) stateIcon.sprite = sprite;
                if (stateIconSize.x > 0) stateIcon.rectTransform.sizeDelta = state == SessionState.Defeated ? stateIconSize * .95f : stateIconSize;
            }
        }

        // ---------- flying gears / stars ----------

        bool CanAnimate => isActiveAndEnabled && hud && session && session.Playing;

        RectTransform FlightTarget(int kind)
        {
            if (kind == 0) return coinTarget;
            var pill = kind == 1 ? statsAttack : statsHealth;
            return pill ? pill : statsTarget;
        }

        void Launch(Vector3 world, int count, int kind)
        {
            if (!CanAnimate || !flyLayer || flights.Length == 0) return;
            var target = FlightTarget(kind);
            if (!target || !target.gameObject.activeInHierarchy) return;
            var view = session.input ? session.input.view : null;
            if (!view) return;
            Vector3 screen = view.WorldToScreenPoint(world + Vector3.up * .6f);
            if (screen.z <= 0) return;
            Camera uiCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(flyLayer, screen, uiCamera, out Vector2 local)) return;
            Vector2 end = flyLayer.InverseTransformPoint(target.TransformPoint(target.rect.center));
            float now = Time.unscaledTime;
            for (int n = 0, i = 0; n < count && i < flights.Length; i++)
            {
                if (flights[i].active || !coins[i]) continue;
                var from = local + new Vector2(Range(-30f, 30f), Range(-20f, 30f));
                var path = end - from;
                // A short sideways arc, never a hover: the gear accelerates straight into the counter icon.
                var side = new Vector2(-path.y, path.x).normalized * Range(60f, 150f) * (n % 2 == 0 ? 1 : -1);
                flights[i] = new Flight
                {
                    from = from, control = from + path * .3f + side, start = now + n * .045f,
                    duration = Range(.42f, .5f), spin = Range(-1f, 1f) * 420, active = true, kind = kind
                };
                var image = coins[i];
                var sprite = kind == 0 ? gearSprite : starSprite;
                if (sprite && image.sprite != sprite) image.sprite = sprite;
                image.rectTransform.localScale = Vector3.zero;
                image.rectTransform.anchoredPosition = from;
                if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
                n++;
            }
        }

        void TickFlights(float now, float dt)
        {
            if (flights.Length == 0 || !flyLayer) return;
            for (int i = 0; i < flights.Length; i++)
            {
                if (!flights[i].active) continue;
                var image = coins[i];
                if (!image) { flights[i].active = false; continue; }
                var f = flights[i];
                RectTransform target = FlightTarget(f.kind);
                float t = (now - f.start) / f.duration;
                if (t < 0) continue;
                if (t >= 1 || !target || !target.gameObject.activeInHierarchy)
                {
                    flights[i].active = false;
                    image.gameObject.SetActive(false);
                    if (t >= 1) Arrive(f.kind, now);
                    continue;
                }
                Vector2 end = flyLayer.InverseTransformPoint(target.TransformPoint(target.rect.center));
                float u = t * t * .7f + t * .3f, v = 1 - u;
                image.rectTransform.anchoredPosition = v * v * f.from + 2 * v * u * f.control + u * u * end;
                float s = t < .14f ? Mathf.Lerp(.2f, 1.1f, t / .14f) : Mathf.Lerp(1.1f, .6f, (t - .14f) / .86f);
                image.rectTransform.localScale = new Vector3(s, s, 1);
                image.rectTransform.localRotation = Quaternion.Euler(0, 0, f.spin * t);
            }
        }

        void Arrive(int kind, float now)
        {
            if (kind == 0) { scrapPop.Punch(now, .12f, .38f); coinPop.Punch(now, .32f, .4f); return; }
            var pop = kind == 1 ? attackPop : healthPop;
            if (pop.target) pop.Punch(now, .16f, .4f); else statsPop.Punch(now, .12f, .4f);
        }

        void Burst(float now)
        {
            if (!flyLayer || sparks.Length == 0 || !hud.resultBadge) return;
            var badge = hud.resultBadge;
            Vector2 origin = flyLayer.InverseTransformPoint(badge.TransformPoint(badge.rect.center));
            for (int i = 0; i < sparks.Length; i++)
            {
                if (!stars[i]) continue;
                // Sideways fans that leave the card centre (title, stats) clear.
                bool left = i % 2 == 0;
                float angle = (left ? Mathf.Lerp(.7f, 1.04f, Range(0f, 1f)) : Mathf.Lerp(-.04f, .3f, Range(0f, 1f))) * Mathf.PI;
                sparks[i] = new Spark
                {
                    position = origin, velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(900f, 1300f) + Vector2.up * 120,
                    start = now + Range(0f, .06f), spin = Range(-1f, 1f) * 400, active = true
                };
                stars[i].rectTransform.anchoredPosition = origin;
                stars[i].rectTransform.localScale = Vector3.zero;
                stars[i].gameObject.SetActive(true);
            }
        }

        void TickSparks(float now, float dt)
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                if (!sparks[i].active) continue;
                var image = stars[i];
                float t = (now - sparks[i].start) / .85f;
                if (!image || t >= 1) { sparks[i].active = false; if (image) image.gameObject.SetActive(false); continue; }
                if (t < 0) continue;
                sparks[i].velocity.y -= 1700 * dt;
                sparks[i].velocity *= Mathf.Exp(-2.4f * dt);
                sparks[i].position += sparks[i].velocity * dt;
                image.rectTransform.anchoredPosition = sparks[i].position;
                float s = t < .12f ? t / .12f * 1.1f : Mathf.Lerp(1.1f, .5f, (t - .12f) / .88f);
                image.rectTransform.localScale = new Vector3(s, s, 1);
                image.rectTransform.localRotation = Quaternion.Euler(0, 0, sparks[i].spin * t);
                var c = image.color; c.a = t < .65f ? 1 : 1 - (t - .65f) / .35f; image.color = c;
            }
        }

        // ---------- helpers ----------

        void ApplyVignette(float alpha, Color color)
        {
            if (!vignette) return;
            if (alpha < .004f) { if (vignette.enabled) vignette.enabled = false; return; }
            color.a = alpha;
            vignette.color = color;
            if (!vignette.enabled) vignette.enabled = true;
        }

        void ClearFlights()
        {
            for (int i = 0; i < flights.Length; i++)
            {
                flights[i].active = false;
                if (coins[i] && coins[i].gameObject.activeSelf) coins[i].gameObject.SetActive(false);
            }
        }

        void ClearSparks()
        {
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i].active = false;
                if (stars[i] && stars[i].gameObject.activeSelf) stars[i].gameObject.SetActive(false);
            }
        }

        void StopPops()
        {
            scrapPop.Stop(); killsPop.Stop(); statsPop.Stop(); coinPop.Stop(); heartPop.Stop(); toastPop.Stop(); gainPop.Stop();
            attackPop.Stop(); healthPop.Stop(); regionPop.Stop();
            if (killsIcon) killsIcon.localRotation = Quaternion.identity;
            if (heart) heart.localScale = Vector3.one;
            killsWiggle = -10;
        }

        void EndOfRun()
        {
            ClearFlights(); StopPops(); flash = 0; ApplyVignette(0, damageColor);
            repairCount = 0; pendingTitle = true;
            if (repairChip && repairChip.activeSelf) repairChip.SetActive(false);
        }

        void ResetAll()
        {
            ClearFlights(); ClearSparks(); StopPops(); titlePop.Stop();
            SetCaptionHidden(false);
            flash = 0; ApplyVignette(0, damageColor);
            repairCount = 0; pendingTitle = false; toastWas = gainWas = badgeWas = overlayWas = shakeTitle = false;
            lastGainText = lastPhaseText = lastHintText = null; phaseUrgent = false;
            if (repairChip && repairChip.activeSelf) repairChip.SetActive(false);
            if (hud)
            {
                if (hpTrail && hud.healthFill) hpTrail.fillAmount = lastFill = hud.healthFill.fillAmount;
                var boss = hud.bossHud;
                if (bossTrail && boss && boss.healthFill) { bossTrail.fillAmount = lastBossFill = boss.healthFill.fillAmount; lastBoss = boss.Target; }
                if (phaseGraphic) phaseGraphic.color = phaseColor;
                if (hud.overlayTitle) hud.overlayTitle.color = pausedTitle;
            }
            if (regionGroup) regionGroup.alpha = 1;
            if (resultRows != null) foreach (var row in resultRows) if (row) row.alpha = 1;
            if (defeatBadge) defeatBadge.enabled = false;
            if (toastGlow) { var c = toastGlow.color; c.a = 0; toastGlow.color = c; }
        }
    }
}
