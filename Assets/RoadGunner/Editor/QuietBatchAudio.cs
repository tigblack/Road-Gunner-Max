using System;
using UnityEditor;
using UnityEngine;

namespace RoadGunner.Editor
{
    /// <summary>Opt-in test output mute. Never changes desktop volume or the published player's audio.</summary>
    [InitializeOnLoad]
    public static class QuietBatchAudio
    {
        static QuietBatchAudio()
        {
            if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-rgMuteAudio") < 0) return;
            // Apply before any test enters Play Mode and retain it across domain reloads.
            AudioListener.volume = 0;
            Debug.Log("ROAD_GUNNER_TEST_AUDIO_MUTED game listener only; desktop volume unchanged");
            EditorApplication.update += MuteTestOutput;
            EditorApplication.playModeStateChanged += _ => MuteTestOutput();
        }

        static void MuteTestOutput() => AudioListener.volume = 0;
    }
}
