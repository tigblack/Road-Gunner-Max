using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    public sealed class HudView : MonoBehaviour
    {
        public GameSession session;
        public Image healthFill;
        public Text healthText, scrapText, killsText, speedText, overlayTitle, overlayScore, debugText;
        public Text statsText, upgradeFeedbackText;
        public GameObject overlay;
        public Button pause, resume, restart, recover;
        [Header("Arena feedback and readable layout")]
        public Text scoreLabel, killsLabel, overlayDescription, overlayDetails, restartHint, overlayKicker, scoreGainText;
        public GameObject upgradeFeedbackPanel, counterPanel, statsPanel;
        public Image overlayBackdrop, overlayCard, overlayAccent;
        public RectTransform designFrame, bossPanel, resultBadge;
        public Zone1BossHud bossHud;
        public ArenaGuidance guidance;

        float nextRefresh, frameTime, feedbackUntil, scoreFlashUntil, resultStarted;
        int observedRun = -1, previousScrap;
        SessionState observedState;
        bool stateKnown;
        ArenaRunStats subscribedStats;
        ArenaExploration subscribedExploration;
        int observedRewardSerial;
        static readonly Color Cream = new Color(.96f, .94f, .86f);
        static readonly Color Mint = new Color(.47f, .85f, .68f);
        static readonly Color Gold = new Color(1, .73f, .30f);
        static readonly Color Coral = new Color(1, .49f, .39f);

        void Start()
        {
            pause.onClick.AddListener(session.TogglePause);
            resume.onClick.AddListener(session.TogglePause);
            restart.onClick.AddListener(session.Restart);
            recover.onClick.AddListener(session.Recover);
            session.Changed += Refresh;
            session.health.Changed += Refresh;
            subscribedStats = session.runStats;
            if (subscribedStats)
            {
                subscribedStats.Changed += Refresh;
                subscribedStats.UpgradeCollected += ShowUpgrade;
            }
            subscribedExploration = session.exploration;
            if (subscribedExploration)
            {
                observedRewardSerial = subscribedExploration.RewardSerial;
                subscribedExploration.Changed += ExplorationChanged;
            }
            SetUpgradeVisible(false);
            if (scoreGainText) scoreGainText.gameObject.SetActive(false);
            Refresh();
        }

        void OnDestroy()
        {
            if (session)
            {
                session.Changed -= Refresh;
                if (session.health) session.health.Changed -= Refresh;
                if (pause) pause.onClick.RemoveListener(session.TogglePause);
                if (resume) resume.onClick.RemoveListener(session.TogglePause);
                if (restart) restart.onClick.RemoveListener(session.Restart);
                if (recover) recover.onClick.RemoveListener(session.Recover);
            }
            if (subscribedStats)
            {
                subscribedStats.Changed -= Refresh;
                subscribedStats.UpgradeCollected -= ShowUpgrade;
            }
            if (subscribedExploration) subscribedExploration.Changed -= ExplorationChanged;
        }

        public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public void Refresh()
        {
            if (!session || !session.health) return;
            bool newRun = observedRun != session.RunSerial;
            if (newRun)
            {
                observedRun = session.RunSerial;
                previousScrap = session.Scrap;
                feedbackUntil = scoreFlashUntil = 0;
                observedRewardSerial = session.exploration ? session.exploration.RewardSerial : 0;
                SetUpgradeVisible(false);
                if (scoreGainText) scoreGainText.gameObject.SetActive(false);
            }
            if (session.Scrap > previousScrap && session.Playing)
            {
                if (scoreGainText)
                {
                    scoreGainText.text = "+" + (session.Scrap - previousScrap) + " SCRAP";
                    scoreGainText.gameObject.SetActive(true);
                }
                scoreFlashUntil = Time.unscaledTime + 1.6f;
            }
            previousScrap = session.Scrap;
            float hp = session.health.Current, maximum = session.health.Maximum;
            healthFill.fillAmount = hp / maximum;
            healthText.text = Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(maximum);
            scrapText.text = session.Scrap.ToString("000");
            killsText.text = session.Kills.ToString("000");
            if (scoreLabel) scoreLabel.text = "SCRAP SCORE";
            if (killsLabel) killsLabel.text = "KILLS";
            var stats = session.runStats;
            if (statsText) statsText.text = "ATK " + (stats ? stats.AttackLevel : 1) + "    HP " + (stats ? stats.HealthLevel : 1);
            overlay.SetActive(!session.Playing);
            resume.gameObject.SetActive(session.State == SessionState.Paused);
            overlayTitle.text = session.State == SessionState.Completed ? "ARENA CLEAR" : session.State == SessionState.Defeated ? "WRECKED" : "PAUSED";
            overlayScore.text = "SCRAP SCORE  " + session.Scrap + "\nKILLS  " + session.Kills;
            if (overlayKicker) overlayKicker.text = session.State == SessionState.Completed ? "WARLORD DEFEATED" : "ROAD GUNNER  /  ARENA 1";
            if (overlayDescription)
                overlayDescription.text = session.State == SessionState.Completed ? "All earned Gear is in your run score.\nThe wasteland is yours." :
                    session.State == SessionState.Defeated ? "Your run has ended.\nTry an upgrade or the repair workshop." :
                    "Defeat Warlord to clear the arena.\nMonster is an optional mini-boss.";
            if (overlayDetails)
                overlayDetails.text = "DAMAGE  " + Number(session.PlayerDamage(session.turret.tuning.bulletDamage)) + " / SHOT   ·   MAX HP " + Number(maximum) + "\n" +
                    "ATK " + (stats ? stats.AttackLevel : 1) + "   ·   HP " + (stats ? stats.HealthLevel : 1) +
                    "   ·   LOOT DMG +" + Number(stats ? stats.BonusDamagePercent : 0) + "%  RAM +" + Number(stats ? stats.BonusRamPercent : 0) + "%\n" +
                    "EXTRA HP +" + Number(stats ? stats.BonusMaxHP : 0) + "   ·   SECRETS " +
                    (session.exploration ? session.exploration.SecretsFound + " / " + session.exploration.SecretCount : "0 / 0");
            if (restartHint) restartHint.text = "NEW RUN: SCORE, KILLS AND UPGRADES RESET";
            if (overlayAccent) overlayAccent.color = session.State == SessionState.Completed ? Gold : session.State == SessionState.Defeated ? Coral : Mint;
            if (!stateKnown || observedState != session.State || newRun)
            {
                observedState = session.State; stateKnown = true; resultStarted = Time.unscaledTime;
            }
            if (resultBadge) resultBadge.gameObject.SetActive(session.State == SessionState.Completed);
            if (!session.Playing) SetUpgradeVisible(false);
            recover.interactable = session.Playing;
            pause.interactable = session.State == SessionState.Playing || session.State == SessionState.Paused;
            RefreshCombatLayout();
        }

        void SetUpgradeVisible(bool visible)
        {
            if (upgradeFeedbackPanel) upgradeFeedbackPanel.SetActive(visible);
            if (upgradeFeedbackText) upgradeFeedbackText.gameObject.SetActive(visible);
        }

        void ExplorationChanged()
        {
            if (!session || !subscribedExploration) return;
            int serial = subscribedExploration.RewardSerial;
            bool reward = observedRun == session.RunSerial && serial > observedRewardSerial;
            Refresh();
            observedRewardSerial = serial;
            if (!reward || !session.Playing || !upgradeFeedbackText) return;
            upgradeFeedbackText.text = subscribedExploration.LastReward.Replace(" · THIS RUN", "\n<size=27>THIS RUN</size>");
            upgradeFeedbackText.color = Gold;
            feedbackUntil = Time.unscaledTime + 3.2f;
            SetUpgradeVisible(true);
        }

        void ShowUpgrade(ArenaUpgradeKind kind)
        {
            if (!upgradeFeedbackText || !session || !session.runStats) return;
            var stats = session.runStats;
            if (kind == ArenaUpgradeKind.Attack)
            {
                float current = session.PlayerDamage(session.turret.tuning.bulletDamage);
                float previous = stats.tuning.ScalePlayerDamage(session.turret.tuning.bulletDamage, Mathf.Max(1, stats.AttackLevel - 1));
                previous *= 1 + stats.BonusDamagePercent / 100;
                float bonus = previous > 0 ? (current / previous - 1) * 100 : 0;
                upgradeFeedbackText.text = "DAMAGE +" + bonus.ToString("0.#", CultureInfo.InvariantCulture) + "%\n<size=27>THIS RUN</size>";
                upgradeFeedbackText.color = Gold;
            }
            else
            {
                float previous = Mathf.Max(1, session.health.tuning.vehicleHealth *
                    (1 + Mathf.Max(0, stats.HealthLevel - 2) * stats.tuning.healthLevelBonus) + stats.BonusMaxHP);
                string gain = Number(session.health.Maximum - previous);
                upgradeFeedbackText.text = "MAX HP +" + gain + " · CURRENT +" + gain + "\n<size=27>THIS RUN</size>";
                upgradeFeedbackText.color = Mint;
            }
            feedbackUntil = Time.unscaledTime + 3.2f;
            SetUpgradeVisible(true);
        }

        void RefreshCombatLayout()
        {
            bool showBoss = session && session.Playing && bossHud && bossHud.IsVisible;
            if (counterPanel) counterPanel.SetActive(!showBoss);
            if (statsPanel) statsPanel.SetActive(!showBoss);
        }

        void Update()
        {
            if (upgradeFeedbackText && upgradeFeedbackText.gameObject.activeSelf && Time.unscaledTime >= feedbackUntil)
                SetUpgradeVisible(false);
            if (scoreGainText && scoreGainText.gameObject.activeSelf && Time.unscaledTime >= scoreFlashUntil)
                scoreGainText.gameObject.SetActive(false);
            if (scrapText) scrapText.color = Time.unscaledTime < scoreFlashUntil ? Gold : Cream;
            if (resultBadge && resultBadge.gameObject.activeSelf)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - resultStarted) / .45f);
                resultBadge.localScale = Vector3.one * Mathf.Lerp(.7f, 1, 1 - Mathf.Pow(1 - t, 3));
            }
            frameTime = Mathf.Lerp(frameTime, Time.unscaledDeltaTime, .03f);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .1f;
            RefreshCombatLayout();
            speedText.text = Mathf.RoundToInt(session.vehicle.Speed * 3.6f).ToString("00") + " <size=24>KM/H</size>";
            if (debugText.gameObject.activeSelf)
                debugText.text = "WHEELS " + session.vehicle.GroundedWheels + "/4  •  " + session.spawns.ActiveCount + " HOSTILES\nTARGET " +
                    (session.turret.Target ? session.turret.Target.name : "none") + "\n" + (frameTime * 1000).ToString("F1") + " ms  •  SLIP " + session.vehicle.LargestSlip.ToString("F2");
        }
        public void ToggleDebug() { debugText.gameObject.SetActive(!debugText.gameObject.activeSelf); }
    }
}
