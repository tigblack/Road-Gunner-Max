using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RoadGunner.Editor
{
    /// <summary>Connects the original loop to the existing session and pause overlay; no new audio manager.</summary>
    public static class ArenaMusicBuilder
    {
        public const string ClipPath = "Assets/RoadGunner/Audio/Music/Dustlight Road.wav";

        public static ArenaMusic Configure(GameSession session)
        {
            if (!session || !session.hud || !session.hud.overlay)
                throw new InvalidOperationException("Arena music requires the existing session and pause overlay.");
            var importer = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Generate/import Dustlight Road.wav before configuring Arena music.");
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .55f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = false; importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (!clip) throw new InvalidOperationException("Original Arena music clip did not import.");

            var music = session.GetComponent<ArenaMusic>();
            if (!music) music = session.gameObject.AddComponent<ArenaMusic>();
            if (!music.source) music.source = session.gameObject.AddComponent<AudioSource>();
            music.session = session; music.loopClip = clip; music.musicVolume = .3f;
            music.fadeSeconds = 1.2f; music.pausedVolumeMultiplier = 0; music.endedVolumeMultiplier = 0;
            var source = music.source;
            source.clip = clip; source.playOnAwake = false; source.loop = true; source.spatialBlend = 0;
            source.dopplerLevel = 0; source.volume = 0; source.priority = 192; source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = null;
            BuildVolumeControl(music, session.hud);
            Dirty(music); Dirty(source);
            return music;
        }

        static void Dirty(UnityEngine.Object value)
        {
            EditorUtility.SetDirty(value);
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 minimum, Vector2 maximum, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = minimum; rect.anchorMax = maximum; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            return rect;
        }

        static Image Image(string name, Transform parent, Vector2 minimum, Vector2 maximum, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = Rect(name, parent, minimum, maximum, offsetMin, offsetMax);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }

        static Text Label(string name, Transform parent, Font font, string value, Vector2 minimum, Vector2 maximum, TextAnchor alignment)
        {
            var rect = Rect(name, parent, minimum, maximum, Vector2.zero, Vector2.zero);
            var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.text = value;
            label.fontSize = 25; label.alignment = alignment; label.color = new Color(.10f, .20f, .17f); label.raycastTarget = false;
            return label;
        }

        static void BuildVolumeControl(ArenaMusic music, HudView hud)
        {
            const string controlName = "Arena music volume";
            var previous = hud.overlay.transform.Find(controlName);
            if (previous) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var root = Rect(controlName, hud.overlay.transform, new Vector2(.5f, .425f), new Vector2(.5f, .425f),
                new Vector2(-320, -40), new Vector2(320, 40));
            Label("Music label", root, hud.healthText.font, "MUSIC", Vector2.zero, new Vector2(.23f, 1), TextAnchor.MiddleLeft);
            var control = Rect("Music slider", root, new Vector2(.25f, .5f), new Vector2(.84f, .5f), new Vector2(0, -32), new Vector2(0, 32));
            var hitArea = control.gameObject.AddComponent<Image>(); hitArea.color = new Color(0, 0, 0, .005f); hitArea.raycastTarget = true;
            var slider = control.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            slider.direction = Slider.Direction.LeftToRight; slider.wholeNumbers = false;
            Image("Track", control, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -5), new Vector2(0, 5), new Color(.13f, .19f, .18f));
            var fillArea = Rect("Fill area", control, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
            var fill = Image("Music fill", fillArea, new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(0, -5), new Vector2(0, 5), new Color(.28f, .60f, .44f));
            var handleArea = Rect("Handle area", control, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
            var handle = Image("Music handle", handleArea, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-12, -22), new Vector2(12, 22), new Color(.18f, .42f, .37f));
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.navigation = new Navigation { mode = Navigation.Mode.None }; slider.SetValueWithoutNotify(.3f);
            music.volumeControl = root.gameObject; music.volumeSlider = slider;
            music.volumeValueLabel = Label("Music value", root, hud.healthText.font, "30%", new Vector2(.86f, 0), Vector2.one, TextAnchor.MiddleRight);
            root.gameObject.SetActive(false); Dirty(slider);
        }
    }
}
