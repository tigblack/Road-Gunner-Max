#if UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator ArenaMusicChecks()
        {
            Isolate();
            var music = s.GetComponent<ArenaMusic>();
            Check("one original Arena music source is connected", music && music.session == s && music.source && music.loopClip &&
                s.GetComponents<ArenaMusic>().Length == 1 && s.GetComponents<AudioSource>().Length == 1 &&
                music.source.gameObject == s.gameObject && music.source != s.effects.source,
                "One dedicated music AudioSource on the existing GameSession; gun/impact/pickup source remains separate");
            if (!music || !music.source || !music.loopClip) yield break;

            var voice = music.source;
            var originalSource = voice; int starts = music.PlaybackStarts;
            bool hadPreference = PlayerPrefs.HasKey(ArenaMusic.VolumePreferenceKey);
            float storedPreference = PlayerPrefs.GetFloat(ArenaMusic.VolumePreferenceKey, .3f);
            float originalVolume = music.MusicVolume, originalTime = voice.time;
            float effectsVolume = s.effects.source ? s.effects.source.volume : 0;
            try
            {
                yield return new WaitForSecondsRealtime(.2f);
                Check("Arena music automatically plays the loaded loop", voice.isPlaying && voice.loop && voice.clip == music.loopClip &&
                    !voice.playOnAwake && voice.spatialBlend == 0 && voice.ignoreListenerPause && starts == 1 &&
                    music.loopClip.loadState == AudioDataLoadState.Loaded && music.loopClip.length > 47 && music.loopClip.length < 50 &&
                    music.loopClip.channels == 2,
                    "Playing=" + voice.isPlaying + "; loop=" + voice.loop + "; starts=" + starts +
                    "; clip=" + music.loopClip.name + "; length=" + music.loopClip.length.ToString("F3") + "s; load=" + music.loopClip.loadState);

                music.SetMusicVolume(.3f);
                yield return new WaitForSecondsRealtime(music.fadeSeconds + .1f);
                Check("music reaches moderate volume without changing SFX", Mathf.Abs(voice.volume - .3f) < .005f &&
                    Mathf.Abs(music.MusicVolume - .3f) < .001f && (!s.effects.source || Mathf.Approximately(effectsVolume, s.effects.source.volume)),
                    "Music source volume=" + voice.volume.ToString("F3") + "; SFX volume retained=" + effectsVolume);

                // Keep these continuity observations away from the natural clip boundary.
                voice.time = 2;
                yield return new WaitForSecondsRealtime(.1f);
                float before = voice.time;
                s.AddScrap(1); s.AddScrap(1); s.health.Heal(1);
                yield return new WaitForSecondsRealtime(.3f);
                Check("small gameplay notifications do not restart music", voice.time > before + .1f &&
                    voice.isPlaying && music.PlaybackStarts == starts && music.source == originalSource,
                    "Playhead=" + before.ToString("F2") + " -> " + voice.time.ToString("F2") + "s; starts=" + music.PlaybackStarts);
                before = voice.time;
                Isolate();
                yield return new WaitForSecondsRealtime(.3f);
                Check("Arena restart keeps one continuous music voice", voice.time > before + .1f && voice.isPlaying &&
                    music.PlaybackStarts == starts && music.source == originalSource && s.GetComponents<AudioSource>().Length == 1,
                    "Restart playhead=" + before.ToString("F2") + " -> " + voice.time.ToString("F2") + "s; starts=" + music.PlaybackStarts);

                // Exercise actual native playback across the imported clip end. Do not call
                // GetData: the clip deliberately uses CompressedInMemory for mobile/WebGL.
                voice.time = Mathf.Max(1, music.loopClip.length - .4f);
                float nearEnd = voice.time;
                yield return new WaitForSecondsRealtime(1.1f);
                Check("music playback wraps the loop without a second source", nearEnd > music.loopClip.length - 1 && voice.isPlaying &&
                    voice.time >= 0 && voice.time < 2 && music.PlaybackStarts == starts && music.source == originalSource,
                    "Playhead across native loop=" + nearEnd.ToString("F3") + " -> " + voice.time.ToString("F3") +
                    "s; starts=" + music.PlaybackStarts + "; audible encoded seam requires browser/listening verification");

                float beforePauseVolume = voice.volume;
                s.SetPaused(true);
                bool retainedBeforeFade = Mathf.Abs(voice.volume - beforePauseVolume) < .005f;
                yield return new WaitForSecondsRealtime(.15f);
                float fadingVolume = voice.volume;
                yield return new WaitForSecondsRealtime(music.fadeSeconds + .1f);
                Check("pause smoothly fades music using unscaled time", retainedBeforeFade && fadingVolume < beforePauseVolume &&
                    voice.volume < .005f && voice.isPlaying && Time.timeScale == 0 &&
                    music.volumeControl && music.volumeControl.activeInHierarchy && music.volumeSlider,
                    "Volume=" + beforePauseVolume.ToString("F3") + " -> " + fadingVolume.ToString("F3") + " -> " + voice.volume.ToString("F3") +
                    "; pause Music control visible=" + (music.volumeControl && music.volumeControl.activeInHierarchy));

                if (music.volumeSlider) music.volumeSlider.value = .17f;
                yield return new WaitForSecondsRealtime(.6f);
                music.musicVolume = .51f;
                music.ReloadVolumePreference();
                Check("pause Music slider persists separately from SFX", music.volumeSlider &&
                    Mathf.Abs(PlayerPrefs.GetFloat(ArenaMusic.VolumePreferenceKey, -1) - .17f) < .001f &&
                    Mathf.Abs(music.MusicVolume - .17f) < .001f && Mathf.Abs(music.volumeSlider.value - .17f) < .001f &&
                    music.volumeValueLabel && music.volumeValueLabel.text == "17%" &&
                    (!s.effects.source || Mathf.Approximately(effectsVolume, s.effects.source.volume)),
                    "Saved/reloaded music=" + music.MusicVolume + "; key=" + ArenaMusic.VolumePreferenceKey + "; SFX unchanged");
                Capture("pause-music-control");
                s.SetPaused(false);
                yield return new WaitForSecondsRealtime(music.fadeSeconds + .1f);
                Check("resume restores chosen music level without restarting", voice.isPlaying && Mathf.Abs(voice.volume - .17f) < .005f &&
                    music.PlaybackStarts == starts && music.volumeControl && !music.volumeControl.activeSelf,
                    "Resumed volume=" + voice.volume.ToString("F3") + "; starts=" + music.PlaybackStarts);

                s.health.DamageEnabled = true;
                s.health.Damage(s.health.Current + 1, false);
                yield return new WaitForSecondsRealtime(music.fadeSeconds + .1f);
                Check("defeat fades the same music voice", s.State == SessionState.Defeated && voice.volume < .005f && voice.isPlaying &&
                    music.PlaybackStarts == starts, "Defeat volume=" + voice.volume.ToString("F3") + "; sources remain one");
            }
            finally
            {
                Isolate();
                music.SetMusicVolume(originalVolume);
                if (hadPreference) PlayerPrefs.SetFloat(ArenaMusic.VolumePreferenceKey, storedPreference);
                else PlayerPrefs.DeleteKey(ArenaMusic.VolumePreferenceKey);
                PlayerPrefs.Save();
                music.ReloadVolumePreference();
                if (voice && music.loopClip) voice.time = Mathf.Clamp(originalTime, 0, music.loopClip.length - .05f);
            }
            Check("music test restores the original volume preference", PlayerPrefs.HasKey(ArenaMusic.VolumePreferenceKey) == hadPreference &&
                (!hadPreference || Mathf.Abs(PlayerPrefs.GetFloat(ArenaMusic.VolumePreferenceKey) - storedPreference) < .001f) &&
                Mathf.Abs(music.MusicVolume - originalVolume) < .001f,
                "Original preference presence/value restored; test creates no permanent settings change");
        }
    }
}
#endif
