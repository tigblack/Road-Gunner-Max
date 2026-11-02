using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RoadGunner
{
    /// <summary>
    /// Arcade sound design under the feedback hub: a pool of 2D voices with per-event clip banks (variants, cooldown,
    /// voice cap, priority, jitter), a two-layer engine driven by a small gear/RPM model, tyre skid, desert wind.
    /// Presentation only. Until the generated core clips exist the legacy CombatEffects one-shots keep playing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArcadeAudio : ArcadeFeedbackSink
    {
        [Serializable]
        public sealed class Bank
        {
            public AudioClip[] clips = new AudioClip[0];
            [Range(0, 1)] public float volume = .5f;
            [Range(0, 1)] public float volumeJitter = .06f;
            [Tooltip("Random pitch offset, +/- semitones.")] [Range(0, 6)] public float pitchJitter = .4f;
            [Tooltip("Minimum unscaled seconds between two starts.")] [Min(0)] public float cooldown = .05f;
            [Min(1)] public int maxVoices = 2;
            [Tooltip("Higher wins when the pool is full.")] [Range(0, 10)] public int priority = 5;
            [NonSerialized] public AudioClip fallback;
            [NonSerialized] public float nextStart;
            [NonSerialized] public int last = -1;

            public bool HasOwn { get { if (clips != null) foreach (var c in clips) if (c) return true; return false; } }
            public bool Has => HasOwn || fallback;

            public void Mix(float volume, float volumeJitter, float pitchJitter, float cooldown, int maxVoices, int priority)
            {
                this.volume = volume; this.volumeJitter = volumeJitter; this.pitchJitter = pitchJitter;
                this.cooldown = cooldown; this.maxVoices = maxVoices; this.priority = priority;
            }

            public AudioClip Pick()
            {
                int count = clips != null ? clips.Length : 0;
                if (count == 0) return fallback;
                // Random variant, never the same one twice in a row.
                int index;
                if (count == 1) index = 0;
                else if (last < 0 || last >= count) index = Random.Range(0, count);
                else { index = Random.Range(0, count - 1); if (index >= last) index++; }
                if (!clips[index]) { for (int i = 0; i < count; i++) if (clips[i]) { index = i; break; } }
                last = index;
                return clips[index] ? clips[index] : fallback;
            }
        }

        struct Voice { public Bank bank; public float start, end, fade; public int priority, frame; }

        public ArcadeFeedback hub;
        public GameSession session;
        [Header("Voice pool (2D, children of this object)")]
        public AudioSource[] voices = new AudioSource[0];
        [Range(0, 1)] public float masterVolume = 1;
        [Range(0, 1)] public float panAmount = .45f;

        [Header("One-shot banks")]
        public Bank playerGun = new Bank(), enemyGun = new Bank(), bulletImpact = new Bank(), metalHit = new Bank(), bodyHit = new Bank();
        public Bank explosion = new Bank(), crash = new Bank(), collision = new Bank(), crateBreak = new Bank(), crateHit = new Bank();
        public Bank scrap = new Bank(), gearDrop = new Bank(), killChime = new Bank(), upgrade = new Bank(), uiClick = new Bank();
        public Bank bossWarning = new Bank(), victory = new Bank(), defeat = new Bank(), harpoonFire = new Bank(), harpoonReel = new Bank(), whoosh = new Bank();

        [Header("Loops (clips live on the sources)")]
        public AudioSource engineIdle;
        public AudioSource engineDrive;
        public AudioSource skid;
        public AudioSource wind;
        [Range(0, 1)] public float engineVolume = .22f;
        [Range(0, 1)] public float skidVolume = .3f;
        [Range(0, 1)] public float windVolume = .17f;
        [Tooltip("Idle layer pitch from idle RPM to its fade-out RPM.")] public Vector2 idlePitch = new Vector2(.94f, 1.38f);
        [Tooltip("Drive layer pitch from idle RPM to the top of a gear. Tune to the recording's native RPM.")] public Vector2 drivePitch = new Vector2(.74f, 1.42f);

        public bool OwnsCoreAudio { get; private set; }
        public bool DrivesEngine { get; private set; }
        public int VoicesStarted { get; private set; }
        public int VoicesRejected { get; private set; }
        public int HighestGear => topGear + 1;

        // Three forward gears as fractions of tuning.maxSpeed: the RPM rises through a gear and dips on each upshift.
        static readonly float[] GearBase = { 0f, .24f, .52f }, GearTop = { .40f, .74f, 1.10f };
        static readonly float[] UpShift = { .36f, .68f, 99f }, DownShift = { -1f, .27f, .56f };
        static readonly float[] ShotPattern = { 0f, -.25f, .15f, -.1f, .3f };

        Voice[] state = new Voice[0];
        Camera view;
        bool paused, ended, started;
        int gear, topGear, shotIndex, scrapStep, impactSuppressedFrame = -1, reelVoice = -1;
        float rpm = .12f, load, blend, shiftTimer, skidAmount, intensity, lastScrap = -10, airborneSince = -1;
        float idleLevel, driveLevel, skidLevel, windLevel;
#if UNITY_EDITOR
        float nextTrace;
#endif

        bool Owns => hub && hub.ownsAudio && OwnsCoreAudio;
        VehicleController Vehicle => session ? session.vehicle : null;
        static float Semitones(float s) => Mathf.Pow(2, s / 12f);

        /// <summary>Default mix. Clip files are loudness-normalised per category, these scale them against music 0.3.</summary>
        public void ResetMix()
        {
            playerGun.Mix(.40f, .06f, .35f, .05f, 3, 6);
            enemyGun.Mix(.30f, .08f, .5f, .07f, 3, 4);
            bulletImpact.Mix(.24f, .1f, .8f, .06f, 2, 2);
            metalHit.Mix(.36f, .08f, .6f, .08f, 2, 5);
            bodyHit.Mix(.40f, .08f, .7f, .07f, 2, 4);
            explosion.Mix(.62f, .05f, .6f, .05f, 2, 9);
            crash.Mix(.50f, .06f, .5f, .10f, 2, 7);
            collision.Mix(.45f, .06f, .6f, .22f, 1, 5);
            crateBreak.Mix(.50f, .05f, .4f, .08f, 2, 6);
            crateHit.Mix(.32f, .1f, .9f, .07f, 2, 3);
            scrap.Mix(.42f, 0, 0, .045f, 3, 5);
            gearDrop.Mix(.20f, .1f, .5f, .08f, 2, 2);
            killChime.Mix(.20f, .05f, 0, .06f, 2, 4);
            upgrade.Mix(.60f, 0, 0, .3f, 1, 8);
            uiClick.Mix(.50f, .05f, .3f, .05f, 2, 9);
            bossWarning.Mix(.55f, 0, 0, .5f, 1, 8);
            victory.Mix(.75f, 0, 0, 2f, 1, 10);
            defeat.Mix(.70f, 0, 0, 2f, 1, 10);
            harpoonFire.Mix(.42f, .06f, .5f, .15f, 1, 5);
            harpoonReel.Mix(.32f, .04f, .3f, .2f, 1, 4);
            whoosh.Mix(.32f, .06f, .6f, .35f, 1, 4);
            masterVolume = 1; panAmount = .45f;
            engineVolume = .22f; skidVolume = .3f; windVolume = .17f;
            idlePitch = new Vector2(.94f, 1.38f); drivePitch = new Vector2(.74f, 1.42f);
        }

        void Reset() { ResetMix(); }

        public override void Bind(ArcadeFeedback feedbackHub) { if (feedbackHub) hub = feedbackHub; }

        void Awake()
        {
            if (!hub) hub = GetComponentInParent<ArcadeFeedback>();
            if (!session) session = hub && hub.session ? hub.session : GetComponentInParent<GameSession>();
            if (voices == null || voices.Length == 0) voices = CreateVoices(14);
            state = new Voice[voices.Length];
            foreach (var source in new[] { engineIdle, engineDrive, skid, wind }) PrepareLoop(source);
        }

        void OnEnable() { RefreshOwnership(); }

        void Start()
        {
            started = true;
            var effects = session ? session.effects : null;
            if (effects)
            {
                // Legacy clips keep every previously audible event audible if a new bank is missing.
                playerGun.fallback = enemyGun.fallback = effects.gun;
                bulletImpact.fallback = effects.impact;
                explosion.fallback = effects.explosion;
                scrap.fallback = upgrade.fallback = crateBreak.fallback = effects.pickup;
            }
            view = session && session.input && session.input.view ? session.input.view : Camera.main;
            RefreshOwnership();
#if UNITY_EDITOR
            Debug.Log("ARCADE AUDIO active: core clips=" + OwnsCoreAudio + " (legacy one-shots " + (OwnsCoreAudio ? "silenced" : "kept") +
                      "), layered engine=" + DrivesEngine + ", skid=" + (skid && skid.clip) + ", wind=" + (wind && wind.clip) + ", voices=" + voices.Length);
            ArcadeAudioRecorder.AttachIfRequested();
#endif
            paused = session && session.State == SessionState.Paused;
            ended = session && (session.State == SessionState.Defeated || session.State == SessionState.Completed);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            if (started) Debug.Log("ARCADE AUDIO stats: core=" + OwnsCoreAudio + " engine=" + DrivesEngine + " voices started=" + VoicesStarted +
                                   " rejected=" + VoicesRejected + " highest gear=" + HighestGear);
#endif
            if (hub && OwnsCoreAudio) hub.ownsAudio = false;
            var vehicle = Vehicle;
            if (vehicle && DrivesEngine) vehicle.externalEngineAudio = false;
            OwnsCoreAudio = DrivesEngine = false;
            foreach (var source in new[] { engineIdle, engineDrive, skid, wind }) if (source) source.Stop();
            idleLevel = driveLevel = skidLevel = windLevel = 0;
            StopVoices();
        }

        void RefreshOwnership()
        {
            OwnsCoreAudio = playerGun.HasOwn && bulletImpact.HasOwn && explosion.HasOwn && scrap.HasOwn;
            if (hub) hub.ownsAudio = OwnsCoreAudio && isActiveAndEnabled;
            var vehicle = Vehicle;
            DrivesEngine = isActiveAndEnabled && engineIdle && engineIdle.clip && vehicle;
            if (vehicle) vehicle.externalEngineAudio = DrivesEngine;
        }

        AudioSource[] CreateVoices(int count)
        {
            var created = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Voice " + (i + 1).ToString("00"));
                go.transform.SetParent(transform, false);
                created[i] = go.AddComponent<AudioSource>();
                ConfigureVoice(created[i]);
            }
            return created;
        }

        public static void ConfigureVoice(AudioSource source)
        {
            if (!source) return;
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0; source.dopplerLevel = 0;
            source.volume = 1; source.pitch = 1; source.panStereo = 0; source.priority = 128; source.outputAudioMixerGroup = null;
        }

        public static void ConfigureLoop(AudioSource source, int priority)
        {
            if (!source) return;
            source.playOnAwake = false; source.loop = true; source.spatialBlend = 0; source.dopplerLevel = 0;
            source.volume = 0; source.pitch = 1; source.panStereo = 0; source.priority = priority; source.outputAudioMixerGroup = null;
        }

        static void PrepareLoop(AudioSource source)
        {
            if (!source) return;
            source.loop = true; source.playOnAwake = false; source.volume = 0;
        }

        // ------------------------------------------------------------------ voices

        int Allocate(Bank bank, float now)
        {
            int same = 0, oldestSame = -1, free = -1, victim = -1, victimPriority = int.MaxValue;
            float oldestStart = float.MaxValue, victimStart = float.MaxValue;
            for (int i = 0; i < voices.Length; i++)
            {
                var source = voices[i];
                if (!source) continue;
                bool busy = now < state[i].end && (source.isPlaying || now < state[i].start);
                if (!busy) { if (free < 0) free = i; continue; }
                if (state[i].bank == bank) { same++; if (state[i].start < oldestStart) { oldestStart = state[i].start; oldestSame = i; } }
                if (state[i].priority < victimPriority || state[i].priority == victimPriority && state[i].start < victimStart)
                { victim = i; victimPriority = state[i].priority; victimStart = state[i].start; }
            }
            if (same >= bank.maxVoices) return oldestSame;
            if (free >= 0) return free;
            return victim >= 0 && victimPriority <= bank.priority ? victim : -1;
        }

        int Play(Bank bank, float volume, float pitch, float pan, float delay = 0)
        {
            if (bank == null || voices == null || voices.Length == 0 || state.Length != voices.Length) return -1;
            float now = Time.unscaledTime;
            if (now < bank.nextStart) return -1;
            var clip = bank.Pick();
            if (!clip) return -1;
            int index = Allocate(bank, now);
            if (index < 0) { VoicesRejected++; return -1; }
            bank.nextStart = now + bank.cooldown; VoicesStarted++;
            float finalPitch = Mathf.Clamp(pitch * Semitones(Random.Range(-bank.pitchJitter, bank.pitchJitter)), .25f, 3f);
            float finalVolume = Mathf.Clamp01(bank.volume * volume * masterVolume * (1 + Random.Range(-bank.volumeJitter, bank.volumeJitter)));
            var source = voices[index];
            source.Stop();
            source.clip = clip; source.loop = false; source.pitch = finalPitch; source.volume = finalVolume;
            source.panStereo = Mathf.Clamp(pan, -1, 1);
            if (delay > 0) source.PlayDelayed(delay); else source.Play();
            state[index] = new Voice { bank = bank, start = now + delay, end = now + delay + clip.length / finalPitch + .05f, priority = bank.priority, frame = Time.frameCount };
            return index;
        }

        int Play2D(Bank bank, float volume = 1, float pitch = 1, float delay = 0) => Play(bank, volume, pitch, 0, delay);

        int PlayAt(Bank bank, Vector3 world, float volume = 1, float pitch = 1, float delay = 0)
        {
            float attenuation = 1;
            var vehicle = Vehicle;
            if (vehicle)
            {
                Vector3 offset = world - vehicle.transform.position; offset.y = 0;
                attenuation = Mathf.Lerp(1, .35f, Mathf.InverseLerp(9, 30, offset.magnitude));
            }
            return Play(bank, volume * attenuation, pitch, Pan(world), delay);
        }

        float Pan(Vector3 world)
        {
            if (!view) return 0;
            Vector3 viewport = view.WorldToViewportPoint(world);
            return viewport.z <= 0 ? 0 : Mathf.Clamp((viewport.x - .5f) * 2, -1, 1) * panAmount;
        }

        void FadeVoice(int index, float seconds)
        {
            if (index < 0 || index >= state.Length || !voices[index] || !voices[index].isPlaying) return;
            state[index].fade = voices[index].volume / Mathf.Max(.02f, seconds);
        }

        void StopVoices(bool keepThisFrame = false)
        {
            if (voices == null) return;
            for (int i = 0; i < voices.Length; i++)
            {
                if (keepThisFrame && i < state.Length && state[i].bank != null && state[i].frame == Time.frameCount) continue;
                if (voices[i]) voices[i].Stop();
                if (i < state.Length) state[i] = default;
            }
            reelVoice = -1;
        }

        // ------------------------------------------------------------------ loops

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i].fade <= 0 || !voices[i]) continue;
                voices[i].volume = Mathf.Max(0, voices[i].volume - state[i].fade * dt);
                if (voices[i].volume <= 0) { voices[i].Stop(); state[i] = default; }
            }
            if (!started) return;
            bool playing = session && session.State == SessionState.Playing;
            if (playing) intensity = Mathf.Max(0, intensity - dt * .45f);
            var vehicle = Vehicle;
            if (DrivesEngine && vehicle && vehicle.engine) vehicle.engine.volume = 0;
            UpdateEngine(vehicle, playing, dt);
            UpdateSkid(vehicle, playing, dt);
            UpdateWind(vehicle, dt);
        }

        void Drive(AudioSource source, ref float level, float target, float rate, float dt, float pitch)
        {
            if (!source || !source.clip) return;
            level = Mathf.MoveTowards(level, target, rate * dt);
            if (level <= .0005f && target <= .0005f)
            {
                level = 0; source.volume = 0;
                if (source.isPlaying) source.Pause();
                return;
            }
            if (!source.isPlaying) { source.UnPause(); if (!source.isPlaying) source.Play(); }
            source.volume = Mathf.Clamp01(level * masterVolume);
            source.pitch = Mathf.Clamp(pitch, .3f, 3f);
        }

        void UpdateEngine(VehicleController vehicle, bool playing, float dt)
        {
            if (!DrivesEngine) return;
            float speed01 = 0, throttle = 0; bool grounded = true, canDrive = false;
            if (vehicle)
            {
                float top = vehicle.tuning ? Mathf.Max(1, vehicle.tuning.maxSpeed) : 11.7f;
                speed01 = Mathf.Clamp(Mathf.Max(Mathf.Abs(vehicle.ForwardSpeed), vehicle.HorizontalSpeed * .8f) / top, 0, 1.15f);
                throttle = Mathf.Clamp01(vehicle.Throttle); grounded = vehicle.GroundedWheels > 0; canDrive = vehicle.CanDrive;
            }
            bool running = playing && canDrive;
            load = Mathf.MoveTowards(load, running ? throttle : 0, dt * (throttle > load ? 6 : 3));
            if (gear < 2 && speed01 > UpShift[gear] && load > .15f) { gear++; shiftTimer = .18f; topGear = Mathf.Max(topGear, gear); }
            else if (gear > 0 && speed01 < DownShift[gear]) gear--;
            shiftTimer = Mathf.Max(0, shiftTimer - dt);

            float target = Mathf.Lerp(.16f, 1f, Mathf.InverseLerp(GearBase[gear], GearTop[gear], speed01));
            if (gear == 0) target = Mathf.Max(target, .16f + load * .42f * (1 - Mathf.InverseLerp(.1f, .3f, speed01)));   // clutch slip on launch
            if (!grounded) target = Mathf.Max(target, .25f + load * .7f);                                                  // free-spinning wheels
            if (shiftTimer > 0) target -= .06f;
            if (!running) target = .12f;
            rpm = Mathf.MoveTowards(rpm, target, dt * (target > rpm ? 2.2f : 3.2f));

            float blendTarget = Mathf.Clamp01(Mathf.InverseLerp(.14f, .42f, rpm) * .75f + load * .35f);
            blend = Mathf.MoveTowards(blend, blendTarget, dt * 4);
            float rate = paused ? 6 : ended ? .45f : 2.5f;
            float on = running ? engineVolume : 0;
            if (engineDrive && engineDrive.clip)
            {
                float dip = shiftTimer > 0 ? .72f : 1;
                Drive(engineIdle, ref idleLevel, on * .9f * Mathf.Lerp(1, .22f, blend), rate, dt, Mathf.Lerp(idlePitch.x, idlePitch.y, Mathf.InverseLerp(.12f, .55f, rpm)));
                Drive(engineDrive, ref driveLevel, on * blend * Mathf.Lerp(.78f, 1.05f, load) * dip, rate, dt, Mathf.Lerp(drivePitch.x, drivePitch.y, rpm));
            }
            else Drive(engineIdle, ref idleLevel, on * Mathf.Lerp(.8f, 1.1f, rpm), rate, dt, Mathf.Lerp(.9f, 1.9f, rpm));
#if UNITY_EDITOR
            if (ArcadeAudioRecorder.Tracing && running && Time.unscaledTime >= nextTrace)
            {
                nextTrace = Time.unscaledTime + .25f;
                Debug.Log("ARCADE AUDIO engine t=" + Time.unscaledTime.ToString("F2") + " speed01=" + speed01.ToString("F2") + " throttle=" + load.ToString("F2") +
                          " gear=" + (gear + 1) + " rpm=" + rpm.ToString("F2") + " idle=" + idleLevel.ToString("F3") + "@" + (engineIdle ? engineIdle.pitch : 0).ToString("F2") +
                          " drive=" + driveLevel.ToString("F3") + "@" + (engineDrive ? engineDrive.pitch : 0).ToString("F2") + " skid=" + skidLevel.ToString("F3") +
                          " wind=" + windLevel.ToString("F3"));
            }
#endif
        }

        void UpdateSkid(VehicleController vehicle, bool playing, float dt)
        {
            if (!skid || !skid.clip) return;
            float amount = 0;
            if (vehicle && playing && vehicle.GroundedWheels > 0)
            {
                float speed = vehicle.HorizontalSpeed, forward = Mathf.Abs(vehicle.ForwardSpeed);
                float slip = Mathf.InverseLerp(.24f, .5f, vehicle.LargestSlip) * Mathf.InverseLerp(1.5f, 4.5f, speed);
                float service = vehicle.tuning ? vehicle.tuning.serviceBrakeTorque : 1100;
                float brake = vehicle.AppliedBrakeTorque >= service * .6f && forward > 3.5f ? Mathf.InverseLerp(3.5f, 9, forward) * .75f : 0;
                amount = Mathf.Max(slip, brake);
            }
            skidAmount = Mathf.MoveTowards(skidAmount, amount, dt * (amount > skidAmount ? 10 : 4));
            Drive(skid, ref skidLevel, skidVolume * skidAmount, 50, dt, Mathf.Lerp(.9f, 1.12f, skidAmount));

            // Landing thump after a real jump.
            if (vehicle && playing)
            {
                if (vehicle.GroundedWheels == 0) { if (airborneSince < 0) airborneSince = Time.unscaledTime; }
                else
                {
                    if (airborneSince >= 0 && vehicle.GroundedWheels >= 2 && Time.unscaledTime - airborneSince > .3f)
                        PlayAt(collision, vehicle.transform.position, .45f, .72f);
                    airborneSince = -1;
                }
            }
            else airborneSince = -1;
        }

        void UpdateWind(VehicleController vehicle, float dt)
        {
            if (!wind || !wind.clip) return;
            float speed01 = 0;
            if (vehicle) speed01 = Mathf.Clamp01(vehicle.HorizontalSpeed / (vehicle.tuning ? Mathf.Max(1, vehicle.tuning.maxSpeed) : 11.7f));
            float scale = paused ? 0 : ended ? .55f : 1;
            float target = windVolume * scale * (1 - .45f * Mathf.Clamp01(intensity)) * (1 + .35f * speed01);
            Drive(wind, ref windLevel, target, paused ? 4 : .35f, dt, 1 + .04f * speed01);
        }

        // ------------------------------------------------------------------ session

        public override void RunRestarted()
        {
            StopVoices(true);   // Restart announces the start region in the same frame; keep that cue
            paused = ended = false;
            gear = 0; rpm = .12f; load = blend = shiftTimer = skidAmount = intensity = 0; scrapStep = 0; airborneSince = -1;
            RefreshOwnership();
        }

        public override void PausedChanged(bool isPaused) { paused = isPaused; }

        public override void Defeat(Vector3 carPosition)
        {
            ended = true;
            Play2D(crash, 1, .8f);
            Play2D(defeat, 1, 1, .25f);
        }

        public override void Victory(Vector3 bossPosition)
        {
            ended = true;
            Play2D(victory, 1, 1, .12f);
        }

        public override void Recovered(Vector3 carPosition) { if (Play2D(whoosh, .9f) < 0) Play2D(uiClick, .5f, .85f); }

        public override void UiClick() { Play2D(uiClick); }

        // ------------------------------------------------------------------ combat

        public override void Shot(Vector3 muzzle, Vector3 end, bool hostile)
        {
            if (!Owns) return;
            intensity = Mathf.Min(1, intensity + (hostile ? .03f : .05f));
            if (hostile) PlayAt(enemyGun, muzzle);
            else PlayAt(playerGun, muzzle, 1, Semitones(ShotPattern[shotIndex++ % ShotPattern.Length]));
        }

        public override void Impact(Vector3 point)
        {
            if (!Owns || Time.frameCount == impactSuppressedFrame) return;
            PlayAt(bulletImpact, point);
        }

        public override void EnemyHit(CombatTarget target, Vector3 point, float damage, bool ram, bool lethal)
        {
            if (ram)
            {
                if (PlayAt(crash, point, Mathf.Lerp(.55f, 1, Mathf.InverseLerp(18, 48, damage)), Random.Range(.92f, 1.02f)) >= 0)
                    impactSuppressedFrame = Time.frameCount;
                return;
            }
            if (lethal) return;  // EnemyKilled voices the final blow
            int played = -1;
            if (target is EnemyBuggy || target is TruckArenaBoss) { if (Owns && metalHit.HasOwn) played = PlayAt(metalHit, point, .8f, .86f); }
            else if (target is WastelandMutant) played = PlayAt(bodyHit, point, 1.1f, .8f);
            else if (target is Raider raider) played = PlayAt(bodyHit, point, raider.arenaBoss ? 1.1f : 1, raider.arenaBoss ? .85f : 1);
            // The turret sparks the same point right after Hit: one sound per bullet.
            if (played >= 0) impactSuppressedFrame = Time.frameCount;
        }

        public override void EnemyKilled(CombatTarget target, Vector3 position, int reward)
        {
            if (target is Raider raider)
            {
                if (raider.arenaBoss) { PlayAt(bodyHit, position, 1.25f, .72f); PlayAt(crash, position, .8f, .7f); }
                else PlayAt(bodyHit, position, 1.15f, .9f);
                PlayAt(killChime, position, raider.arenaBoss ? 1.2f : 1, Semitones(7), .07f);
            }
            else if (target is WastelandMutant)
            {
                PlayAt(bodyHit, position, 1.3f, .66f);
                PlayAt(crash, position, .85f, .62f);
                PlayAt(killChime, position, 1.2f, Semitones(5), .09f);
            }
            else if (target is EnemyBuggy || target is TruckArenaBoss) PlayAt(killChime, position, .8f, Semitones(7), .18f);
        }

        public override void VehicleExploded(Vector3 position)
        {
            intensity = Mathf.Min(1, intensity + .4f);
            if (Owns) PlayAt(explosion, position);
        }

        public override void PlayerDamaged(float amount, float healthFraction)
        {
            if (!metalHit.HasOwn) return;
            float weight = Mathf.Clamp01(amount / 16f);
            if (Play2D(metalHit, Mathf.Lerp(.55f, 1, weight), Mathf.Lerp(.8f, .7f, weight)) >= 0) impactSuppressedFrame = Time.frameCount;
        }

        public override void MeleeHitPlayer(Vector3 point) { PlayAt(crash, point, .6f, 1.12f); }

        public override void AttackTelegraph(CombatTarget attacker, Vector3 point, float radius, float duration)
        {
            intensity = Mathf.Min(1, intensity + .25f);
            PlayAt(bossWarning, point, 1, attacker is WastelandMutant ? .94f : 1);
        }

        public override void AttackSlam(CombatTarget attacker, Vector3 point, bool hitPlayer)
        {
            intensity = Mathf.Min(1, intensity + .2f);
            PlayAt(crash, point, hitPlayer ? 1 : .8f, .68f);
        }

        public override void VehicleCollision(Vector3 point, float impactSpeed)
        {
            float weight = Mathf.InverseLerp(2.5f, 12, impactSpeed);
            PlayAt(collision, point, Mathf.Lerp(.25f, 1, weight), Mathf.Lerp(1.12f, .9f, weight));
        }

        // ------------------------------------------------------------------ loot and progression

        public override void GearDropped(Vector3 position) { PlayAt(gearDrop, position, 1, .7f); }

        public override void ScrapCollected(Vector3 worldPosition, int amount)
        {
            if (!Owns) return;
            float now = Time.unscaledTime;
            scrapStep = now - lastScrap <= 1.2f ? Mathf.Min(scrapStep + 1, 6) : 0;
            lastScrap = now;
            Play2D(scrap, amount > 1 ? 1.08f : 1, Semitones(scrapStep));
        }

        public override void HarpoonLaunched(Vector3 from) { PlayAt(harpoonFire, from); }

        public override void HarpoonLatched(Vector3 at)
        {
            if (metalHit.HasOwn) PlayAt(metalHit, at, .35f, 1.3f);
            reelVoice = PlayAt(harpoonReel, at);
        }

        public override void HarpoonReeled(Vector3 at)
        {
            if (reelVoice >= 0 && reelVoice < state.Length && state[reelVoice].bank == harpoonReel) FadeVoice(reelVoice, .12f);
            reelVoice = -1;
        }

        public override void UpgradeCollected(ArenaUpgradeKind kind, Vector3 position)
        {
            if (Owns) Play2D(upgrade, 1, kind == ArenaUpgradeKind.Health ? .94f : 1);
        }

        public override void CacheHit(ArenaRewardCache cache, Vector3 point)
        {
            if (PlayAt(crateHit, point) >= 0) impactSuppressedFrame = Time.frameCount;
        }

        public override void CacheOpened(ArenaRewardCache cache, bool secret)
        {
            if (!Owns) return;
            Vector3 at = cache ? cache.transform.position : Vector3.zero;
            if (cache) PlayAt(crateBreak, at); else Play2D(crateBreak);
            Play2D(killChime, 1.4f, Semitones(secret ? 12 : 7), .09f);
        }

        public override void SecretRevealed(ArenaRewardCache cache)
        {
            Play2D(whoosh, .7f, 1.15f);
            Play2D(killChime, 1.3f, Semitones(12), .05f);
        }

        public override void RepairChanged(bool active, Vector3 position) { if (active) Play2D(killChime, 1.3f, Semitones(4)); }

        public override void RegionEntered(ArenaExplorationZone zone) { if (Play2D(whoosh, .8f) < 0) Play2D(uiClick, .4f, .8f); }
    }
}
