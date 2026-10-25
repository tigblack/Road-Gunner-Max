using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner
{
    /// <summary>One run-local music voice. Ordinary session notifications never restart its playhead.</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMusic : MonoBehaviour
    {
        public const string VolumePreferenceKey = "RoadGunner.Audio.MusicVolume";
        public GameSession session;
        public AudioSource source;
        public AudioClip loopClip;
        [Range(0, 1)] public float musicVolume = .3f;
        [Min(.05f)] public float fadeSeconds = 1.2f;
        [Range(0, 1)] public float pausedVolumeMultiplier;
        [Range(0, 1)] public float endedVolumeMultiplier;
        public GameObject volumeControl;
        public Slider volumeSlider;
        public Text volumeValueLabel;
        public float MusicVolume => musicVolume;
        public int PlaybackStarts { get; private set; }

        SessionState state;
        bool stateKnown, started, preferenceDirty;
        float targetVolume, saveAfter;

        void Awake()
        {
            if (!session) session = GetComponent<GameSession>();
            if (!source) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.spatialBlend = 0;
            source.dopplerLevel = 0; source.ignoreListenerPause = true; source.volume = 0;
            if (loopClip) source.clip = loopClip;
            ReloadVolumePreference();
        }

        void OnEnable()
        {
            if (session) session.Changed += SessionChanged;
            if (volumeSlider) volumeSlider.onValueChanged.AddListener(SetMusicVolume);
            stateKnown = false; SessionChanged();
            if (started) StartVoice();
        }

        void Start() { started = true; StartVoice(); }

        void StartVoice()
        {
            if (!source || !loopClip || source.isPlaying) return;
            source.clip = loopClip; source.loop = true; source.Play(); PlaybackStarts++;
        }

        void SessionChanged()
        {
            if (!session || stateKnown && state == session.State) return;
            stateKnown = true; state = session.State;
            if (volumeControl) volumeControl.SetActive(state == SessionState.Paused);
            RefreshTarget();
        }

        void RefreshTarget()
        {
            float scale = !session || session.State == SessionState.Playing ? 1 :
                session.State == SessionState.Paused ? pausedVolumeMultiplier : endedVolumeMultiplier;
            targetVolume = Mathf.Clamp01(musicVolume) * Mathf.Clamp01(scale);
        }

        public void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePreferenceKey, musicVolume);
            preferenceDirty = true; saveAfter = Time.unscaledTime + .5f;
            RefreshVolumeControl(); RefreshTarget();
        }

        public void ReloadVolumePreference()
        {
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreferenceKey, musicVolume));
            RefreshVolumeControl(); RefreshTarget();
        }

        void RefreshVolumeControl()
        {
            if (volumeSlider) volumeSlider.SetValueWithoutNotify(musicVolume);
            if (volumeValueLabel) volumeValueLabel.text = Mathf.RoundToInt(musicVolume * 100) + "%";
        }

        void Update()
        {
            if (source && !Mathf.Approximately(source.volume, targetVolume))
                source.volume = Mathf.MoveTowards(source.volume, targetVolume,
                    Time.unscaledDeltaTime / Mathf.Max(.05f, fadeSeconds));
            if (preferenceDirty && Time.unscaledTime >= saveAfter) SavePreference();
        }

        void SavePreference() { if (!preferenceDirty) return; PlayerPrefs.Save(); preferenceDirty = false; }
        void OnApplicationPause(bool paused) { if (paused) SavePreference(); }
        void OnApplicationQuit() { SavePreference(); }
        void OnDisable()
        {
            if (session) session.Changed -= SessionChanged;
            if (volumeSlider) volumeSlider.onValueChanged.RemoveListener(SetMusicVolume);
            SavePreference();
            if (source) source.Stop();
        }
    }
}
