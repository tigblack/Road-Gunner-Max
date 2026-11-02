#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>
    /// Editor-only diagnostics: when Unity starts with "-rgAudioRecord path.wav" the final listener mix is written to a
    /// 16-bit WAV, so the in-game balance can be measured (loudness, peaks, spectrum) where nobody can listen.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ArcadeAudioRecorder : MonoBehaviour
    {
        string path;
        float[] buffer;
        int written, channels = 2, rate;
        volatile bool recording;
        /// <summary>True while a recording was requested; ArcadeAudio then also logs its roll/skid/wind levels at 4 Hz.</summary>
        public static bool Tracing { get; private set; }
        /// <summary>Unscaled time when the recording began (-1 when none): diagnostics log marks against the WAV clock.</summary>
        public static float StartedAt { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Tracing = false; StartedAt = -1; }

        public static void AttachIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-rgAudioRecord");
            if (index < 0 || index + 1 >= args.Length) return;
            var listener = FindAnyObjectByType<AudioListener>();
            if (!listener || listener.GetComponent<ArcadeAudioRecorder>()) return;
            listener.gameObject.AddComponent<ArcadeAudioRecorder>().path = args[index + 1];
            Tracing = true;
        }

        void Awake()
        {
            rate = AudioSettings.outputSampleRate;
            buffer = new float[Mathf.Max(1, rate) * 2 * 240];
            recording = true;
            StartedAt = Time.unscaledTime;
        }

        void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (!recording || buffer == null) return;
            channels = channelCount;
            int count = Math.Min(data.Length, buffer.Length - written);
            if (count <= 0) return;
            Array.Copy(data, 0, buffer, written, count);
            written += count;
        }

        void OnDisable() { Save(); }
        void OnApplicationQuit() { Save(); }

        void Save()
        {
            if (!recording || string.IsNullOrEmpty(path)) return;
            recording = false;
            int frames = written / Mathf.Max(1, channels);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    int bytes = frames * channels * 2;
                    writer.Write(new[] { 'R', 'I', 'F', 'F' }); writer.Write(36 + bytes);
                    writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2);
                    writer.Write((short)(channels * 2)); writer.Write((short)16);
                    writer.Write(new[] { 'd', 'a', 't', 'a' }); writer.Write(bytes);
                    for (int i = 0; i < frames * channels; i++) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(buffer[i] * 32767), -32768, 32767));
                }
                Debug.Log("ARCADE AUDIO recorded " + (frames / (float)Mathf.Max(1, rate)).ToString("F1") + " s of the listener mix (" + rate + " Hz, " + channels + " ch) -> " + path);
            }
            catch (Exception error) { Debug.LogWarning("ARCADE AUDIO recording not saved: " + error.Message); }
        }
    }
}
#endif
