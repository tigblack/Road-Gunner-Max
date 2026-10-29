#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RoadGunner
{
    public sealed partial class Arena1PlayChecks
    {
        IEnumerator AntonFeedback()
        {
            Isolate();
            var guidance = s.GetComponent<ArenaGuidance>();
            Check("onboarding is bound to the saved gameplay HUD", guidance && guidance.session == s &&
                guidance.objectiveText && guidance.objectiveDetailText && guidance.hintText && guidance.hintPanel &&
                guidance.gestureRoot && guidance.directionArrow && guidance.directionLabel && guidance.objectivePanel &&
                guidance.directionRow && s.hud.guidance == guidance,
                "Saved scene must contain the presentation component and its actual UI references");
            if (!guidance) yield break;
            yield return null; yield return null;
            Check("first-run hint explains drag and automatic fire", !guidance.HasDriven && !guidance.RangeHintShown &&
                guidance.hintPanel.activeInHierarchy && guidance.gestureRoot.activeInHierarchy &&
                guidance.CurrentHint.Contains("DRAG TO DRIVE") && guidance.CurrentHint.Contains("gun fires automatically"),
                "Hint=" + guidance.CurrentHint.Replace('\n', ' '));
            Check("arena goal distinguishes mandatory Warlord from optional Monster",
                guidance.objectiveText.text == "DEFEAT WARLORD" && guidance.objectiveDetailText.text.Contains("Monster optional"),
                guidance.objectiveText.text + " / " + guidance.objectiveDetailText.text);

            var presentation = new[] { guidance.hintPanel, guidance.gestureRoot, guidance.objectiveText.gameObject,
                guidance.objectiveDetailText.gameObject, guidance.directionArrow.gameObject, guidance.directionLabel.gameObject };
            bool noControls = presentation.All(root => root.GetComponentsInChildren<Selectable>(true).Length == 0);
            bool noRaycasts = presentation.All(root => root.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget));
            Check("guidance adds no buttons or raycast blockers", noControls && noRaycasts,
                "Selectable controls=" + presentation.Sum(root => root.GetComponentsInChildren<Selectable>(true).Length) +
                "; every guidance Graphic ignores pointer raycasts=" + noRaycasts);
            var raycasts = new List<RaycastResult>();
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenBounds(guidance.hintText.rectTransform).center };
            EventSystem.current.RaycastAll(pointer, raycasts);
            Check("hint does not intercept the real EventSystem hit path",
                !raycasts.Any(hit => presentation.Any(root => hit.gameObject.transform.IsChildOf(root.transform))),
                "Raycast under the visible hint resolves only to the existing gameplay/UI layers; hits=" + raycasts.Count);

            var music = s.GetComponent<ArenaMusic>();
            bool hadMusicPreference = PlayerPrefs.HasKey(ArenaMusic.VolumePreferenceKey);
            float storedMusicPreference = PlayerPrefs.GetFloat(ArenaMusic.VolumePreferenceKey, .3f);
            float originalMusicVolume = music ? music.MusicVolume : 0;
            var originalMusicSource = music ? music.source : null;
            syntheticTouch = InputSystem.AddDevice<Touchscreen>();
            try
            {
                s.input.testInput = false;
                Vector2 origin = ControlPoint(.25f, .28f);
                Touch(201, UnityEngine.InputSystem.TouchPhase.Began, origin);
                yield return null; yield return null;
                Touch(201, UnityEngine.InputSystem.TouchPhase.Moved, origin + Vector2.up * 90);
                yield return null; yield return null; yield return null;
                Check("real touch dismisses onboarding without an acknowledgement button", s.input.joystick.IsHeld &&
                    s.input.Magnitude > .12f && guidance.HasDriven && !guidance.gestureRoot.activeSelf && !guidance.hintPanel.activeSelf,
                    "Production InputSystem touch magnitude=" + s.input.Magnitude + "; HasDriven=" + guidance.HasDriven);
                Check("driving compacts the objective card", guidance.objectivePanel && guidance.objectivePanel.rect.height <= 148.1f &&
                    !guidance.objectiveDetailText.gameObject.activeSelf,
                    "Objective reference height=" + (guidance.objectivePanel ? guidance.objectivePanel.rect.height : 0) + "; introductory detail hidden");
                Touch(201, UnityEngine.InputSystem.TouchPhase.Ended, origin + Vector2.up * 90);
                yield return null; yield return null;
                s.SetPaused(true);
                yield return null;
                Check("pause hides transient guidance and clears its input", guidance.HasDriven && guidance.CurrentHint == "" &&
                    !guidance.hintPanel.activeSelf && !guidance.gestureRoot.activeSelf && !guidance.directionArrow.gameObject.activeSelf &&
                    !guidance.directionLabel.gameObject.activeSelf && s.input.Magnitude == 0 && !s.input.joystick.IsHeld,
                    "Pause preserves tutorial progress while all transient prompts and captured input are hidden");
                s.SetPaused(false);
                yield return null; yield return null;
                Check("resume preserves completed onboarding", guidance.HasDriven && !guidance.gestureRoot.activeSelf &&
                    !guidance.CurrentHint.Contains("DRAG TO DRIVE"), "Resume is the same run");

                int serial = s.RunSerial;
                Isolate(); yield return null; yield return null;
                Check("restart restores the initial hint once for the new run", s.RunSerial > serial && !guidance.HasDriven &&
                    !guidance.RangeHintShown && guidance.hintPanel.activeInHierarchy && guidance.gestureRoot.activeInHierarchy &&
                    guidance.CurrentHint.Contains("DRAG TO DRIVE"), "Run serial=" + serial + " -> " + s.RunSerial);

                // The existing harness supplies the normal input path; combat below is the real auto gun.
                s.input.testDirection = Vector2.up;
                yield return null; yield return null;
                s.input.testDirection = Vector2.zero;
                Check("range tutorial waits for an actual auto-gun target", guidance.HasDriven && !guidance.RangeHintShown,
                    "An empty isolated arena does not fabricate a combat prompt");
                bool targetPlaced = SpawnInGunRange(human);
                s.turret.enabled = true;
                float deadline = Time.realtimeSinceStartup + 5;
                bool acquired = false;
                while (!guidance.RangeHintShown && Time.realtimeSinceStartup < deadline)
                { acquired |= s.turret.Target == human; yield return null; }
                Check("real target triggers the white ten metre range explanation", targetPlaced && acquired && guidance.RangeHintShown &&
                    guidance.CurrentHint.Contains("WHITE RING") && guidance.CurrentHint.Contains("10 m"),
                    "Actual gun acquired target=" + acquired + "; hint=" + guidance.CurrentHint.Replace('\n', ' '));
                string rangeHint = guidance.CurrentHint;
                s.SetPaused(true); yield return new WaitForSecondsRealtime(.35f);
                bool hidden = guidance.CurrentHint == "" && !guidance.hintPanel.activeSelf;
                s.SetPaused(false); yield return null;
                Check("pause preserves the remaining combat explanation", hidden && guidance.RangeHintShown && guidance.CurrentHint == rangeHint,
                    "Paused time does not consume or restart the shown range hint");
                deadline = Time.realtimeSinceStartup + 10;
                while (!guidance.CurrentHint.Contains("SCRAP SCORE") && Time.realtimeSinceStartup < deadline) yield return null;
                Check("earned combat reward explains scrap as score", s.Kills == 1 && !human.Alive && s.turret.ShotsFired > 0 &&
                    guidance.CurrentHint.Contains("SCRAP SCORE"),
                    "Actual auto-gun kill=" + s.Kills + "; shots=" + s.turret.ShotsFired + "; hint=" + guidance.CurrentHint.Replace('\n', ' '));

                Isolate(); s.AddScrap(7); yield return null;
                Check("HUD identifies collected scrap as score", s.hud.scoreLabel && s.hud.scoreLabel.text.ToUpperInvariant().Contains("SCORE") &&
                    s.hud.scrapText.text == "007" && s.Scrap == 7,
                    "Label=" + (s.hud.scoreLabel ? s.hud.scoreLabel.text : "missing") + "; displayed=" + s.hud.scrapText.text);
                var attack = s.runStats.stations.First(station => station.kind == ArenaUpgradeKind.Attack);
                PlaceCar(attack.transform.position + Vector3.up * .7f, testRotation);
                yield return new WaitForSecondsRealtime(.25f);
                Check("free run upgrade does not spend the displayed score", attack.Consumed && s.runStats.AttackLevel == 2 && s.Scrap == 7 &&
                    s.hud.scrapText.text == "007", "Proximity upgrade collected; score remains=" + s.Scrap);

                // Resize the real Game View, not just the exported camera render target.
                foreach (var size in new[] { new Vector2Int(540, 960), new Vector2Int(360, 640), new Vector2Int(540, 1170), new Vector2Int(960, 540) })
                {
                    Isolate(); boss.ResetEncounter(); boss.AIEnabled = false;
                    Vector3 nearBoss = boss.transform.position + boss.transform.forward * 8;
                    PlaceCar(Ground(nearBoss) + Vector3.up * .7f, Quaternion.LookRotation(boss.transform.position - nearBoss));
                    SetAcceptanceResolution(size.x, size.y);
                    deadline = Time.realtimeSinceStartup + 4;
                    while ((Screen.width != size.x || Screen.height != size.y) && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return null; yield return null; Canvas.ForceUpdateCanvases();
                    yield return new WaitForSecondsRealtime(.25f);
                    string aspect = size.x + "x" + size.y;
                    Check("real HUD viewport " + aspect, Screen.width == size.x && Screen.height == size.y,
                        "Actual Screen=" + Screen.width + "x" + Screen.height);
                    CheckAntonPlayingLayout(guidance, aspect);
                    Capture("anton-boss-hud-" + aspect, size.x, size.y);
                    s.SetPaused(true); yield return null; Canvas.ForceUpdateCanvases();
                    CheckAntonOverlay(aspect);
                    Check("pause exposes the existing Music slider " + aspect, music && music.volumeSlider && music.volumeValueLabel &&
                        music.volumeControl && music.volumeControl.activeInHierarchy && music.volumeSlider.fillRect && music.volumeSlider.handleRect,
                        "Real saved music control, fill and handle are visible during pause");
                    if (music && music.volumeSlider && music.volumeSlider.fillRect && music.volumeSlider.handleRect)
                    {
                        // Start away from the requested point so a stale slider value cannot pass.
                        music.SetMusicVolume(.2f);
                        yield return null; Canvas.ForceUpdateCanvases();
                        var slider = music.volumeSlider;
                        var track = (RectTransform)slider.handleRect.parent;
                        Vector3 pointOnTrack = track.TransformPoint(new Vector3(Mathf.Lerp(track.rect.xMin, track.rect.xMax, .7f), track.rect.center.y, 0));
                        var canvas = track.GetComponentInParent<Canvas>();
                        var pointerCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                        Vector2 musicPoint = RectTransformUtility.WorldToScreenPoint(pointerCamera, pointOnTrack);
                        Touch(204, UnityEngine.InputSystem.TouchPhase.Began, musicPoint);
                        yield return null; yield return null;
                        Touch(204, UnityEngine.InputSystem.TouchPhase.Ended, musicPoint);
                        yield return null; yield return null; Canvas.ForceUpdateCanvases();
                        Check("fitted Music slider routes actual seventy percent touch " + aspect,
                            Mathf.Abs(music.MusicVolume - .7f) < .005f && Mathf.Abs(slider.value - .7f) < .005f &&
                            music.volumeValueLabel.text == "70%" && music.source == originalMusicSource &&
                            s.GetComponents<AudioSource>().Length == 1 && s.GetComponents<ArenaMusic>().Length == 1,
                            "Touch=" + musicPoint + "; chosen volume=" + music.MusicVolume.ToString("F3") +
                            "; label=" + music.volumeValueLabel.text + "; music sources=" + s.GetComponents<AudioSource>().Length);
                        Rect frame = ScreenBounds(s.hud.designFrame), fill = ScreenBounds(slider.fillRect), handle = ScreenBounds(slider.handleRect);
                        float referenceScale = frame.height / 1920f;
                        float fillHeight = fill.height / Mathf.Max(.001f, referenceScale), handleHeight = handle.height / Mathf.Max(.001f, referenceScale);
                        Check("Music fill and handle remain thin inside the fitted frame " + aspect,
                            Inside(fill, frame) && Inside(handle, frame) && fillHeight > 0 && fillHeight <= 20 && handleHeight > 0 && handleHeight <= 60,
                            "Actual reference-unit heights: fill=" + fillHeight.ToString("F1") + "; handle=" + handleHeight.ToString("F1") +
                            "; visible bounds=" + fill + " / " + handle);
                    }
                    Capture("anton-pause-" + aspect, size.x, size.y);
                    var resume = s.hud.resume;
                    Vector2 buttonPoint = ScreenBounds((RectTransform)resume.transform).center;
                    Touch(202, UnityEngine.InputSystem.TouchPhase.Began, buttonPoint);
                    yield return null; yield return null;
                    Touch(202, UnityEngine.InputSystem.TouchPhase.Ended, buttonPoint);
                    yield return null; yield return null;
                    Check("fitted overlay routes actual Resume touch " + aspect, s.Playing && !s.hud.overlay.activeSelf,
                        "Production touch at rendered button centre=" + buttonPoint);
                    s.SetPaused(true); yield return null;
                    serial = s.RunSerial;
                    buttonPoint = ScreenBounds((RectTransform)s.hud.restart.transform).center;
                    Touch(203, UnityEngine.InputSystem.TouchPhase.Began, buttonPoint);
                    yield return null; yield return null;
                    Touch(203, UnityEngine.InputSystem.TouchPhase.Ended, buttonPoint);
                    yield return null; yield return null;
                    Check("fitted overlay routes actual Restart touch " + aspect, s.Playing && s.RunSerial > serial && s.Scrap == 0 &&
                        s.Kills == 0 && s.runStats.AttackLevel == 1 && s.runStats.HealthLevel == 1 && !guidance.HasDriven &&
                        guidance.CurrentHint.Contains("DRAG TO DRIVE"), "Rendered Restart starts a fresh normal run; serial=" + s.RunSerial);
                }
            }
            finally
            {
                if (syntheticTouch != null) { InputSystem.RemoveDevice(syntheticTouch); syntheticTouch = null; }
                if (music) music.SetMusicVolume(originalMusicVolume);
                if (hadMusicPreference) PlayerPrefs.SetFloat(ArenaMusic.VolumePreferenceKey, storedMusicPreference);
                else PlayerPrefs.DeleteKey(ArenaMusic.VolumePreferenceKey);
                PlayerPrefs.Save();
                SetAcceptanceResolution(540, 960);
                s.input.Clear();
            }
            yield return null; yield return null;
            Check("fitted Music touch checks restore the original setting", PlayerPrefs.HasKey(ArenaMusic.VolumePreferenceKey) == hadMusicPreference &&
                (!hadMusicPreference || Mathf.Abs(PlayerPrefs.GetFloat(ArenaMusic.VolumePreferenceKey) - storedMusicPreference) < .001f) &&
                (!music || Mathf.Abs(music.MusicVolume - originalMusicVolume) < .001f),
                "Original runtime volume, persisted preference value and preference presence are restored");
        }

        void CheckAntonPlayingLayout(ArenaGuidance guidance, string aspect)
        {
            var hud = s.hud;
            Rect screen = new Rect(0, 0, Screen.width, Screen.height);
            Rect frame = ScreenBounds(hud.designFrame), map = ScreenBounds(s.minimap.mapRect);
            Rect objective = ScreenBounds(guidance.objectiveText.rectTransform), detail = ScreenBounds(guidance.objectiveDetailText.rectTransform);
            Rect bossBounds = ScreenBounds(hud.bossPanel), pause = ScreenBounds((RectTransform)hud.pause.transform);
            bool bound = hud.designFrame && hud.bossPanel && hud.bossHud && hud.counterPanel && hud.statsPanel;
            Check("boss HUD uses the fitted safe frame " + aspect, bound && Inside(frame, screen) &&
                Mathf.Abs(frame.width / frame.height - 1080f / 1920f) < .005f && hud.bossHud.IsVisible && hud.bossHud.Target == boss,
                "Frame=" + frame + "; actual boss visible=" + (hud.bossHud && hud.bossHud.IsVisible));
            Check("boss encounter compacts the objective card " + aspect, guidance.objectivePanel &&
                guidance.objectivePanel.rect.height <= 148.1f && !guidance.objectiveDetailText.gameObject.activeSelf,
                "Objective reference height=" + (guidance.objectivePanel ? guidance.objectivePanel.rect.height : 0) + "; introductory detail hidden during combat");
            Check("boss, objective, map and controls do not overlap " + aspect,
                Inside(bossBounds, frame) && Inside(objective, frame) && Inside(detail, frame) && Inside(map, frame) && Inside(pause, frame) &&
                !bossBounds.Overlaps(map) && !bossBounds.Overlaps(objective) && !bossBounds.Overlaps(detail) && !bossBounds.Overlaps(pause) &&
                !objective.Overlaps(map) && !detail.Overlaps(map) && !objective.Overlaps(detail) &&
                !hud.counterPanel.activeSelf && !hud.statsPanel.activeSelf,
                "Boss=" + bossBounds + "; objective=" + objective + "; detail=" + detail + "; map=" + map + "; ordinary counters hidden");
            Rect hint = ScreenBounds((RectTransform)guidance.hintPanel.transform);
            Check("initial hint stays clear of boss and objective " + aspect, guidance.hintPanel.activeInHierarchy && Inside(hint, frame) &&
                !hint.Overlaps(bossBounds) && !hint.Overlaps(objective) && !hint.Overlaps(detail) && !hint.Overlaps(map), "Hint=" + hint);
        }

        void CheckAntonOverlay(string aspect)
        {
            var hud = s.hud;
            Rect frame = ScreenBounds(hud.designFrame), card = ScreenBounds(hud.overlayCard.rectTransform);
            var labels = new[] { hud.overlayTitle, hud.overlayScore, hud.overlayDescription, hud.overlayDetails, hud.restartHint };
            var controls = new[] { hud.resume, hud.restart };
            bool bounds = Inside(card, frame) && labels.All(label => label && Inside(ScreenBounds(label.rectTransform), card)) &&
                controls.All(control => control && Inside(ScreenBounds((RectTransform)control.transform), card));
            bool nonoverlap = true;
            var rects = labels.Where(label => label && label.gameObject.activeInHierarchy).Select(label => ScreenBounds(label.rectTransform))
                .Concat(controls.Select(control => ScreenBounds((RectTransform)control.transform))).ToArray();
            for (int a = 0; a < rects.Length; a++) for (int b = a + 1; b < rects.Length; b++) nonoverlap &= !rects[a].Overlaps(rects[b]);
            Check("pause overlay keeps content and controls separate " + aspect, hud.overlay.activeInHierarchy && bounds && nonoverlap,
                "Card=" + card + "; visible title/score/details/reset note/Resume/Restart bounds contained=" + bounds + "; no overlap=" + nonoverlap);
            Color background = Over(hud.overlayCard.color, Over(hud.overlayBackdrop.color, Color.white));
            float contrast = labels.Where(label => label && label.gameObject.activeInHierarchy).Select(label => Contrast(Over(label.color, background), background)).DefaultIfEmpty(0).Min();
            Check("overlay text contrasts with a bright game world " + aspect, hud.overlayBackdrop.color.a >= .8f && contrast >= 4.5f,
                "Worst normal-text contrast over white game scene=" + contrast.ToString("F2") + ":1; backdrop alpha=" + hud.overlayBackdrop.color.a);
            Check("restart explicitly discloses run reset " + aspect, hud.restartHint && hud.restartHint.text.ToUpperInvariant().Contains("RESET") &&
                hud.restartHint.text.ToUpperInvariant().Contains("RUN"), hud.restartHint ? hud.restartHint.text : "Missing reset note");
        }

        static void SetAcceptanceResolution(int width, int height)
        {
            // Runtime diagnostics cannot directly reference the separate editor assembly.
            var builder = Type.GetType("RoadGunner.Editor.PrototypeBuilder, Assembly-CSharp-Editor");
            var method = builder?.GetMethod("SetPortraitGameView", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("Missing editor Game View size helper.");
            method.Invoke(null, new object[] { width, height });
        }

        static Rect ScreenBounds(RectTransform rect)
        {
            if (!rect) return default;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
            foreach (var corner in corners)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static bool Inside(Rect inner, Rect outer) => inner.width > 0 && inner.height > 0 &&
            inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
        static Color Over(Color foreground, Color background) => new Color(
            foreground.r * foreground.a + background.r * (1 - foreground.a),
            foreground.g * foreground.a + background.g * (1 - foreground.a),
            foreground.b * foreground.a + background.b * (1 - foreground.a), 1);
        static float Luminance(Color color)
        {
            var linear = color.linear;
            return .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
        }
        static float Contrast(Color a, Color b) => (Mathf.Max(Luminance(a), Luminance(b)) + .05f) /
            (Mathf.Min(Luminance(a), Luminance(b)) + .05f);
    }
}
#endif
