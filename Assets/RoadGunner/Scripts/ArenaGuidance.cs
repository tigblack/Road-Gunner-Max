using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>
    /// Run-scoped presentation only: explains the existing controls, combat, score and repair.
    /// Reads real session/input state; never moves actors or changes rewards, tuning or combat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaGuidance : MonoBehaviour
    {
        public GameSession session;
        public Text objectiveText, objectiveDetailText, hintText;
        public GameObject hintPanel, gestureRoot;
        public RectTransform gestureDot, directionArrow, objectivePanel, directionRow;
        public Text directionLabel;
        public CanvasGroup directionGroup;

        public bool HasDriven { get; private set; }
        public bool RangeHintShown { get; private set; }
        public string CurrentHint => session && session.Playing ? displayedHint : string.Empty;

        const string MainObjective = "DEFEAT WARLORD";
        const string MainDetail = "Defeat Warlord to clear Arena 1 \u2022 Monster optional";
        const string DrivingHint = "DRAG TO DRIVE\nYour gun fires automatically";
        const float BetweenHints = 1.25f;
        const float RepairCooldown = 30f;

        enum HintKind { None, Range, Score, Repair, Exploration }
        GameSession subscribedSession;
        VehicleHealth subscribedHealth;
        Raider[] humans = System.Array.Empty<Raider>();
        Raider[] warlords = System.Array.Empty<Raider>();
        ArenaUpgradeStation[] stations = System.Array.Empty<ArenaUpgradeStation>();
        RepairWorkshop[] workshops = System.Array.Empty<RepairWorkshop>();
        Camera view;
        Graphic gestureGraphic;
        Vector2 gestureOrigin;
        Color gestureColor;
        bool gestureCaptured, initialized, scoreHintShown, rangePending, scorePending, repairPending, directionReady;
        bool explorationHintShown;
        int runSerial = int.MinValue;
        float playClock, hintUntil, nextHintAt, nextRepairAt, nextDirectionAt, lastHealth;
        HintKind activeHint;
        string displayedHint = string.Empty;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Bind();
            if (session && (!initialized || runSerial != session.RunSerial)) ResetForRun();
            else RefreshVisibility();
        }

        void Start()
        {
            // GameSession.Start may execute before or after this component's Start.
            // Its completed Restart publishes Changed with a new RunSerial in either case.
            Bind();
            if (session && (!initialized || runSerial != session.RunSerial)) ResetForRun();
        }

        void OnDisable()
        {
            Unbind();
            HideTransientUI();
        }

        void OnDestroy() { Unbind(); }

        void Bind()
        {
            if (!session) session = GetComponent<GameSession>();
            if (subscribedSession != session)
            {
                Unbind();
                subscribedSession = session;
                if (subscribedSession) subscribedSession.Changed += OnSessionChanged;
            }
            BindHealth();
        }

        void BindHealth()
        {
            var health = session ? session.health : null;
            if (subscribedHealth == health) return;
            if (subscribedHealth) subscribedHealth.Changed -= OnHealthChanged;
            subscribedHealth = health;
            if (subscribedHealth) subscribedHealth.Changed += OnHealthChanged;
        }

        void Unbind()
        {
            if (subscribedSession) subscribedSession.Changed -= OnSessionChanged;
            if (subscribedHealth) subscribedHealth.Changed -= OnHealthChanged;
            subscribedSession = null;
            subscribedHealth = null;
        }

        void ResetForRun()
        {
            runSerial = session.RunSerial;
            initialized = true;
            HasDriven = RangeHintShown = scoreHintShown = false;
            explorationHintShown = false;
            rangePending = scorePending = repairPending = false;
            directionReady = false;
            playClock = hintUntil = nextHintAt = nextRepairAt = nextDirectionAt = 0;
            activeHint = HintKind.None;
            displayedHint = DrivingHint;
            lastHealth = session.health ? session.health.Current : 0;
            CacheArenaReferences();
            MakeNonInteractive(objectiveText ? objectiveText.gameObject : null);
            MakeNonInteractive(objectiveDetailText ? objectiveDetailText.gameObject : null);
            MakeNonInteractive(hintPanel);
            MakeNonInteractive(hintText ? hintText.gameObject : null);
            MakeNonInteractive(gestureRoot);
            MakeNonInteractive(directionArrow ? directionArrow.gameObject : null);
            MakeNonInteractive(directionLabel ? directionLabel.gameObject : null);
            if (directionGroup)
            {
                directionGroup.interactable = false;
                directionGroup.blocksRaycasts = false;
            }
            if (gestureDot && !gestureCaptured)
            {
                gestureOrigin = gestureDot.anchoredPosition;
                gestureGraphic = gestureDot.GetComponent<Graphic>();
                if (gestureGraphic) gestureColor = gestureGraphic.color;
                gestureCaptured = true;
            }
            SetText(objectiveText, MainObjective);
            SetText(objectiveDetailText, MainDetail);
            RefreshVisibility();
        }

        void CacheArenaReferences()
        {
            // Search once per run, including inactive authored actors/pools. Alive is read later.
            var regular = new List<Raider>();
            var bosses = new List<Raider>();
            foreach (var raider in UnityEngine.Object.FindObjectsByType<Raider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (raider.gameObject.scene != session.gameObject.scene || (raider.session && raider.session != session)) continue;
                if (raider.arenaBoss) bosses.Add(raider);
                else regular.Add(raider);
            }
            humans = regular.ToArray();
            warlords = bosses.ToArray();
            var levelStations = new List<ArenaUpgradeStation>();
            foreach (var station in UnityEngine.Object.FindObjectsByType<ArenaUpgradeStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (station.gameObject.scene == session.gameObject.scene && (!station.session || station.session == session)) levelStations.Add(station);
            stations = levelStations.ToArray();
            var levelWorkshops = new List<RepairWorkshop>();
            foreach (var workshop in UnityEngine.Object.FindObjectsByType<RepairWorkshop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (workshop.gameObject.scene == session.gameObject.scene && (!workshop.session || workshop.session == session)) levelWorkshops.Add(workshop);
            workshops = levelWorkshops.ToArray();
            view = session.input ? session.input.view : null;
            if (!view && session.spawns) view = session.spawns.view;
            if (!view && session.follow) view = session.follow.GetComponent<Camera>();
            if (!view) view = Camera.main;
        }

        void OnSessionChanged()
        {
            if (!session) return;
            BindHealth();
            if (!initialized || runSerial != session.RunSerial)
            {
                ResetForRun();
                return;
            }
            if (session.Kills > 0 || session.Scrap > 0) scorePending = !scoreHintShown;
            RefreshVisibility();
        }

        void OnHealthChanged()
        {
            if (!session || !session.health) return;
            float current = session.health.Current;
            // Restart also invokes health.Changed while the roster is being reset.
            // Wait for GameSession.Changed before recaching the completed new run.
            if (!initialized || runSerial != session.RunSerial) return;
            if (session.Playing && current > 0 && current < lastHealth - .001f && playClock >= nextRepairAt)
                repairPending = true;
            if (current >= session.health.Maximum - .01f) repairPending = false;
            lastHealth = current;
        }

        void Update()
        {
            if (!session || !initialized) return;
            if (!session.Playing)
            {
                HideTransientUI();
                return;
            }
            // This clock advances only during play, so pause/resume preserves tutorial state.
            playClock += Time.deltaTime;
            if (!HasDriven)
            {
                if (session.input && session.input.Enabled && session.input.Magnitude > .12f)
                {
                    HasDriven = true;
                    displayedHint = string.Empty;
                    nextHintAt = playClock + .4f;
                    nextDirectionAt = 0;
                }
                else
                {
                    displayedHint = DrivingHint;
                    AnimateGesture();
                    RefreshVisibility();
                    return;
                }
            }

            if (!RangeHintShown && session.turret && session.turret.Target && session.turret.Target.Alive) rangePending = true;
            if (!scoreHintShown && (session.Kills > 0 || session.Scrap > 0)) scorePending = true;
            if (activeHint != HintKind.None && playClock >= hintUntil)
            {
                activeHint = HintKind.None;
                displayedHint = string.Empty;
                nextHintAt = playClock + BetweenHints;
            }
            if (activeHint == HintKind.None && playClock >= nextHintAt) ShowNextHint();
            if (playClock >= nextDirectionAt)
            {
                nextDirectionAt = playClock + .12f;
                UpdateDirection();
            }
            RefreshVisibility();
        }

        void ShowNextHint()
        {
            if (rangePending && !RangeHintShown)
            {
                rangePending = false;
                RangeHintShown = true;
                ShowHint(HintKind.Range, "WHITE RING \u00b7 10 m gun range\nYour gun fires at targets inside the ring", 4.8f);
                return;
            }
            if (scorePending && !scoreHintShown)
            {
                scorePending = false;
                scoreHintShown = true;
                bool upgradesAvailable = false;
                foreach (var station in stations)
                    if (station && station.isActiveAndEnabled && !station.Consumed) { upgradesAvailable = true; break; }
                ShowHint(HintKind.Score, upgradesAvailable
                    ? "GEAR ADDS TO YOUR SCRAP SCORE\nYour harpoon collects drops \u00b7 Upgrade terminals are free"
                    : "GEAR ADDS TO YOUR SCRAP SCORE\nYour harpoon collects nearby drops", 5.5f);
                return;
            }
            if (!repairPending && !explorationHintShown && playClock >= 8 && session.exploration && session.exploration.isActiveAndEnabled)
            {
                explorationHintShown = true;
                ShowHint(HintKind.Exploration, "SUPPLY CACHES \u00b7 RUN BONUSES\nAuto-fire breaks them \u00b7 Side roads hide chests", 5.5f);
                return;
            }
            if (repairPending && playClock >= nextRepairAt)
            {
                repairPending = false;
                if (!session.health || session.health.Current <= 0 || session.health.Current >= session.health.Maximum - .01f || !session.vehicle) return;
                var workshop = NearestWorkshop(session.vehicle.transform.position);
                if (!workshop) return;
                nextRepairAt = playClock + RepairCooldown;
                string text;
                if (workshop.Contains(session.vehicle.transform.position)) text = "WORKSHOP REPAIR\nStay in the service area to restore health";
                else
                {
                    int distance = Mathf.RoundToInt(FlatDistance(session.vehicle.transform.position, workshop.transform.position));
                    text = "DAMAGED? WORKSHOP \u00b7 " + distance + " m\nDrive into its service area for free repairs";
                }
                ShowHint(HintKind.Repair, text, 5f);
            }
        }

        void ShowHint(HintKind kind, string text, float seconds)
        {
            activeHint = kind;
            displayedHint = text;
            hintUntil = playClock + seconds;
        }

        void UpdateDirection()
        {
            SetText(objectiveText, MainObjective);
            SetText(objectiveDetailText, session.Kills == 0
                ? "First: nearby Raiders \u2022 Then Warlord \u2022 Monster optional"
                : MainDetail);
            if (!session.vehicle)
            {
                directionReady = false;
                SetDirectionVisible(false);
                return;
            }
            Vector3 origin = session.vehicle.transform.position;
            Raider target = session.Kills == 0 ? NearestAlive(humans, origin) : null;
            bool firstFight = target != null;
            if (!target) target = NearestAlive(warlords, origin);
            if (!target || !view)
            {
                directionReady = false;
                SetDirectionVisible(false);
                return;
            }
            var delta = target.transform.position - origin;
            delta.y = 0;
            float distance = delta.magnitude;
            // Project a local ground-plane bearing, not the distant target (which can be
            // behind the perspective camera). The arrow stays in its fixed HUD card.
            Vector3 a = view.WorldToScreenPoint(origin);
            Vector3 b = view.WorldToScreenPoint(origin + delta.normalized);
            Vector2 screenDirection = new Vector2(b.x - a.x, b.y - a.y);
            if (screenDirection.sqrMagnitude > .0001f && directionArrow)
            {
                float angle = Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg - 90f;
                directionArrow.localRotation = Quaternion.Euler(0, 0, angle);
            }
            SetText(directionLabel, (firstFight ? "FIRST FIGHT" : "WARLORD") + " \u00b7 " + Mathf.RoundToInt(distance) + " m");
            directionReady = true;
            SetDirectionVisible(true);
        }

        static Raider NearestAlive(Raider[] candidates, Vector3 origin)
        {
            Raider best = null;
            float nearest = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                if (!candidate || !candidate.Alive) continue;
                var delta = candidate.transform.position - origin;
                delta.y = 0;
                if (delta.sqrMagnitude >= nearest) continue;
                nearest = delta.sqrMagnitude;
                best = candidate;
            }
            return best;
        }

        RepairWorkshop NearestWorkshop(Vector3 origin)
        {
            RepairWorkshop best = null;
            float nearest = float.PositiveInfinity;
            foreach (var workshop in workshops)
            {
                if (!workshop || !workshop.isActiveAndEnabled) continue;
                float distance = FlatDistance(origin, workshop.transform.position);
                if (distance >= nearest) continue;
                nearest = distance;
                best = workshop;
            }
            return best;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            var delta = a - b;
            delta.y = 0;
            return delta.magnitude;
        }

        void AnimateGesture()
        {
            if (!gestureDot || !gestureCaptured) return;
            float phase = Mathf.Repeat(playClock, 2f) / 2f;
            float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01(phase / .72f));
            gestureDot.anchoredPosition = gestureOrigin + new Vector2(12f * travel, 34f * travel);
            if (gestureGraphic)
            {
                var color = gestureColor;
                color.a *= phase < .72f ? 1 : 1 - Mathf.InverseLerp(.72f, 1, phase);
                gestureGraphic.color = color;
            }
        }

        void RefreshVisibility()
        {
            if (!session || !session.Playing)
            {
                HideTransientUI();
                return;
            }
            // Teach the arena rules once, then leave only the goal and bearing in combat.
            // Boss approach also compacts the card when the player has not driven yet.
            bool compact = HasDriven || (session.hud && session.hud.bossHud && session.hud.bossHud.IsVisible);
            if (objectivePanel) objectivePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, compact ? 148 : 194);
            if (directionRow) directionRow.anchoredPosition = new Vector2(20, compact ? -80 : -192);
            if (objectiveDetailText) SetActive(objectiveDetailText.gameObject, !compact);
            SetText(hintText, displayedHint);
            SetActive(hintPanel, !string.IsNullOrEmpty(displayedHint));
            if (!hintPanel && hintText) SetActive(hintText.gameObject, !string.IsNullOrEmpty(displayedHint));
            SetActive(gestureRoot, !HasDriven);
            SetDirectionVisible(HasDriven && directionReady);
        }

        void HideTransientUI()
        {
            SetActive(hintPanel, false);
            if (!hintPanel && hintText) SetActive(hintText.gameObject, false);
            SetActive(gestureRoot, false);
            SetDirectionVisible(false);
        }

        void SetDirectionVisible(bool visible)
        {
            SetActive(directionArrow ? directionArrow.gameObject : null, visible);
            SetActive(directionLabel ? directionLabel.gameObject : null, visible);
            if (directionGroup) directionGroup.alpha = visible ? 1 : 0;
        }

        static void SetText(Text label, string text)
        {
            if (label && label.text != text) label.text = text;
        }

        void SetActive(GameObject target, bool visible)
        {
            // A mistaken binding must not disable the component's own update loop.
            if (target && target != gameObject && target.activeSelf != visible) target.SetActive(visible);
        }

        static void MakeNonInteractive(GameObject root)
        {
            if (!root) return;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }
    }
}
