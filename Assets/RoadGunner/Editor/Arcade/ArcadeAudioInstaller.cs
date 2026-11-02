using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Installs the arcade audio sink ("Arcade audio" under the feedback hub) and wires whatever processed clips exist in
    /// Audio/Arcade (Tools/Audio/process_generated_audio.py). Re-runnable: find-or-create by name, same result every time.
    /// Missing clips leave their bank empty, so the sink keeps the legacy sounds for those events.
    /// </summary>
    public static class ArcadeAudioInstaller
    {
        public const string ArcadeFolder = "Assets/RoadGunner/Audio/Arcade";
        public const string MusicPath = "Assets/RoadGunner/Audio/Music/Desert Run.wav";
        public const string FallbackMusicPath = "Assets/RoadGunner/Audio/Music/Dustlight Road.wav";
        const string NodeName = "Arcade audio";
        const int VoiceCount = 14;

        [MenuItem("Tools/Road Gunner/Arcade/Apply Arcade Audio")]
        public static void ApplyMenu()
        {
            Apply(ArcadeFeedbackSetup.OpenGameplayScene());
            Debug.Log("ARCADE AUDIO applied; save the scene to keep it.");
        }

        public static void ApplyBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            Apply(session);
            ArcadeFeedbackSetup.Save();
            Debug.Log("ARCADE AUDIO OK");
        }

        public static void Apply(GameSession session)
        {
            var hub = ArcadeFeedbackSetup.Ensure(session);
            AssetDatabase.Refresh();
            var paths = ArcadeClipPaths();
            foreach (string path in paths) ConfigureImporter(path, true, Path.GetFileNameWithoutExtension(path).EndsWith("_Loop", StringComparison.Ordinal) ? .6f : .7f, null);
            bool hasMusic = AssetImporter.GetAtPath(MusicPath) is AudioImporter;
            if (hasMusic) ConfigureImporter(MusicPath, false, .7f, "ElevenLabs Music v2.5 via DostupAI; processed loop, see sibling source note.");

            var root = Child(hub.transform, NodeName);
            var audio = root.GetComponent<ArcadeAudio>();
            if (!audio) audio = Undo.AddComponent<ArcadeAudio>(root.gameObject);
            audio.enabled = true; audio.hub = hub; audio.session = session;

            var voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                voices[i] = Source(Child(root, "Voice " + (i + 1).ToString("00")));
                ArcadeAudio.ConfigureVoice(voices[i]);
                voices[i].clip = null;
            }
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child.name.StartsWith("Voice ", StringComparison.Ordinal) && int.TryParse(child.name.Substring(6), out int number) && number > VoiceCount)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
            audio.voices = voices;
            audio.engineIdle = Loop(root, "Engine idle", 64, Clip(paths, "Engine_Idle_Loop"));
            audio.engineDrive = Loop(root, "Engine drive", 64, Clip(paths, "Engine_Drive_Loop"));
            audio.skid = Loop(root, "Tire skid", 90, Clip(paths, "Tire_Skid_Loop"));
            audio.wind = Loop(root, "Desert wind", 160, Clip(paths, "Wind_Desert_Loop"));

            audio.playerGun.clips = Clips(paths, "Gun_Player");
            audio.enemyGun.clips = Clips(paths, "Gun_Enemy");
            audio.bulletImpact.clips = Clips(paths, "Impact_Bullet");
            audio.metalHit.clips = Clips(paths, "Impact_Bullet");
            audio.bodyHit.clips = Clips(paths, "Hit_Enemy");
            audio.explosion.clips = Clips(paths, "Explosion");
            audio.crash.clips = Clips(paths, "Crash_Impact");
            audio.collision.clips = Clips(paths, "Crash_Impact");
            audio.crateBreak.clips = Clips(paths, "Crate_Break");
            audio.crateHit.clips = Clips(paths, "Crate_Hit");
            audio.scrap.clips = Clips(paths, "Scrap_Pickup");
            audio.gearDrop.clips = Clips(paths, "Scrap_Pickup");
            audio.killChime.clips = Clips(paths, "Scrap_Pickup");
            audio.upgrade.clips = Clips(paths, "Upgrade_Powerup");
            audio.uiClick.clips = Clips(paths, "UI_Click");
            audio.bossWarning.clips = Clips(paths, "Boss_Warning");
            audio.victory.clips = Clips(paths, "Stinger_Victory");
            audio.defeat.clips = Clips(paths, "Stinger_Defeat");
            audio.harpoonFire.clips = Clips(paths, "Harpoon_Fire");
            audio.harpoonReel.clips = Clips(paths, "Harpoon_Reel");
            audio.whoosh.clips = Clips(paths, "Whoosh");
            audio.ResetMix();
            Dirty(audio);

            // New music only when it has been generated and processed; otherwise (or if it was removed) Dustlight Road.
            var musicClip = hasMusic ? AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath) : null;
            var music = session.GetComponent<ArenaMusic>();
            if (music && !musicClip && (!music.loopClip || music.source && !music.source.clip))
                musicClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FallbackMusicPath);
            if (musicClip && music)
            {
                music.loopClip = musicClip; Dirty(music);
                if (music.source) { music.source.clip = musicClip; Dirty(music.source); }
            }
            if (session.GetComponents<AudioSource>().Length != 1)
                Debug.LogWarning("ARCADE AUDIO: the Session object should hold exactly one AudioSource (music); found " + session.GetComponents<AudioSource>().Length);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);

            bool core = audio.playerGun.HasOwn && audio.bulletImpact.HasOwn && audio.explosion.HasOwn && audio.scrap.HasOwn;
            var banks = new (string name, ArcadeAudio.Bank bank)[]
            {
                ("player gun", audio.playerGun), ("enemy gun", audio.enemyGun), ("bullet impacts", audio.bulletImpact), ("enemy hits", audio.bodyHit),
                ("explosion", audio.explosion), ("crash", audio.crash), ("crate break", audio.crateBreak), ("crate hit", audio.crateHit),
                ("scrap", audio.scrap), ("upgrade", audio.upgrade), ("ui click", audio.uiClick), ("boss warning", audio.bossWarning),
                ("victory", audio.victory), ("defeat", audio.defeat), ("harpoon fire", audio.harpoonFire), ("harpoon reel", audio.harpoonReel), ("whoosh", audio.whoosh),
            };
            string loops = "engine idle=" + (audio.engineIdle.clip ? "yes" : "no") + ", engine drive=" + (audio.engineDrive.clip ? "yes" : "no") +
                           ", skid=" + (audio.skid.clip ? "yes" : "no") + ", wind=" + (audio.wind.clip ? "yes" : "no");
            Debug.Log("ARCADE AUDIO: " + paths.Count + " clips in " + ArcadeFolder + "; core=" + (core ? "new (legacy one-shots silenced at runtime)" : "legacy fallback") +
                      "; music=" + (musicClip && music ? musicClip.name + " " + musicClip.length.ToString("F2") + "s" : "unchanged") + "; loops: " + loops +
                      "\nbanks: " + string.Join(", ", banks.Select(b => b.name + "=" + b.bank.clips.Length)));
            foreach (string path in hasMusic ? paths.Append(MusicPath) : paths) LogImport(path);
        }

        static void LogImport(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer) || !clip) { Debug.LogWarning("ARCADE AUDIO: not imported " + path); return; }
            var normalize = new SerializedObject(importer).FindProperty("m_Normalize") ?? new SerializedObject(importer).FindProperty("normalize");
            var settings = importer.defaultSampleSettings;
            Debug.Log("ARCADE AUDIO clip " + clip.name + ": " + clip.length.ToString("F3") + "s " + clip.channels + "ch " + clip.frequency + "Hz " +
                      settings.loadType + " q" + settings.quality + " preload=" + settings.preloadAudioData + " mono=" + importer.forceToMono +
                      " normalize=" + (normalize != null ? normalize.boolValue.ToString() : "n/a"));
        }

        /// <summary>
        /// Diagnostics (batch, no -quit): plays each guarded loop slowed to pitch 0.25 across its end and reports where the
        /// playhead lands; a smpl-aware import wraps to ~2048 samples (the guard is skipped), a plain loop wraps to ~0.
        /// </summary>
        public static void ProbeLoopPointsBatch()
        {
            var clips = ArcadeClipPaths().Where(p => p.EndsWith("_Loop.wav", StringComparison.Ordinal)).Append(MusicPath)
                .Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).Where(c => c != null).ToList();
            var go = EditorUtility.CreateGameObjectWithHideFlags("Arcade loop probe", HideFlags.HideAndDontSave, typeof(AudioSource));
            var source = go.GetComponent<AudioSource>();
            source.volume = .001f; source.loop = true; source.pitch = .25f; source.spatialBlend = 0;
            int index = -1, previous = 0; double previousTime = 0, deadline = 0;
            void Next()
            {
                index++;
                if (index >= clips.Count) { UnityEngine.Object.DestroyImmediate(go); EditorApplication.update -= Tick; Debug.Log("ARCADE AUDIO probe done"); EditorApplication.Exit(0); return; }
                source.Stop(); source.clip = clips[index]; source.Play();
                source.timeSamples = Mathf.Max(0, clips[index].samples - 6000);
                previous = source.timeSamples; previousTime = EditorApplication.timeSinceStartup; deadline = previousTime + 4;
            }
            void Tick()
            {
                double now = EditorApplication.timeSinceStartup;
                if (index < 0 || index >= clips.Count) return;
                int position = source.timeSamples;
                if (position < previous)
                {
                    var clip = clips[index];
                    double advanced = (now - previousTime) * clip.frequency * source.pitch - (clip.samples - previous);
                    Debug.Log("ARCADE AUDIO loop probe " + clip.name + ": wrapped to sample " + position + " (expected ~" + Math.Round(2048 + advanced) +
                              " if the smpl loop start is honoured, ~" + Math.Round(advanced) + " if not; clip " + clip.samples + " samples)");
                    Next(); return;
                }
                previous = position; previousTime = now;
                if (now > deadline) { Debug.LogWarning("ARCADE AUDIO loop probe " + clips[index].name + ": no wrap observed (position " + position + ")"); Next(); }
            }
            EditorApplication.update += Tick;
            Next();
        }

        static List<string> ArcadeClipPaths()
        {
            if (!AssetDatabase.IsValidFolder(ArcadeFolder)) return new List<string>();
            return AssetDatabase.FindAssets("t:AudioClip", new[] { ArcadeFolder }).Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(p => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        /// <summary>"Name" or numbered variants "Name_01", "Name_02"...</summary>
        static AudioClip[] Clips(List<string> paths, string prefix)
        {
            return paths.Where(p =>
            {
                string name = Path.GetFileNameWithoutExtension(p);
                if (name == prefix) return true;
                if (!name.StartsWith(prefix + "_", StringComparison.Ordinal) || name.Length == prefix.Length + 1) return false;
                return name.Substring(prefix.Length + 1).All(char.IsDigit);
            }).Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).Where(c => c != null).ToArray();
        }

        static AudioClip Clip(List<string> paths, string name) => Clips(paths, name).FirstOrDefault();

        static void ConfigureImporter(string path, bool mono, float quality, string userData)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) return;
            bool changed = false;
            var settings = importer.defaultSampleSettings;
            // CompressedInMemory: on WebGL, iOS keeps it audible with the silent switch (DecompressOnLoad is not);
            // Unity still decodes small (<128 KB) clips to AudioBuffers there, so pitch and loop points stay exact.
            if (settings.loadType != AudioClipLoadType.CompressedInMemory || settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                !Mathf.Approximately(settings.quality, quality) || settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate || !settings.preloadAudioData)
            {
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = quality;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings; changed = true;
            }
            foreach (string platform in new[] { "WebGL", "iOS", "Android", "Standalone" })
                if (importer.ContainsSampleSettingsOverride(platform)) { importer.ClearSampleSettingOverride(platform); changed = true; }
            if (importer.loadInBackground) { importer.loadInBackground = false; changed = true; }
            if (importer.ambisonic) { importer.ambisonic = false; changed = true; }
            if (userData != null && importer.userData != userData) { importer.userData = userData; changed = true; }
            // Per-category loudness is baked into the files; Unity's Normalize (active with Force To Mono) would flatten it.
            var serialized = new SerializedObject(importer);
            var normalize = serialized.FindProperty("m_Normalize") ?? serialized.FindProperty("normalize");
            bool wantMono = mono && normalize != null;   // the processed SFX are mono already; never risk normalisation
            if (normalize != null && normalize.boolValue)
            {
                normalize.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo(); changed = true;
            }
            if (importer.forceToMono != wantMono) { importer.forceToMono = wantMono; changed = true; }
            if (changed) importer.SaveAndReimport();
        }

        static Transform Child(Transform parent, string name)
        {
            var found = parent.Find(name);
            if (found) return found;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Arcade audio");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static AudioSource Source(Transform owner)
        {
            var sources = owner.GetComponents<AudioSource>();
            for (int i = sources.Length - 1; i >= 1; i--) Undo.DestroyObjectImmediate(sources[i]);
            return sources.Length > 0 ? sources[0] : Undo.AddComponent<AudioSource>(owner.gameObject);
        }

        static AudioSource Loop(Transform root, string name, int priority, AudioClip clip)
        {
            var source = Source(Child(root, name));
            ArcadeAudio.ConfigureLoop(source, priority);
            source.clip = clip;
            Dirty(source);
            return source;
        }

        static void Dirty(UnityEngine.Object value) { if (value) EditorUtility.SetDirty(value); }
    }
}
